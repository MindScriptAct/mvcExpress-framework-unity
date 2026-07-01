// Type-based (non-generic) binding API for MvcCommandProcessor.
// Used by ModuleInitializer when binding commands from Inspector registries or [Bind] attributes,
// where the command and message types are known only as System.Type objects at runtime.
// The generic BindCommand<TCommand,TMessage,...> overloads (Params00-Params12) are more efficient
// for user code, but cannot be called when types are determined at runtime.
// This file closes the correct generic overload once via reflection, caches the resulting
// delegate, and invokes the cached delegate on every subsequent call - keeping reflection
// truly confined to the first bind of each (commandType, messageType) pair.
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Interfaces;
using mvcExpress.Internal.Messaging;
using mvcExpress.Logging;
using System;
using System.Collections.Concurrent;
using UnityEngine;

namespace mvcExpress.Internal.Commands
{
    public sealed partial class MvcCommandProcessor
    {
        #region Type-based Binding API (for serialized/registry binding)

        /// <summary>
        /// Type-based bind command API for serialized binding (inspector/registry).
        /// Routes to the appropriate generic implementation based on command type hierarchy.
        /// </summary>
        /// <param name="commandType">Command type (must inherit from MvcCommandBase)</param>
        /// <param name="messageType">Message type (must implement IMessage or IMessage&lt;T...&gt;)</param>
        /// <param name="poolSize">Pool size for pooled commands (ignored for singletons)</param>
        public void BindCommandByType(Type commandType, Type messageType, uint poolSize)
        {
            if (commandType == null) throw new ArgumentNullException(nameof(commandType));
            if (messageType == null) throw new ArgumentNullException(nameof(messageType));

            // Determine payload arity from IMessage interface
            int payloadArity = GetPayloadArity(messageType);

            // Route to appropriate generic binder based on command type and arity
            if (typeof(MvcAsyncCommandBase).IsAssignableFrom(commandType))
            {
                InvokeAsyncBinder(commandType, messageType, payloadArity, poolSize);
            }
            else
            {
                InvokeSyncBinder(commandType, messageType, payloadArity, poolSize);
            }
        }

        /// <summary>
        /// Type-based unbind command API for serialized binding (inspector/registry).
        /// Routes to the appropriate generic implementation based on command type hierarchy.
        /// </summary>
        /// <param name="commandType">Command type</param>
        /// <param name="messageType">Message type</param>
        public void UnbindCommandByType(Type commandType, Type messageType)
        {
            if (commandType == null) throw new ArgumentNullException(nameof(commandType));
            if (messageType == null) throw new ArgumentNullException(nameof(messageType));

            // Determine payload arity from IMessage interface
            int payloadArity = GetPayloadArity(messageType);

            // Route to appropriate generic unbinder based on command type and arity
            if (typeof(MvcAsyncCommandBase).IsAssignableFrom(commandType))
            {
                InvokeAsyncUnbinder(commandType, messageType, payloadArity);
            }
            else
            {
                InvokeSyncUnbinder(commandType, messageType, payloadArity);
            }
        }

        #endregion

        #region Internal Generic Invokers

        // Caches closed bind delegates keyed by (commandType, messageType).
        // Built once per unique pair; subsequent calls skip GetMethods/MakeGenericMethod entirely.
        private static readonly ConcurrentDictionary<(Type, Type), Action<MvcCommandProcessor, uint>> _bindDelegateCache
            = new ConcurrentDictionary<(Type, Type), Action<MvcCommandProcessor, uint>>();

        // Caches closed unbind delegates keyed by (commandType, messageType).
        private static readonly ConcurrentDictionary<(Type, Type), Action<MvcCommandProcessor>> _unbindDelegateCache
            = new ConcurrentDictionary<(Type, Type), Action<MvcCommandProcessor>>();

        private void InvokeSyncBinder(Type commandType, Type messageType, int arity, uint poolSize)
        {
            var key = (commandType, messageType);
            if (!_bindDelegateCache.TryGetValue(key, out var binder))
            {
                binder = BuildBindDelegate(commandType, messageType, arity);
                _bindDelegateCache[key] = binder;
            }
            binder(this, poolSize);
        }

        // Async commands are detected inside BindCommand via IsAsyncCommandType<TCommand>(),
        // so no separate invocation path is needed - route through the sync binder.
        private void InvokeAsyncBinder(Type commandType, Type messageType, int arity, uint poolSize)
        {
            InvokeSyncBinder(commandType, messageType, arity, poolSize);
        }

        private void InvokeSyncUnbinder(Type commandType, Type messageType, int arity)
        {
            var key = (commandType, messageType);
            if (!_unbindDelegateCache.TryGetValue(key, out var unbinder))
            {
                unbinder = BuildUnbindDelegate(commandType, messageType, arity);
                _unbindDelegateCache[key] = unbinder;
            }
            unbinder(this);
        }

        private void InvokeAsyncUnbinder(Type commandType, Type messageType, int arity)
        {
            InvokeSyncUnbinder(commandType, messageType, arity);
        }

        // Builds the closed bind delegate for a (commandType, messageType) pair.
        // Called at most once per unique pair; result is cached by the caller.
        private static Action<MvcCommandProcessor, uint> BuildBindDelegate(Type commandType, Type messageType, int arity)
        {
            int totalGenericArgs = arity + 2;
            var methods = typeof(MvcCommandProcessor).GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            System.Reflection.MethodInfo method = null;
            foreach (var m in methods)
            {
                if (m.Name == nameof(BindCommand) &&
                    m.IsGenericMethodDefinition &&
                    m.GetGenericArguments().Length == totalGenericArgs &&
                    m.GetParameters().Length == 1 &&
                    m.GetParameters()[0].ParameterType == typeof(uint))
                {
                    method = m;
                    break;
                }
            }

            if (method == null)
                throw new InvalidOperationException(
                    $"Could not find BindCommand overload with {totalGenericArgs} generic args for command '{commandType.FullName}' and message '{messageType.FullName}'");

            var closedMethod = method.MakeGenericMethod(BuildTypeArgs(commandType, messageType, arity));

            return (proc, poolSize) =>
            {
                try { closedMethod.Invoke(proc, new object[] { poolSize }); }
                catch (System.Reflection.TargetInvocationException ex)
                {
                    var inner = ex.InnerException ?? ex;
                    MvcDebug.LogError(
                        $"Failed to bind command '{commandType.FullName}' to message '{messageType.FullName}': {inner.Message}\n{inner.StackTrace}");
                    throw inner;
                }
            };
        }

        // Builds the closed unbind delegate for a (commandType, messageType) pair.
        private static Action<MvcCommandProcessor> BuildUnbindDelegate(Type commandType, Type messageType, int arity)
        {
            int totalGenericArgs = arity + 2;
            var methods = typeof(MvcCommandProcessor).GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            System.Reflection.MethodInfo method = null;
            foreach (var m in methods)
            {
                if (m.Name == nameof(UnbindCommand) &&
                    m.IsGenericMethodDefinition &&
                    m.GetGenericArguments().Length == totalGenericArgs &&
                    m.GetParameters().Length == 0)
                {
                    method = m;
                    break;
                }
            }

            if (method == null)
                throw new InvalidOperationException(
                    $"Could not find UnbindCommand overload with {totalGenericArgs} generic args for command '{commandType.FullName}' and message '{messageType.FullName}'");

            var closedMethod = method.MakeGenericMethod(BuildTypeArgs(commandType, messageType, arity));

            return proc =>
            {
                try { closedMethod.Invoke(proc, null); }
                catch (System.Reflection.TargetInvocationException ex)
                {
                    var inner = ex.InnerException ?? ex;
                    MvcDebug.LogError(
                        $"Failed to unbind command '{commandType.FullName}' from message '{messageType.FullName}': {inner.Message}\n{inner.StackTrace}");
                    throw inner;
                }
            };
        }

        private static Type[] BuildTypeArgs(Type commandType, Type messageType, int arity)
        {
            if (arity == 0)
                return new Type[] { commandType, messageType };

            var payloadTypes = GetMessagePayloadTypes(messageType, arity);
            var typeArgs = new Type[arity + 2];
            typeArgs[0] = commandType;
            typeArgs[1] = messageType;
            for (int i = 0; i < arity; i++)
                typeArgs[i + 2] = payloadTypes[i];
            return typeArgs;
        }

        // Determines the number of generic type parameters in the IMessage<T1,...,TN> interface
        // that the message type implements. Returns 0 for plain IMessage (no payload).
        private static int GetPayloadArity(Type messageType)
        {
            if (messageType.IsGenericTypeDefinition)
                return messageType.GetGenericArguments().Length;

            var ifaces = messageType.GetInterfaces();
            for (int i = 0; i < ifaces.Length; i++)
            {
                var iface = ifaces[i];
                if (!iface.IsGenericType) continue;

                var def = iface.GetGenericTypeDefinition();
                if (def.Namespace != typeof(IMessage).Namespace) continue;

                var name = def.Name;
                if (name.StartsWith("IMessage`", StringComparison.Ordinal))
                {
                    return iface.GetGenericArguments().Length;
                }
            }

            return 0; // IMessage with no payload
        }

        /// <summary>
        /// Extracts the payload type arguments from a message's IMessage&lt;T1,...,TN&gt; interface.
        /// </summary>
        private static Type[] GetMessagePayloadTypes(Type messageType, int expectedArity)
        {
            var ifaces = messageType.GetInterfaces();
            for (int i = 0; i < ifaces.Length; i++)
            {
                var iface = ifaces[i];
                if (!iface.IsGenericType) continue;

                var def = iface.GetGenericTypeDefinition();
                if (def.Namespace != typeof(IMessage).Namespace) continue;

                var name = def.Name;
                if (name.StartsWith("IMessage`", StringComparison.Ordinal))
                {
                    var args = iface.GetGenericArguments();
                    if (args.Length == expectedArity)
                        return args;
                }
            }

            throw new InvalidOperationException(
                $"Message type '{messageType.FullName}' does not implement IMessage with {expectedArity} type argument(s). " +
                $"Command binding requires the message to implement the correct IMessage<T1,...,TN> interface.");
        }

        #endregion
    }
}

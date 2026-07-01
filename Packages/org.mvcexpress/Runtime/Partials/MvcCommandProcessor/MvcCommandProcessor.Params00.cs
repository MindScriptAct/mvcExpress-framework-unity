﻿// Arity-0 partial of MvcCommandProcessor - 0-parameter message/command overloads.
// This file is the canonical template for all arity variants (Params01-Params12).
// Pattern repeated in each variant:
//   - CommandBinding<TCommand,TMessage>      : static storage for sync binding state (tokens, bound flag)
//   - CommandBindingAsync<TCommand,TMessage> : same for async commands
//   - BindCommand / BindCommandAsync         : subscribe the executor lambda to the message bus
//   - ExecuteCommand0 / ExecuteCommandAsync0 : called by the lambda; fetches from pool, executes
//   - ExecuteCommandDirect0 / ...Async0      : pool-aware execution core (also used by Run())
//   - UnbindCommand / UnbindCommandAsync     : unsubscribe from bus, mark as unbound
//   - Run / RunAsync                         : execute a command directly without a message
using mvcExpress.Internal.Messaging;
using mvcExpress.Internal.Utilities;
using mvcExpress.Logging;
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace mvcExpress.Internal.Commands
{
    public sealed partial class MvcCommandProcessor
    {
        #region Generic Command Binding (0 Params)

        // ==================== STATIC GENERIC STORAGE (0 PARAMS) ====================

        /// <summary>
        /// Static storage for (TCommand, TMessage) binding with 0 parameters.
        /// Stores subscription tokens and bound state. Pool is centralized in CommandPool.
        /// </summary>
        private static class CommandBinding<TCommand, TMessage>
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage
        {
            internal static SubscriptionToken[] Tokens = new SubscriptionToken[4];
            internal static bool[] IsBound = new bool[4];
            internal static readonly object Lock = new object();

            internal static void EnsureCapacity(int instanceId)
            {
                if (instanceId >= Tokens.Length)
                {
                    lock (Lock)
                    {
                        if (instanceId >= Tokens.Length)
                        {
                            int newSize = Math.Max(instanceId + 1, Tokens.Length * 2);
                            Array.Resize(ref Tokens, newSize);
                            Array.Resize(ref IsBound, newSize);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Static storage for async (TCommand, TMessage) binding with 0 parameters.
        /// </summary>
        private static class CommandBindingAsync<TCommand, TMessage>
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage
        {
            internal static SubscriptionToken[] Tokens = new SubscriptionToken[4];
            internal static bool[] IsBound = new bool[4];
            internal static readonly object Lock = new object();

            internal static void EnsureCapacity(int instanceId)
            {
                if (instanceId >= Tokens.Length)
                {
                    lock (Lock)
                    {
                        if (instanceId >= Tokens.Length)
                        {
                            int newSize = Math.Max(instanceId + 1, Tokens.Length * 2);
                            Array.Resize(ref Tokens, newSize);
                            Array.Resize(ref IsBound, newSize);
                        }
                    }
                }
            }
        }

        // ==================== BIND METHODS (0 PARAMS) ====================

        /// <summary>
        /// GENERIC BIND - Binds a sync command to a parameter-less message.
        /// Command executor is registered directly as a message handler - zero overhead!
        /// </summary>
        private static bool IsAsyncCommandType<TCommand>()
        {
            return typeof(MvcAsyncCommandBase).IsAssignableFrom(typeof(TCommand));
        }

        public void BindCommand<TCommand, TMessage>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage
        {
            if (IsAsyncCommandType<TCommand>())
            {
                BindCommandAsync<TCommand, TMessage>(poolSize);
                return;
            }

            CommandBinding<TCommand, TMessage>.EnsureCapacity(_instanceId);

            // Check if already bound
            if (CommandBinding<TCommand, TMessage>.IsBound[_instanceId])
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                MvcDebug.LogWarning($"Command '{typeof(TCommand).Name}' already bound to message '{typeof(TMessage).Name}' - ignoring duplicate bind");
#endif
                return;
            }

            // Configure pool if poolSize > 0, otherwise use default (0)
            if (poolSize != 0)
            {
                CreatePool<TCommand>(poolSize);
            }

            // THE MAGIC: Subscribe command executor directly to message bus!
            // When TMessage is published, our executor runs alongside other message handlers
            Action commandExecutor = () => ExecuteCommand0<TCommand, TMessage>();

            var token = _messageBus.Subscribe<TMessage>(commandExecutor);

            // Store token for unbind
            CommandBinding<TCommand, TMessage>.Tokens[_instanceId] = token;
            CommandBinding<TCommand, TMessage>.IsBound[_instanceId] = true;

            var capturedToken0 = token;
            var capturedId0 = _instanceId;
            TrackUnbindAction(() =>
            {
                _messageBus.Unsubscribe<TMessage>(capturedToken0);
                CommandBinding<TCommand, TMessage>.IsBound[capturedId0] = false;
            });

            // Maintain binding introspection index (setup-time only)
            TrackBinding(typeof(TMessage), typeof(TCommand), isAsync: false);

        }

        /// <summary>
        /// GENERIC BIND ASYNC - Binds an async command to a parameter-less message.
        /// </summary>
        public void BindCommandAsync<TCommand, TMessage>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage
        {
            CommandBindingAsync<TCommand, TMessage>.EnsureCapacity(_instanceId);

            if (CommandBindingAsync<TCommand, TMessage>.IsBound[_instanceId])
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                MvcDebug.LogWarning($"Async command '{typeof(TCommand).Name}' already bound to message '{typeof(TMessage).Name}' - ignoring duplicate bind");
#endif
                return;
            }

            // Configure pool if poolSize > 0, otherwise use default (0)
            if (poolSize != 0)
            {
                CreatePool<TCommand>(poolSize);
            }

            Action commandExecutor = () => ExecuteCommandAsync0<TCommand, TMessage>();

            var token = _messageBus.Subscribe<TMessage>(commandExecutor);

            CommandBindingAsync<TCommand, TMessage>.Tokens[_instanceId] = token;
            CommandBindingAsync<TCommand, TMessage>.IsBound[_instanceId] = true;

            var capturedToken0Async = token;
            var capturedId0Async = _instanceId;
            TrackUnbindAction(() =>
            {
                _messageBus.Unsubscribe<TMessage>(capturedToken0Async);
                CommandBindingAsync<TCommand, TMessage>.IsBound[capturedId0Async] = false;
            });

            // Maintain binding introspection index (setup-time only)
            TrackBinding(typeof(TMessage), typeof(TCommand), isAsync: true);

        }

        // ==================== EXECUTE METHODS (0 PARAMS) ====================

        /// <summary>
        /// Execute sync command (0 params).
        /// Called directly by message bus when TMessage is published.
        /// </summary>
        private void ExecuteCommand0<TCommand, TMessage>()
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage
        {
            MvcLogInternal.LogCommandExecuting<TMessage, TCommand>(_moduleContext);
            var pool = GetOrCreatePool<TCommand>(0);
            ExecuteCommandDirect0<TCommand>(pool);
        }

        /// <summary>
        /// Execute async command (0 params).
        /// Called directly by message bus when TMessage is published.
        /// Fire-and-forget pattern (async void) - errors handled internally.
        /// </summary>
        private async void ExecuteCommandAsync0<TCommand, TMessage>()
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage
        {
            MvcLogInternal.LogCommandExecuting<TMessage, TCommand>(_moduleContext);
            var pool = GetOrCreatePool<TCommand>(0);
            await ExecuteCommandDirectAsync0<TCommand>(pool);
        }

        /// <summary>
        /// UNIFIED: Execute sync command directly (0 params) - uses pooling!
        /// Reuses the same execution logic as message-based commands.
        /// </summary>
        private void ExecuteCommandDirect0<TCommand>(BoundedObjectPool<MvcCommandBase> pool)
            where TCommand : MvcCommandBase, new()
        {
            var cmd = (TCommand)pool.Get();
            try
            {
                BeginCommandExecution();
                cmd.Initialize(_moduleContext, _container, _messageBus, this);
                var typedCmd = cmd as Command;
                if (typedCmd != null)
                {
                    typedCmd.Execute();
                }
                else
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    MvcDebug.LogError($"Command '{typeof(TCommand).Name}' does not inherit from Command - cannot execute");
#endif
                }
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                MvcDebug.LogError($"Command '{typeof(TCommand).Name}' execution failed: {ex}");
#endif
            }
            finally
            {
                ReturnToPoolGeneric(cmd, pool);
            }
        }

        /// <summary>
        /// UNIFIED: Execute async command directly (0 params) - uses pooling!
        /// </summary>
        private async Task ExecuteCommandDirectAsync0<TCommand>(BoundedObjectPool<MvcCommandBase> pool)
            where TCommand : MvcCommandBase, new()
        {
            var cmd = (TCommand)pool.Get();

            try
            {
                BeginCommandExecution();

                cmd.Initialize(_moduleContext, _container, _messageBus, this);

                var typedCmd = cmd as CommandAsync;

                if (typedCmd != null)
                {
                    await typedCmd.ExecuteAsync();
                }
                else
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    MvcDebug.LogError($"Command '{typeof(TCommand).Name}' does not inherit from CommandAsync - cannot execute");
#endif
                }
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                MvcDebug.LogError($"Async command '{typeof(TCommand).Name}' execution failed: {ex}");
#endif
            }
            finally
            {
                ReturnToPoolGeneric(cmd, pool);
            }
        }

        // ==================== UNBIND METHODS (0 PARAMS) ====================

        /// <summary>
        /// Unbind sync command from message.
        /// Unsubscribes from message bus. Pool is shared and not cleared.
        /// </summary>
        public void UnbindCommand<TCommand, TMessage>()
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage
        {
            if (IsAsyncCommandType<TCommand>())
            {
                UnbindCommandAsync<TCommand, TMessage>();
                return;
            }

            if (_instanceId >= CommandBinding<TCommand, TMessage>.IsBound.Length ||
                !CommandBinding<TCommand, TMessage>.IsBound[_instanceId])
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                MvcDebug.LogWarning($"Command '{typeof(TCommand).Name}' not bound to '{typeof(TMessage).Name}' - nothing to unbind");
#endif
                return;
            }

            // Unsubscribe from message bus
            var token = CommandBinding<TCommand, TMessage>.Tokens[_instanceId];

            _messageBus.Unsubscribe<TMessage>(token);

            // Mark as unbound (pool is shared, don't clear it)
            CommandBinding<TCommand, TMessage>.IsBound[_instanceId] = false;

            // Maintain binding introspection index (setup-time only)
            UntrackBinding(typeof(TMessage), typeof(TCommand), isAsync: false);

            MvcLogInternal.LogCommandUnbound(typeof(TMessage).Name, typeof(TCommand).Name, _moduleContext);
        }

        /// <summary>
        /// Unbind async command from message.
        /// </summary>
        public void UnbindCommandAsync<TCommand, TMessage>()
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage
        {
            if (_instanceId >= CommandBindingAsync<TCommand, TMessage>.IsBound.Length ||
                !CommandBindingAsync<TCommand, TMessage>.IsBound[_instanceId])
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                MvcDebug.LogWarning($"Async command '{typeof(TCommand).Name}' not bound to '{typeof(TMessage).Name}' - nothing to unbind");
#endif
                return;
            }

            var token = CommandBindingAsync<TCommand, TMessage>.Tokens[_instanceId];

            _messageBus.Unsubscribe<TMessage>(token);

            // Mark as unbound (pool is shared, don't clear it)
            CommandBindingAsync<TCommand, TMessage>.IsBound[_instanceId] = false;

            // Maintain binding introspection index (setup-time only)
            UntrackBinding(typeof(TMessage), typeof(TCommand), isAsync: true);

            MvcLogInternal.LogCommandUnbound(typeof(TMessage).Name, typeof(TCommand).Name, _moduleContext);
        }

        // ==================== RUN METHODS (0 PARAMS) ====================

        /// <summary>
        /// Run a command directly without message (sync, 0 params).
        /// Uses centralized pool - creates with default size (0) if not configured.
        /// </summary>
        public void Run<TCommand>() where TCommand : Command, new()
        {
            var pool = GetOrCreatePool<TCommand>(0);
            ExecuteCommandDirect0<TCommand>(pool);
        }

        /// <summary>
        /// Run a command directly without message (async, 0 params).
        /// Uses centralized pool - creates with default size (0) if not configured.
        /// </summary>
        public async Task RunAsync<TCommand>() where TCommand : CommandAsync, new()
        {
            var pool = GetOrCreatePool<TCommand>(0);
            await ExecuteCommandDirectAsync0<TCommand>(pool);
        }

        #endregion
    }
}

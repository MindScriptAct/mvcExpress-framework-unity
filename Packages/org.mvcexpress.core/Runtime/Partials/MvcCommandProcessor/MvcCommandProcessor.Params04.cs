// Arity variant 4 of MvcCommandProcessor - see MvcCommandProcessor.Params00.cs for the template pattern.
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
        #region Generic Command Binding (4 Params)

        // ==================== STATIC GENERIC STORAGE (4 PARAMS) ====================

        private static class CommandBinding<TCommand, TMessage, T1, T2, T3, T4>
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4>
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

        private static class CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4>
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4>
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

        // ==================== BIND METHODS (4 PARAMS) ====================

        /// <summary>
        /// Binds a sync command to a message with four payload values. The command
        /// executor is registered directly as a message handler - zero overhead beyond the delegate call itself.
        /// </summary>
        public void BindCommand<TCommand, TMessage, T1, T2, T3, T4>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4>
        {
            if (IsAsyncCommandType<TCommand>())
            {
                BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4>(poolSize);
                return;
            }

            ValidateCommandArity<TCommand, Command<T1, T2, T3, T4>>();

            CommandBinding<TCommand, TMessage, T1, T2, T3, T4>.EnsureCapacity(_instanceId);

            if (CommandBinding<TCommand, TMessage, T1, T2, T3, T4>.IsBound[_instanceId])
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                MvcDebug.LogWarning($"Command '{typeof(TCommand).Name}' already bound - ignoring duplicate");
#endif
                return;
            }

            if (poolSize != 0)
            {
                CreatePool<TCommand>(poolSize);
            }

            Action<T1, T2, T3, T4> commandExecutor = (p1, p2, p3, p4) => ExecuteCommand4<TCommand, TMessage, T1, T2, T3, T4>(p1, p2, p3, p4);
            var token = _messageBus.Subscribe<TMessage, T1, T2, T3, T4>(commandExecutor);

            CommandBinding<TCommand, TMessage, T1, T2, T3, T4>.Tokens[_instanceId] = token;
            CommandBinding<TCommand, TMessage, T1, T2, T3, T4>.IsBound[_instanceId] = true;

            var capturedToken4 = token;
            var capturedId4 = _instanceId;
            TrackUnbindAction(() =>
            {
                _messageBus.Unsubscribe<TMessage, T1, T2, T3, T4>(capturedToken4);
                CommandBinding<TCommand, TMessage, T1, T2, T3, T4>.IsBound[capturedId4] = false;
            });

            TrackBinding(typeof(TMessage), typeof(TCommand), isAsync: false);

        }

        /// <summary>
        /// Binds an async command to a message with four payload values.
        /// </summary>
        public void BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4>
        {
            ValidateCommandArity<TCommand, CommandAsync<T1, T2, T3, T4>>();

            CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4>.EnsureCapacity(_instanceId);

            if (CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4>.IsBound[_instanceId])
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                MvcDebug.LogWarning($"Async command '{typeof(TCommand).Name}' already bound - ignoring duplicate");
#endif
                return;
            }

            if (poolSize != 0)
            {
                CreatePool<TCommand>(poolSize);
            }

            Action<T1, T2, T3, T4> commandExecutor = (p1, p2, p3, p4) => ExecuteCommandAsync4<TCommand, TMessage, T1, T2, T3, T4>(p1, p2, p3, p4);
            var token = _messageBus.Subscribe<TMessage, T1, T2, T3, T4>(commandExecutor);

            CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4>.Tokens[_instanceId] = token;
            CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4>.IsBound[_instanceId] = true;

            var capturedToken4Async = token;
            var capturedId4Async = _instanceId;
            TrackUnbindAction(() =>
            {
                _messageBus.Unsubscribe<TMessage, T1, T2, T3, T4>(capturedToken4Async);
                CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4>.IsBound[capturedId4Async] = false;
            });

            TrackBinding(typeof(TMessage), typeof(TCommand), isAsync: true);

        }

        // ==================== EXECUTE METHODS (4 PARAMS) ====================

        private void ExecuteCommand4<TCommand, TMessage, T1, T2, T3, T4>(T1 p1, T2 p2, T3 p3, T4 p4)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4>
        {
            MvcLogInternal.LogCommandExecuting<TMessage, TCommand>(_moduleContext);
            var pool = GetOrCreatePool<TCommand>(0);
            ExecuteCommandDirect4<TCommand, T1, T2, T3, T4>(pool, p1, p2, p3, p4);
        }

        private async void ExecuteCommandAsync4<TCommand, TMessage, T1, T2, T3, T4>(T1 p1, T2 p2, T3 p3, T4 p4)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4>
        {
            MvcLogInternal.LogCommandExecuting<TMessage, TCommand>(_moduleContext);
            var pool = GetOrCreatePool<TCommand>(0);
            await ExecuteCommandDirectAsync4<TCommand, T1, T2, T3, T4>(pool, p1, p2, p3, p4);
        }

        private void ExecuteCommandDirect4<TCommand, T1, T2, T3, T4>(BoundedObjectPool<MvcCommandBase> pool, T1 p1, T2 p2, T3 p3, T4 p4)
            where TCommand : MvcCommandBase, new()
        {
            var cmd = (TCommand)pool.Get();

            try
            {
                BeginCommandExecution();
                cmd.Initialize(_moduleContext, _container, _messageBus, this);

                var typedCmd = cmd as Command<T1, T2, T3, T4>;
                typedCmd?.Execute(p1, p2, p3, p4);
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                MvcDebug.LogError($"Command '{typeof(TCommand).Name}' execution failed: {ex}");
#endif
                RaiseCommandFault(typeof(TCommand), ex);
            }
            finally
            {
                ReturnToPoolGeneric(cmd, pool);
            }
        }

        private async Task ExecuteCommandDirectAsync4<TCommand, T1, T2, T3, T4>(BoundedObjectPool<MvcCommandBase> pool, T1 p1, T2 p2, T3 p3, T4 p4)
            where TCommand : MvcCommandBase, new()
        {
            var cmd = (TCommand)pool.Get();

            try
            {
                BeginCommandExecution();
                cmd.Initialize(_moduleContext, _container, _messageBus, this);

                var typedCmd = cmd as CommandAsync<T1, T2, T3, T4>;
                if (typedCmd != null)
                    await typedCmd.ExecuteAsync(p1, p2, p3, p4);
            }
            catch (OperationCanceledException) when (cmd.CancelTokenInternal.IsCancellationRequested)
            {
                // Expected outcome of the module tearing down mid-operation - not a failure.
                // The `when` guard scopes this to cancellation actually caused by this command's
                // own (module-owned) token; an unrelated OperationCanceledException (e.g. an
                // author's own separate timeout token) falls through to the catch below instead,
                // so it stays visible as an error like any other unexpected failure.
                MvcLogInternal.LogCommandCancelled<TCommand>(_moduleContext);
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                MvcDebug.LogError($"Async command '{typeof(TCommand).Name}' execution failed: {ex}");
#endif
                RaiseCommandFault(typeof(TCommand), ex);
            }
            finally
            {
                ReturnToPoolGeneric(cmd, pool);
            }
        }

        // ==================== UNBIND METHODS (4 PARAMS) ====================

        /// <summary>
        /// Unbinds a sync command from a message with four payload values.
        /// Unsubscribes from the message bus; the command pool is shared and not cleared.
        /// </summary>
        public void UnbindGeneric<TCommand, TMessage, T1, T2, T3, T4>()
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4>
        {
            if (_instanceId >= CommandBinding<TCommand, TMessage, T1, T2, T3, T4>.IsBound.Length ||
                !CommandBinding<TCommand, TMessage, T1, T2, T3, T4>.IsBound[_instanceId])
            {
                return;
            }

            var token = CommandBinding<TCommand, TMessage, T1, T2, T3, T4>.Tokens[_instanceId];
            _messageBus.Unsubscribe<TMessage, T1, T2, T3, T4>(token);

            CommandBinding<TCommand, TMessage, T1, T2, T3, T4>.IsBound[_instanceId] = false;
            UntrackBinding(typeof(TMessage), typeof(TCommand));

            MvcLogInternal.LogCommandUnbound(typeof(TMessage).Name, typeof(TCommand).Name, _moduleContext);
        }

        /// <summary>
        /// Unbinds an async command from a message with four payload values.
        /// </summary>
        public void UnbindAsyncGeneric<TCommand, TMessage, T1, T2, T3, T4>()
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4>
        {
            if (_instanceId >= CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4>.IsBound.Length ||
                !CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4>.IsBound[_instanceId])
            {
                return;
            }

            var token = CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4>.Tokens[_instanceId];
            _messageBus.Unsubscribe<TMessage, T1, T2, T3, T4>(token);

            CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4>.IsBound[_instanceId] = false;
            UntrackBinding(typeof(TMessage), typeof(TCommand));

            MvcLogInternal.LogCommandUnbound(typeof(TMessage).Name, typeof(TCommand).Name, _moduleContext);
        }

        // ==================== RUN METHODS (4 PARAMS) ====================

        /// <summary>
        /// Runs a command directly without a message (four payload values, sync).
        /// Uses the centralized pool - creates with the default size (0) if not configured.
        /// </summary>
        public void Run<TCommand, T1, T2, T3, T4>(T1 p1, T2 p2, T3 p3, T4 p4) where TCommand : Command<T1, T2, T3, T4>, new()
        {
            var pool = GetOrCreatePool<TCommand>(0);
            ExecuteCommandDirect4<TCommand, T1, T2, T3, T4>(pool, p1, p2, p3, p4);
        }

        /// <summary>
        /// Runs a command directly without a message (four payload values, async).
        /// Uses the centralized pool - creates with the default size (0) if not configured.
        /// </summary>
        public async Task RunAsync<TCommand, T1, T2, T3, T4>(T1 p1, T2 p2, T3 p3, T4 p4) where TCommand : CommandAsync<T1, T2, T3, T4>, new()
        {
            var pool = GetOrCreatePool<TCommand>(0);
            await ExecuteCommandDirectAsync4<TCommand, T1, T2, T3, T4>(pool, p1, p2, p3, p4);
        }

        #endregion
    }
}

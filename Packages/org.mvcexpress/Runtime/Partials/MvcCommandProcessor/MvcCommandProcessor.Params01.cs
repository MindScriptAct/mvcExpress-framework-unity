﻿// Arity variant 1 of MvcCommandProcessor - see MvcCommandProcessor.Params00.cs for the template pattern.
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
        #region Generic Command Binding (1 Params)

        // ==================== STATIC GENERIC STORAGE (1 PARAM) ====================

        private static class CommandBinding<TCommand, TMessage, T1>
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1>
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

        private static class CommandBindingAsync<TCommand, TMessage, T1>
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1>
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

        // ==================== BIND METHODS (1 PARAM) ====================

        public void BindCommand<TCommand, TMessage, T1>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1>
        {
            if (IsAsyncCommandType<TCommand>())
            {
                BindCommandAsync<TCommand, TMessage, T1>(poolSize);
                return;
            }

            CommandBinding<TCommand, TMessage, T1>.EnsureCapacity(_instanceId);

            if (CommandBinding<TCommand, TMessage, T1>.IsBound[_instanceId])
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                MvcDebug.LogWarning($"Command '{typeof(TCommand).Name}' already bound - ignoring duplicate");
#endif
                return;
            }

            // Configure pool if poolSize > 0
            if (poolSize != 0)
            {
                CreatePool<TCommand>(poolSize);
            }

            Action<T1> commandExecutor = (p1) => ExecuteCommand1<TCommand, TMessage, T1>(p1);
            var token = _messageBus.Subscribe<TMessage, T1>(commandExecutor);

            CommandBinding<TCommand, TMessage, T1>.Tokens[_instanceId] = token;
            CommandBinding<TCommand, TMessage, T1>.IsBound[_instanceId] = true;

            var capturedToken1 = token;
            var capturedId1 = _instanceId;
            TrackUnbindAction(() =>
            {
                _messageBus.Unsubscribe<TMessage, T1>(capturedToken1);
                CommandBinding<TCommand, TMessage, T1>.IsBound[capturedId1] = false;
            });

            TrackBinding(typeof(TMessage), typeof(TCommand), isAsync: false);

        }

        public void BindCommandAsync<TCommand, TMessage, T1>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1>
        {
            CommandBindingAsync<TCommand, TMessage, T1>.EnsureCapacity(_instanceId);

            if (CommandBindingAsync<TCommand, TMessage, T1>.IsBound[_instanceId])
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                MvcDebug.LogWarning($"Async command '{typeof(TCommand).Name}' already bound - ignoring duplicate");
#endif
                return;
            }

            // Configure pool if poolSize > 0
            if (poolSize != 0)
            {
                CreatePool<TCommand>(poolSize);
            }

            Action<T1> commandExecutor = (p1) => ExecuteCommandAsync1<TCommand, TMessage, T1>(p1);
            var token = _messageBus.Subscribe<TMessage, T1>(commandExecutor);

            CommandBindingAsync<TCommand, TMessage, T1>.Tokens[_instanceId] = token;
            CommandBindingAsync<TCommand, TMessage, T1>.IsBound[_instanceId] = true;

            var capturedToken1Async = token;
            var capturedId1Async = _instanceId;
            TrackUnbindAction(() =>
            {
                _messageBus.Unsubscribe<TMessage, T1>(capturedToken1Async);
                CommandBindingAsync<TCommand, TMessage, T1>.IsBound[capturedId1Async] = false;
            });

            TrackBinding(typeof(TMessage), typeof(TCommand), isAsync: true);

        }

        // ==================== EXECUTE METHODS (1 PARAM) ====================

        private void ExecuteCommand1<TCommand, TMessage, T1>(T1 p1)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1>
        {
            MvcLogInternal.LogCommandExecuting<TMessage, TCommand>(_moduleContext);
            var pool = GetOrCreatePool<TCommand>(0);
            ExecuteCommandDirect1<TCommand, T1>(pool, p1);
        }

        private async void ExecuteCommandAsync1<TCommand, TMessage, T1>(T1 p1)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1>
        {
            MvcLogInternal.LogCommandExecuting<TMessage, TCommand>(_moduleContext);
            var pool = GetOrCreatePool<TCommand>(0);
            await ExecuteCommandDirectAsync1<TCommand, T1>(pool, p1);
        }

        private void ExecuteCommandDirect1<TCommand, T1>(BoundedObjectPool<MvcCommandBase> pool, T1 p1)
            where TCommand : MvcCommandBase, new()
        {
            var cmd = (TCommand)pool.Get();

            try
            {
                BeginCommandExecution();
                cmd.Initialize(_moduleContext, _container, _messageBus, this);

                var typedCmd = cmd as Command<T1>;
                typedCmd?.Execute(p1);
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

        private async Task ExecuteCommandDirectAsync1<TCommand, T1>(BoundedObjectPool<MvcCommandBase> pool, T1 p1)
            where TCommand : MvcCommandBase, new()
        {
            var cmd = (TCommand)pool.Get();

            try
            {
                BeginCommandExecution();
                cmd.Initialize(_moduleContext, _container, _messageBus, this);

                var typedCmd = cmd as CommandAsync<T1>;
                if (typedCmd != null)
                    await typedCmd.ExecuteAsync(p1);
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

        // ==================== UNBIND METHODS (1 PARAM) ====================

        public void UnbindGeneric<TCommand, TMessage, T1>()
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1>
        {
            if (_instanceId >= CommandBinding<TCommand, TMessage, T1>.IsBound.Length ||
                !CommandBinding<TCommand, TMessage, T1>.IsBound[_instanceId])
            {
                return;
            }

            var token = CommandBinding<TCommand, TMessage, T1>.Tokens[_instanceId];
            _messageBus.Unsubscribe<TMessage, T1>(token);

            CommandBinding<TCommand, TMessage, T1>.IsBound[_instanceId] = false;
            UntrackBinding(typeof(TMessage), typeof(TCommand));

            MvcLogInternal.LogCommandUnbound(typeof(TMessage).Name, typeof(TCommand).Name, _moduleContext);
        }

        public void UnbindAsyncGeneric<TCommand, TMessage, T1>()
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1>
        {
            if (_instanceId >= CommandBindingAsync<TCommand, TMessage, T1>.IsBound.Length ||
                !CommandBindingAsync<TCommand, TMessage, T1>.IsBound[_instanceId])
            {
                return;
            }

            var token = CommandBindingAsync<TCommand, TMessage, T1>.Tokens[_instanceId];
            _messageBus.Unsubscribe<TMessage, T1>(token);

            CommandBindingAsync<TCommand, TMessage, T1>.IsBound[_instanceId] = false;
            UntrackBinding(typeof(TMessage), typeof(TCommand));

            MvcLogInternal.LogCommandUnbound(typeof(TMessage).Name, typeof(TCommand).Name, _moduleContext);
        }

        // ==================== RUN METHODS (1 PARAM) ====================

        public void Run<TCommand, T1>(T1 p1) where TCommand : Command<T1>, new()
        {
            var pool = GetOrCreatePool<TCommand>(0);
            ExecuteCommandDirect1<TCommand, T1>(pool, p1);
        }

        public async Task RunAsync<TCommand, T1>(T1 p1) where TCommand : CommandAsync<T1>, new()
        {
            var pool = GetOrCreatePool<TCommand>(0);
            await ExecuteCommandDirectAsync1<TCommand, T1>(pool, p1);
        }

        #endregion
    }
}

﻿// Arity variant 5 of MvcCommandProcessor - see MvcCommandProcessor.Params00.cs for the template pattern.
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
        #region Generic Command Binding (5 Params)

        // ==================== STATIC GENERIC STORAGE (5 PARAMS) ====================

        private static class CommandBinding<TCommand, TMessage, T1, T2, T3, T4, T5>
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5>
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

        private static class CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4, T5>
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5>
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

        // ==================== BIND METHODS (5 PARAMS) ====================

        public void BindCommand<TCommand, TMessage, T1, T2, T3, T4, T5>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            if (IsAsyncCommandType<TCommand>())
            {
                BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4, T5>(poolSize);
                return;
            }

            CommandBinding<TCommand, TMessage, T1, T2, T3, T4, T5>.EnsureCapacity(_instanceId);

            if (CommandBinding<TCommand, TMessage, T1, T2, T3, T4, T5>.IsBound[_instanceId])
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

            Action<T1, T2, T3, T4, T5> commandExecutor = (p1, p2, p3, p4, p5) => ExecuteCommand5<TCommand, TMessage, T1, T2, T3, T4, T5>(p1, p2, p3, p4, p5);
            var token = _messageBus.Subscribe<TMessage, T1, T2, T3, T4, T5>(commandExecutor);

            CommandBinding<TCommand, TMessage, T1, T2, T3, T4, T5>.Tokens[_instanceId] = token;
            CommandBinding<TCommand, TMessage, T1, T2, T3, T4, T5>.IsBound[_instanceId] = true;

            var capturedToken5 = token;
            var capturedId5 = _instanceId;
            TrackUnbindAction(() =>
            {
                _messageBus.Unsubscribe<TMessage, T1, T2, T3, T4, T5>(capturedToken5);
                CommandBinding<TCommand, TMessage, T1, T2, T3, T4, T5>.IsBound[capturedId5] = false;
            });

            TrackBinding(typeof(TMessage), typeof(TCommand), isAsync: false);

        }

        public void BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4, T5>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4, T5>.EnsureCapacity(_instanceId);

            if (CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4, T5>.IsBound[_instanceId])
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

            Action<T1, T2, T3, T4, T5> commandExecutor = (p1, p2, p3, p4, p5) => ExecuteCommandAsync5<TCommand, TMessage, T1, T2, T3, T4, T5>(p1, p2, p3, p4, p5);
            var token = _messageBus.Subscribe<TMessage, T1, T2, T3, T4, T5>(commandExecutor);

            CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4, T5>.Tokens[_instanceId] = token;
            CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4, T5>.IsBound[_instanceId] = true;

            var capturedToken5Async = token;
            var capturedId5Async = _instanceId;
            TrackUnbindAction(() =>
            {
                _messageBus.Unsubscribe<TMessage, T1, T2, T3, T4, T5>(capturedToken5Async);
                CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4, T5>.IsBound[capturedId5Async] = false;
            });

            TrackBinding(typeof(TMessage), typeof(TCommand), isAsync: true);

        }

        // ==================== EXECUTE METHODS (5 PARAMS) ====================

        private void ExecuteCommand5<TCommand, TMessage, T1, T2, T3, T4, T5>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            MvcLogInternal.LogCommandExecuting<TMessage, TCommand>(_moduleContext);
            var pool = GetOrCreatePool<TCommand>(0);
            ExecuteCommandDirect5<TCommand, T1, T2, T3, T4, T5>(pool, p1, p2, p3, p4, p5);
        }

        private async void ExecuteCommandAsync5<TCommand, TMessage, T1, T2, T3, T4, T5>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            MvcLogInternal.LogCommandExecuting<TMessage, TCommand>(_moduleContext);
            var pool = GetOrCreatePool<TCommand>(0);
            await ExecuteCommandDirectAsync5<TCommand, T1, T2, T3, T4, T5>(pool, p1, p2, p3, p4, p5);
        }

        private void ExecuteCommandDirect5<TCommand, T1, T2, T3, T4, T5>(BoundedObjectPool<MvcCommandBase> pool, T1 p1, T2 p2, T3 p3, T4 p4, T5 p5)
            where TCommand : MvcCommandBase, new()
        {
            var cmd = (TCommand)pool.Get();

            try
            {
                BeginCommandExecution();
                cmd.Initialize(_moduleContext, _container, _messageBus, this);

                var typedCmd = cmd as Command<T1, T2, T3, T4, T5>;
                typedCmd?.Execute(p1, p2, p3, p4, p5);
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

        private async Task ExecuteCommandDirectAsync5<TCommand, T1, T2, T3, T4, T5>(BoundedObjectPool<MvcCommandBase> pool, T1 p1, T2 p2, T3 p3, T4 p4, T5 p5)
            where TCommand : MvcCommandBase, new()
        {
            var cmd = (TCommand)pool.Get();

            try
            {
                BeginCommandExecution();
                cmd.Initialize(_moduleContext, _container, _messageBus, this);

                var typedCmd = cmd as CommandAsync<T1, T2, T3, T4, T5>;
                if (typedCmd != null)
                    await typedCmd.ExecuteAsync(p1, p2, p3, p4, p5);
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

        // ==================== UNBIND METHODS (5 PARAMS) ====================

        public void UnbindGeneric<TCommand, TMessage, T1, T2, T3, T4, T5>()
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            if (_instanceId >= CommandBinding<TCommand, TMessage, T1, T2, T3, T4, T5>.IsBound.Length ||
                !CommandBinding<TCommand, TMessage, T1, T2, T3, T4, T5>.IsBound[_instanceId])
            {
                return;
            }

            var token = CommandBinding<TCommand, TMessage, T1, T2, T3, T4, T5>.Tokens[_instanceId];
            _messageBus.Unsubscribe<TMessage, T1, T2, T3, T4, T5>(token);

            CommandBinding<TCommand, TMessage, T1, T2, T3, T4, T5>.IsBound[_instanceId] = false;
            UntrackBinding(typeof(TMessage), typeof(TCommand));

            MvcLogInternal.LogCommandUnbound(typeof(TMessage).Name, typeof(TCommand).Name, _moduleContext);
        }

        public void UnbindAsyncGeneric<TCommand, TMessage, T1, T2, T3, T4, T5>()
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            if (_instanceId >= CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4, T5>.IsBound.Length ||
                !CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4, T5>.IsBound[_instanceId])
            {
                return;
            }

            var token = CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4, T5>.Tokens[_instanceId];
            _messageBus.Unsubscribe<TMessage, T1, T2, T3, T4, T5>(token);

            CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4, T5>.IsBound[_instanceId] = false;
            UntrackBinding(typeof(TMessage), typeof(TCommand));

            MvcLogInternal.LogCommandUnbound(typeof(TMessage).Name, typeof(TCommand).Name, _moduleContext);
        }

        // ==================== RUN METHODS (5 PARAMS) ====================

        public void Run<TCommand, T1, T2, T3, T4, T5>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5)
            where TCommand : Command<T1, T2, T3, T4, T5>, new()
        {
            var pool = GetOrCreatePool<TCommand>(0);
            ExecuteCommandDirect5<TCommand, T1, T2, T3, T4, T5>(pool, p1, p2, p3, p4, p5);
        }

        public async Task RunAsync<TCommand, T1, T2, T3, T4, T5>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5)
            where TCommand : CommandAsync<T1, T2, T3, T4, T5>, new()
        {
            var pool = GetOrCreatePool<TCommand>(0);
            await ExecuteCommandDirectAsync5<TCommand, T1, T2, T3, T4, T5>(pool, p1, p2, p3, p4, p5);
        }

        #endregion
    }
}

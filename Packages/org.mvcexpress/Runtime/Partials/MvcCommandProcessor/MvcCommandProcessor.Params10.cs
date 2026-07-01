﻿// Arity variant 10 of MvcCommandProcessor - see MvcCommandProcessor.Params00.cs for the template pattern.
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
        #region Generic Command Binding (10 Params)

        // ==================== STATIC GENERIC STORAGE (10 PARAMS) ====================

        private static class CommandBinding<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
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

        private static class CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
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

        // ==================== BIND METHODS (10 PARAMS) ====================

        public void BindCommand<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            if (IsAsyncCommandType<TCommand>())
            {
                BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(poolSize);
                return;
            }

            CommandBinding<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.EnsureCapacity(_instanceId);

            if (CommandBinding<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.IsBound[_instanceId])
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

            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> commandExecutor = (p1, p2, p3, p4, p5, p6, p7, p8, p9, p10) => ExecuteCommand10<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10);
            var token = _messageBus.Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(commandExecutor);

            CommandBinding<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.Tokens[_instanceId] = token;
            CommandBinding<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.IsBound[_instanceId] = true;

            var capturedToken10 = token;
            var capturedId10 = _instanceId;
            TrackUnbindAction(() =>
            {
                _messageBus.Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(capturedToken10);
                CommandBinding<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.IsBound[capturedId10] = false;
            });

            TrackBinding(typeof(TMessage), typeof(TCommand), isAsync: false);

        }

        public void BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.EnsureCapacity(_instanceId);

            if (CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.IsBound[_instanceId])
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

            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> commandExecutor = (p1, p2, p3, p4, p5, p6, p7, p8, p9, p10) => ExecuteCommandAsync10<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10);
            var token = _messageBus.Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(commandExecutor);

            CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.Tokens[_instanceId] = token;
            CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.IsBound[_instanceId] = true;

            var capturedToken10Async = token;
            var capturedId10Async = _instanceId;
            TrackUnbindAction(() =>
            {
                _messageBus.Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(capturedToken10Async);
                CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.IsBound[capturedId10Async] = false;
            });

            TrackBinding(typeof(TMessage), typeof(TCommand), isAsync: true);

        }

        // ==================== EXECUTE METHODS (10 PARAMS) ====================

        private void ExecuteCommand10<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            MvcLogInternal.LogCommandExecuting<TMessage, TCommand>(_moduleContext);
            var pool = GetOrCreatePool<TCommand>(0);
            ExecuteCommandDirect10<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(pool, p1, p2, p3, p4, p5, p6, p7, p8, p9, p10);
        }

        private async void ExecuteCommandAsync10<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            MvcLogInternal.LogCommandExecuting<TMessage, TCommand>(_moduleContext);
            var pool = GetOrCreatePool<TCommand>(0);
            await ExecuteCommandDirectAsync10<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(pool, p1, p2, p3, p4, p5, p6, p7, p8, p9, p10);
        }

        private void ExecuteCommandDirect10<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(BoundedObjectPool<MvcCommandBase> pool, T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10)
            where TCommand : MvcCommandBase, new()
        {
            var cmd = (TCommand)pool.Get();

            try
            {
                BeginCommandExecution();
                cmd.Initialize(_moduleContext, _container, _messageBus, this);

                var typedCmd = cmd as Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>;
                typedCmd?.Execute(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10);
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

        private async Task ExecuteCommandDirectAsync10<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(BoundedObjectPool<MvcCommandBase> pool, T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10)
            where TCommand : MvcCommandBase, new()
        {
            var cmd = (TCommand)pool.Get();

            try
            {
                BeginCommandExecution();
                cmd.Initialize(_moduleContext, _container, _messageBus, this);

                var typedCmd = cmd as CommandAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>;
                if (typedCmd != null)
                    await typedCmd.ExecuteAsync(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10);
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

        // ==================== UNBIND METHODS (10 PARAMS) ====================

        public void UnbindGeneric<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>()
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            if (_instanceId >= CommandBinding<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.IsBound.Length ||
                !CommandBinding<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.IsBound[_instanceId])
            {
                return;
            }

            var token = CommandBinding<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.Tokens[_instanceId];
            _messageBus.Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(token);

            CommandBinding<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.IsBound[_instanceId] = false;
            UntrackBinding(typeof(TMessage), typeof(TCommand));

            MvcLogInternal.LogCommandUnbound(typeof(TMessage).Name, typeof(TCommand).Name, _moduleContext);
        }

        public void UnbindAsyncGeneric<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>()
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            if (_instanceId >= CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.IsBound.Length ||
                !CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.IsBound[_instanceId])
            {
                return;
            }

            var token = CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.Tokens[_instanceId];
            _messageBus.Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(token);

            CommandBindingAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.IsBound[_instanceId] = false;
            UntrackBinding(typeof(TMessage), typeof(TCommand));

            MvcLogInternal.LogCommandUnbound(typeof(TMessage).Name, typeof(TCommand).Name, _moduleContext);
        }

        // ==================== RUN METHODS (10 PARAMS) ====================

        public void Run<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10)
            where TCommand : Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>, new()
        {
            var pool = GetOrCreatePool<TCommand>(0);
            ExecuteCommandDirect10<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(pool, p1, p2, p3, p4, p5, p6, p7, p8, p9, p10);
        }

        public async Task RunAsync<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10)
            where TCommand : CommandAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>, new()
        {
            var pool = GetOrCreatePool<TCommand>(0);
            await ExecuteCommandDirectAsync10<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(pool, p1, p2, p3, p4, p5, p6, p7, p8, p9, p10);
        }

        #endregion
    }
}

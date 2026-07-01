// Arity variant 3 of MvcCommandProcessor - see MvcCommandProcessor.Params00.cs for the template pattern.
using mvcExpress.Internal.Messaging;
using mvcExpress.Internal.Utilities;
using mvcExpress.Logging;
using System;
using System.Threading.Tasks;

namespace mvcExpress.Internal.Commands
{
    public sealed partial class MvcCommandProcessor
    {
        #region Generic Command Binding (3 Params)

        // ==================== STATIC GENERIC STORAGE (3 PARAMS) ====================

        private static class CommandBinding<TCommand, TMessage, T1, T2, T3>
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3>
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

        private static class CommandBindingAsync<TCommand, TMessage, T1, T2, T3>
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3>
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

        // ==================== BIND METHODS (3 PARAMS) ====================

        public void BindCommand<TCommand, TMessage, T1, T2, T3>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3>
        {
            if (IsAsyncCommandType<TCommand>())
            {
                BindCommandAsync<TCommand, TMessage, T1, T2, T3>(poolSize);
                return;
            }

            CommandBinding<TCommand, TMessage, T1, T2, T3>.EnsureCapacity(_instanceId);

            if (CommandBinding<TCommand, TMessage, T1, T2, T3>.IsBound[_instanceId])
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

            Action<T1, T2, T3> commandExecutor = (p1, p2, p3) => ExecuteCommand3<TCommand, TMessage, T1, T2, T3>(p1, p2, p3);
            var token = _messageBus.Subscribe<TMessage, T1, T2, T3>(commandExecutor);

            CommandBinding<TCommand, TMessage, T1, T2, T3>.Tokens[_instanceId] = token;
            CommandBinding<TCommand, TMessage, T1, T2, T3>.IsBound[_instanceId] = true;

            var capturedToken3 = token;
            var capturedId3 = _instanceId;
            TrackUnbindAction(() =>
            {
                _messageBus.Unsubscribe<TMessage, T1, T2, T3>(capturedToken3);
                CommandBinding<TCommand, TMessage, T1, T2, T3>.IsBound[capturedId3] = false;
            });

            TrackBinding(typeof(TMessage), typeof(TCommand), isAsync: false);

        }

        public void BindCommandAsync<TCommand, TMessage, T1, T2, T3>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3>
        {
            CommandBindingAsync<TCommand, TMessage, T1, T2, T3>.EnsureCapacity(_instanceId);

            if (CommandBindingAsync<TCommand, TMessage, T1, T2, T3>.IsBound[_instanceId])
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

            Action<T1, T2, T3> commandExecutor = (p1, p2, p3) => ExecuteCommandAsync3<TCommand, TMessage, T1, T2, T3>(p1, p2, p3);
            var token = _messageBus.Subscribe<TMessage, T1, T2, T3>(commandExecutor);

            CommandBindingAsync<TCommand, TMessage, T1, T2, T3>.Tokens[_instanceId] = token;
            CommandBindingAsync<TCommand, TMessage, T1, T2, T3>.IsBound[_instanceId] = true;

            var capturedToken3Async = token;
            var capturedId3Async = _instanceId;
            TrackUnbindAction(() =>
            {
                _messageBus.Unsubscribe<TMessage, T1, T2, T3>(capturedToken3Async);
                CommandBindingAsync<TCommand, TMessage, T1, T2, T3>.IsBound[capturedId3Async] = false;
            });

            TrackBinding(typeof(TMessage), typeof(TCommand), isAsync: true);

        }

        // ==================== EXECUTE METHODS (3 PARAMS) ====================

        private void ExecuteCommand3<TCommand, TMessage, T1, T2, T3>(T1 p1, T2 p2, T3 p3)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3>
        {
            MvcLogInternal.LogCommandExecuting<TMessage, TCommand>(_moduleContext);
            var pool = GetOrCreatePool<TCommand>(0);
            ExecuteCommandDirect3<TCommand, T1, T2, T3>(pool, p1, p2, p3);
        }

        private async void ExecuteCommandAsync3<TCommand, TMessage, T1, T2, T3>(T1 p1, T2 p2, T3 p3)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3>
        {
            MvcLogInternal.LogCommandExecuting<TMessage, TCommand>(_moduleContext);
            var pool = GetOrCreatePool<TCommand>(0);
            await ExecuteCommandDirectAsync3<TCommand, T1, T2, T3>(pool, p1, p2, p3);
        }

        private void ExecuteCommandDirect3<TCommand, T1, T2, T3>(BoundedObjectPool<MvcCommandBase> pool, T1 p1, T2 p2, T3 p3)
            where TCommand : MvcCommandBase, new()
        {
            var cmd = (TCommand)pool.Get();

            try
            {
                BeginCommandExecution();
                cmd.Initialize(_moduleContext, _container, _messageBus, this);

                var typedCmd = cmd as Command<T1, T2, T3>;
                typedCmd?.Execute(p1, p2, p3);
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

        private async Task ExecuteCommandDirectAsync3<TCommand, T1, T2, T3>(BoundedObjectPool<MvcCommandBase> pool, T1 p1, T2 p2, T3 p3)
            where TCommand : MvcCommandBase, new()
        {
            var cmd = (TCommand)pool.Get();

            try
            {
                BeginCommandExecution();
                cmd.Initialize(_moduleContext, _container, _messageBus, this);

                var typedCmd = cmd as CommandAsync<T1, T2, T3>;
                if (typedCmd != null)
                    await typedCmd.ExecuteAsync(p1, p2, p3);
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

        // ==================== UNBIND METHODS (3 PARAMS) ====================

        public void UnbindGeneric<TCommand, TMessage, T1, T2, T3>()
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3>
        {
            if (_instanceId >= CommandBinding<TCommand, TMessage, T1, T2, T3>.IsBound.Length ||
                !CommandBinding<TCommand, TMessage, T1, T2, T3>.IsBound[_instanceId])
            {
                return;
            }

            var token = CommandBinding<TCommand, TMessage, T1, T2, T3>.Tokens[_instanceId];
            _messageBus.Unsubscribe<TMessage, T1, T2, T3>(token);

            CommandBinding<TCommand, TMessage, T1, T2, T3>.IsBound[_instanceId] = false;
            UntrackBinding(typeof(TMessage), typeof(TCommand));

            MvcLogInternal.LogCommandUnbound(typeof(TMessage).Name, typeof(TCommand).Name, _moduleContext);
        }

        public void UnbindAsyncGeneric<TCommand, TMessage, T1, T2, T3>()
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3>
        {
            if (_instanceId >= CommandBindingAsync<TCommand, TMessage, T1, T2, T3>.IsBound.Length ||
                !CommandBindingAsync<TCommand, TMessage, T1, T2, T3>.IsBound[_instanceId])
            {
                return;
            }

            var token = CommandBindingAsync<TCommand, TMessage, T1, T2, T3>.Tokens[_instanceId];
            _messageBus.Unsubscribe<TMessage, T1, T2, T3>(token);

            CommandBindingAsync<TCommand, TMessage, T1, T2, T3>.IsBound[_instanceId] = false;
            UntrackBinding(typeof(TMessage), typeof(TCommand));

            MvcLogInternal.LogCommandUnbound(typeof(TMessage).Name, typeof(TCommand).Name, _moduleContext);
        }

        // ==================== RUN METHODS (3 PARAMS) ====================

        public void Run<TCommand, T1, T2, T3>(T1 p1, T2 p2, T3 p3) where TCommand : Command<T1, T2, T3>, new()
        {
            var pool = GetOrCreatePool<TCommand>(0);
            ExecuteCommandDirect3<TCommand, T1, T2, T3>(pool, p1, p2, p3);
        }

        public async Task RunAsync<TCommand, T1, T2, T3>(T1 p1, T2 p2, T3 p3) where TCommand : CommandAsync<T1, T2, T3>, new()
        {
            var pool = GetOrCreatePool<TCommand>(0);
            await ExecuteCommandDirectAsync3<TCommand, T1, T2, T3>(pool, p1, p2, p3);
        }

        #endregion
    }
}
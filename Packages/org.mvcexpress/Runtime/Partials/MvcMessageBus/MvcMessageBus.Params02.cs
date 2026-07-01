// Arity variant 2 of MvcMessageBus - see MvcMessageBus.Params00.cs for the template pattern.
using mvcExpress.Internal.Interfaces;
using System;
using System.Runtime.CompilerServices;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using mvcExpress.Plugins;
#endif

namespace mvcExpress.Internal.Messaging
{
    public sealed partial class MvcMessageBus : IMessageBus, IDisposable
    {
        // Generic clear methods for each storage arity
        private void ClearStorage2(Type messageType, Type[] args)
        {
            var storageType = typeof(Storage2<,,>).MakeGenericType(messageType, args[0], args[1]);
            ClearStorageSlot(storageType);
        }

        // Storage Class for 2 parameters
        private static class Storage2<TMessage, T1, T2> where TMessage : IMessage<T1, T2>
        {
            internal static Action<T1, T2>[][] InstanceHandlers = new Action<T1, T2>[4][];
            internal static int[][] InstanceVersions = new int[4][];
            internal static int[] InstanceCounts = new int[4];
            internal static int[] InstanceVersionCounters = new int[4];
            internal static readonly object Lock = new object();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static void EnsureCapacity(int instanceId)
            {
                if (instanceId >= InstanceHandlers.Length)
                {
                    lock (Lock)
                    {
                        if (instanceId >= InstanceHandlers.Length)
                        {
                            int newSize = Math.Max(instanceId + 1, InstanceHandlers.Length * 2);
                            Array.Resize(ref InstanceHandlers, newSize);
                            Array.Resize(ref InstanceVersions, newSize);
                            Array.Resize(ref InstanceCounts, newSize);
                            Array.Resize(ref InstanceVersionCounters, newSize);
                        }
                    }
                }

                if (InstanceHandlers[instanceId] == null)
                {
                    lock (Lock)
                    {
                        if (InstanceHandlers[instanceId] == null)
                        {
                            InstanceHandlers[instanceId] = new Action<T1, T2>[INITIAL_CAPACITY];
                            InstanceVersions[instanceId] = new int[INITIAL_CAPACITY];
                            InstanceVersionCounters[instanceId] = 1;
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage, T1, T2>(T1 p1, T2 p2) where TMessage : IMessage<T1, T2>
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread();
#endif

            if (_instanceId >= Storage2<TMessage, T1, T2>.InstanceHandlers.Length ||
                Storage2<TMessage, T1, T2>.InstanceHandlers[_instanceId] == null)
            {
                return;
            }

            var handlers = Storage2<TMessage, T1, T2>.InstanceHandlers[_instanceId];
            var count = Storage2<TMessage, T1, T2>.InstanceCounts[_instanceId];

            for (int i = 0; i < count; i++)
            {
                var handler = handlers[i];
                if (handler != null)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    MvcPluginBus.FireMessageDispatched(
                        typeof(TMessage),
                        CrossModuleContext.CurrentPublisher,
                        handler.Target?.GetType(),
                        MvcPluginBus.GetSubscriberModuleType(handler.Target));
#endif
                    handler(p1, p2);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2>(Action<T1, T2> handler) where TMessage : IMessage<T1, T2>
        {
            // IMPROVEMENT 5: Validate message type (Editor/Development only)
            MessageValidationCache.ValidateMessageType(typeof(TMessage));

            // IMPROVEMENT 6: Thread-safe message type tracking
            _usedMessageTypes.TryAdd(typeof(TMessage), 0);

            Storage2<TMessage, T1, T2>.EnsureCapacity(_instanceId);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // IMPROVEMENT 7: Thread-safe subscription in Editor/Development
            lock (Storage2<TMessage, T1, T2>.Lock)
            {
                return SubscribeInternal<TMessage, T1, T2>(handler);
            }
#else
            return SubscribeInternal<TMessage, T1, T2>(handler);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private SubscriptionToken SubscribeInternal<TMessage, T1, T2>(Action<T1, T2> handler) where TMessage : IMessage<T1, T2>
        {
            var count = Storage2<TMessage, T1, T2>.InstanceCounts[_instanceId];
            var handlers = Storage2<TMessage, T1, T2>.InstanceHandlers[_instanceId];
            var versions = Storage2<TMessage, T1, T2>.InstanceVersions[_instanceId];

            if (count == handlers.Length)
            {
                var newSize = Math.Min(handlers.Length * 2, MAX_POOL_SIZE);
                if (newSize <= handlers.Length) newSize = handlers.Length + 4;

                var newHandlers = new Action<T1, T2>[newSize];
                var newVersions = new int[newSize];
                Array.Copy(handlers, newHandlers, count);
                Array.Copy(versions, newVersions, count);
                Storage2<TMessage, T1, T2>.InstanceHandlers[_instanceId] = handlers = newHandlers;
                Storage2<TMessage, T1, T2>.InstanceVersions[_instanceId] = versions = newVersions;
            }

            var version = ++Storage2<TMessage, T1, T2>.InstanceVersionCounters[_instanceId];
            handlers[count] = handler;
            versions[count] = version;
            Storage2<TMessage, T1, T2>.InstanceCounts[_instanceId] = count + 1;

            var token = new SubscriptionToken(count, version);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            token.DebugName = $"{typeof(TMessage).Name}<{typeof(T1).Name}, {typeof(T2).Name}>";
            token.SubscriberTypeName = handler.Target?.GetType().Name ?? "Static";
            token.SubscribeTime = UnityEngine.Time.time;
#endif

            return token;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2>(SubscriptionToken token) where TMessage : IMessage<T1, T2>
        {
            if (_instanceId >= Storage2<TMessage, T1, T2>.InstanceHandlers.Length ||
                Storage2<TMessage, T1, T2>.InstanceHandlers[_instanceId] == null)
            {
                return;
            }

            var idx = token.Index;
            if (idx >= 0 && idx < Storage2<TMessage, T1, T2>.InstanceCounts[_instanceId])
            {
                if (Storage2<TMessage, T1, T2>.InstanceVersions[_instanceId][idx] == token.Version)
                {
                    Storage2<TMessage, T1, T2>.InstanceHandlers[_instanceId][idx] = null;
                    Storage2<TMessage, T1, T2>.InstanceVersions[_instanceId][idx] = 0;
                }
            }
        }

        public void Unsubscribe<TMessage, T1, T2>(Action<T1, T2> handler) where TMessage : IMessage<T1, T2>
        {
            if (handler == null) return;
            if (_instanceId >= Storage2<TMessage, T1, T2>.InstanceHandlers.Length ||
                Storage2<TMessage, T1, T2>.InstanceHandlers[_instanceId] == null)
            {
                return;
            }

            var handlers = Storage2<TMessage, T1, T2>.InstanceHandlers[_instanceId];
            var count = Storage2<TMessage, T1, T2>.InstanceCounts[_instanceId];
            for (int i = 0; i < count; i++)
            {
                if (ReferenceEquals(handlers[i], handler))
                {
                    handlers[i] = null;
                    Storage2<TMessage, T1, T2>.InstanceVersions[_instanceId][i] = 0;
                    return;
                }
            }
        }

        public void UnsubscribeAll<TMessage, T1, T2>() where TMessage : IMessage<T1, T2>
        {
            if (_instanceId >= Storage2<TMessage, T1, T2>.InstanceHandlers.Length ||
                Storage2<TMessage, T1, T2>.InstanceHandlers[_instanceId] == null)
            {
                return;
            }

            Array.Clear(Storage2<TMessage, T1, T2>.InstanceHandlers[_instanceId], 0, Storage2<TMessage, T1, T2>.InstanceCounts[_instanceId]);
            Array.Clear(Storage2<TMessage, T1, T2>.InstanceVersions[_instanceId], 0, Storage2<TMessage, T1, T2>.InstanceCounts[_instanceId]);
            Storage2<TMessage, T1, T2>.InstanceCounts[_instanceId] = 0;
        }






    }
}


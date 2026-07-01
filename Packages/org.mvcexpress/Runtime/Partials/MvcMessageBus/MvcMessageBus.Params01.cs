// Arity variant 1 of MvcMessageBus - see MvcMessageBus.Params00.cs for the template pattern.
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
        private void ClearStorage1(Type messageType, Type[] args)
        {
            var storageType = typeof(Storage1<,>).MakeGenericType(messageType, args[0]);
            ClearStorageSlot(storageType);
        }

        // Storage Class for 1 parameters
        private static class Storage1<TMessage, T1> where TMessage : IMessage<T1>
        {
            internal static Action<T1>[][] InstanceHandlers = new Action<T1>[4][];
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
                            InstanceHandlers[instanceId] = new Action<T1>[INITIAL_CAPACITY];
                            InstanceVersions[instanceId] = new int[INITIAL_CAPACITY];
                            InstanceVersionCounters[instanceId] = 1;
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage, T1>(T1 p1) where TMessage : IMessage<T1>
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread();
#endif

            if (_instanceId >= Storage1<TMessage, T1>.InstanceHandlers.Length ||
                Storage1<TMessage, T1>.InstanceHandlers[_instanceId] == null)
            {
                return;
            }

            var handlers = Storage1<TMessage, T1>.InstanceHandlers[_instanceId];
            var count = Storage1<TMessage, T1>.InstanceCounts[_instanceId];

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
                    handler(p1);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1>(Action<T1> handler) where TMessage : IMessage<T1>
        {
            // IMPROVEMENT 5: Validate message type (Editor/Development only)
            MessageValidationCache.ValidateMessageType(typeof(TMessage));

            // IMPROVEMENT 6: Thread-safe message type tracking
            _usedMessageTypes.TryAdd(typeof(TMessage), 0);

            Storage1<TMessage, T1>.EnsureCapacity(_instanceId);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // IMPROVEMENT 7: Thread-safe subscription in Editor/Development
            // Protects against concurrent subscriptions to the same message type
            lock (Storage1<TMessage, T1>.Lock)
            {
                return SubscribeInternal<TMessage, T1>(handler);
            }
#else
            // Production: No lock for maximum performance
            // Users must ensure single-threaded access per message type
            return SubscribeInternal<TMessage, T1>(handler);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private SubscriptionToken SubscribeInternal<TMessage, T1>(Action<T1> handler) where TMessage : IMessage<T1>
        {
            var count = Storage1<TMessage, T1>.InstanceCounts[_instanceId];
            var handlers = Storage1<TMessage, T1>.InstanceHandlers[_instanceId];
            var versions = Storage1<TMessage, T1>.InstanceVersions[_instanceId];

            if (count == handlers.Length)
            {
                var newSize = Math.Min(handlers.Length * 2, MAX_POOL_SIZE);
                if (newSize <= handlers.Length) newSize = handlers.Length + 4;

                var newHandlers = new Action<T1>[newSize];
                var newVersions = new int[newSize];
                Array.Copy(handlers, newHandlers, count);
                Array.Copy(versions, newVersions, count);
                Storage1<TMessage, T1>.InstanceHandlers[_instanceId] = handlers = newHandlers;
                Storage1<TMessage, T1>.InstanceVersions[_instanceId] = versions = newVersions;
            }

            var version = ++Storage1<TMessage, T1>.InstanceVersionCounters[_instanceId];
            handlers[count] = handler;
            versions[count] = version;
            Storage1<TMessage, T1>.InstanceCounts[_instanceId] = count + 1;

            var token = new SubscriptionToken(count, version);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            token.DebugName = $"{typeof(TMessage).Name}<{typeof(T1).Name}>";
            token.SubscriberTypeName = handler.Target?.GetType().Name ?? "Static";
            token.SubscribeTime = UnityEngine.Time.time;
#endif

            return token;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1>(SubscriptionToken token) where TMessage : IMessage<T1>
        {
            if (_instanceId >= Storage1<TMessage, T1>.InstanceHandlers.Length ||
                Storage1<TMessage, T1>.InstanceHandlers[_instanceId] == null)
            {
                return;
            }

            var idx = token.Index;
            if (idx >= 0 && idx < Storage1<TMessage, T1>.InstanceCounts[_instanceId])
            {
                if (Storage1<TMessage, T1>.InstanceVersions[_instanceId][idx] == token.Version)
                {
                    Storage1<TMessage, T1>.InstanceHandlers[_instanceId][idx] = null;
                    Storage1<TMessage, T1>.InstanceVersions[_instanceId][idx] = 0;
                }
            }
        }

        public void Unsubscribe<TMessage, T1>(Action<T1> handler) where TMessage : IMessage<T1>
        {
            if (handler == null) return;
            if (_instanceId >= Storage1<TMessage, T1>.InstanceHandlers.Length ||
                Storage1<TMessage, T1>.InstanceHandlers[_instanceId] == null)
            {
                return;
            }

            var handlers = Storage1<TMessage, T1>.InstanceHandlers[_instanceId];
            var count = Storage1<TMessage, T1>.InstanceCounts[_instanceId];
            for (int i = 0; i < count; i++)
            {
                if (ReferenceEquals(handlers[i], handler))
                {
                    handlers[i] = null;
                    Storage1<TMessage, T1>.InstanceVersions[_instanceId][i] = 0;
                    return;
                }
            }
        }

        public void UnsubscribeAll<TMessage, T1>() where TMessage : IMessage<T1>
        {
            if (_instanceId >= Storage1<TMessage, T1>.InstanceHandlers.Length ||
                Storage1<TMessage, T1>.InstanceHandlers[_instanceId] == null)
            {
                return;
            }

            Array.Clear(Storage1<TMessage, T1>.InstanceHandlers[_instanceId], 0, Storage1<TMessage, T1>.InstanceCounts[_instanceId]);
            Array.Clear(Storage1<TMessage, T1>.InstanceVersions[_instanceId], 0, Storage1<TMessage, T1>.InstanceCounts[_instanceId]);
            Storage1<TMessage, T1>.InstanceCounts[_instanceId] = 0;
        }




    }
}


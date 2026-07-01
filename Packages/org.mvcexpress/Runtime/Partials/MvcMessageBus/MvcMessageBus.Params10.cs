// Arity variant 10 of MvcMessageBus - see MvcMessageBus.Params00.cs for the template pattern.
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
        private void ClearStorage10(Type messageType, Type[] args)
        {
            var storageType = typeof(Storage10<,,,,,,,,,,>).MakeGenericType(messageType, args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9]);
            ClearStorageSlot(storageType);
        }

        // Storage Class for 10 parameters
        private static class Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            internal static Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>[][] InstanceHandlers = new Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>[4][];
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
                            InstanceHandlers[instanceId] = new Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>[INITIAL_CAPACITY];
                            InstanceVersions[instanceId] = new int[INITIAL_CAPACITY];
                            InstanceVersionCounters[instanceId] = 1;
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread();
#endif
            if (_instanceId >= Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceHandlers.Length ||
                Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceHandlers[_instanceId] == null)
            {
                return;
            }

            var handlers = Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceHandlers[_instanceId];
            var count = Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceCounts[_instanceId];

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
                    handler(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            MessageValidationCache.ValidateMessageType(typeof(TMessage));
            _usedMessageTypes.TryAdd(typeof(TMessage), 0);
            Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.EnsureCapacity(_instanceId);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            lock (Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.Lock)
            {
                return SubscribeInternal<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(handler);
            }
#else
            return SubscribeInternal<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(handler);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private SubscriptionToken SubscribeInternal<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            var count = Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceCounts[_instanceId];
            var handlers = Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceHandlers[_instanceId];
            var versions = Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceVersions[_instanceId];

            if (count == handlers.Length)
            {
                var newSize = Math.Min(handlers.Length * 2, MAX_POOL_SIZE);
                if (newSize <= handlers.Length) newSize = handlers.Length + 4;

                var newHandlers = new Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>[newSize];
                var newVersions = new int[newSize];
                Array.Copy(handlers, newHandlers, count);
                Array.Copy(versions, newVersions, count);
                Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceHandlers[_instanceId] = handlers = newHandlers;
                Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceVersions[_instanceId] = versions = newVersions;
            }

            var version = ++Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceVersionCounters[_instanceId];
            handlers[count] = handler;
            versions[count] = version;
            Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceCounts[_instanceId] = count + 1;

            var token = new SubscriptionToken(count, version);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            token.DebugName = $"{typeof(TMessage).Name}<{typeof(T1).Name},{typeof(T2).Name},{typeof(T3).Name},{typeof(T4).Name},{typeof(T5).Name},{typeof(T6).Name},{typeof(T7).Name},{typeof(T8).Name},{typeof(T9).Name},{typeof(T10).Name}>";
            token.SubscriberTypeName = handler.Target?.GetType().Name ?? "Static";
            token.SubscribeTime = UnityEngine.Time.time;
#endif

            return token;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            if (_instanceId >= Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceHandlers.Length ||
                Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceHandlers[_instanceId] == null)
            {
                return;
            }

            var idx = token.Index;
            if (idx >= 0 && idx < Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceCounts[_instanceId])
            {
                if (Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceVersions[_instanceId][idx] == token.Version)
                {
                    Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceHandlers[_instanceId][idx] = null;
                    Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceVersions[_instanceId][idx] = 0;
                }
            }
        }

        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            if (handler == null) return;
            if (_instanceId >= Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceHandlers.Length ||
                Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceHandlers[_instanceId] == null)
            {
                return;
            }

            var handlers = Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceHandlers[_instanceId];
            var count = Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceCounts[_instanceId];
            for (int i = 0; i < count; i++)
            {
                if (ReferenceEquals(handlers[i], handler))
                {
                    handlers[i] = null;
                    Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceVersions[_instanceId][i] = 0;
                    return;
                }
            }
        }

        public void UnsubscribeAll<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>() where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            if (_instanceId >= Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceHandlers.Length ||
                Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceHandlers[_instanceId] == null)
            {
                return;
            }

            Array.Clear(Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceHandlers[_instanceId], 0, Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceCounts[_instanceId]);
            Array.Clear(Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceVersions[_instanceId], 0, Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceCounts[_instanceId]);
            Storage10<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>.InstanceCounts[_instanceId] = 0;
        }



    }
}


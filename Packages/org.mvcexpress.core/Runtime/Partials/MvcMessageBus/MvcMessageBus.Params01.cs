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
            // InstanceFreeListHeads[instanceId] = index of the most recently unsubscribed slot for
            // reuse (or NO_FREE_SLOT) - see the free-list note next to NO_FREE_SLOT in MvcMessageBus.cs.
            internal static int[] InstanceFreeListHeads = CreateFreeListHeads(4);
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
                            GrowFreeListHeads(ref InstanceFreeListHeads, newSize);
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

        /// <summary>
        /// Publishes a one-payload message to every subscriber on this bus instance, in subscribe order.
        /// Main-thread-only; validated in Editor/Development builds. Zero allocation for struct
        /// messages and struct payload types.
        /// </summary>
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
                    if (MvcPluginBus.HasMessageObservers)
                    {
                        MvcPluginBus.FireMessageDispatched(
                            typeof(TMessage),
                            CrossModuleContext.CurrentPublisher,
                            handler.Target?.GetType(),
                            MvcPluginBus.GetSubscriberModuleType(handler.Target));
                    }
#endif
                    handler(p1);
                }
            }
        }

        /// <summary>
        /// Subscribes a handler to a one-payload message on this bus instance.
        /// </summary>
        /// <returns>A <see cref="SubscriptionToken"/> for removal via <c>Unsubscribe</c>.</returns>
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
            var handlers = Storage1<TMessage, T1>.InstanceHandlers[_instanceId];
            var versions = Storage1<TMessage, T1>.InstanceVersions[_instanceId];

            // Reuse a freed slot before growing the array - see the free-list note in MvcMessageBus.cs.
            var freeIndex = Storage1<TMessage, T1>.InstanceFreeListHeads[_instanceId];
            if (freeIndex != NO_FREE_SLOT)
            {
                Storage1<TMessage, T1>.InstanceFreeListHeads[_instanceId] = DecodeNextFree(versions[freeIndex]);

                var reusedVersion = ++Storage1<TMessage, T1>.InstanceVersionCounters[_instanceId];
                handlers[freeIndex] = handler;
                versions[freeIndex] = reusedVersion;

                var reusedToken = new SubscriptionToken(freeIndex, reusedVersion);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                reusedToken.DebugName = $"{typeof(TMessage).Name}<{typeof(T1).Name}>";
                reusedToken.SubscriberTypeName = handler.Target?.GetType().Name ?? "Static";
                reusedToken.SubscribeTime = UnityEngine.Time.time;
#endif
                return reusedToken;
            }

            var count = Storage1<TMessage, T1>.InstanceCounts[_instanceId];

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

        /// <summary>
        /// Removes a subscription by its <see cref="SubscriptionToken"/>. Safe to call with a stale
        /// or already-removed token - the version stamp makes this a no-op rather than corrupting a
        /// slot that has since been reused by a different subscriber.
        /// </summary>
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
                    Storage1<TMessage, T1>.InstanceVersions[_instanceId][idx] = EncodeNextFree(Storage1<TMessage, T1>.InstanceFreeListHeads[_instanceId]);
                    Storage1<TMessage, T1>.InstanceFreeListHeads[_instanceId] = idx;
                }
            }
        }

        /// <summary>
        /// Removes a subscription by delegate reference (linear scan over active subscribers).
        /// Prefer the <see cref="SubscriptionToken"/> overload on hot paths - this one is O(n) in
        /// the current subscriber count for this message type.
        /// </summary>
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
                    Storage1<TMessage, T1>.InstanceVersions[_instanceId][i] = EncodeNextFree(Storage1<TMessage, T1>.InstanceFreeListHeads[_instanceId]);
                    Storage1<TMessage, T1>.InstanceFreeListHeads[_instanceId] = i;
                    return;
                }
            }
        }

        /// <summary>
        /// Removes every subscriber for a one-payload message on this bus instance, and resets the
        /// <c>HasSubscribers</c> high-watermark for this message type back to false.
        /// </summary>
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
            Storage1<TMessage, T1>.InstanceFreeListHeads[_instanceId] = NO_FREE_SLOT;
        }

        /// <summary>
        /// Check if this bus instance has any active subscriptions for a message type.
        /// Useful for optimization - avoid sending messages when no one is listening.
        /// </summary>
        /// <summary>
        /// Returns whether a one-payload message has ever had a subscriber on this bus instance that has
        /// not since been cleared by <c>UnsubscribeAll</c>.
        /// </summary>
        /// <remarks>
        /// This is a high-watermark check, not a live subscriber count: unsubscribing an individual
        /// handler via either <c>Unsubscribe</c> overload does NOT make this return false again -
        /// only <c>UnsubscribeAll</c> resets it. Do not use this to detect "the last subscriber just
        /// left"; it only tells you whether this message type has ever been subscribed to since the
        /// last full clear.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasSubscribers<TMessage, T1>() where TMessage : IMessage<T1>
        {
            if (!_usedMessageTypes.ContainsKey(typeof(TMessage)))
            {
                return false;
            }

            if (_instanceId >= Storage1<TMessage, T1>.InstanceHandlers.Length ||
                Storage1<TMessage, T1>.InstanceHandlers[_instanceId] == null)
            {
                return false;
            }

            return Storage1<TMessage, T1>.InstanceCounts[_instanceId] > 0;
        }




    }
}


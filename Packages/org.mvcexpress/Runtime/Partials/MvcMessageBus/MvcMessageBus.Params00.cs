// Arity-0 partial of MvcMessageBus - 0-parameter message overloads.
// This file is the canonical template for all arity variants (Params01-Params12).
// Pattern repeated in each variant:
//   - StorageN<TMessage[, T1..TN]>  : static nested class holding per-instance handler arrays
//   - EnsureCapacity(instanceId)    : grows arrays to fit the given instance ID
//   - Publish<TMessage[, T1..TN]>   : iterate handlers and invoke (zero alloc, main thread only)
//   - Subscribe<TMessage[, T1..TN]> : append handler, return SubscriptionToken
//   - SubscribeInternal             : inner unsynchronised subscribe (called under lock in Dev)
//   - Unsubscribe by token          : null-out handler slot using version check
//   - Unsubscribe by delegate       : linear scan + null-out
//   - UnsubscribeAll                : Array.Clear entire handler block
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
        // Clears all handlers in this instance's slot for Storage0<TMessage>.
        // Called from ClearStorageForMessageType during Dispose.
        private void ClearStorage0(Type messageType)
        {
            var storageType = typeof(Storage0<>).MakeGenericType(messageType);
            ClearStorageSlot(storageType);
        }

        /// <summary>
        /// Static-per-generic-type handler storage for 0-parameter messages.
        /// Arrays are indexed by instance ID so each MvcMessageBus instance has its own
        /// handler slots without allocating per-instance dictionaries.
        /// </summary>
        private static class Storage0<TMessage> where TMessage : IMessage
        {
            // InstanceHandlers[instanceId][slotIndex] = the handler delegate (null = unsubscribed).
            internal static Action[][] InstanceHandlers = new Action[4][];
            // InstanceVersions[instanceId][slotIndex] = version stamp matching the SubscriptionToken.
            // If versions differ, the slot was reused after an Unsubscribe - stale tokens are no-ops.
            internal static int[][] InstanceVersions = new int[4][];
            // InstanceCounts[instanceId] = number of slots in use (includes nulled-out unsubscribed slots).
            internal static int[] InstanceCounts = new int[4];
            // InstanceVersionCounters[instanceId] = monotonic counter used to stamp new subscriptions.
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
                            InstanceHandlers[instanceId] = new Action[INITIAL_CAPACITY];
                            InstanceVersions[instanceId] = new int[INITIAL_CAPACITY];
                            InstanceVersionCounters[instanceId] = 1;
                        }
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage>() where TMessage : IMessage
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread();
#endif

            if (_instanceId >= Storage0<TMessage>.InstanceHandlers.Length ||
                Storage0<TMessage>.InstanceHandlers[_instanceId] == null)
            {
                return;
            }

            var handlers = Storage0<TMessage>.InstanceHandlers[_instanceId];
            var count = Storage0<TMessage>.InstanceCounts[_instanceId];

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
                    handler();
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage>(Action handler) where TMessage : IMessage
        {
            // IMPROVEMENT 5: Validate message type (Editor/Development only)
            MessageValidationCache.ValidateMessageType(typeof(TMessage));

            // IMPROVEMENT 6: Thread-safe message type tracking
            _usedMessageTypes.TryAdd(typeof(TMessage), 0);

            Storage0<TMessage>.EnsureCapacity(_instanceId);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // IMPROVEMENT 7: Thread-safe subscription in Editor/Development
            lock (Storage0<TMessage>.Lock)
            {
                return SubscribeInternal<TMessage>(handler);
            }
#else
            // Production: No lock for maximum performance
            return SubscribeInternal<TMessage>(handler);
#endif
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private SubscriptionToken SubscribeInternal<TMessage>(Action handler) where TMessage : IMessage
        {
            var count = Storage0<TMessage>.InstanceCounts[_instanceId];
            var handlers = Storage0<TMessage>.InstanceHandlers[_instanceId];
            var versions = Storage0<TMessage>.InstanceVersions[_instanceId];

            if (count == handlers.Length)
            {
                var newSize = Math.Min(handlers.Length * 2, MAX_POOL_SIZE);
                if (newSize <= handlers.Length) newSize = handlers.Length + 4;

                var newHandlers = new Action[newSize];
                var newVersions = new int[newSize];
                Array.Copy(handlers, newHandlers, count);
                Array.Copy(versions, newVersions, count);
                Storage0<TMessage>.InstanceHandlers[_instanceId] = handlers = newHandlers;
                Storage0<TMessage>.InstanceVersions[_instanceId] = versions = newVersions;
            }

            var version = ++Storage0<TMessage>.InstanceVersionCounters[_instanceId];
            handlers[count] = handler;
            versions[count] = version;
            Storage0<TMessage>.InstanceCounts[_instanceId] = count + 1;

            var token = new SubscriptionToken(count, version);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            token.DebugName = typeof(TMessage).Name;
            token.SubscriberTypeName = handler.Target?.GetType().Name ?? "Static";
            token.SubscribeTime = UnityEngine.Time.time;
#endif

            return token;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage>(SubscriptionToken token) where TMessage : IMessage
        {
            if (_instanceId >= Storage0<TMessage>.InstanceHandlers.Length ||
                Storage0<TMessage>.InstanceHandlers[_instanceId] == null)
            {
                return;
            }

            var idx = token.Index;
            if (idx >= 0 && idx < Storage0<TMessage>.InstanceCounts[_instanceId])
            {
                if (Storage0<TMessage>.InstanceVersions[_instanceId][idx] == token.Version)
                {
                    Storage0<TMessage>.InstanceHandlers[_instanceId][idx] = null;
                    Storage0<TMessage>.InstanceVersions[_instanceId][idx] = 0;
                }
            }
        }

        public void Unsubscribe<TMessage>(Action handler) where TMessage : IMessage
        {
            if (handler == null) return;
            if (_instanceId >= Storage0<TMessage>.InstanceHandlers.Length ||
                Storage0<TMessage>.InstanceHandlers[_instanceId] == null)
            {
                return;
            }

            var handlers = Storage0<TMessage>.InstanceHandlers[_instanceId];
            var count = Storage0<TMessage>.InstanceCounts[_instanceId];
            for (int i = 0; i < count; i++)
            {
                if (ReferenceEquals(handlers[i], handler))
                {
                    handlers[i] = null;
                    Storage0<TMessage>.InstanceVersions[_instanceId][i] = 0;
                    return;
                }
            }
        }

        public void UnsubscribeAll<TMessage>() where TMessage : IMessage
        {
            if (_instanceId >= Storage0<TMessage>.InstanceHandlers.Length ||
                Storage0<TMessage>.InstanceHandlers[_instanceId] == null)
            {
                return;
            }

            Array.Clear(Storage0<TMessage>.InstanceHandlers[_instanceId], 0, Storage0<TMessage>.InstanceCounts[_instanceId]);
            Array.Clear(Storage0<TMessage>.InstanceVersions[_instanceId], 0, Storage0<TMessage>.InstanceCounts[_instanceId]);
            Storage0<TMessage>.InstanceCounts[_instanceId] = 0;
        }

    }
}

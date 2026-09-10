// Arity variant 5 of MvcMessageBus - see MvcMessageBus.Params00.cs for the template pattern.
using mvcExpress.Internal.Interfaces;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using mvcExpress.Plugins;
using mvcExpress.Logging;
#endif
namespace mvcExpress.Internal.Messaging
{
    public sealed partial class MvcMessageBus : IMessageBus, IDisposable
    {
        // Generic clear methods for each storage arity
        private void ClearStorage5(Type messageType, Type[] args)
        {
            var storageType = typeof(Storage5<,,,,,>).MakeGenericType(messageType, args[0], args[1], args[2], args[3], args[4]);
            ClearStorageSlot(storageType);
        }

        // Storage Class for 5 parameters
        private static class Storage5<TMessage, T1, T2, T3, T4, T5> where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            // Handlers[slot] = the handler delegate. Dense: no null holes once compaction has run.
            internal static Action<T1, T2, T3, T4, T5>[] Handlers;
            // SlotOwner[slot] = which handle currently owns Handlers[slot]. Kept in lockstep with
            // Handlers so compaction can fix up HandleToSlot for whatever it moves.
            internal static int[] SlotOwner;
            // Count = number of slots currently in use (== live subscriber count once compaction
            // has run; may briefly exceed it while a hole is pending during an active publish).
            internal static int Count;
            // LiveCount = subscribers actually attached right now - decremented immediately on
            // Unsubscribe, unlike Count which briefly lags behind during a deferred compaction.
            internal static int LiveCount;
            // Set by Unsubscribe when it nulls a slot but can't compact yet (PublishDepth > 0).
            // Checked by CompactIfNeeded so a publish with no removals pays only one bool read.
            internal static bool HasPendingHoles;

            // Handle table: the caller-visible identity behind a SubscriptionToken, independent of
            // physical position in Handlers[]. HandleToSlot[handle] is only meaningful once
            // HandleVersion[handle] has been checked against the token - a freed handle's stale
            // HandleToSlot entry is never trusted otherwise.
            internal static int[] HandleToSlot;
            internal static int[] HandleVersion;
            // Explicit free-list "next" links for handle numbers (LIFO) - separate from the data
            // array entirely, so reusing a handle never touches Handlers[] and can never race with
            // an in-progress Publish over it.
            internal static int[] HandleNextFree;
            internal static int HandleCount;
            internal static int HandleFreeListHead = NO_FREE_SLOT;
            // Shared monotonic counter for handle version stamps. Never reset (including by
            // UnsubscribeAll) so a stale token from before a reset can never collide with a
            // freshly (re)allocated handle's new version.
            internal static int VersionCounter;

            // PublishDepth = number of Publish calls currently in progress (reentrant/nested
            // included) - while > 0, Unsubscribe defers compaction (see HasPendingHoles) and
            // SubscribeInternal always appends past the current publish's captured Count, so a
            // reentrant subscription defers to the next publish (MSG-01).
            internal static int PublishDepth;

            internal static readonly object Lock = new object();

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal static void EnsureCapacity()
            {
                if (Handlers == null)
                {
                    lock (Lock)
                    {
                        if (Handlers == null)
                        {
                            Handlers = new Action<T1, T2, T3, T4, T5>[INITIAL_CAPACITY];
                            SlotOwner = new int[INITIAL_CAPACITY];
                            HandleToSlot = new int[INITIAL_CAPACITY];
                            HandleVersion = new int[INITIAL_CAPACITY];
                            HandleNextFree = new int[INITIAL_CAPACITY];
                            VersionCounter = 1;
                        }
                    }
                }
            }

            // Compacts out every nulled slot left behind by a deferred Unsubscribe, fixing up
            // HandleToSlot for whatever it moves. Only ever called with PublishDepth == 0 - see
            // the call sites in Unsubscribe and Publish's finally.
            internal static void CompactIfNeeded()
            {
                if (!HasPendingHoles) return;

                var handlers = Handlers;
                var slotOwner = SlotOwner;
                var count = Count;
                var writeIndex = 0;
                for (int readIndex = 0; readIndex < count; readIndex++)
                {
                    var handler = handlers[readIndex];
                    if (handler == null) continue;

                    if (writeIndex != readIndex)
                    {
                        var owner = slotOwner[readIndex];
                        handlers[writeIndex] = handler;
                        slotOwner[writeIndex] = owner;
                        HandleToSlot[owner] = writeIndex;
                        handlers[readIndex] = null;
                    }
                    writeIndex++;
                }
                Count = writeIndex;
                HasPendingHoles = false;
            }
        }

        /// <summary>
        /// Publishes a five-payload message to every subscriber on the application bus, in subscribe order.
        /// Main-thread-only; validated in Editor/Development builds. Zero allocation for struct
        /// messages and struct payload types.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage, T1, T2, T3, T4, T5>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5) where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread();
#endif
            if (Storage5<TMessage, T1, T2, T3, T4, T5>.Handlers == null)
            {
                return;
            }

            var count = Storage5<TMessage, T1, T2, T3, T4, T5>.Count;

            Storage5<TMessage, T1, T2, T3, T4, T5>.PublishDepth++;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    // Read the array reference fresh every iteration - see Params00.cs's Publish
                    // for why (a reentrant Subscribe that grows storage mid-publish replaces the
                    // Handlers field with a new array object; a stale cached reference here would
                    // make a further reentrant Unsubscribe invisible to this loop).
                    var handler = Storage5<TMessage, T1, T2, T3, T4, T5>.Handlers[i];
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        try
                        {
                            handler(p1, p2, p3, p4, p5);
                        }
                        catch (Exception ex)
                        {
                            MvcDebug.LogError($"Subscriber '{handler.Target?.GetType().Name ?? "Static"}' threw on {typeof(TMessage).Name}: {ex}");
                        }
#else
                        // MSG-02: this bare call IS the entire release-build contract - deliberately
                        // unguarded so a throwing subscriber propagates and stops later ones, at zero
                        // added cost (see 03_performance_and_messaging.md). Wrapping it in anything
                        // is a policy change requiring that decision, not a bug fix.
                        handler(p1, p2, p3, p4, p5);
#endif
                    }
                }
            }
            finally
            {
                if (--Storage5<TMessage, T1, T2, T3, T4, T5>.PublishDepth == 0)
                {
                    Storage5<TMessage, T1, T2, T3, T4, T5>.CompactIfNeeded();
                }
            }
        }

        /// <summary>
        /// Subscribes a handler to a five-payload message on the application bus.
        /// </summary>
        /// <returns>A <see cref="SubscriptionToken"/> for removal via <c>Unsubscribe</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> handler) where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            MessageValidationCache.ValidateMessageType(typeof(TMessage));
            _usedMessageTypes.TryAdd(typeof(TMessage), 0);
            Storage5<TMessage, T1, T2, T3, T4, T5>.EnsureCapacity();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread();
#endif
            return SubscribeInternal<TMessage, T1, T2, T3, T4, T5>(handler);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private SubscriptionToken SubscribeInternal<TMessage, T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> handler) where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            // 1. Allocate a handle - reuse a freed one if available, otherwise grow the handle table.
            //    This is fully independent of where the handler ends up in Handlers[] below, so
            //    reusing a handle number can never reintroduce the reentrant-subscribe hazard
            //    MSG-01 fixed (that hazard was about reusing a DATA slot, which we no longer do).
            int handle;
            var freeHandle = Storage5<TMessage, T1, T2, T3, T4, T5>.HandleFreeListHead;
            if (freeHandle != NO_FREE_SLOT)
            {
                handle = freeHandle;
                Storage5<TMessage, T1, T2, T3, T4, T5>.HandleFreeListHead = Storage5<TMessage, T1, T2, T3, T4, T5>.HandleNextFree[freeHandle];
            }
            else
            {
                handle = Storage5<TMessage, T1, T2, T3, T4, T5>.HandleCount;
                if (handle == Storage5<TMessage, T1, T2, T3, T4, T5>.HandleToSlot.Length)
                {
                    var newHandleSize = Math.Min(Storage5<TMessage, T1, T2, T3, T4, T5>.HandleToSlot.Length * 2, MAX_POOL_SIZE);
                    if (newHandleSize <= Storage5<TMessage, T1, T2, T3, T4, T5>.HandleToSlot.Length) newHandleSize = Storage5<TMessage, T1, T2, T3, T4, T5>.HandleToSlot.Length + 4;

                    Array.Resize(ref Storage5<TMessage, T1, T2, T3, T4, T5>.HandleToSlot, newHandleSize);
                    Array.Resize(ref Storage5<TMessage, T1, T2, T3, T4, T5>.HandleVersion, newHandleSize);
                    Array.Resize(ref Storage5<TMessage, T1, T2, T3, T4, T5>.HandleNextFree, newHandleSize);
                }
                Storage5<TMessage, T1, T2, T3, T4, T5>.HandleCount = handle + 1;
            }

            var version = ++Storage5<TMessage, T1, T2, T3, T4, T5>.VersionCounter;
            Storage5<TMessage, T1, T2, T3, T4, T5>.HandleVersion[handle] = version;

            // 2. Always append a new dense slot - never reuse one. A reentrant subscription
            //    (from within a currently-running Publish for this same message type) therefore
            //    always lands past that Publish's already-captured Count, deferring to the next
            //    publish regardless of handle-reuse history (MSG-01).
            var handlers = Storage5<TMessage, T1, T2, T3, T4, T5>.Handlers;
            var slotOwner = Storage5<TMessage, T1, T2, T3, T4, T5>.SlotOwner;
            var count = Storage5<TMessage, T1, T2, T3, T4, T5>.Count;

            if (count == handlers.Length)
            {
                var newSize = Math.Min(handlers.Length * 2, MAX_POOL_SIZE);
                if (newSize <= handlers.Length) newSize = handlers.Length + 4;

                var newHandlers = new Action<T1, T2, T3, T4, T5>[newSize];
                var newSlotOwner = new int[newSize];
                Array.Copy(handlers, newHandlers, count);
                Array.Copy(slotOwner, newSlotOwner, count);
                Storage5<TMessage, T1, T2, T3, T4, T5>.Handlers = handlers = newHandlers;
                Storage5<TMessage, T1, T2, T3, T4, T5>.SlotOwner = slotOwner = newSlotOwner;
            }

            handlers[count] = handler;
            slotOwner[count] = handle;
            Storage5<TMessage, T1, T2, T3, T4, T5>.HandleToSlot[handle] = count;
            Storage5<TMessage, T1, T2, T3, T4, T5>.Count = count + 1;
            Storage5<TMessage, T1, T2, T3, T4, T5>.LiveCount++;

            var token = new SubscriptionToken(handle, version);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            token.DebugName = $"{typeof(TMessage).Name}<{typeof(T1).Name},{typeof(T2).Name},{typeof(T3).Name},{typeof(T4).Name},{typeof(T5).Name}>";
            token.SubscriberTypeName = handler.Target?.GetType().Name ?? "Static";
            token.SubscribeTime = UnityEngine.Time.time;
#endif

            return token;
        }

        /// <summary>
        /// Removes a subscription by its <see cref="SubscriptionToken"/>. Safe to call with a stale
        /// or already-removed token - the version stamp makes this a no-op. Physically reclaims the
        /// slot immediately when no publish for this message type is in progress; otherwise defers
        /// that reclaiming until the outermost in-progress publish finishes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread();
#endif
            if (Storage5<TMessage, T1, T2, T3, T4, T5>.Handlers == null)
            {
                return;
            }

            var handle = token.Handle;
            if (handle < 0 || handle >= Storage5<TMessage, T1, T2, T3, T4, T5>.HandleCount)
            {
                return;
            }
            if (Storage5<TMessage, T1, T2, T3, T4, T5>.HandleVersion[handle] != token.Version)
            {
                return;
            }

            var slot = Storage5<TMessage, T1, T2, T3, T4, T5>.HandleToSlot[handle];
            Storage5<TMessage, T1, T2, T3, T4, T5>.Handlers[slot] = null;
            Storage5<TMessage, T1, T2, T3, T4, T5>.LiveCount--;
            Storage5<TMessage, T1, T2, T3, T4, T5>.HasPendingHoles = true;
            Storage5<TMessage, T1, T2, T3, T4, T5>.HandleVersion[handle] = ++Storage5<TMessage, T1, T2, T3, T4, T5>.VersionCounter;

            Storage5<TMessage, T1, T2, T3, T4, T5>.HandleNextFree[handle] = Storage5<TMessage, T1, T2, T3, T4, T5>.HandleFreeListHead;
            Storage5<TMessage, T1, T2, T3, T4, T5>.HandleFreeListHead = handle;

            if (Storage5<TMessage, T1, T2, T3, T4, T5>.PublishDepth == 0)
            {
                Storage5<TMessage, T1, T2, T3, T4, T5>.CompactIfNeeded();
            }
        }

        /// <summary>
        /// Removes a subscription by delegate reference (linear scan over active subscribers).
        /// Prefer the <see cref="SubscriptionToken"/> overload on hot paths - this one is O(n) in
        /// the current subscriber count for this message type.
        /// </summary>
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> handler) where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread();
#endif
            if (handler == null) return;
            if (Storage5<TMessage, T1, T2, T3, T4, T5>.Handlers == null)
            {
                return;
            }

            var handlers = Storage5<TMessage, T1, T2, T3, T4, T5>.Handlers;
            var count = Storage5<TMessage, T1, T2, T3, T4, T5>.Count;
            // Scan backward, not forward: with duplicate equivalent registrations (the same
            // target+method subscribed more than once), this matches C# event `-=` semantics,
            // which remove the LAST matching occurrence, not the first (MSG-03 follow-up).
            for (int i = count - 1; i >= 0; i--)
            {
                if (handlers[i] != null && handlers[i].Equals(handler))
                {
                    var handle = Storage5<TMessage, T1, T2, T3, T4, T5>.SlotOwner[i];
                    handlers[i] = null;
                    Storage5<TMessage, T1, T2, T3, T4, T5>.LiveCount--;
                    Storage5<TMessage, T1, T2, T3, T4, T5>.HasPendingHoles = true;
                    Storage5<TMessage, T1, T2, T3, T4, T5>.HandleVersion[handle] = ++Storage5<TMessage, T1, T2, T3, T4, T5>.VersionCounter;

                    Storage5<TMessage, T1, T2, T3, T4, T5>.HandleNextFree[handle] = Storage5<TMessage, T1, T2, T3, T4, T5>.HandleFreeListHead;
                    Storage5<TMessage, T1, T2, T3, T4, T5>.HandleFreeListHead = handle;

                    if (Storage5<TMessage, T1, T2, T3, T4, T5>.PublishDepth == 0)
                    {
                        Storage5<TMessage, T1, T2, T3, T4, T5>.CompactIfNeeded();
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// Removes every subscriber for a five-payload message on the application bus, and resets the
        /// live subscriber count for this message type back to zero.
        /// </summary>
        public void UnsubscribeAll<TMessage, T1, T2, T3, T4, T5>() where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread();
#endif
            if (Storage5<TMessage, T1, T2, T3, T4, T5>.Handlers == null)
            {
                return;
            }

            Array.Clear(Storage5<TMessage, T1, T2, T3, T4, T5>.Handlers, 0, Storage5<TMessage, T1, T2, T3, T4, T5>.Count);
            Storage5<TMessage, T1, T2, T3, T4, T5>.Count = 0;
            Storage5<TMessage, T1, T2, T3, T4, T5>.LiveCount = 0;
            Storage5<TMessage, T1, T2, T3, T4, T5>.HasPendingHoles = false;
            Storage5<TMessage, T1, T2, T3, T4, T5>.HandleCount = 0;
            Storage5<TMessage, T1, T2, T3, T4, T5>.HandleFreeListHead = NO_FREE_SLOT;
        }

        /// <summary>
        /// Checks whether the application bus has active subscriptions for a message type.
        /// Useful for optimization - avoid sending messages when no one is listening.
        /// </summary>
        /// <summary>
        /// Returns whether at least one subscriber is currently attached for a five-payload message.
        /// </summary>
        /// <remarks>
        /// This reflects the live subscriber count: each <c>Unsubscribe</c> overload
        /// decrements it immediately, so this correctly returns <c>false</c> again once the last
        /// subscriber for this message type has been individually removed, not only after a full
        /// <c>UnsubscribeAll</c>.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasSubscribers<TMessage, T1, T2, T3, T4, T5>() where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            if (!_usedMessageTypes.ContainsKey(typeof(TMessage)))
            {
                return false;
            }

            if (Storage5<TMessage, T1, T2, T3, T4, T5>.Handlers == null)
            {
                return false;
            }

            return Storage5<TMessage, T1, T2, T3, T4, T5>.LiveCount > 0;
        }

    }
}

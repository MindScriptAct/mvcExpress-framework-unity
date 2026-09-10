// Arity variant 12 of MvcMessageBus - see MvcMessageBus.Params00.cs for the template pattern.
using mvcExpress.Internal.Interfaces;
using System;
using System.Runtime.CompilerServices;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using mvcExpress.Plugins;
using mvcExpress.Logging;
#endif
namespace mvcExpress.Internal.Messaging
{
    public sealed partial class MvcMessageBus : IMessageBus, IDisposable
    {
        // Generic clear methods for each storage arity
        private void ClearStorage12(Type messageType, Type[] args)
        {
            var storageType = typeof(Storage12<,,,,,,,,,,,,>).MakeGenericType(messageType, args[0], args[1], args[2], args[3], args[4], args[5], args[6], args[7], args[8], args[9], args[10], args[11]);
            ClearStorageSlot(storageType);
        }

        // Storage Class for 12 parameters
        private static class Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>
        {
            // Handlers[slot] = the handler delegate. Dense: no null holes once compaction has run.
            internal static Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>[] Handlers;
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
                            Handlers = new Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>[INITIAL_CAPACITY];
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
        /// Publishes a twelve-payload message to every subscriber on the application bus, in subscribe order.
        /// Main-thread-only; validated in Editor/Development builds. Zero allocation for struct
        /// messages and struct payload types.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10, T11 p11, T12 p12) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread();
#endif
            if (Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.Handlers == null)
            {
                return;
            }

            var count = Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.Count;

            Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.PublishDepth++;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    // Read the array reference fresh every iteration - see Params00.cs's Publish
                    // for why (a reentrant Subscribe that grows storage mid-publish replaces the
                    // Handlers field with a new array object; a stale cached reference here would
                    // make a further reentrant Unsubscribe invisible to this loop).
                    var handler = Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.Handlers[i];
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
                            handler(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12);
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
                        handler(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12);
#endif
                    }
                }
            }
            finally
            {
                if (--Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.PublishDepth == 0)
                {
                    Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.CompactIfNeeded();
                }
            }
        }

        /// <summary>
        /// Subscribes a handler to a twelve-payload message on the application bus.
        /// </summary>
        /// <returns>A <see cref="SubscriptionToken"/> for removal via <c>Unsubscribe</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            MessageValidationCache.ValidateMessageType(typeof(TMessage));
            _usedMessageTypes.TryAdd(typeof(TMessage), 0);
            Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.EnsureCapacity();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread();
#endif
            return SubscribeInternal<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(handler);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private SubscriptionToken SubscribeInternal<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>
        {
            // 1. Allocate a handle - reuse a freed one if available, otherwise grow the handle table.
            //    This is fully independent of where the handler ends up in Handlers[] below, so
            //    reusing a handle number can never reintroduce the reentrant-subscribe hazard
            //    MSG-01 fixed (that hazard was about reusing a DATA slot, which we no longer do).
            int handle;
            var freeHandle = Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleFreeListHead;
            if (freeHandle != NO_FREE_SLOT)
            {
                handle = freeHandle;
                Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleFreeListHead = Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleNextFree[freeHandle];
            }
            else
            {
                handle = Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleCount;
                if (handle == Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleToSlot.Length)
                {
                    var newHandleSize = Math.Min(Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleToSlot.Length * 2, MAX_POOL_SIZE);
                    if (newHandleSize <= Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleToSlot.Length) newHandleSize = Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleToSlot.Length + 4;

                    Array.Resize(ref Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleToSlot, newHandleSize);
                    Array.Resize(ref Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleVersion, newHandleSize);
                    Array.Resize(ref Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleNextFree, newHandleSize);
                }
                Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleCount = handle + 1;
            }

            var version = ++Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.VersionCounter;
            Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleVersion[handle] = version;

            // 2. Always append a new dense slot - never reuse one. A reentrant subscription
            //    (from within a currently-running Publish for this same message type) therefore
            //    always lands past that Publish's already-captured Count, deferring to the next
            //    publish regardless of handle-reuse history (MSG-01).
            var handlers = Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.Handlers;
            var slotOwner = Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.SlotOwner;
            var count = Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.Count;

            if (count == handlers.Length)
            {
                var newSize = Math.Min(handlers.Length * 2, MAX_POOL_SIZE);
                if (newSize <= handlers.Length) newSize = handlers.Length + 4;

                var newHandlers = new Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>[newSize];
                var newSlotOwner = new int[newSize];
                Array.Copy(handlers, newHandlers, count);
                Array.Copy(slotOwner, newSlotOwner, count);
                Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.Handlers = handlers = newHandlers;
                Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.SlotOwner = slotOwner = newSlotOwner;
            }

            handlers[count] = handler;
            slotOwner[count] = handle;
            Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleToSlot[handle] = count;
            Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.Count = count + 1;
            Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.LiveCount++;

            var token = new SubscriptionToken(handle, version);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            token.DebugName = $"{typeof(TMessage).Name}<{typeof(T1).Name},{typeof(T2).Name},{typeof(T3).Name},{typeof(T4).Name},{typeof(T5).Name},{typeof(T6).Name},{typeof(T7).Name},{typeof(T8).Name},{typeof(T9).Name},{typeof(T10).Name},{typeof(T11).Name},{typeof(T12).Name}>";
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
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread();
#endif
            if (Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.Handlers == null)
            {
                return;
            }

            var handle = token.Handle;
            if (handle < 0 || handle >= Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleCount)
            {
                return;
            }
            if (Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleVersion[handle] != token.Version)
            {
                return;
            }

            var slot = Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleToSlot[handle];
            Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.Handlers[slot] = null;
            Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.LiveCount--;
            Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HasPendingHoles = true;
            Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleVersion[handle] = ++Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.VersionCounter;

            Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleNextFree[handle] = Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleFreeListHead;
            Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleFreeListHead = handle;

            if (Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.PublishDepth == 0)
            {
                Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.CompactIfNeeded();
            }
        }

        /// <summary>
        /// Removes a subscription by delegate reference (linear scan over active subscribers).
        /// Prefer the <see cref="SubscriptionToken"/> overload on hot paths - this one is O(n) in
        /// the current subscriber count for this message type.
        /// </summary>
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread();
#endif
            if (handler == null) return;
            if (Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.Handlers == null)
            {
                return;
            }

            var handlers = Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.Handlers;
            var count = Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.Count;
            // Scan backward, not forward: with duplicate equivalent registrations (the same
            // target+method subscribed more than once), this matches C# event `-=` semantics,
            // which remove the LAST matching occurrence, not the first (MSG-03 follow-up).
            for (int i = count - 1; i >= 0; i--)
            {
                if (handlers[i] != null && handlers[i].Equals(handler))
                {
                    var handle = Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.SlotOwner[i];
                    handlers[i] = null;
                    Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.LiveCount--;
                    Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HasPendingHoles = true;
                    Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleVersion[handle] = ++Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.VersionCounter;

                    Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleNextFree[handle] = Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleFreeListHead;
                    Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleFreeListHead = handle;

                    if (Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.PublishDepth == 0)
                    {
                        Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.CompactIfNeeded();
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// Removes every subscriber for a twelve-payload message on the application bus, and resets the
        /// live subscriber count for this message type back to zero.
        /// </summary>
        public void UnsubscribeAll<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>() where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread();
#endif
            if (Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.Handlers == null)
            {
                return;
            }

            Array.Clear(Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.Handlers, 0, Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.Count);
            Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.Count = 0;
            Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.LiveCount = 0;
            Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HasPendingHoles = false;
            Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleCount = 0;
            Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.HandleFreeListHead = NO_FREE_SLOT;
        }

        /// <summary>
        /// Checks whether the application bus has active subscriptions for a message type.
        /// Useful for optimization - avoid sending messages when no one is listening.
        /// </summary>
        /// <summary>
        /// Returns whether at least one subscriber is currently attached for a twelve-payload message.
        /// </summary>
        /// <remarks>
        /// This reflects the live subscriber count: each <c>Unsubscribe</c> overload
        /// decrements it immediately, so this correctly returns <c>false</c> again once the last
        /// subscriber for this message type has been individually removed, not only after a full
        /// <c>UnsubscribeAll</c>.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasSubscribers<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>() where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>
        {
            if (!_usedMessageTypes.ContainsKey(typeof(TMessage)))
            {
                return false;
            }

            if (Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.Handlers == null)
            {
                return false;
            }

            return Storage12<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>.LiveCount > 0;
        }

    }
}

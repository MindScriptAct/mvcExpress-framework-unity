// Arity-0 partial of MvcMessageBus - 0-parameter message overloads.
// This file is the canonical template for all arity variants (Params01-Params12).
//
// Storage model: Handlers[] is a dense, gap-free array that Publish walks directly - no holes,
// ever, once Unsubscribe's compaction has had a chance to run. A SubscriptionToken's Handle field
// is a stable identity, independent of where the handler currently sits in Handlers[] - a small
// indirection table (HandleToSlot/SlotOwner) tracks the mapping and gets fixed up whenever
// compaction moves something. This is what lets Unsubscribe physically reclaim space (unlike the
// older free-list design) without invalidating any OTHER subscriber's already-issued token: their
// Handle never changes, only where it happens to point.
//
// Compaction is deferred while PublishDepth > 0 (a Publish for this message type is on the call
// stack, possibly several frames deep via nested/reentrant publishes) - physically moving a live
// handler mid-iteration would scramble Publish's own read position. Unsubscribe still nulls the
// slot and frees the handle immediately either way (both are always safe - see MSG-01), it just
// leaves the actual compaction for whichever Publish call's `finally` next brings the depth back
// to zero.
//
// Pattern repeated in each variant:
//   - StorageN<TMessage[, T1..TN]>  : dense handler array + handle indirection table
//   - EnsureCapacity()              : initializes storage on first subscription
//   - CompactIfNeeded()             : closes deferred holes once it's safe to move things
//   - Publish<TMessage[, T1..TN]>   : iterate handlers and invoke (zero alloc, main thread only)
//   - Subscribe<TMessage[, T1..TN]> : allocate/reuse a handle, append a dense slot
//   - SubscribeInternal             : inner unsynchronised subscribe (main-thread-only, validated in Editor/Dev)
//   - Unsubscribe by token          : validate handle+version, null the slot, compact if safe
//   - Unsubscribe by delegate       : linear scan, then the same removal path
//   - UnsubscribeAll                : reset everything for this message type
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
        // Clears all handlers for Storage0<TMessage>.
        // Called from ClearStorageForMessageType during Dispose.
        private void ClearStorage0(Type messageType)
        {
            var storageType = typeof(Storage0<>).MakeGenericType(messageType);
            ClearStorageSlot(storageType);
        }

        /// <summary>
        /// Static-per-generic-type handler storage for 0-parameter messages on the application bus.
        /// </summary>
        private static class Storage0<TMessage> where TMessage : IMessage
        {
            // Handlers[slot] = the handler delegate. Dense: no null holes once compaction has run.
            internal static Action[] Handlers;
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
                            Handlers = new Action[INITIAL_CAPACITY];
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
        /// Publishes a no-payload message to every subscriber on the application bus, in subscribe order.
        /// Main-thread-only; validated in Editor/Development builds. Zero allocation for struct
        /// messages and struct payload types.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage>() where TMessage : IMessage
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread();
#endif

            if (Storage0<TMessage>.Handlers == null)
            {
                return;
            }

            var count = Storage0<TMessage>.Count;

            Storage0<TMessage>.PublishDepth++;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    // Read the array reference fresh every iteration, not once before the loop -
                    // a reentrant Subscribe that grows storage (array full to capacity) replaces
                    // Storage0<TMessage>.Handlers with a brand new array object. A stale cached
                    // reference here would keep reading the old, orphaned array, so a further
                    // reentrant Unsubscribe (which always writes into the CURRENT array) would be
                    // invisible to this loop - it could still invoke a handler already removed
                    // earlier in this same Publish call. Count itself stays a one-time snapshot
                    // (unrelated fix, MSG-01) - only the array reference needs to stay live.
                    var handler = Storage0<TMessage>.Handlers[i];
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
                            handler();
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
                        handler();
#endif
                    }
                }
            }
            finally
            {
                if (--Storage0<TMessage>.PublishDepth == 0)
                {
                    Storage0<TMessage>.CompactIfNeeded();
                }
            }
        }

        /// <summary>
        /// Subscribes a handler to a no-payload message on the application bus.
        /// </summary>
        /// <returns>A <see cref="SubscriptionToken"/> for removal via <c>Unsubscribe</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage>(Action handler) where TMessage : IMessage
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            // IMPROVEMENT 5: Validate message type (Editor/Development only)
            MessageValidationCache.ValidateMessageType(typeof(TMessage));

            // IMPROVEMENT 6: Thread-safe message type tracking
            _usedMessageTypes.TryAdd(typeof(TMessage), 0);

            Storage0<TMessage>.EnsureCapacity();

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread();
#endif
            return SubscribeInternal<TMessage>(handler);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private SubscriptionToken SubscribeInternal<TMessage>(Action handler) where TMessage : IMessage
        {
            // 1. Allocate a handle - reuse a freed one if available, otherwise grow the handle table.
            //    This is fully independent of where the handler ends up in Handlers[] below, so
            //    reusing a handle number can never reintroduce the reentrant-subscribe hazard
            //    MSG-01 fixed (that hazard was about reusing a DATA slot, which we no longer do).
            int handle;
            var freeHandle = Storage0<TMessage>.HandleFreeListHead;
            if (freeHandle != NO_FREE_SLOT)
            {
                handle = freeHandle;
                Storage0<TMessage>.HandleFreeListHead = Storage0<TMessage>.HandleNextFree[freeHandle];
            }
            else
            {
                handle = Storage0<TMessage>.HandleCount;
                if (handle == Storage0<TMessage>.HandleToSlot.Length)
                {
                    var newHandleSize = Math.Min(Storage0<TMessage>.HandleToSlot.Length * 2, MAX_POOL_SIZE);
                    if (newHandleSize <= Storage0<TMessage>.HandleToSlot.Length) newHandleSize = Storage0<TMessage>.HandleToSlot.Length + 4;

                    Array.Resize(ref Storage0<TMessage>.HandleToSlot, newHandleSize);
                    Array.Resize(ref Storage0<TMessage>.HandleVersion, newHandleSize);
                    Array.Resize(ref Storage0<TMessage>.HandleNextFree, newHandleSize);
                }
                Storage0<TMessage>.HandleCount = handle + 1;
            }

            var version = ++Storage0<TMessage>.VersionCounter;
            Storage0<TMessage>.HandleVersion[handle] = version;

            // 2. Always append a new dense slot - never reuse one. A reentrant subscription
            //    (from within a currently-running Publish for this same message type) therefore
            //    always lands past that Publish's already-captured Count, deferring to the next
            //    publish regardless of handle-reuse history (MSG-01).
            var handlers = Storage0<TMessage>.Handlers;
            var slotOwner = Storage0<TMessage>.SlotOwner;
            var count = Storage0<TMessage>.Count;

            if (count == handlers.Length)
            {
                var newSize = Math.Min(handlers.Length * 2, MAX_POOL_SIZE);
                if (newSize <= handlers.Length) newSize = handlers.Length + 4;

                var newHandlers = new Action[newSize];
                var newSlotOwner = new int[newSize];
                Array.Copy(handlers, newHandlers, count);
                Array.Copy(slotOwner, newSlotOwner, count);
                Storage0<TMessage>.Handlers = handlers = newHandlers;
                Storage0<TMessage>.SlotOwner = slotOwner = newSlotOwner;
            }

            handlers[count] = handler;
            slotOwner[count] = handle;
            Storage0<TMessage>.HandleToSlot[handle] = count;
            Storage0<TMessage>.Count = count + 1;
            Storage0<TMessage>.LiveCount++;

            var token = new SubscriptionToken(handle, version);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            token.DebugName = typeof(TMessage).Name;
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
        public void Unsubscribe<TMessage>(SubscriptionToken token) where TMessage : IMessage
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread();
#endif
            if (Storage0<TMessage>.Handlers == null)
            {
                return;
            }

            var handle = token.Handle;
            if (handle < 0 || handle >= Storage0<TMessage>.HandleCount)
            {
                return;
            }
            if (Storage0<TMessage>.HandleVersion[handle] != token.Version)
            {
                return;
            }

            var slot = Storage0<TMessage>.HandleToSlot[handle];
            Storage0<TMessage>.Handlers[slot] = null;
            Storage0<TMessage>.LiveCount--;
            Storage0<TMessage>.HasPendingHoles = true;
            Storage0<TMessage>.HandleVersion[handle] = ++Storage0<TMessage>.VersionCounter;

            Storage0<TMessage>.HandleNextFree[handle] = Storage0<TMessage>.HandleFreeListHead;
            Storage0<TMessage>.HandleFreeListHead = handle;

            if (Storage0<TMessage>.PublishDepth == 0)
            {
                Storage0<TMessage>.CompactIfNeeded();
            }
        }

        /// <summary>
        /// Removes a subscription by delegate reference (linear scan over active subscribers).
        /// Prefer the <see cref="SubscriptionToken"/> overload on hot paths - this one is O(n) in
        /// the current subscriber count for this message type.
        /// </summary>
        public void Unsubscribe<TMessage>(Action handler) where TMessage : IMessage
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread();
#endif
            if (handler == null) return;
            if (Storage0<TMessage>.Handlers == null)
            {
                return;
            }

            var handlers = Storage0<TMessage>.Handlers;
            var count = Storage0<TMessage>.Count;
            // Scan backward, not forward: with duplicate equivalent registrations (the same
            // target+method subscribed more than once), this matches C# event `-=` semantics,
            // which remove the LAST matching occurrence, not the first (MSG-03 follow-up).
            for (int i = count - 1; i >= 0; i--)
            {
                if (handlers[i] != null && handlers[i].Equals(handler))
                {
                    var handle = Storage0<TMessage>.SlotOwner[i];
                    handlers[i] = null;
                    Storage0<TMessage>.LiveCount--;
                    Storage0<TMessage>.HasPendingHoles = true;
                    Storage0<TMessage>.HandleVersion[handle] = ++Storage0<TMessage>.VersionCounter;

                    Storage0<TMessage>.HandleNextFree[handle] = Storage0<TMessage>.HandleFreeListHead;
                    Storage0<TMessage>.HandleFreeListHead = handle;

                    if (Storage0<TMessage>.PublishDepth == 0)
                    {
                        Storage0<TMessage>.CompactIfNeeded();
                    }
                    return;
                }
            }
        }

        /// <summary>
        /// Removes every subscriber for a no-payload message on the application bus, and resets the
        /// live subscriber count for this message type back to zero.
        /// </summary>
        public void UnsubscribeAll<TMessage>() where TMessage : IMessage
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread();
#endif
            if (Storage0<TMessage>.Handlers == null)
            {
                return;
            }

            Array.Clear(Storage0<TMessage>.Handlers, 0, Storage0<TMessage>.Count);
            Storage0<TMessage>.Count = 0;
            Storage0<TMessage>.LiveCount = 0;
            Storage0<TMessage>.HasPendingHoles = false;
            Storage0<TMessage>.HandleCount = 0;
            Storage0<TMessage>.HandleFreeListHead = NO_FREE_SLOT;
        }

    }
}

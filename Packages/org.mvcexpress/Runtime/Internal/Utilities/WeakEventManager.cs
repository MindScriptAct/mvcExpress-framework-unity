﻿using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using mvcExpress.Logging;

namespace mvcExpress.Internal.Utilities
{
    /// <summary>
    /// Weak-reference event publisher that prevents subscribers from being kept alive solely
    /// by virtue of being subscribed to this event.
    /// </summary>
    /// <remarks>
    /// Why this exists:
    /// Standard C# events (<c>+=</c>) hold a strong reference to the subscriber delegate. If a
    /// long-lived publisher (e.g. a global proxy or the MvcFacade singleton) raises events that short-lived
    /// objects subscribe to without cleaning up, those objects are leaked. This manager stores handlers
    /// as <see cref="WeakReference{T}"/> so subscribers can be GC'd naturally; dead references are
    /// purged lazily during <see cref="Raise"/> or explicitly via <see cref="Cleanup"/>.
    ///
    /// Reentrancy:
    /// If <see cref="Raise"/> is called recursively (e.g. a handler fires an event that raises the same
    /// event again), subsequent calls are queued and drained after the current pass finishes. This
    /// prevents stack overflow and ensures handlers see a consistent subscriber list.
    ///
    /// Dead-reference cleanup:
    /// A counter increments each time a dead reference is encountered during <see cref="Raise"/>.
    /// When it reaches <c>CleanupThreshold</c> (10), <see cref="Cleanup"/> is called automatically.
    /// This amortises cleanup cost across multiple raises without requiring manual housekeeping.
    ///
    /// Thread safety: all operations are guarded by a private lock.
    /// Internal - used by framework infrastructure (e.g. transient dependency notifications).
    /// </remarks>
    /// <typeparam name="TEventArgs">Event argument type passed to handlers.</typeparam>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class WeakEventManager<TEventArgs>
    {
        // Weak references to handlers. Entries can become dead if the delegate's target is GC'd.
        private readonly List<WeakReference<Action<TEventArgs>>> _handlers = new List<WeakReference<Action<TEventArgs>>>();
        private readonly object _lock = new object();
        // Reentrancy guard - true while a Raise loop is executing.
        private bool _isRaising;
        // Accumulates dead-reference count across Raise() calls; triggers Cleanup() at threshold.
        private int _deadCount;
        private const int CleanupThreshold = 10;

        // Holds raise calls that arrived while _isRaising was true. Drained after the current pass.
        private readonly Queue<TEventArgs> _pendingRaises = new Queue<TEventArgs>();

        /// <summary>
        /// Subscribe with weak reference (auto-removed when GC'd). Idempotent — subscribing
        /// the same handler a second time is a no-op.
        /// </summary>
        public void Subscribe(Action<TEventArgs> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            lock (_lock)
            {
                for (int i = 0; i < _handlers.Count; i++)
                {
                    if (_handlers[i].TryGetTarget(out var existing) && existing == handler)
                        return;
                }
                _handlers.Add(new WeakReference<Action<TEventArgs>>(handler));
            }
        }

        /// <summary>
        /// Explicitly unsubscribe a handler. Removes all matching entries (value equality,
        /// not reference identity) so callers do not need to cache the original delegate object.
        /// </summary>
        public void Unsubscribe(Action<TEventArgs> handler)
        {
            if (handler == null) return;

            lock (_lock)
            {
                for (int i = _handlers.Count - 1; i >= 0; i--)
                {
                    if (_handlers[i].TryGetTarget(out var target) && target == handler)
                        _handlers.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// Raise event to all alive subscribers.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Raise(TEventArgs args)
        {
            lock (_lock)
            {
                // If a raise is already in progress, queue this invocation so events are not dropped.
                if (_isRaising)
                {
                    _pendingRaises.Enqueue(args);
                    return;
                }

                _isRaising = true;
            }

            try
            {
                while (true)
                {
                    WeakReference<Action<TEventArgs>>[] snapshot;
                    lock (_lock)
                    {
                        snapshot = _handlers.ToArray();
                    }

                    int deadCount = 0;

                    for (int i = 0; i < snapshot.Length; i++)
                    {
                        if (snapshot[i].TryGetTarget(out var handler))
                        {
                            try
                            {
                                handler(args);
                            }
                            catch (Exception ex)
                            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || UNITY_INCLUDE_TESTS
                                MvcDebug.LogError($"Error in weak event handler: {ex.Message}");
#endif
                            }
                        }
                        else
                        {
                            deadCount++;
                        }
                    }

                    lock (_lock)
                    {
                        _deadCount += deadCount;
                        if (_deadCount >= CleanupThreshold)
                        {
                            Cleanup();
                        }

                        if (_pendingRaises.Count == 0)
                        {
                            return;
                        }

                        args = _pendingRaises.Dequeue();
                    }
                }
            }
            finally
            {
                lock (_lock)
                {
                    _isRaising = false;
                }
            }
        }

        /// <summary>
        /// Remove dead (garbage collected) handlers.
        /// </summary>
        public void Cleanup()
        {
            lock (_lock)
            {
                for (int i = _handlers.Count - 1; i >= 0; i--)
                {
                    if (!_handlers[i].TryGetTarget(out _))
                    {
                        _handlers.RemoveAt(i);
                    }
                }
                _deadCount = 0;
            }
        }

        /// <summary>
        /// Clear all handlers immediately.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                _handlers.Clear();
                _deadCount = 0;
            }
        }

        /// <summary>
        /// Get count of alive subscribers.
        /// </summary>
        public int AliveCount
        {
            get
            {
                lock (_lock)
                {
                    int count = 0;
                    for (int i = 0; i < _handlers.Count; i++)
                    {
                        if (_handlers[i].TryGetTarget(out _))
                        {
                            count++;
                        }
                    }
                    return count;
                }
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using mvcExpress.Logging;
using System.Threading;

namespace mvcExpress.Internal.Utilities
{
    /// <summary>
    /// Fixed-capacity object pool used to reuse command instances and avoid GC allocations for
    /// high-frequency commands.
    /// </summary>
    /// <remarks>
    /// Why this exists:
    /// Commands are created and discarded on every message publish. For hot-path commands
    /// (e.g. per-frame input handlers), allocating a new instance each time creates GC pressure.
    /// This pool reuses instances: <see cref="Get"/> returns an existing instance if available;
    /// <see cref="Return"/> resets and re-queues it; excess instances are discarded without pooling.
    ///
    /// Key invariants:
    /// <list type="bullet">
    ///   <item><description>
    ///     <see cref="Get"/> always returns a non-null instance (creates via factory when pool is empty).
    ///     Callers do not need to null-check the result.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="Return"/> with a null argument is silently ignored to keep call sites clean.
    ///   </description></item>
    ///   <item><description>
    ///     When <see cref="MaxSize"/> is 0 the pool is disabled: <see cref="Return"/> disposes and
    ///     discards every object immediately, and <see cref="Get"/> always creates fresh instances.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="IDisposable"/> objects are disposed when discarded (pool full or pool disabled).
    ///   </description></item>
    /// </list>
    ///
    /// Thread safety: main-thread-only, unlocked. This used to lock every operation on the (unverified) theory
    /// that async commands might return to the pool from a background thread. They don't, and
    /// nothing else in this framework legitimately does either: command dispatch triggered via
    /// <c>Publish</c> is validated main-thread-only (<c>MvcMessageBus.ValidateMainThread</c>), and
    /// <c>CommandAsync.ExecuteAsync</c> continuations resume on the main thread via Unity's
    /// <c>SynchronizationContext</c> - the framework relies on and documents exactly this
    /// (async-commands.md: "the framework does not execute the task on a background thread"; a
    /// bare <c>Messenger.Publish</c> after an <c>await</c> is documented as safe for the same
    /// reason). <c>Commander.Run</c>/<c>RunAsync</c>/<c>CreatePool</c> - direct command execution,
    /// not triggered by a message - are the one path that used to have no such guarantee; they now
    /// validate main-thread-use themselves before ever reaching a pool (PERF-07 follow-up), so
    /// there is no supported path left that reaches <see cref="Get"/>/<see cref="Return"/> from a
    /// non-main thread, and the lock was pure overhead on every single dispatch, paid to guard
    /// against a scenario the framework's own design now actually prevents rather than merely
    /// assumed. In Editor/Development builds, every public method here also asserts it is running
    /// on the framework's one authoritative main thread (<c>MvcMessageBus.MainThreadId</c>, not a
    /// thread ID captured by this instance at construction - see <c>ValidateMainThread</c> below
    /// for why a per-instance capture was itself a bug) and throws immediately if not, the same
    /// fail-loud-in-dev/zero-cost-in-release shape <c>MvcMessageBus.ValidateMainThread</c> already
    /// uses. Production builds pay nothing for this check.
    ///
    /// Internal - application code should never need to interact with this class directly.
    /// </remarks>
    /// <typeparam name="T">Pooled object type. Must be a reference type.</typeparam>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class BoundedObjectPool<T> where T : class
    {
        // LIFO stack - recently-used instances stay warm in cache.
        private readonly Stack<T> _pool;
        // Creates a fresh instance when the pool is empty.
        private readonly Func<T> _factory;
        // Optional reset action called before an object is returned to the pool.
        // Commands use this to clear their state before the next execution.
        private readonly Action<T> _reset;
        // 0 = pooling disabled (all returned objects are discarded immediately).
        private uint _maxSize;

        // _currentSize is used by core logic (capacity check) - always present.
        private int _currentSize;

        // Diagnostic-only counters, compiled out of release players: nothing in a release build
        // reads them (GetPoolStatistics/GetAggregatePoolStatistics on MvcCommandProcessor are
        // themselves compiled out there via `#if UNITY_EDITOR || MVC_LOGGING`), so paying for
        // their upkeep on every Get/Return would be pure overhead on the dispatch path.
        // UNITY_INCLUDE_TESTS is included so Player test builds - which call GetStatistics()
        // directly, bypassing that gate - still get real numbers. Outside those configurations the
        // fields don't exist and simply read back as 0. Plain increments, not Interlocked - this
        // whole class is main-thread-only now (see the class remarks above), so there is no
        // concurrent writer to guard against.
#if UNITY_EDITOR || DEVELOPMENT_BUILD || UNITY_INCLUDE_TESTS
        private int _totalCreated;
        private int _totalReturned;
        private int _totalDiscarded;
        private int _totalGets;
        private int _pooledGets;
#endif

        /// <summary>
        /// Create a bounded object pool.
        /// </summary>
        /// <param name="factory">Factory to create new instances.</param>
        /// <param name="reset">Optional reset action before pooling.</param>
        /// <param name="maxSize">Maximum pool size (0=disabled, positive=fixed cap).</param>
        /// <param name="initialCapacity">Initial capacity hint.</param>
        public BoundedObjectPool(Func<T> factory, Action<T> reset = null, uint maxSize = 16, int initialCapacity = 4)
        {
            if (factory == null) throw new ArgumentNullException(nameof(factory));

            _factory = factory;
            _reset = reset;
            _maxSize = maxSize;

            // Choose an initial capacity that is at least 1, and not larger than the maxSize when bounded.
            int capacity = Math.Max(1, Math.Min(initialCapacity, maxSize > 0u ? (int)maxSize : initialCapacity));
            _pool = new Stack<T>(capacity);
        }

        /// <summary>
        /// Current maximum pool size.
        /// </summary>
        public uint MaxSize => _maxSize;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Throws <see cref="InvalidOperationException"/> when called from a thread other than the
        /// framework's authoritative main thread (<see cref="mvcExpress.Internal.Messaging.MvcMessageBus.MainThreadId"/>).
        /// Active only in Editor/Development builds - zero overhead in production. See the class
        /// remarks for why main-thread-only is the correct contract here, not a lock.
        /// </summary>
        /// <remarks>
        /// Deliberately compares against the shared framework main-thread ID, not a thread ID this
        /// instance captured at its own construction (PERF-07 follow-up - that was the actual bug):
        /// a pool is created lazily, on whichever thread first calls
        /// <c>MvcCommandProcessor.GetOrCreatePool</c> for its command type. Every path that reaches
        /// that method through a published message is already main-thread-only (behind
        /// <c>MvcMessageBus.Publish</c>'s own validation), but direct execution
        /// (<c>Commander.Run</c>/<c>RunAsync</c>/<c>CreatePool</c>) used to have no such guarantee -
        /// if one of those happened to be called from a background thread before the command was
        /// ever bound, THIS pool's owner would lock onto that worker thread, and the real main
        /// thread would be rejected forever after for that command type. Comparing against the
        /// shared authoritative ID instead means every pool agrees on what "main thread" means,
        /// independent of which thread happened to construct any particular one. Those direct-
        /// execution entry points are now also validated before they ever reach a pool, so this is
        /// defense in depth rather than the only guard.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ValidateMainThread(string memberName)
        {
            int currentId = Thread.CurrentThread.ManagedThreadId;
            int mainThreadId = mvcExpress.Internal.Messaging.MvcMessageBus.MainThreadId;
            if (currentId != mainThreadId)
            {
                throw new InvalidOperationException(
                    $"BoundedObjectPool<{typeof(T).Name}>.{memberName} was called from a background " +
                    $"thread (thread ID {currentId}; main thread ID {mainThreadId}).\n" +
                    "Command pools are main-thread-only: dispatch via Publish or Commander.Run is " +
                    "main-thread, and CommandAsync.ExecuteAsync continuations resume on the main " +
                    "thread via Unity's SynchronizationContext (see async-commands.md) - there is no " +
                    "supported case where pool access needs to happen off-thread. If you're hitting " +
                    "this, something is calling command-dispatch machinery directly from a background " +
                    "thread or from inside a Task.Run continuation without awaiting back to the main " +
                    "thread first."
                );
            }
        }
#endif

        /// <summary>
        /// Get an object from pool or create new if empty.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Get()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread(nameof(Get));
#endif
#if UNITY_EDITOR || DEVELOPMENT_BUILD || UNITY_INCLUDE_TESTS
            _totalGets++;
#endif
            if (_pool.Count > 0)
            {
                _currentSize--;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || UNITY_INCLUDE_TESTS
                _pooledGets++;
#endif
                return _pool.Pop();
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD || UNITY_INCLUDE_TESTS
            _totalCreated++;
#endif
            return _factory();
        }

        /// <summary>
        /// Return an object to pool (discards if pool is full).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Return(T obj)
        {
            if (obj == null) return; // ignore nulls to keep the pool consistent.

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread(nameof(Return));
#endif

#if UNITY_EDITOR || DEVELOPMENT_BUILD || UNITY_INCLUDE_TESTS
            _totalReturned++;
#endif

            // No pooling mode: everything is discarded on return.
            if (_maxSize == 0)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || UNITY_INCLUDE_TESTS
                _totalDiscarded++;
#endif
                DisposeIfNeeded(obj);
                return;
            }

            // Reset the object before putting it back into circulation.
            _reset?.Invoke(obj);

            // If we have a positive max size and the pool is already full, discard.
            if (_maxSize > 0u && _currentSize >= (int)_maxSize)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || UNITY_INCLUDE_TESTS
                _totalDiscarded++;
#endif
                DisposeIfNeeded(obj);
                return;
            }

            // Otherwise push into the pool for reuse.
            _pool.Push(obj);
            _currentSize++;
        }

        /// <summary>
        /// Resize the pool to a new maximum size.
        /// If new size is smaller than current pooled count, excess objects are discarded.
        /// If new size is larger, pool can grow naturally up to the new limit.
        /// </summary>
        /// <param name="newMaxSize">New maximum pool size (0=disabled, positive=fixed cap).</param>
        public void Resize(uint newMaxSize)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread(nameof(Resize));
#endif
            _maxSize = newMaxSize;

            // If shrinking and we have more objects than the new max, trim excess
            if (_currentSize > (int)newMaxSize)
            {
                int toRemove = _currentSize - (int)newMaxSize;
                while (toRemove > 0 && _pool.Count > 0)
                {
                    var obj = _pool.Pop();
                    _currentSize--;
                    DisposeIfNeeded(obj);
#if UNITY_EDITOR || DEVELOPMENT_BUILD || UNITY_INCLUDE_TESTS
                    _totalDiscarded++;
#endif
                    toRemove--;
                }
            }
        }

        /// <summary>
        /// Clear all pooled objects.
        /// </summary>
        public void Clear()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread(nameof(Clear));
#endif
            while (_pool.Count > 0)
            {
                var obj = _pool.Pop();
                DisposeIfNeeded(obj);
            }
            _currentSize = 0;
        }

        /// <summary>
        /// Trim pool to target size.
        /// </summary>
        public void TrimTo(int targetSize)
        {
            if (targetSize < 0) throw new ArgumentOutOfRangeException(nameof(targetSize));

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread(nameof(TrimTo));
#endif
            if (_pool.Count <= targetSize)
            {
                return;
            }

            var temp = new Stack<T>(_pool.Count);
            while (_pool.Count > 0)
            {
                temp.Push(_pool.Pop());
            }

            _currentSize = 0;
            var toDiscard = Math.Max(0, temp.Count - targetSize);

            while (temp.Count > 0)
            {
                var obj = temp.Pop();
                if (toDiscard > 0)
                {
                    DisposeIfNeeded(obj);
#if UNITY_EDITOR || DEVELOPMENT_BUILD || UNITY_INCLUDE_TESTS
                    _totalDiscarded++;
#endif
                    toDiscard--;
                    continue;
                }

                _pool.Push(obj);
                _currentSize++;
            }
        }

        /// <summary>
        /// Get a point-in-time snapshot of pool statistics.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PoolStatistics GetStatistics()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            ValidateMainThread(nameof(GetStatistics));
#endif
            return new PoolStatistics
            {
                CurrentSize = _currentSize,
                MaxSize = _maxSize,
#if UNITY_EDITOR || DEVELOPMENT_BUILD || UNITY_INCLUDE_TESTS
                TotalCreated = _totalCreated,
                TotalReturned = _totalReturned,
                TotalDiscarded = _totalDiscarded,
                TotalGets = _totalGets,
                PooledGets = _pooledGets,
#else
                TotalCreated = 0,
                TotalReturned = 0,
                TotalDiscarded = 0,
                TotalGets = 0,
                PooledGets = 0,
#endif
                PoolCount = _pool.Count
            };
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DisposeIfNeeded(T obj)
        {
            // A discarded command's own scoped dependencies (Scoped lifetime redesign - see
            // InternalSpecs/Architecture/SCOPED_LIFETIME_REDESIGN.md) must be released whenever
            // the command instance itself is actually thrown away here, regardless of whether the
            // concrete command implements IDisposable - unlike the block below, this must run
            // unconditionally.
            if (obj is mvcExpress.MvcCommandBase command)
            {
                command.ReleaseOwnedScopedResources();
            }

            if (obj is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    // A disposal exception must be visible even in a clean release build, unlike
                    // routine diagnostic logging, so it also always goes through Unity's own
                    // Debug.LogException on top of MvcDebug.LogError's own [Conditional] stripping.
                    MvcDebug.LogError($"Error disposing pooled object: {ex.Message}");
                    UnityEngine.Debug.LogException(ex);
                }
            }
        }

        /// <summary>
        /// Pool statistics for diagnostics.
        /// </summary>
        public struct PoolStatistics
        {
            /// <summary>Number of objects currently pooled.</summary>
            public int CurrentSize;

            /// <summary>Configured maximum pool size.</summary>
            public uint MaxSize;

            /// <summary>Total objects created by factory.</summary>
            public int TotalCreated;

            /// <summary>Total objects returned to pool.</summary>
            public int TotalReturned;

            /// <summary>Total objects discarded (not pooled).</summary>
            public int TotalDiscarded;

            /// <summary>Total <see cref="Get"/> calls.</summary>
            public int TotalGets;

            /// <summary>Total <see cref="Get"/> calls served from the pool rather than the factory.</summary>
            public int PooledGets;

            /// <summary>Current stack count.</summary>
            public int PoolCount;

            /// <summary>
            /// Cache hit rate: fraction of <see cref="Get"/> calls served from the pool
            /// rather than created by the factory.
            /// </summary>
            public float HitRate => TotalGets > 0 ? (float)PooledGets / TotalGets : 0f;

            /// <summary>Fraction of returned objects that were discarded rather than pooled.</summary>
            public float DiscardRate => TotalReturned > 0 ? (float)TotalDiscarded / TotalReturned : 0f;
        }
    }
}

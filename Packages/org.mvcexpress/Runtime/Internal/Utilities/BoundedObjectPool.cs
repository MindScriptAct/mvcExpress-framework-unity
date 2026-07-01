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
    /// Thread safety: a private lock guards all stack operations. Not lock-free; intended for
    /// Unity main-thread usage with occasional cross-thread access from async commands.
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
        private readonly object _lock = new object();

        // _currentSize is used by core logic (capacity check) - always present.
        private int _currentSize;

        // Diagnostic counters are available in Player too because command-pool diagnostics and
        // Player test builds call GetStatistics().
        private int _totalCreated;
        private int _totalReturned;
        private int _totalDiscarded;

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

        /// <summary>
        /// Get an object from pool or create new if empty.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Get()
        {
            lock (_lock)
            {
                if (_pool.Count > 0)
                {
                    _currentSize--;
                    return _pool.Pop();
                }
            }

            Interlocked.Increment(ref _totalCreated);
            return _factory();
        }

        /// <summary>
        /// Return an object to pool (discards if pool is full).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Return(T obj)
        {
            if (obj == null) return; // ignore nulls to keep the pool consistent.

            Interlocked.Increment(ref _totalReturned);

            // No pooling mode: everything is discarded on return.
            if (_maxSize == 0)
            {
                Interlocked.Increment(ref _totalDiscarded);
                DisposeIfNeeded(obj);
                return;
            }

            // Reset the object before putting it back into circulation.
            _reset?.Invoke(obj);

            lock (_lock)
            {
                // If we have a positive max size and the pool is already full, discard.
                if (_maxSize > 0u && _currentSize >= (int)_maxSize)
                {
                    Interlocked.Increment(ref _totalDiscarded);
                    DisposeIfNeeded(obj);
                    return;
                }

                // Otherwise push into the pool for reuse.
                _pool.Push(obj);
                _currentSize++;
            }
        }

        /// <summary>
        /// Resize the pool to a new maximum size.
        /// If new size is smaller than current pooled count, excess objects are discarded.
        /// If new size is larger, pool can grow naturally up to the new limit.
        /// </summary>
        /// <param name="newMaxSize">New maximum pool size (0=disabled, positive=fixed cap).</param>
        public void Resize(uint newMaxSize)
        {
            lock (_lock)
            {
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
                        Interlocked.Increment(ref _totalDiscarded);
                        toRemove--;
                    }
                }
            }
        }

        /// <summary>
        /// Clear all pooled objects.
        /// </summary>
        public void Clear()
        {
            lock (_lock)
            {
                while (_pool.Count > 0)
                {
                    var obj = _pool.Pop();
                    DisposeIfNeeded(obj);
                }
                _currentSize = 0;
            }
        }

        /// <summary>
        /// Trim pool to target size.
        /// </summary>
        public void TrimTo(int targetSize)
        {
            if (targetSize < 0) throw new ArgumentOutOfRangeException(nameof(targetSize));

            lock (_lock)
            {
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
                        Interlocked.Increment(ref _totalDiscarded);
                        toDiscard--;
                        continue;
                    }

                    _pool.Push(obj);
                    _currentSize++;
                }
            }
        }

        /// <summary>
        /// Get a point-in-time snapshot of pool statistics.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PoolStatistics GetStatistics()
        {
            lock (_lock)
            {
                return new PoolStatistics
                {
                    CurrentSize = _currentSize,
                    MaxSize = _maxSize,
                    TotalCreated = _totalCreated,
                    TotalReturned = _totalReturned,
                    TotalDiscarded = _totalDiscarded,
                    PoolCount = _pool.Count
                };
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DisposeIfNeeded(T obj)
        {
            if (obj is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    MvcDebug.LogError($"Error disposing pooled object: {ex.Message}");
#endif
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

            /// <summary>Current stack count.</summary>
            public int PoolCount;

            /// <summary>
            /// Estimated cache hit rate: fraction of Return() calls where the object
            /// had previously been retrieved from the pool (TotalReturned > TotalCreated).
            /// </summary>
            public float HitRate => TotalReturned > 0 ? (float)(TotalReturned - TotalCreated) / TotalReturned : 0f;

            /// <summary>Fraction of returned objects that were discarded rather than pooled.</summary>
            public float DiscardRate => TotalReturned > 0 ? (float)TotalDiscarded / TotalReturned : 0f;
        }
    }
}

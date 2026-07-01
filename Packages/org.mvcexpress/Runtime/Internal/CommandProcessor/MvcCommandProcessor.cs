using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Interfaces;
using mvcExpress.Internal.Messaging;
using mvcExpress.Internal.Utilities;
using mvcExpress.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace mvcExpress.Internal.Commands
{
    /// <summary>
    /// Runtime command dispatcher that binds message types to command pools and executes commands when messages arrive.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed partial class MvcCommandProcessor : ICommandProcessorInternal, IDisposable
    {
        private readonly Type _moduleType;
        private readonly MvcDiContainer _container;
        private readonly MvcMessageBus _messageBus;
        private readonly MvcModule _moduleContext;

        private readonly int _instanceId;
        private static int _nextInstanceId;
        private static readonly object _instanceIdLock = new object();

        private bool _disposed;

        // Transient dependency tracking
        private readonly HashSet<CommandDependencyKey> _trackedCommandDependencies = new HashSet<CommandDependencyKey>();
        private readonly Dictionary<Type, List<BoundedObjectPool<MvcCommandBase>>> _syncPoolsByDependency = new Dictionary<Type, List<BoundedObjectPool<MvcCommandBase>>>();
        private readonly Dictionary<Type, List<BoundedObjectPool<MvcCommandBase>>> _asyncPoolsByDependency = new Dictionary<Type, List<BoundedObjectPool<MvcCommandBase>>>();

        // Track pools by command type so transient dependency removal can invalidate affected pools.
        private readonly Dictionary<Type, PoolTrackingInfo> _poolsByCommandType = new Dictionary<Type, PoolTrackingInfo>();

        // Stores one Action per BindCommand call; each action unsubscribes from the shared bus on Dispose.
        private readonly List<Action> _unbindActions = new List<Action>(16);


        /// <summary>
        /// Tracks one command pool and the transient dependencies discovered for that command type.
        /// </summary>
        private sealed class PoolTrackingInfo
        {
            public BoundedObjectPool<MvcCommandBase> Pool;
            public bool IsAsync;
            public HashSet<Type> KnownDependencies = new HashSet<Type>();
        }

        /// <summary>
        /// Compound key used to avoid registering the same command dependency relationship twice.
        /// </summary>
        private readonly struct CommandDependencyKey : IEquatable<CommandDependencyKey>
        {
            private readonly Type _commandType;
            private readonly Type _dependencyType;
            private readonly int _hashCode;

            public CommandDependencyKey(Type commandType, Type dependencyType)
            {
                _commandType = commandType;
                _dependencyType = dependencyType;
                unchecked
                {
                    _hashCode = (_commandType.GetHashCode() * 397) ^ _dependencyType.GetHashCode();
                }
            }

            public bool Equals(CommandDependencyKey other) => _commandType == other._commandType && _dependencyType == other._dependencyType;
            public override bool Equals(object obj) => obj is CommandDependencyKey other && Equals(other);
            public override int GetHashCode() => _hashCode;
        }

        /// <summary>
        /// Compound key that identifies a command binding independently of the message type index.
        /// </summary>
        private readonly struct BoundCommandKey : IEquatable<BoundCommandKey>
        {
            public readonly Type CommandType;
            public readonly bool IsAsync;
            private readonly int _hashCode;

            public BoundCommandKey(Type commandType, bool isAsync)
            {
                CommandType = commandType;
                IsAsync = isAsync;
                unchecked
                {
                    _hashCode = (commandType.GetHashCode() * 397) ^ (isAsync ? 1 : 0);
                }
            }

            public bool Equals(BoundCommandKey other) => CommandType == other.CommandType && IsAsync == other.IsAsync;
            public override bool Equals(object obj) => obj is BoundCommandKey other && Equals(other);
            public override int GetHashCode() => _hashCode;
        }

        // Binding introspection index (populated by generic Bind*/Unbind* paths)
        private readonly Dictionary<Type, HashSet<BoundCommandKey>> _boundCommandsByMessage = new Dictionary<Type, HashSet<BoundCommandKey>>(64);
        private readonly Dictionary<Type, int> _messageBindingCounts = new Dictionary<Type, int>(64);

        public MvcCommandProcessor(Type moduleType, MvcDiContainer container, MvcMessageBus messageBus, MvcModule module)
        {
            lock (_instanceIdLock)
            {
                _instanceId = _nextInstanceId++;
            }

            _moduleType = moduleType;
            _container = container;
            _messageBus = messageBus;
            _moduleContext = module;

            ((ITransientDependencyNotifier)container).SubscribeToTransientRemoval(OnTransientDependencyRemoved);
        }

        public MvcCommandProcessor(Type moduleType, MvcDiContainer container, MvcMessageBus messageBus)
            : this(moduleType, container, messageBus, null)
        {
        }

        internal void TrackUnbindAction(Action unbindAction)
        {
            if (unbindAction != null)
                _unbindActions.Add(unbindAction);
        }

        public void UnbindAll<TMessage>() where TMessage : IMessageBase
        {
            // Generic-only command processor: there is no message-only unbind.
            // Kept as a no-op for API compatibility with older code.
        }

        // ICommandBindingInfo
        public bool HasMessageBindings<TMessage>() where TMessage : IMessageBase => HasBindings(typeof(TMessage));

        public bool HasBindings(Type messageType)
        {
            if (messageType == null) throw new ArgumentNullException(nameof(messageType));
            return _messageBindingCounts.TryGetValue(messageType, out var c) && c > 0;
        }

        public bool IsBound<TCommand, TMessage>()
            where TCommand : MvcCommandBase
            where TMessage : IMessageBase
            => IsBound(typeof(TCommand), typeof(TMessage));

        public bool IsBound(Type commandType, Type messageType)
        {
            if (commandType == null) throw new ArgumentNullException(nameof(commandType));
            if (messageType == null) throw new ArgumentNullException(nameof(messageType));

            if (!_boundCommandsByMessage.TryGetValue(messageType, out var set))
                return false;

            // If caller queries by type only, treat any binding (sync or async) as 'bound'.
            // This keeps IsBound behavior compatible while allowing both modes to coexist.
            foreach (var k in set)
            {
                if (k.CommandType == commandType)
                    return true;
            }

            return false;
        }

        public int GetCommandBindingCount<TCommand>() where TCommand : MvcCommandBase => GetBindingCountForCommand(typeof(TCommand));

        public int GetBindingCountForCommand(Type commandType)
        {
            if (commandType == null) throw new ArgumentNullException(nameof(commandType));

            int count = 0;
            foreach (var kvp in _boundCommandsByMessage)
            {
                foreach (var k in kvp.Value)
                {
                    if (k.CommandType == commandType)
                    {
                        count++;
                        break;
                    }
                }
            }
            return count;
        }

        public int GetBoundMessageCount() => _boundCommandsByMessage.Count;

        internal void TrackBinding(Type messageType, Type commandType) => TrackBinding(messageType, commandType, isAsync: false);
        internal void TrackBinding(Type messageType, Type commandType, bool isAsync)
        {
            if (!_boundCommandsByMessage.TryGetValue(messageType, out var set))
            {
                set = new HashSet<BoundCommandKey>();
                _boundCommandsByMessage[messageType] = set;
            }

            if (set.Add(new BoundCommandKey(commandType, isAsync)))
            {
                _messageBindingCounts.TryGetValue(messageType, out var c);
                _messageBindingCounts[messageType] = c + 1;
            }
        }

        internal void UntrackBinding(Type messageType, Type commandType) => UntrackBinding(messageType, commandType, isAsync: false);
        internal void UntrackBinding(Type messageType, Type commandType, bool isAsync)
        {
            if (!_boundCommandsByMessage.TryGetValue(messageType, out var set))
                return;

            if (!set.Remove(new BoundCommandKey(commandType, isAsync)))
                return;

            if (_messageBindingCounts.TryGetValue(messageType, out var c) && c > 0)
            {
                c--;
                if (c == 0)
                    _messageBindingCounts.Remove(messageType);
                else
                    _messageBindingCounts[messageType] = c;
            }

            if (set.Count == 0)
                _boundCommandsByMessage.Remove(messageType);
        }

        // Transient dependency tracking hooks used by generic execution paths
        internal void OnDependencyResolved(Type commandType, Type dependencyType, bool isAsync)
        {
            if (!_container.IsTransient(dependencyType))
                return;

            var key = new CommandDependencyKey(commandType, dependencyType);
            if (_trackedCommandDependencies.Contains(key))
                return;

            _trackedCommandDependencies.Add(key);

            // NEW: Link pool to dependency when discovered
            if (_poolsByCommandType.TryGetValue(commandType, out var poolInfo))
            {
                if (poolInfo.KnownDependencies.Add(dependencyType))
                {
                    RegisterPoolForDependency(poolInfo.Pool, dependencyType, poolInfo.IsAsync);
                }
            }
        }

        private void RegisterPoolForDependency(BoundedObjectPool<MvcCommandBase> pool, Type dependencyType, bool isAsync)
        {
            var poolsByDep = isAsync ? _asyncPoolsByDependency : _syncPoolsByDependency;

            if (!poolsByDep.TryGetValue(dependencyType, out var pools))
            {
                pools = new List<BoundedObjectPool<MvcCommandBase>>();
                poolsByDep[dependencyType] = pools;
            }

            if (!pools.Contains(pool))
            {
                pools.Add(pool);
            }
        }

        private void OnTransientDependencyRemoved(Type dependencyType)
        {
            // Invalidate pooled commands
            if (_syncPoolsByDependency.TryGetValue(dependencyType, out var syncPools))
            {
                foreach (var pool in syncPools)
                    pool.Clear();
            }

            if (_asyncPoolsByDependency.TryGetValue(dependencyType, out var asyncPools))
            {
                foreach (var pool in asyncPools)
                    pool.Clear();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            // Unsubscribe all command handlers from the shared bus before clearing state
            for (int i = 0; i < _unbindActions.Count; i++)
            {
                try { _unbindActions[i]?.Invoke(); }
                catch (Exception ex)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    MvcDebug.LogWarning($"Error unsubscribing command: {ex.Message}");
#else
                    _ = ex;
#endif
                }
            }
            _unbindActions.Clear();

            ((ITransientDependencyNotifier)_container).UnsubscribeFromTransientRemoval(OnTransientDependencyRemoved);

            _trackedCommandDependencies.Clear();
            _syncPoolsByDependency.Clear();
            _asyncPoolsByDependency.Clear();
            _poolsByCommandType.Clear();
            _boundCommandsByMessage.Clear();
            _messageBindingCounts.Clear();
        }

        [ThreadStatic]
        private static bool _scopedDependencyResolved;

        private void BeginCommandExecution() => _scopedDependencyResolved = false;
        internal void OnScopedDependencyResolved() => _scopedDependencyResolved = true;

        internal void ReturnToPoolGeneric(MvcCommandBase command, BoundedObjectPool<MvcCommandBase> pool)
        {
            if (command == null || pool == null)
                return;

            if (_scopedDependencyResolved)
                return;

            if (!command.IsValid)
                return;

            pool.Return(command);
        }

        internal void RegisterPoolForTracking(BoundedObjectPool<MvcCommandBase> pool, Type commandType, bool isAsync)
        {
            if (pool == null || commandType == null)
                return;

            // Store pool info for later dependency linking
            _poolsByCommandType[commandType] = new PoolTrackingInfo
            {
                Pool = pool,
                IsAsync = isAsync
            };
        }

        internal void UnregisterPoolFromTracking(BoundedObjectPool<MvcCommandBase> pool, Type commandType, bool isAsync)
        {
            if (pool == null)
                return;

            // Remove from command type tracking
            _poolsByCommandType.Remove(commandType);

            // Remove from dependency tracking
            var poolsByDep = isAsync ? _asyncPoolsByDependency : _syncPoolsByDependency;
            foreach (var poolList in poolsByDep.Values)
                poolList.Remove(pool);
        }

        #region Centralized Command Pool Storage

        /// <summary>
        /// Centralized pool storage per command type.
        /// One pool per TCommand type, shared across all usages (Bind, Run, etc.)
        /// </summary>
        private static class CommandPool<TCommand> where TCommand : MvcCommandBase, new()
        {
            internal static BoundedObjectPool<MvcCommandBase>[] Pools = new BoundedObjectPool<MvcCommandBase>[4];
            internal static readonly object Lock = new object();

            internal static void EnsureCapacity(int instanceId)
            {
                if (instanceId >= Pools.Length)
                {
                    lock (Lock)
                    {
                        if (instanceId >= Pools.Length)
                        {
                            int newSize = Math.Max(instanceId + 1, Pools.Length * 2);
                            Array.Resize(ref Pools, newSize);
                        }
                    }
                }
            }
        }

        #endregion

        #region Pool Management

        /// <summary>
        /// Get or create a pool for the specified command type.
        /// If pool exists, returns it. If not, creates one with the specified default size.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal BoundedObjectPool<MvcCommandBase> GetOrCreatePool<TCommand>(uint defaultPoolSize = 0) where TCommand : MvcCommandBase, new()
        {
            CommandPool<TCommand>.EnsureCapacity(_instanceId);

            var pool = CommandPool<TCommand>.Pools[_instanceId];
            if (pool == null)
            {
                lock (CommandPool<TCommand>.Lock)
                {
                    pool = CommandPool<TCommand>.Pools[_instanceId];
                    if (pool == null)
                    {
                        pool = new BoundedObjectPool<MvcCommandBase>(
                            factory: () => new TCommand(),
                            reset: null,
                            maxSize: defaultPoolSize,
                            initialCapacity: defaultPoolSize > 0u ? (int)Math.Min(4u, defaultPoolSize) : 1
                        );
                        CommandPool<TCommand>.Pools[_instanceId] = pool;

                        // Determine if async based on type
                        bool isAsync = typeof(MvcAsyncCommandBase).IsAssignableFrom(typeof(TCommand));
                        RegisterPoolForTracking(pool, typeof(TCommand), isAsync);
                    }
                }
            }

            return pool;
        }

        /// <summary>
        /// Create or reconfigure a pool for the specified command type.
        /// If pool already exists, resizes it to the new size (preserving pooled objects when possible).
        /// </summary>
        public void CreatePool<TCommand>(uint poolSize) where TCommand : MvcCommandBase, new()
        {
            CommandPool<TCommand>.EnsureCapacity(_instanceId);

            lock (CommandPool<TCommand>.Lock)
            {
                var existingPool = CommandPool<TCommand>.Pools[_instanceId];

                if (existingPool != null)
                {
                    // Pool exists - just resize it (handles both grow and shrink)
                    existingPool.Resize(poolSize);
                    return;
                }

                // Create new pool
                var pool = new BoundedObjectPool<MvcCommandBase>(
                    factory: () => new TCommand(),
                    reset: null,
                    maxSize: poolSize,
                    initialCapacity: poolSize > 0u ? (int)Math.Min(4u, poolSize) : 1
                );
                CommandPool<TCommand>.Pools[_instanceId] = pool;

                bool isAsync = typeof(MvcAsyncCommandBase).IsAssignableFrom(typeof(TCommand));
                RegisterPoolForTracking(pool, typeof(TCommand), isAsync);
            }
        }

        #endregion

        #region Debug Statistics
#if UNITY_EDITOR || MVC_LOGGING

        /// <summary>
        /// Statistics for a single command pool. Only available in UNITY_EDITOR or MVC_LOGGING builds.
        /// </summary>
        public readonly struct CommandPoolStatistics
        {
            /// <summary>Command type name.</summary>
            public readonly string CommandTypeName;

            /// <summary>Whether this is an async command pool.</summary>
            public readonly bool IsAsync;

            /// <summary>Pool statistics from BoundedObjectPool.</summary>
            public readonly BoundedObjectPool<MvcCommandBase>.PoolStatistics PoolStats;

            internal CommandPoolStatistics(string commandTypeName, bool isAsync, BoundedObjectPool<MvcCommandBase>.PoolStatistics poolStats)
            {
                CommandTypeName = commandTypeName;
                IsAsync = isAsync;
                PoolStats = poolStats;
            }
        }

        /// <summary>
        /// Get statistics for all tracked command pools. Only available in UNITY_EDITOR or MVC_LOGGING builds.
        /// </summary>
        /// <returns>List of statistics for each command pool.</returns>
        public List<CommandPoolStatistics> GetPoolStatistics()
        {
            var results = new List<CommandPoolStatistics>(_poolsByCommandType.Count);

            foreach (var kvp in _poolsByCommandType)
            {
                var commandType = kvp.Key;
                var trackingInfo = kvp.Value;

                if (trackingInfo?.Pool == null)
                    continue;

                var poolStats = trackingInfo.Pool.GetStatistics();
                results.Add(new CommandPoolStatistics(
                    commandType.Name,
                    trackingInfo.IsAsync,
                    poolStats
                ));
            }

            return results;
        }

        /// <summary>
        /// Get aggregate statistics across all command pools. Only available in UNITY_EDITOR or MVC_LOGGING builds.
        /// </summary>
        public (int TotalPools, int TotalPooledObjects, int TotalCreated, int TotalDiscarded, float AverageHitRate) GetAggregatePoolStatistics()
        {
            int totalPools = 0;
            int totalPooledObjects = 0;
            int totalCreated = 0;
            int totalDiscarded = 0;
            float hitRateSum = 0f;

            foreach (var kvp in _poolsByCommandType)
            {
                var stats = kvp.Value.Pool.GetStatistics();
                totalPools++;
                totalPooledObjects += stats.CurrentSize;
                totalCreated += stats.TotalCreated;
                totalDiscarded += stats.TotalDiscarded;
                hitRateSum += stats.HitRate;
            }

            float averageHitRate = totalPools > 0 ? hitRateSum / totalPools : 0f;
            return (totalPools, totalPooledObjects, totalCreated, totalDiscarded, averageHitRate);
        }

#endif

        /// <summary>
        /// Get the number of message types with bound commands.
        /// </summary>
        public int GetBoundMessageTypeCount() => _boundCommandsByMessage.Count;

        /// <summary>
        /// Get binding counts for each message type.
        /// </summary>
        public Dictionary<string, int> GetMessageBindingCounts()
        {
            var results = new Dictionary<string, int>(_messageBindingCounts.Count);
            foreach (var kvp in _messageBindingCounts)
            {
                results[kvp.Key.Name] = kvp.Value;
            }
            return results;
        }

        #endregion

        #region Internal Diagnostics & Tooling Support

        /// <summary>
        /// Distinguishes synchronous and asynchronous command bindings for diagnostics.
        /// </summary>
        internal enum CommandBindingMode
        {
            Sync,
            Async
        }

        /// <summary>
        /// Lightweight diagnostic row describing one command binding and its pool state.
        /// </summary>
        internal readonly struct CommandBindingSnapshot
        {
            public readonly Type MessageType;
            public readonly Type CommandType;
            public readonly CommandBindingMode Mode;
            public readonly int PoolCurrent;
            public readonly int PoolMax;

            public CommandBindingSnapshot(Type messageType, Type commandType, CommandBindingMode mode, int poolCurrent, int poolMax)
            {
                MessageType = messageType;
                CommandType = commandType;
                Mode = mode;
                PoolCurrent = poolCurrent;
                PoolMax = poolMax;
            }
        }

#if UNITY_EDITOR || MVC_LOGGING
        internal void GetCommandBindingSnapshot(List<CommandBindingSnapshot> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));

            results.Clear();

            // Pool sizes are tracked by type internally.
            var poolByKey = new Dictionary<BoundCommandKey, (int current, uint max)>(_poolsByCommandType.Count);
            foreach (var kvp in _poolsByCommandType)
            {
                var cmdType = kvp.Key;
                var info = kvp.Value;
                if (cmdType == null || info?.Pool == null)
                    continue;

                var stats = info.Pool.GetStatistics();
                poolByKey[new BoundCommandKey(cmdType, info.IsAsync)] = (stats.CurrentSize, stats.MaxSize);
            }

            foreach (var kvp in _boundCommandsByMessage)
            {
                var msgType = kvp.Key;
                var set = kvp.Value;
                if (msgType == null || set == null)
                    continue;

                foreach (var k in set)
                {
                    var cmdType = k.CommandType;
                    if (cmdType == null)
                        continue;

                    var mode = k.IsAsync ? CommandBindingMode.Async : CommandBindingMode.Sync;

                    int poolCurrent;
                    int poolMax;

                    if (poolByKey.TryGetValue(new BoundCommandKey(cmdType, k.IsAsync), out var pool))
                    {
                        poolCurrent = pool.current;
                        poolMax = (int)pool.max;
                    }
                    else
                    {
                        poolCurrent = 0;
                        poolMax = 0;
                    }

                    results.Add(new CommandBindingSnapshot(msgType, cmdType, mode, poolCurrent, poolMax));
                }
            }
        }
#endif

        #endregion
    }
}

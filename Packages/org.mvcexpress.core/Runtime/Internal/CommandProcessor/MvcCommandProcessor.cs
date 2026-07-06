using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Interfaces;
using mvcExpress.Internal.Messaging;
using mvcExpress.Internal.Utilities;
using mvcExpress.Logging;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;

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

        // Recycled IDs from disposed processors, mirroring MvcMessageBus's instance ID recycling.
        // Without this, every module create/destroy cycle burns a fresh slot in every
        // CommandPool<TCommand>.Pools array, growing them forever.
        private static readonly Queue<int> _recycledInstanceIds = new Queue<int>();

        private static int GetNextInstanceId()
        {
            lock (_instanceIdLock)
            {
                if (_recycledInstanceIds.Count > 0)
                    return _recycledInstanceIds.Dequeue();

                return _nextInstanceId++;
            }
        }

        private static void RecycleInstanceId(int instanceId)
        {
            if (instanceId < 0) return;
            lock (_instanceIdLock)
            {
                _recycledInstanceIds.Enqueue(instanceId);
            }
        }

        private bool _disposed;

        /// <summary>
        /// Invoked whenever a bound or directly-run command throws during execution, in every
        /// build configuration - including release players without <c>MVC_LOGGING</c>, where
        /// <see cref="MvcDebug"/> calls compile away to nothing. Subscribe to route command
        /// failures to a crash reporter or analytics pipeline - see M4.
        /// </summary>
        public static event Action<Type, Exception> OnCommandFault;

        // Single choke point for the M4 fix: every ExecuteCommandDirect*/ExecuteCommandDirectAsync*
        // catch block (across all 13 arity partials) calls this instead of only logging in
        // Editor/Development builds, so a release player is never silently missing a command fault.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void RaiseCommandFault(Type commandType, Exception ex)
        {
#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
            // Editor/Development builds already logged this via MvcDebug.LogError at the call
            // site (with the friendlier "Command 'X' execution failed" message); only fall back
            // to LogException here so release builds still get something without double-logging
            // in Editor/Dev.
            UnityEngine.Debug.LogException(ex);
#endif

            var handler = OnCommandFault;
            if (handler == null) return;

            try
            {
                handler(commandType, ex);
            }
            catch (Exception handlerEx)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                MvcDebug.LogError($"Error in OnCommandFault subscriber: {handlerEx.Message}");
#else
                _ = handlerEx;
#endif
            }
        }

        // M5 fix: BindCommand<TCommand, TMessage[, T1..T12]> only constrains TCommand : MvcCommandBase,
        // so binding e.g. a zero-payload Command to an IMessage<int> compiles - at dispatch time
        // `cmd as Command<T1>` then silently returns null and the command never runs. Every
        // BindCommand/BindCommandAsync overload (across all 13 arity partials) calls this once, at
        // bind time, with TExpectedBase set to the concrete arity-matching base (Command, Command<T1>,
        // CommandAsync<T1,T2>, ...) so a mismatch fails loudly and immediately instead of silently
        // forever. One-time cost at bind time - never on the dispatch path.
        private static void ValidateCommandArity<TCommand, TExpectedBase>()
        {
            if (!typeof(TExpectedBase).IsAssignableFrom(typeof(TCommand)))
            {
                throw new InvalidOperationException(
                    $"[MvcExpress] Command '{typeof(TCommand).FullName}' does not inherit from " +
                    $"'{typeof(TExpectedBase).Name}', so it cannot be bound to a message with this " +
                    $"payload arity - it would compile but silently never execute. Make sure the " +
                    $"command class extends the same Command<...>/CommandAsync<...> arity as the " +
                    $"message's IMessage<...> interface.");
            }
        }

        // Transient dependency tracking
        private readonly HashSet<CommandDependencyKey> _trackedCommandDependencies = new HashSet<CommandDependencyKey>();
        private readonly Dictionary<Type, List<BoundedObjectPool<MvcCommandBase>>> _syncPoolsByDependency = new Dictionary<Type, List<BoundedObjectPool<MvcCommandBase>>>();
        private readonly Dictionary<Type, List<BoundedObjectPool<MvcCommandBase>>> _asyncPoolsByDependency = new Dictionary<Type, List<BoundedObjectPool<MvcCommandBase>>>();

        // Track pools by command type so transient dependency removal can invalidate affected pools.
        private readonly Dictionary<Type, PoolTrackingInfo> _poolsByCommandType = new Dictionary<Type, PoolTrackingInfo>();

        // Stores one Action per BindCommand call; each action unsubscribes from the shared bus on Dispose.
        private readonly List<Action> _unbindActions = new List<Action>(16);

        // Strong reference to the transient-removal handler delegate. The container tracks this
        // subscription via WeakEventManager<Type>, which only weakly references the delegate object
        // itself (not "this processor") - a bare method-group conversion at the subscribe call site
        // would be collectible immediately, silently killing pool invalidation after any GC even
        // while the processor is alive. Dispose() already unsubscribes explicitly, so this field
        // only needs to keep the delegate alive between construction and disposal - see H2.
        private readonly Action<Type> _transientRemovalHandler;


        /// <summary>
        /// Tracks one command pool and the transient dependencies discovered for that command type.
        /// </summary>
        private sealed class PoolTrackingInfo
        {
            public BoundedObjectPool<MvcCommandBase> Pool;
            public bool IsAsync;
            public HashSet<Type> KnownDependencies = new HashSet<Type>();

            // Nulls this processor's slot in the static CommandPool<TCommand>.Pools array. Captured
            // as a closure at pool-creation time (when TCommand is still a compile-time generic
            // argument) since PoolTrackingInfo itself is non-generic and cannot reach back into
            // CommandPool<TCommand> without it.
            public Action ClearStaticSlot;
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
            _instanceId = GetNextInstanceId();

            _moduleType = moduleType;
            _container = container;
            _messageBus = messageBus;
            _moduleContext = module;

            _transientRemovalHandler = OnTransientDependencyRemoved;
            ((ITransientDependencyNotifier)container).SubscribeToTransientRemoval(_transientRemovalHandler);
        }

        public MvcCommandProcessor(Type moduleType, MvcDiContainer container, MvcMessageBus messageBus)
            : this(moduleType, container, messageBus, null)
        {
        }

        /// <summary>
        /// Cancelled when the owning module is destroyed. <see cref="CancellationToken.None"/>
        /// if this processor has no owning module (the no-module constructor overload above).
        /// </summary>
        public CancellationToken CancelToken => _moduleContext?.CancelToken ?? CancellationToken.None;

        internal void TrackUnbindAction(Action unbindAction)
        {
            if (unbindAction != null)
                _unbindActions.Add(unbindAction);
        }

        // Every IMessage<...> generic interface definition, arity 0-12, in payload-count order.
        // Used by UnbindAll<TMessage> to recover the payload Type[] for a message type that is
        // only known as `TMessage : IMessageBase` at the call site - see L4.
        private static readonly Type[] MessageInterfaceDefinitions =
        {
            typeof(IMessage<>), typeof(IMessage<,>), typeof(IMessage<,,>), typeof(IMessage<,,,>),
            typeof(IMessage<,,,,>), typeof(IMessage<,,,,,>), typeof(IMessage<,,,,,,>), typeof(IMessage<,,,,,,,>),
            typeof(IMessage<,,,,,,,,>), typeof(IMessage<,,,,,,,,,>), typeof(IMessage<,,,,,,,,,,>), typeof(IMessage<,,,,,,,,,,,>)
        };

        // Caches the arity-specific Unbind*/UnbindAsync* MethodInfo (generic definitions) keyed by
        // (isAsync, payloadArity), so repeated UnbindAll calls don't re-scan MvcCommandProcessor's
        // methods with reflection every time.
        private static readonly Dictionary<(bool IsAsync, int Arity), MethodInfo> UnbindMethodCache = new Dictionary<(bool, int), MethodInfo>();

        private static Type[] GetMessagePayloadTypes(Type messageType)
        {
            foreach (var iface in messageType.GetInterfaces())
            {
                if (iface == typeof(IMessage))
                    return Type.EmptyTypes;

                if (iface.IsGenericType)
                {
                    var genericDef = iface.GetGenericTypeDefinition();
                    for (int arity = 0; arity < MessageInterfaceDefinitions.Length; arity++)
                    {
                        if (genericDef == MessageInterfaceDefinitions[arity])
                            return iface.GetGenericArguments();
                    }
                }
            }

            return null;
        }

        private static MethodInfo GetUnbindMethodDefinition(bool isAsync, int payloadArity)
        {
            var cacheKey = (isAsync, payloadArity);
            if (UnbindMethodCache.TryGetValue(cacheKey, out var cached))
                return cached;

            // Arity 0 uses UnbindCommand/UnbindCommandAsync; arity 1-12 use UnbindGeneric/UnbindAsyncGeneric
            // - an existing naming inconsistency in the generated arity partials, not introduced here.
            string methodName = payloadArity == 0
                ? (isAsync ? "UnbindCommandAsync" : "UnbindCommand")
                : (isAsync ? "UnbindAsyncGeneric" : "UnbindGeneric");

            MethodInfo found = null;
            foreach (var candidate in typeof(MvcCommandProcessor).GetMethods(BindingFlags.Public | BindingFlags.Instance))
            {
                if (candidate.Name != methodName || !candidate.IsGenericMethodDefinition)
                    continue;

                // TCommand, TMessage, plus one generic parameter per payload value.
                if (candidate.GetGenericArguments().Length == 2 + payloadArity)
                {
                    found = candidate;
                    break;
                }
            }

            UnbindMethodCache[cacheKey] = found;
            return found;
        }

        /// <summary>
        /// Unbinds every command currently bound to <typeparamref name="TMessage"/> on this
        /// processor (both sync and async bindings). Safe to call even if nothing is bound.
        /// </summary>
        public void UnbindAll<TMessage>() where TMessage : IMessageBase
        {
            var messageType = typeof(TMessage);
            if (!_boundCommandsByMessage.TryGetValue(messageType, out var boundSet) || boundSet.Count == 0)
                return;

            // Snapshot first: each invoked Unbind*/UnbindAsync* call mutates _boundCommandsByMessage
            // via UntrackBinding, so iterating the live set directly would be modifying it mid-loop.
            var snapshot = new List<BoundCommandKey>(boundSet);

            var payloadTypes = GetMessagePayloadTypes(messageType);
            if (payloadTypes == null)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                MvcDebug.LogWarning($"UnbindAll<{messageType.Name}>: could not determine payload arity (no recognised IMessage interface) - nothing unbound.");
#endif
                return;
            }

            foreach (var key in snapshot)
            {
                var unbindMethodDefinition = GetUnbindMethodDefinition(key.IsAsync, payloadTypes.Length);
                if (unbindMethodDefinition == null)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    MvcDebug.LogWarning($"UnbindAll<{messageType.Name}>: no matching unbind method found for command '{key.CommandType.Name}' (isAsync={key.IsAsync}, arity={payloadTypes.Length}).");
#endif
                    continue;
                }

                var typeArgs = new Type[2 + payloadTypes.Length];
                typeArgs[0] = key.CommandType;
                typeArgs[1] = messageType;
                Array.Copy(payloadTypes, 0, typeArgs, 2, payloadTypes.Length);

                unbindMethodDefinition.MakeGenericMethod(typeArgs).Invoke(this, null);
            }
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

            ((ITransientDependencyNotifier)_container).UnsubscribeFromTransientRemoval(_transientRemovalHandler);

            // Drop every pooled command (disposing IDisposable ones) and null this processor's
            // slot in the static CommandPool<TCommand>.Pools array. Without this, the pools and
            // every command instance they hold (each pinning _moduleContext, _diContainer, and
            // injected proxies/services) would be retained by static memory forever - see H1.
            foreach (var trackingInfo in _poolsByCommandType.Values)
            {
                trackingInfo.Pool?.Clear();
                trackingInfo.ClearStaticSlot?.Invoke();
            }

            _trackedCommandDependencies.Clear();
            _syncPoolsByDependency.Clear();
            _asyncPoolsByDependency.Clear();
            _poolsByCommandType.Clear();
            _boundCommandsByMessage.Clear();
            _messageBindingCounts.Clear();

            RecycleInstanceId(_instanceId);
        }

        [ThreadStatic]
        private static bool _scopedDependencyResolved;

        private void BeginCommandExecution() => _scopedDependencyResolved = false;
        internal void OnScopedDependencyResolved() => _scopedDependencyResolved = true;

        /// <summary>
        /// Returns a command to its pool for reuse, or discards it when it must not be reused
        /// (scoped dependency resolved, marked invalid via <see cref="MvcCommandBase.Invalidate"/>,
        /// or no pool available). Discarded commands are disposed here directly because they
        /// bypass <see cref="BoundedObjectPool{T}.Return"/> - the only other place that invokes
        /// <see cref="IDisposable"/> - entirely.
        /// </summary>
        internal void ReturnToPoolGeneric(MvcCommandBase command, BoundedObjectPool<MvcCommandBase> pool)
        {
            if (command == null)
                return;

            if (pool == null || _scopedDependencyResolved || !command.IsValid)
            {
                DisposeDiscardedCommand(command);
                return;
            }

            pool.Return(command);
        }

        private static void DisposeDiscardedCommand(MvcCommandBase command)
        {
            if (command is not IDisposable disposable)
                return;

            try
            {
                disposable.Dispose();
            }
            catch (Exception ex)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                MvcDebug.LogError($"Error disposing discarded command '{command.GetType().Name}': {ex.Message}");
#else
                _ = ex;
#endif
            }
        }

        internal void RegisterPoolForTracking(BoundedObjectPool<MvcCommandBase> pool, Type commandType, bool isAsync, Action clearStaticSlot = null)
        {
            if (pool == null || commandType == null)
                return;

            // Store pool info for later dependency linking
            _poolsByCommandType[commandType] = new PoolTrackingInfo
            {
                Pool = pool,
                IsAsync = isAsync,
                ClearStaticSlot = clearStaticSlot
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
                        // reset: null - command pooling doesn't use BoundedObjectPool's per-return
                        // reset hook. Per-execution state belongs in Execute()/ExecuteAsync()
                        // locals, not instance fields that would need clearing between reuses.
                        pool = new BoundedObjectPool<MvcCommandBase>(
                            factory: () => new TCommand(),
                            reset: null,
                            maxSize: defaultPoolSize,
                            initialCapacity: defaultPoolSize > 0u ? (int)Math.Min(4u, defaultPoolSize) : 1
                        );
                        CommandPool<TCommand>.Pools[_instanceId] = pool;

                        // Determine if async based on type
                        bool isAsync = typeof(MvcAsyncCommandBase).IsAssignableFrom(typeof(TCommand));
                        int instanceId = _instanceId;
                        RegisterPoolForTracking(pool, typeof(TCommand), isAsync, () => CommandPool<TCommand>.Pools[instanceId] = null);
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
                // reset: null - see the matching note in GetOrCreatePool<TCommand>() above;
                // this is an unused generic-pool capability, not an external-tooling hook.
                var pool = new BoundedObjectPool<MvcCommandBase>(
                    factory: () => new TCommand(),
                    reset: null,
                    maxSize: poolSize,
                    initialCapacity: poolSize > 0u ? (int)Math.Min(4u, poolSize) : 1
                );
                CommandPool<TCommand>.Pools[_instanceId] = pool;

                bool isAsync = typeof(MvcAsyncCommandBase).IsAssignableFrom(typeof(TCommand));
                int instanceId = _instanceId;
                RegisterPoolForTracking(pool, typeof(TCommand), isAsync, () => CommandPool<TCommand>.Pools[instanceId] = null);
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

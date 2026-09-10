﻿using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Initialization;
using mvcExpress.Internal.Messaging;
using mvcExpress.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
// Aliased rather than "using System.Diagnostics;" - that namespace also contains
// System.Diagnostics.Debug, which would collide with UnityEngine.Debug (used throughout this
// file) and turn every unqualified Debug.* call below into a CS0104 ambiguous-reference error.
using Stopwatch = System.Diagnostics.Stopwatch;

namespace mvcExpress
{
    /// <summary>
    /// DontDestroyOnLoad singleton that owns all global framework state: the global DI container,
    /// the shared message bus, the live module registry, and startup module configuration.
    /// </summary>
    /// <remarks>
    /// Every <see cref="MvcModule"/> registers with this facade on <c>Awake</c> and unregisters
    /// on <c>OnDestroy</c>. The facade is created automatically on demand - most projects do not
    /// need to place it in a scene manually, though doing so allows Inspector-driven startup
    /// module configuration and global proxy/service registration.
    /// <para>
    /// Access pattern: use <see cref="FacadeInstance"/> when creation is acceptable (the common
    /// case), or <see cref="InstanceOrNull"/> in teardown paths where the app may already be
    /// shutting down.
    /// </para>
    /// <para>
    /// Deferred publish: actors that need to publish a message from a background thread call
    /// <c>Messenger.PublishDeferred</c>, which enqueues an action via
    /// <see cref="TryEnqueueDeferredPublish"/>. The queue is drained each frame in <c>Update</c>
    /// on the main thread before game logic runs.
    /// </para>
    /// </remarks>
    public class MvcFacade : MonoBehaviour
    {
        private static MvcFacade _facadeInstance; // single live instance; null before first module Awake or after final destroy
        private static bool _isQuitting;        // set in OnApplicationQuit to prevent new-instance creation during teardown

        //[Header("Startup Modules")]
        [SerializeField] private MvcStartupModuleEntry[] _startupModules = Array.Empty<MvcStartupModuleEntry>();

        //[Header("View Prefab Catalogs")]
        [SerializeField] private ViewPrefabCatalog[] _viewPrefabCatalogs = Array.Empty<ViewPrefabCatalog>();

        [Header("Global MVC Containers (optional in Edit Mode)")]
        [SerializeField] private GlobalServiceRegistryBehaviour _globalServiceRegistry;
        [SerializeField] private GlobalProxyRegistryBehaviour _globalProxyRegistry;

        // Grace period prevents the facade from being destroyed mid-transition when all modules
        // unregister at the end of one scene before the next scene's modules register.
        private const int DestructionGracePeriodFrames = 2;
        private int _destructionCountdown = -1; // -1 = idle; >0 = counting down; 0 = check and maybe destroy

        /// <summary>
        /// Gets the singleton facade instance, creating and initializing it when it does not
        /// yet exist. Safe to call from any module's <c>Awake</c>.
        /// </summary>
        /// <remarks>
        /// Returns the existing (possibly quitting) instance during <c>OnApplicationQuit</c>
        /// rather than creating a new one. Use <see cref="InstanceOrNull"/> when you need a
        /// null-safe check and do not want to force creation.
        /// </remarks>
        /// <exception cref="MvcCompositionException">
        /// A <c>[RegisterGlobal]</c> attribute was invalid the first time the facade initialized.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The facade already failed to initialize once; it will not silently retry.
        /// </exception>
        public static MvcFacade FacadeInstance
        {
            get
            {
                if (_isQuitting)
                    return _facadeInstance;

                if (_facadeInstance != null && _facadeInstance.gameObject != null)
                {
                    // Checks the stored failure directly instead of calling InitializeIfNeeded()
                    // again - calling it here would be reentrant and infinitely recurse whenever
                    // this property is reached from *inside* an in-progress InitializeIfNeeded()
                    // call, which is exactly what happens for the documented
                    // RegisterGlobalServices()/RegisterGlobalProxies() -> GlobalDependencies.Register()
                    // -> MvcFacade.Global -> FacadeInstance path (a prior attempt at this fix called
                    // InitializeIfNeeded() here and broke that path with a stack overflow).
                    // _initializationFailure is only non-null once that call has already unwound, so
                    // this check is safe to make unconditionally, on every access, including nested ones.
                    if (_facadeInstance._initializationFailure != null)
                    {
                        throw BuildAlreadyFailedException(_facadeInstance._initializationFailure);
                    }

                    return _facadeInstance;
                }

                if (_facadeInstance == null || _facadeInstance.gameObject == null)
                {
#if UNITY_2022_2_OR_NEWER
                    var existing = FindAnyObjectByType<MvcFacade>(FindObjectsInactive.Include);
#else
                    var existing = FindObjectOfType<MvcFacade>(true);
#endif
                    if (existing != null)
                    {
                        _facadeInstance = existing;
                        _facadeInstance.InitializeIfNeeded();
                    }
                    else
                    {
                        var go = new GameObject(nameof(MvcFacade));
                        _facadeInstance = go.AddComponent<MvcFacade>();
                        _facadeInstance.InitializeIfNeeded();
                    }
                }

                return _facadeInstance;
            }
        }

        /// <summary>
        /// Returns the existing facade instance, or <c>null</c> if it has not been created yet
        /// or has already been destroyed. Use in teardown paths (e.g., module <c>OnDestroy</c>)
        /// where creating a new instance would be incorrect.
        /// </summary>
        public static MvcFacade InstanceOrNull => _facadeInstance;

        // Indirection through private properties lets subclasses (e.g. in tests) override the active sets.
        private MvcStartupModuleEntry[] ActiveStartupModules => _startupModules;
        private ViewPrefabCatalog[] ActiveViewPrefabCatalogs => _viewPrefabCatalogs;

        /// <summary>
        /// Inspector-configured startup module entries. Those with <c>AutoStart = true</c> are
        /// spawned automatically in <c>Start</c>; others can be triggered via
        /// <see cref="SpawnModule(Type)"/>.
        /// </summary>
        public MvcStartupModuleEntry[] StartupModules
        {
            get => _startupModules;
            set => _startupModules = value ?? Array.Empty<MvcStartupModuleEntry>();
        }

        /// <summary>
        /// App-level catalogs of mediator prefab mappings. When a module resolves a mediator
        /// prefab by type and no module-local mapping exists, these catalogs are searched in
        /// order.
        /// </summary>
        public ViewPrefabCatalog[] ViewPrefabCatalogs
        {
            get => _viewPrefabCatalogs;
            set => _viewPrefabCatalogs = value ?? Array.Empty<ViewPrefabCatalog>();
        }

        // Module registry - keyed by concrete module type; guarded by _moduleLock for thread safety.
        private readonly object _moduleLock = new object();
        private readonly Dictionary<Type, MvcModule> _modules = new Dictionary<Type, MvcModule>();

        /// <summary>
        /// Gets a read-only snapshot of all currently registered modules, keyed by concrete
        /// module type. Each call allocates a new dictionary; use sparingly (e.g. tooling/debug).
        /// </summary>
        public IReadOnlyDictionary<Type, MvcModule> Modules
        {
            get
            {
                lock (_moduleLock)
                {
                    return new Dictionary<Type, MvcModule>(_modules);
                }
            }
        }

        // Holds all globally-registered services and proxies; accessible to every module's actors.
        private readonly MvcDiContainer _globalContainer = new MvcDiContainer();

        /// <summary>
        /// Registration/resolution access to the global container, for use from
        /// <see cref="RegisterGlobalServices"/> and <see cref="RegisterGlobalProxies"/> overrides.
        /// </summary>
        protected FacadeGlobalContainerApi GlobalDependencies => default;

        /// <summary>
        /// Override to register long-lived, application-wide services using
        /// <see cref="GlobalDependencies"/>. Subclass <see cref="MvcFacade"/> and place the
        /// subclass on the scene's facade GameObject to use this.
        /// </summary>
        /// <remarks>
        /// Runs once, inside <c>InitializeIfNeeded</c>, before any <see cref="MvcModule"/> has
        /// registered with the facade - so every module can resolve these dependencies from its
        /// very first initialization phase with no race condition. Runs after the Inspector-driven
        /// Global Services registry and the <c>[RegisterGlobal]</c> attribute drain, matching the
        /// Inspector -> Attribute -> Code order used by <see cref="MvcModule"/>'s own initialization
        /// pipeline. This only determines which source registers a type first: registering the same
        /// type from more than one source throws, it does not silently override the earlier one.
        /// </remarks>
        protected virtual void RegisterGlobalServices() { }

        /// <summary>
        /// Override to register application-wide model/state proxies using
        /// <see cref="GlobalDependencies"/>. Subclass <see cref="MvcFacade"/> and place the
        /// subclass on the scene's facade GameObject to use this.
        /// </summary>
        /// <remarks>
        /// Runs once, inside <c>InitializeIfNeeded</c>, after <see cref="RegisterGlobalServices"/>
        /// and before any <see cref="MvcModule"/> has registered with the facade - so every module
        /// can resolve these dependencies from its very first initialization phase with no race
        /// condition. Runs after the Inspector-driven Global Proxies registry and the
        /// <c>[RegisterGlobal]</c> attribute drain, matching the Inspector -> Attribute -> Code
        /// order used by <see cref="MvcModule"/>'s own initialization pipeline. This only determines
        /// which source registers a type first: registering the same type from more than one source
        /// throws, it does not silently override the earlier one.
        /// </remarks>
        protected virtual void RegisterGlobalProxies() { }

        // Forces facade creation if not yet alive. Use GlobalContainerOrNull in teardown paths.
        internal static MvcDiContainer Global => FacadeInstance._globalContainer;

        // Null-safe accessor used by Unregister paths that may run after MvcFacade.OnDestroy.
        internal static MvcDiContainer GlobalContainerOrNull => _facadeInstance?._globalContainer;

        // Tracks and initializes proxies registered in the global scope; cleaned up on OnDestroy.
        private GlobalProxyRegistrar _globalProxyRegistrar;

        // Tracks and initializes services registered in the global scope; cleaned up on OnDestroy.
        private mvcExpress.Internal.Initialization.GlobalServiceRegistrar _globalServiceRegistrar;

        // Plain (non-Proxy) IMvcLifecycle instances registered globally via code
        // (FacadeGlobalContainerApi/ModuleGlobalContainerApi) or the [RegisterGlobal] attribute,
        // in the order their OnInitialized() was called. Inspector-sourced services are already
        // tracked separately by _globalServiceRegistrar (Proxies never need this - they have their
        // own OnInitialized/OnCleanup and are torn down by _globalContainer.Dispose() itself, same
        // as any other IDisposable/ProxyBehaviour it owns).
        private readonly List<IMvcLifecycle> _globalLifecycleServices = new List<IMvcLifecycle>();

        // Called from FacadeGlobalContainerApi/ModuleGlobalContainerApi's InitializeRegisteredInstance
        // and from DrainAttributeGlobalRegistrations, right after OnInitialized().
        internal void TrackGlobalLifecycleService(IMvcLifecycle service)
        {
            if (service != null) _globalLifecycleServices.Add(service);
        }

        // Calls OnCleanup() on every tracked global lifecycle service in reverse registration
        // order (last registered, first cleaned up), mirroring ModuleInitializer.CleanupServices().
        // Must run before the global container is disposed/cleared. Idempotent: the
        // list is empty (a no-op loop) after the first call.
        private void CleanupGlobalLifecycleServices()
        {
            for (int i = _globalLifecycleServices.Count - 1; i >= 0; i--)
            {
                var service = _globalLifecycleServices[i];
                try
                {
                    service.OnCleanup();
                }
                catch (Exception ex)
                {
                    // Also logged via Unity's own Debug.LogException, unconditionally - a teardown
                    // exception must be visible even in a clean release build, unlike routine
                    // diagnostic logging.
                    MvcDebug.LogError(
                        $"[MvcFacade] Global service '{service.GetType().FullName}' threw during OnCleanup: {ex.Message}");
                    Debug.LogException(ex);
                }
                finally
                {
                    _globalContainer.ReleaseScopedInstancesFor(service);
                }
            }
            _globalLifecycleServices.Clear();
        }

        // Single bus shared by all modules and global actors; owned here, referenced by modules.
        private readonly MvcMessageBus _messageBus = new MvcMessageBus();

        // Forces facade creation if not yet alive; safe to call from module Awake.
        internal static MvcMessageBus MessageBus => FacadeInstance._messageBus;

        // Actions enqueued by MessengerApi.PublishDeferred from any thread; drained each Update on the main thread.
        private readonly ConcurrentQueue<Action> _deferredPublishes = new ConcurrentQueue<Action>();

        // Guards against a race: a background thread reading a non-null
        // _facadeInstance into a local, then enqueuing into that instance's queue, at the same
        // moment OnDestroy is tearing this facade down. Without a shared gate, that enqueue can
        // land in the queue *after* OnDestroy's own final drain has already run (or after the
        // facade is otherwise gone for the frame), silently stranding the action forever even
        // though the caller was told the enqueue succeeded. OnDestroy takes this lock first, sets
        // _deferredPublishClosed, and only then drains - so every enqueue either (a) completes
        // before the close and is guaranteed to be in the queue when that final drain runs, or
        // (b) observes the close and correctly returns false instead of silently succeeding.
        private readonly object _deferredPublishLock = new object();
        private bool _deferredPublishClosed;

        // These two monotonic counters let Update() (and the pending-count
        // telemetry below) know exactly how much work is queued without ever calling
        // ConcurrentQueue<T>.Count, which sums per-segment head/tail deltas across however many
        // segments the queue currently has - genuinely more segments, and more cost, under exactly
        // the sustained-producer load this feature exists to protect against. A subtraction of two
        // cheap atomic reads is O(1) regardless of queue depth or segment count.
        // _enqueuedSequence is written only under _deferredPublishLock (so the plain increment is
        // race-free) but read from any thread without that lock, so every read goes through
        // Interlocked.Read for tear-safety and cross-thread visibility. _drainedSequence is written
        // only from the main thread (Update/OnDestroy are both Unity lifecycle callbacks), so it
        // never needs Interlocked on the write side - only readers on other threads need the atomic
        // read.
        private long _enqueuedSequence;
        private long _drainedSequence;

        /// <summary>
        /// Per-frame budget knobs for the deferred-publish queue drained in <see cref="Update"/>.
        /// Both default to "unlimited" (the original, always-drain-everything behavior) so existing
        /// projects see no change unless they opt in. Set either to bound how much main-thread time
        /// a burst of <c>PublishDeferred</c> calls from background threads can spend in one frame -
        /// a producer that queues faster than one frame can drain would
        /// otherwise monopolize <see cref="Update"/> indefinitely.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Overload policy is "carries over, never drops": whatever a budget leaves undrained this
        /// frame simply waits and is eligible again next frame (see <see cref="Update"/>'s
        /// frame-start snapshot). This project chose the non-lossy default deliberately - a
        /// deferred publish exists specifically because the caller cannot publish synchronously
        /// right now, so silently dropping it (drop-newest/drop-oldest) would be a correctness
        /// regression a caller has no way to detect. A project that genuinely needs a lossy policy
        /// under sustained overload should implement it at the call site (e.g. rate-limit before
        /// calling <c>PublishDeferred</c>), where it has the domain context to choose what to drop.
        /// </para>
        /// <para>
        /// <b>What the unlimited default does and does not protect against:</b> it fully prevents a
        /// <i>continuous</i> producer from monopolizing every frame forever - the frame-start
        /// snapshot in <see cref="Update"/> already bounds one <c>Update</c> call's work to what was
        /// queued before that frame began, regardless of this budget. It does NOT protect against a
        /// large backlog that is already sitting in the queue when a frame starts (e.g. a burst
        /// enqueued while the game was paused, or before any facade existed to drain it) - under the
        /// default, that entire backlog still drains in the one frame that first observes it, which
        /// can still spike that frame. Set a finite budget if pre-existing backlogs, not just
        /// sustained producers, are a concern for your project.
        /// </para>
        /// <para>
        /// Invalid values are rejected rather than silently accepted - see the setters below. A
        /// negative <see cref="MaxDeferredPublishesPerFrame"/> would otherwise make the drain loop's
        /// own budget check false on its very first comparison, stalling the queue forever, which is
        /// worse than the frame spike this feature exists to prevent.
        /// </para>
        /// </remarks>
        public static int MaxDeferredPublishesPerFrame
        {
            get => _maxDeferredPublishesPerFrame;
            set
            {
                if (value < 0)
                {
                    MvcDebug.LogWarning(
                        $"[MvcFacade] MaxDeferredPublishesPerFrame cannot be negative (got {value}); " +
                        "clamping to 0. A negative value would stall the deferred-publish queue forever " +
                        "instead of merely limiting it.");
                    value = 0;
                }
                _maxDeferredPublishesPerFrame = value;
            }
        }
        private static int _maxDeferredPublishesPerFrame = int.MaxValue;

        /// <summary>See <see cref="MaxDeferredPublishesPerFrame"/>. Time-based sibling budget, in
        /// milliseconds of wall-clock time spent draining the queue this frame.</summary>
        public static double MaxDeferredPublishMillisecondsPerFrame
        {
            get => _maxDeferredPublishMillisecondsPerFrame;
            set
            {
                if (double.IsNaN(value))
                {
                    MvcDebug.LogWarning(
                        "[MvcFacade] MaxDeferredPublishMillisecondsPerFrame cannot be NaN; " +
                        "ignoring and keeping the current value.");
                    return;
                }
                if (value < 0)
                {
                    MvcDebug.LogWarning(
                        $"[MvcFacade] MaxDeferredPublishMillisecondsPerFrame cannot be negative (got {value}); " +
                        "clamping to 0. A negative value would stall the deferred-publish queue forever " +
                        "instead of merely limiting it.");
                    value = 0;
                }
                _maxDeferredPublishMillisecondsPerFrame = value;
            }
        }
        private static double _maxDeferredPublishMillisecondsPerFrame = double.MaxValue;

        /// <summary>
        /// Number of deferred-publish actions currently queued and not yet run. Watch this (or
        /// <see cref="PeakDeferredPublishCount"/>) to notice a producer
        /// that is outrunning the per-frame drain before it becomes a frame-time problem.
        /// </summary>
        /// <remarks>
        /// Computed as enqueued-minus-drained (two sequence counters) rather than
        /// <c>ConcurrentQueue&lt;T&gt;.Count</c> - see the fields' comments above. Safe to poll from
        /// any thread, including every frame from your own code, at constant cost regardless of how
        /// deep the queue is.
        /// </remarks>
        public static int PendingDeferredPublishCount
        {
            get
            {
                var facade = _facadeInstance;
                if (facade == null) return 0;
                return (int)(Interlocked.Read(ref facade._enqueuedSequence) - Interlocked.Read(ref facade._drainedSequence));
            }
        }

        /// <summary>
        /// High-water mark of <see cref="PendingDeferredPublishCount"/> observed at the end of any
        /// <see cref="Update"/> since the current facade instance was created (reset on the next
        /// <c>Awake</c>, including after a Domain Reload-disabled play session restarts).
        /// </summary>
        public static int PeakDeferredPublishCount { get; private set; }

        /// <summary>
        /// Enqueues a publish action to run on the main thread during the next Update.
        /// Called by <see cref="MessengerApi.PublishDeferred{TMessage}()"/> from any thread.
        /// Returns false (and drops the action) when the facade has already been destroyed or is
        /// in the process of being destroyed - see the <see cref="_deferredPublishLock"/> remarks.
        /// </summary>
        internal static bool TryEnqueueDeferredPublish(Action publishAction)
        {
            var facade = _facadeInstance;
            if (facade == null) return false;

            lock (facade._deferredPublishLock)
            {
                if (facade._deferredPublishClosed) return false;
                facade._deferredPublishes.Enqueue(publishAction);
                // Plain increment, not Interlocked - already inside the lock, which is the only
                // writer-side synchronization this field needs. Readers (Update, OnDestroy,
                // PendingDeferredPublishCount) use Interlocked.Read for their side of the contract.
                facade._enqueuedSequence++;
                return true;
            }
        }

        // Called by MvcModule.TryGetMediatorPrefab as a fallback after checking the module's own registry.
        internal static bool TryGetAppMediatorPrefab(Type mediatorType, out GameObject prefab)
        {
            prefab = null;
            var app = InstanceOrNull;
            if (app == null)
                return false;

            return app.TryGetMediatorPrefab(mediatorType, out prefab);
        }

        // Searches ViewPrefabCatalogs in order for a mediator type→prefab mapping.
        private bool TryGetMediatorPrefab(Type mediatorType, out GameObject prefab)
        {
            prefab = null;

            if (mediatorType == null)
                return false;

            var catalogs = ActiveViewPrefabCatalogs;
            if (catalogs == null)
                return false;

            for (int i = 0; i < catalogs.Length; i++)
            {
                var catalog = catalogs[i];
                if (catalog == null)
                    continue;

                if (catalog.TryGetMediatorPrefab(mediatorType, out prefab))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Returns whether a module of the supplied concrete type is currently alive and registered.
        /// </summary>
        /// <param name="moduleType">Concrete <see cref="MvcModule"/> type to check.</param>
        /// <returns><c>true</c> when a module of that type is registered.</returns>
        public bool IsModuleRegistered(Type moduleType)
        {
            if (moduleType == null) return false;
            lock (_moduleLock)
            {
                return _modules.ContainsKey(moduleType);
            }
        }

        /// <summary>
        /// Attempts to retrieve the cached display name of a registered module.
        /// </summary>
        /// <param name="moduleType">Concrete module type to query.</param>
        /// <param name="name">Cached <see cref="MvcModule.IdName"/> when the module is registered; otherwise <c>null</c>.</param>
        /// <returns><c>true</c> when the module is registered and <paramref name="name"/> was set.</returns>
        public bool TryGetModuleName(Type moduleType, out string name)
        {
            if (moduleType == null)
            {
                name = null;
                return false;
            }
            lock (_moduleLock)
            {
                if (_modules.TryGetValue(moduleType, out var module))
                {
                    name = module.IdName; // Use cached name
                    return true;
                }
                name = null;
                return false;
            }
        }

        /// <summary>
        /// Returns a snapshot of all currently registered modules mapped to their display names.
        /// Allocates a new dictionary on each call; intended for tooling and console display.
        /// </summary>
        public Dictionary<Type, string> GetAllRegisteredModules()
        {
            lock (_moduleLock)
            {
                var result = new Dictionary<Type, string>();
                foreach (var kvp in _modules)
                {
                    result[kvp.Key] = kvp.Value.IdName; // Use cached name
                }
                return result;
            }
        }

        private bool _initialized; // guards InitializeIfNeeded() so it runs exactly once per instance

        // [RegisterGlobal] failures found so far this drain - same non-conditional trick as
        // ModuleInitializer._compositionFailures. Cleared at the start of each drain so a manual
        // re-drain (e.g. in tests) doesn't accumulate stale entries.
        private readonly List<string> _globalCompositionFailures = new List<string>();

        /// <summary>
        /// Unity lifecycle. Enforces the singleton pattern, resets the quit flag (needed when
        /// Domain Reload is disabled), and runs one-time initialization.
        /// </summary>
        private void Awake()
        {
            // Reset _isQuitting so FacadeInstance works correctly when Domain Reload is disabled.
            // Without this, the flag stays true from the previous play session and FacadeInstance
            // returns null on every subsequent session, breaking all module initialization.
            _isQuitting = false;

            if (_facadeInstance != null && _facadeInstance != this)
            {
                MvcDebug.LogWarning("Duplicate MvcFacade instance detected. Destroying duplicate.");
                Destroy(gameObject);
                return;
            }

            // Same Domain-Reload-disabled reasoning as _isQuitting above: a stale high-water mark
            // from the previous play session should not carry into this one. Reset only after this
            // instance is accepted as the singleton (above) - a duplicate must not be able to wipe
            // the real singleton's telemetry on its way to self-destructing.
            PeakDeferredPublishCount = 0;

            _facadeInstance = this;
            InitializeIfNeeded();
        }

        // Spawns AutoStart entries after all scene Awake() calls have completed.
        private void Start()
        {
            if (!Application.isPlaying)
                return;

            StartConfiguredModules();
        }

        /// <summary>
        /// Unity lifecycle. Drains publish actions queued via <see cref="TryEnqueueDeferredPublish"/>
        /// so they execute on the main thread before game logic runs this frame.
        /// </summary>
        /// <remarks>
        /// Drains only a frame-start snapshot of the queue, not "until observed empty".
        /// Under the default unlimited budget this drains everything that was queued before this
        /// Update began - identical to the old behavior - but never chases new arrivals a
        /// concurrent producer enqueues *during* this same drain; those simply wait for next
        /// frame's snapshot. That bounds one Update call's deferred-publish work to a size known
        /// at its start, which is what actually stops a continuous background producer from
        /// monopolizing a single frame; <see cref="MaxDeferredPublishesPerFrame"/>/
        /// <see cref="MaxDeferredPublishMillisecondsPerFrame"/> then let a project also cap that
        /// per-frame work below the snapshot size when even one frame's backlog is too much.
        /// <para>
        /// Perf follow-up: the snapshot and pending-count telemetry are both derived from
        /// <see cref="_enqueuedSequence"/>/<see cref="_drainedSequence"/> instead of
        /// <c>ConcurrentQueue&lt;T&gt;.Count</c>, so this method costs the same two cheap atomic
        /// reads every frame whether the queue is empty or deep, instead of a cost that scales with
        /// how many internal segments the queue has accumulated.
        /// </para>
        /// </remarks>
        private void Update()
        {
            long targetSequence = Interlocked.Read(ref _enqueuedSequence);

            // Sample the peak BEFORE draining, not just after: under the (default) unlimited
            // budget the drain loop below empties the queue down to targetSequence every frame, so
            // a post-drain-only sample would almost always read ~0 regardless of how large the
            // burst was - defeating the whole point of a high-water mark. This captures the true
            // frame-start backlog size; the post-drain sample below still runs too, to also catch a
            // concurrent producer that enqueues past targetSequence while this Update is draining.
            long frameStartPending = targetSequence - _drainedSequence;
            if (frameStartPending > PeakDeferredPublishCount)
                PeakDeferredPublishCount = (int)frameStartPending;

            int budget = MaxDeferredPublishesPerFrame;
            double millisecondBudget = MaxDeferredPublishMillisecondsPerFrame;
            // Stopwatch only allocated when a time budget is actually configured - the default
            // (unbudgeted) path stays allocation-free, consistent with this method previously
            // doing no per-frame allocation of its own.
            Stopwatch timeBudgetStopwatch = millisecondBudget < double.MaxValue ? Stopwatch.StartNew() : null;

            int processed = 0;
            while (_drainedSequence < targetSequence && processed < budget)
            {
                if (timeBudgetStopwatch != null && timeBudgetStopwatch.Elapsed.TotalMilliseconds >= millisecondBudget)
                    break;

                if (!_deferredPublishes.TryDequeue(out var action))
                    break; // Shouldn't happen while _drainedSequence < targetSequence, but never spin past reality.

                _drainedSequence++;
                processed++;

                try
                {
                    action();
                }
                catch (Exception e)
                {
                    // One bad deferred publisher must not block the rest of the queue from
                    // draining this frame - log and continue, matching Unity's own event-loop
                    // behavior of isolating per-callback exceptions.
                    Debug.LogException(e);
                }
            }

            long pending = Interlocked.Read(ref _enqueuedSequence) - _drainedSequence;
            if (pending > PeakDeferredPublishCount)
                PeakDeferredPublishCount = (int)pending;
        }

        /// <summary>
        /// Unity lifecycle. Ticks the deferred-destruction countdown and destroys the facade
        /// once all modules have unregistered and the grace period has elapsed.
        /// </summary>
        private void LateUpdate()
        {
            if (_destructionCountdown > 0)
            {
                _destructionCountdown--;
                return;
            }

            if (_destructionCountdown == 0)
            {
                // Grace period expired, check if we should still destroy
                bool shouldDestroy;
                lock (_moduleLock)
                {
                    shouldDestroy = _modules.Count == 0;
                }

                if (shouldDestroy && !_isQuitting)
                {
                    var go = gameObject;
                    _facadeInstance = null;
                    Destroy(go);
                }

                _destructionCountdown = -1;
            }
        }

        /// <summary>
        /// Clears all global container registrations and reinitializes global registrars.
        /// </summary>
        /// <remarks>
        /// <para>
        /// Primarily intended for tests and deep resets. Runtime gameplay code should prefer
        /// explicit unregister operations for known transient global dependencies.
        /// </para>
        /// <para>
        /// Reentrancy/lifecycle contract: every actor's
        /// <c>OnCleanup()</c> that this call can reach runs before the container is cleared, and
        /// plain code/attribute/Inspector-sourced <c>IMvcLifecycle</c> services are fully
        /// round-trippable - re-registered and re-initialized correctly, with an <c>OnInitialized</c>
        /// always paired to the <c>OnCleanup</c> that preceded it. Inspector-configured global
        /// <see cref="ProxyBehaviour"/> instances are a documented exception: their
        /// <c>OnCleanup()</c> runs correctly here, but their GameObject is still destroyed as a side
        /// effect of the container clear that follows (the same disposal path any other registered
        /// instance goes through), so they do not come back as a working, re-initialized instance
        /// afterward the way a plain service does - re-adding one requires re-configuring it in the
        /// Inspector before the next play/initialize, not merely calling this method again.
        /// </para>
        /// </remarks>
        public void ClearGlobalContainer()
        {
            // Lifecycle cleanup must run before the container is cleared/disposed.
            // This must also reach Inspector-sourced global services: _globalServiceRegistrar
            // tracks those separately from _globalLifecycleServices (see the field comment above)
            // and is not touched by CleanupGlobalLifecycleServices(), so without this call an
            // Inspector service's OnCleanup() would never run before InitializeGlobalServices()
            // (below) re-registers and re-initializes the same instance.
            CleanupGlobalLifecycleServices();
            _globalServiceRegistrar?.Cleanup();

            // Same reasoning, for Inspector-sourced global proxies. Runs OnCleanup()
            // and resets the initialized flags now, synchronously, rather than leaving it to
            // whatever OnDestroy() cascade eventually fires once the container clear below
            // destroys these GameObjects (Unity defers that to end-of-frame in Play Mode).
            _globalProxyRegistrar?.ResetTrackedProxiesForContainerClear();
            _globalContainer.Clear();

            // Re-initialize global registrars to clear their tracking lists
            InitializeGlobalServices();
            InitializeGlobalProxies();
        }

        /// <summary>
        /// Enables diagnostic logging for all modules.
        /// </summary>
        public static void EnableGlobalLogging()  => mvcExpress.Logging.MvcLogInternal.LoggingEnabled = true;

        /// <summary>
        /// Disables diagnostic logging for all modules.
        /// </summary>
        public static void DisableGlobalLogging() => mvcExpress.Logging.MvcLogInternal.LoggingEnabled = false;

        /// <summary>
        /// Unity lifecycle. Sets <c>_isQuitting</c> so <see cref="FacadeInstance"/> stops
        /// creating new instances during application shutdown.
        /// </summary>
        private void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        /// <summary>
        /// Unity lifecycle. Cleans up every tracked global lifecycle service and registrar, then
        /// disposes the global container and the message bus, then clears the static facade
        /// reference.
        /// </summary>
        /// <remarks>
        /// Order matters: actors are cleaned up (their <c>OnCleanup()</c> may still
        /// need to publish/unsubscribe) BEFORE the container that owns them is disposed, and the
        /// container is disposed BEFORE the message bus so that cleanup code can still safely use
        /// it.
        /// <para>
        /// Closes the deferred-publish gate first, before any other teardown step, so no
        /// enqueue race (see <see cref="_deferredPublishLock"/>) can land an action after this
        /// point; then immediately drains whatever is already queued - including anything that
        /// raced in just before the close - while the message bus is still alive to deliver it.
        /// This is a full, unbudgeted drain (not the budgeted <see cref="Update"/> snapshot): the
        /// facade is not getting another frame, so there is no "next frame" left to carry work over
        /// to, and losing queued publishes silently on teardown would defeat the whole point of the
        /// race fix.
        /// </para>
        /// </remarks>
        private void OnDestroy()
        {
            lock (_deferredPublishLock)
            {
                _deferredPublishClosed = true;
            }

            while (_deferredPublishes.TryDequeue(out var action))
            {
                _drainedSequence++;

                try
                {
                    action();
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }

            CleanupGlobalLifecycleServices();

            if (_globalProxyRegistrar != null)
            {
                _globalProxyRegistrar.Cleanup();
                _globalProxyRegistrar = null;
            }

            if (_globalServiceRegistrar != null)
            {
                _globalServiceRegistrar.Cleanup();
                _globalServiceRegistrar = null;
            }

            // Disposes every uniquely-owned registration exactly once - IDisposable services/proxies
            // and ProxyBehaviour GameObjects alike - covering Code- and
            // Attribute-sourced global registrations that neither registrar above tracks.
            _globalContainer.Dispose();

            _messageBus?.Dispose();

            if (_facadeInstance == this)
            {
                _facadeInstance = null;
            }
        }

        // Set when a previous InitializeIfNeeded() call threw. Without this, a second call would
        // just see _initialized still false and silently retry the whole sequence - re-running
        // non-idempotent hooks (RegisterGlobalServices/RegisterGlobalProxies) and re-hitting the
        // same failure. Mirrors ModuleInitializer's fail-once contract for a module.
        private Exception _initializationFailure;

        // Shared by both places that reject use of an already-failed facade: FacadeInstance's
        // early-return branch (checked directly, never via InitializeIfNeeded() - see the long
        // comment there about why) and InitializeIfNeeded() itself (reached via the find-or-create
        // branch, e.g. an old failed facade found by FindAnyObjectByType after a domain reload).
        private static InvalidOperationException BuildAlreadyFailedException(Exception failure)
        {
            return new InvalidOperationException(
                "[MvcFacade] Cannot use the facade after global initialization failed. " +
                $"Previous error: {failure.Message}", failure);
        }

        // Idempotent - guarded by _initialized. Actual work lives in InitializeIfNeededCore(); this
        // wrapper only adds the fail-once behavior described on _initializationFailure above.
        private void InitializeIfNeeded()
        {
            if (_initialized) return;

            if (_initializationFailure != null)
            {
                throw BuildAlreadyFailedException(_initializationFailure);
            }

            try
            {
                InitializeIfNeededCore();
                _initialized = true;
            }
            catch (Exception ex)
            {
                _initializationFailure = ex;
                throw;
            }
        }

        // Scans assemblies for attribute-based registrations and initializes global service/proxy
        // registrars. Split out from InitializeIfNeeded() so that wrapper can catch every exception
        // this raises, including MvcCompositionException from a bad [RegisterGlobal] type.
        private void InitializeIfNeededCore()
        {
            DontDestroyOnLoad(gameObject);

            // Scan assemblies for [Register], [Bind], [Attach], [RegisterGlobal], [StartupModule]
            // attributes ONCE (per application lifetime). Results are cached in separate lists/dicts
            // for fast lookup during module initialization.
#if MVC_EXPRESS_NO_ATTRIBUTE
            // Attribute style disabled via Project Settings > mvcExpress > Composition.
#else
            mvcExpress.Internal.Initialization.AttributeScanner.ScanAssemblies();
#endif

            // Keep empty-by-default in edit mode; create containers on-demand.
#if MVC_EXPRESS_NO_UNITY
            // Unity style disabled via Project Settings > mvcExpress > Composition.
#else
            if (Application.isPlaying)
            {
                EnsureGlobalRegistries();
            }

            InitializeGlobalServices();
            InitializeGlobalProxies();
#endif

            // Drain [RegisterGlobal] attribute entries into the global container after the
            // Inspector-driven registries have been processed.
#if MVC_EXPRESS_NO_ATTRIBUTE
            // Attribute style disabled via Project Settings > mvcExpress > Composition.
#else
            DrainAttributeGlobalRegistrations();
#endif

            // Code-override hook: subclasses register application-wide dependencies here, before
            // any module has registered with the facade, so there is no race condition. Runs last
            // so code registrations win over Inspector/attribute ones for the same type - matching
            // the Inspector -> Attribute -> Code order used by MvcModule's initialization pipeline.
            RegisterGlobalServices();
            RegisterGlobalProxies();
        }

        // Discovers or creates the "Global Services" and "Global Proxies" child GameObjects,
        // then positions them first in the hierarchy for visual clarity.
        private void EnsureGlobalRegistries()
        {
            _globalServiceRegistry = EnsureContainer(_globalServiceRegistry, "Global Services");
            _globalProxyRegistry = EnsureContainer(_globalProxyRegistry, "Global Proxies");

            // Best-effort sibling ordering.
            if (_globalServiceRegistry != null) _globalServiceRegistry.transform.SetSiblingIndex(0);
            if (_globalProxyRegistry != null) _globalProxyRegistry.transform.SetSiblingIndex(Mathf.Min(1, transform.childCount - 1));
        }

        // Creates or resets the GlobalProxyRegistrar and registers any proxies configured in
        // the Inspector GlobalProxyRegistry. Called from InitializeIfNeeded and ClearGlobalContainer.
        private void InitializeGlobalProxies()
        {
            if (_globalProxyRegistrar == null)
            {
                _globalProxyRegistrar = new GlobalProxyRegistrar(_globalContainer, _messageBus);
            }

            _globalProxyRegistrar.ClearTrackingLists();

            if (_globalProxyRegistry != null)
            {
                WarnIfGlobalProxyRegistryUsesUnityStyle();
                _globalProxyRegistrar.RegisterSerializedGlobalProxyMappings(_globalProxyRegistry.ProxyMappings, _globalProxyRegistry.transform);
            }

            _globalProxyRegistrar.CompleteProxyInitialization();
        }

        // Creates or resets the GlobalServiceRegistrar and registers any services configured in
        // the Inspector GlobalServiceRegistry. Called from InitializeIfNeeded and ClearGlobalContainer.
        private void InitializeGlobalServices()
        {
            if (_globalServiceRegistrar == null)
            {
                _globalServiceRegistrar = new mvcExpress.Internal.Initialization.GlobalServiceRegistrar(_globalContainer);
            }

            _globalServiceRegistrar.ClearTrackingLists();

            if (_globalServiceRegistry != null)
            {
                WarnIfGlobalServiceRegistryUsesUnityStyle();
                _globalServiceRegistrar.RegisterSerializedGlobalServiceBehaviours(_globalServiceRegistry.ServiceMappings, _globalServiceRegistry.transform);
            }

            _globalServiceRegistrar.CompleteServiceInitialization();
        }

        // Returns 'existing' if non-null; searches children by component type, then by child name,
        // and finally creates a new child GameObject with 'defaultName' as a fallback.
        private T EnsureContainer<T>(T existing, string defaultName) where T : MonoBehaviour
        {
            if (existing != null)
            {
                return existing;
            }

            // Prefer component search (code-only workflow)
            var found = GetComponentInChildren<T>(includeInactive: true);
            if (found != null)
            {
                return found;
            }

            // Fallback to name search
            var t = transform.Find(defaultName);
            if (t != null)
            {
                var c = t.GetComponent<T>();
                if (c != null)
                {
                    return c;
                }
            }

            // Create container
            var go = new GameObject(defaultName);
            go.transform.SetParent(transform, false);
            var created = go.AddComponent<T>();

#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                go.hideFlags = HideFlags.DontSaveInBuild | HideFlags.DontSaveInEditor;
            }
#endif

            return created;
        }

        /// <summary>
        /// Registers a module with the facade. Called automatically from
        /// <see cref="MvcModule.Awake"/>. Throws if a module of the same type is already registered.
        /// </summary>
        /// <param name="module">Module instance to register.</param>
        /// <remarks>Also cancels any pending deferred self-destruction.</remarks>
        internal void RegisterModule(MvcModule module)
        {
            if (module == null)
            {
                throw new ArgumentNullException(nameof(module));
            }

            var moduleType = module.ModuleType; // Use cached type
            bool alreadyExists;
            int totalCount;

            lock (_moduleLock)
            {
                // Cancel any pending destruction
                _destructionCountdown = -1;

                alreadyExists = _modules.ContainsKey(moduleType);

                if (!alreadyExists)
                {
                    _modules.Add(moduleType, module);
                    totalCount = _modules.Count;
                }
                else
                {
                    totalCount = 0;
                }
            }

            if (alreadyExists)
            {
                throw new InvalidOperationException(
                    $"[MvcFacade] Module of type '{module.IdName}' is already registered. Only one instance of each module type is allowed.");
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.FireModuleRegistered(module.ModuleType);
#endif

            _ = FacadeInstance;
        }

        /// <summary>
        /// Unregisters a module from the facade. Called automatically from
        /// <see cref="MvcModule.OnDestroy"/>. When the registry becomes empty, starts the
        /// deferred self-destruction countdown.
        /// </summary>
        /// <param name="module">Module instance to remove.</param>
        internal void UnregisterModule(MvcModule module)
        {
            if (module == null)
            {
                return;
            }

            var moduleType = module.ModuleType; // Use cached type
            bool wasRemoved;

            lock (_moduleLock)
            {
                // Only remove the entry if it still holds THIS instance. A module that was
                // rejected as a duplicate by RegisterModule (or any other instance that never
                // owned the slot) must not be able to evict a different, currently-registered
                // instance of the same type during its own teardown.
                if (_modules.TryGetValue(moduleType, out var current) && ReferenceEquals(current, module))
                {
                    _modules.Remove(moduleType);
                    wasRemoved = true;
                }
                else
                {
                    wasRemoved = false;
                }

                if (wasRemoved)
                {
                    if (_modules.Count == 0 && !_isQuitting)
                    {
                        // Start grace period instead of immediate destruction
                        _destructionCountdown = DestructionGracePeriodFrames;
                    }
                }
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (wasRemoved)
            {
                MvcPluginBus.FireModuleUnregistered(moduleType);
            }
#endif
        }

        /// <summary>
        /// Returns the registered module of type <typeparamref name="T"/>, or <c>null</c> when
        /// no such module is currently alive.
        /// </summary>
        /// <typeparam name="T">Concrete module type.</typeparam>
        /// <returns>The registered module instance, or <c>null</c>.</returns>
        public T GetModule<T>() where T : MvcModule
        {
            lock (_moduleLock)
            {
                if (_modules.TryGetValue(typeof(T), out var module))
                {
                    return module as T;
                }
                return null;
            }
        }

        /// <summary>
        /// Attempts to get a registered module by concrete type.
        /// </summary>
        /// <typeparam name="T">Concrete module type.</typeparam>
        /// <param name="module">Registered module instance when found; otherwise <c>null</c>.</param>
        /// <returns><c>true</c> when the module is registered.</returns>
        public bool TryGetModule<T>(out T module) where T : MvcModule
        {
            lock (_moduleLock)
            {
                if (_modules.TryGetValue(typeof(T), out var m))
                {
                    module = m as T;
                    return true;
                }
                module = null;
                return false;
            }
        }

        /// <summary>
        /// Attempts to get a registered module by runtime type.
        /// </summary>
        /// <param name="moduleType">Concrete module type to query.</param>
        /// <param name="module">Registered module instance when found; otherwise <c>null</c>.</param>
        /// <returns><c>true</c> when the module is registered.</returns>
        public bool TryGetModule(Type moduleType, out MvcModule module)
        {
            if (moduleType == null)
            {
                module = null;
                return false;
            }
            lock (_moduleLock)
            {
                return _modules.TryGetValue(moduleType, out module);
            }
        }

        /// <summary>
        /// Returns the registered module for the supplied runtime type, or <c>null</c> when
        /// not found.
        /// </summary>
        /// <param name="moduleType">Concrete module type to query.</param>
        /// <returns>The registered module instance, or <c>null</c>.</returns>
        public MvcModule GetModule(Type moduleType)
        {
            if (moduleType == null) return null;
            lock (_moduleLock)
            {
                _modules.TryGetValue(moduleType, out var module);
                return module;
            }
        }

        /// <summary>
        /// Initializes and attaches a mediator to an already-registered module, wiring it to
        /// that module's DI container and message bus.
        /// </summary>
        /// <param name="moduleType">Concrete module type that should own the mediator.</param>
        /// <param name="mediator">Mediator instance to initialize and attach.</param>
        /// <returns><c>true</c> when the target module was found and accepted the mediator.</returns>
        /// <remarks>
        /// Prefer <see cref="MvcModule.MediatorHub"/> from inside the module itself. Use this
        /// method only from external code that has a reference to the facade but not to the
        /// specific module.
        /// </remarks>
        public bool AttachMediator(Type moduleType, MediatorBehaviour mediator)
        {
            if (mediator == null || moduleType == null) return false;

            MvcModule module;
            lock (_moduleLock)
            {
                if (!_modules.TryGetValue(moduleType, out module))
                {
                    return false;
                }
            }

            module.InitializeMediator(mediator);
            return true;
        }

        /// <summary>
        /// Spawns all <see cref="StartupModules"/> entries that have <c>AutoStart</c> set to
        /// <c>true</c>, in list order. Called automatically from <c>Start</c>; can also be
        /// called manually to re-trigger after a configuration change.
        /// </summary>
        public void StartConfiguredModules()
        {
            var entries = new List<StartupModuleLaunch>(16);

            var startupModules = ActiveStartupModules;
#if MVC_EXPRESS_NO_UNITY
            // Unity style disabled via Project Settings > mvcExpress > Composition.
#else
            if (startupModules != null)
            {
                for (int i = 0; i < startupModules.Length; i++)
                {
                    var entry = startupModules[i];
                    if (entry == null || !entry.AutoStart)
                        continue;

                    WarnForStartupModuleStyle(entry);
                    entries.Add(StartupModuleLaunch.FromEntry(entry));
                }
            }
#endif

            // Append [StartupModule] attribute entries that have no matching Inspector entry.
#if MVC_EXPRESS_NO_ATTRIBUTE
            // Attribute style disabled via Project Settings > mvcExpress > Composition.
#else
            DrainAttributeStartupModules(entries);
#endif

            for (int i = 0; i < entries.Count; i++)
            {
                SpawnStartupModule(entries[i], null, null);
            }
        }

        // Routes a launch descriptor to either prefab-based or code-based spawning.
        private MvcModule SpawnStartupModule(StartupModuleLaunch launch, Transform moduleParent, Transform viewContainer)
        {
            if (launch.StartupEntry != null)
                return SpawnModuleFromEntry(launch.StartupEntry, moduleParent, viewContainer);

            return SpawnCodeModuleIfNeeded(launch.ModuleType, moduleParent, viewContainer);
        }

        // Resolves the concrete type from the entry, then delegates to prefab or code spawning.
        // Returns the already-registered module if one exists for that type.
        private MvcModule SpawnModuleFromEntry(MvcStartupModuleEntry entry, Transform moduleParent, Transform viewContainer)
        {
            if (entry == null)
                return null;

            var moduleType = entry.ResolveModuleType();
            if (moduleType == null || !typeof(MvcModule).IsAssignableFrom(moduleType) || moduleType.IsAbstract)
            {
#if UNITY_EDITOR
                MvcDebug.LogWarning("MvcFacade has an invalid startup module entry.");
#endif
                return null;
            }

            // If already registered, do not spawn again.
            if (IsModuleRegistered(moduleType))
            {
                return GetModule(moduleType);
            }

            if (entry.ModulePrefab != null)
            {
                return SpawnModulePrefab(entry.ModulePrefab, moduleType, moduleParent, viewContainer);
            }

            return SpawnCodeModuleIfNeeded(moduleType, moduleParent, viewContainer);
        }

        // Instantiates a module prefab in deactivated state, validates the root MvcModule type,
        // then activates it so Unity calls Awake and triggers module initialization.
        private MvcModule SpawnModulePrefab(GameObject prefab, Type moduleType, Transform moduleParent, Transform viewContainer)
        {
            // Ensure there is an MvcModule on prefab root.
            var prefabModule = prefab.GetComponent<MvcModule>();
            if (prefabModule == null)
            {
                MvcDebug.LogError($"Module prefab '{prefab.name}' has no MvcModule on root.");
                return null;
            }

            if (prefabModule.ModuleType != moduleType)
            {
                MvcDebug.LogError($"Module prefab '{prefab.name}' does not match startup module type '{moduleType.FullName}'.");
                return null;
            }

            var prefabWasActive = prefab.activeSelf;
            if (prefabWasActive)
            {
                prefab.SetActive(false);
            }

            var instance = Instantiate(prefab, moduleParent);
            instance.name = prefab.name;

            if (prefabWasActive)
            {
                prefab.SetActive(true);
            }

            var module = instance.GetComponent<MvcModule>();
            if (module == null)
            {
                MvcDebug.LogError($"Instantiated prefab '{prefab.name}' but it has no MvcModule on root.");
                Destroy(instance);
                return null;
            }

            // Set the view container if provided (before Awake/initialization)
            if (viewContainer != null)
            {
                module.SetModuleViewContainer(viewContainer);
            }

            // Module registration happens in module.Awake() after activation.
            instance.SetActive(true);
            return module;
        }

        // Creates a module via AddComponent if one of that type is not already registered.
        // Uses deactivate-add-activate to defer Awake until the viewContainer is configured.
        private MvcModule SpawnCodeModuleIfNeeded(Type moduleType, Transform moduleParent, Transform viewContainer)
        {
            if (moduleType == null)
                return null;

            if (TryGetModule(moduleType, out var existing) && existing != null)
                return existing;

            MvcCompositionStyleWarning.WarnIfDisabled(
                MvcCompositionStyle.Code,
                $"code-created module '{moduleType.Name}'");

            var go = new GameObject(moduleType.Name);
            go.SetActive(false);
            go.transform.SetParent(moduleParent, false);

            var module = go.AddComponent(moduleType) as MvcModule;
            if (module == null)
            {
                MvcDebug.LogError($"Failed to create module type '{moduleType.FullName}'.");
                Destroy(go);
                return null;
            }

            if (viewContainer != null)
            {
                module.SetModuleViewContainer(viewContainer);
            }

            go.SetActive(true);
            return module;
        }

        /// <summary>
        /// Spawns the startup-entry-backed module of type <typeparamref name="T"/> at scene root,
        /// or returns the already-registered instance if one exists.
        /// </summary>
        /// <typeparam name="T">Concrete module type to spawn.</typeparam>
        /// <returns>The spawned or existing module, or <c>null</c> when no enabled startup entry
        /// for <typeparamref name="T"/> exists.</returns>
        public static T SpawnModule<T>() where T : MvcModule
        {
            return SpawnModule(typeof(T), null, null) as T;
        }

        /// <summary>
        /// Spawns the startup-entry-backed module of the supplied type at scene root, or returns
        /// the already-registered instance if one exists.
        /// </summary>
        /// <param name="moduleType">Concrete module type to spawn.</param>
        /// <returns>The spawned or existing module, or <c>null</c> when no enabled startup entry
        /// exists for <paramref name="moduleType"/>.</returns>
        public static MvcModule SpawnModule(Type moduleType)
        {
            return SpawnModule(moduleType, null, null);
        }

        /// <summary>
        /// Spawns the startup-entry-backed module with optional hierarchy overrides, or returns
        /// the already-registered instance if one exists.
        /// </summary>
        /// <param name="moduleType">Concrete module type to spawn.</param>
        /// <param name="moduleParent">Optional parent transform for the new module GameObject.</param>
        /// <param name="viewContainer">Optional default parent transform for mediator prefabs
        /// spawned by this module.</param>
        /// <returns>The spawned or existing module, or <c>null</c> when no enabled startup entry
        /// exists for <paramref name="moduleType"/>.</returns>
        public static MvcModule SpawnModule(
            Type moduleType,
            Transform moduleParent,
            Transform viewContainer)
        {
            if (moduleType == null) return null;

            var app = FacadeInstance;
            if (app == null)
                return null;

            if (app.TryGetModule(moduleType, out var existing) && existing != null)
                return existing;

            if (app.TryGetActiveModuleEntry(moduleType, out var entry))
            {
                return app.SpawnModuleFromEntry(entry, moduleParent, viewContainer);
            }

#if UNITY_EDITOR
            MvcDebug.LogWarning($"No startup entry found for module type '{moduleType.Name}'.");
#endif
            return null;
        }

        // Searches StartupModules for an enabled entry whose resolved type matches moduleType.
        private bool TryGetActiveModuleEntry(Type moduleType, out MvcStartupModuleEntry entry)
        {
            entry = null;

            if (moduleType == null)
                return false;

            var startupModules = ActiveStartupModules;
            if (startupModules == null)
                return false;

            for (int i = 0; i < startupModules.Length; i++)
            {
                var candidate = startupModules[i];
                if (candidate == null)
                    continue;

                if (candidate.ResolveModuleType() == moduleType)
                {
                    entry = candidate;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Transient value object that captures the resolved type and entry reference for a
        /// single startup module so they can be dispatched together.
        /// </summary>
        private readonly struct StartupModuleLaunch
        {
            /// <summary>Concrete module type resolved from the startup entry.</summary>
            public readonly Type ModuleType;

            /// <summary>
            /// Source entry that owns the optional prefab and view-container configuration;
            /// null when this descriptor was created from a plain type (no entry).
            /// </summary>
            public readonly MvcStartupModuleEntry StartupEntry;

            private StartupModuleLaunch(Type moduleType, MvcStartupModuleEntry startupEntry)
            {
                ModuleType = moduleType;
                StartupEntry = startupEntry;
            }

            /// <summary>
            /// Creates a launch descriptor from an Inspector-configured startup entry.
            /// </summary>
            public static StartupModuleLaunch FromEntry(MvcStartupModuleEntry entry)
            {
                return new StartupModuleLaunch(entry.ResolveModuleType(), entry);
            }

            /// <summary>
            /// Creates a code-only launch descriptor from a bare module type.
            /// Used by the <c>[StartupModule]</c> attribute drain path.
            /// No prefab or view-container configuration is available for attribute-sourced modules.
            /// </summary>
            public static StartupModuleLaunch FromType(Type moduleType)
            {
                return new StartupModuleLaunch(moduleType, null);
            }
        }

        // Registers all types decorated with [RegisterGlobal] into the global DI container.
        // Called once from InitializeIfNeeded after the Inspector-driven registries have run.
        // Plain C# types are created with Activator.CreateInstance; MonoBehaviour types (ProxyBehaviour
        // or MonoBehaviour service) are resolved to a scene instance under "Global Proxies"/"Global
        // Services" - reused if already tracked or hand-placed, created if none exists.
        // Every entry is still attempted even if an earlier one is invalid; throws one aggregated
        // MvcCompositionException at the end if anything failed (see the Clear()/throw below).
        private void DrainAttributeGlobalRegistrations()
        {
            var entries = mvcExpress.Internal.Initialization.AttributeScanner.GetGlobalRegistrationMetadata();
            if (entries.Count == 0)
                return;

            _globalCompositionFailures.Clear();

            // Collect instances so we can call CompleteInitialization after all are registered
            // (mirrors the deferred-init pattern used by GlobalProxyRegistrar and GlobalServiceRegistrar).
            var pendingProxies = new List<Proxy>(entries.Count);
            var pendingProxyBehaviours = new List<ProxyBehaviour>(entries.Count);
            var pendingObjects = new List<object>(entries.Count);

            // Group stacked [RegisterGlobal] attributes on the same class so they share ONE
            // instance registered under every requested logic/view type, instead of one instance
            // (and one container registration) per attribute.
            var groups = new Dictionary<Type, List<mvcExpress.Internal.Initialization.GlobalRegistrationMetadata>>();
            for (int i = 0; i < entries.Count; i++)
            {
                var meta = entries[i];
                if (!groups.TryGetValue(meta.ConcreteType, out var group))
                    groups[meta.ConcreteType] = group = new List<mvcExpress.Internal.Initialization.GlobalRegistrationMetadata>();
                group.Add(meta);
            }

            foreach (var group in groups.Values)
            {
                DrainAttributeGlobalRegistrationGroup(group, pendingProxies, pendingProxyBehaviours, pendingObjects);
            }

            // Complete deferred initialization in the same order as registration.
            for (int i = 0; i < pendingProxies.Count; i++)
            {
                pendingProxies[i].CompleteInitialization();
            }

            for (int i = 0; i < pendingProxyBehaviours.Count; i++)
            {
                pendingProxyBehaviours[i].CompleteInitialization();
            }

            for (int i = 0; i < pendingObjects.Count; i++)
            {
                // Inject [Inject] members before calling OnInitialized so dependencies are ready.
                mvcExpress.Internal.DependencyInjection.MvcInjectionUtility.InjectMembers(
                    pendingObjects[i], _globalContainer, useViewScope: false);

                if (pendingObjects[i] is IMvcLifecycle lifecycle)
                {
                    lifecycle.OnInitialized();
                    TrackGlobalLifecycleService(lifecycle);
                }
            }

            // Everything was attempted and successes are fully initialized - now abort if any entry
            // was invalid, reporting every failure at once instead of just the first.
            if (_globalCompositionFailures.Count > 0)
            {
                throw new MvcCompositionException("global registration", _globalCompositionFailures);
            }
        }

        // Resolves/creates one instance for a group of stacked [RegisterGlobal] attributes sharing
        // the same concrete type, registers it into the global container under every requested
        // logic/view type across the whole group, and queues it for deferred initialization.
        private void DrainAttributeGlobalRegistrationGroup(
            List<mvcExpress.Internal.Initialization.GlobalRegistrationMetadata> group,
            List<Proxy> pendingProxies,
            List<ProxyBehaviour> pendingProxyBehaviours,
            List<object> pendingObjects)
        {
            var meta0 = group[0];
            var concreteType = meta0.ConcreteType;

            MvcCompositionStyleWarning.WarnIfDisabled(
                MvcCompositionStyle.Attribute,
                $"[RegisterGlobal] global registration of '{concreteType.Name}'");

            object instance;

            if (meta0.IsMonoBehaviour)
            {
                var parentContainer = meta0.IsProxy ? _globalProxyRegistry.transform : _globalServiceRegistry.transform;

                // Prefer an instance already tracked via Unity registry (Inspector-wired).
                MonoBehaviour preTracked = null;
                if (meta0.IsProxy)
                {
                    var proxyMappings = _globalProxyRegistry.ProxyMappings;
                    for (int m = 0; m < proxyMappings.Length; m++)
                    {
                        var p = proxyMappings[m]?.Proxy;
                        if (p != null && p.GetType() == concreteType) { preTracked = p; break; }
                    }
                }
                else
                {
                    var serviceMappings = _globalServiceRegistry.ServiceMappings;
                    for (int m = 0; m < serviceMappings.Length; m++)
                    {
                        var s = serviceMappings[m]?.Service;
                        if (s != null && s.GetType() == concreteType) { preTracked = s; break; }
                    }
                }

                // Not tracked: find a hand-placed instance anywhere under the facade, or create
                // one under the Global Proxies/Global Services container if none exists.
                var resolution = mvcExpress.Internal.Initialization.AttributeMonoBehaviourResolver.Resolve(
                    transform, concreteType, parentContainer, preTracked);

                if (resolution.Kind == mvcExpress.Internal.Initialization.MonoBehaviourResolutionKind.Ambiguous)
                {
                    var failureMessage =
                        $"[RegisterGlobal] Type '{concreteType.FullName}' has {resolution.Conflicts.Length} " +
                        $"instances in the MvcFacade hierarchy: " +
                        $"{string.Join(", ", Array.ConvertAll(resolution.Conflicts, go => go.name))}. " +
                        $"Remove the duplicate(s) so the correct instance is unambiguous. Skipping registration.";
                    MvcDebug.LogError(failureMessage);
                    _globalCompositionFailures.Add(failureMessage);
                    return;
                }

#if UNITY_EDITOR || MVC_LOGGING
                if (resolution.Kind == mvcExpress.Internal.Initialization.MonoBehaviourResolutionKind.Created)
                {
                    MvcDebug.Log(
                        $"[RegisterGlobal] Auto-created '{concreteType.Name}' under " +
                        $"{(meta0.IsProxy ? "Global Proxies" : "Global Services")} container.");
                }
#endif

                instance = resolution.Instance;
            }
            else
            {
                try
                {
                    instance = Activator.CreateInstance(concreteType);
                }
                catch (Exception ex)
                {
                    // Activator.CreateInstance wraps a throwing constructor in TargetInvocationException -
                    // unwrap it, or the recorded message hides the actual cause.
                    var inner = ex is System.Reflection.TargetInvocationException ? ex.InnerException ?? ex : ex;
                    var failureMessage =
                        $"[RegisterGlobal] Failed to create instance of '{concreteType.FullName}': {inner.Message}";
                    MvcDebug.LogError(failureMessage);
                    _globalCompositionFailures.Add(failureMessage);
                    return;
                }
            }

            // Intentionally unwrapped - lifecycle conflicts must hard-throw (configuration bug),
            // unlike the surrounding soft-skip failure modes (missing instance, ambiguous
            // MonoBehaviour, etc.).
            var lifecycles = group.ConvertAll(m => m.Lifecycle);
            var lifecycle = mvcExpress.Internal.Initialization.AttributeGroupingUtility.ResolveGroupLifecycle(
                concreteType, lifecycles, "RegisterGlobal");
            bool isScoped = lifecycle == RegistrationLifecycle.Scoped;

            try
            {
                var builder = _globalContainer.Register(instance, concreteType);

                foreach (var meta in group)
                {
                    if (meta.RegisterToLogic)
                    {
                        if (meta.LogicType == concreteType)
                            builder.ToLogic();
                        else
                            builder.ToLogicAs(meta.LogicType);
                    }

                    if (meta.RegisterToView)
                    {
                        if (meta.ViewType == concreteType)
                            builder.ToView();
                        else
                            builder.ToViewAs(meta.ViewType);
                    }
                }

                if (isScoped)
                    builder.AsScoped();
                else if (lifecycle == RegistrationLifecycle.Transient)
                    builder.AsTransient();
                else
                    builder.AsPermanent();
            }
            catch (Exception ex)
            {
                var failureMessage =
                    $"[RegisterGlobal] Failed to register '{concreteType.FullName}' into global container: {ex.Message}";
                MvcDebug.LogError(failureMessage);
                _globalCompositionFailures.Add(failureMessage);
                return;
            }

            // Scoped: 'instance' was a throwaway created only to walk the builder above - the
            // container discarded it and will construct its own per resolution scope. Skip
            // deferred initialization entirely; there is nothing valid left to initialize.
            if (isScoped)
                return;

            // Defer initialization so all global registrations are visible to injection.
            if (meta0.IsProxy && instance is Proxy proxy)
            {
                proxy.Initialize(concreteType, _messageBus, _globalContainer, deferOnInitialized: true);
                pendingProxies.Add(proxy);
            }
            else if (meta0.IsProxy && instance is ProxyBehaviour proxyBehaviour)
            {
                proxyBehaviour.InitializeGlobal(_globalContainer, _messageBus, deferOnInitialized: true);
                pendingProxyBehaviours.Add(proxyBehaviour);
            }
            else
            {
                pendingObjects.Add(instance);
            }
        }

        // Appends [StartupModule] attribute entries to the launch list, skipping any module type
        // that already has an Inspector entry (Inspector configuration takes precedence).
        // Entries are sorted by Order before being appended.
        private void DrainAttributeStartupModules(List<StartupModuleLaunch> entries)
        {
            // If the scanner was reset after InitializeIfNeeded ran (e.g. by a test to suppress
            // auto-start of test-fake types), treat the cache as empty.
            if (!mvcExpress.Internal.Initialization.AttributeScanner.IsScanned)
                return;

            var attrEntries = mvcExpress.Internal.Initialization.AttributeScanner.GetStartupModuleMetadata();
            if (attrEntries.Count == 0)
                return;

            // Build a set of types already covered by Inspector entries so we can skip duplicates.
            var coveredTypes = new HashSet<Type>();
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].ModuleType != null)
                    coveredTypes.Add(entries[i].ModuleType);
            }

            // Sort attribute entries by Order before appending.
            var sorted = new List<StartupModuleMetadata>(attrEntries);
            sorted.Sort((a, b) => a.Order.CompareTo(b.Order));

            for (int i = 0; i < sorted.Count; i++)
            {
                var meta = sorted[i];

                // Inspector config for the same type takes precedence.
                if (coveredTypes.Contains(meta.ModuleType))
                    continue;

                MvcCompositionStyleWarning.WarnIfDisabled(
                    MvcCompositionStyle.Attribute,
                    $"[StartupModule] auto-start for '{meta.ModuleType.Name}'");

                entries.Add(StartupModuleLaunch.FromType(meta.ModuleType));
            }
        }

        // Warns if the project has disabled Unity-style registration but the global service registry has entries.
        private void WarnIfGlobalServiceRegistryUsesUnityStyle()
        {
            var mappings = _globalServiceRegistry != null ? _globalServiceRegistry.ServiceMappings : null;
            if (mappings != null && mappings.Length > 0)
            {
                MvcCompositionStyleWarning.WarnIfDisabled(
                    MvcCompositionStyle.Unity,
                    "MvcFacade Global Service Registry");
            }
        }

        // Warns if the project has disabled Unity-style registration but the global proxy registry has entries.
        private void WarnIfGlobalProxyRegistryUsesUnityStyle()
        {
            var mappings = _globalProxyRegistry != null ? _globalProxyRegistry.ProxyMappings : null;
            if (mappings != null && mappings.Length > 0)
            {
                MvcCompositionStyleWarning.WarnIfDisabled(
                    MvcCompositionStyle.Unity,
                    "MvcFacade Global Proxy Registry");
            }
        }

        // Warns if the project has disabled a composition style but the startup entry uses it
        // (prefab entry → Unity style; code entry → Code style).
        private static void WarnForStartupModuleStyle(MvcStartupModuleEntry entry)
        {
            if (entry == null)
                return;

            var moduleType = entry.ResolveModuleType();
            var moduleName = moduleType != null ? moduleType.Name : "unresolved module";
            if (entry.ModulePrefab != null)
            {
                MvcCompositionStyleWarning.WarnIfDisabled(
                    MvcCompositionStyle.Unity,
                    $"prefab startup module '{moduleName}' in MvcFacade");
                return;
            }

            MvcCompositionStyleWarning.WarnIfDisabled(
                MvcCompositionStyle.Code,
                $"code-created startup module '{moduleName}' in MvcFacade");
        }

#if UNITY_EDITOR
        // Keeps serialized type-name strings in startup entries in sync with the referenced type
        // objects whenever the Inspector is saved.
        private void OnValidate()
        {
            if (_startupModules == null)
                return;

            for (int i = 0; i < _startupModules.Length; i++)
            {
                _startupModules[i]?.EditorSyncTypeNameFromReferences();
            }
        }
#endif
    }
}

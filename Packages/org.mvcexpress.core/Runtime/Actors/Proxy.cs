using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Interfaces;
using mvcExpress.Logging;
using System;

namespace mvcExpress
{
    /// <summary>
    /// Code-only singleton base class for owning external-system access and data persistence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use <see cref="Proxy"/> for anything that touches the outside world: save files, REST
    /// calls, local databases, platform SDKs, or any stateful external resource. Unlike a
    /// <c>Service</c>, which contains pure business logic, a <c>Proxy</c> is the boundary
    /// between the framework and external systems.
    /// </para>
    /// <para>
    /// Prefer <see cref="Proxy"/> over <see cref="ProxyBehaviour"/> unless you need serialized
    /// Unity references, scene-object lifetime, or a Unity component API (e.g. coroutines,
    /// physics callbacks). Code-only proxies have lighter overhead and cleaner disposal.
    /// </para>
    /// <para>
    /// Registered once in <c>RegisterProxies()</c> and lives as long as its owning module.
    /// Override <see cref="OnInitialized"/> for startup logic and <see cref="OnCleanup"/> for
    /// teardown. Dependencies injected via <c>[Inject]</c> are available by the time
    /// <see cref="OnInitialized"/> runs.
    /// </para>
    /// </remarks>
    public abstract partial class Proxy : IDisposable
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Leak detection, not part of disposal. Every Proxy used to carry its own finalizer as a
        // "safety net" - but the finalizer could only call Dispose(false), which (correctly) never
        // touches managed state or calls OnCleanup, so a leaked proxy was never actually cleaned up
        // by it; the only real effect was that every Proxy, including every properly-disposed one
        // before its own GC.SuppressFinalize took effect, paid to be finalizable (an extra GC
        // generation survived, a queue entry on the finalizer thread) for a safety net that caught
        // nothing.
        //
        // This canary isolates the (still useful) diagnostic - "a proxy was collected without ever
        // being disposed" - onto a separate, minimal object instead. It carries none of the
        // proxy's own state, so it's cheap to finalize; Proxy holds the only strong reference to
        // it, so the canary becomes unreachable (and gets queued to the finalizer thread) in the
        // exact same GC pass as the Proxy itself would. Only Editor/Development builds construct
        // one at all - a release Player has zero finalizable objects from this class, not
        // "finalizable but suppressed".
        private sealed class LeakCanary
        {
            private readonly string _proxyTypeName;
            public bool Disposed;

            public LeakCanary(string proxyTypeName)
            {
                _proxyTypeName = proxyTypeName;
            }

            ~LeakCanary()
            {
                if (!Disposed)
                {
                    MvcDebug.LogWarning(
                        $"[Proxy leak] A '{_proxyTypeName}' was garbage-collected without Dispose() " +
                        "ever running. Every Proxy is owned by its module and disposed automatically " +
                        "on module teardown - seeing this means something held a reference to it (or " +
                        "constructed it) outside that lifecycle, or a module's teardown path was " +
                        "skipped.");
                }
            }
        }

        private readonly LeakCanary _leakCanary;
#endif

        protected Proxy()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _leakCanary = new LeakCanary(GetType().Name);
#endif
        }

        /// <summary>
        /// Runtime type of the module that registered this proxy, available after <see cref="OnInitialized"/>.
        /// </summary>
        public Type ModuleType { get; private set; }

        // Dependencies supplied by the module
        private MvcDiContainer _diContainer;
        private MvcActorContext _actorContext;
        private MessengerApi _messenger;
        private ModuleContainerApi _container;
        private GlobalContainerApi _globalContainer;

        // State tracking flags
        private bool _dependenciesLinked;
        private bool _initialized;
        private bool _disposed;

        /// <summary>
        /// Publishes typed messages from this proxy.
        /// </summary>
        protected MessengerApi Messenger => _messenger;

        /// <summary>
        /// Resolves module-scoped dependencies from this proxy's logic scope.
        /// </summary>
        protected ModuleContainerApi Container => _container;

        /// <summary>
        /// Resolves application-wide dependencies from this proxy.
        /// </summary>
        protected GlobalContainerApi Global => _globalContainer;

        // Scoped lifetime redesign: lets MvcDiContainer.TryResolveScopedForOwner recover this
        // proxy's own module/message-bus context when it needs to construct a scoped dependency
        // that is itself a Proxy (reusing the real Initialize/CompleteInitialization pipeline
        // instead of a bare Activator.CreateInstance).
        internal MvcActorContext ActorContext => _actorContext;

        /// <summary>
        /// Wires framework services into this proxy and optionally triggers <see cref="OnInitialized"/>.
        /// </summary>
        /// <remarks>
        /// Called by the framework during <c>RegisterProxies()</c>. User code should not call this
        /// directly unless writing a custom registrar.
        /// </remarks>
        /// <param name="moduleId">Runtime type of the owning module; stored as <see cref="ModuleType"/>.</param>
        /// <param name="messageBus">Publisher for the single application-wide message bus.</param>
        /// <param name="diContainer">Module DI container used for member injection and resolution.</param>
        /// <param name="deferOnInitialized">
        /// When <c>true</c>, skips the <see cref="OnInitialized"/> call so the registrar can batch all
        /// proxies before completing the phase. The registrar must call <see cref="CompleteInitialization"/>
        /// afterwards.
        /// </param>
        public void Initialize(Type moduleId, IMessagePublisher messageBus, MvcDiContainer diContainer, bool deferOnInitialized = false)
        {
            Initialize(moduleId, null, messageBus, diContainer, deferOnInitialized, scopeOwnerOverride: null);
        }

        internal void Initialize(MvcModule module, IMessagePublisher messageBus, MvcDiContainer diContainer, bool deferOnInitialized = false)
        {
            Initialize(module != null ? module.ModuleType : null, module, messageBus, diContainer, deferOnInitialized, scopeOwnerOverride: null);
        }

        internal void InitializeScoped(Type moduleId, IMessagePublisher messageBus, MvcDiContainer diContainer, object scopeOwner)
        {
            Initialize(moduleId, null, messageBus, diContainer, deferOnInitialized: false, scopeOwnerOverride: scopeOwner);
        }

        private void Initialize(Type moduleId, MvcModule module, IMessagePublisher messageBus, MvcDiContainer diContainer, bool deferOnInitialized, object scopeOwnerOverride)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_disposed)
            {
                throw new ObjectDisposedException((moduleId ?? GetType()).Name);
            }
            if (moduleId == null)
            {
                throw new ArgumentNullException(nameof(moduleId));
            }
            if (messageBus == null)
            {
                throw new ArgumentNullException(nameof(messageBus));
            }
            if (diContainer == null)
            {
                throw new ArgumentNullException(nameof(diContainer));
            }
#endif

            this.ModuleType = moduleId;
            this._diContainer = diContainer;
            _actorContext = new MvcActorContext(
                this,
                module,
                moduleId,
                diContainer,
                messageBus,
                MvcLogContext.LogCategory.Proxy);
            _messenger = new MessengerApi(_actorContext);
            _container = new ModuleContainerApi(_actorContext);
            _globalContainer = new GlobalContainerApi(_actorContext);
            _dependenciesLinked = true;

            if (!deferOnInitialized)
            {
                CompleteInitialization(scopeOwnerOverride);
            }
        }

        /// <summary>
        /// Completes initialization by injecting dependencies and calling <see cref="OnInitialized"/>.
        /// </summary>
        internal void CompleteInitialization(object scopeOwnerOverride = null)
        {
            if (!_dependenciesLinked || _initialized)
            {
                return;
            }

            // Set before running the fallible work below so a legitimate re-entrant resolution
            // (e.g. a circular scoped-dependency graph) sees this proxy as already
            // initializing and does not recurse. Reverted on failure so this proxy is not left
            // permanently marked "initialized" when it never actually finished.
            _initialized = true;

            try
            {
                MvcInjectionUtility.InjectMembers(this, _diContainer, useViewScope: false, scopeOwnerOverride: scopeOwnerOverride);
                OnInitialized();
            }
            catch
            {
                _initialized = false;
                throw;
            }
        }

        /// <summary>
        /// Called once after all <c>[Inject]</c> members have been resolved and filled.
        /// </summary>
        /// <remarks>
        /// Override this for proxy startup logic: open file handles, subscribe to platform
        /// callbacks, warm caches, etc. At this point all services registered before this proxy
        /// are available, but later proxies in the same phase may not be registered yet.
        /// Do not call base unless a base class override exists.
        /// </remarks>
        protected virtual void OnInitialized() { }

        /// <summary>
        /// Releases all proxy resources; called automatically by the module on shutdown.
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Standard dispose pattern implementation; calls <see cref="OnCleanup"/> on the managed path.
        /// </summary>
        /// <param name="disposing">
        /// <c>true</c> when called from <see cref="Dispose()"/>; <c>false</c> when called from the
        /// finalizer. Managed resources must only be released when <c>true</c>.
        /// </param>
        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) return;
            _disposed = true;

            if (disposing)
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                // The proxy was properly disposed through the managed path - tell the canary so
                // its own finalizer (if the GC gets to it before this session ends) stays quiet
                // instead of reporting a leak that didn't happen.
                if (_leakCanary != null)
                {
                    _leakCanary.Disposed = true;
                }
#endif

                // OnCleanup() must not be able to skip scoped-dependency
                // release or reference clearing below - a throwing override used to abort this
                // whole branch, leaking every scoped dependency this proxy owned. The try/finally
                // does not swallow the exception: every caller of Dispose() (DisposeScopedInstance,
                // TearDownOwnedInstance) already wraps the call in its own try/catch and logs it.
                try
                {
                    if (_initialized)
                    {
                        OnCleanup();
                    }
                }
                finally
                {
                    // Scoped lifetime redesign: this proxy is the injectee dying, so every Scoped
                    // dependency dedicated to it dies with it - see
                    // MvcDiContainer.ReleaseScopedInstancesFor and the disposal-trigger map in
                    // InternalSpecs/Architecture/SCOPED_LIFETIME_REDESIGN.md.
                    _diContainer?.ReleaseScopedInstancesFor(this);
                    var globalContainer = MvcFacade.GlobalContainerOrNull;
                    if (globalContainer != null && !ReferenceEquals(globalContainer, _diContainer))
                    {
                        globalContainer.ReleaseScopedInstancesFor(this);
                    }

                    _diContainer = null;
                    _actorContext = default;
                    _messenger = default;
                    _container = default;
                    _globalContainer = default;
                }
            }
        }

        /// <summary>
        /// Called on the managed-dispose path before framework references are nulled out.
        /// </summary>
        /// <remarks>
        /// Override this to close file handles, cancel HTTP requests, deregister platform
        /// callbacks, or release any other resources the proxy acquired in <see cref="OnInitialized"/>.
        /// Framework APIs (<see cref="Messenger"/>, <see cref="Container"/>) are still valid
        /// when <see cref="OnCleanup"/> runs; they become null immediately after.
        /// </remarks>
        protected virtual void OnCleanup() { }

        // No finalizer here anymore - see the LeakCanary remarks above for why a
        // per-instance finalizer on Proxy itself was pure cost with no working safety net. A
        // subclass that genuinely owns an unmanaged resource directly (rare - prefer wrapping it in
        // a managed IDisposable type that finalizes itself) can still add its own `~Derived() =>
        // Dispose(false);`; Dispose(bool) above already implements the differential correctly
        // (skips managed-only cleanup when disposing is false), so that extensibility point doesn't
        // require Proxy itself to declare a finalizer.
    }
}

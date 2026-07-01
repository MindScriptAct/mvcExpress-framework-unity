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

        /// <summary>
        /// Wires framework services into this proxy and optionally triggers <see cref="OnInitialized"/>.
        /// </summary>
        /// <remarks>
        /// Called by the framework during <c>RegisterProxies()</c>. User code should not call this
        /// directly unless writing a custom registrar.
        /// </remarks>
        /// <param name="moduleId">Runtime type of the owning module; stored as <see cref="ModuleType"/>.</param>
        /// <param name="messageBus">Shared message publisher for this module scope.</param>
        /// <param name="diContainer">Module DI container used for member injection and resolution.</param>
        /// <param name="deferOnInitialized">
        /// When <c>true</c>, skips the <see cref="OnInitialized"/> call so the registrar can batch all
        /// proxies before completing the phase. The registrar must call <see cref="CompleteInitialization"/>
        /// afterwards.
        /// </param>
        public void Initialize(Type moduleId, IMessagePublisher messageBus, MvcDiContainer diContainer, bool deferOnInitialized = false)
        {
            Initialize(moduleId, null, messageBus, diContainer, deferOnInitialized);
        }

        internal void Initialize(MvcModule module, IMessagePublisher messageBus, MvcDiContainer diContainer, bool deferOnInitialized = false)
        {
            Initialize(module != null ? module.ModuleType : null, module, messageBus, diContainer, deferOnInitialized);
        }

        private void Initialize(Type moduleId, MvcModule module, IMessagePublisher messageBus, MvcDiContainer diContainer, bool deferOnInitialized)
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
            _globalContainer = new GlobalContainerApi();
            _dependenciesLinked = true;

            if (!deferOnInitialized)
            {
                CompleteInitialization();
            }
        }

        /// <summary>
        /// Completes initialization by injecting dependencies and calling <see cref="OnInitialized"/>.
        /// </summary>
        internal void CompleteInitialization()
        {
            if (!_dependenciesLinked || _initialized)
            {
                return;
            }

            _initialized = true;

            MvcInjectionUtility.InjectMembers(this, _diContainer, useViewScope: false);
            OnInitialized();
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
                if (_initialized)
                {
                    OnCleanup();
                }

                _diContainer = null;
                _actorContext = default;
                _messenger = default;
                _container = default;
                _globalContainer = default;
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

        // Safety net: the module always calls Dispose(), but guard against leaked proxies.
        ~Proxy()
        {
            Dispose(false);
        }
    }
}

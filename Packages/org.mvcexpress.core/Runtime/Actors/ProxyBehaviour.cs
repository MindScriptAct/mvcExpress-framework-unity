using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Interfaces;
using mvcExpress.Logging;
using System;
using UnityEngine;

namespace mvcExpress
{
    /// <summary>
    /// MonoBehaviour variant of <see cref="Proxy"/> for external-system access that requires
    /// Unity component lifecycle.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Prefer the plain <see cref="Proxy"/> unless you specifically need:
    /// <list type="bullet">
    ///   <item>Inspector-serialized Unity references (e.g. prefabs, ScriptableObjects)</item>
    ///   <item>Unity component APIs: coroutines, <c>Update</c>, physics or audio callbacks</item>
    ///   <item>Scene-object lifetime managed by Unity (the GameObject destroy cascade)</item>
    /// </list>
    /// </para>
    /// <para>
    /// Like <see cref="Proxy"/>, a <c>ProxyBehaviour</c> is a module-scoped singleton that owns
    /// access to an external system or permanent data source. Registered in
    /// <c>RegisterProxies()</c> by placing the component on a child <c>GameObject</c> inside
    /// the module hierarchy. Override <see cref="OnInitialized"/> for startup logic and
    /// <see cref="OnCleanup"/> for teardown.
    /// </para>
    /// <para>
    /// Global proxies (registered on <c>MvcFacade</c> rather than a module) are initialized via
    /// <see cref="InitializeGlobal"/> and have no <see cref="ModuleType"/>.
    /// </para>
    /// </remarks>
    public abstract partial class ProxyBehaviour : MonoBehaviour
    {
        // Module and dependency references
        private MvcModule _moduleContext;
        private MvcDiContainer _diContainer;
        private MvcActorContext _actorContext;
        private MessengerApi _messenger;
        private ModuleContainerApi _container;
        private GlobalContainerApi _globalContainer;

        // Cached module type to avoid repeated GetType() calls
        private Type _cachedModuleType;

        /// <summary>
        /// Runtime type of the module that registered this proxy, or <c>null</c> for global proxies
        /// registered on <c>MvcFacade</c>.
        /// </summary>
        public Type ModuleType => _cachedModuleType;

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
        // that is itself a Proxy.
        internal MvcActorContext ActorContext => _actorContext;

        // State tracking
        private bool _dependenciesLinked;
        private bool _initialized;

        /// <summary>
        /// Wires module framework services into this component and optionally triggers <see cref="OnInitialized"/>.
        /// </summary>
        /// <remarks>
        /// Called by the framework during <c>RegisterProxies()</c>. User code should not call this
        /// directly unless writing a custom registrar.
        /// </remarks>
        /// <param name="module">Owning module instance; provides the proxy's DI scope.</param>
        /// <param name="diContainer">Module DI container used for member injection and resolution.</param>
        /// <param name="messageBus">Publisher for the single application-wide message bus.</param>
        /// <param name="deferOnInitialized">
        /// When <c>true</c>, skips the <see cref="OnInitialized"/> call so the registrar can batch all
        /// proxies before completing the phase. The registrar must call <see cref="CompleteInitialization"/>
        /// afterwards.
        /// </param>
        public void Initialize(MvcModule module, MvcDiContainer diContainer, IMessagePublisher messageBus, bool deferOnInitialized = false)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (module == null || diContainer == null || messageBus == null)
            {
                MvcDebug.LogError($"Initialize called with null parameter on proxy '{name}'.");
                return;
            }
#endif

            this._moduleContext = module;
            this._cachedModuleType = module?.ModuleType;
            this._diContainer = diContainer;
            _actorContext = new MvcActorContext(
                this,
                module,
                _cachedModuleType,
                diContainer,
                messageBus,
                MvcLogContext.LogCategory.Proxy);
            _messenger = new MessengerApi(_actorContext);
            _container = new ModuleContainerApi(_actorContext);
            _globalContainer = new GlobalContainerApi(_actorContext);
            _dependenciesLinked = true;

            if (!deferOnInitialized)
            {
                CompleteInitialization();
            }
        }

        /// <summary>
        /// Wires global framework services into this component for proxies registered on <c>MvcFacade</c>.
        /// </summary>
        /// <remarks>
        /// Use when the proxy should be accessible across all modules. <see cref="ModuleType"/> will
        /// be <c>null</c> after this call. User code should not call this directly.
        /// </remarks>
        /// <param name="container">Application-wide DI container used for member injection.</param>
        /// <param name="messageBus">Global shared message publisher.</param>
        /// <param name="deferOnInitialized">
        /// When <c>true</c>, skips the <see cref="OnInitialized"/> call so the registrar can batch all
        /// global proxies before completing the phase. The registrar must call <see cref="CompleteInitialization"/>
        /// afterwards.
        /// </param>
        public void InitializeGlobal(MvcDiContainer container, IMessagePublisher messageBus, bool deferOnInitialized = false)
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (container == null || messageBus == null)
            {
                MvcDebug.LogError($"InitializeGlobal called with null parameter on proxy '{name}'.");
                return;
            }
#endif

            this._moduleContext = null;
            this._cachedModuleType = null;
            _diContainer = container;
            _actorContext = new MvcActorContext(
                this,
                null,
                null,
                container,
                messageBus,
                MvcLogContext.LogCategory.Proxy);
            _messenger = new MessengerApi(_actorContext);
            _container = new ModuleContainerApi(_actorContext);
            _globalContainer = new GlobalContainerApi(_actorContext);
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

            // Set before running the fallible work below so a legitimate re-entrant resolution
            // (e.g. a circular scoped-dependency graph) sees this proxy as already
            // initializing and does not recurse. Reverted on failure so this proxy is not left
            // permanently marked "initialized" when it never actually finished.
            _initialized = true;

            try
            {
                MvcInjectionUtility.InjectMembers(this, _diContainer, useViewScope: false);
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
        /// callbacks, warm caches, etc. Do not call base unless a base class override exists.
        /// </remarks>
        protected virtual void OnInitialized() { }

        // Unity destroy hook; routes into the framework cleanup sequence.
        private void OnDestroy()
        {
            // OnCleanup() must not be able to skip scoped-dependency release
            // or reference clearing below - a throwing override used to abort this whole method,
            // leaking every scoped dependency this proxy owned. The try/finally does not swallow
            // the exception: it still propagates to Unity's own engine-level invocation of
            // OnDestroy(), which logs it exactly like any other uncaught MonoBehaviour callback
            // exception, without stopping teardown.
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
                _moduleContext = null;
                _actorContext = default;
                _messenger = default;
                _container = default;
                _globalContainer = default;
            }
        }

        /// <summary>
        /// Soft-resets this proxy for a global container reset - runs <see cref="OnCleanup"/> and releases owned scoped dependencies exactly like
        /// <c>OnDestroy</c>, but does not null out framework references, since
        /// <see cref="InitializeGlobal"/> is expected to run again immediately afterward. Also
        /// clears the initialized flags so that next <c>InitializeGlobal</c>/
        /// <c>CompleteInitialization</c> call performs a real re-injection and re-OnInitialized(),
        /// not a silent no-op via the idempotency guard in <see cref="CompleteInitialization"/>.
        /// </summary>
        /// <remarks>
        /// Only ever called from <see cref="Internal.Initialization.GlobalProxyRegistrar"/>'s
        /// container-clear path, never from <c>OnDestroy</c>'s real-destruction path - calling
        /// both on the same proxy in the same teardown would double-invoke <see cref="OnCleanup"/>.
        /// Note this does not prevent the underlying GameObject from being destroyed by the global
        /// container's own <c>Clear()</c> (which still finds and tears down this instance like any
        /// other registered instance) - an Inspector-configured global proxy's GameObject does not
        /// currently survive a container reset; this method only ensures its lifecycle callbacks
        /// fire correctly and promptly for whatever window it has left, rather than being skipped
        /// or deferred to the eventual, separate <c>OnDestroy</c> cascade.
        /// </remarks>
        internal void ResetForContainerClear()
        {
            // A throwing OnCleanup() must not be able to skip scoped
            // release or the flag reset below - the caller (GlobalProxyRegistrar) already wraps
            // this whole call in try/catch and logs, so letting the exception propagate out of the
            // finally is safe and does not need a second catch here.
            try
            {
                if (_initialized)
                {
                    OnCleanup();
                }
            }
            finally
            {
                _diContainer?.ReleaseScopedInstancesFor(this);
                var globalContainer = MvcFacade.GlobalContainerOrNull;
                if (globalContainer != null && !ReferenceEquals(globalContainer, _diContainer))
                {
                    globalContainer.ReleaseScopedInstancesFor(this);
                }

                _initialized = false;
                _dependenciesLinked = false;
            }
        }

        /// <summary>
        /// Called from <c>OnDestroy</c> before framework references are nulled out.
        /// </summary>
        /// <remarks>
        /// Override this to deregister Unity event listeners, cancel coroutines, close
        /// external connections, or release any other resources acquired in
        /// <see cref="OnInitialized"/>. Framework APIs (<see cref="Messenger"/>,
        /// <see cref="Container"/>) are still valid when <see cref="OnCleanup"/> runs;
        /// they become null immediately after.
        /// </remarks>
        protected virtual void OnCleanup() { }
    }
}

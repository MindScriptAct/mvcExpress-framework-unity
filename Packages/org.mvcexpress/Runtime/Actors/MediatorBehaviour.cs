using mvcExpress;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Interfaces;
using mvcExpress.Internal.Messaging;
using mvcExpress.Logging;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace mvcExpress
{
    /// <summary>
    /// MonoBehaviour base class that connects Unity views to an MvcExpress module.
    /// </summary>
    /// <remarks>
    /// Derive from <see cref="MediatorBehaviour"/> for UI panels, scene views, and
    /// other Unity-facing objects. Mediators publish user intent, subscribe to state
    /// messages, update view components, and resolve only view-scope dependencies.
    /// </remarks>
    public partial class MediatorBehaviour : MonoBehaviour
    {
        // Module context and dependencies
        private Type _moduleType;
        private MvcModule _moduleContext;
        private MvcDiContainer _diContainer;
        private IMessageBus _messageBus;
        private MvcActorContext _actorContext;
        private MediatorMessengerApi _messenger;
        private ModuleContainerApi _container;
        private GlobalContainerApi _globalContainer;
        private MediatorHubApi _mediatorHub;

        // State tracking
        private bool _dependenciesLinked;
        private bool _initialized;
        private bool _isDestroyed;

        // Automatic subscription cleanup
        private readonly SubscriptionTracker _subscriptionTracker = new SubscriptionTracker();

        internal bool IsDestroyed => _isDestroyed;
        internal MvcModule ModuleContext => _moduleContext;
        internal IMessageBus MessageBus => _messageBus;
        internal SubscriptionTracker SubscriptionTracker => _subscriptionTracker;

        /// <summary>
        /// Publishes and subscribes to typed messages for this mediator.
        /// </summary>
        protected MediatorMessengerApi Messenger => _messenger;

        /// <summary>
        /// Resolves module-scoped dependencies from this mediator's view scope.
        /// </summary>
        protected ModuleContainerApi Container => _container;

        /// <summary>
        /// Resolves application-wide dependencies from this mediator.
        /// </summary>
        protected GlobalContainerApi Global => _globalContainer;

        /// <summary>
        /// Attaches or detaches runtime mediators through the owning module.
        /// </summary>
        protected MediatorHubApi MediatorHub => _mediatorHub;

        /// <summary>
        /// Initializes the mediator with module dependencies and messaging context.
        /// </summary>
        /// <param name="module">Owning module.</param>
        /// <param name="diContainer">Module dependency container.</param>
        /// <param name="messageBus">Shared message bus.</param>
        /// <param name="deferOnInitialized">When true, delays <see cref="OnInitialized"/> until the registrar completes the phase.</param>
        public void Initialize(MvcModule module, MvcDiContainer diContainer, IMessageBus messageBus, bool deferOnInitialized = false)
        {
            this._moduleType = module.GetType();
            this._moduleContext = module;
            this._diContainer = diContainer;
            this._messageBus = messageBus;
            _actorContext = new MvcActorContext(
                this,
                module,
                _moduleType,
                diContainer,
                messageBus,
                MvcLogContext.LogCategory.Mediator);
            _messenger = new MediatorMessengerApi(_actorContext);
            _container = new ModuleContainerApi(_actorContext);
            _globalContainer = new GlobalContainerApi();
            _mediatorHub = new MediatorHubApi(this);
            _dependenciesLinked = true;

            if (!deferOnInitialized)
            {
                CompleteInitialization();
            }
        }

        /// <summary>
        /// Completes initialization by injecting view-scope dependencies and calling <see cref="OnInitialized"/>.
        /// </summary>
        internal void CompleteInitialization()
        {
            if (!_dependenciesLinked || _initialized)
            {
                return;
            }

            _initialized = true;

            if (_diContainer != null)
            {
                using (_diContainer.BeginViewScope())
                {
                    MvcInjectionUtility.InjectMembers(this, _diContainer, useViewScope: true);
                    OnInitialized();
                }
            }
            else
            {
                OnInitialized();
            }
        }

        /// <summary>
        /// Called after dependency injection is complete.
        /// </summary>
        /// <remarks>
        /// Subscribe to messages and connect Unity view callbacks here. The mediator is
        /// already linked to its owning module when this method runs.
        /// </remarks>
        protected virtual void OnInitialized() { }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private bool _warnedNotInitialized;
        private void Start()
        {
            // Unity lifecycle order: scene MediatorBehaviour.Awake can run before MvcModule.Awake finishes
            // and before the ModuleInitializer links this mediator. Start runs later, so if we're still not
            // linked here it's a real configuration/runtime issue (mediator not registered to any module).
            if (!_warnedNotInitialized && !_dependenciesLinked && isActiveAndEnabled)
            {
                _warnedNotInitialized = true;
                MvcDebug.LogWarning($"Mediator '{name}' was not initialized. Ensure it is listed in the module's Mediator registry or attached at runtime.");
            }
        }
#endif

        /// <summary>
        /// Unity lifecycle: Awake.
        /// </summary>
        private void Awake()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Don't warn here: Awake commonly runs before the module has a chance to link dependencies.
#endif
        }

        /// <summary>
        /// Unity lifecycle: cleanup on destroy with automatic subscription removal.
        /// </summary>
        private void OnDestroy()
        {
            CleanupMediator();
        }

        internal void CleanupMediator()
        {
            if (_isDestroyed) return;
            _isDestroyed = true;

            OnCleanup();

            if (_messageBus != null)
            {
                _subscriptionTracker.UnsubscribeAll();
            }

            _diContainer = null;
            _messageBus = null;
            _moduleContext = null;
            _actorContext = default;
            _messenger = default;
            _container = default;
            _globalContainer = default;
            _mediatorHub = default;
        }

        /// <summary>
        /// Called during <c>OnDestroy</c> for custom cleanup.
        /// </summary>
        /// <remarks>
        /// Remove Unity event listeners here. Message subscriptions registered through
        /// <see cref="Messenger"/> are removed automatically.
        /// </remarks>
        protected virtual void OnCleanup() { }

        /// <summary>
        /// Module class type that owns this mediator.
        /// </summary>
        public Type ModuleType => _moduleType;
    }
}

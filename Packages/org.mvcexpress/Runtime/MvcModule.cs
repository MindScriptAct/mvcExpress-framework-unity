﻿using mvcExpress;
using mvcExpress.Internal.Commands;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Initialization;
using mvcExpress.Internal.Interfaces;
using mvcExpress.Internal.Messaging;
using mvcExpress.Internal.Proxy;
using mvcExpress.Internal.Services;
using mvcExpress.Logging;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace mvcExpress
{
    /// <summary>
    /// Base class for a self-contained mvcExpress application unit. This is the composition root
    /// for one standalone application - a complete feature area, game mode, or screen that could
    /// work independently with its own data, logic, and views.
    /// </summary>
    /// <remarks>
    /// Each module owns an isolated DI container and a set of command bindings. All modules share
    /// a single app-wide message bus owned by <see cref="MvcFacade"/>. Derive from this class
    /// and override the lifecycle methods in order:
    /// <list type="number">
    ///   <item><see cref="RegisterServices"/> - long-lived business-logic singletons</item>
    ///   <item><see cref="RegisterProxies"/> - external data / state singletons</item>
    ///   <item><see cref="BindCommands"/> - message-to-command wiring</item>
    ///   <item><see cref="AttachMediators"/> - view bridge initialization</item>
    ///   <item><see cref="OnInitialized"/> - post-init orchestration and first messages</item>
    /// </list>
    /// Unity calls <see cref="Awake"/> which drives the sequence automatically. Only one instance
    /// of each concrete module type may exist at a time - <see cref="MvcFacade"/> enforces this.
    /// </remarks>
    [DefaultExecutionOrder(int.MinValue + 1000)]
    public abstract partial class MvcModule : MonoBehaviour
    {

        /// <summary>
        /// Whether diagnostic logging is enabled for this module instance.
        /// </summary>
        /// <remarks>
        /// Controls per-module verbosity in the MvcConsole and Unity log. Project-wide settings
        /// in <c>MvcSettings</c> can override or suppress this value globally.
        /// Use <see cref="EnableModuleLogging"/> and <see cref="DisableModuleLogging"/> to change this at runtime.
        /// </remarks>
        [SerializeField, HideInInspector] private bool _loggingEnabled = false;
        public bool LoggingEnabled => _loggingEnabled;

        /// <summary>Enables diagnostic logging for this module.</summary>
        public void EnableModuleLogging()  => _loggingEnabled = true;

        /// <summary>Disables diagnostic logging for this module.</summary>
        public void DisableModuleLogging() => _loggingEnabled = false;

        [Header("MVC Containers (optional in Edit Mode)")]
        [SerializeField] private ServiceRegistryBehaviour _servicesContainer;   // Inspector-assigned Services child
        [SerializeField] private ProxyRegistryBehaviour _modelContainer;        // Inspector-assigned Model (Proxies) child
        [SerializeField] private CommandBindingsBehaviour _controllerContainer; // Inspector-assigned Controller (Commands) child
        [SerializeField] private MediatorRegistryBehaviour _viewContainer;      // Inspector-assigned View (Mediators) child

        // Override for where mediators instantiated by code are parented; null means use _viewContainer or module transform.
        [SerializeField, HideInInspector] private Transform _moduleViewContainer;

        // Maps mediator type → prefab, populated from _viewContainer.MediatorPrefabs at initialization.
        private readonly Dictionary<Type, GameObject> _mediatorPrefabByType = new Dictionary<Type, GameObject>(16);

        // Mediators explicitly added via MediatorHub.AddMediator before or during initialization.
        private readonly List<MediatorBehaviour> _manualMediators = new List<MediatorBehaviour>(4);

        // Core runtime services - all null until EnsureCoreServicesInitialized() runs.
        private MvcDiContainer _diContainer;          // isolated DI container for this module
        private MvcMessageBus _messageBus;            // shared app-wide bus; reference only (owned by MvcFacade)
        private MvcCommandProcessor _commandProcessor; // routes published messages to bound commands
        private ModuleInitializer _initializer;        // drives the phased init sequence; null before InitializeModule()
        private MvcActorContext _actorContext;         // identity + container refs passed to actor APIs
        private MessengerApi _messenger;
        private ModuleRegistrationContainerApi _container;
        private ModuleGlobalContainerApi _globalContainer;
        private CommanderApi _commander;
        private MediatorHubApi _mediatorHub;

        internal MvcMessageBus MessageBus => _messageBus;
        internal MvcDiContainer DiContainer => _diContainer;
        internal MvcCommandProcessor CommandProcessor => _commandProcessor;

        internal Transform ModelContainer => _modelContainer != null ? _modelContainer.transform : null;
        internal Transform ServicesContainer => _servicesContainer != null ? _servicesContainer.transform : null;
        internal Transform ControllerContainer => _controllerContainer != null ? _controllerContainer.transform : null;
        /// <summary>
        /// Transform of the View registry child used for view-layer hierarchy operations.
        /// Falls back to the module's own transform when no View container is configured.
        /// </summary>
        protected Transform ViewContainer => _viewContainer != null ? _viewContainer.transform : transform;

        /// <summary>
        /// Publishes typed messages on the app-wide message bus. Modules react to messages by
        /// binding commands via <see cref="Commander"/> - use <see cref="MediatorBehaviour"/> to subscribe.
        /// </summary>
        protected MessengerApi Messenger => _messenger;

        /// <summary>
        /// Registers and resolves module-scoped dependencies. Use from
        /// <see cref="RegisterServices"/> and <see cref="RegisterProxies"/>.
        /// </summary>
        protected ModuleRegistrationContainerApi Container => _container;

        /// <summary>
        /// Registers and resolves application-wide (cross-module) dependencies. Use from
        /// <see cref="RegisterServices"/> or <see cref="RegisterProxies"/> when the dependency
        /// must be shared with other modules.
        /// </summary>
        protected ModuleGlobalContainerApi Global => _globalContainer;

        /// <summary>
        /// Binds messages to commands, executes commands directly, and inspects command bindings
        /// for this module. Use from <see cref="BindCommands"/> or <see cref="OnInitialized"/>.
        /// </summary>
        protected CommanderApi Commander => _commander;

        /// <summary>
        /// Attaches, detaches, and queries mediators owned by this module.
        /// Use from <see cref="AttachMediators"/> or at runtime.
        /// </summary>
        protected MediatorHubApi MediatorHub => _mediatorHub;

        /// <summary>
        /// The default parent transform used when code spawns mediator prefabs for this module.
        /// Priority: explicitly set container → View registry transform → module transform.
        /// Never returns null.
        /// </summary>
        public Transform ModuleViewContainer
        {
            get
            {
                // Priority: configured container > View container > module itself
                if (_moduleViewContainer != null)
                    return _moduleViewContainer;

                if (_viewContainer != null)
                    return _viewContainer.transform;

                return transform;
            }
        }

        /// <summary>
        /// Overrides the default parent transform used when spawning mediator prefabs.
        /// </summary>
        /// <param name="container">
        /// Target container transform. Pass <c>null</c> to reset to the View registry container
        /// (or the module transform if no View registry is configured).
        /// </param>
        /// <remarks>
        /// Call this before <see cref="AttachMediators"/> (or from <see cref="OnModuleAwake"/>)
        /// to redirect prefab-spawned mediators into a different hierarchy, for example a
        /// world-space canvas or a dedicated UI root.
        /// </remarks>
        public void SetModuleViewContainer(Transform container)
        {
            _moduleViewContainer = container;

            // If setting to null, ensure we fall back to View container
            if (_moduleViewContainer == null && _viewContainer != null)
            {
                _moduleViewContainer = _viewContainer.transform;
            }
        }

        // Suppresses duplicate command-binding log entries when bindings come from the Unity Inspector registry.
        internal bool SuppressCommandBindingLog { get; set; }

        /// <summary>
        /// Initializes the module's dependency container, message bus, commands, proxies, and mediators.
        /// </summary>
        /// <remarks>
        /// Unity calls this automatically from <c>Awake</c>.
        /// </remarks>
        protected internal void InitializeModule()
        {
            if (_initializer == null)
            {
                EnsureCoreServicesInitialized();
                EnsureMvcContainers();

                // Ensure cached type is populated before creating ModuleInitializer
                // This prevents null module type from being passed to AttributeScanner
                var moduleType = ModuleType; // Force lazy initialization of cached type

                _initializer = new ModuleInitializer(
                    moduleType,
                    _diContainer,
                    _messageBus,
                    _commandProcessor,
                    this
                );
            }

            _initializer.Initialize();
        }

        /// <summary>
        /// Override to register long-lived business-logic services for this module using
        /// <see cref="Container"/> or <see cref="Global"/>.
        /// </summary>
        /// <remarks>
        /// First phase of the initialization sequence. Services registered here are available
        /// for <c>[Inject]</c> resolution into proxies, commands, and mediators in later phases.
        /// </remarks>
        protected virtual void RegisterServices() { }

        /// <summary>
        /// Override to register model/state proxies for this module using <see cref="Container"/>
        /// or <see cref="Global"/>.
        /// </summary>
        /// <remarks>
        /// Second phase of the initialization sequence. Both services and proxies are available
        /// for injection when this method runs.
        /// </remarks>
        protected virtual void RegisterProxies() { }

        /// <summary>
        /// Override to bind typed messages to synchronous or asynchronous commands using
        /// <see cref="Commander"/>.
        /// </summary>
        /// <remarks>
        /// Third phase. Services and proxies are fully initialized; do not register new
        /// dependencies here.
        /// </remarks>
        protected virtual void BindCommands() { }

        /// <summary>
        /// Override to attach scene-placed or prefab-spawned mediators using
        /// <see cref="MediatorHub"/>.
        /// </summary>
        /// <remarks>
        /// Fourth phase. Services, proxies, and commands are ready. Mediators call their own
        /// <c>OnInitialized</c> immediately after attachment.
        /// </remarks>
        protected virtual void AttachMediators() { }

        /// <summary>
        /// Override to perform post-init orchestration once the entire module is ready.
        /// </summary>
        /// <remarks>
        /// Fifth and final phase. Publish initial messages here - the message bus is fully
        /// wired and all command handlers are registered. Do not register services or proxies
        /// in this method.
        /// </remarks>
        protected virtual void OnInitialized() { }

        /// <summary>
        /// Lazily creates the DI container, command processor, and all actor API structs.
        /// Safe to call multiple times - no-op after first execution.
        /// </summary>
        internal void EnsureCoreServicesInitialized()
        {
            if (_diContainer == null)
            {
                _diContainer = new MvcDiContainer();
            }

            // Use the single shared bus owned by MvcFacade
            _messageBus = MvcFacade.MessageBus;

            if (_commandProcessor == null)
            {
                // Use cached ModuleType to avoid redundant GetType() call
                _commandProcessor = new MvcCommandProcessor(ModuleType, _diContainer, _messageBus, this);
            }

            _actorContext = new MvcActorContext(
                this,
                this,
                ModuleType,
                _diContainer,
                _messageBus,
                mvcExpress.Logging.MvcLogContext.LogCategory.Module);
            _messenger = new MessengerApi(_actorContext);
            _container = new ModuleRegistrationContainerApi(this);
            _globalContainer = new ModuleGlobalContainerApi(this);
            _commander = new CommanderApi(_actorContext, _commandProcessor);
            _mediatorHub = new MediatorHubApi(this);
        }

        /// <summary>
        /// Reports editor/development errors when code publishes before initialization completes.
        /// </summary>
        internal void CheckCanPublishInEditor()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (_initializer != null && _initializer.IsInitializing)
            {
                MvcDebug.LogError("Cannot publish messages until module initialization has completed.");
            }
#endif
        }

        /// <summary>
        /// Lazily discovers or creates the four child container GameObjects (Services, Model,
        /// Controller, View) and imports mediator prefab mappings from the View registry.
        /// Safe to call multiple times - uses existing containers when already assigned.
        /// </summary>
        internal void EnsureMvcContainers()
        {
            // Ensure order in hierarchy: Services, Model, Controller, View
            _servicesContainer = EnsureContainer(_servicesContainer, "Services");
            _modelContainer = EnsureContainer(_modelContainer, "Model");
            _controllerContainer = EnsureContainer(_controllerContainer, "Controller");

            // Discover view container without auto-creating. Auto-creating a
            // MediatorRegistryBehaviour would cause ModuleViewContainer to return the
            // "View" child transform instead of module.transform for code-spawned modules
            // that have no view registry configured.
            if (_viewContainer == null)
            {
                _viewContainer = GetComponentInChildren<MediatorRegistryBehaviour>(includeInactive: true);
                if (_viewContainer == null)
                {
                    var viewGo = transform.Find("View");
                    if (viewGo != null)
                        _viewContainer = viewGo.GetComponent<MediatorRegistryBehaviour>();
                }
            }

            if (_viewContainer != null && _moduleViewContainer == null)
            {
                var configured = _viewContainer.ViewContainer;
                // Only inherit when the registry's ViewContainer was explicitly set to a
                // different transform (not just the default self-assignment in its getter).
                if (configured != null && configured != _viewContainer.transform)
                    _moduleViewContainer = configured;
            }

            ImportMediatorPrefabMappings();
        }

        // Reads MediatorRegistryBehaviour.MediatorPrefabs and rebuilds the type→prefab lookup.
        private void ImportMediatorPrefabMappings()
        {
            _mediatorPrefabByType.Clear();

            if (_viewContainer == null)
            {
                return;
            }

            var mappings = _viewContainer.MediatorPrefabs;
            if (mappings == null || mappings.Length == 0)
            {
                return;
            }

            for (int i = 0; i < mappings.Length; i++)
            {
                var m = mappings[i];
                if (m == null) continue;
                if (m.Prefab == null) continue;
                if (string.IsNullOrWhiteSpace(m.MediatorTypeName)) continue;

                var type = Type.GetType(m.MediatorTypeName, throwOnError: false);
                if (type == null)
                {
                    MvcDebug.LogWarning($"Mediator registry has unknown mediator type name '{m.MediatorTypeName}' in module '{GetType().Name}'.");
                    continue;
                }

                if (!typeof(MediatorBehaviour).IsAssignableFrom(type))
                {
                    MvcDebug.LogWarning($"Mediator registry mapping type '{type.FullName}' is not a MediatorBehaviour in module '{GetType().Name}'.");
                    continue;
                }

                _mediatorPrefabByType[type] = m.Prefab;
            }
        }

        /// <summary>
        /// Attempts to find a prefab for the supplied mediator type, checking this module's
        /// registry first, then falling back to the app-level <see cref="MvcFacade"/> catalogs.
        /// </summary>
        /// <param name="mediatorType">Mediator type to resolve a prefab for.</param>
        /// <param name="prefab">Resolved prefab when a mapping exists; otherwise null.</param>
        /// <returns><c>true</c> when a prefab was found in either registry.</returns>
        internal bool TryGetMediatorPrefab(Type mediatorType, out GameObject prefab)
        {
            EnsureMvcContainers();

            if (_mediatorPrefabByType.TryGetValue(mediatorType, out prefab) && prefab != null)
                return true;

            return MvcFacade.TryGetAppMediatorPrefab(mediatorType, out prefab);
        }

        // Returns 'existing' if non-null; otherwise searches children by component type, then by
        // child name, and finally creates a new child GameObject with 'defaultName' as a fallback.
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

        // Bridge methods called by ModuleInitializer so the protected overrides stay inaccessible from framework code.
        internal void OnRegisterProxies() => RegisterProxies();
        internal void OnRegisterServices() => RegisterServices();
        internal void OnBindCommands() => BindCommands();
        internal void OnInitMediators() => AttachMediators();
        internal void OnModuleInitialized() => OnInitialized();

        // Returns proxy mappings declared in the Inspector Model registry, or empty when none.
        internal ProxyMapping[] GetProxyMappings()
        {
            EnsureMvcContainers();
            return _modelContainer?.ProxyMappings ?? Array.Empty<ProxyMapping>();
        }

        // Returns service mappings declared in the Inspector Services registry, or empty when none.
        internal ServiceMapping[] GetServiceMappings()
        {
            EnsureMvcContainers();
            return _servicesContainer?.ServiceMappings ?? Array.Empty<ServiceMapping>();
        }

        // Returns command bindings declared in the Inspector Controller registry, or empty when none.
        internal CommandBindingMapping[] GetCommandBindings()
        {
            EnsureMvcContainers();
            return _controllerContainer?.CommandBindings ?? Array.Empty<CommandBindingMapping>();
        }

        // Returns scene mediators declared in the Inspector View registry, or empty when none.
        internal MediatorBehaviour[] GetSceneMediators()
        {
            EnsureMvcContainers();
            return _viewContainer?.SceneMediators ?? Array.Empty<MediatorBehaviour>();
        }

        // Alias used by ModuleInitializer; both Unity-assigned and scene-dragged mediators
        // are sourced from the View registry.
        internal MediatorBehaviour[] GetSerializedMediators() => GetSceneMediators();

        internal List<MediatorBehaviour> GetManualMediators() => _manualMediators;

        // Immediately wires a mediator to this module's container and message bus, bypassing deferred init.
        internal void InitializeMediator(MediatorBehaviour mediator)
        {
            if (mediator == null) return;
            mediator.Initialize(this, _diContainer, _messageBus, deferOnInitialized: false);
        }

        /// <summary>
        /// Unity lifecycle entry point. Ensures <see cref="MvcFacade"/> is ready, runs the module's
        /// phased initialization sequence, and registers this module with the facade.
        /// </summary>
        /// <remarks>
        /// Override <see cref="OnModuleAwake"/> for pre-init work rather than overriding this
        /// method directly. If you must override <c>Awake</c>, always call <c>base.Awake()</c>.
        /// </remarks>
        protected virtual void Awake()
        {
            OnModuleAwake();

            // Ensure facade exists (and AttributeScanner is scanned) BEFORE module initialization
            _ = MvcFacade.FacadeInstance;

            InitializeModule();
            MvcFacade.FacadeInstance.RegisterModule(this);
        }

        /// <summary>
        /// Unity lifecycle exit point. Tears down all mediators, proxies, and the command
        /// processor, then unregisters from <see cref="MvcFacade"/>.
        /// </summary>
        /// <remarks>
        /// Override <see cref="OnModuleDestroy"/> for post-cleanup work rather than overriding
        /// this method directly.
        /// </remarks>
        protected virtual void OnDestroy()
        {
            if (_initializer != null)
            {
                _initializer.MediatorRegistrar?.Cleanup();
                _initializer.ProxyRegistrar?.Cleanup();
                _initializer.CleanupServices();
            }

            _manualMediators.Clear();

            _commandProcessor?.Dispose();
            _commandProcessor = null;
            _messageBus = null; // release reference only; bus is owned by MvcFacade
            _diContainer = null;

            var facade = MvcFacade.InstanceOrNull;
            if (facade != null)
            {
                facade.UnregisterModule(this);
            }

            OnModuleDestroy();
        }

        /// <summary>
        /// Called at the very beginning of <see cref="Awake"/>, before the framework runs its
        /// initialization sequence. Use for any module-specific pre-init setup.
        /// </summary>
        protected virtual void OnModuleAwake() { }

        /// <summary>
        /// Called at the very end of <see cref="OnDestroy"/>, after the framework has torn down
        /// all actors and unregistered from <see cref="MvcFacade"/>. Use for any module-specific
        /// post-destroy cleanup.
        /// </summary>
        protected virtual void OnModuleDestroy() { }

        /// <summary>
        /// Called by container APIs whenever a dependency is registered via code. Validates
        /// registration phase (dev/editor builds), re-parents ProxyBehaviours into the Model
        /// container, and informs the proxy registrar so it can track lifecycle cleanup.
        /// </summary>
        internal void OnContainerRegistering(object instance)
        {
            if (instance == null) return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // Validate that proxies are registered during the correct phase
            if (instance is ProxyBehaviour || instance is Proxy)
            {
                if (_initializer != null)
                {
                    var currentPhase = _initializer.CurrentPhase;
                    
                    // Allow proxy registration during:
                    // 1. Proxies phase (RegisterProxies)
                    // 2. Initialized phase (from commands or runtime)
                    // 3. NotStarted (edge case for pre-init)
                    bool isValidPhase = currentPhase == InitializationPhase.Proxies 
                                     || currentPhase == InitializationPhase.Initialized 
                                     || currentPhase == InitializationPhase.NotStarted;
                    
                    if (!isValidPhase)
                    {
                        var typeName = instance.GetType().Name;
                        var phaseName = currentPhase.ToString();
                        var correctMethod = GetMethodNameForPhase(currentPhase);
                        
                        MvcDebug.LogError(
                            $"Registration error in module '{GetType().Name}':\n" +
                            $"Proxy '{typeName}' is being registered during '{phaseName}' phase!\n" +
                            $"Proxies can only be registered in:\n" +
                            $"  - RegisterProxies() method (setup phase)\n" +
                            $"  - Commands executed after module initialization (dynamic registration)\n" +
                            $"Current call is from {correctMethod}() which runs too early.\n" +
                            $"Move this registration to RegisterProxies() or to a command executed via OnInitialized().");
                    }
                }
            }
#endif

            if (instance is ProxyBehaviour pb)
            {
                if (pb == null || pb.gameObject == null)
                {
                    throw new InvalidOperationException(
                        $"[MvcExpress] Invalid registration: '{instance.GetType().FullName}' is a ProxyBehaviour and cannot be registered using 'new'. " +
                        $"Create it with GameObject.AddComponent and use RegisterBehaviour<TBehaviour>() instead.");
                }

                EnsureMvcContainers();

                if (_modelContainer != null && pb.transform != null && pb.transform.parent != _modelContainer.transform)
                {
                    pb.transform.SetParent(_modelContainer.transform, false);
                }

                _initializer?.ProxyRegistrar?.TrackProxyBehaviour(pb);
                return;
            }

            if (instance is Proxy proxy)
            {
                _initializer?.ProxyRegistrar?.TrackCodeProxy(proxy);
            }
        }

        /// <summary>
        /// Removes every mediator that was attached at runtime (after the
        /// <see cref="AttachMediators"/> phase completed). Setup-time (scene) mediators
        /// are unaffected.
        /// </summary>
        /// <returns><c>true</c> when all runtime mediators were successfully removed.</returns>
        internal bool DetachAllRuntimeMediatorsInternal()
        {
            if (_initializer == null)
            {
                MvcDebug.LogWarning($"Cannot detach mediators because module '{GetType().Name}' is not initialized.");
                return false;
            }

            var registrar = _initializer.MediatorRegistrar;
            var runtimeMediators = registrar.RuntimeMediators;

            if (runtimeMediators.Count == 0)
            {
                return true;
            }

            var mediatorsToRemove = new List<MediatorBehaviour>(runtimeMediators);
            bool allSuccessful = true;

            for (int i = mediatorsToRemove.Count - 1; i >= 0; i--)
            {
                var mediator = mediatorsToRemove[i];
                if (mediator != null)
                {
                    bool removed = registrar.RemoveRuntimeMediator(mediator);
                    if (!removed)
                    {
                        allSuccessful = false;
                        MvcDebug.LogWarning($"Failed to detach runtime mediator '{mediator.name}' from module '{GetType().Name}'.");
                    }
                }
            }

            return allSuccessful;
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private string GetMethodNameForPhase(InitializationPhase phase)
        {
            switch (phase)
            {
                case InitializationPhase.Services:
                    return "RegisterServices";
                case InitializationPhase.Proxies:
                    return "RegisterProxies";
                case InitializationPhase.Commands:
                    return "BindCommands";
                case InitializationPhase.Mediators:
                    return "AttachMediators";
                case InitializationPhase.Finalization:
                case InitializationPhase.Initialized:
                    return "OnInitialized";
                default:
                    return phase.ToString();
            }
        }
#endif

        private Type _cachedModuleType;       // lazily assigned by ModuleType property
        private string _cachedModuleTypeName;  // lazily assigned by IdName property

        /// <summary>
        /// Concrete type of this module, cached to avoid repeated <c>GetType()</c> allocations
        /// during editor UI repaints and initialization logging.
        /// </summary>
        public Type ModuleType => _cachedModuleType ??= GetType();

        /// <summary>
        /// Short display name derived from <see cref="ModuleType"/>. Used in log messages,
        /// console tooling, and <see cref="MvcFacade"/> module lookup.
        /// </summary>
        public string IdName => _cachedModuleTypeName ??= ModuleType.Name;

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only. Returns all non-proxy registrations currently in the DI container.
        /// Consumed by the <c>ServiceRegistryBehaviour</c> custom inspector for live debugging.
        /// </summary>
        public IEnumerable<RegistrationInfo> GetAllServices()
        {
            if (_diContainer == null || _initializer == null || !_initializer.IsInitialized)
                yield break;

            foreach (var instance in _diContainer.EnumerateAllInstances())
            {
                // Services are anything that's NOT a Proxy or ProxyBehaviour
                if (instance is Proxy or ProxyBehaviour)
                    continue;

                // Determine registration source
                var source = DetermineRegistrationSource(instance, isService: true);

                yield return new RegistrationInfo
                {
                    Type = instance.GetType(),
                    Instance = instance,
                    Source = source,
                    IsMonoBehaviour = instance is UnityEngine.MonoBehaviour
                };
            }
        }

        /// <summary>
        /// Editor-only. Returns all <see cref="Proxy"/> and <see cref="ProxyBehaviour"/>
        /// registrations currently in the DI container. Consumed by the
        /// <c>ProxyRegistryBehaviour</c> custom inspector for live debugging.
        /// </summary>
        public IEnumerable<RegistrationInfo> GetAllProxies()
        {
            if (_diContainer == null || _initializer == null || !_initializer.IsInitialized)
                yield break;

            foreach (var instance in _diContainer.EnumerateAllInstances())
            {
                // Proxies are Proxy or ProxyBehaviour types
                if (instance is not (Proxy or ProxyBehaviour))
                    continue;

                // Determine registration source
                var source = DetermineRegistrationSource(instance, isService: false);

                yield return new RegistrationInfo
                {
                    Type = instance.GetType(),
                    Instance = instance,
                    Source = source,
                    IsMonoBehaviour = instance is UnityEngine.MonoBehaviour
                };
            }
        }

        // Classifies a registered instance as Unity (Inspector-dragged), Attribute ([Register]/[Bind]),
        // or Code (manual Register() call) for display in the custom inspector.
        private RegistrationSource DetermineRegistrationSource(object instance, bool isService)
        {
            // Check Unity registry
            if (instance is UnityEngine.MonoBehaviour)
            {
                if (isService)
                {
                    var serviceMappings = GetServiceMappings();
                    if (serviceMappings != null)
                    {
                        foreach (var mapping in serviceMappings)
                        {
                            if (mapping.Service == (UnityEngine.Object)instance)
                                return RegistrationSource.Unity;
                        }
                    }
                }
                else
                {
                    var proxyMappings = GetProxyMappings();
                    if (proxyMappings != null)
                    {
                        foreach (var mapping in proxyMappings)
                        {
                            if (mapping.Proxy == (UnityEngine.Object)instance)
                                return RegistrationSource.Unity;
                        }
                    }
                }
            }

            // Check attribute registry (from ModuleInitializer's tracking lists)
            if (_initializer != null)
            {
                if (isService)
                {
                    var attributeServices = _initializer.GetAttributeServices();
                    if (attributeServices != null && attributeServices.Contains(instance))
                        return RegistrationSource.Attribute;
                }
                else
                {
                    var attributeProxies = _initializer.GetAttributeProxies();
                    if (attributeProxies != null && attributeProxies.Contains(instance))
                        return RegistrationSource.Attribute;
                }
            }

            // Default to code registration
            return RegistrationSource.Code;
        }

        /// <summary>
        /// Editor-only. Snapshot of a single DI container registration for inspector display.
        /// </summary>
        public struct RegistrationInfo
        {
            /// <summary>Runtime type of the registered instance.</summary>
            public Type Type;
            /// <summary>The registered instance itself.</summary>
            public object Instance;
            /// <summary>How this instance was registered.</summary>
            public RegistrationSource Source;
            /// <summary><c>true</c> when the instance derives from <see cref="MonoBehaviour"/>.</summary>
            public bool IsMonoBehaviour;
        }

        /// <summary>
        /// Editor-only. Indicates which of the three registration methods was used.
        /// </summary>
        public enum RegistrationSource
        {
            /// <summary>Dragged into the Inspector registry at authoring time.</summary>
            Unity,
            /// <summary>Auto-registered via <c>[Register]</c>, <c>[Bind]</c>, or <c>[Attach]</c> attribute.</summary>
            Attribute,
            /// <summary>Registered explicitly in code from <see cref="RegisterServices"/>, <see cref="RegisterProxies"/>, or a command.</summary>
            Code
        }
#endif
    }

}

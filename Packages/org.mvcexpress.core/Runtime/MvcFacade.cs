﻿using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Initialization;
using mvcExpress.Internal.Messaging;
using mvcExpress.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using UnityEngine;

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
    public sealed class MvcFacade : MonoBehaviour
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
        public static MvcFacade FacadeInstance
        {
            get
            {
                if (_isQuitting)
                    return _facadeInstance;

                if (_facadeInstance != null && _facadeInstance.gameObject != null)
                    return _facadeInstance;

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

        // Forces facade creation if not yet alive. Use GlobalContainerOrNull in teardown paths.
        internal static MvcDiContainer Global => FacadeInstance._globalContainer;

        // Null-safe accessor used by Unregister paths that may run after MvcFacade.OnDestroy.
        internal static MvcDiContainer GlobalContainerOrNull => _facadeInstance?._globalContainer;

        // Tracks and initializes proxies registered in the global scope; cleaned up on OnDestroy.
        private GlobalProxyRegistrar _globalProxyRegistrar;

        // Tracks and initializes services registered in the global scope; cleaned up on OnDestroy.
        private mvcExpress.Internal.Initialization.GlobalServiceRegistrar _globalServiceRegistrar;

        // Single bus shared by all modules and global actors; owned here, referenced by modules.
        private readonly MvcMessageBus _messageBus = new MvcMessageBus();

        // Forces facade creation if not yet alive; safe to call from module Awake.
        internal static MvcMessageBus MessageBus => FacadeInstance._messageBus;

        // Actions enqueued by MessengerApi.PublishDeferred from any thread; drained each Update on the main thread.
        private readonly ConcurrentQueue<Action> _deferredPublishes = new ConcurrentQueue<Action>();

        /// <summary>
        /// Enqueues a publish action to run on the main thread during the next Update.
        /// Called by <see cref="MessengerApi.PublishDeferred{TMessage}()"/> from any thread.
        /// Returns false (and drops the action) when the facade has already been destroyed.
        /// </summary>
        internal static bool TryEnqueueDeferredPublish(Action publishAction)
        {
            var facade = _facadeInstance;
            if (facade == null) return false;
            facade._deferredPublishes.Enqueue(publishAction);
            return true;
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
        private void Update()
        {
            while (_deferredPublishes.TryDequeue(out var action))
            {
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
        /// Primarily intended for tests and deep resets. Runtime gameplay code should prefer
        /// explicit unregister operations for known transient global dependencies.
        /// </remarks>
        public void ClearGlobalContainer()
        {
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
        /// Unity lifecycle. Disposes the message bus, cleans up global proxy and service
        /// registrars, and clears the static facade reference.
        /// </summary>
        private void OnDestroy()
        {
            _messageBus?.Dispose();

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

            if (_facadeInstance == this)
            {
                _facadeInstance = null;
            }
        }

        // Runs DontDestroyOnLoad, scans assemblies for attribute-based registrations, and
        // initializes global service/proxy registrars. Idempotent - guarded by _initialized.
        private void InitializeIfNeeded()
        {
            if (_initialized) return;
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
            // Inspector-driven registries have been processed (Unity style takes precedence).
#if MVC_EXPRESS_NO_ATTRIBUTE
            // Attribute style disabled via Project Settings > mvcExpress > Composition.
#else
            DrainAttributeGlobalRegistrations();
#endif

            _initialized = true;
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
                wasRemoved = _modules.Remove(moduleType);

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
        private void DrainAttributeGlobalRegistrations()
        {
            var entries = mvcExpress.Internal.Initialization.AttributeScanner.GetGlobalRegistrationMetadata();
            if (entries.Count == 0)
                return;

            // Collect instances so we can call CompleteInitialization after all are registered
            // (mirrors the deferred-init pattern used by GlobalProxyRegistrar and GlobalServiceRegistrar).
            var pendingProxies = new List<Proxy>(entries.Count);
            var pendingProxyBehaviours = new List<ProxyBehaviour>(entries.Count);
            var pendingObjects = new List<object>(entries.Count);

            for (int i = 0; i < entries.Count; i++)
            {
                var meta = entries[i];

                MvcCompositionStyleWarning.WarnIfDisabled(
                    MvcCompositionStyle.Attribute,
                    $"[RegisterGlobal] global registration of '{meta.ConcreteType.Name}'");

                object instance;

                if (meta.IsMonoBehaviour)
                {
                    var parentContainer = meta.IsProxy ? _globalProxyRegistry.transform : _globalServiceRegistry.transform;

                    // Prefer an instance already tracked via Unity registry (Inspector-wired).
                    MonoBehaviour preTracked = null;
                    if (meta.IsProxy)
                    {
                        var proxyMappings = _globalProxyRegistry.ProxyMappings;
                        for (int m = 0; m < proxyMappings.Length; m++)
                        {
                            var p = proxyMappings[m]?.Proxy;
                            if (p != null && p.GetType() == meta.ConcreteType) { preTracked = p; break; }
                        }
                    }
                    else
                    {
                        var serviceMappings = _globalServiceRegistry.ServiceMappings;
                        for (int m = 0; m < serviceMappings.Length; m++)
                        {
                            var s = serviceMappings[m]?.Service;
                            if (s != null && s.GetType() == meta.ConcreteType) { preTracked = s; break; }
                        }
                    }

                    // Not tracked: find a hand-placed instance anywhere under the facade, or create
                    // one under the Global Proxies/Global Services container if none exists.
                    var resolution = mvcExpress.Internal.Initialization.AttributeMonoBehaviourResolver.Resolve(
                        transform, meta.ConcreteType, parentContainer, preTracked);

                    if (resolution.Kind == mvcExpress.Internal.Initialization.MonoBehaviourResolutionKind.Ambiguous)
                    {
                        MvcDebug.LogError(
                            $"[RegisterGlobal] Type '{meta.ConcreteType.FullName}' has {resolution.Conflicts.Length} " +
                            $"instances in the MvcFacade hierarchy: " +
                            $"{string.Join(", ", Array.ConvertAll(resolution.Conflicts, go => go.name))}. " +
                            $"Remove the duplicate(s) so the correct instance is unambiguous. Skipping registration.");
                        continue;
                    }

#if UNITY_EDITOR || MVC_LOGGING
                    if (resolution.Kind == mvcExpress.Internal.Initialization.MonoBehaviourResolutionKind.Created)
                    {
                        MvcDebug.Log(
                            $"[RegisterGlobal] Auto-created '{meta.ConcreteType.Name}' under " +
                            $"{(meta.IsProxy ? "Global Proxies" : "Global Services")} container.");
                    }
#endif

                    instance = resolution.Instance;
                }
                else
                {
                    try
                    {
                        instance = Activator.CreateInstance(meta.ConcreteType);
                    }
                    catch (Exception ex)
                    {
                        MvcDebug.LogError(
                            $"[RegisterGlobal] Failed to create instance of '{meta.ConcreteType.FullName}': {ex.Message}");
                        continue;
                    }
                }

                bool isScoped = meta.Lifecycle == RegistrationLifecycle.Scoped;

                try
                {
                    var builder = _globalContainer.Register(instance, meta.ConcreteType);

                    if (meta.RegisterToLogic)
                    {
                        if (meta.LogicType == meta.ConcreteType)
                            builder.ToLogic();
                        else
                            builder.ToLogicAs(meta.LogicType);
                    }

                    if (meta.RegisterToView)
                    {
                        if (meta.ViewType == meta.ConcreteType)
                            builder.ToView();
                        else
                            builder.ToViewAs(meta.ViewType);
                    }

                    if (isScoped)
                        builder.AsScoped();
                    else if (meta.Lifecycle == RegistrationLifecycle.Transient)
                        builder.AsTransient();
                    else
                        builder.AsPermanent();
                }
                catch (Exception ex)
                {
                    MvcDebug.LogError(
                        $"[RegisterGlobal] Failed to register '{meta.ConcreteType.FullName}' into global container: {ex.Message}");
                    continue;
                }

                // Scoped: 'instance' was a throwaway created only to walk the builder above - the
                // container discarded it and will construct its own per resolution scope. Skip
                // deferred initialization entirely; there is nothing valid left to initialize.
                if (isScoped)
                    continue;

                // Defer initialization so all global registrations are visible to injection.
                if (meta.IsProxy && instance is Proxy proxy)
                {
                    proxy.Initialize(meta.ConcreteType, _messageBus, _globalContainer, deferOnInitialized: true);
                    pendingProxies.Add(proxy);
                }
                else if (meta.IsProxy && instance is ProxyBehaviour proxyBehaviour)
                {
                    proxyBehaviour.InitializeGlobal(_globalContainer, _messageBus, deferOnInitialized: true);
                    pendingProxyBehaviours.Add(proxyBehaviour);
                }
                else
                {
                    pendingObjects.Add(instance);
                }
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
                    lifecycle.OnInitialized();
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

using mvcExpress.Internal.Commands;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;
using System;
using System.Collections.Generic;
using System.Reflection;
using mvcExpress.Logging;
using UnityEngine;

namespace mvcExpress.Internal.Initialization
{
    /// <summary>
    /// Initialization phases for MvcModule.
    /// Represents the current state of module initialization.
    /// </summary>
    internal enum InitializationPhase
    {
        /// <summary>
        /// Module has not started initialization.
        /// </summary>
        NotStarted = 0,

        /// <summary>
        /// Initializing core framework services (Messenger, Commander, Container).
        /// </summary>
        CoreServices = 1,

        /// <summary>
        /// Initializing service layer (user-registered services).
        /// </summary>
        Services = 2,

        /// <summary>
        /// Initializing model layer (Proxies).
        /// </summary>
        Proxies = 3,

        /// <summary>
        /// Initializing controller layer (Commands).
        /// </summary>
        Commands = 4,

        /// <summary>
        /// Initializing view layer (Mediators).
        /// </summary>
        Mediators = 5,

        /// <summary>
        /// Finalizing initialization (calling user hooks).
        /// </summary>
        Finalization = 6,

        /// <summary>
        /// Module initialization completed successfully.
        /// </summary>
        Initialized = 7,

        /// <summary>
        /// Module initialization failed.
        /// </summary>
        Failed = 10
    }

    /// <summary>
    /// Drives the strict 5-phase initialization sequence for a single <see cref="MvcModule"/>.
    /// </summary>
    /// <remarks>
    /// Why this exists as a separate class: extracting initialization from MvcModule keeps the
    /// module MonoBehaviour thin and makes the sequencing logic unit-testable.
    ///
    /// The mandatory ordering is:
    ///   1. CoreServices  - framework objects (bus, container, command processor)
    ///   2. Services      - user services (Unity registry → attribute → code)
    ///   3. Proxies       - model layer (Unity registry → attribute → code)
    ///   4. Commands      - controller layer (Unity registry → attribute → code)
    ///   5. Mediators     - view layer (Unity registry → attribute → code)
    ///   6. Finalization  - calls user's OnModuleInitialized()
    ///
    /// Breaking the order causes hard-to-diagnose injection failures because actors
    /// attempt to resolve dependencies that have not yet been registered.
    /// This class is internal; MvcModule is the only consumer.
    /// </remarks>
    internal sealed class ModuleInitializer
    {
        // The concrete module type (e.g. GameModule) used for logging and attribute lookup.
        private readonly Type _moduleType;
        // The MonoBehaviour instance - needed to call user lifecycle hooks and start coroutines.
        private readonly MvcModule _moduleContext;
        private readonly MvcDiContainer _diContainer;
        private readonly MvcMessageBus _messageBus;
        private readonly MvcCommandProcessor _commandProcessor;

        // Delegate sub-registrars to keep this class focused on sequencing, not registration details.
        private readonly ProxyRegistrar _proxyRegistrar;
        private readonly MediatorRegistrar _mediatorRegistrar;

        // Monotonically advancing phase guard - only forward transitions are allowed.
        private InitializationPhase _currentPhase = InitializationPhase.NotStarted;
        // Captured on failure so the public FailureException property can surface it after the fact.
        private Exception _failureException;
        // Deferred error text logged one frame after the exception so Unity's own exception output
        // appears first in the console, improving readability.
        private string _pendingErrorSummary;

        // Attribute-registered services are tracked separately so their OnInitialized() is called
        // in insertion order (step 5), after Unity-registry services (step 4) and before
        // code-registered services (step 6).
        private readonly List<object> _attributeRegisteredServices = new List<object>();

        // All IMvcLifecycle services in registration order. OnCleanup() is called in reverse
        // (last registered, first cleaned up) during module destruction.
        private readonly List<IMvcLifecycle> _lifecycleServices = new List<IMvcLifecycle>();

        // Attribute-registered proxies are tracked so the MvcConsole can display the registration
        // source for each proxy in the architecture view.
        private readonly List<object> _attributeRegisteredProxies = new List<object>();

        /// <summary>
        /// Current initialization phase.
        /// </summary>
        public InitializationPhase CurrentPhase => _currentPhase;

        /// <summary>
        /// True if initialization has completed successfully.
        /// </summary>
        public bool IsInitialized => _currentPhase == InitializationPhase.Initialized;

        /// <summary>
        /// True if initialization is currently in progress.
        /// </summary>
        public bool IsInitializing => _currentPhase > InitializationPhase.NotStarted
                                    && _currentPhase < InitializationPhase.Initialized
                                     && _currentPhase != InitializationPhase.Failed;

        /// <summary>
        /// True if initialization has failed.
        /// </summary>
        public bool IsFailed => _currentPhase == InitializationPhase.Failed;

        /// <summary>
        /// Exception that caused initialization failure, if any.
        /// </summary>
        public Exception FailureException => _failureException;

        /// <summary>
        /// Constructs the initializer and its sub-registrars.
        /// All parameters are required; null arguments are rejected immediately.
        /// </summary>
        public ModuleInitializer(
            Type moduleType,
            MvcDiContainer diContainer,
            MvcMessageBus messageBus,
            MvcCommandProcessor commandProcessor,
            MvcModule module)
        {
            if (moduleType == null) throw new ArgumentNullException(nameof(moduleType));
            if (diContainer == null) throw new ArgumentNullException(nameof(diContainer));
            if (messageBus == null) throw new ArgumentNullException(nameof(messageBus));
            if (commandProcessor == null) throw new ArgumentNullException(nameof(commandProcessor));
            if (module == null) throw new ArgumentNullException(nameof(module));

            _moduleType = moduleType;
            _diContainer = diContainer;
            _messageBus = messageBus;
            _commandProcessor = commandProcessor;
            _moduleContext = module;

            _proxyRegistrar = new ProxyRegistrar(_moduleType, _diContainer, _messageBus, _moduleContext);
            _mediatorRegistrar = new MediatorRegistrar(_moduleType, _diContainer, _messageBus, _moduleContext);
        }

        /// <summary>
        /// Execute the complete 5-phase initialization sequence.
        /// </summary>
        /// <remarks>
        /// Safe to call only once. Calling again after success logs a warning and returns.
        /// Calling again after failure throws immediately - a failed module cannot be re-initialized.
        /// Any exception during a phase sets the phase to <see cref="InitializationPhase.Failed"/>
        /// and re-throws after scheduling a deferred summary log (one frame later, so Unity's own
        /// exception output appears first in the console).
        /// </remarks>
        internal void Initialize()
        {
            // Prevent re-initialization or initialization while in progress
            if (_currentPhase != InitializationPhase.NotStarted)
            {
                if (_currentPhase == InitializationPhase.Initialized)
                {
                    MvcDebug.LogWarning($"Module '{_moduleType}' is already initialized.");
                    return;
                }

                if (_currentPhase == InitializationPhase.Failed)
                {
                    throw new InvalidOperationException(
                        $"[ModuleInitializer] Cannot re-initialize module '{_moduleType}' after failure. " +
                        $"Previous error: {_failureException?.Message}");
                }

                throw new InvalidOperationException(
                    $"[ModuleInitializer] Module '{_moduleType}' is already initializing (current phase: {_currentPhase}).");
            }

            // LOG: Module initialization started
            var startTime = UnityEngine.Time.realtimeSinceStartup;

#if UNITY_EDITOR || MVC_LOGGING
            mvcExpress.Logging.MvcLogInternal.LogModuleInitializationStarted(_moduleContext, _moduleContext.gameObject);
#endif

            try
            {
                // Phase 1: Core Services
                TransitionToPhase(InitializationPhase.CoreServices);
                InitializeCoreServices();

                // Phase 2: Services Layer
                TransitionToPhase(InitializationPhase.Services);
                InitializeServices();

                // Phase 3: Model Layer (Proxies)
                TransitionToPhase(InitializationPhase.Proxies);
                InitializeProxies();

                // Phase 4: Controller Layer (Commands)
                TransitionToPhase(InitializationPhase.Commands);
                InitializeCommands();

                // Phase 5: View Layer (Mediators)
                TransitionToPhase(InitializationPhase.Mediators);
                InitializeMediators();

                // Mark as completed
                TransitionToPhase(InitializationPhase.Initialized);

                // User hook
                FinalizeInitialization();

#if UNITY_EDITOR || MVC_LOGGING
                // LOG: Module initialization completed
                var elapsedTime = UnityEngine.Time.realtimeSinceStartup - startTime;
                mvcExpress.Logging.MvcLogInternal.LogModuleInitializationCompleted(_moduleContext, _moduleContext.gameObject, elapsedTime);
#endif

            }
            catch (Exception ex)
            {
                var failedPhase = _currentPhase;
                _failureException = ex;
                _currentPhase = InitializationPhase.Failed;

                // Find the innermost exception with a meaningful message
                var rootCause = ex;
                while (rootCause.InnerException != null)
                {
                    rootCause = rootCause.InnerException;
                }

                // Build summary message to log AFTER Unity's exception handler
                _pendingErrorSummary =
                   $"[ModuleInitializer] Module '{_moduleType}' failed at phase '{failedPhase}'.\n" +
                   $"Root cause: {rootCause.GetType().Name}: {rootCause.Message}\n" +
                   $"Full exception chain: {ex.GetType().Name}: {ex.Message}";

                // Schedule summary to be logged after Unity's exception handler completes.
                // StartCoroutine requires an active game object; on inactive ones Unity emits
                // a spurious LogError about the inactive coroutine. Drop the deferred summary
                // in that case - the thrown exception already carries full error context.
                if (_moduleContext.isActiveAndEnabled)
                {
                    _moduleContext.StartCoroutine(LogErrorSummaryNextFrame());
                }
                else
                {
                    _pendingErrorSummary = null;
                }

                throw; // Unity logs exception first
            }
        }

        // Coroutine that emits the deferred error summary one frame after the thrown exception.
        // This ensures Unity's built-in exception log appears first in the console, giving
        // the developer the full stack trace before seeing the framework summary.
        private System.Collections.IEnumerator LogErrorSummaryNextFrame()
        {
            yield return null;

            if (!string.IsNullOrEmpty(_pendingErrorSummary))
            {
                MvcDebug.LogError(_pendingErrorSummary);
                _pendingErrorSummary = null;
            }
        }

        /// <summary>
        /// Transition to a new initialization phase.
        /// Validates that the transition is valid.
        /// </summary>
        private void TransitionToPhase(InitializationPhase newPhase)
        {
            // Validate phase transition order to catch misuse early
            if (newPhase <= _currentPhase && newPhase != InitializationPhase.Failed)
            {
                throw new InvalidOperationException(
                    $"[ModuleInitializer] Invalid phase transition from {_currentPhase} to {newPhase}. " +
                    "Phases must progress in order.");
            }

            _currentPhase = newPhase;
        }

        /// <summary>
        /// Phase 1: Initialize core framework services (Messenger, Commander, Container).
        /// </summary>
        private void InitializeCoreServices()
        {
            ValidatePhase(InitializationPhase.CoreServices);

            _moduleContext.EnsureCoreServicesInitialized();
        }

        /// <summary>
        /// Register services configured in the inspector and via module code.
        /// Executed before proxy registration.
        /// </summary>
        private void InitializeServices()
        {
            ValidatePhase(InitializationPhase.Services);

            // 1) Register services configured in the inspector (so proxies/commands can inject them)
            RegisterSerializedServiceBehaviours();

            // 2) Register services marked with [Register] attribute
            RegisterAttributeServices();

            // 3) Allow user to register services with code.
            _moduleContext.OnRegisterServices();

            // 4) Initialize services that opt-in to module lifecycle.
            InitializeRegisteredServices();

        }

        // Calls OnInitialized() on all services after all three registration sources are complete.
        // Three-step order mirrors registration order: Unity → Attribute → Code.
        // Splitting registration and initialization allows all services to be registered in the
        // DI container before any of them try to inject dependencies on each other.
        private void InitializeRegisteredServices()
        {
            // Step 4: Initialize Unity services (from ServiceMappings)
            var mappings = _moduleContext.GetServiceMappings();
            if (mappings != null && mappings.Length > 0)
            {
                for (int i = 0; i < mappings.Length; i++)
                {
                    var mapping = mappings[i];
                    if (mapping == null) continue;

                    var svc = mapping.Service;
                    if (svc == null) continue;

                    MvcInjectionUtility.InjectMembers(svc, _diContainer, useViewScope: false);

                    if (svc is mvcExpress.IMvcLifecycle initializable)
                    {
                        try
                        {
                            initializable.OnInitialized();
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException(
                                $"[ModuleInitializer] Service '{svc.GetType().FullName}' (GameObject: '{svc.gameObject.name}') " +
                                $"failed during OnInitialized in module '{_moduleType}'.\n" +
                                $"Original error: {ex.Message}", ex);
                        }

                        _lifecycleServices.Add(initializable);
                    }
                }
            }

            // Step 5: Initialize attribute-registered services
            if (_attributeRegisteredServices.Count > 0)
            {
                foreach (var service in _attributeRegisteredServices)
                {
                    MvcInjectionUtility.InjectMembers(service, _diContainer, useViewScope: false);

                    if (service is mvcExpress.IMvcLifecycle initializable)
                    {
                        try
                        {
                            initializable.OnInitialized();
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidOperationException(
                                $"[ModuleInitializer] Attribute service '{service.GetType().FullName}' failed during OnInitialized in module '{_moduleType}'. " +
                                $"Phase: {InitializationPhase.Services}", ex);
                        }

                        _lifecycleServices.Add(initializable);
                    }
                }
            }

            // Step 6: Initialize code-registered services (excluding Unity and attribute services)
            InitializeCodeRegisteredServicesFromContainer();
        }

        private void InitializeCodeRegisteredServicesFromContainer()
        {
            // Use the new internal API - no reflection needed!
            // This is 10-25x faster than the old reflection-based approach.
            //
            // A dual-scope registration (e.g. Register(svc).ToLogic().ToView()) stores the same
            // instance under two different container keys, so EnumerateAllInstances yields it twice.
            // Without dedup this loop would inject it, call OnInitialized(), and add it to
            // _lifecycleServices twice - so OnCleanup() fires twice too at teardown (see M2).
            var processedInstances = new HashSet<object>();

            foreach (var instance in _diContainer.EnumerateAllInstances())
            {
                if (!processedInstances.Add(instance))
                {
                    continue;
                }

                // Skip Unity services (they're handled separately via ServiceMappings - step 4)
                if (instance is UnityEngine.Object)
                {
                    continue;
                }

                // Skip attribute-registered services (they're handled separately - step 5)
                if (_attributeRegisteredServices.Contains(instance))
                {
                    continue;
                }

                // Only code-registered plain services remain (step 6).
                // Resolve [Inject]/[InjectGlobal] properties now - all services are
                // already in the container, so circular property-injection dependencies
                // (A↔B, A→B→C→A) can be satisfied without a two-phase workaround.
                MvcInjectionUtility.InjectMembers(instance, _diContainer, useViewScope: false);

                if (instance is mvcExpress.IMvcLifecycle initializable)
                {
                    try
                    {
                        initializable.OnInitialized();
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            $"[ModuleInitializer] Code service '{instance.GetType().FullName}' failed during OnInitialized in module '{_moduleType}'. " +
                            $"Phase: {InitializationPhase.Services}", ex);
                    }

                    _lifecycleServices.Add(initializable);
                }
            }

        }

        /// <summary>
        /// Phase 2: Initialize all proxies (serialized, code-only, and user-registered).
        /// This phase populates the model layer.
        /// </summary>
        private void InitializeProxies()
        {
            ValidatePhase(InitializationPhase.Proxies);

            _proxyRegistrar.ClearTrackingLists();

            // 1) Register proxies configured in the inspector.
#if MVC_EXPRESS_NO_UNITY
            // Unity style disabled via Project Settings > mvcExpress > Composition.
#else
            var proxyMappings = _moduleContext.GetProxyMappings();
            if (proxyMappings != null && proxyMappings.Length > 0)
            {
                MvcCompositionStyleWarning.WarnIfDisabled(
                    MvcCompositionStyle.Unity,
                    $"Proxy Registry on module '{_moduleType.Name}'");
            }
            _proxyRegistrar.RegisterSerializedProxyBehaviours();
#endif

            // 2) Register proxies marked with [Register] attribute.
            RegisterAttributeProxies();

            // 3) Allow user to register proxies with code.
            _moduleContext.OnRegisterProxies();

            // 4) Complete proxy initialization after all dependencies are known.
            _proxyRegistrar.CompleteProxyInitialization();

        }

        /// <summary>
        /// Register proxies marked with [Register] attribute.
        /// Uses cached metadata from AttributeScanner for performance.
        /// </summary>
        private void RegisterAttributeProxies()
        {
#if MVC_EXPRESS_NO_ATTRIBUTE
            return;
#else
            // Get cached proxy metadata for this module
            var proxyMetadata = AttributeScanner.GetProxyMetadata(_moduleType);

            if (proxyMetadata.Count == 0)
                return;

            MvcCompositionStyleWarning.WarnIfDisabled(
                MvcCompositionStyle.Attribute,
                $"[Register] proxy metadata for module '{_moduleType.Name}'");

            // Get ProxyBehaviours tracked from Unity registry
            var trackedProxies = _proxyRegistrar.GetTrackedProxyBehaviours();

            foreach (var metadata in proxyMetadata)
            {
                object proxyInstance = null;

                // Check if this is a ProxyBehaviour (MonoBehaviour)
                if (typeof(ProxyBehaviour).IsAssignableFrom(metadata.ProxyType))
                {
                    // Prefer an instance already tracked via Unity registry (Inspector-wired).
                    ProxyBehaviour preTracked = null;
                    if (trackedProxies != null)
                    {
                        foreach (var tracked in trackedProxies)
                        {
                            if (tracked != null && tracked.GetType() == metadata.ProxyType)
                            {
                                preTracked = tracked;
                                break;
                            }
                        }
                    }

                    // Not tracked: find a hand-placed instance anywhere in the module hierarchy,
                    // or create one under the Model container if none exists.
                    var resolution = AttributeMonoBehaviourResolver.Resolve(
                        _moduleContext.transform,
                        metadata.ProxyType,
                        _moduleContext.ModelContainer,
                        preTracked);

                    if (resolution.Kind == MonoBehaviourResolutionKind.Ambiguous)
                    {
                        MvcDebug.LogError(
                            $"Proxy '{metadata.ProxyType.FullName}' has [Register] attribute but " +
                            $"{resolution.Conflicts.Length} instances exist in module '{_moduleType}''s hierarchy: " +
                            $"{string.Join(", ", Array.ConvertAll(resolution.Conflicts, go => go.name))}. " +
                            $"Remove the duplicate(s) so the correct instance is unambiguous. Skipping registration.");
                        continue;
                    }

                    proxyInstance = resolution.Instance;

#if UNITY_EDITOR || MVC_LOGGING
                    if (resolution.Kind == MonoBehaviourResolutionKind.Created)
                    {
                        MvcDebug.Log(
                            $"[Register] Auto-created '{metadata.ProxyType.Name}' under Model container (module '{_moduleType}').");
                    }
#endif
                }
                else if (typeof(mvcExpress.Proxy).IsAssignableFrom(metadata.ProxyType))
                {
                    // Code-only Proxy: Create instance
                    try
                    {
                        proxyInstance = Activator.CreateInstance(metadata.ProxyType);
                        
                        if (proxyInstance == null)
                        {
                            MvcDebug.LogError(
                                $"Failed to create instance of proxy '{metadata.ProxyType.FullName}' " +
                                $"in module '{_moduleType}'. Skipping registration.");
                            continue;
                        }
                    }
                    catch (Exception ex)
                    {
                        MvcDebug.LogError(
                            $"Failed to create instance of proxy '{metadata.ProxyType.FullName}' " +
                            $"in module '{_moduleType}': {ex.Message}");
                        continue;
                    }
                }
                else
                {
                    MvcDebug.LogError(
                        $"Type '{metadata.ProxyType.FullName}' has [Register] attribute but is not a Proxy or ProxyBehaviour. " +
                        $"Skipping registration for module '{_moduleType}'.");
                    continue;
                }

                try
                {
                    if (proxyInstance is ProxyBehaviour proxyBehaviour)
                    {
                        // Use ProxyRegistrationHelper for ProxyBehaviour types
                        ProxyRegistrationHelper.RegisterProxyWithScopes(
                            _diContainer,
                            proxyBehaviour,
                            metadata.LogicType,
                            metadata.ViewType,
                            metadata.RegisterToLogic,
                            metadata.RegisterToView,
                            metadata.Lifecycle == RegistrationLifecycle.Transient);

                        // Track for CompleteProxyInitialization (no-op if already tracked, e.g. preTracked instances).
                        _proxyRegistrar.TrackProxyBehaviour(proxyBehaviour);

                        // Track for source detection
                        _attributeRegisteredProxies.Add(proxyBehaviour);
                    }
                    else if (proxyInstance is mvcExpress.Proxy codeProxy)
                    {
                        // Use non-generic fluent API for code-only proxies
                        var builder = _diContainer.Register(proxyInstance, metadata.ProxyType);

                        if (metadata.RegisterToLogic)
                        {
                            if (metadata.LogicType == metadata.ProxyType)
                                builder.ToLogic();
                            else
                                builder.ToLogicAs(metadata.LogicType);
                        }

                        if (metadata.RegisterToView)
                        {
                            if (metadata.ViewType == metadata.ProxyType)
                                builder.ToView();
                            else
                                builder.ToViewAs(metadata.ViewType);
                        }

                        if (metadata.Lifecycle == RegistrationLifecycle.Scoped)
                        {
                            builder.AsScoped();
                            // codeProxy is a throwaway instance discarded by AsScoped() - the container
                            // builds its own instance per resolution scope. Do NOT track/initialize it.
                        }
                        else
                        {
                            if (metadata.Lifecycle == RegistrationLifecycle.Transient)
                                builder.AsTransient();
                            else
                                builder.AsPermanent();

                            // Track code-only proxy for initialization
                            _proxyRegistrar.TrackCodeProxy(codeProxy);

                            // Track for source detection
                            _attributeRegisteredProxies.Add(codeProxy);
                        }
                    }

#if UNITY_EDITOR || MVC_LOGGING
                    // LOG: Proxy registered via attribute
                    mvcExpress.Logging.MvcLogInternal.LogProxyRegistered(
                        metadata.ProxyType.Name,
                        _moduleContext,
                        mvcExpress.Logging.MvcLogContext.RegistrationSource.Attribute,
                        proxyInstance is UnityEngine.Object uo ? (uo as Component)?.gameObject : null,
                        null, 0);
                    MvcPluginBus.FireProxyRegistered(metadata.ProxyType, _moduleType, mvcExpress.Logging.MvcLogContext.RegistrationSource.Attribute);
#endif
                }
                catch (Exception ex)
                {
                    MvcDebug.LogError(
                        $"Failed to register proxy '{metadata.ProxyType.FullName}' via attribute in module '{_moduleType}': {ex.Message}");
                }
            }
#endif
        }

        /// <summary>
        /// Register services marked with [Register] attribute.
        /// Uses cached metadata from AttributeScanner for performance.
        /// </summary>
        private void RegisterAttributeServices()
        {
#if MVC_EXPRESS_NO_ATTRIBUTE
            return;
#else
            // Get cached service metadata for this module
            var serviceMetadata = AttributeScanner.GetServiceMetadata(_moduleType);

            if (serviceMetadata.Count == 0)
                return;

            MvcCompositionStyleWarning.WarnIfDisabled(
                MvcCompositionStyle.Attribute,
                $"[Register] service metadata for module '{_moduleType.Name}'");

            foreach (var metadata in serviceMetadata)
            {
                try
                {
                    object serviceInstance = null;

                    if (typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(metadata.ServiceType))
                    {
                        // Prefer an instance already tracked via Unity registry (Inspector-wired).
                        UnityEngine.MonoBehaviour preTracked = null;
                        var mappings = _moduleContext.GetServiceMappings();
                        for (int i = 0; i < mappings.Length; i++)
                        {
                            var svc = mappings[i]?.Service;
                            if (svc != null && svc.GetType() == metadata.ServiceType)
                            {
                                preTracked = svc;
                                break;
                            }
                        }

                        // Not tracked: find a hand-placed instance anywhere in the module hierarchy,
                        // or create one under the Services container if none exists.
                        var resolution = AttributeMonoBehaviourResolver.Resolve(
                            _moduleContext.transform,
                            metadata.ServiceType,
                            _moduleContext.ServicesContainer,
                            preTracked);

                        if (resolution.Kind == MonoBehaviourResolutionKind.Ambiguous)
                        {
                            MvcDebug.LogError(
                                $"Service '{metadata.ServiceType.FullName}' has [Register] attribute but " +
                                $"{resolution.Conflicts.Length} instances exist in module '{_moduleType}''s hierarchy: " +
                                $"{string.Join(", ", Array.ConvertAll(resolution.Conflicts, go => go.name))}. " +
                                $"Remove the duplicate(s) so the correct instance is unambiguous. Skipping registration.");
                            continue;
                        }

#if UNITY_EDITOR || MVC_LOGGING
                        if (resolution.Kind == MonoBehaviourResolutionKind.Created)
                        {
                            MvcDebug.Log(
                                $"[Register] Auto-created '{metadata.ServiceType.Name}' under Services container (module '{_moduleType}').");
                        }
#endif

                        serviceInstance = resolution.Instance;
                    }
                    else
                    {
                        // Create code-only service instance
                        serviceInstance = Activator.CreateInstance(metadata.ServiceType);
                    }

                    if (serviceInstance == null)
                    {
                        MvcDebug.LogError(
                            $"Failed to create instance of service '{metadata.ServiceType.FullName}' " +
                            $"in module '{_moduleType}'. Skipping registration.");
                        continue;
                    }

                    // Register using fluent API
                    var builder = _diContainer.Register(serviceInstance, metadata.ServiceType);

                    if (metadata.RegisterToLogic)
                    {
                        if (metadata.LogicType == metadata.ServiceType)
                            builder.ToLogic();
                        else
                            builder.ToLogicAs(metadata.LogicType);
                    }

                    if (metadata.RegisterToView)
                    {
                        if (metadata.ViewType == metadata.ServiceType)
                            builder.ToView();
                        else
                            builder.ToViewAs(metadata.ViewType);
                    }

                    if (metadata.Lifecycle == RegistrationLifecycle.Scoped)
                    {
                        builder.AsScoped();
                        // serviceInstance is a throwaway instance discarded by AsScoped() - the container
                        // builds its own instance per resolution scope. Do NOT track it for initialization.
                    }
                    else
                    {
                        if (metadata.Lifecycle == RegistrationLifecycle.Transient)
                            builder.AsTransient();
                        else
                            builder.AsPermanent();

                        // Track this service for separate initialization (step 5)
                        _attributeRegisteredServices.Add(serviceInstance);
                    }

#if UNITY_EDITOR || MVC_LOGGING
                    // LOG: Service registered via attribute
                    mvcExpress.Logging.MvcLogInternal.LogServiceRegistered(
                        metadata.ServiceType.Name,
                        _moduleContext,
                        mvcExpress.Logging.MvcLogContext.RegistrationSource.Attribute,
                        null,
                        null, 0);
                    MvcPluginBus.FireServiceRegistered(metadata.ServiceType, _moduleType, mvcExpress.Logging.MvcLogContext.RegistrationSource.Attribute);
#endif
                }
                catch (Exception ex)
                {
                    MvcDebug.LogError(
                        $"Failed to register service '{metadata.ServiceType.FullName}' via attribute in module '{_moduleType}': {ex.Message}");
                }
            }
#endif
        }

        /// <summary>
        /// Bind commands marked with [Bind] attribute.
        /// Uses cached metadata from AttributeScanner for performance.
        /// </summary>
        private void BindAttributeCommands()
        {
#if MVC_EXPRESS_NO_ATTRIBUTE
            return;
#else
            // Get cached command metadata for this module
            var commandMetadata = AttributeScanner.GetCommandMetadata(_moduleType);

            if (commandMetadata.Count == 0)
                return;

            MvcCompositionStyleWarning.WarnIfDisabled(
                MvcCompositionStyle.Attribute,
                $"[Bind] command metadata for module '{_moduleType.Name}'");

            foreach (var metadata in commandMetadata)
            {
                try
                {
                    // Use the existing BindFromRegistry method with metadata values
                    BindFromRegistry(metadata.CommandType, metadata.MessageType, metadata.IsAsync, metadata.PoolSize);

#if UNITY_EDITOR || MVC_LOGGING
                    // LOG: Command bound via attribute
                    mvcExpress.Logging.MvcLogInternal.LogCommandBound(
                        metadata.MessageType.Name,
                        metadata.CommandType.Name,
                        _moduleContext,
                        mvcExpress.Logging.MvcLogContext.RegistrationSource.Attribute,
                        null,
                        null, 0);
                    MvcPluginBus.FireCommandBound(metadata.CommandType, metadata.MessageType, _moduleType, mvcExpress.Logging.MvcLogContext.RegistrationSource.Attribute);
#endif
                }
                catch (Exception ex)
                {
                    MvcDebug.LogError(
                        $"Failed to bind command '{metadata.CommandType.FullName}' to message '{metadata.MessageType.FullName}' via attribute in module '{_moduleType}': {ex.Message}");
                }
            }
#endif
        }

        /// <summary>
        /// Phase 3: Initialize commands (map messages to command handlers).
        /// This phase sets up the controller layer.
        /// </summary>
        private void InitializeCommands()
        {
            ValidatePhase(InitializationPhase.Commands);

            // 1) Unity: Bind commands configured in the inspector
            RegisterSerializedCommandBindings();

            // 2) Attribute: Bind commands marked with [Bind] attribute
            BindAttributeCommands();

            // 3) Code: Allow user to bind/map commands with code
            _moduleContext.OnBindCommands();

        }

        /// <summary>
        /// Phase 4: Initialize all mediators (serialized, manual, and user-registered).
        /// This phase populates the view layer.
        /// </summary>
        private void InitializeMediators()
        {
            ValidatePhase(InitializationPhase.Mediators);

            _mediatorRegistrar.ClearTrackingLists();

            // 1) Unity: Register mediators from the inspector
#if MVC_EXPRESS_NO_UNITY
            // Unity style disabled via Project Settings > mvcExpress > Composition.
#else
            var sceneMediators = _moduleContext.GetSerializedMediators();
            if (sceneMediators != null && sceneMediators.Length > 0)
            {
                MvcCompositionStyleWarning.WarnIfDisabled(
                    MvcCompositionStyle.Unity,
                    $"Mediator Registry on module '{_moduleType.Name}'");
            }
            _mediatorRegistrar.RegisterSerializedMediators();
#endif

            // 2) Attribute: Attach mediators marked with [Attach] attribute
            AttachAttributeMediators();

            // 3) Code: Allow user to attach mediators with code
            _moduleContext.OnInitMediators();

            _mediatorRegistrar.RegisterManualMediators();
            _mediatorRegistrar.LockManualRegistration();

            // 4) Initialize: Complete mediator initialization
            _mediatorRegistrar.CompleteMediatorInitialization();

        }

        /// <summary>
        /// Attach mediators marked with [Attach] attribute.
        /// Uses cached metadata from AttributeScanner for performance.
        /// </summary>
        private void AttachAttributeMediators()
        {
#if MVC_EXPRESS_NO_ATTRIBUTE
            return;
#else
            // Get cached mediator metadata for this module
            var mediatorMetadata = AttributeScanner.GetMediatorMetadata(_moduleType);

            if (mediatorMetadata.Count == 0)
                return;

            MvcCompositionStyleWarning.WarnIfDisabled(
                MvcCompositionStyle.Attribute,
                $"[Attach] mediator metadata for module '{_moduleType.Name}'");

            // Prefab-backed mediators can materialize containers that hold scene-discovered child mediators.
            // Attach those roots first so [Attach(... FindInScene = true)] has a deterministic object graph to find.
            for (int i = 0; i < mediatorMetadata.Count; i++)
            {
                var metadata = mediatorMetadata[i];
                if (metadata.IsPrefabBased)
                    AttachAttributeMediator(metadata);
            }

            for (int i = 0; i < mediatorMetadata.Count; i++)
            {
                var metadata = mediatorMetadata[i];
                if (!metadata.IsPrefabBased)
                    AttachAttributeMediator(metadata);
            }
#endif
        }

        private void AttachAttributeMediator(MediatorAttachmentMetadata metadata)
        {
            try
            {
                MediatorBehaviour mediatorInstance = null;

                // Strategy 1: Instantiate from ViewPrefabCatalog/module mediator registry.
                if (metadata.UsePrefabCatalog)
                {
                    if (!_moduleContext.TryGetMediatorPrefab(metadata.MediatorType, out var prefab) || prefab == null)
                    {
                        MvcDebug.LogError(
                            $"View prefab not found for mediator '{metadata.MediatorType.FullName}' in module '{_moduleType}'. Add it to a ViewPrefabCatalog or module MediatorRegistry.");
                        return;
                    }

                    var instance = UnityEngine.Object.Instantiate(prefab);
                    instance.name = $"{prefab.name} (Mediator)";
                    instance.transform.SetParent(_moduleContext.ModuleViewContainer, false);

                    mediatorInstance = instance.GetComponent(metadata.MediatorType) as MediatorBehaviour;
                    if (mediatorInstance == null)
                    {
                        MvcDebug.LogError(
                            $"View prefab '{prefab.name}' does not contain mediator component '{metadata.MediatorType.FullName}' in module '{_moduleType}'.");
                        UnityEngine.Object.Destroy(instance);
                        return;
                    }
                }
                // Strategy 2: Instantiate from Resources path.
                else if (metadata.IsPrefabBased)
                {
                    var prefab = UnityEngine.Resources.Load<GameObject>(metadata.PrefabPath);
                    if (prefab == null)
                    {
                        MvcDebug.LogError(
                            $"Prefab not found at path '{metadata.PrefabPath}' for mediator '{metadata.MediatorType.FullName}' in module '{_moduleType}'.");
                        return;
                    }

                    var instance = UnityEngine.Object.Instantiate(prefab);
                    mediatorInstance = instance.GetComponent(metadata.MediatorType) as MediatorBehaviour;

                    if (mediatorInstance == null)
                    {
                        MvcDebug.LogError(
                            $"Prefab at '{metadata.PrefabPath}' does not contain component '{metadata.MediatorType.FullName}' in module '{_moduleType}'.");
                        UnityEngine.Object.Destroy(instance);
                        return;
                    }
                }
                // Strategy 3: Find existing instance in scene
                else if (metadata.FindInScene)
                {
#if UNITY_2023_1_OR_NEWER
                    mediatorInstance = UnityEngine.Object.FindAnyObjectByType(metadata.MediatorType) as MediatorBehaviour;
#else
                    mediatorInstance = UnityEngine.Object.FindObjectOfType(metadata.MediatorType) as MediatorBehaviour;
#endif

                    if (mediatorInstance == null)
                    {
                        MvcDebug.LogWarning(
                            $"Mediator '{metadata.MediatorType.FullName}' not found in scene for module '{_moduleType}'.");
                        return;
                    }
                }
                else
                {
                    MvcDebug.LogWarning(
                        $"Mediator '{metadata.MediatorType.FullName}' has [Attach] attribute but no PrefabPath or FindInScene flag in module '{_moduleType}'.");
                    return;
                }

                // Attach through the module's internal mediator host path.
                if (mediatorInstance != null)
                {
                    _moduleContext.AttachMediator(mediatorInstance);

#if UNITY_EDITOR || MVC_LOGGING
                    // LOG: Mediator attached via attribute
                    mvcExpress.Logging.MvcLogInternal.LogMediatorAttached(
                        metadata.MediatorType.Name,
                        mediatorInstance.gameObject.name,
                        _moduleContext,
                        mvcExpress.Logging.MvcLogContext.RegistrationSource.Attribute,
                        mediatorInstance.gameObject,
                        null, 0,
                        metadata.IsPrefabBased);
                    MvcPluginBus.FireMediatorAttached(metadata.MediatorType, _moduleType, mvcExpress.Logging.MvcLogContext.RegistrationSource.Attribute);
#endif
                }
            }
            catch (Exception ex)
            {
                MvcDebug.LogError(
                    $"Failed to attach mediator '{metadata.MediatorType.FullName}' via attribute in module '{_moduleType}': {ex.Message}");
            }
        }

        /// <summary>
        /// Finalize initialization by calling user-defined hooks.
        /// </summary>
        private void FinalizeInitialization()
        {
            ValidatePhase(InitializationPhase.Initialized);
            _moduleContext.OnModuleInitialized();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.FireModuleInitialized(_moduleType);
#endif
        }

        /// <summary>
        /// Validates that the current phase matches the expected phase.
        /// Used as a safety check to ensure methods are called in the correct order.
        /// </summary>
        private void ValidatePhase(InitializationPhase expectedPhase)
        {
            if (_currentPhase != expectedPhase)
            {
                throw new InvalidOperationException(
                    $"[ModuleInitializer] Phase mismatch: expected {expectedPhase}, but current phase is {_currentPhase}.");
            }
        }

        /// <summary>
        /// Get the proxy registrar for external access.
        /// </summary>
        internal ProxyRegistrar ProxyRegistrar => _proxyRegistrar;

        /// <summary>
        /// Get the mediator registrar for external access.
        /// </summary>
        internal MediatorRegistrar MediatorRegistrar => _mediatorRegistrar;

        /// <summary>
        /// Calls <see cref="IMvcLifecycle.OnCleanup"/> on every service that opted in to the
        /// lifecycle, in reverse registration order (last registered, first cleaned up).
        /// Must be called before the DI container is disposed.
        /// </summary>
        internal void CleanupServices()
        {
            for (int i = _lifecycleServices.Count - 1; i >= 0; i--)
            {
                try
                {
                    _lifecycleServices[i].OnCleanup();
                }
                catch (Exception ex)
                {
                    MvcDebug.LogError(
                        $"[ModuleInitializer] Service '{_lifecycleServices[i].GetType().FullName}' " +
                        $"threw during OnCleanup in module '{_moduleType}': {ex.Message}");
                }
            }
            _lifecycleServices.Clear();
        }

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only: Get the list of attribute-registered services for source tracking.
        /// </summary>
        internal List<object> GetAttributeServices() => _attributeRegisteredServices;

        /// <summary>
        /// Editor-only: Get the list of attribute-registered proxies for source tracking.
        /// </summary>
        internal List<object> GetAttributeProxies() => _attributeRegisteredProxies;
#endif

        // Registers Unity-inspector-configured services (step 1 of InitializeServices).
        // Validates each mapping, resolves the registered type names, then calls
        // ServiceRegistrationHelper to add them to the DI container.
        private void RegisterSerializedServiceBehaviours()
        {
#if MVC_EXPRESS_NO_UNITY
            return;
#else
            var mappings = _moduleContext.GetServiceMappings();
            if (mappings == null || mappings.Length == 0)
                return;

            MvcCompositionStyleWarning.WarnIfDisabled(
                MvcCompositionStyle.Unity,
                $"Service Registry on module '{_moduleType.Name}'");

            for (int i = 0; i < mappings.Length; i++)
            {
                var mapping = mappings[i];
                if (mapping == null || !mapping.IsValid())
                {
                    MvcDebug.LogWarning(
                        $"Invalid service mapping at index {i} in module '{_moduleType.Name}'.\n" +
                        $"This mapping will be skipped.");
                    continue;
                }

                var svc = mapping.Service;
                if (svc == null)
                {
                    MvcDebug.LogWarning(
                        $"Service mapping at index {i} has null Service reference in module '{_moduleType.Name}'.\n" +
                        $"This mapping will be skipped.");
                    continue;
                }

                Type logicType = null;
                Type viewType = null;

                try
                {
                    logicType = mapping.RegisterToLogic ? mapping.ResolveLogicType(_moduleType) : null;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"[ModuleInitializer] Failed to resolve LOGIC type for service mapping at index {i}.\n" +
                        $"Module: {_moduleType.FullName}\n" +
                        $"Service: {svc.GetType().FullName} on GameObject '{svc.gameObject.name}'\n" +
                        $"Logic Type Name: {mapping.LogicTypeName}\n" +
                        $"Error: {ex.Message}", ex);
                }

                try
                {
                    viewType = mapping.RegisterToView ? mapping.ResolveViewType(_moduleType) : null;
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"[ModuleInitializer] Failed to resolve VIEW type for service mapping at index {i}.\n" +
                        $"Module: {_moduleType.FullName}\n" +
                        $"Service: {svc.GetType().FullName} on GameObject '{svc.gameObject.name}'\n" +
                        $"View Type Name: {mapping.ViewTypeName}\n" +
                        $"Error: {ex.Message}", ex);
                }

                if (mapping.RegisterToLogic && logicType == null)
                {
                    MvcDebug.LogError(
                        $"Service mapping at index {i} is configured to RegisterToLogic=true but logic type could not be resolved.\n" +
                        $"Module: {_moduleType.Name}\n" +
                        $"Service: {svc.GetType().FullName} on GameObject '{svc.gameObject.name}'\n" +
                        $"Logic Type Name: {mapping.LogicTypeName}\n" +
                        $"This service will NOT be registered to the logic layer.\n" +
                        $"FIX: Check the error messages above for type resolution failures.");
                    continue;
                }

                if (mapping.RegisterToView && viewType == null)
                {
                    MvcDebug.LogError(
                        $"Service mapping at index {i} is configured to RegisterToView=true but view type could not be resolved.\n" +
                        $"Module: {_moduleType.Name}\n" +
                        $"Service: {svc.GetType().FullName} on GameObject '{svc.gameObject.name}'\n" +
                        $"View Type Name: {mapping.ViewTypeName}\n" +
                        $"This service will NOT be registered to the view layer.\n" +
                        $"FIX: Check the error messages above for type resolution failures.");
                    continue;
                }

                try
                {
                    ServiceRegistrationHelper.RegisterServiceWithScopes(
                        _diContainer,
                        svc,
                        logicType,
                        viewType,
                        mapping.RegisterToLogic,
                        mapping.RegisterToView,
                        mapping.IsTransient);

#if UNITY_EDITOR || MVC_LOGGING
                    // LOG: Service registered from Unity registry
                    mvcExpress.Logging.MvcLogInternal.LogServiceRegistered(
                        svc.GetType().Name,
                        _moduleContext,
                        mvcExpress.Logging.MvcLogContext.RegistrationSource.Unity,
                        svc.gameObject,
                        null, 0);
                    MvcPluginBus.FireServiceRegistered(svc.GetType(), _moduleType, mvcExpress.Logging.MvcLogContext.RegistrationSource.Unity);
#endif
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        $"[ModuleInitializer] Failed to register service at index {i}.\n" +
                        $"Module: {_moduleType.FullName}\n" +
                        $"Service: {svc.GetType().FullName} on GameObject '{svc.gameObject.name}'\n" +
                        $"Logic Type: {logicType?.FullName ?? "<none>"}\n" +
                        $"View Type: {viewType?.FullName ?? "<none>"}\n" +
                        $"RegisterToLogic: {mapping.RegisterToLogic}\n" +
                        $"RegisterToView: {mapping.RegisterToView}\n" +
                        $"Error: {ex.Message}", ex);
                }
            }
#endif
        }

        // Registers command→message bindings configured in the Unity Inspector registry.
        // Type names are stored as assembly-qualified strings and resolved at runtime.
        // Async vs sync is inferred from the command's type hierarchy, not stored explicitly.
        private void RegisterSerializedCommandBindings()
        {
#if MVC_EXPRESS_NO_UNITY
            return;
#else
            var bindings = _moduleContext.GetCommandBindings();
            if (bindings == null || bindings.Length == 0)
                return;

            MvcCompositionStyleWarning.WarnIfDisabled(
                MvcCompositionStyle.Unity,
                $"Command Bindings Registry on module '{_moduleType.Name}'");

            for (int i = 0; i < bindings.Length; i++)
            {
                var b = bindings[i];
                if (b == null) continue;

                if (string.IsNullOrWhiteSpace(b.CommandTypeName) || string.IsNullOrWhiteSpace(b.MessageTypeName))
                {
                    MvcDebug.LogWarning($"Invalid command binding (missing type names) at index {i} in module '{_moduleType}'.");
                    continue;
                }

                var cmdType = Type.GetType(b.CommandTypeName, throwOnError: false);
                var msgType = Type.GetType(b.MessageTypeName, throwOnError: false);

                if (cmdType == null)
                {
                    MvcDebug.LogWarning($"Unknown command type '{b.CommandTypeName}' in module '{_moduleType}'.");
                    continue;
                }

                if (msgType == null)
                {
                    MvcDebug.LogWarning($"Unknown message type '{b.MessageTypeName}' in module '{_moduleType}'.");
                    continue;
                }

                try
                {
                    // Async is now inferred from command type. MvcAsyncCommandBase (not
                    // CommandAsync) is the common base for every async arity - see L2.
                    var isAsync = typeof(MvcAsyncCommandBase).IsAssignableFrom(cmdType);

                    BindFromRegistry(cmdType, msgType, isAsync, (uint)Math.Max(0, b.PoolSize));

#if UNITY_EDITOR || MVC_LOGGING
                    // LOG: Command bound from Unity registry
                    mvcExpress.Logging.MvcLogInternal.LogCommandBound(
                        msgType.Name,
                        cmdType.Name,
                        _moduleContext,
                        mvcExpress.Logging.MvcLogContext.RegistrationSource.Unity,
                        _moduleContext.ControllerContainer != null ? _moduleContext.ControllerContainer.gameObject : null,
                        null, 0);
                    MvcPluginBus.FireCommandBound(cmdType, msgType, _moduleType, mvcExpress.Logging.MvcLogContext.RegistrationSource.Unity);
#endif
                }
                catch (Exception ex)
                {
                    MvcDebug.LogError(
                        $"Failed to bind command '{cmdType.FullName}' to message '{msgType.FullName}' at index {i} in module '{_moduleType}': {ex.Message}");
                }
            }
#endif
        }

        /// <summary>
        /// Delegates a type-based bind to <see cref="MvcCommandProcessor.BindCommandByType"/>.
        /// Temporarily suppresses the per-binding log on the module so the higher-level
        /// registration log (Unity/Attribute/Code source) is emitted instead, avoiding
        /// a duplicate log entry for the same bind event.
        /// </summary>
        private void BindFromRegistry(Type commandType, Type messageType, bool isAsync, uint poolSize)
        {
            if (commandType == null) throw new ArgumentNullException(nameof(commandType));
            if (messageType == null) throw new ArgumentNullException(nameof(messageType));

            // Use the type-based binding API which auto-detects sync/async/singleton
            _moduleContext.SuppressCommandBindingLog = true;
            try
            {
                _commandProcessor.BindCommandByType(commandType, messageType, poolSize);
            }
            finally
            {
                _moduleContext.SuppressCommandBindingLog = false;
            }
        }
    }
}

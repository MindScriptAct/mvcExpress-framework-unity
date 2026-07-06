using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;
using mvcExpress.Logging;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace mvcExpress.Internal.Initialization
{
    /// <summary>
    /// Manages the mediator registration sub-phase of <see cref="ModuleInitializer"/>'s Phase 5 (Mediators).
    /// Also handles runtime mediator attachment/detachment after initialization is complete.
    /// </summary>
    /// <remarks>
    /// Two separate lists track mediators with different lifecycles:
    /// <list type="bullet">
    ///   <item><description>
    ///     <c>_registeredMediators</c> - scene + manual mediators registered during module init.
    ///     These are expected to live as long as the module.
    ///   </description></item>
    ///   <item><description>
    ///     <c>_runtimeMediators</c> - mediators attached after init (e.g. from Commands via
    ///     <c>AttachMediator</c>). These can be detached individually without restarting the module.
    ///   </description></item>
    /// </list>
    ///
    /// Manual registration lock: during the <c>OnInitMediators()</c> callback, the module can queue
    /// mediators via <c>AddMediator()</c>. <see cref="LockManualRegistration"/> is called immediately
    /// after to prevent late callers from enqueuing mediators that would never be initialized.
    ///
    /// Initialization is deferred: all mediators are registered first (so DI is available),
    /// then <see cref="CompleteMediatorInitialization"/> injects dependencies and calls
    /// <c>OnInitialized()</c>.
    ///
    /// Internal - not part of the public API.
    /// </remarks>
    internal sealed class MediatorRegistrar
    {
        private readonly Type _moduleType;
        private readonly MvcDiContainer _container;
        private readonly MvcMessageBus _messageBus;
        private readonly MvcModule _moduleContext;
        
        // Scene + manual mediators - registered once during module init. Not individually removable.
        private readonly List<MediatorBehaviour> _registeredMediators = new List<MediatorBehaviour>(4);
        // Runtime mediators - attached after module init; can be removed via RemoveRuntimeMediator.
        private readonly List<MediatorBehaviour> _runtimeMediators = new List<MediatorBehaviour>(4);

        // Becomes true after OnInitMediators() returns. Prevents any further manual registration
        // queuing that would never get initialized.
        private bool _manualRegistrationLocked;

        public IReadOnlyList<MediatorBehaviour> RegisteredMediators => _registeredMediators;
        public IReadOnlyList<MediatorBehaviour> RuntimeMediators => _runtimeMediators;
        public bool IsManualRegistrationLocked => _manualRegistrationLocked;

        public MediatorRegistrar(
            Type moduleType,
            MvcDiContainer container,
            MvcMessageBus messageBus,
            MvcModule module)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            if (messageBus == null) throw new ArgumentNullException(nameof(messageBus));
            if (module == null) throw new ArgumentNullException(nameof(module));
            
            _moduleType = moduleType;
            _container = container;
            _messageBus = messageBus;
            _moduleContext = module;
        }

        /// <summary>
        /// Clear all tracking lists. Called at the start of initialization.
        /// </summary>
        public void ClearTrackingLists()
        {
            _registeredMediators.Clear();
            _manualRegistrationLocked = false;
        }

        /// <summary>
        /// Register all serialized mediators configured in the inspector.
        /// </summary>
        public void RegisterSerializedMediators()
        {
            var mediators = _moduleContext.GetSerializedMediators();
            if (mediators == null || mediators.Length == 0)
            {
                return;
            }

            for (int i = 0; i < mediators.Length; i++)
            {
                var mediator = mediators[i];
                if (mediator == null) continue;

                mediator.Initialize(_moduleContext, _container, _messageBus, deferOnInitialized: true);
                _registeredMediators.Add(mediator);

#if UNITY_EDITOR || MVC_LOGGING
                // LOG: Mediator attached from Unity registry
                mvcExpress.Logging.MvcLogInternal.LogMediatorAttached(
                    mediator.GetType().Name,
                    mediator.gameObject.name,
                    _moduleContext,
                    mvcExpress.Logging.MvcLogContext.RegistrationSource.Unity,
                    mediator.gameObject,
                    null, 0);
#endif
            }
        }

        /// <summary>
        /// Register all mediators that were added via AddMediator during InitMediators.
        /// </summary>
        public void RegisterManualMediators()
        {
            var manualMediators = _moduleContext.GetManualMediators();
            if (manualMediators.Count == 0)
            {
                return;
            }

            for (int i = 0; i < manualMediators.Count; i++)
            {
                var mediator = manualMediators[i];
                if (mediator == null) continue;

                mediator.Initialize(_moduleContext, _container, _messageBus, deferOnInitialized: true);
                _registeredMediators.Add(mediator);
            }

            manualMediators.Clear(); // Prevent double-registration on later phases
        }

        /// <summary>
        /// Lock manual registration to prevent AddMediator from being called after initialization.
        /// </summary>
        public void LockManualRegistration()
        {
            _manualRegistrationLocked = true;
        }

        /// <summary>
        /// Complete initialization of all registered mediators.
        /// This injects dependencies and calls OnInitialized hooks.
        /// </summary>
        public void CompleteMediatorInitialization()
        {
            for (int i = 0; i < _registeredMediators.Count; i++)
            {
                var mediator = _registeredMediators[i];
                if (mediator == null) continue;

                try
                {
                    mediator.CompleteInitialization();
                }
                catch (Exception ex)
                {
                    // Provide clear context about which mediator failed
                    throw new InvalidOperationException(
                        $"[MediatorRegistrar] Mediator '{mediator.GetType().FullName}' (GameObject: '{mediator.gameObject.name}') " +
                        $"failed during initialization in module: {_moduleType.Name}.\n" +
                        $"Original error: {ex.Message}", ex);
                }
            }
        }

        /// <summary>
        /// Add a mediator at runtime (after module initialization).
        /// </summary>
        public bool AddRuntimeMediator(MediatorBehaviour mediator)
        {
            if (mediator == null)
            {
                MvcDebug.LogWarning("Cannot add null mediator at runtime.");
                return false;
            }

            if (_runtimeMediators.Contains(mediator))
            {
                MvcDebug.LogWarning($"Mediator '{mediator.name}' is already registered in module: {_moduleType.Name}.");
                return false;
            }

            mediator.Initialize(_moduleContext, _container, _messageBus, deferOnInitialized: false);
            _runtimeMediators.Add(mediator);

#if UNITY_EDITOR || MVC_LOGGING
            // LOG: Runtime mediator attachment (Controller action)
            // Gate the StackTrace(true) capture itself, not just the log call - ShouldLogFor
            // mirrors the same check LogMediatorAttached does internally, so when logging is
            // disabled this expensive capture is skipped entirely instead of running for nothing - see L5.
            if (mvcExpress.Logging.MvcLogInternal.ShouldLogFor(_moduleContext))
            {
                var caller = new System.Diagnostics.StackTrace(true).GetFrame(2); // Skip AddRuntimeMediator and AttachMediator
                var callerMethod = caller?.GetMethod();
                bool isPrefab = callerMethod != null && callerMethod.Name == "AttachPrefabMediator";

                mvcExpress.Logging.MvcLogInternal.LogMediatorAttached(
                    mediator.GetType().Name,
                    mediator.gameObject.name,
                    _moduleContext,
                    mvcExpress.Logging.MvcLogContext.RegistrationSource.Code,
                    mediator.gameObject,
                    null, 0,
                    isPrefab);
            }
#endif

            return true;
        }

        /// <summary>
        /// Remove a runtime mediator.
        /// </summary>
        public bool RemoveRuntimeMediator(MediatorBehaviour mediator)
        {
            if (mediator == null)
            {
                MvcDebug.LogWarning("Cannot remove null mediator.");
                return false;
            }

            if (!_runtimeMediators.Remove(mediator))
            {
                MvcDebug.LogWarning($"Mediator '{mediator.name}' not found in runtime mediators of module: {_moduleType.Name}.");
                return false;
            }

            mediator.CleanupMediator();
            return true;
        }

        /// <summary>
        /// Cleanup all registered mediators during module destruction.
        /// </summary>
        public void Cleanup()
        {
            // Explicitly clean up runtime mediators. These may live outside the module's
            // GameObject hierarchy, so Unity's OnDestroy chain alone cannot be relied on.
            // CleanupMediator() is idempotent - safe to call even if Unity fires OnDestroy later.
            for (int i = _runtimeMediators.Count - 1; i >= 0; i--)
            {
                var mediator = _runtimeMediators[i];
                if (mediator != null)
                    mediator.CleanupMediator();
            }
            _runtimeMediators.Clear();

            // Scene and manual mediators are always children of the module's GameObject;
            // Unity's OnDestroy chain handles their cleanup automatically.
            _registeredMediators.Clear();
        }

        /// <summary>
        /// Adds a mediator to the registered list with deferred initialization.
        /// Used for mediators that are discovered dynamically during the init phase
        /// (e.g. child mediators found after a prefab-based parent mediator is instantiated).
        /// Idempotent - returns <c>true</c> if the mediator is already in the list.
        /// </summary>
        internal bool AddDeferredRegisteredMediator(MediatorBehaviour mediator)
        {
            if (mediator == null)
                return false;

            if (_registeredMediators.Contains(mediator))
                return true;

            mediator.Initialize(_moduleContext, _container, _messageBus, deferOnInitialized: true);
            _registeredMediators.Add(mediator);
            return true;
        }
    }
}

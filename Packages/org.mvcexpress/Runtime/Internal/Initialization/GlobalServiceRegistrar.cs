using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Services;
using mvcExpress.Logging;
using System;
using System.Collections.Generic;
using UnityEngine;
using mvcExpress;

namespace mvcExpress.Internal.Initialization
{
    /// <summary>
    /// Registers global service behaviours from MvcFacade inspector mappings into the global container.
    /// </summary>
    internal sealed class GlobalServiceRegistrar
    {
        private readonly MvcDiContainer _globalContainer;
        private readonly List<MonoBehaviour> _registeredServices = new List<MonoBehaviour>(8);

        public IReadOnlyList<MonoBehaviour> RegisteredServices => _registeredServices;

        public GlobalServiceRegistrar(MvcDiContainer globalContainer)
        {
            _globalContainer = globalContainer ?? throw new ArgumentNullException(nameof(globalContainer));
        }

        public void ClearTrackingLists()
        {
            _registeredServices.Clear();
        }

        public void RegisterSerializedGlobalServiceBehaviours(ServiceMapping[] mappings, Transform runtimeParent = null)
        {
            if (mappings == null || mappings.Length == 0)
                return;

            for (int i = 0; i < mappings.Length; i++)
            {
                var mapping = mappings[i];
                if (mapping == null || !mapping.IsValid())
                {
                    MvcDebug.LogWarning($"Invalid service mapping at index {i} in MvcExpress facade.");
                    continue;
                }

                var svc = mapping.Service;
                if (svc != null && runtimeParent != null && !svc.gameObject.scene.IsValid())
                {
                    var go = UnityEngine.Object.Instantiate(svc.gameObject, runtimeParent);
                    go.name = svc.gameObject.name;
                    svc = go.GetComponent(svc.GetType()) as MonoBehaviour;
                    if (svc == null)
                    {
                        MvcDebug.LogError($"Failed to instantiate global service prefab '{mapping.Service.name}' in MvcExpress facade.");
                        UnityEngine.Object.Destroy(go);
                        continue;
                    }
                }

                var logicType = mapping.RegisterToLogic ? mapping.ResolveLogicType() : null;
                var viewType = mapping.RegisterToView ? mapping.ResolveViewType() : null;

                if (mapping.RegisterToLogic && logicType == null)
                {
                    MvcDebug.LogError($"Failed to resolve logic type '{mapping.LogicTypeName}' for service '{svc.name}' in MvcExpress facade.");
                    continue;
                }

                if (mapping.RegisterToView && viewType == null)
                {
                    MvcDebug.LogError($"Failed to resolve view type '{mapping.ViewTypeName}' for service '{svc.name}' in MvcExpress facade.");
                    continue;
                }

                try
                {
                    ServiceRegistrationHelper.RegisterServiceWithScopes(
                        _globalContainer,
                        svc,
                        logicType,
                        viewType,
                        mapping.RegisterToLogic,
                        mapping.RegisterToView,
                        mapping.IsTransient);

                    _registeredServices.Add(svc);
                }
                catch (Exception ex)
                {
                    MvcDebug.LogError($"Failed to register global service '{svc.name}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Inject [Inject]/[InjectGlobal] members then call OnInitialized on all registered
        /// services that implement IMvcLifecycle. Invoke after all services have been
        /// registered so cross-service injection dependencies can be satisfied.
        /// </summary>
        public void CompleteServiceInitialization()
        {
            for (int i = 0; i < _registeredServices.Count; i++)
            {
                MvcInjectionUtility.InjectMembers(_registeredServices[i], _globalContainer, useViewScope: false);

                if (_registeredServices[i] is IMvcLifecycle initializable)
                    initializable.OnInitialized();
            }
        }

        public void Cleanup()
        {
            for (int i = _registeredServices.Count - 1; i >= 0; i--)
            {
                if (_registeredServices[i] is IMvcLifecycle lifecycle)
                    lifecycle.OnCleanup();
            }
            _registeredServices.Clear();
        }
    }
}

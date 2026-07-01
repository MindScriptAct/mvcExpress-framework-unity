using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;
using mvcExpress.Internal.Proxy;
using mvcExpress.Logging;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace mvcExpress.Internal.Initialization
{
    /// <summary>
    /// Manages registration of global <see cref="ProxyBehaviour"/> instances that live on
    /// <see cref="MvcFacade"/> and are accessible to every module in the application.
    /// </summary>
    /// <remarks>
    /// Global proxies differ from module-scoped proxies in two ways:
    /// <list type="bullet">
    ///   <item><description>They are registered into the global DI container, not a module container.</description></item>
    ///   <item><description>They survive across scene loads (DontDestroyOnLoad via MvcFacade).</description></item>
    /// </list>
    ///
    /// Initialization is deferred: all proxies are registered first, then
    /// <see cref="CompleteProxyInitialization"/> injects dependencies and calls <c>OnInitialized()</c>.
    /// Internal - not part of the public API.
    /// </remarks>
    internal sealed class GlobalProxyRegistrar
    {
        private readonly MvcDiContainer _globalContainer;
        private readonly MvcMessageBus _messageBus;
        private readonly List<ProxyBehaviour> _registeredProxyBehaviours = new List<ProxyBehaviour>(8);

        public IReadOnlyList<ProxyBehaviour> RegisteredProxyBehaviours => _registeredProxyBehaviours;

        public GlobalProxyRegistrar(MvcDiContainer globalContainer, MvcMessageBus messageBus)
        {
            _globalContainer = globalContainer ?? throw new ArgumentNullException(nameof(globalContainer));
            _messageBus = messageBus ?? throw new ArgumentNullException(nameof(messageBus));
        }

        /// <summary>Resets the tracking list. Called before re-registration during hot-reload scenarios.</summary>
        public void ClearTrackingLists()
        {
            _registeredProxyBehaviours.Clear();
        }

        /// <summary>
        /// Track a proxy for deferred initialization completion.
        /// Used by the GlobalProxyRegistry path which registers proxies externally.
        /// </summary>
        public void TrackForCompletion(ProxyBehaviour proxy)
        {
            if (proxy != null)
                _registeredProxyBehaviours.Add(proxy);
        }

        public void RegisterSerializedGlobalProxyMappings(ProxyMapping[] mappings, Transform runtimeParent = null)
        {
            if (mappings == null || mappings.Length == 0)
                return;

            for (int i = 0; i < mappings.Length; i++)
            {
                var mapping = mappings[i];
                if (mapping == null || !mapping.IsValid())
                {
                    MvcDebug.LogWarning($"Invalid proxy mapping at index {i} in MvcExpress facade.");
                    continue;
                }

                var proxy = mapping.Proxy;
                if (proxy != null && runtimeParent != null && !proxy.gameObject.scene.IsValid())
                {
                    var go = UnityEngine.Object.Instantiate(proxy.gameObject, runtimeParent);
                    go.name = proxy.gameObject.name;
                    proxy = go.GetComponent(proxy.GetType()) as ProxyBehaviour;
                    if (proxy == null)
                    {
                        MvcDebug.LogError($"Failed to instantiate global proxy prefab '{mapping.Proxy.name}' in MvcExpress facade.");
                        UnityEngine.Object.Destroy(go);
                        continue;
                    }
                }

                var logicType = mapping.RegisterToLogic ? mapping.ResolveLogicType() : null;
                var viewType = mapping.RegisterToView ? mapping.ResolveViewType() : null;

                if (mapping.RegisterToLogic && logicType == null)
                {
                    MvcDebug.LogError($"Failed to resolve logic type '{mapping.LogicTypeName}' for global proxy '{proxy.name}' in MvcExpress facade.");
                    continue;
                }

                if (mapping.RegisterToView && viewType == null)
                {
                    MvcDebug.LogError($"Failed to resolve view type '{mapping.ViewTypeName}' for global proxy '{proxy.name}' in MvcExpress facade.");
                    continue;
                }

                try
                {
                    ProxyRegistrationHelper.RegisterProxyWithScopes(
                        _globalContainer,
                        proxy,
                        logicType,
                        viewType,
                        mapping.RegisterToLogic,
                        mapping.RegisterToView,
                        mapping.IsTransient);

                    proxy.InitializeGlobal(_globalContainer, _messageBus, deferOnInitialized: true);
                    _registeredProxyBehaviours.Add(proxy);
                }
                catch (Exception ex)
                {
                    MvcDebug.LogError($"Failed to register global proxy '{proxy.name}': {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Complete initialization of all registered global proxies.
        /// This injects dependencies and calls OnInitialized hooks.
        /// </summary>
        public void CompleteProxyInitialization()
        {
            for (int i = 0; i < _registeredProxyBehaviours.Count; i++)
            {
                _registeredProxyBehaviours[i]?.CompleteInitialization();
            }
        }

        /// <summary>
        /// Cleanup all registered global proxies during facade destruction.
        /// </summary>
        public void Cleanup()
        {
            _registeredProxyBehaviours.Clear();
        }
    }
}

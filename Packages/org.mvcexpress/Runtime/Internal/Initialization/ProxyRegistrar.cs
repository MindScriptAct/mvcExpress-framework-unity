using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Interfaces;
using mvcExpress.Internal.Proxy;
using mvcExpress.Logging;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace mvcExpress.Internal.Initialization
{
    /// <summary>
    /// Manages the proxy registration sub-phase of <see cref="ModuleInitializer"/>'s Phase 3 (Proxies).
    /// </summary>
    /// <remarks>
    /// Why a separate class: keeping proxy lifecycle logic out of MvcModule and ModuleInitializer
    /// reduces class size and makes each concern independently reviewable.
    ///
    /// Two proxy flavours are handled:
    /// <list type="bullet">
    ///   <item><description>
    ///     <see cref="ProxyBehaviour"/> - MonoBehaviour components already in the scene or registry;
    ///     tracked in <c>_registeredProxyBehaviours</c>.
    ///   </description></item>
    ///   <item><description>
    ///     <see cref="Proxy"/> - plain C# objects created by attribute or code registration;
    ///     tracked in <c>_registeredCodeProxies</c>.
    ///   </description></item>
    /// </list>
    ///
    /// Initialization is intentionally deferred: all proxies are registered into the DI container first
    /// (so their types are resolvable), then <see cref="CompleteProxyInitialization"/> injects
    /// dependencies and calls <c>OnInitialized()</c> in registration order.
    /// This two-pass approach allows proxies to inject each other without ordering constraints.
    ///
    /// Internal - not part of the public API.
    /// </remarks>
    internal sealed class ProxyRegistrar
    {
        private readonly Type _moduleType;
        private readonly MvcModule _moduleContext;
        private readonly MvcDiContainer _container;
        private readonly IMessagePublisher _messageBus;

        // ProxyBehaviour instances (from scene/registry and from attribute registrations).
        // Cleared at the start of each module init and repopulated during the registration phase.
        private readonly List<ProxyBehaviour> _registeredProxyBehaviours = new List<ProxyBehaviour>(8);
        // Plain C# Proxy instances (from attribute or code registration).
        private readonly List<mvcExpress.Proxy> _registeredCodeProxies = new List<mvcExpress.Proxy>(8);

        /// <summary>All registered ProxyBehaviour instances for this module.</summary>
        public IReadOnlyList<ProxyBehaviour> RegisteredProxyBehaviours => _registeredProxyBehaviours;
        /// <summary>All registered plain C# Proxy instances for this module.</summary>
        public IReadOnlyList<mvcExpress.Proxy> RegisteredCodeProxies => _registeredCodeProxies;

        public ProxyRegistrar(
            Type moduleType,
            MvcDiContainer container,
            IMessagePublisher messageBus,
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
            _registeredProxyBehaviours.Clear();
            _registeredCodeProxies.Clear();
        }

        /// <summary>
        /// Get all tracked proxy instances (both ProxyBehaviours and code-only proxies).
        /// Used for attribute registration.
        /// </summary>
        public IReadOnlyList<ProxyBehaviour> GetTrackedProxyBehaviours()
        {
            return _registeredProxyBehaviours;
        }

        /// <summary>
        /// Register all serialized proxy behaviours configured in the inspector.
        /// </summary>
        public void RegisterSerializedProxyBehaviours()
        {
            var proxyMappings = _moduleContext.GetProxyMappings();
            if (proxyMappings == null || proxyMappings.Length == 0)
            {
                return;
            }

            for (int i = 0; i < proxyMappings.Length; i++)
            {
                var mapping = proxyMappings[i];
                if (mapping == null || !mapping.IsValid())
                {
                    MvcDebug.LogWarning($"Invalid proxy mapping at index {i} in module '{_moduleType.Name}'.");
                    continue;
                }

                var proxy = mapping.Proxy;
                var logicType = mapping.RegisterToLogic ? mapping.ResolveLogicType() : null;
                var viewType = mapping.RegisterToView ? mapping.ResolveViewType() : null;

                if (mapping.RegisterToLogic && logicType == null)
                {
                    MvcDebug.LogError($"Failed to resolve logic type '{mapping.LogicTypeName}' for proxy '{proxy.name}' in module '{_moduleType.Name}'.");
                    continue;
                }

                if (mapping.RegisterToView && viewType == null)
                {
                    MvcDebug.LogError($"Failed to resolve view type '{mapping.ViewTypeName}' for proxy '{proxy.name}' in module '{_moduleType.Name}'.");
                    continue;
                }

                ProxyRegistrationHelper.RegisterProxyWithScopes(
                    _container,
                    proxy,
                    logicType,
                    viewType,
                    mapping.RegisterToLogic,
                    mapping.RegisterToView,
                    mapping.IsTransient
                );

                proxy.Initialize(_moduleContext, _container, _messageBus, deferOnInitialized: true);
                _registeredProxyBehaviours.Add(proxy);

#if UNITY_EDITOR || MVC_LOGGING
                // Log proxy registration
                mvcExpress.Logging.MvcLogInternal.LogProxyRegistered(
                    proxy.GetType().Name,
                    _moduleContext,
                    mvcExpress.Logging.MvcLogContext.RegistrationSource.Unity,
                    proxy.gameObject);
                MvcPluginBus.FireProxyRegistered(proxy.GetType(), _moduleType, mvcExpress.Logging.MvcLogContext.RegistrationSource.Unity);
#endif
            }
        }

        /// <summary>
        /// Track code-only proxy instances registered via the fluent DI API.
        /// Proxy initialization is deferred until CompleteProxyInitialization so all dependencies can be registered first.
        /// </summary>
        public void TrackCodeProxy(mvcExpress.Proxy proxy)
        {
            if (proxy == null) return;
            if (_registeredCodeProxies.Contains(proxy)) return;

            _registeredCodeProxies.Add(proxy);
        }

        /// <summary>
        /// Track runtime-registered ProxyBehaviour instances registered via the fluent DI API.
        /// Initialization is deferred until CompleteProxyInitialization so all dependencies can be registered first.
        /// </summary>
        public void TrackProxyBehaviour(ProxyBehaviour proxy)
        {
            if (proxy == null) return;
            if (_registeredProxyBehaviours.Contains(proxy)) return;

            _registeredProxyBehaviours.Add(proxy);
        }

        /// <summary>
        /// Complete initialization of all registered proxies.
        /// This injects dependencies and calls OnInitialized hooks.
        /// </summary>
        public void CompleteProxyInitialization()
        {
            for (int i = 0; i < _registeredProxyBehaviours.Count; i++)
            {
                var pb = _registeredProxyBehaviours[i];
                if (pb == null) continue;

                // Serialized proxies are initialized during RegisterProxyBehaviours; runtime-registered ones are not.
                // Initialize them here (idempotent due to ProxyBehaviour guards).
                pb.Initialize(_moduleContext, _container, _messageBus, deferOnInitialized: true);
                pb.CompleteInitialization();
            }

            for (int i = 0; i < _registeredCodeProxies.Count; i++)
            {
                var proxy = _registeredCodeProxies[i];
                if (proxy == null) continue;

                proxy.Initialize(_moduleType, _messageBus, _container, deferOnInitialized: true);
                proxy.CompleteInitialization();
            }
        }

        /// <summary>
        /// Dispose code-only proxies and clear all tracking lists during module destruction.
        /// ProxyBehaviour instances are destroyed by Unity's scene teardown; only plain C# proxies
        /// need explicit disposal here.
        /// </summary>
        public void Cleanup()
        {
            // Dispose code-only proxies if needed
            for (int i = _registeredCodeProxies.Count - 1; i >= 0; i--)
            {
                var proxy = _registeredCodeProxies[i];
                if (proxy is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception ex)
                    {
                        MvcDebug.LogError($"Error disposing proxy: {ex.Message}");
                    }
                }
            }
            _registeredCodeProxies.Clear();

            _registeredProxyBehaviours.Clear();
        }
    }
}

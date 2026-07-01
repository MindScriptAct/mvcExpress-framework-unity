using mvcExpress.Internal.Proxy;
using System;
using UnityEngine;

namespace mvcExpress
{
    /// <summary>
    /// Inspector-friendly registry that registers Proxies into the application-wide global DI container.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Attach this component to the <see cref="MvcFacade"/> GameObject (or a child of it) to expose
    /// <see cref="ProxyBehaviour"/> instances to all modules via the global container. Global proxies
    /// are resolved by any module actor that uses <c>[InjectGlobal]</c>.
    /// </para>
    /// <para>
    /// Use this only for Proxies that manage truly shared state (e.g., <c>PlayerAccountProxy</c>,
    /// <c>PersistenceProxy</c>). Module-scoped proxies should use <see cref="ProxyRegistryBehaviour"/>
    /// instead to maintain clear ownership boundaries.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GlobalProxyRegistryBehaviour : MonoBehaviour
    {
        [SerializeField]
        private ProxyMapping[] _proxyMappings = Array.Empty<ProxyMapping>();

        /// <summary>
        /// Global proxy mappings configured for the app.
        /// </summary>
        public ProxyMapping[] ProxyMappings => _proxyMappings;
    }
}

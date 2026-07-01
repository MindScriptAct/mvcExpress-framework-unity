using mvcExpress.Internal.Proxy;
using System;
using UnityEngine;

namespace mvcExpress
{
    /// <summary>
    /// Inspector-friendly registry that registers Proxies into the owning module's DI container.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the Unity-behaviour registration method for Proxies - the first of three methods
    /// (Inspector → Attribute → Code). Add this component as a child of your <see cref="MvcModule"/>
    /// GameObject and configure <see cref="ProxyMappings"/> in the Inspector. The module
    /// initializer reads these mappings during <c>RegisterProxies</c>, before any
    /// <c>[Register]</c> attributes or code registrations.
    /// </para>
    /// <para>
    /// Use this approach for <see cref="ProxyBehaviour"/> instances that live in the scene
    /// or need to be assigned via the Inspector (e.g., a <c>ProxyBehaviour</c> pre-placed as
    /// a scene component).
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ProxyRegistryBehaviour : MonoBehaviour
    {
        [SerializeField]
        private ProxyMapping[] _proxyMappings = Array.Empty<ProxyMapping>();

        /// <summary>
        /// Proxy mappings configured for the owning module.
        /// </summary>
        public ProxyMapping[] ProxyMappings => _proxyMappings;
    }
}

using mvcExpress.Internal.Services;
using System;
using UnityEngine;

namespace mvcExpress
{
    /// <summary>
    /// Inspector-friendly registry that registers Services into the owning module's DI container.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the Unity-behaviour registration method for Services - the first of three methods
    /// (Inspector → Attribute → Code). Add this component as a child of your <see cref="MvcModule"/>
    /// GameObject and configure <see cref="ServiceMappings"/> in the Inspector. The module
    /// initializer reads these mappings during <c>RegisterServices</c>, before any
    /// <c>[Register]</c> attributes or code registrations are processed.
    /// </para>
    /// <para>
    /// Use this approach when: the service instance is a scene MonoBehaviour that must be
    /// assigned by drag-and-drop, or when non-programmers need to configure which service
    /// implementation is active without touching code.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class ServiceRegistryBehaviour : MonoBehaviour
    {
        [SerializeField]
        private ServiceMapping[] _serviceMappings = Array.Empty<ServiceMapping>();

        /// <summary>
        /// Service mappings configured for the owning module.
        /// </summary>
        public ServiceMapping[] ServiceMappings => _serviceMappings;

#if UNITY_EDITOR
        /// <summary>
        /// Gets the owning module for editor diagnostics.
        /// </summary>
        internal MvcModule GetOwningModuleForDebug()
        {
            return GetComponentInParent<MvcModule>();
        }
#endif
    }
}

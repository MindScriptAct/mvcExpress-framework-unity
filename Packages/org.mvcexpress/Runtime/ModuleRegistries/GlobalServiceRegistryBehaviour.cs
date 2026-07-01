using mvcExpress.Internal.Services;
using System;
using UnityEngine;

namespace mvcExpress
{
    /// <summary>
    /// Inspector-friendly registry that registers Services into the application-wide global DI container.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Attach this component to the <see cref="MvcFacade"/> GameObject (or a child of it) to expose
    /// Services to all modules via the global container. Global services are resolved by any module
    /// actor that uses <c>[InjectGlobal]</c>.
    /// </para>
    /// <para>
    /// Use this only for Services that are genuinely cross-module (e.g., <c>NetworkService</c>,
    /// <c>AudioService</c>). Module-scoped services should use <see cref="ServiceRegistryBehaviour"/>
    /// instead to keep module boundaries clean.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class GlobalServiceRegistryBehaviour : MonoBehaviour
    {
        [SerializeField]
        private ServiceMapping[] _serviceMappings = Array.Empty<ServiceMapping>();

        /// <summary>
        /// Global service mappings configured for the app.
        /// </summary>
        public ServiceMapping[] ServiceMappings => _serviceMappings;
    }
}

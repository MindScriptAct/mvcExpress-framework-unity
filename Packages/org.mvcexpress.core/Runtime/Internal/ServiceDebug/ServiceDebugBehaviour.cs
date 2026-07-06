using UnityEngine;

namespace mvcExpress.Internal.ServiceDebug
{
    /// <summary>
    /// Editor-only debug proxy for a code-registered service.
    /// Created at runtime by <c>ServiceHierarchyVisualizer</c>; never included in builds
    /// because the owning GameObject carries <c>HideFlags.DontSaveInBuild</c>.
    /// </summary>
    [AddComponentMenu("")] // hide from Add Component menu
    public sealed class ServiceDebugBehaviour : MonoBehaviour
    {
        /// <summary>The live service instance this component visualises.</summary>
        public object ServiceInstance { get; private set; }

        /// <summary>Called via SendMessage by ServiceHierarchyVisualizer.</summary>
        public void SetService(object service) => ServiceInstance = service;
    }
}

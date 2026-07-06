using UnityEngine;

namespace mvcExpress.Internal.ProxyDebug
{
    /// <summary>
    /// Editor-visible component that represents a code-only proxy in the Unity hierarchy.
    /// </summary>
    internal sealed class ProxyDebugBehaviour : MonoBehaviour
    {
        [SerializeField] private string _proxyTypeName;

        internal object ProxyInstance { get; private set; }

        internal void SetProxy(object proxy)
        {
            // Keep the hierarchy name readable while preserving the full type for inspection.
            ProxyInstance = proxy;
            _proxyTypeName = proxy != null ? proxy.GetType().FullName : null;
            name = proxy != null ? proxy.GetType().Name : name;
        }
    }
}

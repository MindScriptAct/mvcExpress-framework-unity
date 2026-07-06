#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;

namespace mvcExpress.Plugins
{
    /// <summary>
    /// Observer for module lifecycle events. Editor/dev builds only.
    /// </summary>
    public interface IModuleObserver
    {
        /// <summary>Called when a module is registered with <see cref="MvcFacade"/> (during <c>Awake</c>).</summary>
        /// <param name="moduleType">Concrete type of the registered module.</param>
        void OnModuleRegistered(Type moduleType);

        /// <summary>Called when a module completes its full initialization sequence.</summary>
        /// <param name="moduleType">Concrete type of the initialized module.</param>
        void OnModuleInitialized(Type moduleType);

        /// <summary>Called when a module is unregistered from <see cref="MvcFacade"/> (during <c>OnDestroy</c>).</summary>
        /// <param name="moduleType">Concrete type of the unregistered module.</param>
        void OnModuleUnregistered(Type moduleType);
    }
}
#endif

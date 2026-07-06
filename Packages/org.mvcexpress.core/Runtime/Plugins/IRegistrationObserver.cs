#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;
using mvcExpress.Logging;

namespace mvcExpress.Plugins
{
    /// <summary>
    /// Observer for service, proxy, command, and mediator registration events. Editor/dev builds only.
    /// </summary>
    public interface IRegistrationObserver
    {
        /// <summary>Called when a service is registered into a module's DI container.</summary>
        /// <param name="serviceType">Registered service type.</param>
        /// <param name="moduleType">Module that owns the registration.</param>
        /// <param name="source">Which registration style (Unity/Attribute/Code) produced this registration.</param>
        void OnServiceRegistered(Type serviceType, Type moduleType, MvcLogContext.RegistrationSource source);

        /// <summary>Called when a proxy is registered into a module's DI container.</summary>
        /// <param name="proxyType">Registered proxy type.</param>
        /// <param name="moduleType">Module that owns the registration.</param>
        /// <param name="source">Which registration style produced this registration.</param>
        void OnProxyRegistered(Type proxyType, Type moduleType, MvcLogContext.RegistrationSource source);

        /// <summary>Called when a command is bound to a message type.</summary>
        /// <param name="commandType">Command type that was bound.</param>
        /// <param name="messageType">Message type the command is bound to.</param>
        /// <param name="moduleType">Module that owns the binding.</param>
        /// <param name="source">Which registration style produced this binding.</param>
        void OnCommandBound(Type commandType, Type messageType, Type moduleType, MvcLogContext.RegistrationSource source);

        /// <summary>Called when a mediator is attached to a module.</summary>
        /// <param name="mediatorType">Mediator type that was attached.</param>
        /// <param name="moduleType">Module that owns the mediator.</param>
        /// <param name="source">Which registration style produced this attachment.</param>
        void OnMediatorAttached(Type mediatorType, Type moduleType, MvcLogContext.RegistrationSource source);
    }
}
#endif

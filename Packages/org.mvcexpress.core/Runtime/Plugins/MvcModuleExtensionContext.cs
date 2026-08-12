using mvcExpress.Internal.Messaging;
using mvcExpress;

namespace mvcExpress.Plugins
{
    /// <summary>
    /// Access surface handed to <see cref="IMvcModuleExtension"/> callbacks. Exposes the
    /// owning module, its message publishing API, its DI registration container, and the
    /// app-wide message bus for non-mediator subscriptions.
    /// </summary>
    public readonly struct MvcModuleExtensionContext
    {
        /// <summary>The module this extension is attached to.</summary>
        public MvcModule Module { get; }

        /// <summary>Publishes typed messages on the app-wide bus, attributed to the module.</summary>
        public MessengerApi Messenger { get; }

        /// <summary>Registers and resolves module-scoped dependencies.</summary>
        public ModuleRegistrationContainerApi Container { get; }

        /// <summary>
        /// The app-wide message bus. Advanced: allows extensions to Subscribe/Unsubscribe
        /// outside the mediator path. Callers own their unsubscription.
        /// </summary>
        public MvcMessageBus MessageBus { get; }

        internal MvcModuleExtensionContext(
            MvcModule module,
            MessengerApi messenger,
            ModuleRegistrationContainerApi container,
            MvcMessageBus messageBus)
        {
            Module = module;
            Messenger = messenger;
            Container = container;
            MessageBus = messageBus;
        }
    }
}

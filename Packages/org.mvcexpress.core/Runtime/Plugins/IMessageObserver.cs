#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;

namespace mvcExpress.Plugins
{
    /// <summary>
    /// Observer for message publish and per-handler dispatch events. Editor/dev builds only.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>OnMessagePublished</c> fires once per publish call.
    /// <c>OnMessageDispatched</c> fires once per handler that receives the message.
    /// </para>
    /// <para>
    /// <c>publisherModuleType</c> is null when a global actor (no owning module) publishes.
    /// <c>subscriberModuleType</c> is null when a global actor subscribes.
    /// </para>
    /// Register via <see cref="MvcPluginBus.Register(object)"/> or <see cref="MvcPluginBus.Register(IMessageObserver)"/>.
    /// </remarks>
    public interface IMessageObserver
    {
        /// <summary>Called once each time a message is published on the bus, before any handlers run.</summary>
        /// <param name="messageType">The concrete message type that was published.</param>
        /// <param name="publisherModuleType">Module type that published the message; <c>null</c> for global actors.</param>
        void OnMessagePublished(Type messageType, Type publisherModuleType);

        /// <summary>Called once per subscriber handler that receives a dispatched message.</summary>
        /// <param name="messageType">The concrete message type.</param>
        /// <param name="publisherModuleType">Module type that published the message; <c>null</c> for global actors.</param>
        /// <param name="subscriberActorType">Runtime type of the subscribing actor (command, mediator, etc.).</param>
        /// <param name="subscriberModuleType">Module type that owns the subscriber; <c>null</c> for global actors.</param>
        void OnMessageDispatched(
            Type messageType,
            Type publisherModuleType,
            Type subscriberActorType,
            Type subscriberModuleType);
    }
}
#endif

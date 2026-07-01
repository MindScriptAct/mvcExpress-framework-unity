#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;

namespace mvcExpress.Plugins
{
    /// <summary>
    /// Thread-local publisher context set by <c>MessengerApi.Publish()</c> before dispatching
    /// and cleared after. Safe as [ThreadStatic] because MvcMessageBus.Publish() is main-thread only.
    /// </summary>
    /// <remarks>
    /// Used by <see cref="MvcPluginBus"/> to deliver publisher module identity to
    /// <see cref="IMessageObserver.OnMessageDispatched"/>. Also the shared primitive for the
    /// future Module Guardian plugin.
    /// </remarks>
    internal static class CrossModuleContext
    {
        [System.ThreadStatic]
        private static Type _currentPublisherModuleType;

        /// <summary>The module type of the actor currently dispatching a message, or null for global actors.</summary>
        internal static Type CurrentPublisher => _currentPublisherModuleType;

        internal static void SetPublisher(Type moduleType) => _currentPublisherModuleType = moduleType;
        internal static void ClearPublisher() => _currentPublisherModuleType = null;
    }
}
#endif

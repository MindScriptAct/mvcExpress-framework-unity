using System;

namespace mvcExpress.Internal.Interfaces
{
    /// <summary>
    /// INTERNAL: Framework infrastructure for transient dependency lifecycle notifications.
    /// Used by MvcCommandProcessor to invalidate pooled commands when their dependencies are removed.
    /// 
    /// NOT FOR APPLICATION USE - This is internal framework plumbing.
    /// </summary>
    internal interface ITransientDependencyNotifier
    {
        /// <summary>
        /// Subscribe to transient dependency removal using weak reference.
        /// The subscription will automatically be removed when the subscriber is GC'd.
        /// 
        /// FRAMEWORK INTERNAL: Used by command processor for automatic command invalidation.
        /// </summary>
        /// <param name="handler">Action to invoke when a transient dependency is removed</param>
        void SubscribeToTransientRemoval(Action<Type> handler);

        /// <summary>
        /// Explicitly unsubscribe from transient dependency removal.
        /// 
        /// FRAMEWORK INTERNAL: Used during command processor disposal.
        /// </summary>
        /// <param name="handler">Action to unsubscribe</param>
        void UnsubscribeFromTransientRemoval(Action<Type> handler);
    }
}

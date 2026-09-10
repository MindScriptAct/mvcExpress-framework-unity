using System;
using System.Collections.Generic;
using System.ComponentModel;
using mvcExpress.Logging;

namespace mvcExpress.Internal.Messaging
{
    /// <summary>
    /// Tracks all active message subscriptions for a single <see cref="MediatorBehaviour"/> and
    /// bulk-unsubscribes them when the mediator is destroyed.
    /// </summary>
    /// <remarks>
    /// Why this exists:
    /// Without a tracker, each mediator would have to maintain its own list of
    /// <see cref="SubscriptionToken"/> values and call <c>Unsubscribe</c> for each one in
    /// <c>OnDestroy</c> - boilerplate the framework wants to eliminate. <see cref="UnsubscribeAll"/>
    /// does that bulk removal in one call.
    ///
    /// Usage invariant:
    /// <see cref="UnsubscribeAll"/> must be called from the mediator's <c>CleanupMediator</c> path
    /// before the mediator object is released. Failure to do so will leave handlers in the message
    /// bus pointing to a dead object, which causes no crash (null-handler guard in the bus) but
    /// wastes a slot forever, since nothing else will unsubscribe it.
    ///
    /// This is deliberately explicit-only ownership - <see cref="UnsubscribeAll"/> is the sole
    /// cleanup mechanism, called once from the mediator's teardown path. An earlier version of this
    /// class also carried a periodic <c>WeakReference</c>-based sweep intended as an automatic
    /// safety net for a mediator garbage-collected without <c>OnDestroy</c> running - that sweep was
    /// removed (MSG-06) because it could never actually find anything: the message bus's own
    /// subscribed delegate holds a *strong* reference to the same subscriber for as long as the
    /// subscription is live, so the subscriber can never become weakly-collectible while tracked
    /// here. Explicit ownership via <see cref="UnsubscribeAll"/> was always the only mechanism that
    /// actually worked; removing the non-functional sweep also removes a per-subscription
    /// <c>WeakReference</c> and a wasted scan every <c>Track</c> call.
    ///
    /// Thread-safe via a single lock. All operations are expected on the Unity main thread, but
    /// the lock is present as a safety net for Editor tooling that may inspect state off-thread.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class SubscriptionTracker
    {
        // All tracked subscriptions for this mediator, removed individually via Untrack or in bulk
        // via UnsubscribeAll.
        private readonly List<TrackedSubscription> _subscriptions = new List<TrackedSubscription>(16);
        private readonly object _lock = new object();

        // Lightweight struct stored per subscription.
        private struct TrackedSubscription
        {
            public SubscriptionToken Token;
            public Type MessageType;
            public int ParamCount;
            // Delegate that calls the appropriate Unsubscribe overload on the message bus.
            public Action<SubscriptionToken> UnsubscribeAction;
        }

        /// <summary>
        /// Records a subscription so it can be unsubscribed automatically on mediator destroy.
        /// </summary>
        /// <param name="messageType">The message type that was subscribed to.</param>
        /// <param name="subscriber">The subscribing object. Only validated non-null - not stored;
        /// ownership of the subscription is tracked explicitly via <paramref name="token"/> and
        /// <paramref name="unsubscribeAction"/>, not by holding a reference to the subscriber.</param>
        /// <param name="token">The token returned by <see cref="MvcMessageBus.Subscribe{TMessage}"/>.</param>
        /// <param name="paramCount">Payload parameter count, used to select the correct Unsubscribe overload.</param>
        /// <param name="unsubscribeAction">Closure that calls the correct Unsubscribe method on the bus.</param>
        public void Track(
            Type messageType,
            object subscriber,
            SubscriptionToken token,
            int paramCount,
            Action<SubscriptionToken> unsubscribeAction)
        {
            if (subscriber == null) return;

            lock (_lock)
            {
                _subscriptions.Add(new TrackedSubscription
                {
                    Token = token,
                    MessageType = messageType,
                    ParamCount = paramCount,
                    UnsubscribeAction = unsubscribeAction
                });
            }
        }

        /// <summary>
        /// Convenience overload that captures the message type automatically.
        /// Avoids requiring callers to pass <c>typeof(TMessage)</c> explicitly.
        /// </summary>
        public void Track<TMessage>(
            object subscriber,
            SubscriptionToken token,
            int paramCount,
            Action<SubscriptionToken> unsubscribeAction)
        {
            Track(typeof(TMessage), subscriber, token, paramCount, unsubscribeAction);
        }

        /// <summary>
        /// Removes a specific subscription from the tracker without calling its unsubscribe action.
        /// Used when the caller has already unsubscribed from the bus and only wants to clean up
        /// the tracker entry.
        /// </summary>
        /// <param name="messageType">
        /// The message type the token was issued for. <see cref="SubscriptionToken.Handle"/>/<see cref="SubscriptionToken.Version"/>
        /// are only unique per message type (each type's <c>Storage&lt;TMessage&gt;</c> has its own
        /// version counter), so matching on the token alone can remove the wrong entry when a
        /// mediator has first-subscriber tokens on two different message types.
        /// </param>
        /// <param name="token">Token returned by the original <c>Subscribe</c> call.</param>
        /// <returns><c>true</c> if the token was found and removed; <c>false</c> otherwise.</returns>
        public bool Untrack(Type messageType, SubscriptionToken token)
        {
            lock (_lock)
            {
                for (int i = _subscriptions.Count - 1; i >= 0; i--)
                {
                    if (_subscriptions[i].MessageType == messageType &&
                        _subscriptions[i].Token.Handle == token.Handle &&
                        _subscriptions[i].Token.Version == token.Version)
                    {
                        _subscriptions.RemoveAt(i);
                        return true;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Unsubscribe all tracked subscriptions immediately.
        /// </summary>
        public void UnsubscribeAll()
        {
            lock (_lock)
            {
                for (int i = _subscriptions.Count - 1; i >= 0; i--)
                {
                    var sub = _subscriptions[i];
                    try
                    {
                        sub.UnsubscribeAction?.Invoke(sub.Token);
                    }
                    catch (Exception ex)
                    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        MvcDebug.LogWarning($"Error unsubscribing tracked subscription: {ex.Message}");
#endif
                    }
                }
                _subscriptions.Clear();
            }
        }

        /// <summary>
        /// Get count of tracked subscriptions.
        /// </summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _subscriptions.Count;
                }
            }
        }
    }
}

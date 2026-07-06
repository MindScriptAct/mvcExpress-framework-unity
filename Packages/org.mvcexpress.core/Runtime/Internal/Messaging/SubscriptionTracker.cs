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
    /// <c>OnDestroy</c> - boilerplate the framework wants to eliminate. The tracker holds weak
    /// references to the subscriber objects so stale subscriptions are cleaned up even if the
    /// mediator is garbage-collected without <c>OnDestroy</c> being called (e.g. editor hot-reload).
    ///
    /// Usage invariant:
    /// <see cref="UnsubscribeAll"/> must be called from the mediator's <c>CleanupMediator</c> path
    /// before the mediator object is released. Failure to do so will leave handlers in the message
    /// bus pointing to a dead object, which causes no crash (null-handler guard in the bus) but
    /// wastes a slot until the next <see cref="CleanupDead"/> pass.
    ///
    /// Periodic dead-ref cleanup:
    /// <see cref="CheckAndCleanup"/> increments an internal counter each call and triggers
    /// <see cref="CleanupDead"/> every <c>CleanupThreshold</c> (20) calls to reclaim slots from
    /// GC'd subscribers without paying a full scan on every subscribe.
    ///
    /// Thread-safe via a single lock. All operations are expected on the Unity main thread, but
    /// the lock is present as a safety net for Editor tooling that may inspect state off-thread.
    /// </remarks>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class SubscriptionTracker
    {
        // All live (and possibly dead) tracked subscriptions for this mediator.
        private readonly List<TrackedSubscription> _subscriptions = new List<TrackedSubscription>(16);
        private readonly object _lock = new object();
        // Running count of calls to CheckAndCleanup() since the last full CleanupDead() sweep.
        private int _deadCount;
        // How many CheckAndCleanup() calls trigger a full CleanupDead() sweep.
        private const int CleanupThreshold = 20;

        // Lightweight struct stored per subscription. WeakReference prevents the tracker from
        // keeping subscribers alive past their natural lifetime.
        private struct TrackedSubscription
        {
            public WeakReference<object> SubscriberRef;
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
        /// <param name="subscriber">The subscribing object (held via weak reference).</param>
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
                    SubscriberRef = new WeakReference<object>(subscriber),
                    Token = token,
                    MessageType = messageType,
                    ParamCount = paramCount,
                    UnsubscribeAction = unsubscribeAction
                });
            }

            // Wired in as documented on CheckAndCleanup() itself - see L10. Called outside the
            // lock above so any CleanupDead() sweep it triggers (which invokes arbitrary
            // unsubscribeAction callbacks) doesn't run while still holding the lock for the Add.
            CheckAndCleanup();
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
        /// The message type the token was issued for. <see cref="SubscriptionToken.Index"/>/<see cref="SubscriptionToken.Version"/>
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
                        _subscriptions[i].Token.Index == token.Index &&
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
        /// Clean up subscriptions with dead subscribers.
        /// </summary>
        public void CleanupDead()
        {
            lock (_lock)
            {
                for (int i = _subscriptions.Count - 1; i >= 0; i--)
                {
                    var sub = _subscriptions[i];
                    if (!sub.SubscriberRef.TryGetTarget(out _))
                    {
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

                        _subscriptions.RemoveAt(i);
                    }
                }
                _deadCount = 0;

            }
        }

        /// <summary>
        /// Increments the dead-reference counter and triggers <see cref="CleanupDead"/> when
        /// the counter reaches <c>CleanupThreshold</c>, lazily reclaiming slots from GC'd
        /// subscribers without paying a full scan cost on every subscribe.
        /// </summary>
        /// <remarks>
        /// Invoked automatically by <see cref="Track"/> after every new subscription - no
        /// external caller needs to invoke this for normal usage. Exposed publicly in case
        /// tooling needs to force a cleanup check without adding a new subscription.
        /// </remarks>
        public void CheckAndCleanup()
        {
            lock (_lock)
            {
                _deadCount++;
                if (_deadCount >= CleanupThreshold)
                {
                    CleanupDead();
                }
            }
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
                _deadCount = 0;
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

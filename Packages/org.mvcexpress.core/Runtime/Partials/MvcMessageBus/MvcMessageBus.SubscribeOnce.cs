// SubscribeOnce partial of MvcMessageBus - fire-once subscriptions for arities 0-5.
// Each overload allocates a wrapper delegate that invokes the caller's handler and then
// unsubscribes itself using its own captured SubscriptionToken. The unsubscribe happens in a
// `finally` block so a throwing handler still results in auto-unsubscribe. Two independent
// SubscribeOnce registrations for the same message type each capture their OWN token, so both
// fire exactly once and unsubscribe independently. Arity 0-5 only; use plain Subscribe plus
// manual bookkeeping for higher arities.
using mvcExpress.Internal.Interfaces;
using System;

namespace mvcExpress.Internal.Messaging
{
    public sealed partial class MvcMessageBus : IMessageBus, IDisposable
    {
        /// <summary>
        /// Subscribes to a no-payload message, automatically unsubscribing after the first
        /// delivery. Useful for fire-once signals like a one-time transition or intro sequence.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage"/>.</typeparam>
        /// <param name="handler">Callback invoked exactly once, on the first <typeparamref name="TMessage"/> publish.</param>
        /// <returns>
        /// The wrapper's <see cref="SubscriptionToken"/>, which can be passed to <see cref="Unsubscribe{TMessage}"/>
        /// to cancel before the first delivery.
        /// </returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the auto-unsubscribe behavior - prefer a
        /// normal <see cref="Subscribe{TMessage}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeOnce<TMessage>(Action handler) where TMessage : IMessage
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            SubscriptionToken token = default;
            Action wrapper = null;
            wrapper = () =>
            {
                try
                {
                    handler();
                }
                finally
                {
                    Unsubscribe<TMessage>(token);
                }
            };

            token = Subscribe<TMessage>(wrapper);
            return token;
        }

        /// <summary>
        /// Subscribes to a one-parameter message, automatically unsubscribing after the first
        /// delivery. Useful for fire-once signals like a one-time transition or intro sequence.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1}"/>.</typeparam>
        /// <param name="handler">Callback invoked exactly once, on the first <typeparamref name="TMessage"/> publish.</param>
        /// <returns>
        /// The wrapper's <see cref="SubscriptionToken"/>, which can be passed to <see cref="Unsubscribe{TMessage, T1}"/>
        /// to cancel before the first delivery.
        /// </returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the auto-unsubscribe behavior - prefer a
        /// normal <see cref="Subscribe{TMessage, T1}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeOnce<TMessage, T1>(Action<T1> handler) where TMessage : IMessage<T1>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            SubscriptionToken token = default;
            Action<T1> wrapper = null;
            wrapper = (p1) =>
            {
                try
                {
                    handler(p1);
                }
                finally
                {
                    Unsubscribe<TMessage, T1>(token);
                }
            };

            token = Subscribe<TMessage, T1>(wrapper);
            return token;
        }

        /// <summary>
        /// Subscribes to a two-parameter message, automatically unsubscribing after the first
        /// delivery. Useful for fire-once signals like a one-time transition or intro sequence.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2}"/>.</typeparam>
        /// <param name="handler">Callback invoked exactly once, on the first <typeparamref name="TMessage"/> publish.</param>
        /// <returns>
        /// The wrapper's <see cref="SubscriptionToken"/>, which can be passed to <see cref="Unsubscribe{TMessage, T1, T2}"/>
        /// to cancel before the first delivery.
        /// </returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the auto-unsubscribe behavior - prefer a
        /// normal <see cref="Subscribe{TMessage, T1, T2}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeOnce<TMessage, T1, T2>(Action<T1, T2> handler) where TMessage : IMessage<T1, T2>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            SubscriptionToken token = default;
            Action<T1, T2> wrapper = null;
            wrapper = (p1, p2) =>
            {
                try
                {
                    handler(p1, p2);
                }
                finally
                {
                    Unsubscribe<TMessage, T1, T2>(token);
                }
            };

            token = Subscribe<TMessage, T1, T2>(wrapper);
            return token;
        }

        /// <summary>
        /// Subscribes to a three-parameter message, automatically unsubscribing after the first
        /// delivery. Useful for fire-once signals like a one-time transition or intro sequence.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2, T3}"/>.</typeparam>
        /// <param name="handler">Callback invoked exactly once, on the first <typeparamref name="TMessage"/> publish.</param>
        /// <returns>
        /// The wrapper's <see cref="SubscriptionToken"/>, which can be passed to <see cref="Unsubscribe{TMessage, T1, T2, T3}"/>
        /// to cancel before the first delivery.
        /// </returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the auto-unsubscribe behavior - prefer a
        /// normal <see cref="Subscribe{TMessage, T1, T2, T3}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeOnce<TMessage, T1, T2, T3>(Action<T1, T2, T3> handler) where TMessage : IMessage<T1, T2, T3>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            SubscriptionToken token = default;
            Action<T1, T2, T3> wrapper = null;
            wrapper = (p1, p2, p3) =>
            {
                try
                {
                    handler(p1, p2, p3);
                }
                finally
                {
                    Unsubscribe<TMessage, T1, T2, T3>(token);
                }
            };

            token = Subscribe<TMessage, T1, T2, T3>(wrapper);
            return token;
        }

        /// <summary>
        /// Subscribes to a four-parameter message, automatically unsubscribing after the first
        /// delivery. Useful for fire-once signals like a one-time transition or intro sequence.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2, T3, T4}"/>.</typeparam>
        /// <param name="handler">Callback invoked exactly once, on the first <typeparamref name="TMessage"/> publish.</param>
        /// <returns>
        /// The wrapper's <see cref="SubscriptionToken"/>, which can be passed to <see cref="Unsubscribe{TMessage, T1, T2, T3, T4}"/>
        /// to cancel before the first delivery.
        /// </returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the auto-unsubscribe behavior - prefer a
        /// normal <see cref="Subscribe{TMessage, T1, T2, T3, T4}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeOnce<TMessage, T1, T2, T3, T4>(Action<T1, T2, T3, T4> handler) where TMessage : IMessage<T1, T2, T3, T4>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            SubscriptionToken token = default;
            Action<T1, T2, T3, T4> wrapper = null;
            wrapper = (p1, p2, p3, p4) =>
            {
                try
                {
                    handler(p1, p2, p3, p4);
                }
                finally
                {
                    Unsubscribe<TMessage, T1, T2, T3, T4>(token);
                }
            };

            token = Subscribe<TMessage, T1, T2, T3, T4>(wrapper);
            return token;
        }

        /// <summary>
        /// Subscribes to a five-parameter message, automatically unsubscribing after the first
        /// delivery. Useful for fire-once signals like a one-time transition or intro sequence.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2, T3, T4, T5}"/>.</typeparam>
        /// <param name="handler">Callback invoked exactly once, on the first <typeparamref name="TMessage"/> publish.</param>
        /// <returns>
        /// The wrapper's <see cref="SubscriptionToken"/>, which can be passed to <see cref="Unsubscribe{TMessage, T1, T2, T3, T4, T5}"/>
        /// to cancel before the first delivery.
        /// </returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the auto-unsubscribe behavior - prefer a
        /// normal <see cref="Subscribe{TMessage, T1, T2, T3, T4, T5}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeOnce<TMessage, T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> handler) where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            SubscriptionToken token = default;
            Action<T1, T2, T3, T4, T5> wrapper = null;
            wrapper = (p1, p2, p3, p4, p5) =>
            {
                try
                {
                    handler(p1, p2, p3, p4, p5);
                }
                finally
                {
                    Unsubscribe<TMessage, T1, T2, T3, T4, T5>(token);
                }
            };

            token = Subscribe<TMessage, T1, T2, T3, T4, T5>(wrapper);
            return token;
        }
    }
}

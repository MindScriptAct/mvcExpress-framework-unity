// SubscribeWhen partial of MvcMessageBus - conditional subscriptions for arities 0-5.
// Each overload allocates a persistent wrapper delegate that evaluates the caller's condition on
// every dispatch and only invokes the handler when it returns true. This is NOT one-shot: the
// wrapper stays subscribed and keeps firing whenever the predicate is true, including on the very
// first publish if the predicate is already true at subscribe time (no false-to-true transition
// is required). Arity 0-5 only; use plain Subscribe plus manual bookkeeping for higher arities.
using mvcExpress.Internal.Interfaces;
using System;

namespace mvcExpress.Internal.Messaging
{
    public sealed partial class MvcMessageBus : IMessageBus, IDisposable
    {
        /// <summary>
        /// Subscribes to a no-payload message, invoking <paramref name="handler"/> only when
        /// <paramref name="condition"/> evaluates to <c>true</c> at the time of publish.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage"/>.</typeparam>
        /// <param name="handler">Callback invoked when <typeparamref name="TMessage"/> is published and <paramref name="condition"/> is true.</param>
        /// <param name="condition">Checked on every publish; the handler only runs when this returns <c>true</c>.</param>
        /// <returns>The wrapper's <see cref="SubscriptionToken"/> for optional early removal via <see cref="Unsubscribe{TMessage}"/>.</returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the condition check - prefer a normal
        /// <see cref="Subscribe{TMessage}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeWhen<TMessage>(Action handler, Func<bool> condition) where TMessage : IMessage
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            Action wrapper = () =>
            {
                if (!condition()) return;
                handler();
            };

            return Subscribe<TMessage>(wrapper);
        }

        /// <summary>
        /// Subscribes to a one-parameter message, invoking <paramref name="handler"/> only when
        /// <paramref name="condition"/> evaluates to <c>true</c> at the time of publish.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1}"/>.</typeparam>
        /// <param name="handler">Callback invoked when <typeparamref name="TMessage"/> is published and <paramref name="condition"/> is true.</param>
        /// <param name="condition">Checked on every publish; the handler only runs when this returns <c>true</c>.</param>
        /// <returns>The wrapper's <see cref="SubscriptionToken"/> for optional early removal via <see cref="Unsubscribe{TMessage, T1}"/>.</returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the condition check - prefer a normal
        /// <see cref="Subscribe{TMessage, T1}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeWhen<TMessage, T1>(Action<T1> handler, Func<bool> condition) where TMessage : IMessage<T1>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            Action<T1> wrapper = (p1) =>
            {
                if (!condition()) return;
                handler(p1);
            };

            return Subscribe<TMessage, T1>(wrapper);
        }

        /// <summary>
        /// Subscribes to a two-parameter message, invoking <paramref name="handler"/> only when
        /// <paramref name="condition"/> evaluates to <c>true</c> at the time of publish.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2}"/>.</typeparam>
        /// <param name="handler">Callback invoked when <typeparamref name="TMessage"/> is published and <paramref name="condition"/> is true.</param>
        /// <param name="condition">Checked on every publish; the handler only runs when this returns <c>true</c>.</param>
        /// <returns>The wrapper's <see cref="SubscriptionToken"/> for optional early removal via <see cref="Unsubscribe{TMessage, T1, T2}"/>.</returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the condition check - prefer a normal
        /// <see cref="Subscribe{TMessage, T1, T2}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeWhen<TMessage, T1, T2>(Action<T1, T2> handler, Func<bool> condition) where TMessage : IMessage<T1, T2>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            Action<T1, T2> wrapper = (p1, p2) =>
            {
                if (!condition()) return;
                handler(p1, p2);
            };

            return Subscribe<TMessage, T1, T2>(wrapper);
        }

        /// <summary>
        /// Subscribes to a three-parameter message, invoking <paramref name="handler"/> only when
        /// <paramref name="condition"/> evaluates to <c>true</c> at the time of publish.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2, T3}"/>.</typeparam>
        /// <param name="handler">Callback invoked when <typeparamref name="TMessage"/> is published and <paramref name="condition"/> is true.</param>
        /// <param name="condition">Checked on every publish; the handler only runs when this returns <c>true</c>.</param>
        /// <returns>The wrapper's <see cref="SubscriptionToken"/> for optional early removal via <see cref="Unsubscribe{TMessage, T1, T2, T3}"/>.</returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the condition check - prefer a normal
        /// <see cref="Subscribe{TMessage, T1, T2, T3}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeWhen<TMessage, T1, T2, T3>(Action<T1, T2, T3> handler, Func<bool> condition) where TMessage : IMessage<T1, T2, T3>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            Action<T1, T2, T3> wrapper = (p1, p2, p3) =>
            {
                if (!condition()) return;
                handler(p1, p2, p3);
            };

            return Subscribe<TMessage, T1, T2, T3>(wrapper);
        }

        /// <summary>
        /// Subscribes to a four-parameter message, invoking <paramref name="handler"/> only when
        /// <paramref name="condition"/> evaluates to <c>true</c> at the time of publish.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2, T3, T4}"/>.</typeparam>
        /// <param name="handler">Callback invoked when <typeparamref name="TMessage"/> is published and <paramref name="condition"/> is true.</param>
        /// <param name="condition">Checked on every publish; the handler only runs when this returns <c>true</c>.</param>
        /// <returns>The wrapper's <see cref="SubscriptionToken"/> for optional early removal via <see cref="Unsubscribe{TMessage, T1, T2, T3, T4}"/>.</returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the condition check - prefer a normal
        /// <see cref="Subscribe{TMessage, T1, T2, T3, T4}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeWhen<TMessage, T1, T2, T3, T4>(Action<T1, T2, T3, T4> handler, Func<bool> condition) where TMessage : IMessage<T1, T2, T3, T4>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            Action<T1, T2, T3, T4> wrapper = (p1, p2, p3, p4) =>
            {
                if (!condition()) return;
                handler(p1, p2, p3, p4);
            };

            return Subscribe<TMessage, T1, T2, T3, T4>(wrapper);
        }

        /// <summary>
        /// Subscribes to a five-parameter message, invoking <paramref name="handler"/> only when
        /// <paramref name="condition"/> evaluates to <c>true</c> at the time of publish.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2, T3, T4, T5}"/>.</typeparam>
        /// <param name="handler">Callback invoked when <typeparamref name="TMessage"/> is published and <paramref name="condition"/> is true.</param>
        /// <param name="condition">Checked on every publish; the handler only runs when this returns <c>true</c>.</param>
        /// <returns>The wrapper's <see cref="SubscriptionToken"/> for optional early removal via <see cref="Unsubscribe{TMessage, T1, T2, T3, T4, T5}"/>.</returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the condition check - prefer a normal
        /// <see cref="Subscribe{TMessage, T1, T2, T3, T4, T5}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeWhen<TMessage, T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> handler, Func<bool> condition) where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            Action<T1, T2, T3, T4, T5> wrapper = (p1, p2, p3, p4, p5) =>
            {
                if (!condition()) return;
                handler(p1, p2, p3, p4, p5);
            };

            return Subscribe<TMessage, T1, T2, T3, T4, T5>(wrapper);
        }
    }
}

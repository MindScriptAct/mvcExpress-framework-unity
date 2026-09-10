// SubscribeWhen partial of MvcMessageBus - conditional subscriptions for arities 0-12.
// Each overload allocates a persistent wrapper delegate that evaluates the caller's condition on
// every dispatch and only invokes the handler when it returns true. This is NOT one-shot: the
// wrapper stays subscribed and keeps firing whenever the predicate is true, including on the very
// first publish if the predicate is already true at subscribe time (no false-to-true transition
// is required).
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

        /// <summary>
        /// Subscribes to a six-parameter message, invoking <paramref name="handler"/> only when
        /// <paramref name="condition"/> evaluates to <c>true</c> at the time of publish.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2, T3, T4, T5, T6}"/>.</typeparam>
        /// <param name="handler">Callback invoked when <typeparamref name="TMessage"/> is published and <paramref name="condition"/> is true.</param>
        /// <param name="condition">Checked on every publish; the handler only runs when this returns <c>true</c>.</param>
        /// <returns>The wrapper's <see cref="SubscriptionToken"/> for optional early removal via <see cref="Unsubscribe{TMessage, T1, T2, T3, T4, T5, T6}"/>.</returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the condition check - prefer a normal
        /// <see cref="Subscribe{TMessage, T1, T2, T3, T4, T5, T6}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeWhen<TMessage, T1, T2, T3, T4, T5, T6>(Action<T1, T2, T3, T4, T5, T6> handler, Func<bool> condition) where TMessage : IMessage<T1, T2, T3, T4, T5, T6>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            Action<T1, T2, T3, T4, T5, T6> wrapper = (p1, p2, p3, p4, p5, p6) =>
            {
                if (!condition()) return;
                handler(p1, p2, p3, p4, p5, p6);
            };

            return Subscribe<TMessage, T1, T2, T3, T4, T5, T6>(wrapper);
        }

        /// <summary>
        /// Subscribes to a seven-parameter message, invoking <paramref name="handler"/> only when
        /// <paramref name="condition"/> evaluates to <c>true</c> at the time of publish.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2, T3, T4, T5, T6, T7}"/>.</typeparam>
        /// <param name="handler">Callback invoked when <typeparamref name="TMessage"/> is published and <paramref name="condition"/> is true.</param>
        /// <param name="condition">Checked on every publish; the handler only runs when this returns <c>true</c>.</param>
        /// <returns>The wrapper's <see cref="SubscriptionToken"/> for optional early removal via <see cref="Unsubscribe{TMessage, T1, T2, T3, T4, T5, T6, T7}"/>.</returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the condition check - prefer a normal
        /// <see cref="Subscribe{TMessage, T1, T2, T3, T4, T5, T6, T7}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeWhen<TMessage, T1, T2, T3, T4, T5, T6, T7>(Action<T1, T2, T3, T4, T5, T6, T7> handler, Func<bool> condition) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            Action<T1, T2, T3, T4, T5, T6, T7> wrapper = (p1, p2, p3, p4, p5, p6, p7) =>
            {
                if (!condition()) return;
                handler(p1, p2, p3, p4, p5, p6, p7);
            };

            return Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7>(wrapper);
        }

        /// <summary>
        /// Subscribes to an eight-parameter message, invoking <paramref name="handler"/> only when
        /// <paramref name="condition"/> evaluates to <c>true</c> at the time of publish.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2, T3, T4, T5, T6, T7, T8}"/>.</typeparam>
        /// <param name="handler">Callback invoked when <typeparamref name="TMessage"/> is published and <paramref name="condition"/> is true.</param>
        /// <param name="condition">Checked on every publish; the handler only runs when this returns <c>true</c>.</param>
        /// <returns>The wrapper's <see cref="SubscriptionToken"/> for optional early removal via <see cref="Unsubscribe{TMessage, T1, T2, T3, T4, T5, T6, T7, T8}"/>.</returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the condition check - prefer a normal
        /// <see cref="Subscribe{TMessage, T1, T2, T3, T4, T5, T6, T7, T8}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeWhen<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(Action<T1, T2, T3, T4, T5, T6, T7, T8> handler, Func<bool> condition) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            Action<T1, T2, T3, T4, T5, T6, T7, T8> wrapper = (p1, p2, p3, p4, p5, p6, p7, p8) =>
            {
                if (!condition()) return;
                handler(p1, p2, p3, p4, p5, p6, p7, p8);
            };

            return Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(wrapper);
        }

        /// <summary>
        /// Subscribes to a nine-parameter message, invoking <paramref name="handler"/> only when
        /// <paramref name="condition"/> evaluates to <c>true</c> at the time of publish.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2, T3, T4, T5, T6, T7, T8, T9}"/>.</typeparam>
        /// <param name="handler">Callback invoked when <typeparamref name="TMessage"/> is published and <paramref name="condition"/> is true.</param>
        /// <param name="condition">Checked on every publish; the handler only runs when this returns <c>true</c>.</param>
        /// <returns>The wrapper's <see cref="SubscriptionToken"/> for optional early removal via <see cref="Unsubscribe{TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9}"/>.</returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the condition check - prefer a normal
        /// <see cref="Subscribe{TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeWhen<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> handler, Func<bool> condition) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> wrapper = (p1, p2, p3, p4, p5, p6, p7, p8, p9) =>
            {
                if (!condition()) return;
                handler(p1, p2, p3, p4, p5, p6, p7, p8, p9);
            };

            return Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(wrapper);
        }

        /// <summary>
        /// Subscribes to a ten-parameter message, invoking <paramref name="handler"/> only when
        /// <paramref name="condition"/> evaluates to <c>true</c> at the time of publish.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2, T3, T4, T5, T6, T7, T8, T9, T10}"/>.</typeparam>
        /// <param name="handler">Callback invoked when <typeparamref name="TMessage"/> is published and <paramref name="condition"/> is true.</param>
        /// <param name="condition">Checked on every publish; the handler only runs when this returns <c>true</c>.</param>
        /// <returns>The wrapper's <see cref="SubscriptionToken"/> for optional early removal via <see cref="Unsubscribe{TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10}"/>.</returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the condition check - prefer a normal
        /// <see cref="Subscribe{TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeWhen<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> handler, Func<bool> condition) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> wrapper = (p1, p2, p3, p4, p5, p6, p7, p8, p9, p10) =>
            {
                if (!condition()) return;
                handler(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10);
            };

            return Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(wrapper);
        }

        /// <summary>
        /// Subscribes to an eleven-parameter message, invoking <paramref name="handler"/> only when
        /// <paramref name="condition"/> evaluates to <c>true</c> at the time of publish.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11}"/>.</typeparam>
        /// <param name="handler">Callback invoked when <typeparamref name="TMessage"/> is published and <paramref name="condition"/> is true.</param>
        /// <param name="condition">Checked on every publish; the handler only runs when this returns <c>true</c>.</param>
        /// <returns>The wrapper's <see cref="SubscriptionToken"/> for optional early removal via <see cref="Unsubscribe{TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11}"/>.</returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the condition check - prefer a normal
        /// <see cref="Subscribe{TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeWhen<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> handler, Func<bool> condition) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> wrapper = (p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11) =>
            {
                if (!condition()) return;
                handler(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11);
            };

            return Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(wrapper);
        }

        /// <summary>
        /// Subscribes to a twelve-parameter message, invoking <paramref name="handler"/> only when
        /// <paramref name="condition"/> evaluates to <c>true</c> at the time of publish.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12}"/>.</typeparam>
        /// <param name="handler">Callback invoked when <typeparamref name="TMessage"/> is published and <paramref name="condition"/> is true.</param>
        /// <param name="condition">Checked on every publish; the handler only runs when this returns <c>true</c>.</param>
        /// <returns>The wrapper's <see cref="SubscriptionToken"/> for optional early removal via <see cref="Unsubscribe{TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12}"/>.</returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the condition check - prefer a normal
        /// <see cref="Subscribe{TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeWhen<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> handler, Func<bool> condition) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));

            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> wrapper = (p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12) =>
            {
                if (!condition()) return;
                handler(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12);
            };

            return Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(wrapper);
        }
    }
}

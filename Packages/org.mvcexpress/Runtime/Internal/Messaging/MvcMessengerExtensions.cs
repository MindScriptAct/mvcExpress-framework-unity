using mvcExpress;
using System;

namespace mvcExpress.Internal.Messaging
{
    /// <summary>
    /// Extension methods for <see cref="MvcMessageBus"/> providing higher-level subscription patterns.
    ///
    /// GOAL:
    /// - Keep the core messenger focused on the low-level, zero-allocation API.
    /// - Offer more expressive subscription helpers ("subscribe once", "subscribe when")
    ///   built on top of the core primitives.
    ///
    /// NOTE:
    /// - These helpers do allocate wrapper delegates to provide the additional behavior
    ///   (unsubscribe after first call, conditional invocation). That is usually
    ///   acceptable at the edges of the system, but core hot-path subscriptions
    ///   should still prefer the raw <c>MvcMessageBus.Subscribe</c> API.
    /// </summary>
    public static class MvcMessengerExtensions
    {
        // ONE-TIME SUBSCRIPTIONS
        // ----------------------
        // These helpers call the user handler only once and then automatically
        // unsubscribe using the returned SubscriptionToken. This is useful for
        // "fire-once" events like initialization or transitions.
        
        /// <summary>
        /// Subscribe a handler to <typeparamref name="TMessage"/> that will be invoked only once.
        /// After the first message is received, the subscription is automatically removed.
        /// </summary>
        public static SubscriptionToken SubscribeOnce<TMessage>(
            this MvcMessageBus messenger, 
            Action handler) 
            where TMessage : IMessage
        {
            if (messenger == null) throw new ArgumentNullException(nameof(messenger));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            
            SubscriptionToken token = default;   // will capture the token once we have it
            Action wrapper = null;               // wrapper delegate that unsubscribes itself
            
            wrapper = () =>
            {
                try
                {
                    // Forward the call to the original handler.
                    handler();
                }
                finally
                {
                    // Always unsubscribe, even if handler throws, to guarantee "once" semantics.
                    messenger.Unsubscribe<TMessage>(token);
                }
            };
            
            // Subscribe the wrapper instead of the original handler so we can
            // perform the auto-unsubscribe logic.
            token = messenger.Subscribe<TMessage>(wrapper);
            return token;
        }
        
        /// <summary>
        /// Subscribe a one-parameter handler to <typeparamref name="TMessage"/> that is invoked only once.
        /// </summary>
        public static SubscriptionToken SubscribeOnce<TMessage, T1>(
            this MvcMessageBus messenger, 
            Action<T1> handler) 
            where TMessage : IMessage<T1>
        {
            if (messenger == null) throw new ArgumentNullException(nameof(messenger));
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
                    messenger.Unsubscribe<TMessage, T1>(token);
                }
            };
            
            token = messenger.Subscribe<TMessage, T1>(wrapper);
            return token;
        }
        
        /// <summary>
        /// Subscribe a two-parameter handler to <typeparamref name="TMessage"/> that is invoked only once.
        /// </summary>
        public static SubscriptionToken SubscribeOnce<TMessage, T1, T2>(
            this MvcMessageBus messenger, 
            Action<T1, T2> handler) 
            where TMessage : IMessage<T1, T2>
        {
            if (messenger == null) throw new ArgumentNullException(nameof(messenger));
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
                    messenger.Unsubscribe<TMessage, T1, T2>(token);
                }
            };
            
            token = messenger.Subscribe<TMessage, T1, T2>(wrapper);
            return token;
        }
        
        /// <summary>
        /// Subscribe a three-parameter handler to <typeparamref name="TMessage"/> that is invoked only once.
        /// </summary>
        public static SubscriptionToken SubscribeOnce<TMessage, T1, T2, T3>(
            this MvcMessageBus messenger, 
            Action<T1, T2, T3> handler) 
            where TMessage : IMessage<T1, T2, T3>
        {
            if (messenger == null) throw new ArgumentNullException(nameof(messenger));
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
                    messenger.Unsubscribe<TMessage, T1, T2, T3>(token);
                }
            };
            
            token = messenger.Subscribe<TMessage, T1, T2, T3>(wrapper);
            return token;
        }
        
        /// <summary>
        /// Subscribe a four-parameter handler to <typeparamref name="TMessage"/> that is invoked only once.
        /// </summary>
        public static SubscriptionToken SubscribeOnce<TMessage, T1, T2, T3, T4>(
            this MvcMessageBus messenger, 
            Action<T1, T2, T3, T4> handler) 
            where TMessage : IMessage<T1, T2, T3, T4>
        {
            if (messenger == null) throw new ArgumentNullException(nameof(messenger));
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
                    messenger.Unsubscribe<TMessage, T1, T2, T3, T4>(token);
                }
            };
            
            token = messenger.Subscribe<TMessage, T1, T2, T3, T4>(wrapper);
            return token;
        }
        
        /// <summary>
        /// Subscribe a five-parameter handler to <typeparamref name="TMessage"/> that is invoked only once.
        /// </summary>
        public static SubscriptionToken SubscribeOnce<TMessage, T1, T2, T3, T4, T5>(
            this MvcMessageBus messenger, 
            Action<T1, T2, T3, T4, T5> handler) 
            where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            if (messenger == null) throw new ArgumentNullException(nameof(messenger));
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
                    messenger.Unsubscribe<TMessage, T1, T2, T3, T4, T5>(token);
                }
            };
            
            token = messenger.Subscribe<TMessage, T1, T2, T3, T4, T5>(wrapper);
            return token;
        }
        
        // CONDITIONAL SUBSCRIPTIONS
        // -------------------------
        // These helpers wrap the original handler with a condition delegate. On
        // each message, the condition is checked and the handler is only invoked
        // when it returns true. This is handy when your subscriber only cares
        // about messages while some state flag is active.
        
        /// <summary>
        /// Subscribe to <typeparamref name="TMessage"/> and invoke <paramref name="handler"/>
        /// only when <paramref name="condition"/> evaluates to <c>true</c>.
        /// </summary>
        public static SubscriptionToken SubscribeWhen<TMessage>(
            this MvcMessageBus messenger,
            Action handler,
            Func<bool> condition)
            where TMessage : IMessage
        {
            if (messenger == null) throw new ArgumentNullException(nameof(messenger));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            
            // Wrap the original handler so we can gate execution on the condition.
            return messenger.Subscribe<TMessage>(() =>
            {
                if (condition())
                {
                    handler();
                }
            });
        }
        
        /// <summary>
        /// Subscribe to <typeparamref name="TMessage"/> with one parameter and invoke
        /// <paramref name="handler"/> only when <paramref name="condition"/> is true.
        /// </summary>
        public static SubscriptionToken SubscribeWhen<TMessage, T1>(
            this MvcMessageBus messenger,
            Action<T1> handler,
            Func<bool> condition)
            where TMessage : IMessage<T1>
        {
            if (messenger == null) throw new ArgumentNullException(nameof(messenger));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            
            return messenger.Subscribe<TMessage, T1>((p1) =>
            {
                if (condition())
                {
                    handler(p1);
                }
            });
        }
        
        /// <summary>
        /// Subscribe to <typeparamref name="TMessage"/> with two parameters and invoke
        /// <paramref name="handler"/> only when <paramref name="condition"/> is true.
        /// </summary>
        public static SubscriptionToken SubscribeWhen<TMessage, T1, T2>(
            this MvcMessageBus messenger,
            Action<T1, T2> handler,
            Func<bool> condition)
            where TMessage : IMessage<T1, T2>
        {
            if (messenger == null) throw new ArgumentNullException(nameof(messenger));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            
            return messenger.Subscribe<TMessage, T1, T2>((p1, p2) =>
            {
                if (condition())
                {
                    handler(p1, p2);
                }
            });
        }
        
        /// <summary>
        /// Subscribe to <typeparamref name="TMessage"/> with three parameters and invoke
        /// <paramref name="handler"/> only when <paramref name="condition"/> is true.
        /// </summary>
        public static SubscriptionToken SubscribeWhen<TMessage, T1, T2, T3>(
            this MvcMessageBus messenger,
            Action<T1, T2, T3> handler,
            Func<bool> condition)
            where TMessage : IMessage<T1, T2, T3>
        {
            if (messenger == null) throw new ArgumentNullException(nameof(messenger));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            
            return messenger.Subscribe<TMessage, T1, T2, T3>((p1, p2, p3) =>
            {
                if (condition())
                {
                    handler(p1, p2, p3);
                }
            });
        }
        
        /// <summary>
        /// Subscribe to <typeparamref name="TMessage"/> with four parameters and invoke
        /// <paramref name="handler"/> only when <paramref name="condition"/> is true.
        /// </summary>
        public static SubscriptionToken SubscribeWhen<TMessage, T1, T2, T3, T4>(
            this MvcMessageBus messenger,
            Action<T1, T2, T3, T4> handler,
            Func<bool> condition)
            where TMessage : IMessage<T1, T2, T3, T4>
        {
            if (messenger == null) throw new ArgumentNullException(nameof(messenger));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            
            return messenger.Subscribe<TMessage, T1, T2, T3, T4>((p1, p2, p3, p4) =>
            {
                if (condition())
                {
                    handler(p1, p2, p3, p4);
                }
            });
        }
        
        /// <summary>
        /// Subscribe to <typeparamref name="TMessage"/> with five parameters and invoke
        /// <paramref name="handler"/> only when <paramref name="condition"/> is true.
        /// </summary>
        public static SubscriptionToken SubscribeWhen<TMessage, T1, T2, T3, T4, T5>(
            this MvcMessageBus messenger,
            Action<T1, T2, T3, T4, T5> handler,
            Func<bool> condition)
            where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            if (messenger == null) throw new ArgumentNullException(nameof(messenger));
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            
            return messenger.Subscribe<TMessage, T1, T2, T3, T4, T5>((p1, p2, p3, p4, p5) =>
            {
                if (condition())
                {
                    handler(p1, p2, p3, p4, p5);
                }
            });
        }
    }
}

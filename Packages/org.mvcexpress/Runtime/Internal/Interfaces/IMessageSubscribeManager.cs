using mvcExpress.Internal.Messaging;
using System;
using System.Runtime.CompilerServices;

namespace mvcExpress.Internal.Interfaces
{
    /// <summary>
    /// Manages message subscriptions for mediators.
    /// </summary>
    public interface IMessageSubscribeManager
    {
        #region Subscribe Methods

        /// <summary>Subscribe to a message with no parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage>(Action handler) where TMessage : IMessage;

        /// <summary>Subscribe to a message with 1 parameter.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1>(Action<T1> handler) where TMessage : IMessage<T1>;

        /// <summary>Subscribe to a message with 2 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2>(Action<T1, T2> handler) where TMessage : IMessage<T1, T2>;

        /// <summary>Subscribe to a message with 3 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3>(Action<T1, T2, T3> handler) where TMessage : IMessage<T1, T2, T3>;

        /// <summary>Subscribe to a message with 4 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4>(Action<T1, T2, T3, T4> handler) where TMessage : IMessage<T1, T2, T3, T4>;

        /// <summary>Subscribe to a message with 5 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> handler) where TMessage : IMessage<T1, T2, T3, T4, T5>;

        /// <summary>Subscribe to a message with 6 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4, T5, T6>(Action<T1, T2, T3, T4, T5, T6> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6>;

        /// <summary>Subscribe to a message with 7 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7>(Action<T1, T2, T3, T4, T5, T6, T7> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7>;

        /// <summary>Subscribe to a message with 8 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(Action<T1, T2, T3, T4, T5, T6, T7, T8> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8>;

        /// <summary>Subscribe to a message with 9 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9>;

        /// <summary>Subscribe to a message with 10 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>;

        /// <summary>Subscribe to a message with 11 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>;

        /// <summary>Subscribe to a message with 12 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>;

        #endregion

        #region Unsubscribe by Token

        /// <summary>Unsubscribe using a subscription token for a message with no parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage>(SubscriptionToken token) where TMessage : IMessage;

        /// <summary>Unsubscribe using a subscription token for a message with 1 parameter.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1>(SubscriptionToken token) where TMessage : IMessage<T1>;

        /// <summary>Unsubscribe using a subscription token for a message with 2 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2>(SubscriptionToken token) where TMessage : IMessage<T1, T2>;

        /// <summary>Unsubscribe using a subscription token for a message with 3 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3>;

        /// <summary>Unsubscribe using a subscription token for a message with 4 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3, T4>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4>;

        /// <summary>Unsubscribe using a subscription token for a message with 5 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4, T5>;

        /// <summary>Unsubscribe using a subscription token for a message with 6 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4, T5, T6>;

        /// <summary>Unsubscribe using a subscription token for a message with 7 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7>;

        /// <summary>Unsubscribe using a subscription token for a message with 8 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8>;

        /// <summary>Unsubscribe using a subscription token for a message with 9 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9>;

        /// <summary>Unsubscribe using a subscription token for a message with 10 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>;

        /// <summary>Unsubscribe using a subscription token for a message with 11 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>;

        /// <summary>Unsubscribe using a subscription token for a message with 12 parameters.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>;

        /// <summary>Unsubscribe using a handler for a message with no parameters.</summary>
        public void Unsubscribe<TMessage>(Action handler) where TMessage : IMessage;

        /// <summary>Unsubscribe using a handler for a message with 1 parameter.</summary>
        public void Unsubscribe<TMessage, T1>(Action<T1> handler) where TMessage : IMessage<T1>;

        /// <summary>Unsubscribe using a handler for a message with 2 parameters.</summary>
        public void Unsubscribe<TMessage, T1, T2>(Action<T1, T2> handler) where TMessage : IMessage<T1, T2>;

        /// <summary>Unsubscribe using a handler for a message with 3 parameters.</summary>
        public void Unsubscribe<TMessage, T1, T2, T3>(Action<T1, T2, T3> handler) where TMessage : IMessage<T1, T2, T3>;

        /// <summary>Unsubscribe using a handler for a message with 4 parameters.</summary>
        public void Unsubscribe<TMessage, T1, T2, T3, T4>(Action<T1, T2, T3, T4> handler) where TMessage : IMessage<T1, T2, T3, T4>;

        /// <summary>Unsubscribe using a handler for a message with 5 parameters.</summary>
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> handler) where TMessage : IMessage<T1, T2, T3, T4, T5>;

        /// <summary>Unsubscribe using a handler for a message with 6 parameters.</summary>
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6>(Action<T1, T2, T3, T4, T5, T6> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6>;

        /// <summary>Unsubscribe using a handler for a message with 7 parameters.</summary>
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7>(Action<T1, T2, T3, T4, T5, T6, T7> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7>;

        /// <summary>Unsubscribe using a handler for a message with 8 parameters.</summary>
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(Action<T1, T2, T3, T4, T5, T6, T7, T8> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8>;

        /// <summary>Unsubscribe using a handler for a message with 9 parameters.</summary>
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9>;

        /// <summary>Unsubscribe using a handler for a message with 10 parameters.</summary>
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>;

        /// <summary>Unsubscribe using a handler for a message with 11 parameters.</summary>
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>;

        /// <summary>Unsubscribe using a handler for a message with 12 parameters.</summary>
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>;

        #endregion

        #region UnsubscribeAll Methods

        /// <summary>Remove all subscriptions for a message with no parameters.</summary>
        public void UnsubscribeAll<TMessage>() where TMessage : IMessage;

        /// <summary>Remove all subscriptions for a message with 1 parameter.</summary>
        public void UnsubscribeAll<TMessage, T1>() where TMessage : IMessage<T1>;

        /// <summary>Remove all subscriptions for a message with 2 parameters.</summary>
        public void UnsubscribeAll<TMessage, T1, T2>() where TMessage : IMessage<T1, T2>;

        /// <summary>Remove all subscriptions for a message with 3 parameters.</summary>
        public void UnsubscribeAll<TMessage, T1, T2, T3>() where TMessage : IMessage<T1, T2, T3>;

        /// <summary>Remove all subscriptions for a message with 4 parameters.</summary>
        public void UnsubscribeAll<TMessage, T1, T2, T3, T4>() where TMessage : IMessage<T1, T2, T3, T4>;

        /// <summary>Remove all subscriptions for a message with 5 parameters.</summary>
        public void UnsubscribeAll<TMessage, T1, T2, T3, T4, T5>() where TMessage : IMessage<T1, T2, T3, T4, T5>;

        /// <summary>Remove all subscriptions for a message with 6 parameters.</summary>
        public void UnsubscribeAll<TMessage, T1, T2, T3, T4, T5, T6>() where TMessage : IMessage<T1, T2, T3, T4, T5, T6>;

        /// <summary>Remove all subscriptions for a message with 7 parameters.</summary>
        public void UnsubscribeAll<TMessage, T1, T2, T3, T4, T5, T6, T7>() where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7>;

        /// <summary>Remove all subscriptions for a message with 8 parameters.</summary>
        public void UnsubscribeAll<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>() where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8>;

        /// <summary>Remove all subscriptions for a message with 9 parameters.</summary>
        public void UnsubscribeAll<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>() where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9>;

        /// <summary>Remove all subscriptions for a message with 10 parameters.</summary>
        public void UnsubscribeAll<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>() where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>;

        /// <summary>Remove all subscriptions for a message with 11 parameters.</summary>
        public void UnsubscribeAll<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>() where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>;

        /// <summary>Remove all subscriptions for a message with 12 parameters.</summary>
        public void UnsubscribeAll<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>() where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>;

        #endregion

    }
}
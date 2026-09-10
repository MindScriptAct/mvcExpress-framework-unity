using mvcExpress;
using System.Runtime.CompilerServices;

namespace mvcExpress.Internal.Interfaces
{
    /// <summary>
    /// Publishes messages on the single application-wide message bus.
    /// </summary>
    /// <remarks>
    /// One application has exactly one message bus, shared by every module and actor. Every publish
    /// broadcasts to all matching subscribers in the application; this interface does not provide
    /// module-local scope, target-module delivery, message bridges, or per-module filtering.
    /// </remarks>
    public interface IMessagePublisher
    {
        // ==================== Application-wide Publish ====================

        /// <summary>Publishes a message without payload to all matching subscribers in the application.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Publish<TMessage>() where TMessage : IMessage;

        /// <summary>Publish a message with one payload value.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Publish<TMessage, T1>(T1 p1) where TMessage : IMessage<T1>;

        /// <summary>Publish a message with two payload values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Publish<TMessage, T1, T2>(T1 p1, T2 p2) where TMessage : IMessage<T1, T2>;

        /// <summary>Publish a message with three payload values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Publish<TMessage, T1, T2, T3>(T1 p1, T2 p2, T3 p3) where TMessage : IMessage<T1, T2, T3>;

        /// <summary>Publish a message with four payload values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Publish<TMessage, T1, T2, T3, T4>(T1 p1, T2 p2, T3 p3, T4 p4) where TMessage : IMessage<T1, T2, T3, T4>;

        /// <summary>Publish a message with five payload values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Publish<TMessage, T1, T2, T3, T4, T5>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5) where TMessage : IMessage<T1, T2, T3, T4, T5>;

        /// <summary>Publish a message with six payload values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Publish<TMessage, T1, T2, T3, T4, T5, T6>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6) where TMessage : IMessage<T1, T2, T3, T4, T5, T6>;

        /// <summary>Publish a message with seven payload values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Publish<TMessage, T1, T2, T3, T4, T5, T6, T7>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7>;

        /// <summary>Publish a message with eight payload values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8>;

        /// <summary>Publish a message with nine payload values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9>;

        /// <summary>Publish a message with ten payload values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>;

        /// <summary>Publish a message with eleven payload values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10, T11 p11) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>;

        /// <summary>Publish a message with twelve payload values.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        void Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p101, T11 p11, T12 p12) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>;

    }
}

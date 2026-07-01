using System;
using System.Collections.Generic;
using System.Text;

namespace mvcExpress
{
    /// <summary>
    /// Base marker for all message declarations.
    /// </summary>
    /// <remarks>
    /// Useful for metadata and introspection APIs (e.g., the MvcConsole, binding validators)
    /// that need to identify any message type regardless of payload arity. Application code
    /// should implement the typed <see cref="IMessage"/> or <c>IMessage&lt;T1&gt;</c> variants
    /// rather than this base interface directly.
    /// </remarks>
    public interface IMessageBase { }

    /// <summary>
    /// Contract for a message with no payload, published on the module's internal message bus.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Messages are the only sanctioned way to communicate between framework actors
    /// (Services, Proxies, Commands, and Mediators). A publisher calls
    /// <c>Publish(new MyMessage())</c> and every <c>Subscribe&lt;MyMessage&gt;</c> subscriber
    /// is notified synchronously in subscription order.
    /// </para>
    /// <para><b>Struct vs class trade-off:</b><br/>
    /// Implement messages as <c>readonly struct</c> for high-frequency events - the framework
    /// passes them by value so there is zero heap allocation per publish.
    /// Use a <c>class</c> only when the message carries large payload that would be expensive
    /// to copy, accepting the GC cost.
    /// </para>
    /// <para><b>Scope:</b> The message bus is application-wide. A message published anywhere
    /// is delivered to every subscriber in every module simultaneously. There is no per-module
    /// filtering; cross-module communication requires no special mechanism beyond publishing
    /// the message on the shared bus.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Zero-payload signal - fire-and-forget event, no allocation when a struct
    /// public readonly struct GameStartedMessage : IMessage { }
    ///
    /// // Publisher (Service, Proxy, Command, or Module)
    /// Publish(new GameStartedMessage());
    ///
    /// // Subscriber (MediatorBehaviour.OnInitialized)
    /// Subscribe&lt;GameStartedMessage&gt;(OnGameStarted);
    /// private void OnGameStarted(GameStartedMessage msg) { /* react */ }
    /// </code>
    /// </example>
    public interface IMessage : IMessageBase { }

    /// <summary>
    /// Contract for a message with 1 typed payload value.
    /// </summary>
    /// <remarks>
    /// Prefer a <c>readonly struct</c> to avoid GC allocations on publish.
    /// </remarks>
    /// <typeparam name="T1">Type of the first payload value.</typeparam>
    /// <example>
    /// <code>
    /// public readonly struct ScoreChangedMessage : IMessage&lt;int&gt;
    /// {
    ///     public readonly int NewScore;
    ///     public ScoreChangedMessage(int newScore) => NewScore = newScore;
    /// }
    ///
    /// Publish(new ScoreChangedMessage(100));
    /// Subscribe&lt;ScoreChangedMessage&gt;(OnScoreChanged);
    /// </code>
    /// </example>
    public interface IMessage<T1> : IMessageBase { }

    /// <summary>Message contract with 2 typed payload values.</summary>
    /// <typeparam name="T1">Type of the first payload value.</typeparam>
    /// <typeparam name="T2">Type of the second payload value.</typeparam>
    public interface IMessage<T1, T2> : IMessageBase { }

    /// <summary>Message contract with 3 typed payload values.</summary>
    /// <typeparam name="T1">Type of the first payload value.</typeparam>
    /// <typeparam name="T2">Type of the second payload value.</typeparam>
    /// <typeparam name="T3">Type of the third payload value.</typeparam>
    public interface IMessage<T1, T2, T3> : IMessageBase { }

    /// <summary>Message contract with 4 typed payload values.</summary>
    /// <typeparam name="T1">Type of the first payload value.</typeparam>
    /// <typeparam name="T2">Type of the second payload value.</typeparam>
    /// <typeparam name="T3">Type of the third payload value.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload value.</typeparam>
    public interface IMessage<T1, T2, T3, T4> : IMessageBase { }

    /// <summary>Message contract with 5 typed payload values.</summary>
    /// <typeparam name="T1">Type of the first payload value.</typeparam>
    /// <typeparam name="T2">Type of the second payload value.</typeparam>
    /// <typeparam name="T3">Type of the third payload value.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload value.</typeparam>
    /// <typeparam name="T5">Type of the fifth payload value.</typeparam>
    public interface IMessage<T1, T2, T3, T4, T5> : IMessageBase { }

    /// <summary>Message contract with 6 typed payload values.</summary>
    /// <typeparam name="T1">Type of the first payload value.</typeparam>
    /// <typeparam name="T2">Type of the second payload value.</typeparam>
    /// <typeparam name="T3">Type of the third payload value.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload value.</typeparam>
    /// <typeparam name="T5">Type of the fifth payload value.</typeparam>
    /// <typeparam name="T6">Type of the sixth payload value.</typeparam>
    public interface IMessage<T1, T2, T3, T4, T5, T6> : IMessageBase { }

    /// <summary>Message contract with 7 typed payload values.</summary>
    /// <typeparam name="T1">Type of the first payload value.</typeparam>
    /// <typeparam name="T2">Type of the second payload value.</typeparam>
    /// <typeparam name="T3">Type of the third payload value.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload value.</typeparam>
    /// <typeparam name="T5">Type of the fifth payload value.</typeparam>
    /// <typeparam name="T6">Type of the sixth payload value.</typeparam>
    /// <typeparam name="T7">Type of the seventh payload value.</typeparam>
    public interface IMessage<T1, T2, T3, T4, T5, T6, T7> : IMessageBase { }

    /// <summary>Message contract with 8 typed payload values.</summary>
    /// <typeparam name="T1">Type of the first payload value.</typeparam>
    /// <typeparam name="T2">Type of the second payload value.</typeparam>
    /// <typeparam name="T3">Type of the third payload value.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload value.</typeparam>
    /// <typeparam name="T5">Type of the fifth payload value.</typeparam>
    /// <typeparam name="T6">Type of the sixth payload value.</typeparam>
    /// <typeparam name="T7">Type of the seventh payload value.</typeparam>
    /// <typeparam name="T8">Type of the eighth payload value.</typeparam>
    public interface IMessage<T1, T2, T3, T4, T5, T6, T7, T8> : IMessageBase { }

    /// <summary>Message contract with 9 typed payload values.</summary>
    /// <typeparam name="T1">Type of the first payload value.</typeparam>
    /// <typeparam name="T2">Type of the second payload value.</typeparam>
    /// <typeparam name="T3">Type of the third payload value.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload value.</typeparam>
    /// <typeparam name="T5">Type of the fifth payload value.</typeparam>
    /// <typeparam name="T6">Type of the sixth payload value.</typeparam>
    /// <typeparam name="T7">Type of the seventh payload value.</typeparam>
    /// <typeparam name="T8">Type of the eighth payload value.</typeparam>
    /// <typeparam name="T9">Type of the ninth payload value.</typeparam>
    public interface IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9> : IMessageBase { }

    /// <summary>Message contract with 10 typed payload values.</summary>
    /// <typeparam name="T1">Type of the first payload value.</typeparam>
    /// <typeparam name="T2">Type of the second payload value.</typeparam>
    /// <typeparam name="T3">Type of the third payload value.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload value.</typeparam>
    /// <typeparam name="T5">Type of the fifth payload value.</typeparam>
    /// <typeparam name="T6">Type of the sixth payload value.</typeparam>
    /// <typeparam name="T7">Type of the seventh payload value.</typeparam>
    /// <typeparam name="T8">Type of the eighth payload value.</typeparam>
    /// <typeparam name="T9">Type of the ninth payload value.</typeparam>
    /// <typeparam name="T10">Type of the tenth payload value.</typeparam>
    public interface IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> : IMessageBase { }

    /// <summary>Message contract with 11 typed payload values.</summary>
    /// <typeparam name="T1">Type of the first payload value.</typeparam>
    /// <typeparam name="T2">Type of the second payload value.</typeparam>
    /// <typeparam name="T3">Type of the third payload value.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload value.</typeparam>
    /// <typeparam name="T5">Type of the fifth payload value.</typeparam>
    /// <typeparam name="T6">Type of the sixth payload value.</typeparam>
    /// <typeparam name="T7">Type of the seventh payload value.</typeparam>
    /// <typeparam name="T8">Type of the eighth payload value.</typeparam>
    /// <typeparam name="T9">Type of the ninth payload value.</typeparam>
    /// <typeparam name="T10">Type of the tenth payload value.</typeparam>
    /// <typeparam name="T11">Type of the eleventh payload value.</typeparam>
    public interface IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> : IMessageBase { }

    /// <summary>Message contract with 12 typed payload values.</summary>
    /// <typeparam name="T1">Type of the first payload value.</typeparam>
    /// <typeparam name="T2">Type of the second payload value.</typeparam>
    /// <typeparam name="T3">Type of the third payload value.</typeparam>
    /// <typeparam name="T4">Type of the fourth payload value.</typeparam>
    /// <typeparam name="T5">Type of the fifth payload value.</typeparam>
    /// <typeparam name="T6">Type of the sixth payload value.</typeparam>
    /// <typeparam name="T7">Type of the seventh payload value.</typeparam>
    /// <typeparam name="T8">Type of the eighth payload value.</typeparam>
    /// <typeparam name="T9">Type of the ninth payload value.</typeparam>
    /// <typeparam name="T10">Type of the tenth payload value.</typeparam>
    /// <typeparam name="T11">Type of the eleventh payload value.</typeparam>
    /// <typeparam name="T12">Type of the twelfth payload value.</typeparam>
    public interface IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> : IMessageBase { }
}

using mvcExpress.Internal.Interfaces;
using mvcExpress.Internal.Messaging;
using mvcExpress.Logging;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using mvcExpress.Plugins;
#endif

namespace mvcExpress
{
    /// <summary>
    /// Bidirectional message bus facade for <see cref="MediatorBehaviour"/>: publish view events
    /// upward and subscribe to module messages downward.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Mediators are the view bridge, so they need both directions:
    /// <list type="bullet">
    ///   <item><b>Publish</b> - translate Unity UI events (button presses, gesture recognizers,
    ///   etc.) into framework messages that Services, Proxies, or Commands can react to.</item>
    ///   <item><b>Subscribe</b> - react to model-state changes published by Services or Proxies
    ///   (e.g. <c>ScoreChangedMessage</c>) and update the view accordingly.</item>
    /// </list>
    /// </para>
    /// <para>
    /// All subscriptions made through this API are tracked by the mediator's
    /// <c>SubscriptionTracker</c> and removed automatically when the mediator is detached or
    /// destroyed, so you do not need to <c>Unsubscribe</c> manually in <c>OnCleanup</c>
    /// unless you want to stop listening while the mediator is still alive.
    /// </para>
    /// <para>
    /// For non-mediator actors (Services, Proxies, Commands) use <see cref="MessengerApi"/>
    /// instead - it is publish-only and carries no subscription overhead.
    /// </para>
    /// </remarks>
    public readonly struct MediatorMessengerApi
    {
        // Actor context provides log metadata and the owning mediator reference.
        private readonly MvcActorContext _context;

        // Constructed by the framework when wiring up the mediator; not part of the public API.
        internal MediatorMessengerApi(MvcActorContext context)
        {
            _context = context;
        }

        // Typed cast; safe because this struct is only created for MediatorBehaviour actors.
        private MediatorBehaviour Mediator => (MediatorBehaviour)_context.Actor;
        // Direct bus access from the mediator for both publish and subscribe paths.
        // Concrete MvcMessageBus (not IMessageBus) so every call below is a direct call the
        // JIT/IL2CPP can inline/devirtualize, not an interface dispatch.
        private MvcMessageBus Bus => Mediator.MessageBus;

        /// <summary>
        /// Publishes a no-payload message from the mediator to the app-wide message bus.
        /// </summary>
        /// <typeparam name="TMessage">
        /// Marker type identifying the event. Must implement <see cref="IMessage"/>.
        /// Prefer a <c>struct</c> to avoid allocations.
        /// </typeparam>
        /// <remarks>
        /// Caller attributes are compiler-filled for diagnostics; do not pass them manually.
        /// Typical use: translating a button-click Unity event into a framework message.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage>(
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
            where TMessage : IMessage
        {
            MvcLogInternal.LogMessagePublished<TMessage>(_context.Actor, _context.ResolveModule(), filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                Bus.Publish<TMessage>();
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            Bus.Publish<TMessage>();
#endif
        }

        /// <summary>
        /// Publishes a message with one payload value from the mediator to the app-wide message bus.
        /// </summary>
        /// <typeparam name="TMessage">Marker type identifying the event. Must implement <see cref="IMessage{T1}"/>.</typeparam>
        /// <typeparam name="T1">Payload value type.</typeparam>
        /// <param name="p1">Payload value forwarded to every subscriber.</param>
        /// <param name="filePath">Do not pass; compiler-filled source file path for diagnostics.</param>
        /// <param name="lineNumber">Do not pass; compiler-filled line number for diagnostics.</param>
        /// <param name="memberName">Do not pass; compiler-filled member name for diagnostics.</param>
        /// <remarks>Caller attributes are compiler-filled for diagnostics; do not pass them manually.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage, T1>(
            T1 p1,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
            where TMessage : IMessage<T1>
        {
            MvcLogInternal.LogMessagePublished<TMessage>(_context.Actor, _context.ResolveModule(), filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                Bus.Publish<TMessage, T1>(p1);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            Bus.Publish<TMessage, T1>(p1);
#endif
        }

        /// <summary>
        /// Publishes a message with two payload values from the mediator to the app-wide message bus.
        /// </summary>
        /// <typeparam name="TMessage">
        /// Marker type that identifies the event. Must implement <see cref="IMessage{T1, T2}"/>.
        /// Prefer a <c>struct</c> to avoid allocations.
        /// </typeparam>
        /// <typeparam name="T1">First payload value type.</typeparam>
        /// <typeparam name="T2">Second payload value type.</typeparam>
        /// <param name="p1">First payload value forwarded to every subscriber.</param>
        /// <param name="p2">Second payload value forwarded to every subscriber.</param>
        /// <param name="filePath">Do not pass; compiler-filled source file path for diagnostics.</param>
        /// <param name="lineNumber">Do not pass; compiler-filled line number for diagnostics.</param>
        /// <param name="memberName">Do not pass; compiler-filled member name for diagnostics.</param>
        /// <remarks>Caller attributes are compiler-filled for diagnostics; do not pass them manually.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage, T1, T2>(
            T1 p1,
            T2 p2,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
            where TMessage : IMessage<T1, T2>
        {
            MvcLogInternal.LogMessagePublished<TMessage>(_context.Actor, _context.ResolveModule(), filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                Bus.Publish<TMessage, T1, T2>(p1, p2);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            Bus.Publish<TMessage, T1, T2>(p1, p2);
#endif
        }

        /// <summary>
        /// Publishes a message with three payload values from the mediator to the app-wide message bus.
        /// </summary>
        /// <typeparam name="TMessage">
        /// Marker type that identifies the event. Must implement <see cref="IMessage{T1, T2, T3}"/>.
        /// Prefer a <c>struct</c> to avoid allocations.
        /// </typeparam>
        /// <typeparam name="T1">First payload value type.</typeparam>
        /// <typeparam name="T2">Second payload value type.</typeparam>
        /// <typeparam name="T3">Third payload value type.</typeparam>
        /// <param name="p1">First payload value forwarded to every subscriber.</param>
        /// <param name="p2">Second payload value forwarded to every subscriber.</param>
        /// <param name="p3">Third payload value forwarded to every subscriber.</param>
        /// <param name="filePath">Do not pass; compiler-filled source file path for diagnostics.</param>
        /// <param name="lineNumber">Do not pass; compiler-filled line number for diagnostics.</param>
        /// <param name="memberName">Do not pass; compiler-filled member name for diagnostics.</param>
        /// <remarks>Caller attributes are compiler-filled for diagnostics; do not pass them manually.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage, T1, T2, T3>(
            T1 p1,
            T2 p2,
            T3 p3,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
            where TMessage : IMessage<T1, T2, T3>
        {
            MvcLogInternal.LogMessagePublished<TMessage>(_context.Actor, _context.ResolveModule(), filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                Bus.Publish<TMessage, T1, T2, T3>(p1, p2, p3);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            Bus.Publish<TMessage, T1, T2, T3>(p1, p2, p3);
#endif
        }

        /// <summary>
        /// Publishes a message with four payload values from the mediator to the app-wide message bus.
        /// </summary>
        /// <typeparam name="TMessage">
        /// Marker type that identifies the event. Must implement <see cref="IMessage{T1, T2, T3, T4}"/>.
        /// Prefer a <c>struct</c> to avoid allocations.
        /// </typeparam>
        /// <typeparam name="T1">First payload value type.</typeparam>
        /// <typeparam name="T2">Second payload value type.</typeparam>
        /// <typeparam name="T3">Third payload value type.</typeparam>
        /// <typeparam name="T4">Fourth payload value type.</typeparam>
        /// <param name="p1">First payload value forwarded to every subscriber.</param>
        /// <param name="p2">Second payload value forwarded to every subscriber.</param>
        /// <param name="p3">Third payload value forwarded to every subscriber.</param>
        /// <param name="p4">Fourth payload value forwarded to every subscriber.</param>
        /// <param name="filePath">Do not pass; compiler-filled source file path for diagnostics.</param>
        /// <param name="lineNumber">Do not pass; compiler-filled line number for diagnostics.</param>
        /// <param name="memberName">Do not pass; compiler-filled member name for diagnostics.</param>
        /// <remarks>Caller attributes are compiler-filled for diagnostics; do not pass them manually.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage, T1, T2, T3, T4>(
            T1 p1,
            T2 p2,
            T3 p3,
            T4 p4,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
            where TMessage : IMessage<T1, T2, T3, T4>
        {
            MvcLogInternal.LogMessagePublished<TMessage>(_context.Actor, _context.ResolveModule(), filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                Bus.Publish<TMessage, T1, T2, T3, T4>(p1, p2, p3, p4);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            Bus.Publish<TMessage, T1, T2, T3, T4>(p1, p2, p3, p4);
#endif
        }

        /// <summary>
        /// Publishes a message with five payload values from the mediator to the app-wide message bus.
        /// </summary>
        /// <typeparam name="TMessage">
        /// Marker type that identifies the event. Must implement <see cref="IMessage{T1, T2, T3, T4, T5}"/>.
        /// Prefer a <c>struct</c> to avoid allocations.
        /// </typeparam>
        /// <typeparam name="T1">First payload value type.</typeparam>
        /// <typeparam name="T2">Second payload value type.</typeparam>
        /// <typeparam name="T3">Third payload value type.</typeparam>
        /// <typeparam name="T4">Fourth payload value type.</typeparam>
        /// <typeparam name="T5">Fifth payload value type.</typeparam>
        /// <param name="p1">First payload value forwarded to every subscriber.</param>
        /// <param name="p2">Second payload value forwarded to every subscriber.</param>
        /// <param name="p3">Third payload value forwarded to every subscriber.</param>
        /// <param name="p4">Fourth payload value forwarded to every subscriber.</param>
        /// <param name="p5">Fifth payload value forwarded to every subscriber.</param>
        /// <param name="filePath">Do not pass; compiler-filled source file path for diagnostics.</param>
        /// <param name="lineNumber">Do not pass; compiler-filled line number for diagnostics.</param>
        /// <param name="memberName">Do not pass; compiler-filled member name for diagnostics.</param>
        /// <remarks>Caller attributes are compiler-filled for diagnostics; do not pass them manually.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage, T1, T2, T3, T4, T5>(
            T1 p1,
            T2 p2,
            T3 p3,
            T4 p4,
            T5 p5,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
            where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            MvcLogInternal.LogMessagePublished<TMessage>(_context.Actor, _context.ResolveModule(), filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                Bus.Publish<TMessage, T1, T2, T3, T4, T5>(p1, p2, p3, p4, p5);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            Bus.Publish<TMessage, T1, T2, T3, T4, T5>(p1, p2, p3, p4, p5);
#endif
        }

        /// <summary>
        /// Publishes a message with six payload values from the mediator to the app-wide message bus.
        /// </summary>
        /// <typeparam name="TMessage">
        /// Marker type that identifies the event. Must implement <see cref="IMessage{T1, T2, T3, T4, T5, T6}"/>.
        /// Prefer a <c>struct</c> to avoid allocations.
        /// </typeparam>
        /// <typeparam name="T1">First payload value type.</typeparam>
        /// <typeparam name="T2">Second payload value type.</typeparam>
        /// <typeparam name="T3">Third payload value type.</typeparam>
        /// <typeparam name="T4">Fourth payload value type.</typeparam>
        /// <typeparam name="T5">Fifth payload value type.</typeparam>
        /// <typeparam name="T6">Sixth payload value type.</typeparam>
        /// <param name="p1">First payload value forwarded to every subscriber.</param>
        /// <param name="p2">Second payload value forwarded to every subscriber.</param>
        /// <param name="p3">Third payload value forwarded to every subscriber.</param>
        /// <param name="p4">Fourth payload value forwarded to every subscriber.</param>
        /// <param name="p5">Fifth payload value forwarded to every subscriber.</param>
        /// <param name="p6">Sixth payload value forwarded to every subscriber.</param>
        /// <param name="filePath">Do not pass; compiler-filled source file path for diagnostics.</param>
        /// <param name="lineNumber">Do not pass; compiler-filled line number for diagnostics.</param>
        /// <param name="memberName">Do not pass; compiler-filled member name for diagnostics.</param>
        /// <remarks>Caller attributes are compiler-filled for diagnostics; do not pass them manually.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage, T1, T2, T3, T4, T5, T6>(
            T1 p1,
            T2 p2,
            T3 p3,
            T4 p4,
            T5 p5,
            T6 p6,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6>
        {
            MvcLogInternal.LogMessagePublished<TMessage>(_context.Actor, _context.ResolveModule(), filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                Bus.Publish<TMessage, T1, T2, T3, T4, T5, T6>(p1, p2, p3, p4, p5, p6);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            Bus.Publish<TMessage, T1, T2, T3, T4, T5, T6>(p1, p2, p3, p4, p5, p6);
#endif
        }

        /// <summary>
        /// Publishes a message with seven payload values from the mediator to the app-wide message bus.
        /// </summary>
        /// <typeparam name="TMessage">
        /// Marker type that identifies the event. Must implement <see cref="IMessage{T1, T2, T3, T4, T5, T6, T7}"/>.
        /// Prefer a <c>struct</c> to avoid allocations.
        /// </typeparam>
        /// <typeparam name="T1">First payload value type.</typeparam>
        /// <typeparam name="T2">Second payload value type.</typeparam>
        /// <typeparam name="T3">Third payload value type.</typeparam>
        /// <typeparam name="T4">Fourth payload value type.</typeparam>
        /// <typeparam name="T5">Fifth payload value type.</typeparam>
        /// <typeparam name="T6">Sixth payload value type.</typeparam>
        /// <typeparam name="T7">Seventh payload value type.</typeparam>
        /// <param name="p1">First payload value forwarded to every subscriber.</param>
        /// <param name="p2">Second payload value forwarded to every subscriber.</param>
        /// <param name="p3">Third payload value forwarded to every subscriber.</param>
        /// <param name="p4">Fourth payload value forwarded to every subscriber.</param>
        /// <param name="p5">Fifth payload value forwarded to every subscriber.</param>
        /// <param name="p6">Sixth payload value forwarded to every subscriber.</param>
        /// <param name="p7">Seventh payload value forwarded to every subscriber.</param>
        /// <param name="filePath">Do not pass; compiler-filled source file path for diagnostics.</param>
        /// <param name="lineNumber">Do not pass; compiler-filled line number for diagnostics.</param>
        /// <param name="memberName">Do not pass; compiler-filled member name for diagnostics.</param>
        /// <remarks>Caller attributes are compiler-filled for diagnostics; do not pass them manually.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage, T1, T2, T3, T4, T5, T6, T7>(
            T1 p1,
            T2 p2,
            T3 p3,
            T4 p4,
            T5 p5,
            T6 p6,
            T7 p7,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7>
        {
            MvcLogInternal.LogMessagePublished<TMessage>(_context.Actor, _context.ResolveModule(), filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                Bus.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7>(p1, p2, p3, p4, p5, p6, p7);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            Bus.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7>(p1, p2, p3, p4, p5, p6, p7);
#endif
        }

        /// <summary>
        /// Publishes a message with eight payload values from the mediator to the app-wide message bus.
        /// </summary>
        /// <typeparam name="TMessage">
        /// Marker type that identifies the event. Must implement <see cref="IMessage{T1, T2, T3, T4, T5, T6, T7, T8}"/>.
        /// Prefer a <c>struct</c> to avoid allocations.
        /// </typeparam>
        /// <typeparam name="T1">First payload value type.</typeparam>
        /// <typeparam name="T2">Second payload value type.</typeparam>
        /// <typeparam name="T3">Third payload value type.</typeparam>
        /// <typeparam name="T4">Fourth payload value type.</typeparam>
        /// <typeparam name="T5">Fifth payload value type.</typeparam>
        /// <typeparam name="T6">Sixth payload value type.</typeparam>
        /// <typeparam name="T7">Seventh payload value type.</typeparam>
        /// <typeparam name="T8">Eighth payload value type.</typeparam>
        /// <param name="p1">First payload value forwarded to every subscriber.</param>
        /// <param name="p2">Second payload value forwarded to every subscriber.</param>
        /// <param name="p3">Third payload value forwarded to every subscriber.</param>
        /// <param name="p4">Fourth payload value forwarded to every subscriber.</param>
        /// <param name="p5">Fifth payload value forwarded to every subscriber.</param>
        /// <param name="p6">Sixth payload value forwarded to every subscriber.</param>
        /// <param name="p7">Seventh payload value forwarded to every subscriber.</param>
        /// <param name="p8">Eighth payload value forwarded to every subscriber.</param>
        /// <param name="filePath">Do not pass; compiler-filled source file path for diagnostics.</param>
        /// <param name="lineNumber">Do not pass; compiler-filled line number for diagnostics.</param>
        /// <param name="memberName">Do not pass; compiler-filled member name for diagnostics.</param>
        /// <remarks>Caller attributes are compiler-filled for diagnostics; do not pass them manually.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(
            T1 p1,
            T2 p2,
            T3 p3,
            T4 p4,
            T5 p5,
            T6 p6,
            T7 p7,
            T8 p8,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8>
        {
            MvcLogInternal.LogMessagePublished<TMessage>(_context.Actor, _context.ResolveModule(), filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                Bus.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(p1, p2, p3, p4, p5, p6, p7, p8);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            Bus.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(p1, p2, p3, p4, p5, p6, p7, p8);
#endif
        }

        /// <summary>
        /// Publishes a message with nine payload values from the mediator to the app-wide message bus.
        /// </summary>
        /// <typeparam name="TMessage">
        /// Marker type that identifies the event. Must implement <see cref="IMessage{T1, T2, T3, T4, T5, T6, T7, T8, T9}"/>.
        /// Prefer a <c>struct</c> to avoid allocations.
        /// </typeparam>
        /// <typeparam name="T1">First payload value type.</typeparam>
        /// <typeparam name="T2">Second payload value type.</typeparam>
        /// <typeparam name="T3">Third payload value type.</typeparam>
        /// <typeparam name="T4">Fourth payload value type.</typeparam>
        /// <typeparam name="T5">Fifth payload value type.</typeparam>
        /// <typeparam name="T6">Sixth payload value type.</typeparam>
        /// <typeparam name="T7">Seventh payload value type.</typeparam>
        /// <typeparam name="T8">Eighth payload value type.</typeparam>
        /// <typeparam name="T9">Ninth payload value type.</typeparam>
        /// <param name="p1">First payload value forwarded to every subscriber.</param>
        /// <param name="p2">Second payload value forwarded to every subscriber.</param>
        /// <param name="p3">Third payload value forwarded to every subscriber.</param>
        /// <param name="p4">Fourth payload value forwarded to every subscriber.</param>
        /// <param name="p5">Fifth payload value forwarded to every subscriber.</param>
        /// <param name="p6">Sixth payload value forwarded to every subscriber.</param>
        /// <param name="p7">Seventh payload value forwarded to every subscriber.</param>
        /// <param name="p8">Eighth payload value forwarded to every subscriber.</param>
        /// <param name="p9">Ninth payload value forwarded to every subscriber.</param>
        /// <param name="filePath">Do not pass; compiler-filled source file path for diagnostics.</param>
        /// <param name="lineNumber">Do not pass; compiler-filled line number for diagnostics.</param>
        /// <param name="memberName">Do not pass; compiler-filled member name for diagnostics.</param>
        /// <remarks>Caller attributes are compiler-filled for diagnostics; do not pass them manually.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
            T1 p1,
            T2 p2,
            T3 p3,
            T4 p4,
            T5 p5,
            T6 p6,
            T7 p7,
            T8 p8,
            T9 p9,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9>
        {
            MvcLogInternal.LogMessagePublished<TMessage>(_context.Actor, _context.ResolveModule(), filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                Bus.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(p1, p2, p3, p4, p5, p6, p7, p8, p9);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            Bus.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(p1, p2, p3, p4, p5, p6, p7, p8, p9);
#endif
        }

        /// <summary>
        /// Publishes a message with ten payload values from the mediator to the app-wide message bus.
        /// </summary>
        /// <typeparam name="TMessage">
        /// Marker type that identifies the event. Must implement <see cref="IMessage{T1, T2, T3, T4, T5, T6, T7, T8, T9, T10}"/>.
        /// Prefer a <c>struct</c> to avoid allocations.
        /// </typeparam>
        /// <typeparam name="T1">First payload value type.</typeparam>
        /// <typeparam name="T2">Second payload value type.</typeparam>
        /// <typeparam name="T3">Third payload value type.</typeparam>
        /// <typeparam name="T4">Fourth payload value type.</typeparam>
        /// <typeparam name="T5">Fifth payload value type.</typeparam>
        /// <typeparam name="T6">Sixth payload value type.</typeparam>
        /// <typeparam name="T7">Seventh payload value type.</typeparam>
        /// <typeparam name="T8">Eighth payload value type.</typeparam>
        /// <typeparam name="T9">Ninth payload value type.</typeparam>
        /// <typeparam name="T10">Tenth payload value type.</typeparam>
        /// <param name="p1">First payload value forwarded to every subscriber.</param>
        /// <param name="p2">Second payload value forwarded to every subscriber.</param>
        /// <param name="p3">Third payload value forwarded to every subscriber.</param>
        /// <param name="p4">Fourth payload value forwarded to every subscriber.</param>
        /// <param name="p5">Fifth payload value forwarded to every subscriber.</param>
        /// <param name="p6">Sixth payload value forwarded to every subscriber.</param>
        /// <param name="p7">Seventh payload value forwarded to every subscriber.</param>
        /// <param name="p8">Eighth payload value forwarded to every subscriber.</param>
        /// <param name="p9">Ninth payload value forwarded to every subscriber.</param>
        /// <param name="p10">Tenth payload value forwarded to every subscriber.</param>
        /// <param name="filePath">Do not pass; compiler-filled source file path for diagnostics.</param>
        /// <param name="lineNumber">Do not pass; compiler-filled line number for diagnostics.</param>
        /// <param name="memberName">Do not pass; compiler-filled member name for diagnostics.</param>
        /// <remarks>Caller attributes are compiler-filled for diagnostics; do not pass them manually.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
            T1 p1,
            T2 p2,
            T3 p3,
            T4 p4,
            T5 p5,
            T6 p6,
            T7 p7,
            T8 p8,
            T9 p9,
            T10 p10,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            MvcLogInternal.LogMessagePublished<TMessage>(_context.Actor, _context.ResolveModule(), filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                Bus.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            Bus.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10);
#endif
        }

        /// <summary>
        /// Publishes a message with eleven payload values from the mediator to the app-wide message bus.
        /// </summary>
        /// <typeparam name="TMessage">
        /// Marker type that identifies the event. Must implement <see cref="IMessage{T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11}"/>.
        /// Prefer a <c>struct</c> to avoid allocations.
        /// </typeparam>
        /// <typeparam name="T1">First payload value type.</typeparam>
        /// <typeparam name="T2">Second payload value type.</typeparam>
        /// <typeparam name="T3">Third payload value type.</typeparam>
        /// <typeparam name="T4">Fourth payload value type.</typeparam>
        /// <typeparam name="T5">Fifth payload value type.</typeparam>
        /// <typeparam name="T6">Sixth payload value type.</typeparam>
        /// <typeparam name="T7">Seventh payload value type.</typeparam>
        /// <typeparam name="T8">Eighth payload value type.</typeparam>
        /// <typeparam name="T9">Ninth payload value type.</typeparam>
        /// <typeparam name="T10">Tenth payload value type.</typeparam>
        /// <typeparam name="T11">Eleventh payload value type.</typeparam>
        /// <param name="p1">First payload value forwarded to every subscriber.</param>
        /// <param name="p2">Second payload value forwarded to every subscriber.</param>
        /// <param name="p3">Third payload value forwarded to every subscriber.</param>
        /// <param name="p4">Fourth payload value forwarded to every subscriber.</param>
        /// <param name="p5">Fifth payload value forwarded to every subscriber.</param>
        /// <param name="p6">Sixth payload value forwarded to every subscriber.</param>
        /// <param name="p7">Seventh payload value forwarded to every subscriber.</param>
        /// <param name="p8">Eighth payload value forwarded to every subscriber.</param>
        /// <param name="p9">Ninth payload value forwarded to every subscriber.</param>
        /// <param name="p10">Tenth payload value forwarded to every subscriber.</param>
        /// <param name="p11">Eleventh payload value forwarded to every subscriber.</param>
        /// <param name="filePath">Do not pass; compiler-filled source file path for diagnostics.</param>
        /// <param name="lineNumber">Do not pass; compiler-filled line number for diagnostics.</param>
        /// <param name="memberName">Do not pass; compiler-filled member name for diagnostics.</param>
        /// <remarks>Caller attributes are compiler-filled for diagnostics; do not pass them manually.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
            T1 p1,
            T2 p2,
            T3 p3,
            T4 p4,
            T5 p5,
            T6 p6,
            T7 p7,
            T8 p8,
            T9 p9,
            T10 p10,
            T11 p11,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>
        {
            MvcLogInternal.LogMessagePublished<TMessage>(_context.Actor, _context.ResolveModule(), filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                Bus.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            Bus.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11);
#endif
        }

        /// <summary>
        /// Publishes a message with twelve payload values from the mediator to the app-wide message bus.
        /// </summary>
        /// <typeparam name="TMessage">
        /// Marker type that identifies the event. Must implement <see cref="IMessage{T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12}"/>.
        /// Prefer a <c>struct</c> to avoid allocations.
        /// </typeparam>
        /// <typeparam name="T1">First payload value type.</typeparam>
        /// <typeparam name="T2">Second payload value type.</typeparam>
        /// <typeparam name="T3">Third payload value type.</typeparam>
        /// <typeparam name="T4">Fourth payload value type.</typeparam>
        /// <typeparam name="T5">Fifth payload value type.</typeparam>
        /// <typeparam name="T6">Sixth payload value type.</typeparam>
        /// <typeparam name="T7">Seventh payload value type.</typeparam>
        /// <typeparam name="T8">Eighth payload value type.</typeparam>
        /// <typeparam name="T9">Ninth payload value type.</typeparam>
        /// <typeparam name="T10">Tenth payload value type.</typeparam>
        /// <typeparam name="T11">Eleventh payload value type.</typeparam>
        /// <typeparam name="T12">Twelfth payload value type.</typeparam>
        /// <param name="p1">First payload value forwarded to every subscriber.</param>
        /// <param name="p2">Second payload value forwarded to every subscriber.</param>
        /// <param name="p3">Third payload value forwarded to every subscriber.</param>
        /// <param name="p4">Fourth payload value forwarded to every subscriber.</param>
        /// <param name="p5">Fifth payload value forwarded to every subscriber.</param>
        /// <param name="p6">Sixth payload value forwarded to every subscriber.</param>
        /// <param name="p7">Seventh payload value forwarded to every subscriber.</param>
        /// <param name="p8">Eighth payload value forwarded to every subscriber.</param>
        /// <param name="p9">Ninth payload value forwarded to every subscriber.</param>
        /// <param name="p10">Tenth payload value forwarded to every subscriber.</param>
        /// <param name="p11">Eleventh payload value forwarded to every subscriber.</param>
        /// <param name="p12">Twelfth payload value forwarded to every subscriber.</param>
        /// <param name="filePath">Do not pass; compiler-filled source file path for diagnostics.</param>
        /// <param name="lineNumber">Do not pass; compiler-filled line number for diagnostics.</param>
        /// <param name="memberName">Do not pass; compiler-filled member name for diagnostics.</param>
        /// <remarks>Caller attributes are compiler-filled for diagnostics; do not pass them manually.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
            T1 p1,
            T2 p2,
            T3 p3,
            T4 p4,
            T5 p5,
            T6 p6,
            T7 p7,
            T8 p8,
            T9 p9,
            T10 p10,
            T11 p11,
            T12 p12,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>
        {
            MvcLogInternal.LogMessagePublished<TMessage>(_context.Actor, _context.ResolveModule(), filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                Bus.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            Bus.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12);
#endif
        }

        /// <summary>
        /// Subscribes the mediator to a no-payload message and returns a token for optional manual removal.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage"/>.</typeparam>
        /// <param name="handler">Callback invoked each time <typeparamref name="TMessage"/> is published.</param>
        /// <returns>
        /// A <see cref="SubscriptionToken"/> that can be passed to <see cref="Unsubscribe{TMessage}"/> to
        /// stop listening before the mediator is detached. If the mediator is already destroyed the
        /// token is <c>default</c> and the handler is never invoked.
        /// </returns>
        /// <remarks>
        /// The subscription is tracked automatically and removed when the mediator is detached or
        /// its GameObject is destroyed. You only need to call <see cref="Unsubscribe{TMessage}"/>
        /// if you want to stop listening while the mediator is still alive.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage>(Action handler) where TMessage : IMessage
        {
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Action toSubscribe = () =>
            {
                LogHandled<TMessage>(mediator);
                handler();
            };
#else
            var toSubscribe = handler;
#endif
            var token = bus.Subscribe<TMessage>(toSubscribe);
            tracker.Track(typeof(TMessage), mediator, token, 0, t => bus.Unsubscribe<TMessage>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a message with one payload value.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1}"/>.</typeparam>
        /// <typeparam name="T1">Payload value type passed to the handler.</typeparam>
        /// <param name="handler">Callback invoked with the payload each time <typeparamref name="TMessage"/> is published.</param>
        /// <returns>
        /// A <see cref="SubscriptionToken"/> for optional early removal via
        /// <see cref="Unsubscribe{TMessage, T1}"/>. Returns <c>default</c> if the mediator is destroyed.
        /// </returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1>(Action<T1> handler) where TMessage : IMessage<T1>
        {
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Action<T1> toSubscribe = p1 =>
            {
                LogHandled<TMessage>(mediator);
                handler(p1);
            };
#else
            var toSubscribe = handler;
#endif
            var token = bus.Subscribe<TMessage, T1>(toSubscribe);
            tracker.Track(typeof(TMessage), mediator, token, 1, t => bus.Unsubscribe<TMessage, T1>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a message with two payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2>(Action<T1, T2> handler) where TMessage : IMessage<T1, T2>
        {
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Action<T1, T2> toSubscribe = (p1, p2) =>
            {
                LogHandled<TMessage>(mediator);
                handler(p1, p2);
            };
#else
            var toSubscribe = handler;
#endif
            var token = bus.Subscribe<TMessage, T1, T2>(toSubscribe);
            tracker.Track(typeof(TMessage), mediator, token, 2, t => bus.Unsubscribe<TMessage, T1, T2>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a message with three payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3>(Action<T1, T2, T3> handler) where TMessage : IMessage<T1, T2, T3>
        {
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Action<T1, T2, T3> toSubscribe = (p1, p2, p3) =>
            {
                LogHandled<TMessage>(mediator);
                handler(p1, p2, p3);
            };
#else
            var toSubscribe = handler;
#endif
            var token = bus.Subscribe<TMessage, T1, T2, T3>(toSubscribe);
            tracker.Track(typeof(TMessage), mediator, token, 3, t => bus.Unsubscribe<TMessage, T1, T2, T3>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a message with four payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4>(Action<T1, T2, T3, T4> handler) where TMessage : IMessage<T1, T2, T3, T4>
        {
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Action<T1, T2, T3, T4> toSubscribe = (p1, p2, p3, p4) =>
            {
                LogHandled<TMessage>(mediator);
                handler(p1, p2, p3, p4);
            };
#else
            var toSubscribe = handler;
#endif
            var token = bus.Subscribe<TMessage, T1, T2, T3, T4>(toSubscribe);
            tracker.Track(typeof(TMessage), mediator, token, 4, t => bus.Unsubscribe<TMessage, T1, T2, T3, T4>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a message with five payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> handler) where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Action<T1, T2, T3, T4, T5> toSubscribe = (p1, p2, p3, p4, p5) =>
            {
                LogHandled<TMessage>(mediator);
                handler(p1, p2, p3, p4, p5);
            };
#else
            var toSubscribe = handler;
#endif
            var token = bus.Subscribe<TMessage, T1, T2, T3, T4, T5>(toSubscribe);
            tracker.Track(typeof(TMessage), mediator, token, 5, t => bus.Unsubscribe<TMessage, T1, T2, T3, T4, T5>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a message with six payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4, T5, T6>(Action<T1, T2, T3, T4, T5, T6> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6>
        {
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Action<T1, T2, T3, T4, T5, T6> toSubscribe = (p1, p2, p3, p4, p5, p6) =>
            {
                LogHandled<TMessage>(mediator);
                handler(p1, p2, p3, p4, p5, p6);
            };
#else
            var toSubscribe = handler;
#endif
            var token = bus.Subscribe<TMessage, T1, T2, T3, T4, T5, T6>(toSubscribe);
            tracker.Track(typeof(TMessage), mediator, token, 6, t => bus.Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a message with seven payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7>(Action<T1, T2, T3, T4, T5, T6, T7> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7>
        {
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Action<T1, T2, T3, T4, T5, T6, T7> toSubscribe = (p1, p2, p3, p4, p5, p6, p7) =>
            {
                LogHandled<TMessage>(mediator);
                handler(p1, p2, p3, p4, p5, p6, p7);
            };
#else
            var toSubscribe = handler;
#endif
            var token = bus.Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7>(toSubscribe);
            tracker.Track(typeof(TMessage), mediator, token, 7, t => bus.Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a message with eight payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(Action<T1, T2, T3, T4, T5, T6, T7, T8> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8>
        {
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Action<T1, T2, T3, T4, T5, T6, T7, T8> toSubscribe = (p1, p2, p3, p4, p5, p6, p7, p8) =>
            {
                LogHandled<TMessage>(mediator);
                handler(p1, p2, p3, p4, p5, p6, p7, p8);
            };
#else
            var toSubscribe = handler;
#endif
            var token = bus.Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(toSubscribe);
            tracker.Track(typeof(TMessage), mediator, token, 8, t => bus.Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a message with nine payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9>
        {
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9> toSubscribe = (p1, p2, p3, p4, p5, p6, p7, p8, p9) =>
            {
                LogHandled<TMessage>(mediator);
                handler(p1, p2, p3, p4, p5, p6, p7, p8, p9);
            };
#else
            var toSubscribe = handler;
#endif
            var token = bus.Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(toSubscribe);
            tracker.Track(typeof(TMessage), mediator, token, 9, t => bus.Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a message with ten payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10> toSubscribe = (p1, p2, p3, p4, p5, p6, p7, p8, p9, p10) =>
            {
                LogHandled<TMessage>(mediator);
                handler(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10);
            };
#else
            var toSubscribe = handler;
#endif
            var token = bus.Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(toSubscribe);
            tracker.Track(typeof(TMessage), mediator, token, 10, t => bus.Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a message with eleven payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>
        {
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11> toSubscribe = (p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11) =>
            {
                LogHandled<TMessage>(mediator);
                handler(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11);
            };
#else
            var toSubscribe = handler;
#endif
            var token = bus.Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(toSubscribe);
            tracker.Track(typeof(TMessage), mediator, token, 11, t => bus.Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a message with twelve payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SubscriptionToken Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> handler) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>
        {
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            Action<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12> toSubscribe = (p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12) =>
            {
                LogHandled<TMessage>(mediator);
                handler(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12);
            };
#else
            var toSubscribe = handler;
#endif
            var token = bus.Subscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(toSubscribe);
            tracker.Track(typeof(TMessage), mediator, token, 12, t => bus.Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        // ============================================================
        // HAS SUBSCRIBERS
        // ============================================================
        // Lets a mediator skip building an expensive payload when nobody is listening.
        // Delegates straight to the concrete MvcMessageBus - no allocation, no dispatch.

        /// <summary>
        /// Returns true if at least one subscriber is currently listening for
        /// <typeparamref name="TMessage"/>. Useful to skip building an expensive payload when
        /// nobody would receive it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasSubscribers<TMessage>() where TMessage : IMessage
        {
            return Bus.HasSubscribers<TMessage>();
        }

        /// <summary>
        /// Returns true if at least one subscriber is currently listening for one payload value of
        /// <typeparamref name="TMessage"/>. Useful to skip building an expensive payload when
        /// nobody would receive it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasSubscribers<TMessage, T1>() where TMessage : IMessage<T1>
        {
            return Bus.HasSubscribers<TMessage, T1>();
        }

        /// <summary>
        /// Returns true if at least one subscriber is currently listening for two payload values of
        /// <typeparamref name="TMessage"/>. Useful to skip building an expensive payload when
        /// nobody would receive it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasSubscribers<TMessage, T1, T2>() where TMessage : IMessage<T1, T2>
        {
            return Bus.HasSubscribers<TMessage, T1, T2>();
        }

        /// <summary>
        /// Returns true if at least one subscriber is currently listening for three payload values of
        /// <typeparamref name="TMessage"/>. Useful to skip building an expensive payload when
        /// nobody would receive it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasSubscribers<TMessage, T1, T2, T3>() where TMessage : IMessage<T1, T2, T3>
        {
            return Bus.HasSubscribers<TMessage, T1, T2, T3>();
        }

        /// <summary>
        /// Returns true if at least one subscriber is currently listening for four payload values of
        /// <typeparamref name="TMessage"/>. Useful to skip building an expensive payload when
        /// nobody would receive it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasSubscribers<TMessage, T1, T2, T3, T4>() where TMessage : IMessage<T1, T2, T3, T4>
        {
            return Bus.HasSubscribers<TMessage, T1, T2, T3, T4>();
        }

        /// <summary>
        /// Returns true if at least one subscriber is currently listening for five payload values of
        /// <typeparamref name="TMessage"/>. Useful to skip building an expensive payload when
        /// nobody would receive it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasSubscribers<TMessage, T1, T2, T3, T4, T5>() where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            return Bus.HasSubscribers<TMessage, T1, T2, T3, T4, T5>();
        }

        /// <summary>
        /// Returns true if at least one subscriber is currently listening for six payload values of
        /// <typeparamref name="TMessage"/>. Useful to skip building an expensive payload when
        /// nobody would receive it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasSubscribers<TMessage, T1, T2, T3, T4, T5, T6>() where TMessage : IMessage<T1, T2, T3, T4, T5, T6>
        {
            return Bus.HasSubscribers<TMessage, T1, T2, T3, T4, T5, T6>();
        }

        /// <summary>
        /// Returns true if at least one subscriber is currently listening for seven payload values of
        /// <typeparamref name="TMessage"/>. Useful to skip building an expensive payload when
        /// nobody would receive it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasSubscribers<TMessage, T1, T2, T3, T4, T5, T6, T7>() where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7>
        {
            return Bus.HasSubscribers<TMessage, T1, T2, T3, T4, T5, T6, T7>();
        }

        /// <summary>
        /// Returns true if at least one subscriber is currently listening for eight payload values of
        /// <typeparamref name="TMessage"/>. Useful to skip building an expensive payload when
        /// nobody would receive it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasSubscribers<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>() where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8>
        {
            return Bus.HasSubscribers<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>();
        }

        /// <summary>
        /// Returns true if at least one subscriber is currently listening for nine payload values of
        /// <typeparamref name="TMessage"/>. Useful to skip building an expensive payload when
        /// nobody would receive it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasSubscribers<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>() where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9>
        {
            return Bus.HasSubscribers<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>();
        }

        /// <summary>
        /// Returns true if at least one subscriber is currently listening for ten payload values of
        /// <typeparamref name="TMessage"/>. Useful to skip building an expensive payload when
        /// nobody would receive it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasSubscribers<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>() where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            return Bus.HasSubscribers<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>();
        }

        /// <summary>
        /// Returns true if at least one subscriber is currently listening for eleven payload values of
        /// <typeparamref name="TMessage"/>. Useful to skip building an expensive payload when
        /// nobody would receive it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasSubscribers<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>() where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>
        {
            return Bus.HasSubscribers<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>();
        }

        /// <summary>
        /// Returns true if at least one subscriber is currently listening for twelve payload values of
        /// <typeparamref name="TMessage"/>. Useful to skip building an expensive payload when
        /// nobody would receive it.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasSubscribers<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>() where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>
        {
            return Bus.HasSubscribers<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>();
        }

        // ============================================================
        // ONE-TIME AND CONDITIONAL SUBSCRIPTIONS
        // ============================================================
        // Higher-level helpers built on Subscribe/Unsubscribe. Both allocate a wrapper
        // delegate to implement their extra behavior (auto-unsubscribe / condition check) -
        // prefer plain Subscribe on hot-path messages. Arity 0-5 only; use plain Subscribe
        // plus manual bookkeeping for higher arities.

        /// <summary>
        /// Subscribes the mediator to a no-payload message, automatically unsubscribing after
        /// the first delivery. Useful for fire-once signals like a one-time transition or intro sequence.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage"/>.</typeparam>
        /// <param name="handler">Callback invoked exactly once, on the first <typeparamref name="TMessage"/> publish.</param>
        /// <returns>
        /// A <see cref="SubscriptionToken"/> that can be passed to <see cref="Unsubscribe{TMessage}"/> to
        /// cancel before the first delivery. Returns <c>default</c> if the mediator is already destroyed.
        /// </returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the auto-unsubscribe behavior - prefer a normal
        /// <see cref="Subscribe{TMessage}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeOnce<TMessage>(Action handler) where TMessage : IMessage
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;

            SubscriptionToken token = default;
            Action wrapper = null;
            wrapper = () =>
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogHandled<TMessage>(mediator);
#endif
                try
                {
                    handler();
                }
                finally
                {
                    bus.Unsubscribe<TMessage>(token);
                    tracker.Untrack(typeof(TMessage), token);
                }
            };

            token = bus.Subscribe<TMessage>(wrapper);
            tracker.Track(typeof(TMessage), mediator, token, 0, t => bus.Unsubscribe<TMessage>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a one-parameter message, automatically unsubscribing after
        /// the first delivery. Useful for fire-once signals like a one-time transition or intro sequence.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1}"/>.</typeparam>
        /// <param name="handler">Callback invoked exactly once, on the first <typeparamref name="TMessage"/> publish.</param>
        /// <returns>
        /// A <see cref="SubscriptionToken"/> that can be passed to <see cref="Unsubscribe{TMessage, T1}"/> to
        /// cancel before the first delivery. Returns <c>default</c> if the mediator is already destroyed.
        /// </returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the auto-unsubscribe behavior - prefer a normal
        /// <see cref="Subscribe{TMessage, T1}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeOnce<TMessage, T1>(Action<T1> handler) where TMessage : IMessage<T1>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;

            SubscriptionToken token = default;
            Action<T1> wrapper = null;
            wrapper = (p1) =>
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogHandled<TMessage>(mediator);
#endif
                try
                {
                    handler(p1);
                }
                finally
                {
                    bus.Unsubscribe<TMessage, T1>(token);
                    tracker.Untrack(typeof(TMessage), token);
                }
            };

            token = bus.Subscribe<TMessage, T1>(wrapper);
            tracker.Track(typeof(TMessage), mediator, token, 1, t => bus.Unsubscribe<TMessage, T1>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a two-parameter message, automatically unsubscribing after
        /// the first delivery. Useful for fire-once signals like a one-time transition or intro sequence.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2}"/>.</typeparam>
        /// <param name="handler">Callback invoked exactly once, on the first <typeparamref name="TMessage"/> publish.</param>
        /// <returns>
        /// A <see cref="SubscriptionToken"/> that can be passed to <see cref="Unsubscribe{TMessage, T1, T2}"/> to
        /// cancel before the first delivery. Returns <c>default</c> if the mediator is already destroyed.
        /// </returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the auto-unsubscribe behavior - prefer a normal
        /// <see cref="Subscribe{TMessage, T1, T2}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeOnce<TMessage, T1, T2>(Action<T1, T2> handler) where TMessage : IMessage<T1, T2>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;

            SubscriptionToken token = default;
            Action<T1, T2> wrapper = null;
            wrapper = (p1, p2) =>
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogHandled<TMessage>(mediator);
#endif
                try
                {
                    handler(p1, p2);
                }
                finally
                {
                    bus.Unsubscribe<TMessage, T1, T2>(token);
                    tracker.Untrack(typeof(TMessage), token);
                }
            };

            token = bus.Subscribe<TMessage, T1, T2>(wrapper);
            tracker.Track(typeof(TMessage), mediator, token, 2, t => bus.Unsubscribe<TMessage, T1, T2>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a three-parameter message, automatically unsubscribing after
        /// the first delivery. Useful for fire-once signals like a one-time transition or intro sequence.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2, T3}"/>.</typeparam>
        /// <param name="handler">Callback invoked exactly once, on the first <typeparamref name="TMessage"/> publish.</param>
        /// <returns>
        /// A <see cref="SubscriptionToken"/> that can be passed to <see cref="Unsubscribe{TMessage, T1, T2, T3}"/> to
        /// cancel before the first delivery. Returns <c>default</c> if the mediator is already destroyed.
        /// </returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the auto-unsubscribe behavior - prefer a normal
        /// <see cref="Subscribe{TMessage, T1, T2, T3}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeOnce<TMessage, T1, T2, T3>(Action<T1, T2, T3> handler) where TMessage : IMessage<T1, T2, T3>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;

            SubscriptionToken token = default;
            Action<T1, T2, T3> wrapper = null;
            wrapper = (p1, p2, p3) =>
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogHandled<TMessage>(mediator);
#endif
                try
                {
                    handler(p1, p2, p3);
                }
                finally
                {
                    bus.Unsubscribe<TMessage, T1, T2, T3>(token);
                    tracker.Untrack(typeof(TMessage), token);
                }
            };

            token = bus.Subscribe<TMessage, T1, T2, T3>(wrapper);
            tracker.Track(typeof(TMessage), mediator, token, 3, t => bus.Unsubscribe<TMessage, T1, T2, T3>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a four-parameter message, automatically unsubscribing after
        /// the first delivery. Useful for fire-once signals like a one-time transition or intro sequence.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2, T3, T4}"/>.</typeparam>
        /// <param name="handler">Callback invoked exactly once, on the first <typeparamref name="TMessage"/> publish.</param>
        /// <returns>
        /// A <see cref="SubscriptionToken"/> that can be passed to <see cref="Unsubscribe{TMessage, T1, T2, T3, T4}"/> to
        /// cancel before the first delivery. Returns <c>default</c> if the mediator is already destroyed.
        /// </returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the auto-unsubscribe behavior - prefer a normal
        /// <see cref="Subscribe{TMessage, T1, T2, T3, T4}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeOnce<TMessage, T1, T2, T3, T4>(Action<T1, T2, T3, T4> handler) where TMessage : IMessage<T1, T2, T3, T4>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;

            SubscriptionToken token = default;
            Action<T1, T2, T3, T4> wrapper = null;
            wrapper = (p1, p2, p3, p4) =>
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogHandled<TMessage>(mediator);
#endif
                try
                {
                    handler(p1, p2, p3, p4);
                }
                finally
                {
                    bus.Unsubscribe<TMessage, T1, T2, T3, T4>(token);
                    tracker.Untrack(typeof(TMessage), token);
                }
            };

            token = bus.Subscribe<TMessage, T1, T2, T3, T4>(wrapper);
            tracker.Track(typeof(TMessage), mediator, token, 4, t => bus.Unsubscribe<TMessage, T1, T2, T3, T4>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a five-parameter message, automatically unsubscribing after
        /// the first delivery. Useful for fire-once signals like a one-time transition or intro sequence.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2, T3, T4, T5}"/>.</typeparam>
        /// <param name="handler">Callback invoked exactly once, on the first <typeparamref name="TMessage"/> publish.</param>
        /// <returns>
        /// A <see cref="SubscriptionToken"/> that can be passed to <see cref="Unsubscribe{TMessage, T1, T2, T3, T4, T5}"/> to
        /// cancel before the first delivery. Returns <c>default</c> if the mediator is already destroyed.
        /// </returns>
        /// <remarks>
        /// Allocates a wrapper delegate to implement the auto-unsubscribe behavior - prefer a normal
        /// <see cref="Subscribe{TMessage, T1, T2, T3, T4, T5}"/> call on hot-path messages.
        /// </remarks>
        public SubscriptionToken SubscribeOnce<TMessage, T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> handler) where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;

            SubscriptionToken token = default;
            Action<T1, T2, T3, T4, T5> wrapper = null;
            wrapper = (p1, p2, p3, p4, p5) =>
            {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogHandled<TMessage>(mediator);
#endif
                try
                {
                    handler(p1, p2, p3, p4, p5);
                }
                finally
                {
                    bus.Unsubscribe<TMessage, T1, T2, T3, T4, T5>(token);
                    tracker.Untrack(typeof(TMessage), token);
                }
            };

            token = bus.Subscribe<TMessage, T1, T2, T3, T4, T5>(wrapper);
            tracker.Track(typeof(TMessage), mediator, token, 5, t => bus.Unsubscribe<TMessage, T1, T2, T3, T4, T5>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a no-payload message, invoking <paramref name="handler"/>
        /// only when <paramref name="condition"/> evaluates to <c>true</c> at the time of publish.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage"/>.</typeparam>
        /// <param name="handler">Callback invoked when <typeparamref name="TMessage"/> is published and <paramref name="condition"/> is true.</param>
        /// <param name="condition">Checked on every publish; the handler only runs when this returns <c>true</c>.</param>
        /// <returns>
        /// A <see cref="SubscriptionToken"/> for optional early removal via <see cref="Unsubscribe{TMessage}"/>.
        /// Returns <c>default</c> if the mediator is already destroyed.
        /// </returns>
        public SubscriptionToken SubscribeWhen<TMessage>(Action handler, Func<bool> condition) where TMessage : IMessage
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;

            Action toSubscribe = () =>
            {
                if (!condition()) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogHandled<TMessage>(mediator);
#endif
                handler();
            };

            var token = bus.Subscribe<TMessage>(toSubscribe);
            tracker.Track(typeof(TMessage), mediator, token, 0, t => bus.Unsubscribe<TMessage>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a one-parameter message, invoking <paramref name="handler"/>
        /// only when <paramref name="condition"/> evaluates to <c>true</c> at the time of publish.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1}"/>.</typeparam>
        /// <param name="handler">Callback invoked when <typeparamref name="TMessage"/> is published and <paramref name="condition"/> is true.</param>
        /// <param name="condition">Checked on every publish; the handler only runs when this returns <c>true</c>.</param>
        /// <returns>
        /// A <see cref="SubscriptionToken"/> for optional early removal via <see cref="Unsubscribe{TMessage, T1}"/>.
        /// Returns <c>default</c> if the mediator is already destroyed.
        /// </returns>
        public SubscriptionToken SubscribeWhen<TMessage, T1>(Action<T1> handler, Func<bool> condition) where TMessage : IMessage<T1>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;

            Action<T1> toSubscribe = (p1) =>
            {
                if (!condition()) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogHandled<TMessage>(mediator);
#endif
                handler(p1);
            };

            var token = bus.Subscribe<TMessage, T1>(toSubscribe);
            tracker.Track(typeof(TMessage), mediator, token, 1, t => bus.Unsubscribe<TMessage, T1>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a two-parameter message, invoking <paramref name="handler"/>
        /// only when <paramref name="condition"/> evaluates to <c>true</c> at the time of publish.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2}"/>.</typeparam>
        /// <param name="handler">Callback invoked when <typeparamref name="TMessage"/> is published and <paramref name="condition"/> is true.</param>
        /// <param name="condition">Checked on every publish; the handler only runs when this returns <c>true</c>.</param>
        /// <returns>
        /// A <see cref="SubscriptionToken"/> for optional early removal via <see cref="Unsubscribe{TMessage, T1, T2}"/>.
        /// Returns <c>default</c> if the mediator is already destroyed.
        /// </returns>
        public SubscriptionToken SubscribeWhen<TMessage, T1, T2>(Action<T1, T2> handler, Func<bool> condition) where TMessage : IMessage<T1, T2>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;

            Action<T1, T2> toSubscribe = (p1, p2) =>
            {
                if (!condition()) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogHandled<TMessage>(mediator);
#endif
                handler(p1, p2);
            };

            var token = bus.Subscribe<TMessage, T1, T2>(toSubscribe);
            tracker.Track(typeof(TMessage), mediator, token, 2, t => bus.Unsubscribe<TMessage, T1, T2>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a three-parameter message, invoking <paramref name="handler"/>
        /// only when <paramref name="condition"/> evaluates to <c>true</c> at the time of publish.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2, T3}"/>.</typeparam>
        /// <param name="handler">Callback invoked when <typeparamref name="TMessage"/> is published and <paramref name="condition"/> is true.</param>
        /// <param name="condition">Checked on every publish; the handler only runs when this returns <c>true</c>.</param>
        /// <returns>
        /// A <see cref="SubscriptionToken"/> for optional early removal via <see cref="Unsubscribe{TMessage, T1, T2, T3}"/>.
        /// Returns <c>default</c> if the mediator is already destroyed.
        /// </returns>
        public SubscriptionToken SubscribeWhen<TMessage, T1, T2, T3>(Action<T1, T2, T3> handler, Func<bool> condition) where TMessage : IMessage<T1, T2, T3>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;

            Action<T1, T2, T3> toSubscribe = (p1, p2, p3) =>
            {
                if (!condition()) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogHandled<TMessage>(mediator);
#endif
                handler(p1, p2, p3);
            };

            var token = bus.Subscribe<TMessage, T1, T2, T3>(toSubscribe);
            tracker.Track(typeof(TMessage), mediator, token, 3, t => bus.Unsubscribe<TMessage, T1, T2, T3>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a four-parameter message, invoking <paramref name="handler"/>
        /// only when <paramref name="condition"/> evaluates to <c>true</c> at the time of publish.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2, T3, T4}"/>.</typeparam>
        /// <param name="handler">Callback invoked when <typeparamref name="TMessage"/> is published and <paramref name="condition"/> is true.</param>
        /// <param name="condition">Checked on every publish; the handler only runs when this returns <c>true</c>.</param>
        /// <returns>
        /// A <see cref="SubscriptionToken"/> for optional early removal via <see cref="Unsubscribe{TMessage, T1, T2, T3, T4}"/>.
        /// Returns <c>default</c> if the mediator is already destroyed.
        /// </returns>
        public SubscriptionToken SubscribeWhen<TMessage, T1, T2, T3, T4>(Action<T1, T2, T3, T4> handler, Func<bool> condition) where TMessage : IMessage<T1, T2, T3, T4>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;

            Action<T1, T2, T3, T4> toSubscribe = (p1, p2, p3, p4) =>
            {
                if (!condition()) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogHandled<TMessage>(mediator);
#endif
                handler(p1, p2, p3, p4);
            };

            var token = bus.Subscribe<TMessage, T1, T2, T3, T4>(toSubscribe);
            tracker.Track(typeof(TMessage), mediator, token, 4, t => bus.Unsubscribe<TMessage, T1, T2, T3, T4>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Subscribes the mediator to a five-parameter message, invoking <paramref name="handler"/>
        /// only when <paramref name="condition"/> evaluates to <c>true</c> at the time of publish.
        /// </summary>
        /// <typeparam name="TMessage">Message type to listen for. Must implement <see cref="IMessage{T1, T2, T3, T4, T5}"/>.</typeparam>
        /// <param name="handler">Callback invoked when <typeparamref name="TMessage"/> is published and <paramref name="condition"/> is true.</param>
        /// <param name="condition">Checked on every publish; the handler only runs when this returns <c>true</c>.</param>
        /// <returns>
        /// A <see cref="SubscriptionToken"/> for optional early removal via <see cref="Unsubscribe{TMessage, T1, T2, T3, T4, T5}"/>.
        /// Returns <c>default</c> if the mediator is already destroyed.
        /// </returns>
        public SubscriptionToken SubscribeWhen<TMessage, T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> handler, Func<bool> condition) where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            if (condition == null) throw new ArgumentNullException(nameof(condition));
            if (!CanSubscribe()) return default;
            var mediator = Mediator;
            var bus = Bus;
            var tracker = mediator.SubscriptionTracker;

            Action<T1, T2, T3, T4, T5> toSubscribe = (p1, p2, p3, p4, p5) =>
            {
                if (!condition()) return;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                LogHandled<TMessage>(mediator);
#endif
                handler(p1, p2, p3, p4, p5);
            };

            var token = bus.Subscribe<TMessage, T1, T2, T3, T4, T5>(toSubscribe);
            tracker.Track(typeof(TMessage), mediator, token, 5, t => bus.Unsubscribe<TMessage, T1, T2, T3, T4, T5>(t));
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.TrackSubscriber(handler.Target, _context.ModuleType);
#endif
            return token;
        }

        /// <summary>
        /// Removes a no-payload subscription before the mediator is detached or destroyed.
        /// </summary>
        /// <typeparam name="TMessage">Message type the token was issued for.</typeparam>
        /// <param name="token">Token returned by the matching <see cref="Subscribe{TMessage}"/> call.</param>
        /// <remarks>
        /// Only needed for early removal. The subscription is removed automatically on mediator
        /// teardown, so this is optional in <c>OnCleanup</c>.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage>(SubscriptionToken token) where TMessage : IMessage
        {
            Bus.Unsubscribe<TMessage>(token);
            Mediator.SubscriptionTracker.Untrack(typeof(TMessage), token);
        }

        /// <summary>
        /// Removes a single-payload subscription before the mediator is detached or destroyed.
        /// </summary>
        /// <typeparam name="TMessage">Message type the token was issued for.</typeparam>
        /// <typeparam name="T1">Payload type matching the original subscription.</typeparam>
        /// <param name="token">Token returned by the matching <see cref="Subscribe{TMessage, T1}"/> call.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1>(SubscriptionToken token) where TMessage : IMessage<T1>
        {
            Bus.Unsubscribe<TMessage, T1>(token);
            Mediator.SubscriptionTracker.Untrack(typeof(TMessage), token);
        }

        /// <summary>
        /// Unsubscribes a mediator handler from a message with two payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2>(SubscriptionToken token) where TMessage : IMessage<T1, T2>
        {
            Bus.Unsubscribe<TMessage, T1, T2>(token);
            Mediator.SubscriptionTracker.Untrack(typeof(TMessage), token);
        }

        /// <summary>
        /// Unsubscribes a mediator handler from a message with three payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3>
        {
            Bus.Unsubscribe<TMessage, T1, T2, T3>(token);
            Mediator.SubscriptionTracker.Untrack(typeof(TMessage), token);
        }

        /// <summary>
        /// Unsubscribes a mediator handler from a message with four payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3, T4>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4>
        {
            Bus.Unsubscribe<TMessage, T1, T2, T3, T4>(token);
            Mediator.SubscriptionTracker.Untrack(typeof(TMessage), token);
        }

        /// <summary>
        /// Unsubscribes a mediator handler from a message with five payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            Bus.Unsubscribe<TMessage, T1, T2, T3, T4, T5>(token);
            Mediator.SubscriptionTracker.Untrack(typeof(TMessage), token);
        }

        /// <summary>
        /// Unsubscribes a mediator handler from a message with six payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4, T5, T6>
        {
            Bus.Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6>(token);
            Mediator.SubscriptionTracker.Untrack(typeof(TMessage), token);
        }

        /// <summary>
        /// Unsubscribes a mediator handler from a message with seven payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7>
        {
            Bus.Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7>(token);
            Mediator.SubscriptionTracker.Untrack(typeof(TMessage), token);
        }

        /// <summary>
        /// Unsubscribes a mediator handler from a message with eight payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8>
        {
            Bus.Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(token);
            Mediator.SubscriptionTracker.Untrack(typeof(TMessage), token);
        }

        /// <summary>
        /// Unsubscribes a mediator handler from a message with nine payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9>
        {
            Bus.Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(token);
            Mediator.SubscriptionTracker.Untrack(typeof(TMessage), token);
        }

        /// <summary>
        /// Unsubscribes a mediator handler from a message with ten payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            Bus.Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(token);
            Mediator.SubscriptionTracker.Untrack(typeof(TMessage), token);
        }

        /// <summary>
        /// Unsubscribes a mediator handler from a message with eleven payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>
        {
            Bus.Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(token);
            Mediator.SubscriptionTracker.Untrack(typeof(TMessage), token);
        }

        /// <summary>
        /// Unsubscribes a mediator handler from a message with twelve payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(SubscriptionToken token) where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>
        {
            Bus.Unsubscribe<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(token);
            Mediator.SubscriptionTracker.Untrack(typeof(TMessage), token);
        }

        // Guard against subscriptions after Unity has destroyed the mediator's GameObject.
        private bool CanSubscribe()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (Mediator.IsDestroyed)
            {
                MvcDebug.LogWarning($"Cannot subscribe after mediator '{Mediator.name}' is destroyed.");
                return false;
            }
#endif
            return true;
        }

        // Emits a console diagnostic entry each time a subscribed handler is invoked.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void LogHandled<TMessage>(MediatorBehaviour mediator)
        {
            MvcLogInternal.LogMessageHandled(
                mediator.GetType().Name,
                typeof(TMessage).Name,
                mediator.ModuleContext,
                MvcLogContext.LogCategory.Mediator);
        }
    }
}

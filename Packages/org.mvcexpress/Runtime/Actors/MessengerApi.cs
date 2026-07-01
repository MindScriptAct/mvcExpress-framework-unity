using mvcExpress.Internal.Interfaces;
using mvcExpress.Logging;
using System.Runtime.CompilerServices;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
using mvcExpress.Plugins;
#endif

namespace mvcExpress
{
    /// <summary>
    /// Publish-only message bus facade exposed to Services, Proxies, Commands, and Modules.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the outbound half of the message system. Actors that only produce events
    /// (Services, Proxies, Commands) receive a <see cref="MessengerApi"/> - they can
    /// <c>Publish</c> but cannot <c>Subscribe</c>. Mediators that also need to react to
    /// messages use <see cref="MediatorMessengerApi"/> instead.
    /// </para>
    /// <para>
    /// Messages are marker types: implement <see cref="IMessage"/> (no payload),
    /// <see cref="IMessage{T1}"/> (one payload value), and so on. Payload values are passed
    /// as ordinary method arguments so the call is allocation-free when the message type is
    /// a <c>struct</c>. Prefer <c>struct</c> messages on high-frequency paths to avoid GC pressure.
    /// </para>
    /// <para>
    /// All publish calls are dispatched on the single app-wide bus shared by all modules.
    /// Every subscriber anywhere in the application will receive the message.
    /// </para>
    /// <para>
    /// <b>Thread safety:</b> <c>Publish</c> must be called from the Unity main thread. Calling it
    /// from a background thread throws <see cref="System.InvalidOperationException"/> in Editor and
    /// Development builds. When publishing from a third-party callback or after <c>Task.Run</c>
    /// that does not return to the main thread, use <c>PublishDeferred</c> instead - it enqueues
    /// delivery to the next <c>Update</c> frame on the main thread. <c>PublishDeferred</c> allocates
    /// a lambda per call, so do not use it on high-frequency paths.
    /// </para>
    /// </remarks>
    public readonly struct MessengerApi
    {
        // Actor context supplies the publisher, the owning module reference, and log metadata.
        private readonly MvcActorContext _context;

        // Constructed by the framework when wiring up each actor; not part of the public API.
        internal MessengerApi(MvcActorContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Publishes a no-payload message to the app-wide message bus.
        /// </summary>
        /// <typeparam name="TMessage">
        /// Marker type that identifies the event. Must implement <see cref="IMessage"/>.
        /// Prefer a <c>struct</c> to avoid allocations.
        /// </typeparam>
        /// <remarks>
        /// The <c>filePath</c>, <c>lineNumber</c>, and <c>memberName</c> parameters are filled
        /// automatically by the compiler via <c>[CallerFilePath]</c> / <c>[CallerLineNumber]</c> /
        /// <c>[CallerMemberName]</c> and are used exclusively for console diagnostics. Do not pass
        /// them manually.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage>(
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
            where TMessage : IMessage
        {
            LogPublished<TMessage>(filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                _context.MessagePublisher.Publish<TMessage>();
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            _context.MessagePublisher.Publish<TMessage>();
#endif
        }

        /// <summary>
        /// Publishes a message with one payload value to the app-wide message bus.
        /// </summary>
        /// <typeparam name="TMessage">
        /// Marker type that identifies the event. Must implement <see cref="IMessage{T1}"/>.
        /// Prefer a <c>struct</c> to avoid allocations.
        /// </typeparam>
        /// <typeparam name="T1">Payload value type.</typeparam>
        /// <param name="p1">Payload value forwarded to every subscriber.</param>
        /// <param name="filePath">Do not pass; compiler-filled source file path for diagnostics.</param>
        /// <param name="lineNumber">Do not pass; compiler-filled line number for diagnostics.</param>
        /// <param name="memberName">Do not pass; compiler-filled member name for diagnostics.</param>
        /// <remarks>
        /// Caller attributes are compiler-filled for diagnostics; do not pass them manually.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage, T1>(
            T1 p1,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
            where TMessage : IMessage<T1>
        {
            LogPublished<TMessage>(filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                _context.MessagePublisher.Publish<TMessage, T1>(p1);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            _context.MessagePublisher.Publish<TMessage, T1>(p1);
#endif
        }

        /// <summary>
        /// Publishes a message with two payload values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Publish<TMessage, T1, T2>(
            T1 p1,
            T2 p2,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
            where TMessage : IMessage<T1, T2>
        {
            LogPublished<TMessage>(filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                _context.MessagePublisher.Publish<TMessage, T1, T2>(p1, p2);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            _context.MessagePublisher.Publish<TMessage, T1, T2>(p1, p2);
#endif
        }

        /// <summary>
        /// Publishes a message with three payload values.
        /// </summary>
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
            LogPublished<TMessage>(filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                _context.MessagePublisher.Publish<TMessage, T1, T2, T3>(p1, p2, p3);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            _context.MessagePublisher.Publish<TMessage, T1, T2, T3>(p1, p2, p3);
#endif
        }

        /// <summary>
        /// Publishes a message with four payload values.
        /// </summary>
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
            LogPublished<TMessage>(filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                _context.MessagePublisher.Publish<TMessage, T1, T2, T3, T4>(p1, p2, p3, p4);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            _context.MessagePublisher.Publish<TMessage, T1, T2, T3, T4>(p1, p2, p3, p4);
#endif
        }

        /// <summary>
        /// Publishes a message with five payload values.
        /// </summary>
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
            LogPublished<TMessage>(filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                _context.MessagePublisher.Publish<TMessage, T1, T2, T3, T4, T5>(p1, p2, p3, p4, p5);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            _context.MessagePublisher.Publish<TMessage, T1, T2, T3, T4, T5>(p1, p2, p3, p4, p5);
#endif
        }

        /// <summary>
        /// Publishes a message with six payload values.
        /// </summary>
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
            LogPublished<TMessage>(filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                _context.MessagePublisher.Publish<TMessage, T1, T2, T3, T4, T5, T6>(p1, p2, p3, p4, p5, p6);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            _context.MessagePublisher.Publish<TMessage, T1, T2, T3, T4, T5, T6>(p1, p2, p3, p4, p5, p6);
#endif
        }

        /// <summary>
        /// Publishes a message with seven payload values.
        /// </summary>
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
            LogPublished<TMessage>(filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                _context.MessagePublisher.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7>(p1, p2, p3, p4, p5, p6, p7);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            _context.MessagePublisher.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7>(p1, p2, p3, p4, p5, p6, p7);
#endif
        }

        /// <summary>
        /// Publishes a message with eight payload values.
        /// </summary>
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
            LogPublished<TMessage>(filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                _context.MessagePublisher.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(p1, p2, p3, p4, p5, p6, p7, p8);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            _context.MessagePublisher.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(p1, p2, p3, p4, p5, p6, p7, p8);
#endif
        }

        /// <summary>
        /// Publishes a message with nine payload values.
        /// </summary>
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
            LogPublished<TMessage>(filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                _context.MessagePublisher.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(p1, p2, p3, p4, p5, p6, p7, p8, p9);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            _context.MessagePublisher.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(p1, p2, p3, p4, p5, p6, p7, p8, p9);
#endif
        }

        /// <summary>
        /// Publishes a message with ten payload values.
        /// </summary>
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
            LogPublished<TMessage>(filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                _context.MessagePublisher.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            _context.MessagePublisher.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10);
#endif
        }

        /// <summary>
        /// Publishes a message with eleven payload values.
        /// </summary>
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
            LogPublished<TMessage>(filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                _context.MessagePublisher.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            _context.MessagePublisher.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11);
#endif
        }

        /// <summary>
        /// Publishes a message with twelve payload values.
        /// </summary>
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
            LogPublished<TMessage>(filePath, lineNumber, memberName);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            CrossModuleContext.SetPublisher(_context.ModuleType);
            MvcPluginBus.FireMessagePublished(typeof(TMessage), _context.ModuleType);
            try
            {
                _context.MessagePublisher.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12);
            }
            finally
            {
                CrossModuleContext.ClearPublisher();
            }
#else
            _context.MessagePublisher.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12);
#endif
        }

        // ============================================================
        // DEFERRED PUBLISH - safe to call from any thread
        // ============================================================
        // These overloads enqueue a Publish action onto MvcFacade's per-frame drain queue.
        // Delivery is guaranteed to run on the Unity main thread during the next Update.
        // Because they capture values in a lambda they always allocate - do not use on hot paths.

        /// <summary>
        /// Schedules a no-payload message for main-thread delivery at the start of the next frame.
        /// Use instead of <see cref="Publish{TMessage}"/> when calling from a background thread or callback.
        /// </summary>
        public void PublishDeferred<TMessage>() where TMessage : IMessage
        {
            var publisher = _context.MessagePublisher;
            MvcFacade.TryEnqueueDeferredPublish(() => publisher.Publish<TMessage>());
        }

        /// <summary>Schedules a message with one payload value for main-thread delivery next frame.</summary>
        public void PublishDeferred<TMessage, T1>(T1 p1) where TMessage : IMessage<T1>
        {
            var publisher = _context.MessagePublisher;
            MvcFacade.TryEnqueueDeferredPublish(() => publisher.Publish<TMessage, T1>(p1));
        }

        /// <summary>Schedules a message with two payload values for main-thread delivery next frame.</summary>
        public void PublishDeferred<TMessage, T1, T2>(T1 p1, T2 p2) where TMessage : IMessage<T1, T2>
        {
            var publisher = _context.MessagePublisher;
            MvcFacade.TryEnqueueDeferredPublish(() => publisher.Publish<TMessage, T1, T2>(p1, p2));
        }

        /// <summary>Schedules a message with three payload values for main-thread delivery next frame.</summary>
        public void PublishDeferred<TMessage, T1, T2, T3>(T1 p1, T2 p2, T3 p3) where TMessage : IMessage<T1, T2, T3>
        {
            var publisher = _context.MessagePublisher;
            MvcFacade.TryEnqueueDeferredPublish(() => publisher.Publish<TMessage, T1, T2, T3>(p1, p2, p3));
        }

        /// <summary>Schedules a message with four payload values for main-thread delivery next frame.</summary>
        public void PublishDeferred<TMessage, T1, T2, T3, T4>(T1 p1, T2 p2, T3 p3, T4 p4)
            where TMessage : IMessage<T1, T2, T3, T4>
        {
            var publisher = _context.MessagePublisher;
            MvcFacade.TryEnqueueDeferredPublish(() => publisher.Publish<TMessage, T1, T2, T3, T4>(p1, p2, p3, p4));
        }

        /// <summary>Schedules a message with five payload values for main-thread delivery next frame.</summary>
        public void PublishDeferred<TMessage, T1, T2, T3, T4, T5>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5)
            where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            var publisher = _context.MessagePublisher;
            MvcFacade.TryEnqueueDeferredPublish(() => publisher.Publish<TMessage, T1, T2, T3, T4, T5>(p1, p2, p3, p4, p5));
        }

        /// <summary>Schedules a message with six payload values for main-thread delivery next frame.</summary>
        public void PublishDeferred<TMessage, T1, T2, T3, T4, T5, T6>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6)
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6>
        {
            var publisher = _context.MessagePublisher;
            MvcFacade.TryEnqueueDeferredPublish(() => publisher.Publish<TMessage, T1, T2, T3, T4, T5, T6>(p1, p2, p3, p4, p5, p6));
        }

        /// <summary>Schedules a message with seven payload values for main-thread delivery next frame.</summary>
        public void PublishDeferred<TMessage, T1, T2, T3, T4, T5, T6, T7>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7)
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7>
        {
            var publisher = _context.MessagePublisher;
            MvcFacade.TryEnqueueDeferredPublish(() => publisher.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7>(p1, p2, p3, p4, p5, p6, p7));
        }

        /// <summary>Schedules a message with eight payload values for main-thread delivery next frame.</summary>
        public void PublishDeferred<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8)
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8>
        {
            var publisher = _context.MessagePublisher;
            MvcFacade.TryEnqueueDeferredPublish(() => publisher.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(p1, p2, p3, p4, p5, p6, p7, p8));
        }

        /// <summary>Schedules a message with nine payload values for main-thread delivery next frame.</summary>
        public void PublishDeferred<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9)
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9>
        {
            var publisher = _context.MessagePublisher;
            MvcFacade.TryEnqueueDeferredPublish(() => publisher.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(p1, p2, p3, p4, p5, p6, p7, p8, p9));
        }

        /// <summary>Schedules a message with ten payload values for main-thread delivery next frame.</summary>
        public void PublishDeferred<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10)
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            var publisher = _context.MessagePublisher;
            MvcFacade.TryEnqueueDeferredPublish(() => publisher.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10));
        }

        /// <summary>Schedules a message with eleven payload values for main-thread delivery next frame.</summary>
        public void PublishDeferred<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10, T11 p11)
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>
        {
            var publisher = _context.MessagePublisher;
            MvcFacade.TryEnqueueDeferredPublish(() => publisher.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11));
        }

        /// <summary>Schedules a message with twelve payload values for main-thread delivery next frame.</summary>
        public void PublishDeferred<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10, T11 p11, T12 p12)
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>
        {
            var publisher = _context.MessagePublisher;
            MvcFacade.TryEnqueueDeferredPublish(() => publisher.Publish<TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12));
        }

        // Every publish records caller info for diagnostics before dispatching to the shared bus.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LogPublished<TMessage>(string filePath, int lineNumber, string memberName)
        {
            if (_context.Actor is MvcModule module)
            {
                module.CheckCanPublishInEditor();
            }

            MvcLogInternal.LogMessagePublished<TMessage>(
                _context.Actor,
                _context.ResolveModule(),
                filePath,
                lineNumber,
                memberName);
        }
    }
}

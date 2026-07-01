using mvcExpress.Internal.Interfaces;
using mvcExpress.Logging;
using System;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace mvcExpress
{
    /// <summary>
    /// Binds, runs, inspects, and pools commands for a module.
    /// </summary>
    /// <remarks>
    /// Modules use this API to bind messages to command types. Modules and commands can also
    /// use it to run commands directly when a decoupled message publish is not needed.
    /// Payload overloads follow the message arity from zero to twelve values.
    /// </remarks>
    public readonly struct CommanderApi
    {
        private readonly MvcActorContext _context;
        private readonly ICommandProcessorInternal _processor;

        internal CommanderApi(MvcActorContext context, ICommandProcessorInternal processor)
        {
            _context = context;
            _processor = processor;
        }

        private ICommandRunner Runner => _processor;
        private ICommandBindingInfo BindingInfo => _processor;
        private ICommandPoolCreator PoolCreator => _processor;

        private static bool IsAsyncCommandType<TCommand>()
        {
            return typeof(MvcAsyncCommandBase).IsAssignableFrom(typeof(TCommand));
        }

        /// <summary>
        /// Binds a no-payload message to a command.
        /// </summary>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        public void Bind<TCommand, TMessage>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage
        {
            if (IsAsyncCommandType<TCommand>())
                _processor.BindCommandAsync<TCommand, TMessage>(poolSize);
            else
                _processor.BindCommand<TCommand, TMessage>(poolSize);

            LogBound<TCommand, TMessage>();
        }
#endif

        /// <summary>
        /// Binds a one-payload message to a command.
        /// </summary>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        public void Bind<TCommand, TMessage, T1>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1>
        {
            if (IsAsyncCommandType<TCommand>())
                _processor.BindCommandAsync<TCommand, TMessage, T1>(poolSize);
            else
                _processor.BindCommand<TCommand, TMessage, T1>(poolSize);

            LogBound<TCommand, TMessage>();
        }
#endif

        /// <summary>
        /// Binds a two-payload message to a command.
        /// </summary>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        public void Bind<TCommand, TMessage, T1, T2>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2>
        {
            if (IsAsyncCommandType<TCommand>())
                _processor.BindCommandAsync<TCommand, TMessage, T1, T2>(poolSize);
            else
                _processor.BindCommand<TCommand, TMessage, T1, T2>(poolSize);

            LogBound<TCommand, TMessage>();
        }
#endif

        /// <summary>
        /// Binds a three-payload message to a command.
        /// </summary>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        public void Bind<TCommand, TMessage, T1, T2, T3>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3>
        {
            if (IsAsyncCommandType<TCommand>())
                _processor.BindCommandAsync<TCommand, TMessage, T1, T2, T3>(poolSize);
            else
                _processor.BindCommand<TCommand, TMessage, T1, T2, T3>(poolSize);

            LogBound<TCommand, TMessage>();
        }
#endif

        /// <summary>
        /// Binds a four-payload message to a command.
        /// </summary>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        public void Bind<TCommand, TMessage, T1, T2, T3, T4>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4>
        {
            if (IsAsyncCommandType<TCommand>())
                _processor.BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4>(poolSize);
            else
                _processor.BindCommand<TCommand, TMessage, T1, T2, T3, T4>(poolSize);

            LogBound<TCommand, TMessage>();
        }
#endif

        /// <summary>
        /// Binds a five-payload message to a command.
        /// </summary>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        public void Bind<TCommand, TMessage, T1, T2, T3, T4, T5>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5>
        {
            if (IsAsyncCommandType<TCommand>())
                _processor.BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4, T5>(poolSize);
            else
                _processor.BindCommand<TCommand, TMessage, T1, T2, T3, T4, T5>(poolSize);

            LogBound<TCommand, TMessage>();
        }
#endif

        /// <summary>
        /// Binds a six-payload message to a command.
        /// </summary>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        public void Bind<TCommand, TMessage, T1, T2, T3, T4, T5, T6>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6>
        {
            if (IsAsyncCommandType<TCommand>())
                _processor.BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6>(poolSize);
            else
                _processor.BindCommand<TCommand, TMessage, T1, T2, T3, T4, T5, T6>(poolSize);

            LogBound<TCommand, TMessage>();
        }
#endif

        /// <summary>
        /// Binds a seven-payload message to a command.
        /// </summary>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        public void Bind<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7>
        {
            if (IsAsyncCommandType<TCommand>())
                _processor.BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7>(poolSize);
            else
                _processor.BindCommand<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7>(poolSize);

            LogBound<TCommand, TMessage>();
        }
#endif

        /// <summary>
        /// Binds an eight-payload message to a command.
        /// </summary>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        public void Bind<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8>
        {
            if (IsAsyncCommandType<TCommand>())
                _processor.BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(poolSize);
            else
                _processor.BindCommand<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(poolSize);

            LogBound<TCommand, TMessage>();
        }
#endif

        /// <summary>
        /// Binds a nine-payload message to a command.
        /// </summary>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        public void Bind<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9>
        {
            if (IsAsyncCommandType<TCommand>())
                _processor.BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(poolSize);
            else
                _processor.BindCommand<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(poolSize);

            LogBound<TCommand, TMessage>();
        }
#endif

        /// <summary>
        /// Binds a ten-payload message to a command.
        /// </summary>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        public void Bind<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>
        {
            if (IsAsyncCommandType<TCommand>())
                _processor.BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(poolSize);
            else
                _processor.BindCommand<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(poolSize);

            LogBound<TCommand, TMessage>();
        }
#endif

        /// <summary>
        /// Binds an eleven-payload message to a command.
        /// </summary>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        public void Bind<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>
        {
            if (IsAsyncCommandType<TCommand>())
                _processor.BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(poolSize);
            else
                _processor.BindCommand<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(poolSize);

            LogBound<TCommand, TMessage>();
        }
#endif

        /// <summary>
        /// Binds a twelve-payload message to a command.
        /// </summary>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        public void Bind<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>
        {
            if (IsAsyncCommandType<TCommand>())
                _processor.BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(poolSize);
            else
                _processor.BindCommand<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(poolSize);

            LogBound<TCommand, TMessage>();
        }
#endif

        /// <summary>
        /// Removes a no-payload message binding for a command.
        /// </summary>
        public void Unbind<TCommand, TMessage>()
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage
        {
            if (IsAsyncCommandType<TCommand>())
                _processor.UnbindCommandAsync<TCommand, TMessage>();
            else
                _processor.UnbindCommand<TCommand, TMessage>();
        }

        /// <summary>
        /// Runs a no-payload synchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Run<TCommand>(
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
            where TCommand : Command, new()
        {
            LogRun<TCommand>(filePath, lineNumber);
            Runner.Run<TCommand>();
        }

        /// <summary>
        /// Runs a one-payload synchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Run<TCommand, T1>(
            T1 p1,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
            where TCommand : Command<T1>, new()
        {
            LogRun<TCommand>(filePath, lineNumber);
            Runner.Run<TCommand, T1>(p1);
        }

        /// <summary>
        /// Runs a two-payload synchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Run<TCommand, T1, T2>(
            T1 p1,
            T2 p2,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
            where TCommand : Command<T1, T2>, new()
        {
            LogRun<TCommand>(filePath, lineNumber);
            Runner.Run<TCommand, T1, T2>(p1, p2);
        }

        /// <summary>
        /// Runs a three-payload synchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Run<TCommand, T1, T2, T3>(
            T1 p1,
            T2 p2,
            T3 p3,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
            where TCommand : Command<T1, T2, T3>, new()
        {
            LogRun<TCommand>(filePath, lineNumber);
            Runner.Run<TCommand, T1, T2, T3>(p1, p2, p3);
        }

        /// <summary>
        /// Runs a four-payload synchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Run<TCommand, T1, T2, T3, T4>(
            T1 p1,
            T2 p2,
            T3 p3,
            T4 p4,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
            where TCommand : Command<T1, T2, T3, T4>, new()
        {
            LogRun<TCommand>(filePath, lineNumber);
            Runner.Run<TCommand, T1, T2, T3, T4>(p1, p2, p3, p4);
        }

        /// <summary>
        /// Runs a five-payload synchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Run<TCommand, T1, T2, T3, T4, T5>(
            T1 p1,
            T2 p2,
            T3 p3,
            T4 p4,
            T5 p5,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
            where TCommand : Command<T1, T2, T3, T4, T5>, new()
        {
            LogRun<TCommand>(filePath, lineNumber);
            Runner.Run<TCommand, T1, T2, T3, T4, T5>(p1, p2, p3, p4, p5);
        }

        /// <summary>
        /// Runs a six-payload synchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Run<TCommand, T1, T2, T3, T4, T5, T6>(
            T1 p1,
            T2 p2,
            T3 p3,
            T4 p4,
            T5 p5,
            T6 p6,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
            where TCommand : Command<T1, T2, T3, T4, T5, T6>, new()
        {
            LogRun<TCommand>(filePath, lineNumber);
            Runner.Run<TCommand, T1, T2, T3, T4, T5, T6>(p1, p2, p3, p4, p5, p6);
        }

        /// <summary>
        /// Runs a seven-payload synchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Run<TCommand, T1, T2, T3, T4, T5, T6, T7>(
            T1 p1,
            T2 p2,
            T3 p3,
            T4 p4,
            T5 p5,
            T6 p6,
            T7 p7,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
            where TCommand : Command<T1, T2, T3, T4, T5, T6, T7>, new()
        {
            LogRun<TCommand>(filePath, lineNumber);
            Runner.Run<TCommand, T1, T2, T3, T4, T5, T6, T7>(p1, p2, p3, p4, p5, p6, p7);
        }

        /// <summary>
        /// Runs an eight-payload synchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Run<TCommand, T1, T2, T3, T4, T5, T6, T7, T8>(
            T1 p1,
            T2 p2,
            T3 p3,
            T4 p4,
            T5 p5,
            T6 p6,
            T7 p7,
            T8 p8,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
            where TCommand : Command<T1, T2, T3, T4, T5, T6, T7, T8>, new()
        {
            LogRun<TCommand>(filePath, lineNumber);
            Runner.Run<TCommand, T1, T2, T3, T4, T5, T6, T7, T8>(p1, p2, p3, p4, p5, p6, p7, p8);
        }

        /// <summary>
        /// Runs a nine-payload synchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Run<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9>(
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
            [CallerLineNumber] int lineNumber = 0)
            where TCommand : Command<T1, T2, T3, T4, T5, T6, T7, T8, T9>, new()
        {
            LogRun<TCommand>(filePath, lineNumber);
            Runner.Run<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9>(p1, p2, p3, p4, p5, p6, p7, p8, p9);
        }

        /// <summary>
        /// Runs a ten-payload synchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Run<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(
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
            [CallerLineNumber] int lineNumber = 0)
            where TCommand : Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>, new()
        {
            LogRun<TCommand>(filePath, lineNumber);
            Runner.Run<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10);
        }

        /// <summary>
        /// Runs an eleven-payload synchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Run<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(
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
            [CallerLineNumber] int lineNumber = 0)
            where TCommand : Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>, new()
        {
            LogRun<TCommand>(filePath, lineNumber);
            Runner.Run<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11);
        }

        /// <summary>
        /// Runs a twelve-payload synchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Run<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(
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
            [CallerLineNumber] int lineNumber = 0)
            where TCommand : Command<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>, new()
        {
            LogRun<TCommand>(filePath, lineNumber);
            Runner.Run<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12);
        }

        /// <summary>
        /// Runs a no-payload asynchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task RunAsync<TCommand>() where TCommand : CommandAsync, new()
        {
            return Runner.RunAsync<TCommand>();
        }

        /// <summary>
        /// Runs a one-payload asynchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task RunAsync<TCommand, T1>(T1 p1) where TCommand : CommandAsync<T1>, new()
        {
            return Runner.RunAsync<TCommand, T1>(p1);
        }

        /// <summary>
        /// Runs a two-payload asynchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task RunAsync<TCommand, T1, T2>(T1 p1, T2 p2) where TCommand : CommandAsync<T1, T2>, new()
        {
            return Runner.RunAsync<TCommand, T1, T2>(p1, p2);
        }

        /// <summary>
        /// Runs a three-payload asynchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task RunAsync<TCommand, T1, T2, T3>(T1 p1, T2 p2, T3 p3) where TCommand : CommandAsync<T1, T2, T3>, new()
        {
            return Runner.RunAsync<TCommand, T1, T2, T3>(p1, p2, p3);
        }

        /// <summary>
        /// Runs a four-payload asynchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task RunAsync<TCommand, T1, T2, T3, T4>(T1 p1, T2 p2, T3 p3, T4 p4) where TCommand : CommandAsync<T1, T2, T3, T4>, new()
        {
            return Runner.RunAsync<TCommand, T1, T2, T3, T4>(p1, p2, p3, p4);
        }

        /// <summary>
        /// Runs a five-payload asynchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task RunAsync<TCommand, T1, T2, T3, T4, T5>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5) where TCommand : CommandAsync<T1, T2, T3, T4, T5>, new()
        {
            return Runner.RunAsync<TCommand, T1, T2, T3, T4, T5>(p1, p2, p3, p4, p5);
        }

        /// <summary>
        /// Runs a six-payload asynchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task RunAsync<TCommand, T1, T2, T3, T4, T5, T6>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6) where TCommand : CommandAsync<T1, T2, T3, T4, T5, T6>, new()
        {
            return Runner.RunAsync<TCommand, T1, T2, T3, T4, T5, T6>(p1, p2, p3, p4, p5, p6);
        }

        /// <summary>
        /// Runs a seven-payload asynchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task RunAsync<TCommand, T1, T2, T3, T4, T5, T6, T7>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7) where TCommand : CommandAsync<T1, T2, T3, T4, T5, T6, T7>, new()
        {
            return Runner.RunAsync<TCommand, T1, T2, T3, T4, T5, T6, T7>(p1, p2, p3, p4, p5, p6, p7);
        }

        /// <summary>
        /// Runs an eight-payload asynchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task RunAsync<TCommand, T1, T2, T3, T4, T5, T6, T7, T8>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8) where TCommand : CommandAsync<T1, T2, T3, T4, T5, T6, T7, T8>, new()
        {
            return Runner.RunAsync<TCommand, T1, T2, T3, T4, T5, T6, T7, T8>(p1, p2, p3, p4, p5, p6, p7, p8);
        }

        /// <summary>
        /// Runs a nine-payload asynchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task RunAsync<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9) where TCommand : CommandAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9>, new()
        {
            return Runner.RunAsync<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9>(p1, p2, p3, p4, p5, p6, p7, p8, p9);
        }

        /// <summary>
        /// Runs a ten-payload asynchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task RunAsync<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10) where TCommand : CommandAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>, new()
        {
            return Runner.RunAsync<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10);
        }

        /// <summary>
        /// Runs an eleven-payload asynchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task RunAsync<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10, T11 p11) where TCommand : CommandAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>, new()
        {
            return Runner.RunAsync<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11);
        }

        /// <summary>
        /// Runs a twelve-payload asynchronous command directly.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task RunAsync<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(T1 p1, T2 p2, T3 p3, T4 p4, T5 p5, T6 p6, T7 p7, T8 p8, T9 p9, T10 p10, T11 p11, T12 p12) where TCommand : CommandAsync<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>, new()
        {
            return Runner.RunAsync<TCommand, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12);
        }

        /// <summary>
        /// Returns whether any commands are bound to the supplied message type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasMessageBindings<TMessage>() where TMessage : IMessageBase
        {
            return BindingInfo.HasMessageBindings<TMessage>();
        }

        /// <summary>
        /// Returns whether any commands are bound to the supplied message type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasBindings(Type messageType)
        {
            return BindingInfo.HasBindings(messageType);
        }

        /// <summary>
        /// Returns whether the supplied command is bound to the supplied message type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsBound<TCommand, TMessage>()
            where TCommand : MvcCommandBase
            where TMessage : IMessageBase
        {
            return BindingInfo.IsBound<TCommand, TMessage>();
        }

        /// <summary>
        /// Returns whether the supplied command type is bound to the supplied message type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsBound(Type commandType, Type messageType)
        {
            return BindingInfo.IsBound(commandType, messageType);
        }

        /// <summary>
        /// Gets the number of message bindings for a command type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetCommandBindingCount<TCommand>() where TCommand : MvcCommandBase
        {
            return BindingInfo.GetCommandBindingCount<TCommand>();
        }

        /// <summary>
        /// Gets the number of message bindings for a command type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetBindingCountForCommand(Type commandType)
        {
            return BindingInfo.GetBindingCountForCommand(commandType);
        }

        /// <summary>
        /// Gets the number of distinct message types with command bindings.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int GetBoundMessageCount()
        {
            return BindingInfo.GetBoundMessageCount();
        }

        /// <summary>
        /// Creates or resizes the pool used for a command type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void CreatePool<TCommand>(uint poolSize) where TCommand : MvcCommandBase, new()
        {
            PoolCreator.CreatePool<TCommand>(poolSize);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LogRun<TCommand>(string filePath, int lineNumber)
        {
            var module = _context.ResolveModule();
            if (_context.Actor is MvcModule)
            {
                MvcLogInternal.LogCommandRun(typeof(TCommand).Name, module, filePath, lineNumber);
                return;
            }

            MvcLogInternal.LogCommandExecutedFromCommand(_context.Actor.GetType().Name, typeof(TCommand).Name, module, filePath, lineNumber);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.FireCommandExecuted(typeof(TCommand), null, module?.GetType());
#endif
        }

        private void LogBound<TCommand, TMessage>()
            where TCommand : MvcCommandBase
        {
            if (_context.Actor is not MvcModule module || module.SuppressCommandBindingLog)
            {
                return;
            }

            MvcCompositionStyleWarning.WarnIfDisabled(
                MvcCompositionStyle.Code,
                $"code command binding '{typeof(TCommand).Name}' to '{typeof(TMessage).Name}' in module '{module.GetType().Name}'");

            var caller = new System.Diagnostics.StackTrace(true).GetFrame(2);
            MvcLogInternal.LogCommandBound(
                typeof(TMessage).Name,
                typeof(TCommand).Name,
                module,
                MvcLogContext.RegistrationSource.Code,
                null,
                caller?.GetFileName(),
                caller?.GetFileLineNumber() ?? 0);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.FireCommandBound(typeof(TCommand), typeof(TMessage), module.GetType(), MvcLogContext.RegistrationSource.Code);
#endif
        }
    }
}

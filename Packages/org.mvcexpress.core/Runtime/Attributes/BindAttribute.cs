using System;

namespace mvcExpress
{
    /// <summary>
    /// Marks a <see cref="Command"/> or <see cref="CommandAsync"/> class for declarative
    /// binding to a message type during module initialization.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use <c>[Bind]</c> when you want a command to declare its own message binding instead of
    /// wiring it in <see cref="MvcModule.BindCommands"/>. This is the attribute-based registration
    /// method - it runs after Unity inspector bindings (<see cref="CommandBindingsBehaviour"/>) and
    /// before code bindings in <c>BindCommands</c>.
    /// </para>
    /// <para>
    /// A <c>TargetModuleType</c> is <b>required</b>: the shared message bus must know which
    /// module owns the binding so it does not accidentally inject the command's dependencies
    /// from the wrong module's DI container. For a pooled singleton-style command,
    /// set <see cref="PoolSize"/> to 1 (equivalent to <c>CommandSingleton</c> behaviour).
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [Bind(typeof(StartGameMessage), typeof(GameplayModule))]
    /// public sealed class StartGameCommand : Command
    /// {
    ///     [Inject] private GameStateProxy _gameState;
    ///     public override void Execute() { _gameState.Start(); }
    /// }
    ///
    /// // Async variant
    /// [Bind(typeof(LoadLevelMessage), typeof(GameplayModule), IsAsync = true)]
    /// public sealed class LoadLevelCommand : CommandAsync
    /// {
    ///     public override async System.Threading.Tasks.Task ExecuteAsync() { await LoadAsync(); }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class BindAttribute : Attribute
    {
        /// <summary>
        /// Message type that triggers the command.
        /// </summary>
        public Type MessageType { get; }

        /// <summary>
        /// Concrete module type that owns the binding.
        /// </summary>
        public Type TargetModuleType { get; }

        /// <summary>
        /// Gets or sets whether the binding should execute through the async command path.
        /// </summary>
        /// <remarks>
        /// Normally the framework can infer async command types. This flag is kept for
        /// declarative registry metadata and compatibility with binding helpers.
        /// </remarks>
        public bool IsAsync { get; set; }

        /// <summary>
        /// Gets or sets the maximum pool size for the bound command type.
        /// </summary>
        /// <remarks>Zero disables pooling.</remarks>
        public uint PoolSize { get; set; }

        /// <summary>
        /// Creates a command binding for a specific module.
        /// </summary>
        /// <param name="messageType">Message type to bind to. Must implement <see cref="IMessageBase"/>.</param>
        /// <param name="targetModuleType">Concrete module type that owns this command binding.</param>
        public BindAttribute(Type messageType, Type targetModuleType)
        {
            MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
            TargetModuleType = targetModuleType ?? throw new ArgumentNullException(
                nameof(targetModuleType),
                "[Bind] requires a target module type. Use [Bind(typeof(YourMessage), typeof(YourModule))].");
        }

        /// <summary>
        /// Validates the attribute configuration during assembly scanning.
        /// </summary>
        /// <param name="commandType">Command type decorated with this attribute.</param>
        internal void Validate(Type commandType)
        {
            if (commandType == null)
                throw new ArgumentNullException(nameof(commandType));

            // Only typed message markers can participate in command binding.
            if (!typeof(IMessageBase).IsAssignableFrom(MessageType))
            {
                throw new InvalidOperationException(
                    $"[BindAttribute] Message type '{MessageType.FullName}' must implement an IMessage interface. " +
                    $"Command: '{commandType.FullName}'");
            }

            // Binding to a concrete module prevents accidental cross-module dependency failures.
            if (!typeof(MvcModule).IsAssignableFrom(TargetModuleType))
            {
                throw new InvalidOperationException(
                    $"[BindAttribute] Target module type '{TargetModuleType.FullName}' must inherit from MvcModule. " +
                    $"Command: '{commandType.FullName}'");
            }
        }
    }
}

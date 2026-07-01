using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Interfaces;
using mvcExpress.Internal.Commands;
using mvcExpress.Logging;
using System;

namespace mvcExpress
{
    /// <summary>
    /// Base class for synchronous and asynchronous command implementations.
    /// </summary>
    /// <remarks>
    /// Commands exist to isolate one operation or use case behind a typed message or direct
    /// command run. The framework initializes commands with module context, injects dependencies,
    /// and optionally reuses instances through command pools.
    /// </remarks>
    public abstract partial class MvcCommandBase
    {
        /// <summary>
        /// Module class type for this command (debugging purposes).
        /// </summary>
        public Type ModuleType { get; private set; }

        // Core dependencies are refreshed before each execution so pooled commands
        // always operate against the current module context.
        private MvcModule _moduleContext;
        private MvcDiContainer _diContainer;
        private MvcActorContext _actorContext;
        private MessengerApi _messenger;
        private CommandContainerApi _container;
        private CommandGlobalContainerApi _globalContainer;
        private CommanderApi _commander;

        internal MvcModule ModuleContext => _moduleContext;

        // Validity flag for pool management (invalidated when transient dependencies are removed)
        internal volatile bool IsValid = true;

        // Set to true after the first Initialize() call completes injection + OnInitialize().
        // Pooled commands skip both on subsequent executions; the pool-clearing mechanism
        // (triggered by transient dep removal) ensures stale refs never survive in the pool.
        private bool _hasBeenInjected;

        /// <summary>
        /// Publishes typed messages from this command.
        /// </summary>
        protected MessengerApi Messenger => _messenger;

        /// <summary>
        /// Resolves and dynamically manages module-scoped dependencies from this command.
        /// </summary>
        protected CommandContainerApi Container => _container;

        /// <summary>
        /// Resolves and dynamically manages global dependencies from this command.
        /// </summary>
        protected CommandGlobalContainerApi Global => _globalContainer;

        /// <summary>
        /// Runs or binds commands from this command.
        /// </summary>
        protected CommanderApi Commander => _commander;

        /// <summary>
        /// Refreshes command context and, on the first execution only, injects dependencies
        /// and calls <see cref="OnInitialize"/>.
        /// </summary>
        /// <remarks>
        /// Context structs (Messenger, Container, Commander) are cheap value-type assignments
        /// refreshed every execution. Dependency injection and <see cref="OnInitialize"/> run
        /// once when the command object is first created. Pooled commands skip both on reuse
        /// because the pool is cleared whenever a transient dependency changes, guaranteeing
        /// that any command returned to the pool still holds valid references.
        /// </remarks>
        internal void Initialize(MvcModule module, MvcDiContainer diContainer, IMessageBus messageBus, ICommandProcessorInternal commandProcessor)
        {
            ModuleType = module.GetType();
            this._moduleContext = module;
            this._diContainer = diContainer;
            _actorContext = new MvcActorContext(
                this,
                module,
                ModuleType,
                diContainer,
                messageBus,
                MvcLogContext.LogCategory.Command);
            _messenger = new MessengerApi(_actorContext);
            _container = new CommandContainerApi(_actorContext);
            _globalContainer = new CommandGlobalContainerApi();
            _commander = new CommanderApi(_actorContext, commandProcessor);

            if (!_hasBeenInjected)
            {
                var processor = commandProcessor as MvcCommandProcessor;
                using (diContainer.BeginScopedResolutionScope())
                {
                    MvcInjectionUtility.InjectMembers(this, diContainer, useViewScope: false, processor);
                }
                _hasBeenInjected = true;
                OnInitialize();
            }
        }

        /// <summary>
        /// Invalidate this command (will be discarded instead of returned to pool).
        /// </summary>
        internal void Invalidate()
        {
            IsValid = false;
        }

        /// <summary>
        /// Called once when the command object is first created, after dependency injection completes.
        /// Override for one-time setup that uses injected dependencies.
        /// </summary>
        /// <remarks>
        /// For logic that must run on every execution, put it in <c>Execute()</c> instead.
        /// Pooled commands are reused across many executions; <c>OnInitialize</c> is not called again
        /// unless the command object itself is discarded and recreated (e.g. after a transient
        /// dependency changes and the pool is cleared).
        /// </remarks>
        protected virtual void OnInitialize() { }
    }
}

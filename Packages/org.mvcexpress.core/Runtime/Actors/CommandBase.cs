using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Interfaces;
using mvcExpress.Internal.Commands;
using mvcExpress.Logging;
using System;
using System.Threading;

namespace mvcExpress
{
    /// <summary>
    /// Base class for synchronous and asynchronous command implementations.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Commands exist to isolate one operation or use case behind a typed message or direct
    /// command run. The framework initializes commands with module context, injects dependencies,
    /// and optionally reuses instances through command pools.
    /// </para>
    /// <para>
    /// <b>Discard-time cleanup (the <c>OnInitialize</c> mirror image):</b> implement
    /// <see cref="System.IDisposable"/> on your concrete command if it needs to release
    /// resources once, when the instance is permanently thrown away. The internal command
    /// pool calls <c>Dispose()</c> exactly once per instance, and only when that instance will
    /// never be executed again - never when it is returned to the pool for reuse. This happens
    /// when: pooling is disabled for the command type (<c>poolSize == 0</c>), the pool is
    /// already at capacity, the pool is resized smaller or cleared (e.g. a transient
    /// dependency the command used was unregistered), or the command resolved a scoped
    /// dependency / was marked invalid during its own execution (see <see cref="IsValid"/>).
    /// Do not rely on <c>Dispose()</c> for cleanup that must happen after every execution -
    /// pooled commands are reused many times without a matching <c>Dispose()</c> call in
    /// between; put per-execution cleanup at the end of <c>Execute()</c>/<c>ExecuteAsync()</c>
    /// instead.
    /// </para>
    /// <para>
    /// Prefer implementing <see cref="System.IDisposable"/> explicitly
    /// (<c>void IDisposable.Dispose()</c>) rather than with a public <c>Dispose()</c> method.
    /// Commands are constructed and disposed exclusively by the framework's pool - a public
    /// <c>Dispose()</c> would be a callable, discoverable part of your command's API for no
    /// reason, and nothing about command ownership resembles the "caller owns disposal"
    /// pattern <c>IDisposable</c> normally signals. Explicit implementation keeps the method
    /// reachable only through an <c>IDisposable</c> reference (which is exactly how the pool
    /// invokes it) while keeping it off <c>myCommand.</c> intellisense for everyone else.
    /// </para>
    /// <example>
    /// <code>
    /// public class SpawnEnemyCommand : Command, IDisposable
    /// {
    ///     private NativeArray&lt;float3&gt; _scratchBuffer;
    ///
    ///     protected override void OnInitialize()
    ///     {
    ///         _scratchBuffer = new NativeArray&lt;float3&gt;(64, Allocator.Persistent);
    ///     }
    ///
    ///     public override void Execute()
    ///     {
    ///         // use _scratchBuffer ...
    ///     }
    ///
    ///     // Explicit implementation: only the pool's `is IDisposable` check can reach this.
    ///     void IDisposable.Dispose()
    ///     {
    ///         if (_scratchBuffer.IsCreated)
    ///             _scratchBuffer.Dispose();
    ///     }
    /// }
    /// </code>
    /// </example>
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
        private CancellationToken _cancelToken;

        internal MvcModule ModuleContext => _moduleContext;

        // Exposed as `protected CancelToken` only by MvcAsyncCommandBase - kept internal
        // here so sync Command/MvcSyncCommandBase (which also derives from this class)
        // never surfaces a CancelToken member.
        internal CancellationToken CancelTokenInternal => _cancelToken;

        // Validity flag for pool management (invalidated when transient dependencies are removed).
        //
        // Read/write surface for this flag is `internal`, and AssemblyInfo.cs grants
        // InternalsVisibleTo to org.mvcexpress.core.Editor, org.mvcexpress.core.Tests, AND
        // org.mvcexpress.console.Editor - an assembly that does not exist anywhere in this
        // repo. That third grant only makes sense as a hook for an external/companion runtime
        // debug console: something that lists live pooled command instances and lets a
        // developer flag a specific one (e.g. one observed misbehaving, or holding a reference
        // to a torn-down scene object) so it is discarded on its next return instead of being
        // silently recycled and handed back out. Plausible tool-facing uses:
        //   - A "kill this instance" action in a command/pool inspector window.
        //   - Marking instances stale after a hot-reload or domain reload the tool detects,
        //     without waiting for the transient-dependency-removal path to notice.
        //   - Surfacing IsValid as a read-only column (alongside GetPoolStatistics()) so a
        //     console can show which pooled instances are already doomed before they cycle out.
        // None of this is implemented in-repo today; `Invalidate()` (below) is never called
        // by any code in this package or its tests.
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
        /// A given command instance is only ever created by, and returned to, the pool slot
        /// owned by one specific processor, so the module, container, bus, and processor passed
        /// in here are invariant for the lifetime of the instance - they are only ever built
        /// once, on first execution, and never need to be rebuilt afterward. The context rebuild
        /// and the injection/<see cref="OnInitialize"/> one-time setup therefore share the same
        /// <see cref="_hasBeenInjected"/> guard: both become stale under the exact same
        /// condition (never, for a given instance).
        /// </remarks>
        internal void Initialize(MvcModule module, MvcDiContainer diContainer, IMessageBus messageBus, ICommandProcessorInternal commandProcessor)
        {
            if (_hasBeenInjected)
                return;

            ModuleType = module.ModuleType;
            this._moduleContext = module;
            this._diContainer = diContainer;
            this._cancelToken = commandProcessor.CancelToken;
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

            var processor = commandProcessor as MvcCommandProcessor;
            using (diContainer.BeginScopedResolutionScope())
            {
                MvcInjectionUtility.InjectMembers(this, diContainer, useViewScope: false, processor);
            }
            _hasBeenInjected = true;
            OnInitialize();
        }

        /// <summary>
        /// Invalidate this command (will be discarded instead of returned to pool).
        /// </summary>
        /// <remarks>
        /// Same investigation note as <see cref="IsValid"/>: nothing in this package calls
        /// this today. A concrete command can call it on itself mid-<c>Execute()</c>/
        /// <c>ExecuteAsync()</c> (it is <c>internal</c>, so only code in this assembly or an
        /// <c>InternalsVisibleTo</c> friend - Tests, Editor, or the external console tool - can
        /// reach it) to force discard of this specific instance regardless of pool capacity.
        /// <see cref="MvcCommandBase"/>'s remarks cover what "discard" means for
        /// <see cref="System.IDisposable"/> commands: an invalidated instance is disposed just
        /// like any other discarded one (see <c>ReturnToPoolGeneric</c> in
        /// <c>MvcCommandProcessor</c>).
        /// </remarks>
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

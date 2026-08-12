using mvcExpress.Logging;
using System.Runtime.CompilerServices;

namespace mvcExpress
{
    /// <summary>
    /// Read-only facade for resolving dependencies from the current actor's module container.
    /// </summary>
    /// <remarks>
    /// Proxies and mediators use this API to consume module-scoped dependencies without
    /// gaining registration authority. The active logic/view scope is fixed by the actor's
    /// type - mediators always resolve view-scope, everything else always resolves
    /// logic-scope - not by whether a mediator happens to be inside its <c>OnInitialized()</c>
    /// call when it resolves. See <see cref="Internal.DependencyInjection.MvcDiContainer.ResolveWithScope{T}"/>.
    /// </remarks>
    public readonly struct ModuleContainerApi
    {
        private readonly MvcActorContext _context;

        internal ModuleContainerApi(MvcActorContext context)
        {
            _context = context;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool UseViewScope => _context.Category == MvcLogContext.LogCategory.Mediator;

        /// <summary>
        /// Resolves a dependency from the current actor scope.
        /// </summary>
        /// <typeparam name="T">Dependency type to resolve.</typeparam>
        /// <returns>The registered instance of <typeparamref name="T"/>.</returns>
        /// <exception cref="System.InvalidOperationException">
        /// Thrown when <typeparamref name="T"/> is not registered in the active scope.
        /// </exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Resolve<T>()
        {
            return _context.DiContainer.ResolveWithScope<T>(UseViewScope, _context.Actor);
        }

        /// <summary>
        /// Attempts to resolve a dependency from the current actor scope without throwing.
        /// </summary>
        /// <typeparam name="T">Dependency type to resolve.</typeparam>
        /// <param name="value">Resolved dependency when available; otherwise the default value.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryResolve<T>(out T value)
        {
            return _context.DiContainer.TryResolveWithScope<T>(UseViewScope, out value);
        }
    }
}

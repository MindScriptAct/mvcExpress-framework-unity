using System.Runtime.CompilerServices;

namespace mvcExpress
{
    /// <summary>
    /// Resolve-only facade for the application-wide (<see cref="MvcFacade"/>) dependency container.
    /// </summary>
    /// <remarks>
    /// Given to mediators and proxies that need cross-module shared dependencies but must NOT
    /// register or mutate the global container. Use <see cref="CommandGlobalContainerApi"/> from
    /// commands or <see cref="ModuleGlobalContainerApi"/> from modules when write access is also
    /// required.
    /// </remarks>
    public readonly struct GlobalContainerApi
    {
        // Retaining the actor context provides both its fixed Logic/View domain and the owner
        // identity required by per-injectee scoped resolution. The ambient container flag is
        // only reliable while initialization is on the stack.
        private readonly MvcActorContext _context;

        internal GlobalContainerApi(MvcActorContext context)
        {
            _context = context;
        }

        private bool UseViewScope => _context.Category == Logging.MvcLogContext.LogCategory.Mediator;

        /// <summary>
        /// Resolves a dependency from the global container.
        /// </summary>
        /// <typeparam name="T">Dependency type to resolve.</typeparam>
        /// <returns>The registered instance of <typeparamref name="T"/>.</returns>
        /// <remarks>Throws if <typeparamref name="T"/> is not registered in the global container.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Resolve<T>()
        {
            return MvcFacade.Global.ResolveWithScope<T>(UseViewScope, _context.Actor);
        }

        /// <summary>
        /// Attempts to resolve a dependency from the global container without throwing.
        /// </summary>
        /// <typeparam name="T">Dependency type to resolve.</typeparam>
        /// <param name="value">Resolved dependency when available; otherwise the default value for <typeparamref name="T"/>.</param>
        /// <returns><c>true</c> when <typeparamref name="T"/> was found; <c>false</c> when not registered.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryResolve<T>(out T value)
        {
            return MvcFacade.Global.TryResolveWithScope<T>(UseViewScope, out value, _context.Actor);
        }
    }
}

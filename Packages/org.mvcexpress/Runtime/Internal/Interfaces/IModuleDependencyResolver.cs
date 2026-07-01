using System;

namespace mvcExpress.Internal.Interfaces
{
    /// <summary>
    /// Interface for resolving module-scoped dependencies.
    /// Automatically detects logic vs view scope based on injection context.
    /// </summary>
    public interface IModuleDependencyResolver
    {
        /// <summary>
        /// Resolves a dependency from the current module's container.
        /// Automatically uses view scope when called from mediators, otherwise uses logic scope.
        /// Supports both concrete types and interfaces.
        /// </summary>
        /// <typeparam name="T">Type to resolve</typeparam>
        /// <returns>The resolved instance</returns>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the type is not registered in the active scope.
        /// </exception>
        T Resolve<T>();

        /// <summary>
        /// Try resolve from the active scope without throwing.
        /// Automatically detects view scope (mediators) vs logic scope (commands/proxies).
        /// </summary>
        /// <typeparam name="T">Type to resolve</typeparam>
        /// <param name="value">The resolved instance if successful, default value otherwise</param>
        /// <returns>True if successful, false otherwise</returns>
        bool TryResolve<T>(out T value);
    }
}

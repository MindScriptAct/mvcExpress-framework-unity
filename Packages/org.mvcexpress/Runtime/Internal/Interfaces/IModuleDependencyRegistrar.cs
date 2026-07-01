using System;
using mvcExpress.Internal.DependencyInjection;
using UnityEngine;

namespace mvcExpress.Internal.Interfaces
{
    /// <summary>
    /// Interface for registering and unregistering module-scoped dependencies.
    /// Provides fluent API for dependency registration with lifecycle management.
    /// </summary>
    public interface IModuleDependencyRegistrar
    {
        /// <summary>
        /// Begin registration of a dependency instance using fluent builder pattern.
        /// Must be completed by calling AsPersistent() or AsTransient() on the builder.
        /// </summary>
        /// <typeparam name="T">Type of the instance to register</typeparam>
        /// <param name="instance">The instance to register</param>
        /// <returns>A fluent builder for configuring the registration</returns>
        /// <example>
        /// // Register to logic only as concrete type:
        /// Container.Register(proxy).ToLogic().AsPersistent();
        /// 
        /// // Register to view only as interface:
        /// Container.Register(proxy).ToViewAs&lt;IReadOnly&gt;().AsPersistent();
        /// 
        /// // Register to both with different types:
        /// Container.Register(proxy).ToLogic().ToViewAs&lt;IReadOnly&gt;().AsPersistent();
        /// 
        /// // Register to both as same interface:
        /// Container.Register(proxy).ToLogicAs&lt;IProxy&gt;().ToViewAs&lt;IProxy&gt;().AsPersistent();
        /// </example>
        RegistrationBuilder<T> Register<T>(T instance);

        /// <summary>
        /// Begin registration of a dependency instance using fluent builder pattern (runtime version).
        /// Must be completed by calling AsPersistent() or AsTransient() on the builder.
        /// Use this overload when working with Type objects at runtime (reflection, configuration, etc.).
        /// </summary>
        /// <param name="instance">The instance to register</param>
        /// <param name="type">The type to register the instance as</param>
        /// <returns>A non-generic fluent builder for configuring the registration</returns>
        /// <example>
        /// // Runtime/reflection scenario:
        /// Type serviceType = Type.GetType(configuredTypeName);
        /// object instance = Activator.CreateInstance(serviceType);
        /// Container.Register(instance, serviceType).ToLogic().AsPersistent();
        /// </example>
        RegistrationBuilder Register(object instance, Type type);

        /// <summary>
        /// Check if a type is registered as transient (can be destroyed/unregistered).
        /// </summary>
        /// <param name="type">Type to check</param>
        /// <returns>True if the type is transient, false if persistent or not registered</returns>
        bool IsTransient(Type type);

        /// <summary>
        /// Unregisters a type from all containers (logic and view).
        /// For persistent types, throws an error (cannot be destroyed).
        /// For transient types, triggers command invalidation.
        /// </summary>
        /// <typeparam name="T">Type to unregister</typeparam>
        /// <exception cref="InvalidOperationException">
        /// Thrown if attempting to unregister a persistent dependency.
        /// Persistent dependencies cannot be destroyed during module lifetime.
        /// </exception>
        void Unregister<T>();

        /// <summary>
        /// Unregisters a type from all containers (logic and view).
        /// For persistent types, throws an error (cannot be destroyed).
        /// For transient types, triggers command invalidation.
        /// </summary>
        /// <param name="type">Type to unregister</param>
        /// <exception cref="InvalidOperationException">
        /// Thrown if attempting to unregister a persistent dependency.
        /// Persistent dependencies cannot be destroyed during module lifetime.
        /// </exception>
        void Unregister(Type type);
    }

    public interface IModuleBehaviourRegistrar
    {
        /// <summary>
        /// Register a behaviour with the module.
        /// </summary>
        /// <typeparam name="TBehaviour">The type of the behaviour to register</typeparam>
        /// <returns>A fluent builder for configuring the registration</returns>
        RegistrationBuilder<TBehaviour> RegisterBehaviour<TBehaviour>() where TBehaviour : MonoBehaviour;
    }
}

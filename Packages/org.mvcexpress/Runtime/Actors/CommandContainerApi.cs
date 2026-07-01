using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Logging;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace mvcExpress
{
    /// <summary>
    /// Provides commands with resolve and dynamic-registration access to the owning module's container.
    /// </summary>
    /// <remarks>
    /// Commands primarily use this API to resolve services and proxies. The registration methods
    /// (<see cref="Register{T}"/>, <see cref="Unregister{T}"/>) exist for advanced runtime
    /// composition - dynamically adding or removing a dependency after the module's normal
    /// initialization phases have completed. For global-container access from a command, use
    /// <see cref="CommandGlobalContainerApi"/> instead.
    /// </remarks>
    public readonly struct CommandContainerApi
    {
        private readonly MvcActorContext _context;

        internal CommandContainerApi(MvcActorContext context)
        {
            _context = context;
        }

        /// <summary>
        /// Begins registering an instance in the owning module container.
        /// </summary>
        /// <typeparam name="T">Concrete compile-time type of the instance.</typeparam>
        /// <param name="instance">Instance to register.</param>
        /// <param name="filePath">Do not pass; compiler-filled source file path for diagnostics.</param>
        /// <param name="lineNumber">Do not pass; compiler-filled line number for diagnostics.</param>
        /// <returns>
        /// A fluent <see cref="RegistrationBuilder{T}"/> used to choose scope and lifetime.
        /// Call <c>.ToLogic()</c> for service/proxy visibility, <c>.ToView()</c> for mediator
        /// visibility, or the typed variants (<c>.ToLogicAs&lt;TInterface&gt;()</c> /
        /// <c>.ToViewAs&lt;TInterface&gt;()</c>) to register under a specific interface.
        /// Append <c>.AsPersistent()</c> to survive module unload or <c>.AsTransient()</c> for
        /// standard module-lifetime scoping.
        /// </returns>
        /// <remarks>
        /// Proxies registered here after module initialization are immediately initialized
        /// (bypassing the normal init pipeline). Prefer <see cref="MvcModule.RegisterProxies"/>
        /// for setup-time registration.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RegistrationBuilder<T> Register<T>(
            T instance,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            var module = _context.ResolveModule();
            module?.OnContainerRegistering(instance);
            WarnForCodeRegistration(instance, module);

            var builder = _context.DiContainer.Register(instance);
            LogRegistration(typeof(T), instance, module, filePath, lineNumber);
            InitializeDynamicProxy(instance, module);
            return builder;
        }

        /// <summary>
        /// Begins registering an instance under an explicit service type.
        /// </summary>
        /// <param name="instance">Instance to register.</param>
        /// <param name="type">Type or interface that should identify this registration in the container.</param>
        /// <param name="filePath">Do not pass; compiler-filled source file path for diagnostics.</param>
        /// <param name="lineNumber">Do not pass; compiler-filled line number for diagnostics.</param>
        /// <returns>A fluent registration builder used to choose scope and lifetime.</returns>
        /// <remarks>
        /// Use this overload when the concrete type should not be the lookup key - for example,
        /// when the rest of the module resolves by interface.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RegistrationBuilder Register(
            object instance,
            Type type,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            var module = _context.ResolveModule();
            module?.OnContainerRegistering(instance);
            WarnForCodeRegistration(instance, module);

            var builder = _context.DiContainer.Register(instance, type);
            LogRegistration(type, instance, module, filePath, lineNumber);
            InitializeDynamicProxy(instance, module);
            return builder;
        }

        /// <summary>
        /// Returns whether the supplied type is registered with transient lifetime in the module container.
        /// </summary>
        /// <param name="type">Registered type to inspect.</param>
        /// <returns><c>true</c> when the registration was created with <c>.AsTransient()</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsTransient(Type type)
        {
            return _context.DiContainer.IsTransient(type);
        }

        /// <summary>
        /// Removes the registration for <typeparamref name="T"/> from the owning module container.
        /// </summary>
        /// <remarks>Safe to call at runtime; typically paired with a prior <see cref="Register{T}"/> call.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unregister<T>()
        {
            _context.DiContainer.Unregister<T>();
        }

        /// <summary>
        /// Removes the registration for the supplied type from the owning module container.
        /// </summary>
        /// <param name="type">Registered type to remove.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unregister(Type type)
        {
            _context.DiContainer.Unregister(type);
        }

        /// <summary>
        /// Creates a new GameObject, adds <typeparamref name="TBehaviour"/> to it, and registers it
        /// in the module's Model container hierarchy.
        /// </summary>
        /// <typeparam name="TBehaviour">MonoBehaviour type to create and register.</typeparam>
        /// <returns>A fluent registration builder used to choose scope and lifetime.</returns>
        /// <remarks>
        /// Use this instead of <see cref="Register{T}"/> when the dependency must be a MonoBehaviour.
        /// The new GameObject is parented under the module's Model container. Throws if the command
        /// has no module reference.
        /// </remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RegistrationBuilder<TBehaviour> RegisterBehaviour<TBehaviour>() where TBehaviour : MonoBehaviour
        {
            var module = _context.ResolveModule();
            if (module == null)
            {
                throw new InvalidOperationException("[MvcExpress] Command is not initialized with a module reference. Cannot RegisterBehaviour.");
            }

            return new ModuleRegistrationContainerApi(module).RegisterBehaviour<TBehaviour>();
        }

        /// <summary>
        /// Resolves a dependency from the command's logic scope (module container).
        /// </summary>
        /// <typeparam name="T">Dependency type to resolve.</typeparam>
        /// <returns>The registered instance of <typeparamref name="T"/>.</returns>
        /// <remarks>Throws if <typeparamref name="T"/> is not registered in the module container.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Resolve<T>()
        {
            return _context.DiContainer.Resolve<T>(caller: _context.Actor);
        }

        /// <summary>
        /// Attempts to resolve a dependency from the command's logic scope without throwing.
        /// </summary>
        /// <typeparam name="T">Dependency type to resolve.</typeparam>
        /// <param name="value">Resolved dependency when available; otherwise the default value for <typeparamref name="T"/>.</param>
        /// <returns><c>true</c> when <typeparamref name="T"/> was found; <c>false</c> when not registered.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryResolve<T>(out T value)
        {
            return _context.DiContainer.TryResolve<T>(out value);
        }

        // Dynamically registered proxies need framework context immediately because
        // they are added after the normal module initialization pipeline has passed.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InitializeDynamicProxy(object instance, MvcModule module)
        {
            if (module == null)
            {
                return;
            }

            if (instance is Proxy proxy)
            {
                proxy.Initialize(module, module.MessageBus, module.DiContainer, deferOnInitialized: false);
            }
            else if (instance is ProxyBehaviour proxyBehaviour)
            {
                proxyBehaviour.Initialize(module, module.DiContainer, module.MessageBus, deferOnInitialized: false);
            }
        }

        // Keep command-origin registration logs distinguishable from setup-time logs.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LogRegistration(Type type, object instance, MvcModule module, string filePath, int lineNumber)
        {
            if (module == null || instance == null)
            {
                return;
            }

            var commandTypeName = _context.Actor.GetType().Name;

            if (instance is Proxy || instance is ProxyBehaviour)
            {
                MvcLogInternal.LogProxyRegistered(
                    type.Name,
                    module,
                    MvcLogContext.RegistrationSource.Code,
                    null,
                    filePath,
                    lineNumber,
                    commandTypeName);
                return;
            }

            MvcLogInternal.LogServiceRegistered(
                type.Name,
                module,
                MvcLogContext.RegistrationSource.Code,
                null,
                filePath,
                lineNumber,
                commandTypeName);
        }

        // Warn when a project disables code composition but a command registers dependencies at runtime.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WarnForCodeRegistration(object instance, MvcModule module)
        {
            if (instance == null)
                return;

            var moduleName = module != null ? module.GetType().Name : "unknown module";
            MvcCompositionStyleWarning.WarnIfDisabled(
                MvcCompositionStyle.Code,
                $"command code registration of '{instance.GetType().Name}' in module '{moduleName}'");
        }
    }
}

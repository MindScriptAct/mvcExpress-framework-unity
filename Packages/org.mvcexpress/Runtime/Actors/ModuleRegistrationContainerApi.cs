using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Logging;
using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace mvcExpress
{
    /// <summary>
    /// Full registration and resolution API for a module's own isolated dependency container.
    /// </summary>
    /// <remarks>
    /// This is the primary composition surface used from <see cref="MvcModule.RegisterServices"/>
    /// and <see cref="MvcModule.RegisterProxies"/>. Unlike the actor-facing read-only APIs
    /// (<see cref="ModuleContainerApi"/>, <see cref="GlobalContainerApi"/>), this struct is
    /// intentionally privileged - it is only ever handed to the module's own lifecycle methods.
    /// The fluent builders it returns control whether each dependency is visible to logic actors,
    /// view actors, or both, and whether it survives a module unload.
    /// </remarks>
    public readonly struct ModuleRegistrationContainerApi
    {
        private readonly MvcModule _module;

        internal ModuleRegistrationContainerApi(MvcModule module)
        {
            _module = module;
        }

        /// <summary>
        /// Begins registering an instance in the module container.
        /// </summary>
        /// <typeparam name="T">Concrete compile-time type of the instance.</typeparam>
        /// <param name="instance">Instance to register.</param>
        /// <param name="filePath">Do not pass; compiler-filled source file path for diagnostics.</param>
        /// <param name="lineNumber">Do not pass; compiler-filled line number for diagnostics.</param>
        /// <returns>
        /// A fluent <see cref="RegistrationBuilder{T}"/> used to choose scope and lifetime.
        /// Call <c>.ToLogic()</c> to make the dependency visible to services, proxies, and
        /// commands; <c>.ToView()</c> to make it visible to mediators; or the typed variants
        /// (<c>.ToLogicAs&lt;TInterface&gt;()</c> / <c>.ToViewAs&lt;TInterface&gt;()</c>) to
        /// register under a specific interface type. Append <c>.AsPersistent()</c> to keep the
        /// instance alive when the module unloads (global lifetime) or <c>.AsTransient()</c>
        /// for standard module-lifetime scoping.
        /// </returns>
        /// <remarks>
        /// Called from <see cref="MvcModule.RegisterServices"/> or
        /// <see cref="MvcModule.RegisterProxies"/>. Logs are emitted with caller file/line for
        /// tooling traceability.
        /// </remarks>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RegistrationBuilder<T> Register<T>(
            T instance,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            _module.EnsureCoreServicesInitialized();
            _module.OnContainerRegistering(instance);
            WarnForCodeRegistration(instance);

            var registration = _module.DiContainer.Register(instance);
            LogRegistration(typeof(T), instance, filePath, lineNumber);
            return registration;
        }
#endif

        /// <summary>
        /// Begins registering an instance under an explicit type.
        /// </summary>
        /// <param name="instance">Instance to register.</param>
        /// <param name="type">Type or interface that should identify this registration in the container.</param>
        /// <param name="filePath">Do not pass; compiler-filled source file path for diagnostics.</param>
        /// <param name="lineNumber">Do not pass; compiler-filled line number for diagnostics.</param>
        /// <returns>A fluent registration builder used to choose scope and lifetime.</returns>
        /// <remarks>
        /// Use this overload when the concrete type should not be the lookup key - for example,
        /// when commands and proxies resolve by interface.
        /// </remarks>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RegistrationBuilder Register(
            object instance,
            Type type,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            _module.EnsureCoreServicesInitialized();
            _module.OnContainerRegistering(instance);
            WarnForCodeRegistration(instance);

            var registration = _module.DiContainer.Register(instance, type);
            LogRegistration(type, instance, filePath, lineNumber);
            return registration;
        }
#endif

        /// <summary>
        /// Returns whether the supplied type is registered with transient lifetime in the module container.
        /// </summary>
        /// <param name="type">Registered type to inspect.</param>
        /// <returns><c>true</c> when the registration was created with <c>.AsTransient()</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsTransient(Type type)
        {
            _module.EnsureCoreServicesInitialized();
            return _module.DiContainer.IsTransient(type);
        }

        /// <summary>
        /// Removes the registration for <typeparamref name="T"/> from the module container.
        /// </summary>
        /// <remarks>Intended for dynamic teardown scenarios; not typically needed during setup.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unregister<T>()
        {
            _module.EnsureCoreServicesInitialized();
            _module.DiContainer.Unregister<T>();
        }

        /// <summary>
        /// Removes the registration for the supplied type from the module container.
        /// </summary>
        /// <param name="type">Registered type to remove.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unregister(Type type)
        {
            _module.EnsureCoreServicesInitialized();
            _module.DiContainer.Unregister(type);
        }

        /// <summary>
        /// Creates a new GameObject, adds <typeparamref name="TBehaviour"/> to it, and registers it
        /// in the module's Model container hierarchy.
        /// </summary>
        /// <typeparam name="TBehaviour">MonoBehaviour type to create and register.</typeparam>
        /// <returns>A fluent registration builder used to choose scope and lifetime.</returns>
        /// <remarks>
        /// Use this instead of <see cref="Register{T}"/> when the dependency must be a MonoBehaviour.
        /// The new GameObject is parented under the module's Model ("Proxies") container transform.
        /// </remarks>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RegistrationBuilder<TBehaviour> RegisterBehaviour<TBehaviour>() where TBehaviour : MonoBehaviour
        {
            _module.EnsureCoreServicesInitialized();
            _module.EnsureMvcContainers();

            var go = new GameObject(typeof(TBehaviour).Name);
            go.transform.SetParent(_module.ModelContainer != null ? _module.ModelContainer : _module.transform, false);

            var behaviour = go.AddComponent<TBehaviour>();
            _module.OnContainerRegistering(behaviour);
            WarnForCodeRegistration(behaviour);

            return _module.DiContainer.Register(behaviour);
        }
#endif

        /// <summary>
        /// Resolves a dependency from the module's active scope.
        /// </summary>
        /// <typeparam name="T">Dependency type to resolve.</typeparam>
        /// <returns>The registered instance of <typeparamref name="T"/>.</returns>
        /// <remarks>Throws if <typeparamref name="T"/> is not registered in the module container.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Resolve<T>()
        {
            _module.EnsureCoreServicesInitialized();
            return _module.DiContainer.Resolve<T>(caller: _module);
        }

        /// <summary>
        /// Attempts to resolve a dependency from the module's active scope without throwing.
        /// </summary>
        /// <typeparam name="T">Dependency type to resolve.</typeparam>
        /// <param name="value">Resolved dependency when available; otherwise the default value for <typeparamref name="T"/>.</param>
        /// <returns><c>true</c> when <typeparamref name="T"/> was found; <c>false</c> when not registered.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryResolve<T>(out T value)
        {
            _module.EnsureCoreServicesInitialized();
            return _module.DiContainer.TryResolve<T>(out value);
        }

        // Registration logs include caller file/line so console tooling can point users to setup code.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void LogRegistration(Type type, object instance, string filePath, int lineNumber)
        {
            if (instance == null)
            {
                return;
            }

            if (instance is Proxy || instance is ProxyBehaviour)
            {
                MvcLogInternal.LogProxyRegistered(
                    type.Name,
                    _module,
                    MvcLogContext.RegistrationSource.Code,
                    null,
                    filePath,
                    lineNumber);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                MvcPluginBus.FireProxyRegistered(type, _module.GetType(), MvcLogContext.RegistrationSource.Code);
#endif
                return;
            }

            MvcLogInternal.LogServiceRegistered(
                type.Name,
                _module,
                MvcLogContext.RegistrationSource.Code,
                null,
                filePath,
                lineNumber);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            MvcPluginBus.FireServiceRegistered(type, _module.GetType(), MvcLogContext.RegistrationSource.Code);
#endif
        }

        // Warn when project settings discourage code composition but this module uses it.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WarnForCodeRegistration(object instance)
        {
            if (instance == null)
                return;

            MvcCompositionStyleWarning.WarnIfDisabled(
                MvcCompositionStyle.Code,
                $"code registration of '{instance.GetType().Name}' in module '{_module.GetType().Name}'");
        }
    }
}

using mvcExpress.Internal.DependencyInjection;
using System;
using System.Runtime.CompilerServices;

namespace mvcExpress
{
    /// <summary>
    /// Provides modules (and services running inside a module) with full registration and
    /// resolution access to the application-wide (<see cref="MvcFacade"/>) dependency container.
    /// </summary>
    /// <remarks>
    /// Use this API from <see cref="MvcModule.RegisterServices"/> or <see cref="MvcModule.RegisterProxies"/>
    /// when a dependency must be shared across multiple modules. Registrations made here are
    /// visible to every module's actors for the lifetime of the application. The module's own
    /// isolated container is unaffected. For command-time global access use
    /// <see cref="CommandGlobalContainerApi"/>; for resolve-only access use
    /// <see cref="GlobalContainerApi"/>.
    /// </remarks>
    public readonly struct ModuleGlobalContainerApi
    {
        private readonly MvcModule _module;

        internal ModuleGlobalContainerApi(MvcModule module)
        {
            _module = module;
        }

        /// <summary>
        /// Begins registering an instance in the global container.
        /// </summary>
        /// <typeparam name="T">Concrete compile-time type of the instance.</typeparam>
        /// <param name="instance">Instance to register globally.</param>
        /// <returns>
        /// A fluent <see cref="RegistrationBuilder{T}"/> used to choose scope and lifetime.
        /// Call <c>.ToLogic()</c> for logic-actor visibility or <c>.ToView()</c> for mediator
        /// visibility. Append <c>.AsPermanent()</c> to survive module unloads.
        /// </returns>
        /// <remarks>
        /// Proxies and <see cref="IMvcLifecycle"/> instances are initialized immediately after
        /// registration because the module's normal initialization pipeline has already passed.
        /// </remarks>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RegistrationBuilder<T> Register<T>(T instance)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            _module.EnsureCoreServicesInitialized();
            WarnForCodeRegistration(instance);
            var builder = MvcFacade.Global.Register(instance);
            InitializeRegisteredInstance(instance);
            return builder;
        }
#endif

        /// <summary>
        /// Begins registering an instance globally under an explicit type.
        /// </summary>
        /// <param name="instance">Instance to register globally.</param>
        /// <param name="type">Type or interface that should identify this registration in the container.</param>
        /// <returns>A fluent registration builder used to choose scope and lifetime.</returns>
        /// <remarks>
        /// Use this overload when the rest of the application resolves by interface rather than by
        /// the concrete type.
        /// </remarks>
#if MVC_EXPRESS_NO_CODE
        // Code style disabled via Project Settings > mvcExpress > Composition.
#else
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RegistrationBuilder Register(object instance, Type type)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            _module.EnsureCoreServicesInitialized();
            WarnForCodeRegistration(instance);
            var builder = MvcFacade.Global.Register(instance, type);
            InitializeRegisteredInstance(instance);
            return builder;
        }
#endif

        /// <summary>
        /// Returns whether a global registration uses transient lifetime.
        /// </summary>
        /// <param name="type">Registered type to inspect.</param>
        /// <returns><c>true</c> when the registration was created with <c>.AsTransient()</c>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsTransient(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            return MvcFacade.Global.IsTransient(type);
        }

        /// <summary>
        /// Unregisters a global registration. Uses <see cref="MvcFacade.GlobalContainerOrNull"/>
        /// (rather than <see cref="MvcFacade.Global"/>) and no-ops if it is null, since
        /// during application/Play Mode shutdown a module's OnDestroy can run after MvcFacade's
        /// own OnDestroy has already cleared the facade instance - at that point the global
        /// container is being torn down anyway, so there is nothing to unregister from.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unregister<T>()
        {
            MvcFacade.GlobalContainerOrNull?.Unregister<T>();
        }

        /// <summary>
        /// Unregisters a global registration. See <see cref="Unregister{T}"/> for why this
        /// uses <see cref="MvcFacade.GlobalContainerOrNull"/> and no-ops if it is null.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unregister(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            MvcFacade.GlobalContainerOrNull?.Unregister(type);
        }

        /// <summary>
        /// Resolves a dependency from the global container.
        /// </summary>
        /// <typeparam name="T">Dependency type to resolve.</typeparam>
        /// <returns>The registered instance of <typeparamref name="T"/>.</returns>
        /// <remarks>Throws if <typeparamref name="T"/> is not registered in the global container.</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Resolve<T>()
        {
            return MvcFacade.Global.Resolve<T>();
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
            return MvcFacade.Global.TryResolve<T>(out value);
        }

        // Global proxies and IMvcLifecycle services are wired immediately because
        // they are outside any later module-owned registration phase.
        // Proxy and ProxyBehaviour call CompleteInitialization() internally, which runs
        // InjectMembers before OnInitialized. Plain IMvcLifecycle services must be injected
        // explicitly here before the callback.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InitializeRegisteredInstance(object instance)
        {
            if (instance is ProxyBehaviour proxyBehaviour)
            {
                proxyBehaviour.InitializeGlobal(MvcFacade.Global, MvcFacade.MessageBus);
            }
            else if (instance is Proxy proxy)
            {
                proxy.Initialize(_module.ModuleType, _module.MessageBus, MvcFacade.Global);
            }
            else if (instance is IMvcLifecycle initializable)
            {
                MvcInjectionUtility.InjectMembers(instance, MvcFacade.Global, useViewScope: false);
                initializable.OnInitialized();
            }
        }

        // Warn when a project disables code composition but a module registers global dependencies.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WarnForCodeRegistration(object instance)
        {
            if (instance == null)
                return;

            MvcCompositionStyleWarning.WarnIfDisabled(
                MvcCompositionStyle.Code,
                $"code registration of global '{instance.GetType().Name}' from module '{_module.GetType().Name}'");
        }
    }
}

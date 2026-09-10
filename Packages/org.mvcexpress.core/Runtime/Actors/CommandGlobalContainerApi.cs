﻿using mvcExpress.Internal.DependencyInjection;
using System;
using System.Runtime.CompilerServices;

namespace mvcExpress
{
    /// <summary>
    /// Provides commands with full registration and resolution access to the application-wide
    /// (<see cref="MvcFacade"/>) dependency container.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="GlobalContainerApi"/> (resolve-only), this API also allows commands to
    /// register and unregister global dependencies at runtime - for example, a command that
    /// bootstraps or tears down a cross-module service. Use this sparingly: global registrations
    /// are visible to every module and persist until explicitly removed or the app shuts down.
    /// For module-scoped dependencies use <see cref="CommandContainerApi"/> instead.
    /// </remarks>
    public readonly struct CommandGlobalContainerApi
    {
        private readonly MvcActorContext _context;

        internal CommandGlobalContainerApi(MvcActorContext context)
        {
            _context = context;
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RegistrationBuilder<T> Register<T>(T instance)
        {
            if (instance == null)
            {
                throw new ArgumentNullException(nameof(instance));
            }

            var builder = MvcFacade.Global.Register(instance);
            // Deferred until the builder chain commits as non-scoped - see
            // RegistrationBuilder<T>.OnNonScopedCommitted. A .AsScoped() chain must never
            // initialize this throwaway seed instance.
            builder.OnNonScopedCommitted = InitializeRegisteredInstance;
            return builder;
        }

        /// <summary>
        /// Begins registering an instance globally under an explicit type.
        /// </summary>
        /// <param name="instance">Instance to register globally.</param>
        /// <param name="type">Type or interface that should identify this registration in the container.</param>
        /// <returns>A fluent registration builder used to choose scope and lifetime.</returns>
        /// <remarks>
        /// Use this overload when the rest of the application resolves by interface rather than
        /// by the concrete type.
        /// </remarks>
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

            var builder = MvcFacade.Global.Register(instance, type);
            // See the matching comment on Register<T> above.
            builder.OnNonScopedCommitted = InitializeRegisteredInstance;
            return builder;
        }

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
        /// during application/Play Mode shutdown a command's cleanup can run after MvcFacade's
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
            return MvcFacade.Global.ResolveWithScope<T>(false, _context.Actor);
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
            return MvcFacade.Global.TryResolveWithScope(false, out value, _context.Actor);
        }

        // Global proxies and IMvcLifecycle services are wired immediately because this is a
        // dynamic runtime registration from a command, outside any module/facade-owned
        // registration phase - see the matching comment on
        // FacadeGlobalContainerApi/ModuleGlobalContainerApi.InitializeRegisteredInstance. Proxy
        // and ProxyBehaviour call CompleteInitialization()/InitializeGlobal() internally, which
        // runs InjectMembers before OnInitialized. Plain IMvcLifecycle services must be injected
        // explicitly here before the callback, and tracked so OnCleanup() runs at facade
        // destroy.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InitializeRegisteredInstance(object instance)
        {
            if (instance is ProxyBehaviour proxyBehaviour)
            {
                proxyBehaviour.InitializeGlobal(MvcFacade.Global, MvcFacade.MessageBus);
            }
            else if (instance is Proxy proxy)
            {
                var moduleType = _context.ResolveModule()?.ModuleType ?? typeof(MvcFacade);
                proxy.Initialize(moduleType, MvcFacade.MessageBus, MvcFacade.Global);
            }
            else if (instance is IMvcLifecycle initializable)
            {
                MvcInjectionUtility.InjectMembers(instance, MvcFacade.Global, useViewScope: false);
                initializable.OnInitialized();
                MvcFacade.FacadeInstance.TrackGlobalLifecycleService(initializable);
            }
        }
    }
}

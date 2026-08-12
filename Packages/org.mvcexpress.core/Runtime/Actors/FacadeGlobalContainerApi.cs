using mvcExpress.Internal.DependencyInjection;
using System;
using System.Runtime.CompilerServices;

namespace mvcExpress
{
    /// <summary>
    /// Provides <see cref="MvcFacade"/> subclasses with registration and resolution access to
    /// the application-wide global dependency container, for use from
    /// <see cref="MvcFacade.RegisterGlobalServices"/> and <see cref="MvcFacade.RegisterGlobalProxies"/>.
    /// </summary>
    /// <remarks>
    /// Registrations made here run before any <see cref="MvcModule"/> registers with the facade,
    /// so every module can safely resolve them from its very first initialization phase - no
    /// race condition between global setup and module startup. For the same kind of access from
    /// inside a module, see <see cref="ModuleGlobalContainerApi"/>.
    /// </remarks>
    public readonly struct FacadeGlobalContainerApi
    {
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
            InitializeRegisteredInstance(instance);
            return builder;
        }

        /// <summary>
        /// Begins registering an instance globally under an explicit type.
        /// </summary>
        /// <param name="instance">Instance to register globally.</param>
        /// <param name="type">Type or interface that should identify this registration in the container.</param>
        /// <returns>A fluent registration builder used to choose scope and lifetime.</returns>
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
            InitializeRegisteredInstance(instance);
            return builder;
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

        // Global proxies and IMvcLifecycle services are wired immediately because this API is
        // used outside any module-owned registration phase (before any module exists yet).
        // Proxy and ProxyBehaviour call CompleteInitialization()/InitializeGlobal() internally,
        // which runs InjectMembers before OnInitialized. Plain IMvcLifecycle services must be
        // injected explicitly here before the callback.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void InitializeRegisteredInstance(object instance)
        {
            if (instance is ProxyBehaviour proxyBehaviour)
            {
                proxyBehaviour.InitializeGlobal(MvcFacade.Global, MvcFacade.MessageBus);
            }
            else if (instance is Proxy proxy)
            {
                proxy.Initialize(typeof(MvcFacade), MvcFacade.MessageBus, MvcFacade.Global);
            }
            else if (instance is IMvcLifecycle initializable)
            {
                MvcInjectionUtility.InjectMembers(instance, MvcFacade.Global, useViewScope: false);
                initializable.OnInitialized();
            }
        }
    }
}

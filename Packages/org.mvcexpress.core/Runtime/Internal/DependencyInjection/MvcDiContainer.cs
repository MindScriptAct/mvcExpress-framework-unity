using mvcExpress;
using mvcExpress.Internal.Interfaces;
using mvcExpress.Internal.Utilities;
using mvcExpress.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Threading;
using UnityEngine;

namespace mvcExpress.Internal.DependencyInjection
{
    /// <summary>
    /// Fluent builder for dependency registration.
    /// </summary>
    public sealed class RegistrationBuilder<T>
    {
        private readonly MvcDiContainer _container;
        private readonly object _instance;
        private readonly List<Type> _logicTypes = new List<Type>();
        private readonly List<Type> _viewTypes = new List<Type>();
        private bool _isTransient = false;
        private bool _isScoped = false;
        private bool _completed = false;

        private Type _logicListElementType;
        private Type _viewListElementType;
        private bool _registerToLogicList;
        private bool _registerToViewList;

        // Scoped lifetime redesign: some dynamic/runtime registration entry points
        // (CommandContainerApi, ModuleGlobalContainerApi, FacadeGlobalContainerApi) need to
        // initialize a freshly-registered Proxy/service immediately, since the module's normal
        // initialization pipeline has already passed. They can only know whether that's actually
        // correct to do once THIS builder decides the final lifetime, not before - a Scoped chain
        // must never initialize the throwaway seed instance handed to it. Setting this callback
        // defers that initialization to Complete()'s non-scoped commit, instead of running eagerly
        // before .AsScoped() (or any terminal operation) has even been called.
        internal Action<object> OnNonScopedCommitted;

        internal RegistrationBuilder(MvcDiContainer container, object instance)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _instance = instance ?? throw new ArgumentNullException(nameof(instance));
        }

        public RegistrationBuilder<T> ToLogic()
        {
            ThrowIfCompleted();
            AddLogicType(_instance.GetType());
            return this;
        }

        public RegistrationBuilder<T> ToLogicAs<TLogic>()
        {
            ThrowIfCompleted();
            AddLogicType(typeof(TLogic));
            return this;
        }

        public RegistrationBuilder<T> ToView()
        {
            ThrowIfCompleted();
            AddViewType(_instance.GetType());
            return this;
        }

        public RegistrationBuilder<T> ToViewAs<TView>()
        {
            ThrowIfCompleted();
            AddViewType(typeof(TView));
            return this;
        }

        public RegistrationBuilder<T> ToLogicList<TLogic>()
        {
            ThrowIfCompleted();
            ValidateExposedType(typeof(TLogic));
            _registerToLogicList = true;
            _logicListElementType = typeof(TLogic);
            return this;
        }

        public RegistrationBuilder<T> ToViewList<TView>()
        {
            ThrowIfCompleted();
            ValidateExposedType(typeof(TView));
            _registerToViewList = true;
            _viewListElementType = typeof(TView);
            return this;
        }

        public void AsPermanent()
        {
            ThrowIfCompleted();
            _isTransient = false;
            Complete();
        }

        public void AsTransient()
        {
            ThrowIfCompleted();
            _isTransient = true;
            Complete();
        }

        public void AsScoped()
        {
            ThrowIfCompleted();
            _isScoped = true;
            _isTransient = false;
            Complete();
        }

        private void AddLogicType(Type type)
        {
            ValidateExposedType(type);
            if (_logicTypes.Contains(type))
            {
                throw new InvalidOperationException(
                    $"'{type.Name}' was already mapped to logic scope in this Register(...) chain. " +
                    $"Each type may only be mapped once per chain.");
            }
            _logicTypes.Add(type);
        }

        private void AddViewType(Type type)
        {
            ValidateExposedType(type);
            if (_viewTypes.Contains(type))
            {
                throw new InvalidOperationException(
                    $"'{type.Name}' was already mapped to view scope in this Register(...) chain. " +
                    $"Each type may only be mapped once per chain.");
            }
            _viewTypes.Add(type);
        }

        // A caller could map an alias (.ToLogicAs<T>()/.ToViewAs<T>(), the
        // runtime-Type overloads on the non-generic RegistrationBuilder, or a list element type via
        // .ToLogicList<T>()/.ToViewList<T>()) that the instance does not actually implement. Nothing
        // downstream of AddLogicType/AddViewType checks this - the four internal dictionaries are
        // Type -> object, so a mismatched mapping is stored without complaint - and the failure
        // previously only surfaced later as a cast exception during Resolve<T>() or list consumption,
        // far from the misconfigured .Register(...) call. Validating here, at the fluent-call site,
        // reports the actual defect immediately and by name.
        private void ValidateExposedType(Type exposedType)
        {
            if (exposedType.ContainsGenericParameters)
            {
                throw new InvalidOperationException(
                    $"[MvcExpress] Cannot register '{_instance.GetType().FullName}' against open generic " +
                    $"type '{exposedType.Name}'. Specify a closed generic type instead " +
                    $"(e.g. typeof(IRepository<Foo>), not typeof(IRepository<>)).");
            }

            if (!exposedType.IsInstanceOfType(_instance))
            {
                throw new InvalidOperationException(
                    $"[MvcExpress] Cannot register '{_instance.GetType().FullName}' as " +
                    $"'{exposedType.FullName}': '{_instance.GetType().FullName}' does not implement or " +
                    $"derive from '{exposedType.FullName}'.");
            }
        }

        private void ThrowIfCompleted()
        {
            if (_completed)
            {
                throw new InvalidOperationException(
                    "Registration already completed. Cannot modify a completed registration builder.");
            }
        }

        private void Complete()
        {
            if (_completed) return;
            _completed = true;

            if (_logicTypes.Count == 0 && _viewTypes.Count == 0 && !_registerToLogicList && !_registerToViewList)
            {
                throw new InvalidOperationException(
                    "Must register to at least one layer. Call .ToLogic(), .ToLogicAs<T>(), .ToView(), .ToViewAs<T>, .ToLogicList<T>(), or .ToViewList<T>() before completing registration.");
            }

            if (_isScoped)
            {
                if (_instance is UnityEngine.MonoBehaviour)
                {
                    throw new InvalidOperationException(
                        $"[MvcExpress] AsScoped() is not supported for MonoBehaviour types. '{_instance.GetType().FullName}' is a MonoBehaviour - Scoped requires the container to construct the instance itself via Activator.CreateInstance, which isn't possible for Unity components. Use Permanent or Transient instead.");
                }

                if (_registerToLogicList || _registerToViewList)
                {
                    throw new InvalidOperationException(
                        "[MvcExpress] AsScoped() cannot be combined with ToLogicList<T>()/ToViewList<T>() - " +
                        "scoped list membership has no design yet (see InternalSpecs/Architecture/SCOPED_LIFETIME_REDESIGN.md).");
                }

                _container.RegisterScopedIdentity(_logicTypes, _viewTypes, _instance.GetType());
                return;
            }

            _container.RegisterInternal(
                _instance,
                _logicTypes,
                _viewTypes,
                _isTransient,
                _registerToLogicList,
                _logicListElementType,
                _registerToViewList,
                _viewListElementType);

            // The registration is already committed at this point. If the post-commit
            // initialization callback (injection / OnInitialized()) throws, undo the commit
            // instead of leaving a half-initialized instance permanently resolvable.
            try
            {
                OnNonScopedCommitted?.Invoke(_instance);
            }
            catch
            {
                _container.RollbackFailedInitialization(
                    _logicTypes, _viewTypes, _instance,
                    _registerToLogicList, _logicListElementType,
                    _registerToViewList, _viewListElementType);
                throw;
            }
        }
    }

    public sealed class RegistrationBuilder
    {
        private readonly MvcDiContainer _container;
        private readonly object _instance;
        private readonly Type _instanceType;
        private readonly List<Type> _logicTypes = new List<Type>();
        private readonly List<Type> _viewTypes = new List<Type>();
        private bool _isTransient = false;
        private bool _isScoped = false;
        private bool _completed = false;

        private Type _logicListElementType;
        private Type _viewListElementType;
        private bool _registerToLogicList;
        private bool _registerToViewList;

        // See the matching field on RegistrationBuilder<T> for why this exists.
        internal Action<object> OnNonScopedCommitted;

        internal RegistrationBuilder(MvcDiContainer container, object instance, Type type)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _instance = instance ?? throw new ArgumentNullException(nameof(instance));
            _instanceType = type ?? throw new ArgumentNullException(nameof(type));
        }

        public RegistrationBuilder ToLogic()
        {
            ThrowIfCompleted();
            AddLogicType(_instanceType);
            return this;
        }

        public RegistrationBuilder ToLogicAs(Type logicType)
        {
            ThrowIfCompleted();
            if (logicType == null) throw new ArgumentNullException(nameof(logicType));
            AddLogicType(logicType);
            return this;
        }

        public RegistrationBuilder ToView()
        {
            ThrowIfCompleted();
            AddViewType(_instanceType);
            return this;
        }

        public RegistrationBuilder ToViewAs(Type viewType)
        {
            ThrowIfCompleted();
            if (viewType == null) throw new ArgumentNullException(nameof(viewType));
            AddViewType(viewType);
            return this;
        }

        public RegistrationBuilder ToLogicList(Type elementType)
        {
            ThrowIfCompleted();
            if (elementType == null) throw new ArgumentNullException(nameof(elementType));
            ValidateExposedType(elementType);
            _registerToLogicList = true;
            _logicListElementType = elementType;
            return this;
        }

        public RegistrationBuilder ToViewList(Type elementType)
        {
            ThrowIfCompleted();
            if (elementType == null) throw new ArgumentNullException(nameof(elementType));
            ValidateExposedType(elementType);
            _registerToViewList = true;
            _viewListElementType = elementType;
            return this;
        }

        public void AsPermanent()
        {
            ThrowIfCompleted();
            _isTransient = false;
            Complete();
        }

        public void AsTransient()
        {
            ThrowIfCompleted();
            _isTransient = true;
            Complete();
        }

        public void AsScoped()
        {
            ThrowIfCompleted();
            _isScoped = true;
            _isTransient = false;
            Complete();
        }

        private void AddLogicType(Type type)
        {
            ValidateExposedType(type);
            if (_logicTypes.Contains(type))
            {
                throw new InvalidOperationException(
                    $"'{type.Name}' was already mapped to logic scope in this Register(...) chain. " +
                    $"Each type may only be mapped once per chain.");
            }
            _logicTypes.Add(type);
        }

        private void AddViewType(Type type)
        {
            ValidateExposedType(type);
            if (_viewTypes.Contains(type))
            {
                throw new InvalidOperationException(
                    $"'{type.Name}' was already mapped to view scope in this Register(...) chain. " +
                    $"Each type may only be mapped once per chain.");
            }
            _viewTypes.Add(type);
        }

        // See the matching method on RegistrationBuilder<T> - same rationale.
        private void ValidateExposedType(Type exposedType)
        {
            if (exposedType.ContainsGenericParameters)
            {
                throw new InvalidOperationException(
                    $"[MvcExpress] Cannot register '{_instance.GetType().FullName}' against open generic " +
                    $"type '{exposedType.Name}'. Specify a closed generic type instead " +
                    $"(e.g. typeof(IRepository<Foo>), not typeof(IRepository<>)).");
            }

            if (!exposedType.IsInstanceOfType(_instance))
            {
                throw new InvalidOperationException(
                    $"[MvcExpress] Cannot register '{_instance.GetType().FullName}' as " +
                    $"'{exposedType.FullName}': '{_instance.GetType().FullName}' does not implement or " +
                    $"derive from '{exposedType.FullName}'.");
            }
        }

        private void ThrowIfCompleted()
        {
            if (_completed)
            {
                throw new InvalidOperationException(
                    "Registration already completed. Cannot modify a completed registration builder.");
            }
        }

        private void Complete()
        {
            if (_completed) return;
            _completed = true;

            if (_logicTypes.Count == 0 && _viewTypes.Count == 0 && !_registerToLogicList && !_registerToViewList)
            {
                throw new InvalidOperationException(
                    "Must register to at least one layer. Call .ToLogic(), .ToLogicAs(), .ToView(), .ToViewAs(), .ToLogicList(), or .ToViewList() before completing registration.");
            }

            if (_isScoped)
            {
                if (_instance is UnityEngine.MonoBehaviour)
                {
                    throw new InvalidOperationException(
                        $"[MvcExpress] AsScoped() is not supported for MonoBehaviour types. '{_instance.GetType().FullName}' is a MonoBehaviour - Scoped requires the container to construct the instance itself via Activator.CreateInstance, which isn't possible for Unity components. Use Permanent or Transient instead.");
                }

                if (_registerToLogicList || _registerToViewList)
                {
                    throw new InvalidOperationException(
                        "[MvcExpress] AsScoped() cannot be combined with ToLogicList()/ToViewList() - " +
                        "scoped list membership has no design yet (see InternalSpecs/Architecture/SCOPED_LIFETIME_REDESIGN.md).");
                }

                // The explicit type is an exposure key, not necessarily the implementation
                // type (it is commonly an interface). Scoped construction must always use the
                // concrete runtime type of the supplied seed instance.
                _container.RegisterScopedIdentity(_logicTypes, _viewTypes, _instance.GetType());
                return;
            }

            _container.RegisterInternal(
                _instance,
                _logicTypes,
                _viewTypes,
                _isTransient,
                _registerToLogicList,
                _logicListElementType,
                _registerToViewList,
                _viewListElementType);

            // The registration is already committed at this point. If the post-commit
            // initialization callback (injection / OnInitialized()) throws, undo the commit
            // instead of leaving a half-initialized instance permanently resolvable.
            try
            {
                OnNonScopedCommitted?.Invoke(_instance);
            }
            catch
            {
                _container.RollbackFailedInitialization(
                    _logicTypes, _viewTypes, _instance,
                    _registerToLogicList, _logicListElementType,
                    _registerToViewList, _viewListElementType);
                throw;
            }
        }
    }

    /// <summary>
    /// Dependency injection container with dual-scope support.
    /// 
    /// SINGLE REGISTRATION POLICY: Each type can only be registered once per scope.
    /// This ensures clear, explicit dependency graphs and prevents ambiguity.
    /// If you need multiple implementations, create distinct types or a composite class.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public sealed class MvcDiContainer : IModuleDiContainer, ITransientDependencyNotifier, IDisposable
    {
        // Logic container: accessible to commands and other proxies (full access)
        private readonly ConcurrentDictionary<Type, object> _logicObjects = new ConcurrentDictionary<Type, object>();
        private readonly ConcurrentDictionary<Type, object> _logicInterfaces = new ConcurrentDictionary<Type, object>();

        // View container: accessible to mediators (restricted/read-only access)
        private readonly ConcurrentDictionary<Type, object> _viewObjects = new ConcurrentDictionary<Type, object>();
        private readonly ConcurrentDictionary<Type, object> _viewInterfaces = new ConcurrentDictionary<Type, object>();

        // Transient dependency tracking for command invalidation
        private readonly ConcurrentDictionary<Type, bool> _transientTypes = new ConcurrentDictionary<Type, bool>();

        // PERFORMANCE OPTIMIZATION: Cache IsInterface results to avoid reflection on every injection
        // Type.IsInterface is a virtual call with ~2-5ns overhead. Cache eliminates this completely.
        private static readonly ConcurrentDictionary<Type, bool> _isInterfaceCache = new ConcurrentDictionary<Type, bool>();

        // MEMORY LEAK FIX: Use WeakEventManager instead of direct event
        // Event triggered when a transient dependency is unregistered
        private readonly WeakEventManager<Type> _transientDependencyRemoved = new WeakEventManager<Type>();

        // List-bindings: elementType -> (member instance -> isTransient for THAT member).
        // Per-member lifetime, not per-list. The inner dictionary is always
        // constructed with ReferenceIdentityComparer.Instance (in AddToListBinding) so two
        // distinct, value-equal members never collapse into one bookkeeping entry.
        private readonly ConcurrentDictionary<Type, ConcurrentDictionary<object, bool>> _logicListMembers = new ConcurrentDictionary<Type, ConcurrentDictionary<object, bool>>();
        private readonly ConcurrentDictionary<Type, ConcurrentDictionary<object, bool>> _viewListMembers = new ConcurrentDictionary<Type, ConcurrentDictionary<object, bool>>();

        // Counts how many container keys currently resolve to a given instance (an instance
        // registered via .ToLogicAs<IFoo>().ToView() lives under two keys: IFoo in logic, the
        // concrete type in view). UnregisterInternal only disposes an instance once this reaches
        // zero, so unregistering one key never disposes an instance still resolvable under another.
        // Guarded by _refCountLock rather than a lock-free CAS loop because
        // register/unregister are setup/teardown operations, not hot-path - a plain Dictionary
        // keyed by reference (default object equality) is the cheapest correct structure, and
        // removing the entry once the count hits zero avoids re-creating the exact per-instance
        // leak this is meant to close.
        // ReferenceIdentityComparer.Instance is required here too: without it, two
        // distinct instances whose type overrides Equals/GetHashCode (a value-object service, or
        // even a boxed struct, which gets structural equality for free) collide onto one dictionary
        // entry. One of them then never reaches its own zero-count and is never disposed - the
        // shared count only ever grows from the collision, so it delays a dispose, it does not
        // trigger a wrong/early one, but the entangled instance leaks (no Dispose(), no OnCleanup())
        // for as long as its colliding neighbor's key stays registered.
        private readonly Dictionary<object, int> _instanceKeyRefCounts = new Dictionary<object, int>(ReferenceIdentityComparer.Instance);
        private readonly object _refCountLock = new object();

        private void IncrementInstanceRefCount(object instance)
        {
            lock (_refCountLock)
            {
                _instanceKeyRefCounts.TryGetValue(instance, out var count);
                _instanceKeyRefCounts[instance] = count + 1;
            }
        }

        // Decrements the instance's key-refcount and returns true if this was the last reference
        // (count reached zero, entry removed) - i.e. the caller may now safely dispose it.
        private bool DecrementInstanceRefCount(object instance)
        {
            lock (_refCountLock)
            {
                if (!_instanceKeyRefCounts.TryGetValue(instance, out var count))
                {
                    // Not tracked - shouldn't happen for anything registered via RegisterInternal,
                    // but default to "safe to dispose" rather than silently leaking it.
                    return true;
                }

                if (count <= 1)
                {
                    _instanceKeyRefCounts.Remove(instance);
                    return true;
                }

                _instanceKeyRefCounts[instance] = count - 1;
                return false;
            }
        }

        private bool _disposed;

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only. Fires from <see cref="RegisterInternal"/> whenever a <see cref="Proxies"/> or
        /// <see cref="ProxyBehaviour"/> instance is registered, so <c>ProxyHierarchyVisualizer</c>
        /// can create/reparent the corresponding debug wrapper GameObject under the module's
        /// "Model" root (or "Static" for the global container) without requiring a public API.
        /// </summary>
        internal event Action<object> ProxyRegisteredForDebug;

        /// <summary>
        /// Editor-only. Fires from <see cref="RegisterInternal"/> for code-only service
        /// registrations - i.e. instances that are neither a <see cref="Proxies"/>/<see cref="ProxyBehaviour"/>
        /// nor a <see cref="UnityEngine.MonoBehaviour"/>.
        /// </summary>
        internal event Action<object> ServiceRegisteredForDebug;

        /// <summary>
        /// Editor-only. Mirrors <see cref="ProxyRegisteredForDebug"/>: fires from
        /// <see cref="ReleaseInstanceKey"/> when a code-only proxy is fully unregistered (no
        /// container key still resolves to it), so <c>ProxyHierarchyVisualizer</c> can destroy the
        /// wrapper GameObject it created for that instance directly. Without this,
        /// removing the wrapper required scanning every <see cref="MvcModule"/> in the scene to
        /// find it by name.
        /// </summary>
        internal event Action<object> ProxyUnregisteredForDebug;
#endif

        /// <summary>
        /// FRAMEWORK INTERNAL: Subscribe to transient dependency removal using weak reference.
        /// Explicit interface implementation - hidden from public API.
        /// </summary>
        void ITransientDependencyNotifier.SubscribeToTransientRemoval(Action<Type> handler)
        {
            _transientDependencyRemoved.Subscribe(handler);
        }

        /// <summary>
        /// FRAMEWORK INTERNAL: Explicitly unsubscribe from transient dependency removal.
        /// Explicit interface implementation - hidden from public API.
        /// </summary>
        void ITransientDependencyNotifier.UnsubscribeFromTransientRemoval(Action<Type> handler)
        {
            _transientDependencyRemoved.Unsubscribe(handler);
        }

        // SIMPLIFIED: Use AsyncLocal only - modern .NET optimizes this well
        // No more dual ThreadStatic/AsyncLocal complexity
        // AsyncLocal handles both sync and async scenarios correctly
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        // Editor: Keep AsyncLocal for async/await debugging
        private static readonly AsyncLocal<int> _viewScopeDepth = new AsyncLocal<int>();
        private static bool IsViewScope => _viewScopeDepth.Value > 0;
#else
        // Production: Use ThreadStatic for ~10x faster access (~5ns vs ~50-100ns)
        [ThreadStatic]
        private static int _viewScopeDepth;
        private static bool IsViewScope => _viewScopeDepth > 0;
#endif

        internal struct ViewScopeToken : IDisposable
        {
            private readonly bool _active;
            private readonly int _previousDepth;

            public ViewScopeToken(bool active, int previousDepth)
            {
                _active = active;
                _previousDepth = previousDepth;
            }

            public void Dispose()
            {
                if (_active)
                {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    _viewScopeDepth.Value = _previousDepth;
#else
                    _viewScopeDepth = _previousDepth;
#endif
                }
            }
        }

        internal ViewScopeToken BeginViewScope()
        {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            int previousDepth = _viewScopeDepth.Value;
            _viewScopeDepth.Value = previousDepth + 1;
#else
            int previousDepth = _viewScopeDepth;
            _viewScopeDepth = previousDepth + 1;
#endif
            return new ViewScopeToken(true, previousDepth);
        }

        /// <summary>
        /// Check if a type is registered as transient (can be destroyed/unregistered).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsTransient(Type type)
        {
            return _transientTypes.TryGetValue(type, out var isTransient) && isTransient;
        }

        #region Scoped lifetime (see InternalSpecs/Architecture/SCOPED_LIFETIME_REDESIGN.md)
        //
        // Per-injectee ownership: each ACTOR that resolves a scoped type gets its own dedicated
        // instance - constructed, recursively injected, and lifecycle-initialized through the same
        // pipeline permanent/transient actors use - which lives exactly as long as that actor does.
        // Identity is the registrationId assigned once per .AsScoped() builder chain (not the
        // requested Type), so an aliased registration resolves to one instance per injectee
        // instead of one instance per alias. Ownership storage is a ConditionalWeakTable keyed by
        // the injectee reference itself, so a skipped teardown call cannot leak the owner.

        private int _nextScopedRegistrationId;
        private readonly ConcurrentDictionary<int, ScopedRegistrationDescriptor> _scopedRegistrations = new ConcurrentDictionary<int, ScopedRegistrationDescriptor>();
        private readonly ConcurrentDictionary<Type, int> _logicScopedTypeToRegistrationId = new ConcurrentDictionary<Type, int>();
        private readonly ConcurrentDictionary<Type, int> _viewScopedTypeToRegistrationId = new ConcurrentDictionary<Type, int>();
        private readonly ConditionalWeakTable<object, ScopedInstanceScope> _scopedInstanceScopes = new ConditionalWeakTable<object, ScopedInstanceScope>();
        private readonly object _scopedRegistrationLock = new object();

        /// <summary>
        /// Check if a type is registered as scoped (dedicated per-injectee lifetime).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsScoped(Type type)
        {
            return type != null &&
                (_logicScopedTypeToRegistrationId.ContainsKey(type) ||
                 _viewScopedTypeToRegistrationId.ContainsKey(type));
        }

        /// <summary>
        /// Records one registrationId shared by every logic/view type in a single
        /// <c>.AsScoped()</c> builder chain, so aliased registrations resolve to one instance
        /// per injectee instead of one instance per alias. Validates every
        /// requested type before mutating anything, so a chain requesting N types either
        /// registers all N or none of them - this also means a chain that exposes the SAME type
        /// to both Logic and View (e.g. <c>.ToLogic().ToView().AsScoped()</c>) never collides with
        /// itself, since both lists are checked against prior state before either is committed.
        /// </summary>
        internal void RegisterScopedIdentity(IReadOnlyList<Type> logicTypes, IReadOnlyList<Type> viewTypes, Type implementationType)
        {
            if (implementationType == null) throw new ArgumentNullException(nameof(implementationType));

            ValidateScopedImplementation(implementationType, logicTypes);
            ValidateScopedImplementation(implementationType, viewTypes);

            // Scoped registration is setup-time work. A small lock makes validation+commit
            // genuinely atomic and prevents concurrent chains from overwriting one another's
            // type-to-registration mapping.
            lock (_scopedRegistrationLock)
            {
                ValidateScopedTypes(logicTypes);
                ValidateScopedTypes(viewTypes);

                int id = Interlocked.Increment(ref _nextScopedRegistrationId);
                var descriptor = new ScopedRegistrationDescriptor(
                    id,
                    implementationType,
                    exposedToLogic: logicTypes.Count > 0,
                    exposedToView: viewTypes.Count > 0);

                _scopedRegistrations[id] = descriptor;
                for (int i = 0; i < logicTypes.Count; i++) _logicScopedTypeToRegistrationId[logicTypes[i]] = id;
                for (int i = 0; i < viewTypes.Count; i++) _viewScopedTypeToRegistrationId[viewTypes[i]] = id;
            }
        }

        private static void ValidateScopedImplementation(Type implementationType, IReadOnlyList<Type> exposedTypes)
        {
            if (implementationType.IsInterface || implementationType.IsAbstract)
            {
                throw new InvalidOperationException(
                    $"[MvcExpress] Scoped implementation '{implementationType.FullName}' must be a concrete type.");
            }

            for (int i = 0; i < exposedTypes.Count; i++)
            {
                var exposedType = exposedTypes[i];
                if (!exposedType.IsAssignableFrom(implementationType))
                {
                    throw new InvalidOperationException(
                        $"[MvcExpress] Scoped implementation '{implementationType.FullName}' is not assignable to exposed type '{exposedType.FullName}'.");
                }
            }
        }

        private void ValidateScopedTypes(IReadOnlyList<Type> types)
        {
            for (int i = 0; i < types.Count; i++)
            {
                var type = types[i];
                if (_logicObjects.ContainsKey(type) || _logicInterfaces.ContainsKey(type) || _viewObjects.ContainsKey(type) || _viewInterfaces.ContainsKey(type))
                {
                    throw new InvalidOperationException(
                        $"[MvcExpress] Cannot register '{type.FullName}' as scoped because it is already registered as an instance. Scoped types cannot be registered as instances.");
                }

                if (_logicScopedTypeToRegistrationId.ContainsKey(type) ||
                    _viewScopedTypeToRegistrationId.ContainsKey(type))
                {
                    throw new InvalidOperationException(
                        $"[MvcExpress] Type '{type.FullName}' already registered as scoped. Scoped registrations must be unique.");
                }
            }
        }

        /// <summary>
        /// Looks up the registration descriptor for a requested scoped type. Returns null if not registered.
        /// </summary>
        internal ScopedRegistrationDescriptor GetScopedRegistration(Type requestedType)
        {
            if (requestedType != null &&
                (_logicScopedTypeToRegistrationId.TryGetValue(requestedType, out var id) ||
                 _viewScopedTypeToRegistrationId.TryGetValue(requestedType, out id)))
            {
                return _scopedRegistrations.TryGetValue(id, out var descriptor) ? descriptor : null;
            }
            return null;
        }

        /// <summary>
        /// Resolves a scoped dependency dedicated to <paramref name="scopeOwner"/> - the injectee
        /// actor instance - constructing+injecting+lifecycle-initializing it on first use and
        /// returning the same instance for every later call with the same owner and registration.
        /// </summary>
        internal bool TryResolveScopedForOwner(Type type, object scopeOwner, bool useViewScope, out object value)
        {
            var requestedMappings = useViewScope
                ? _viewScopedTypeToRegistrationId
                : _logicScopedTypeToRegistrationId;
            var otherMappings = useViewScope
                ? _logicScopedTypeToRegistrationId
                : _viewScopedTypeToRegistrationId;

            if (!requestedMappings.TryGetValue(type, out var registrationId))
            {
                if (otherMappings.ContainsKey(type))
                {
                    var requestedDomain = useViewScope ? "View" : "Logic";
                    var registeredDomain = useViewScope ? "Logic" : "View";
                    throw new InvalidOperationException(
                        $"[MvcExpress] Type '{type.FullName}' is registered as scoped for {registeredDomain} only, " +
                        $"and is not resolvable from {requestedDomain} scope. Scoped registrations are domain-" +
                        $"isolated - expose it to {requestedDomain} explicitly if this cross-domain use is intentional.");
                }

                value = null;
                return false;
            }

            if (!_scopedRegistrations.TryGetValue(registrationId, out var descriptor))
            {
                value = null;
                return false;
            }

            if (scopeOwner == null)
            {
                throw new InvalidOperationException(
                    $"[MvcExpress] Cannot resolve scoped type '{type.FullName}': no owning actor was supplied. " +
                    "Scoped resolution requires a call site that knows its own injectee (an actor's own " +
                    "[Inject] field, or that actor's Container.Resolve<T>()/TryResolve<T>()) - a bare/global " +
                    "resolve call has no injectee to dedicate the instance to.");
            }

            while (true)
            {
                var scope = _scopedInstanceScopes.GetValue(scopeOwner, _ => new ScopedInstanceScope());
                lock (scope.SyncRoot)
                {
                    // Release removes the table entry before marking the old scope. A resolver
                    // that had already captured that scope must retry against the replacement.
                    if (scope.IsDisposed)
                    {
                        _scopedInstanceScopes.Remove(scopeOwner);
                        continue;
                    }

                    // Whoever's own [Inject] fields are currently being resolved (if any) is the
                    // one asking for this type - record it as depending on whatever we resolve
                    // below, whether that turns out to be a fresh instance or an already-cached
                    // sibling. See COR-17.
                    object dependent = scope.ConstructionStack.Count > 0
                        ? scope.ConstructionStack[scope.ConstructionStack.Count - 1]
                        : null;

                    if (scope.Instances.TryGetValue(registrationId, out value))
                    {
                        if (dependent != null && !ReferenceEquals(dependent, value))
                        {
                            RecordDependencyEdge(scope, dependent, value);
                        }
                        return true;
                    }

                    var instance = Activator.CreateInstance(descriptor.ImplementationType);

                    // Cache before recursive construction below: a nested scoped [Inject] field
                    // that resolves this registration again must see the in-progress instance.
                    scope.Instances[registrationId] = instance;
                    int creationIndex = scope.CreationOrder.Count;
                    scope.CreationOrder.Add(instance);

                    if (dependent != null)
                    {
                        RecordDependencyEdge(scope, dependent, instance);
                    }

                    scope.ConstructionStack.Add(instance);
                    try
                    {
                        if (instance is mvcExpress.Proxy proxy)
                        {
                            var ownerContext = ResolveActorContext(scopeOwner);
                            if (ownerContext == null)
                            {
                                throw new InvalidOperationException(
                                    $"[MvcExpress] Cannot construct scoped Proxy dependency '{descriptor.ImplementationType.FullName}': " +
                                    $"owner '{scopeOwner.GetType().FullName}' exposes no actor context (module/message-bus) " +
                                    "to initialize it with.");
                            }

                            // Preserve the top-level owner while the nested Proxy injects its own
                            // fields; otherwise it would create a second scope and break aliasing
                            // (or recurse forever for a circular scoped graph).
                            proxy.InitializeScoped(ownerContext.Value.ModuleType, ownerContext.Value.MessagePublisher, this, scopeOwner);
                        }
                        else
                        {
                            MvcInjectionUtility.InjectMembers(instance, this, useViewScope, scopeOwnerOverride: scopeOwner);

                            if (instance is IMvcLifecycle lifecycle)
                            {
                                lifecycle.OnInitialized();
                            }
                        }
                    }
                    catch
                    {
                        // Never leave a half-initialized object cached. Nested instances already
                        // completed during this attempt remain valid members of the owner's scope.
                        scope.Instances.Remove(registrationId);
                        if (creationIndex < scope.CreationOrder.Count &&
                            ReferenceEquals(scope.CreationOrder[creationIndex], instance))
                        {
                            scope.CreationOrder.RemoveAt(creationIndex);
                        }
                        if (dependent != null)
                        {
                            RemoveDependencyEdge(scope, dependent, instance);
                        }
                        scope.DependencyEdges.Remove(instance);
                        DisposeScopedInstanceIgnoringErrors(instance);
                        throw;
                    }
                    finally
                    {
                        scope.ConstructionStack.RemoveAt(scope.ConstructionStack.Count - 1);
                    }

                    value = instance;
                    return true;
                }
            }
        }

        // Recovers the actor context (module type + message bus) of whichever known actor type
        // owns this scoped resolution, so a scoped Proxy dependency can go through its real
        // Initialize pipeline. Plain-object owners (e.g. a raw scope key in a unit test) have no
        // context to recover; that's fine unless the constructed dependency turns out to be a
        // Proxy, which the caller above reports clearly.
        private static MvcActorContext? ResolveActorContext(object scopeOwner)
        {
            switch (scopeOwner)
            {
                case mvcExpress.Proxy p: return p.ActorContext;
                case ProxyBehaviour pb: return pb.ActorContext;
                case MediatorBehaviour m: return m.ActorContext;
                case MvcCommandBase cmd: return cmd.ActorContext;
                case MvcModule module: return module.ActorContext;
                default: return null;
            }
        }

        /// <summary>
        /// Disposes/cleans up every scoped instance owned by <paramref name="scopeOwner"/> -
        /// called exactly once, from that actor's own teardown (see the disposal-trigger map in
        /// SCOPED_LIFETIME_REDESIGN.md). Safe to call even when the owner has no scoped instances
        /// (cheap no-op) or was already released.
        /// </summary>
        internal void ReleaseScopedInstancesFor(object scopeOwner)
        {
            if (scopeOwner == null) return;
            if (!_scopedInstanceScopes.TryGetValue(scopeOwner, out var scope)) return;

            List<object> instances;
            Dictionary<object, List<object>> dependencyEdges;
            lock (scope.SyncRoot)
            {
                if (scope.IsDisposed) return;
                _scopedInstanceScopes.Remove(scopeOwner);
                scope.MarkDisposed();
                instances = new List<object>(scope.CreationOrder);
                dependencyEdges = scope.DependencyEdges.Count > 0
                    ? new Dictionary<object, List<object>>(scope.DependencyEdges, ReferenceIdentityComparer.Instance)
                    : null;
                scope.Instances.Clear();
                scope.CreationOrder.Clear();
                scope.DependencyEdges.Clear();
                scope.ConstructionStack.Clear();
            }

            var disposalOrder = ComputeScopedDisposalOrder(instances, dependencyEdges);

            List<Exception> errors = null;
            for (int i = 0; i < disposalOrder.Count; i++)
            {
                DisposeScopedInstance(disposalOrder[i], ref errors);
            }

            // This used to be gated behind an extra #if
            // UNITY_EDITOR||DEVELOPMENT_BUILD on top of MvcDebug.LogError's own [Conditional]
            // stripping - doubly restrictive, and inconsistent with every other cleanup path in
            // the framework (which just call MvcDebug.LogError directly, respecting MVC_LOGGING
            // like everywhere else). A teardown exception represents a bug in user code that can
            // leak a resource or corrupt long-lived state - unlike routine diagnostic logging, it
            // must be visible even in a clean release build, so it also always goes through
            // Unity's own Debug.LogException (bypassing MvcDebug's stripping entirely), matching
            // the precedent already established in MvcFacade.Update()'s deferred-publish handler.
            if (errors != null)
            {
                foreach (var ex in errors)
                {
                    MvcDebug.LogError($"[MvcExpress] Error releasing scoped instance owned by '{scopeOwner.GetType().Name}': {ex.Message}");
                    UnityEngine.Debug.LogException(ex);
                }
            }
        }

        private static void RecordDependencyEdge(ScopedInstanceScope scope, object dependent, object dependency)
        {
            if (!scope.DependencyEdges.TryGetValue(dependent, out var dependencies))
            {
                dependencies = new List<object>();
                scope.DependencyEdges[dependent] = dependencies;
            }
            dependencies.Add(dependency);
        }

        private static void RemoveDependencyEdge(ScopedInstanceScope scope, object dependent, object dependency)
        {
            if (!scope.DependencyEdges.TryGetValue(dependent, out var dependencies)) return;
            for (int i = dependencies.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(dependencies[i], dependency))
                {
                    dependencies.RemoveAt(i);
                    break;
                }
            }
        }

        // Orders `creationOrder` so every instance is disposed before the dependencies recorded
        // for it in `dependencyEdges` (a dependent's own OnCleanup()/Dispose() must never observe
        // an already-torn-down instance it still references - COR-17). Falls back to raw creation
        // order wherever no edge is known, which is also what a circular scoped dependency (A and B
        // each recorded as depending on the other) degrades to for the pair involved - no order can
        // satisfy both edges of a real cycle, so the cycle is broken rather than looped forever.
        private static List<object> ComputeScopedDisposalOrder(List<object> creationOrder, Dictionary<object, List<object>> dependencyEdges)
        {
            if (dependencyEdges == null || dependencyEdges.Count == 0)
            {
                return creationOrder;
            }

            var visited = new HashSet<object>(ReferenceIdentityComparer.Instance);
            var visiting = new HashSet<object>(ReferenceIdentityComparer.Instance);
            var postorder = new List<object>(creationOrder.Count);
            var stack = new Stack<(object node, int nextDependencyIndex)>();

            foreach (var root in creationOrder)
            {
                if (visited.Contains(root)) continue;

                stack.Push((root, 0));
                visiting.Add(root);

                while (stack.Count > 0)
                {
                    var (node, nextIndex) = stack.Pop();
                    var dependencies = dependencyEdges.TryGetValue(node, out var deps) ? deps : null;

                    if (dependencies != null && nextIndex < dependencies.Count)
                    {
                        var next = dependencies[nextIndex];
                        stack.Push((node, nextIndex + 1));

                        // Already finished, or currently an ancestor on this path (a circular
                        // scoped dependency) - either way, don't descend into it again.
                        if (!visited.Contains(next) && visiting.Add(next))
                        {
                            stack.Push((next, 0));
                        }
                    }
                    else
                    {
                        visiting.Remove(node);
                        visited.Add(node);
                        postorder.Add(node);
                    }
                }
            }

            postorder.Reverse();
            return postorder;
        }

        private static void DisposeScopedInstance(object instance, ref List<Exception> errors)
        {
            if (instance is mvcExpress.Proxy proxy)
            {
                try { proxy.Dispose(); }
                catch (Exception ex) { (errors ??= new List<Exception>()).Add(ex); }
                return;
            }

            if (instance is IMvcLifecycle lifecycle)
            {
                try { lifecycle.OnCleanup(); }
                catch (Exception ex) { (errors ??= new List<Exception>()).Add(ex); }
            }

            if (instance is IDisposable disposable)
            {
                try { disposable.Dispose(); }
                catch (Exception ex) { (errors ??= new List<Exception>()).Add(ex); }
            }
        }

        private static void DisposeScopedInstanceIgnoringErrors(object instance)
        {
            List<Exception> ignored = null;
            DisposeScopedInstance(instance, ref ignored);
        }

        #endregion

        #region Registration (Fluent Builder API)

        /// <summary>
        /// Begin registration using fluent builder.
        /// 
        /// NOTE: Each type can only be registered ONCE per scope (logic/view).
        /// Attempting to register the same type twice will throw an exception.
        /// </summary>
        /// <example>
        /// // Register to logic only as concrete type:
        /// Container.Register(proxy).ToLogic().AsPermanent();
        /// 
        /// // Register to view only as interface:
        /// Container.Register(proxy).ToViewAs&lt;IReadOnly&gt;().AsPermanent();
        /// 
        /// // Register to both with different types:
        /// Container.Register(proxy).ToLogic().ToViewAs&lt;IReadOnly&gt;().AsPermanent();
        /// 
        /// // Register to both as same interface:
        /// Container.Register(proxy).ToLogicAs&lt;IProxy&gt;().ToViewAs&lt;IProxy&gt;().AsPermanent();
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RegistrationBuilder<T> Register<T>(T instance)
        {
            return new RegistrationBuilder<T>(this, instance);
        }

        /// <summary>
        /// Begin registration with runtime type.
        /// 
        /// NOTE: Each type can only be registered ONCE per scope (logic/view).
        /// Attempting to register the same type twice will throw an exception.
        /// </summary>
        /// <example>
        /// // Runtime/reflection scenario:
        /// Type serviceType = Type.GetType(configuredTypeName);
        /// object instance = Activator.CreateInstance(serviceType);
        /// Container.Register(instance, serviceType).ToLogic().AsPermanent();
        /// 
        /// // Iterating through a collection:
        /// foreach (var service in services)
        /// {
        ///     Container.Register(service.Instance, service.Type).ToLogic().AsTransient();
        /// }
        /// </example>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public RegistrationBuilder Register(object instance, Type type)
        {
            return new RegistrationBuilder(this, instance, type);
        }

        // Undoes the direct-key commits phase 2 of RegisterInternal already made, for a chain
        // that fails later in the same call (either phase 2's own TryAdd race, or phase 3's
        // list-binding) - extracted so both catch blocks share one rollback path.
        private void RollbackCommittedDirectKeys(List<Type> addedLogic, List<Type> addedView, object obj)
        {
            for (int i = 0; i < addedLogic.Count; i++)
            {
                var type = addedLogic[i];
                var isInterface = _isInterfaceCache.GetOrAdd(type, t => t.IsInterface);
                var dict = isInterface ? _logicInterfaces : _logicObjects;
                dict.TryRemove(type, out _);
                _transientTypes.TryRemove(type, out _);
                DecrementInstanceRefCount(obj);
            }
            for (int i = 0; i < addedView.Count; i++)
            {
                var type = addedView[i];
                var isInterface = _isInterfaceCache.GetOrAdd(type, t => t.IsInterface);
                var dict = isInterface ? _viewInterfaces : _viewObjects;
                dict.TryRemove(type, out _);
                _transientTypes.TryRemove(type, out _);
                DecrementInstanceRefCount(obj);
            }
        }

        // Fully undoes a chain that RegisterInternal already committed successfully, for use
        // when the caller's post-commit initialization step (injection / OnInitialized(), wired
        // through OnNonScopedCommitted) throws. Intentionally bypasses
        // Unregister(Type)'s permanent-registration guard: a registration whose initialization
        // never completed should never have been considered committed in the first place.
        internal void RollbackFailedInitialization(
            List<Type> logicTypes,
            List<Type> viewTypes,
            object obj,
            bool registerToLogicList,
            Type logicListElementType,
            bool registerToViewList,
            Type viewListElementType)
        {
            if (registerToLogicList)
            {
                RollbackListBinding(obj, logicListElementType, useView: false);
            }
            if (registerToViewList)
            {
                RollbackListBinding(obj, viewListElementType, useView: true);
            }

            RollbackCommittedDirectKeys(logicTypes, viewTypes, obj);
        }

        /// <summary>
        /// Internal registration method called by RegistrationBuilder.
        /// </summary>
        internal void RegisterInternal(
            object obj,
            IReadOnlyList<Type> logicTypes,
            IReadOnlyList<Type> viewTypes,
            bool isTransient,
            bool registerToLogicList,
            Type logicListElementType,
            bool registerToViewList,
            Type viewListElementType)
        {
            if (obj == null)
                throw new InvalidOperationException("Cannot register null instance.");

            bool registerToLogic = logicTypes.Count > 0;
            bool registerToView = viewTypes.Count > 0;

            if (!registerToLogic && !registerToView && !registerToLogicList && !registerToViewList)
                throw new InvalidOperationException("Must register to at least one scope (logic, view, logic list, or view list).");

            if (registerToLogicList)
            {
                var listType = typeof(List<>).MakeGenericType(logicListElementType);
                if (IsScoped(listType))
                    throw new InvalidOperationException("[MvcExpress] Attempted to register an instance for a scoped list type.");
            }
            if (registerToViewList)
            {
                var listType = typeof(List<>).MakeGenericType(viewListElementType);
                if (IsScoped(listType))
                    throw new InvalidOperationException("[MvcExpress] Attempted to register an instance for a scoped list type.");
            }

            // Phase 1: validate every requested key is free before mutating anything, so a chain
            // requesting N types either registers all N or none of them.
            for (int i = 0; i < logicTypes.Count; i++)
            {
                var type = logicTypes[i];
                if (IsScoped(type))
                {
                    throw new InvalidOperationException(
                        $"[MvcExpress] Type '{type.FullName}' is already registered as scoped and cannot also be registered as an instance.");
                }
                var isInterface = _isInterfaceCache.GetOrAdd(type, t => t.IsInterface);
                var targetDict = isInterface ? _logicInterfaces : _logicObjects;
                if (targetDict.ContainsKey(type))
                {
                    throw new InvalidOperationException(
                        $"Type '{type.Name}' already registered in logic container. " +
                        $"Each type can only be registered once per scope. " +
                        $"Use distinct types or create a composite class for multiple implementations.");
                }
                // A type's registration identity spans both domains - _transientTypes and
                // UnregisterInternal key on type alone, so an independent second registration
                // under the other domain (different instance and/or lifetime) would silently
                // corrupt that shared state: make a permanent registration removable, make a
                // transient one impossible to remove, or let unregistering one domain destroy the
                // other's unrelated instance. A single instance
                // deliberately exposed to both domains remains fully supported via one chain
                // (e.g. .ToLogicAs<T>().ToViewAs<T>()) - that commits together in phase 2 below,
                // so it is never present here during phase 1's validation.
                var crossDomainDict = isInterface ? _viewInterfaces : _viewObjects;
                if (crossDomainDict.ContainsKey(type))
                {
                    throw new InvalidOperationException(
                        $"Type '{type.Name}' is already registered in the view container by a separate " +
                        $"Register(...) call. A type's registration identity spans both domains - to " +
                        $"expose one instance to both Logic and View, map it to both in the SAME " +
                        $"Register(...) chain (e.g. .ToLogicAs<T>().ToViewAs<T>()).");
                }
            }
            for (int i = 0; i < viewTypes.Count; i++)
            {
                var type = viewTypes[i];
                if (IsScoped(type))
                {
                    throw new InvalidOperationException(
                        $"[MvcExpress] Type '{type.FullName}' is already registered as scoped and cannot also be registered as an instance.");
                }
                var isInterface = _isInterfaceCache.GetOrAdd(type, t => t.IsInterface);
                var targetDict = isInterface ? _viewInterfaces : _viewObjects;
                if (targetDict.ContainsKey(type))
                {
                    throw new InvalidOperationException(
                        $"Type '{type.Name}' already registered in view container. " +
                        $"Each type can only be registered once per scope. " +
                        $"Use distinct types or create a composite class for multiple implementations.");
                }
                // See the matching comment in the logic-types loop above.
                var crossDomainDict = isInterface ? _logicInterfaces : _logicObjects;
                if (crossDomainDict.ContainsKey(type))
                {
                    throw new InvalidOperationException(
                        $"Type '{type.Name}' is already registered in the logic container by a separate " +
                        $"Register(...) call. A type's registration identity spans both domains - to " +
                        $"expose one instance to both Logic and View, map it to both in the SAME " +
                        $"Register(...) chain (e.g. .ToLogicAs<T>().ToViewAs<T>()).");
                }
            }

            // Phase 2: commit. TryAdd can still fail here under a concurrent registration racing
            // between phase 1 and phase 2 (both dictionaries are ConcurrentDictionary) - roll back
            // everything this call already added and re-throw, so the chain stays all-or-nothing.
            var addedLogic = new List<Type>(logicTypes.Count);
            var addedView = new List<Type>(viewTypes.Count);
            try
            {
                for (int i = 0; i < logicTypes.Count; i++)
                {
                    var type = logicTypes[i];
                    var isInterface = _isInterfaceCache.GetOrAdd(type, t => t.IsInterface);
                    var targetDict = isInterface ? _logicInterfaces : _logicObjects;
                    if (!targetDict.TryAdd(type, obj))
                    {
                        throw new InvalidOperationException(
                            $"Type '{type.Name}' already registered in logic container. " +
                            $"Each type can only be registered once per scope. " +
                            $"Use distinct types or create a composite class for multiple implementations.");
                    }
                    _transientTypes[type] = isTransient;
                    IncrementInstanceRefCount(obj);
                    addedLogic.Add(type);
                }

                for (int i = 0; i < viewTypes.Count; i++)
                {
                    var type = viewTypes[i];
                    var isInterface = _isInterfaceCache.GetOrAdd(type, t => t.IsInterface);
                    var targetDict = isInterface ? _viewInterfaces : _viewObjects;
                    if (!targetDict.TryAdd(type, obj))
                    {
                        throw new InvalidOperationException(
                            $"Type '{type.Name}' already registered in view container. " +
                            $"Each type can only be registered once per scope. " +
                            $"Use distinct types or create a composite class for multiple implementations.");
                    }
                    _transientTypes[type] = isTransient;
                    IncrementInstanceRefCount(obj);
                    addedView.Add(type);
                }
            }
            catch
            {
                RollbackCommittedDirectKeys(addedLogic, addedView, obj);
                throw;
            }

            // Phase 3: list-binding. Wrapped so a failure here (e.g. List<T> already occupied by a
            // non-list instance) rolls back both any list-binding this same chain just added AND
            // the direct keys committed in phase 2 - a chain must stay all-or-nothing.
            bool addedLogicListMember = false;
            bool addedViewListMember = false;
            try
            {
                if (registerToLogicList)
                {
                    AddToListBinding(obj, logicListElementType, useView: false, isTransient: isTransient);
                    addedLogicListMember = true;
                }

                if (registerToViewList)
                {
                    AddToListBinding(obj, viewListElementType, useView: true, isTransient: isTransient);
                    addedViewListMember = true;
                }
            }
            catch
            {
                if (addedLogicListMember)
                {
                    RollbackListBinding(obj, logicListElementType, useView: false);
                }
                if (addedViewListMember)
                {
                    RollbackListBinding(obj, viewListElementType, useView: true);
                }
                RollbackCommittedDirectKeys(addedLogic, addedView, obj);
                throw;
            }

            // A MonoBehaviour-vs-scoped-type conflict check used to live here,
            // AFTER phase 2/3 had already committed - so a rejected registration could still leave
            // direct keys or a list binding committed. Removed rather than relocated: it checked
            // IsScoped(type) for the same logicTypes/viewTypes phase 1 above (lines ~1104-1169)
            // already checks IsScoped(type) for, unconditionally, before any commit - so phase 1
            // always threw first, with a differently-worded but equally accurate message ("already
            // registered as scoped"), making this second check unreachable in every code path that
            // can actually occur. Confirmed empirically (a MonoBehaviour registered against an
            // already-scoped type key throws phase 1's message, never reaches here) before removing -
            // see Tests/Runtime/MvcDiContainer_RegistrationAtomicity_UnitTests.cs.

#if UNITY_EDITOR
            // Registration has already fully committed by this point (phase
            // 2/3 above). A throwing debug-event subscriber (e.g. a broken ProxyHierarchyVisualizer)
            // must not be able to make this call report failure while its state is actually
            // committed - isolate it the same way MvcFacade.Update() isolates one bad deferred
            // publisher from the rest of its queue, rather than let it propagate uncaught.
            try
            {
                if (obj is mvcExpress.Proxy || obj is mvcExpress.ProxyBehaviour)
                {
                    ProxyRegisteredForDebug?.Invoke(obj);
                }
                else if (!(obj is UnityEngine.MonoBehaviour))
                {
                    ServiceRegisteredForDebug?.Invoke(obj);
                }
            }
            catch (Exception ex)
            {
                MvcDebug.LogError($"[MvcExpress] A debug-registration subscriber threw for '{obj.GetType().FullName}': {ex.Message}");
                UnityEngine.Debug.LogException(ex);
            }
#endif
        }

        private void AddToListBinding(object obj, Type elementType, bool useView, bool isTransient)
        {
            if (elementType == null)
                throw new ArgumentNullException(nameof(elementType));

            var listType = typeof(List<>).MakeGenericType(elementType);
            var readOnlyListType = typeof(IReadOnlyList<>).MakeGenericType(elementType);

            // Prevent mixing list-binding with an already-registered non-list instance for the same
            // List<T> key - without this, TryGetValue below would find that instance, and the
            // `(IList)existing` cast a few lines down would throw a confusing InvalidCastException
            // instead of a clear, actionable error.
            object existingAtListType =
                (_logicObjects.TryGetValue(listType, out var lo) ? lo : null) ??
                (_logicInterfaces.TryGetValue(listType, out var li) ? li : null) ??
                (_viewObjects.TryGetValue(listType, out var vo) ? vo : null) ??
                (_viewInterfaces.TryGetValue(listType, out var vi) ? vi : null);

            if (existingAtListType != null && existingAtListType is not System.Collections.IList)
            {
                throw new InvalidOperationException(
                    $"[MvcExpress] Cannot register a list binding for '{elementType.FullName}': " +
                    $"'{listType.FullName}' is already registered as a non-list instance. Registering " +
                    $"both a list binding and a direct instance under the same List<T> type is not supported.");
            }

            var objectsDict = useView ? _viewObjects : _logicObjects;
            var interfacesDict = useView ? _viewInterfaces : _logicInterfaces;
            var membersDict = useView ? _viewListMembers : _logicListMembers;

            if (!objectsDict.TryGetValue(listType, out var existing))
            {
                existing = Activator.CreateInstance(listType);

                // Free read-only exposure: List<T> already implements IReadOnlyList<T>,
                // so the same instance can be aliased under that interface at zero extra cost (no
                // copy, no wrapper). Callers that only need to read the list can
                // Resolve<IReadOnlyList<T>>() instead of Resolve<List<T>>(). Guarded against the
                // (unlikely) case that IReadOnlyList<T> was already independently registered to a
                // different instance, so this can't silently steal that key.
                if (interfacesDict.TryGetValue(readOnlyListType, out var existingAlias) &&
                    !ReferenceEquals(existingAlias, existing))
                {
                    throw new InvalidOperationException(
                        $"[MvcExpress] Cannot register a list binding for '{elementType.FullName}': " +
                        $"'{readOnlyListType.FullName}' is already registered to a different instance. " +
                        $"ToLogicList<T>()/ToViewList<T>() automatically exposes the list under " +
                        $"IReadOnlyList<T> as well as List<T>.");
                }

                if (objectsDict.TryAdd(listType, existing))
                {
                    interfacesDict[readOnlyListType] = existing;
                }
                else
                {
                    // Lost a concurrent creation race - use the winner's list instance instead.
                    objectsDict.TryGetValue(listType, out existing);
                }
            }

            ((System.Collections.IList)existing).Add(obj);
            _transientTypes[listType] = isTransient;

            // List membership is its own reference to `obj`, exactly like a direct-key alias
            // (.ToLogicAs<IFoo>().ToView()). Without this, unregistering a direct key
            // the same instance also holds could dispose it while it is still live inside the list.
            IncrementInstanceRefCount(obj);

            var members = membersDict.GetOrAdd(elementType,
                _ => new ConcurrentDictionary<object, bool>(ReferenceIdentityComparer.Instance));
            members[obj] = isTransient;
        }

        // Pure bookkeeping: removes `instance` from one List<T>'s membership tracking and the list
        // itself, tearing down the List<T> registration (and its IReadOnlyList<T> alias) once it
        // becomes empty. Does not touch ref-counting or dispose the instance - callers decide that,
        // since this is reused both for a real removal (which should release/dispose) and for
        // rolling back a list-binding that a failed registration chain already added (which must
        // not dispose an instance the caller still owns).
        private bool RemoveOneListMember(object instance, Type elementType, bool useView)
        {
            var membersDict = useView ? _viewListMembers : _logicListMembers;
            if (!membersDict.TryGetValue(elementType, out var members)) return false;
            if (!members.TryRemove(instance, out _)) return false;

            var objectsDict = useView ? _viewObjects : _logicObjects;
            var interfacesDict = useView ? _viewInterfaces : _logicInterfaces;
            var listType = typeof(List<>).MakeGenericType(elementType);
            var readOnlyListType = typeof(IReadOnlyList<>).MakeGenericType(elementType);

            if (objectsDict.TryGetValue(listType, out var listObj))
            {
                var list = (System.Collections.IList)listObj;
                list.Remove(instance);
                if (list.Count == 0)
                {
                    objectsDict.TryRemove(listType, out _);
                    interfacesDict.TryRemove(readOnlyListType, out _);
                    _transientTypes.TryRemove(listType, out _);
                    membersDict.TryRemove(elementType, out _);
                }
            }

            return true;
        }

        // Undoes a list-binding this SAME failed registration chain just added - bookkeeping only,
        // no dispose (the instance is the caller's own and the chain never committed).
        private void RollbackListBinding(object obj, Type elementType, bool useView)
        {
            if (RemoveOneListMember(obj, elementType, useView))
            {
                DecrementInstanceRefCount(obj);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unregister<T>()
        {
            UnregisterInternal(typeof(T));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Unregister(Type type)
        {
            UnregisterInternal(type);
        }

        // Removes `instance` from every list binding it currently participates in (logic and
        // view), disposing it via the normal ref-counted teardown once its last reference (list
        // membership or direct key) is gone. Backs the public UnregisterInstance(object).
        private void RemoveFromListBindings(object instance)
        {
            if (instance == null) return;

            foreach (var kvp in _logicListMembers)
            {
                if (RemoveOneListMember(instance, kvp.Key, useView: false))
                {
                    ReleaseInstanceKey(instance, kvp.Key);
                }
            }

            foreach (var kvp in _viewListMembers)
            {
                if (RemoveOneListMember(instance, kvp.Key, useView: true))
                {
                    ReleaseInstanceKey(instance, kvp.Key);
                }
            }
        }

        /// <summary>
        /// Removes a single instance from every transient list binding it participates in
        /// (<c>.ToLogicList&lt;T&gt;()</c>/<c>.ToViewList&lt;T&gt;()</c>), disposing it once its
        /// last reference (this list membership, or any remaining direct key) is released. Use
        /// this to remove one member of a list without affecting the rest of the list or any
        /// other registration. No-ops if <paramref name="instance"/> is not a list member anywhere.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// Thrown if the instance is a PERMANENT list member. Permanent list bindings, like
        /// permanent direct registrations, cannot be individually removed.
        /// </exception>
        public void UnregisterInstance(object instance)
        {
            if (instance == null) return;

            foreach (var kvp in _logicListMembers)
            {
                if (kvp.Value.TryGetValue(instance, out var isTransient) && !isTransient)
                {
                    throw new InvalidOperationException(
                        $"[MvcExpress] Cannot unregister instance of type '{instance.GetType().FullName}': " +
                        $"it is registered as a PERMANENT member of List<{kvp.Key.Name}>. Permanent " +
                        $"list members cannot be individually removed. Use .AsTransient() at " +
                        $"registration if this member has a dynamic lifecycle.");
                }
            }
            foreach (var kvp in _viewListMembers)
            {
                if (kvp.Value.TryGetValue(instance, out var isTransient) && !isTransient)
                {
                    throw new InvalidOperationException(
                        $"[MvcExpress] Cannot unregister instance of type '{instance.GetType().FullName}': " +
                        $"it is registered as a PERMANENT member of List<{kvp.Key.Name}>. Permanent " +
                        $"list members cannot be individually removed. Use .AsTransient() at " +
                        $"registration if this member has a dynamic lifecycle.");
                }
            }

            RemoveFromListBindings(instance);
        }

        private void UnregisterInternal(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            // Check if type is registered as permanent (cannot be destroyed)
            if (_transientTypes.TryGetValue(type, out var isTransient) && !isTransient)
            {
                throw new InvalidOperationException($"[MvcExpress] Cannot unregister permanent type '{type.FullName}'. Permanent dependencies cannot be destroyed. Use .AsTransient() during registration if this dependency has a dynamic lifecycle.");
            }

            // Scoped registrations have no shared instance to dispose here. Remove the alias
            // from both domains; existing per-owner instances remain valid until their owners
            // die, while future resolutions can no longer create or retrieve one through this
            // alias. Drop the descriptor after its final alias is gone.
            lock (_scopedRegistrationLock)
            {
                int scopedRegistrationId = 0;
                bool removedScoped = _logicScopedTypeToRegistrationId.TryRemove(type, out scopedRegistrationId);
                if (_viewScopedTypeToRegistrationId.TryRemove(type, out var viewScopedRegistrationId))
                {
                    removedScoped = true;
                    if (scopedRegistrationId == 0) scopedRegistrationId = viewScopedRegistrationId;
                }

                if (removedScoped && !HasScopedAliases(scopedRegistrationId))
                {
                    _scopedRegistrations.TryRemove(scopedRegistrationId, out _);
                }
            }

            // Remove from all containers under this key. A dual-scope registration
            // (.ToLogicAs<IFoo>().ToView()) keys the same instance under two different Types, so
            // this only removes the key matching `type` here; ReleaseInstanceKey below decides
            // whether the instance itself is actually torn down, based on the shared ref-count.
            bool removed = false;

            if (_logicObjects.TryRemove(type, out var logicObj))
            {
                removed = true;
                ReleaseInstanceKey(logicObj, type);
            }
            if (_logicInterfaces.TryRemove(type, out var logicInterface))
            {
                removed = true;
                ReleaseInstanceKey(logicInterface, type);
            }
            if (_viewObjects.TryRemove(type, out var viewObj))
            {
                removed = true;
                ReleaseInstanceKey(viewObj, type);
            }
            if (_viewInterfaces.TryRemove(type, out var viewInterface))
            {
                removed = true;
                ReleaseInstanceKey(viewInterface, type);
            }

            if (removed && isTransient)
            {
                // MEMORY LEAK FIX: Trigger command invalidation using weak event manager
                _transientDependencyRemoved.Raise(type);
            }

            _transientTypes.TryRemove(type, out _);
        }

        private bool HasScopedAliases(int registrationId)
        {
            foreach (var id in _logicScopedTypeToRegistrationId.Values)
            {
                if (id == registrationId) return true;
            }

            foreach (var id in _viewScopedTypeToRegistrationId.Values)
            {
                if (id == registrationId) return true;
            }

            return false;
        }

        // Releases one container key's reference to `instance` and, only once no other key
        // (e.g. the other half of a .ToLogicAs<IFoo>().ToView() dual-scope registration) still
        // resolves to it, tears down its proxy-hierarchy visualization / GameObject and disposes
        // it. Called once per key removed by UnregisterInternal.
        private void ReleaseInstanceKey(object instance, Type type)
        {
            if (instance == null) return;
            if (!DecrementInstanceRefCount(instance)) return; // still resolvable under another key

#if UNITY_EDITOR
            // Code-only proxies have an editor-only wrapper GO under Model, created by
            // ProxyHierarchyVisualizer in response to ProxyRegisteredForDebug. Fire the
            // matching unregistered event so it can remove its own tracked wrapper directly
            // (this used to scan every MvcModule in the scene to find the wrapper).
            if (!(instance is ProxyBehaviour))
            {
                ProxyUnregisteredForDebug?.Invoke(instance);
            }
#endif

            TearDownOwnedInstance(instance, type);
        }

        // Actual teardown of one owned instance: destroys a ProxyBehaviour's GameObject through
        // Unity's normal destroy policy (a bare IDisposable check is not enough for a Unity
        // object), or disposes it if it implements IDisposable. Shared by the
        // single-key release path (ReleaseInstanceKey, ref-counted) and the bulk Clear() path
        // (reference-equality deduplicated) - each caller is responsible for ensuring this runs
        // exactly once per owned instance.
        private void TearDownOwnedInstance(object instance, Type typeForLogging)
        {
            if (instance is ProxyBehaviour pb)
            {
                if (pb != null)
                {
                    var go = pb.gameObject;
                    if (go != null)
                    {
#if UNITY_EDITOR
                        if (Application.isPlaying)
                            UnityEngine.Object.Destroy(go);
                        else
                            UnityEngine.Object.DestroyImmediate(go);
#else
                        UnityEngine.Object.Destroy(go);
#endif
                    }
                }
            }

            // Dispose the instance if it implements IDisposable
            if (instance is IDisposable disposable)
            {
                try
                {
                    disposable.Dispose();
                }
                catch (Exception ex)
                {
                    // See the matching comment in ReleaseScopedInstancesFor.
                    MvcDebug.LogError($"Error disposing instance of type '{typeForLogging.FullName}': {ex.Message}");
                    UnityEngine.Debug.LogException(ex);
                }
            }
        }

        #endregion

        #region Inject (module-scoped, auto-detects logic vs view scope)

        /// <summary>
        /// Resolves a dependency from the current scope.
        /// </summary>
        /// <exception cref="InvalidOperationException">Thrown if the type is not registered in the active scope.</exception>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public T Resolve<T>()
        {
            return Resolve<T>(caller: null);
        }

        /// <summary>
        /// Resolves a dependency from the current scope with caller reference for error reporting.
        /// </summary>
        internal T Resolve<T>(object caller)
        {
            return ResolveWithScope<T>(IsViewScope, caller);
        }

        /// <summary>
        /// Resolves a dependency from an explicitly chosen scope, bypassing the ambient
        /// <see cref="IsViewScope"/> flag.
        /// </summary>
        /// <remarks>
        /// The ambient flag only reflects the correct scope while a <see cref="BeginViewScope"/>
        /// window is open (i.e. during a mediator's injection + <c>OnInitialized()</c> call). Actor
        /// APIs whose scope is fixed by the actor's type itself - a mediator is always view, a proxy
        /// is always logic - must use this overload instead, so calls made outside that window (an
        /// <c>OnEnable</c>, <c>Update</c>, or any other later callback) still resolve correctly.
        /// </remarks>
        internal T ResolveWithScope<T>(bool useViewScope, object caller = null)
        {
            var type = typeof(T);

            // TryResolveInternal already falls back to a scoped resolution (dedicated to
            // `caller`, the injectee) when the type isn't found in the plain containers.
            if (TryResolveInternal(type, useViewScope, caller, out var value))
            {
                return (T)value;
            }

            // ERROR PATH: Build context string only when error occurs
            var scope = useViewScope ? "view" : "logic";
            var callerInfo = BuildCallerContext(caller);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            var diagnosticHint = BuildDiagnosticHint(type);
            if (diagnosticHint != null)
            {
                throw new InvalidOperationException(
                    $"Type '{type.FullName}' is not registered in {scope} scope.{callerInfo}" +
                    diagnosticHint);
            }
#endif

            throw new InvalidOperationException(
                $"Type '{type.FullName}' is not registered in {scope} scope (or as scoped).{callerInfo}\n\n" +
                $"To fix this:\n" +
                $"1. Register the type in your module's RegisterProxies() or RegisterServices() method:\n" +
                $"   Register(new {type.Name}()).To{(useViewScope ? "View" : "Logic")}().AsPermanent();\n" +
                $"2. If this is a view-layer dependency, ensure it's registered with .ToView() or .ToViewAs<T>()\n" +
                $"3. If this is a logic-layer dependency in a mediator, you may need to register it to both scopes:\n" +
                $"   Register(instance).ToLogic().ToView().AsPermanent();");
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// Checks other containers to provide an actionable hint when Resolve fails.
        /// Only called in Editor/Development builds, only on the error path.
        /// </summary>
        private string BuildDiagnosticHint(Type type)
        {
            try
            {
                var globalContainer = MvcFacade.GlobalContainerOrNull;
                if (globalContainer == null) return null;

                bool isGlobalContainer = ReferenceEquals(this, globalContainer);

                if (isGlobalContainer)
                {
                    // Called ResolveGlobal<T>() - check if type lives in a module container
                    var app = MvcFacade.InstanceOrNull;
                    if (app == null) return null;

                    foreach (var kvp in app.Modules)
                    {
                        var moduleContainer = kvp.Value.DiContainer;
                        if (moduleContainer != null && moduleContainer.TryResolveInternal(type, false, null, out _))
                        {
                            return $"\n\n★ WRONG CONTAINER: '{type.Name}' is registered in module '{kvp.Key.Name}', not in the global container.\n" +
                                   $"  You called ResolveGlobal<{type.Name}>() but this is a module-scoped dependency.\n" +
                                   $"  Fix: Use Resolve<{type.Name}>() instead.";
                        }
                    }
                }
                else
                {
                    // Called Resolve<T>() - first check the global container
                    if (globalContainer.TryResolveInternal(type, false, null, out _))
                    {
                        return $"\n\n★ WRONG CONTAINER: '{type.Name}' is registered in the global container, not this module.\n" +
                               $"  You called Resolve<{type.Name}>() but this is a global dependency.\n" +
                               $"  Fix: Use ResolveGlobal<{type.Name}>() instead.";
                    }

                    // Check all other module containers for cross-module access attempt
                    var app = MvcFacade.InstanceOrNull;
                    if (app != null)
                    {
                        foreach (var kvp in app.Modules)
                        {
                            var moduleContainer = kvp.Value.DiContainer;
                            if (moduleContainer == null || ReferenceEquals(moduleContainer, this)) continue;

                            if (moduleContainer.TryResolveInternal(type, false, null, out _))
                            {
                                return $"\n\n★ CROSS-MODULE ACCESS: '{type.Name}' is registered in module '{kvp.Key.Name}', not in this module.\n" +
                                       $"  Accessing another module's dependencies directly is not allowed.\n" +
                                       $"  Fix: Register '{type.Name}' in the global container with RegisterGlobal(), " +
                                       $"then use ResolveGlobal<{type.Name}>() to access it from any module.";
                            }
                        }
                    }
                }
            }
            catch
            {
                // Diagnostic hint must never itself throw - silently fall back to the standard error
            }

            return null;
        }
#endif

        /// <summary>
        /// Tries to resolve a dependency without throwing.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryResolve<T>(out T value)
        {
            return TryResolveWithScope(IsViewScope, out value);
        }

        /// <summary>
        /// Tries to resolve a dependency without throwing, threading <paramref name="caller"/>
        /// through as the Scoped-resolution owner. See <see cref="Resolve{T}(object)"/> for why
        /// actor APIs need this overload instead of the bare <see cref="TryResolve{T}(out T)"/>
        /// (which implements <see cref="IModuleDependencyResolver"/> and must keep that exact
        /// single-parameter signature).
        /// </summary>
        internal bool TryResolve<T>(out T value, object caller)
        {
            return TryResolveWithScope(IsViewScope, out value, caller);
        }

        /// <summary>
        /// Tries to resolve a dependency from an explicitly chosen scope, bypassing the ambient
        /// <see cref="IsViewScope"/> flag. See <see cref="ResolveWithScope{T}"/> for why actor APIs
        /// with a scope fixed by actor type need this instead of the ambient-flag overload.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal bool TryResolveWithScope<T>(bool useViewScope, out T value, object caller = null)
        {
            var type = typeof(T);

            if (TryResolveInternal(type, useViewScope, caller, out var resolved))
            {
                value = (T)resolved;
                return true;
            }

            value = default;
            return false;
        }

        /// <summary>
        /// Resolves <paramref name="type"/> from the plain logic/view containers, falling back to
        /// a scoped resolution dedicated to <paramref name="scopeOwner"/> (the injectee) when not
        /// found there - see <see cref="TryResolveScopedForOwner"/>.
        /// </summary>
        internal bool TryResolveInternal(Type type, bool useViewScope, object scopeOwner, out object value)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            var isInterface = _isInterfaceCache.GetOrAdd(type, t => t.IsInterface);

            if (useViewScope)
            {
                if (isInterface)
                {
                    if (_viewInterfaces.TryGetValue(type, out value)) return true;
                }
                else
                {
                    if (_viewObjects.TryGetValue(type, out value)) return true;
                }
            }
            else
            {
                if (isInterface)
                {
                    if (_logicInterfaces.TryGetValue(type, out value)) return true;
                }
                else
                {
                    if (_logicObjects.TryGetValue(type, out value)) return true;
                }
            }

            // Fallback to a scoped resolution dedicated to scopeOwner.
            if (TryResolveScopedForOwner(type, scopeOwner, useViewScope, out value))
            {
                return true;
            }

            value = null;
            return false;
        }

        internal object Resolve(Type type, bool useViewScope)
        {
            if (TryResolveInternal(type ?? throw new ArgumentNullException(nameof(type)), useViewScope, null, out var value))
            {
                return value;
            }

            var scope = useViewScope ? "view" : "logic";
            throw new InvalidOperationException($"Type '{type.FullName}' is not registered in {scope} container.");
        }

        #endregion

        #region IDisposable Support

        /// <summary>
        /// Get all registered types in this container (for debugging/error messages).
        /// </summary>
        internal List<string> GetRegisteredTypes()
        {
            var types = new List<string>();
            
            foreach (var kvp in _logicObjects)
                types.Add(kvp.Key.Name);
            foreach (var kvp in _logicInterfaces)
                types.Add(kvp.Key.Name);
            foreach (var kvp in _viewObjects)
                types.Add(kvp.Key.Name);
            foreach (var kvp in _viewInterfaces)
                types.Add(kvp.Key.Name);
            
            return types;
        }

        // Unity's bundled BCL only exposes an internal (inaccessible) System.Collections.Generic.
        // ReferenceEqualityComparer, not the public one added in .NET 5 - this is a minimal local
        // stand-in so a HashSet<object> can dedupe strictly by identity, ignoring any Equals/
        // GetHashCode override a registered instance's type might define.
        // Internal (not private) so ModuleInitializer's own identity-dedup structures can reuse it
        // instead of re-implementing it.
        internal sealed class ReferenceIdentityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceIdentityComparer Instance = new ReferenceIdentityComparer();
            private ReferenceIdentityComparer() { }
            public new bool Equals(object x, object y) => ReferenceEquals(x, y);
            public int GetHashCode(object obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        // Adds every non-null instance in `dict` to `into` (reference-equality deduplicated -
        // `into` must be constructed with ReferenceIdentityComparer.Instance). Used by Clear()
        // to find each uniquely-owned instance before any index is cleared.
        private static void CollectInstances(ConcurrentDictionary<Type, object> dict, HashSet<object> into)
        {
            foreach (var kvp in dict)
            {
                if (kvp.Value != null)
                {
                    into.Add(kvp.Value);
                }
            }
        }

        // Adds every list-bound member tracked in `listMembers` to `into`. A list-only member
        // (no direct key) is never a VALUE in the four main dictionaries - only the List<T>
        // container itself is - so without this, Clear() would never dispose it at all.
        private static void CollectListMembers(
            ConcurrentDictionary<Type, ConcurrentDictionary<object, bool>> listMembers, HashSet<object> into)
        {
            foreach (var kvp in listMembers)
            {
                foreach (var memberKvp in kvp.Value)
                {
                    into.Add(memberKvp.Key);
                }
            }
        }

        /// <summary>
        /// Dispose pattern - cleanup all registrations and event subscriptions.
        /// </summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Clear();
        }

        /// <summary>
        /// Forcefully clears all registrations and disposes all registered instances.
        /// Use with caution - this bypasses persistence rules and is primarily intended for testing or deep resets.
        /// </summary>
        public void Clear()
        {
            // Collect each REFERENCE-DISTINCT owned instance BEFORE touching any index. An
            // instance aliased under multiple keys/domains (e.g. .ToLogicAs<IFoo>().ToView()) is
            // torn down exactly once below instead of once per dictionary it appears in.
            // ReferenceIdentityComparer.Instance is required: an instance
            // whose type overrides Equals/GetHashCode for value semantics must still be
            // deduplicated by identity, not by value equality.
            var uniqueInstances = new HashSet<object>(ReferenceIdentityComparer.Instance);
            CollectInstances(_logicObjects, uniqueInstances);
            CollectInstances(_logicInterfaces, uniqueInstances);
            CollectInstances(_viewObjects, uniqueInstances);
            CollectInstances(_viewInterfaces, uniqueInstances);
            CollectListMembers(_logicListMembers, uniqueInstances);
            CollectListMembers(_viewListMembers, uniqueInstances);

            // Clear all dictionaries
            _logicObjects.Clear();
            _logicInterfaces.Clear();
            _viewObjects.Clear();
            _viewInterfaces.Clear();
            _transientTypes.Clear();
            _scopedRegistrations.Clear();
            _logicScopedTypeToRegistrationId.Clear();
            _viewScopedTypeToRegistrationId.Clear();
            // _scopedInstanceScopes (ConditionalWeakTable) is intentionally not enumerated/cleared
            // here - each actor releases its own scope explicitly at its own teardown (see the
            // disposal-trigger map in SCOPED_LIFETIME_REDESIGN.md); by the time a module's
            // container Dispose()/Clear() runs, its actors have already released themselves.

            // List-binding membership maps hold strong references to every instance ever registered
            // via ToLogicList/ToViewList - without this they outlive the container.
            _logicListMembers.Clear();
            _viewListMembers.Clear();

            // Per-instance key-refcounts used by UnregisterInternal - Clear() disposes
            // everything unconditionally, so the bookkeeping is now meaningless.
            lock (_refCountLock)
            {
                _instanceKeyRefCounts.Clear();
            }

            // Tear down each owned instance exactly once, now that no index still references it.
            foreach (var instance in uniqueInstances)
            {
                TearDownOwnedInstance(instance, instance.GetType());
            }

            // Clear event subscriptions
            _transientDependencyRemoved.Clear();
        }

        #endregion

        #region Internal Diagnostics & Tooling Support

        /// <summary>
        /// Enumerates all registered instances across all scopes (logic + view).
        /// Used for initialization callbacks and external tooling/debugging.
        /// INTERNAL USE ONLY - Not part of public API.
        /// </summary>
        internal IEnumerable<object> EnumerateAllInstances()
        {
            foreach (var kvp in _logicObjects)
            {
                if (kvp.Value != null)
                    yield return kvp.Value;
            }

            foreach (var kvp in _logicInterfaces)
            {
                if (kvp.Value != null)
                    yield return kvp.Value;
            }

            foreach (var kvp in _viewObjects)
            {
                if (kvp.Value != null)
                    yield return kvp.Value;
            }

            foreach (var kvp in _viewInterfaces)
            {
                if (kvp.Value != null)
                    yield return kvp.Value;
            }
        }

        /// <summary>
        /// Container scope where a registration is visible.
        /// </summary>
        internal enum RegistrationScope
        {
            Logic,
            View
        }

        /// <summary>
        /// Diagnostic row describing one container registration.
        /// </summary>
        internal readonly struct RegistrationSnapshot
        {
            public readonly Type RegisteredType;
            public readonly object Instance;
            public readonly RegistrationScope Scope;
            public readonly RegistrationLifecycle Lifecycle;
            public readonly bool IsListBinding;

            public RegistrationSnapshot(Type registeredType, object instance, RegistrationScope scope, RegistrationLifecycle lifecycle, bool isListBinding)
            {
                RegisteredType = registeredType;
                Instance = instance;
                Scope = scope;
                Lifecycle = lifecycle;
                IsListBinding = isListBinding;
            }
        }

        /// <summary>
        /// Returns a snapshot of current registrations (types + instances) across logic and view scopes.
        /// This is intended for tooling/debugging.
        /// </summary>
        internal void GetRegistrationSnapshot(List<RegistrationSnapshot> results)
        {
            if (results == null)
                throw new ArgumentNullException(nameof(results));

            results.Clear();

            AppendSnapshot(_logicObjects, RegistrationScope.Logic, results);
            AppendSnapshot(_logicInterfaces, RegistrationScope.Logic, results);
            AppendSnapshot(_viewObjects, RegistrationScope.View, results);
            AppendSnapshot(_viewInterfaces, RegistrationScope.View, results);

            // Add scoped types that do not have instance entries (scoped registrations never
            // occupy the plain instance dictionaries - each injectee gets its own dedicated
            // instance on resolve, not one shared instance stored here).
            foreach (var kvp in _logicScopedTypeToRegistrationId)
            {
                var t = kvp.Key;
                if (t == null) continue;
                if (!_scopedRegistrations.ContainsKey(kvp.Value)) continue;

                results.Add(new RegistrationSnapshot(t, instance: null, scope: RegistrationScope.Logic, lifecycle: RegistrationLifecycle.Scoped, isListBinding: false));
            }

            foreach (var kvp in _viewScopedTypeToRegistrationId)
            {
                var t = kvp.Key;
                if (t == null) continue;
                if (!_scopedRegistrations.ContainsKey(kvp.Value)) continue;

                results.Add(new RegistrationSnapshot(t, instance: null, scope: RegistrationScope.View, lifecycle: RegistrationLifecycle.Scoped, isListBinding: false));
            }
        }

        private void AppendSnapshot(ConcurrentDictionary<Type, object> dict, RegistrationScope scope, List<RegistrationSnapshot> results)
        {
            foreach (var kvp in dict)
            {
                var t = kvp.Key;
                var instance = kvp.Value;
                if (t == null) continue;

                var isScoped = IsScoped(t);
                var lifecycle = isScoped ? RegistrationLifecycle.Scoped : (IsTransient(t) ? RegistrationLifecycle.Transient : RegistrationLifecycle.Permanent);
                var isListBinding = t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>);

                results.Add(new RegistrationSnapshot(t, instance, scope, lifecycle, isListBinding));
            }
        }

        #endregion

        /// <summary>
        /// Builds caller context string from caller object (only called on error path).
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static string BuildCallerContext(object caller)
        {
            if (caller == null)
                return string.Empty;

            // Check specific types and extract relevant context
            if (caller is MediatorBehaviour mediator)
            {
                var typeName = caller.GetType().FullName;
                var goName = mediator.gameObject != null ? mediator.gameObject.name : "null";
                return $"\nCalled from: Mediator '{typeName}' (GameObject: '{goName}')";
            }

            if (caller is mvcExpress.Proxy)
            {
                return $"\nCalled from: Proxy '{caller.GetType().FullName}'";
            }

            if (caller is mvcExpress.ProxyBehaviour)
            {
                return $"\nCalled from: Proxy '{caller.GetType().FullName}'";
            }

            if (caller is MvcCommandBase)
            {
                return $"\nCalled from: Command '{caller.GetType().FullName}'";
            }

            // Fallback for unknown types
            return $"\nCalled from: {caller.GetType().FullName}";
        }
    }
}

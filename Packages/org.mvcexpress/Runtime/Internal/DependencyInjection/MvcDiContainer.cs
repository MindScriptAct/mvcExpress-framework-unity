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
        private Type _logicType;
        private Type _viewType;
        private bool _registerToLogic;
        private bool _registerToView;
        private bool _isTransient = false;
        private bool _isScoped = false;
        private bool _completed = false;

        private Type _logicListElementType;
        private Type _viewListElementType;
        private bool _registerToLogicList;
        private bool _registerToViewList;

        internal RegistrationBuilder(MvcDiContainer container, object instance)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _instance = instance ?? throw new ArgumentNullException(nameof(instance));

            // Default: register as instance type
            _logicType = instance.GetType();
            _viewType = instance.GetType();
            _registerToLogic = false;
            _registerToView = false;
        }

        /// <summary>
        /// Register to logic layer as concrete type.
        /// </summary>
        public RegistrationBuilder<T> ToLogic()
        {
            ThrowIfCompleted();
            _registerToLogic = true;
            _logicType = _instance.GetType();
            return this;
        }

        /// <summary>
        /// Register to logic layer as specified type.
        /// </summary>
        public RegistrationBuilder<T> ToLogicAs<TLogic>()
        {
            ThrowIfCompleted();
            _registerToLogic = true;
            _logicType = typeof(TLogic);
            return this;
        }

        /// <summary>
        /// Register to view layer as concrete type.
        /// </summary>
        public RegistrationBuilder<T> ToView()
        {
            ThrowIfCompleted();
            _registerToView = true;
            _viewType = _instance.GetType();
            return this;
        }

        /// <summary>
        /// Register to view layer as specified type.
        /// </summary>
        public RegistrationBuilder<T> ToViewAs<TView>()
        {
            ThrowIfCompleted();
            _registerToView = true;
            _viewType = typeof(TView);
            return this;
        }

        /// <summary>
        /// Register to logic layer as List of specified type.
        /// </summary>
        public RegistrationBuilder<T> ToLogicList<TLogic>()
        {
            ThrowIfCompleted();
            _registerToLogicList = true;
            _logicListElementType = typeof(TLogic);
            return this;
        }

        /// <summary>
        /// Register to view layer as List of specified type.
        /// </summary>
        public RegistrationBuilder<T> ToViewList<TView>()
        {
            ThrowIfCompleted();
            _registerToViewList = true;
            _viewListElementType = typeof(TView);
            return this;
        }

        /// <summary>
        /// Complete registration as persistent dependency.
        /// </summary>
        public void AsPersistent()
        {
            ThrowIfCompleted();
            _isTransient = false;
            Complete();
        }

        /// <summary>
        /// Complete registration as transient dependency.
        /// </summary>
        public void AsTransient()
        {
            ThrowIfCompleted();
            _isTransient = true;
            Complete();
        }

        /// <summary>
        /// Complete registration as scoped dependency.
        /// </summary>
        public void AsScoped()
        {
            ThrowIfCompleted();
            _isScoped = true;
            _isTransient = false;
            Complete();
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

            if (!_registerToLogic && !_registerToView && !_registerToLogicList && !_registerToViewList)
            {
                throw new InvalidOperationException(
                    "Must register to at least one layer. Call .ToLogic(), .ToLogicAs<T>(), .ToView(), .ToViewAs<T>, .ToLogicList<T>(), or .ToViewList<T>() before completing registration.");
            }

            if (_isScoped)
            {
                // AsScoped means: do not register an instance; register the type as scoped factory instead.
                // Only code-only proxies are supported.
                if (!(_instance is mvcExpress.Proxy))
                {
                    throw new InvalidOperationException(
                        $"[MvcExpress] AsScoped() is only supported for code-only proxies. '{_instance.GetType().FullName}' is not a Proxy.");
                }

                if (_registerToLogic) _container.MarkScoped(_logicType);
                if (_registerToView) _container.MarkScoped(_viewType);
                return;
            }

            _container.RegisterInternal(
                _instance,
                _logicType,
                _viewType,
                _isTransient,
                _registerToLogic,
                _registerToView,
                _registerToLogicList,
                _logicListElementType,
                _registerToViewList,
                _viewListElementType);
        }
    }

    /// <summary>
    /// Non-generic fluent builder for dependency registration.
    /// </summary>
    public sealed class RegistrationBuilder
    {
        private readonly MvcDiContainer _container;
        private readonly object _instance;
        private readonly Type _instanceType;
        private Type _logicType;
        private Type _viewType;
        private bool _registerToLogic;
        private bool _registerToView;
        private bool _isTransient = false;
        private bool _isScoped = false;
        private bool _completed = false;

        private Type _logicListElementType;
        private Type _viewListElementType;
        private bool _registerToLogicList;
        private bool _registerToViewList;

        internal RegistrationBuilder(MvcDiContainer container, object instance, Type type)
        {
            _container = container ?? throw new ArgumentNullException(nameof(container));
            _instance = instance ?? throw new ArgumentNullException(nameof(instance));
            _instanceType = type ?? throw new ArgumentNullException(nameof(type));

            // Default: register as specified type
            _logicType = type;
            _viewType = type;
            _registerToLogic = false;
            _registerToView = false;
        }

        /// <summary>
        /// Register to logic layer as specified type.
        /// </summary>
        public RegistrationBuilder ToLogic()
        {
            ThrowIfCompleted();
            _registerToLogic = true;
            _logicType = _instanceType;
            return this;
        }

        /// <summary>
        /// Register to logic layer as specified type.
        /// </summary>
        public RegistrationBuilder ToLogicAs(Type logicType)
        {
            ThrowIfCompleted();
            if (logicType == null)
                throw new ArgumentNullException(nameof(logicType));
            _registerToLogic = true;
            _logicType = logicType;
            return this;
        }

        /// <summary>
        /// Register to view layer as specified type.
        /// </summary>
        public RegistrationBuilder ToView()
        {
            ThrowIfCompleted();
            _registerToView = true;
            _viewType = _instanceType;
            return this;
        }

        /// <summary>
        /// Register to view layer as specified type.
        /// </summary>
        public RegistrationBuilder ToViewAs(Type viewType)
        {
            ThrowIfCompleted();
            if (viewType == null)
                throw new ArgumentNullException(nameof(viewType));
            _registerToView = true;
            _viewType = viewType;
            return this;
        }

        /// <summary>
        /// Register to logic layer as List of specified type.
        /// </summary>
        public RegistrationBuilder ToLogicList(Type elementType)
        {
            ThrowIfCompleted();
            _registerToLogicList = true;
            _logicListElementType = elementType ?? throw new ArgumentNullException(nameof(elementType));
            return this;
        }

        /// <summary>
        /// Register to view layer as List of specified type.
        /// </summary>
        public RegistrationBuilder ToViewList(Type elementType)
        {
            ThrowIfCompleted();
            _registerToViewList = true;
            _viewListElementType = elementType ?? throw new ArgumentNullException(nameof(elementType));
            return this;
        }

        /// <summary>
        /// Complete registration as persistent dependency.
        /// </summary>
        public void AsPersistent()
        {
            ThrowIfCompleted();
            _isTransient = false;
            Complete();
        }

        /// <summary>
        /// Complete registration as transient dependency.
        /// </summary>
        public void AsTransient()
        {
            ThrowIfCompleted();
            _isTransient = true;
            Complete();
        }

        /// <summary>
        /// Complete registration as scoped dependency.
        /// </summary>
        public void AsScoped()
        {
            ThrowIfCompleted();
            _isScoped = true;
            _isTransient = false;
            Complete();
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

            if (!_registerToLogic && !_registerToView && !_registerToLogicList && !_registerToViewList)
            {
                throw new InvalidOperationException(
                    "Must register to at least one layer. Call .ToLogic(), .ToLogicAs(), .ToView(), .ToViewAs(), .ToLogicList(), or .ToViewList() before completing registration.");
            }

            if (_isScoped)
            {
                if (!(_instance is mvcExpress.Proxy))
                {
                    throw new InvalidOperationException(
                        $"[MvcExpress] AsScoped() is only supported for code-only proxies. '{_instance.GetType().FullName}' is not a Proxy.");
                }

                if (_registerToLogic) _container.MarkScoped(_logicType);
                if (_registerToView) _container.MarkScoped(_viewType);
                return;
            }

            _container.RegisterInternal(
                _instance,
                _logicType,
                _viewType,
                _isTransient,
                _registerToLogic,
                _registerToView,
                _registerToLogicList,
                _logicListElementType,
                _registerToViewList,
                _viewListElementType);
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

        // Scoped dependency tracking (type->concrete implementation type).
        // For now this is restricted to concrete types (code-only proxies).
        private readonly ConcurrentDictionary<Type, Type> _scopedTypes = new ConcurrentDictionary<Type, Type>();

        // List-bindings: elementType -> set of instances that were added to List<elementType>
        private readonly ConcurrentDictionary<Type, ConcurrentDictionary<object, bool>> _logicListMembers = new ConcurrentDictionary<Type, ConcurrentDictionary<object, bool>>();
        private readonly ConcurrentDictionary<Type, ConcurrentDictionary<object, bool>> _viewListMembers = new ConcurrentDictionary<Type, ConcurrentDictionary<object, bool>>();

        private static readonly AsyncLocal<ScopedResolutionContext> _scopedResolutionContext = new AsyncLocal<ScopedResolutionContext>();

        /// <summary>
        /// Holds one scoped instance cache for a single nested resolution operation.
        /// </summary>
        internal sealed class ScopedResolutionContext
        {
            public readonly Dictionary<Type, object> Instances = new Dictionary<Type, object>();
        }

        /// <summary>
        /// Restores the previous scoped resolution context and disposes scoped instances when complete.
        /// </summary>
        internal readonly struct ScopedResolutionToken : IDisposable
        {
            private readonly ScopedResolutionContext _previous;
            private readonly ScopedResolutionContext _current;

            internal ScopedResolutionToken(ScopedResolutionContext previous, ScopedResolutionContext current)
            {
                _previous = previous;
                _current = current;
            }

            public void Dispose()
            {
                if (!ReferenceEquals(_scopedResolutionContext.Value, _current))
                {
                    _scopedResolutionContext.Value = _previous;
                    return;
                }

                DisposeContext(_current);
                _scopedResolutionContext.Value = _previous;
            }

            private static void DisposeContext(ScopedResolutionContext ctx)
            {
                if (ctx == null) return;

                foreach (var kvp in ctx.Instances)
                {
                    if (kvp.Value is IDisposable d)
                    {
                        try { d.Dispose(); }
                        catch { }
                    }
                }

                ctx.Instances.Clear();
            }
        }

        internal ScopedResolutionToken BeginScopedResolutionScope()
        {
            var previous = _scopedResolutionContext.Value;
            var current = new ScopedResolutionContext();
            _scopedResolutionContext.Value = current;
            return new ScopedResolutionToken(previous, current);
        }

        private static ScopedResolutionContext CurrentScopedContext => _scopedResolutionContext.Value;

        private bool _disposed;

#if UNITY_EDITOR
        internal event Action<object> ProxyRegisteredForDebug;
        // Fires for code-only registrations that are not proxies or MonoBehaviours.
        internal event Action<object> ServiceRegisteredForDebug;
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

        /// <summary>
        /// Check if a type is registered as scoped (limited lifetime, auto-removed on dispose).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsScoped(Type type)
        {
            return _scopedTypes.ContainsKey(type);
        }

        internal void MarkScoped(Type type)
        {
            if (type == null)
                throw new ArgumentNullException(nameof(type));

            // If already registered as an instance in either scope, scoped registration is invalid.
            if (_logicObjects.ContainsKey(type) || _logicInterfaces.ContainsKey(type) || _viewObjects.ContainsKey(type) || _viewInterfaces.ContainsKey(type))
            {
                throw new InvalidOperationException(
                    $"[MvcExpress] Cannot register '{type.FullName}' as scoped because it is already registered as an instance. Scoped types cannot be registered as instances.");
            }

            // Single registration policy across logic/view for scoped: a type may only be scoped once.
            if (!_scopedTypes.TryAdd(type, type))
            {
                throw new InvalidOperationException(
                    $"[MvcExpress] Type '{type.FullName}' already registered as scoped. Scoped registrations must be unique.");
            }
        }
        #region Registration (Fluent Builder API)

        /// <summary>
        /// Begin registration using fluent builder.
        /// 
        /// NOTE: Each type can only be registered ONCE per scope (logic/view).
        /// Attempting to register the same type twice will throw an exception.
        /// </summary>
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
        /// Container.Register(instance, serviceType).ToLogic().AsPersistent();
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

        /// <summary>
        /// Internal registration method called by RegistrationBuilder.
        /// </summary>
        internal void RegisterInternal(
            object obj,
            Type logicType,
            Type viewType,
            bool isTransient,
            bool registerToLogic,
            bool registerToView,
            bool registerToLogicList,
            Type logicListElementType,
            bool registerToViewList,
            Type viewListElementType)
        {
            if (obj == null)
                throw new InvalidOperationException("Cannot register null instance.");

            if (!registerToLogic && !registerToView && !registerToLogicList && !registerToViewList)
                throw new InvalidOperationException("Must register to at least one scope (logic, view, logic list, or view list).");

            // Important: list keys are List<TElement>, so they must not be scoped either.
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

            // Existing single registrations
            if (registerToLogic)
            {
                var isInterface = _isInterfaceCache.GetOrAdd(logicType, t => t.IsInterface);
                var targetDict = isInterface ? _logicInterfaces : _logicObjects;

                if (!targetDict.TryAdd(logicType, obj))
                {
                    throw new InvalidOperationException(
                        $"Type '{logicType.Name}' already registered in logic container. " +
                        $"Each type can only be registered once per scope. " +
                        $"Use distinct types or create a composite class for multiple implementations.");
                }

                // Track lifecycle (transient vs persistent) for invalidation on unregister
                _transientTypes[logicType] = isTransient;
            }

            // Register to view container if requested
            if (registerToView)
            {
                var isInterface = _isInterfaceCache.GetOrAdd(viewType, t => t.IsInterface);
                var targetDict = isInterface ? _viewInterfaces : _viewObjects;

                if (!targetDict.TryAdd(viewType, obj))
                {
                    // Rollback logic registration if view registration fails
                    if (registerToLogic)
                    {
                        var logicIsInterface = _isInterfaceCache.GetOrAdd(logicType, t => t.IsInterface);
                        var logicDict = logicIsInterface ? _logicInterfaces : _logicObjects;
                        logicDict.TryRemove(logicType, out _);
                        _transientTypes.TryRemove(logicType, out _);
                    }
                    throw new InvalidOperationException(
                        $"Type '{viewType.Name}' already registered in view container. " +
                        $"Each type can only be registered once per scope. " +
                        $"Use distinct types or create a composite class for multiple implementations.");
                }

                // Track lifecycle for view registration too
                _transientTypes[viewType] = isTransient;
            }

            // List registrations (multi bind)
            if (registerToLogicList)
            {
                AddToListBinding(obj, logicListElementType, useView: false, isTransient: isTransient);
            }

            if (registerToViewList)
            {
                AddToListBinding(obj, viewListElementType, useView: true, isTransient: isTransient);
            }

            // Reject scoped registration for ProxyBehaviour (only raw proxies can be scoped).
            // (kept for backwards compatibility / safety)
            if (_scopedTypes.TryGetValue(logicType, out _) && obj is mvcExpress.ProxyBehaviour)
            {
                throw new InvalidOperationException($"[MvcExpress] Scoped registration is only supported for code-only proxies. '{logicType.FullName}' is a ProxyBehaviour.");
            }
            if (registerToView && _scopedTypes.TryGetValue(viewType, out _) && obj is mvcExpress.ProxyBehaviour)
            {
                throw new InvalidOperationException($"[MvcExpress] Scoped registration is only supported for code-only proxies. '{viewType.FullName}' is a ProxyBehaviour.");
            }

#if UNITY_EDITOR
            if (obj is mvcExpress.Proxy || obj is mvcExpress.ProxyBehaviour)
            {
                ProxyRegisteredForDebug?.Invoke(obj);
            }
            else if (!(obj is UnityEngine.MonoBehaviour))
            {
                // Code-only service (not a proxy, not a scene component).
                ServiceRegisteredForDebug?.Invoke(obj);
            }
#endif

        }

        private void AddToListBinding(object obj, Type elementType, bool useView, bool isTransient)
        {
            if (elementType == null)
                throw new ArgumentNullException(nameof(elementType));

            var listType = typeof(List<>).MakeGenericType(elementType);

            // Prevent mixing list-binding with an already-registered non-list instance for the same List<T>
            if (_logicObjects.ContainsKey(listType) || _logicInterfaces.ContainsKey(listType) || _viewObjects.ContainsKey(listType) || _viewInterfaces.ContainsKey(listType))
            {
                // ok: list already exists
            }

            // Create or get list instance from the correct container
            if (useView)
            {
                if (!_viewObjects.TryGetValue(listType, out var existing))
                {
                    existing = Activator.CreateInstance(listType);
                    if (!_viewObjects.TryAdd(listType, existing))
                    {
                        _viewObjects.TryGetValue(listType, out existing);
                    }
                }

                ((System.Collections.IList)existing).Add(obj);
                _transientTypes[listType] = isTransient;

                var members = _viewListMembers.GetOrAdd(elementType, _ => new ConcurrentDictionary<object, bool>());
                members[obj] = true;
            }
            else
            {
                if (!_logicObjects.TryGetValue(listType, out var existing))
                {
                    existing = Activator.CreateInstance(listType);
                    if (!_logicObjects.TryAdd(listType, existing))
                    {
                        _logicObjects.TryGetValue(listType, out existing);
                    }
                }

                ((System.Collections.IList)existing).Add(obj);
                _transientTypes[listType] = isTransient;

                var members = _logicListMembers.GetOrAdd(elementType, _ => new ConcurrentDictionary<object, bool>());
                members[obj] = true;
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

        private void RemoveFromListBindings(object instance)
        {
            if (instance == null) return;

            foreach (var kvp in _logicListMembers)
            {
                if (kvp.Value.TryRemove(instance, out _))
                {
                    var listType = typeof(List<>).MakeGenericType(kvp.Key);
                    if (_logicObjects.TryGetValue(listType, out var listObj))
                    {
                        var list = (System.Collections.IList)listObj;
                        list.Remove(instance);
                        if (list.Count == 0)
                        {
                            _logicObjects.TryRemove(listType, out _);
                            _transientTypes.TryRemove(listType, out _);
                        }
                    }
                }
            }

            foreach (var kvp in _viewListMembers)
            {
                if (kvp.Value.TryRemove(instance, out _))
                {
                    var listType = typeof(List<>).MakeGenericType(kvp.Key);
                    if (_viewObjects.TryGetValue(listType, out var listObj))
                    {
                        var list = (System.Collections.IList)listObj;
                        list.Remove(instance);
                        if (list.Count == 0)
                        {
                            _viewObjects.TryRemove(listType, out _);
                            _transientTypes.TryRemove(listType, out _);
                        }
                    }
                }
            }
        }

        private void UnregisterInternal(Type type)
        {
            if (type == null)
            {
                throw new ArgumentNullException(nameof(type));
            }

            // Check if type is registered as persistent (cannot be destroyed)
            if (_transientTypes.TryGetValue(type, out var isTransient) && !isTransient)
            {
                throw new InvalidOperationException($"[MvcExpress] Cannot unregister persistent type '{type.FullName}'. Persistent dependencies cannot be destroyed. Use .AsTransient() during registration if this dependency has a dynamic lifecycle.");
            }

            // Remove from all containers and dispose if IDisposable
            bool removed = false;
            object instance = null;

            if (_logicObjects.TryRemove(type, out var logicObj))
            {
                instance = logicObj;
                removed = true;
            }
            if (_logicInterfaces.TryRemove(type, out var logicInterface))
            {
                instance = instance ?? logicInterface;
                removed = true;
            }
            if (_viewObjects.TryRemove(type, out var viewObj))
            {
                instance = instance ?? viewObj;
                removed = true;
            }
            if (_viewInterfaces.TryRemove(type, out var viewInterface))
            {
                instance = instance ?? viewInterface;
                removed = true;
            }

            // If this is a proxy, remove its visualization from Model hierarchy.
            if (instance != null)
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
#if UNITY_EDITOR
                else
                {
                    // Code-only proxies have an editor-only wrapper GO under Model (ProxyHierarchyVisualizer)
                    var typeName = instance.GetType().Name;
#if UNITY_6000_5_OR_NEWER
                    var modules = UnityEngine.Object.FindObjectsByType<mvcExpress.MvcModule>(FindObjectsInactive.Include);
#elif UNITY_2022_2_OR_NEWER
#pragma warning disable CS0618 // FindObjectsSortMode: deprecated in 6000.5, required before it
                    var modules = UnityEngine.Object.FindObjectsByType<mvcExpress.MvcModule>(FindObjectsInactive.Include, FindObjectsSortMode.None);
#pragma warning restore CS0618
#else
                    var modules = UnityEngine.Object.FindObjectsOfType<mvcExpress.MvcModule>(true);
#endif
                    for (int i = 0; i < modules.Length; i++)
                    {
                        var m = modules[i];
                        if (m == null) continue;

                        var model = m.transform != null ? m.transform.Find("Model") : null;
                        if (model == null) continue;

                        var wrapper = model.Find(typeName);
                        if (wrapper == null) continue;

                        // Verify this wrapper belongs to the same proxy instance
                        var dbg = wrapper.GetComponent("ProxyDebugBehaviour");
                        if (dbg != null)
                        {
                            // If this wrapper still exists, remove it.
                            UnityEngine.Object.Destroy(wrapper.gameObject);
                        }
                    }
                }
#endif
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
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    MvcDebug.LogError($"Error disposing instance of type '{type.FullName}': {ex.Message}");
#endif
                }
            }

            if (removed && isTransient)
            {
                // MEMORY LEAK FIX: Trigger command invalidation using weak event manager
                _transientDependencyRemoved.Raise(type);
            }

            _transientTypes.TryRemove(type, out _);
            _scopedTypes.TryRemove(type, out _);

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
            var type = typeof(T);

            // 1) Try the active scope container first
            if (TryResolveInternal(type, IsViewScope, out var value))
            {
                return (T)value;
            }

            // 2) Fallback: if not found in containers, try scoped
            if (TryResolveScoped(type, out var scoped))
            {
                return (T)scoped;
            }

            // ERROR PATH: Build context string only when error occurs
            var scope = IsViewScope ? "view" : "logic";
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
                $"   Register(new {type.Name}()).To{(IsViewScope ? "View" : "Logic")}().AsPersistent();\n" +
                $"2. If this is a view-layer dependency, ensure it's registered with .ToView() or .ToViewAs<T>()\n" +
                $"3. If this is a logic-layer dependency in a mediator, you may need to register it to both scopes:\n" +
                $"   Register(instance).ToLogic().ToView().AsPersistent();");
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
                        if (moduleContainer != null && moduleContainer.TryResolveInternal(type, false, out _))
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
                    if (globalContainer.TryResolveInternal(type, false, out _))
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

                            if (moduleContainer.TryResolveInternal(type, false, out _))
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
            var type = typeof(T);

            if (TryResolveInternal(type, IsViewScope, out var resolved))
            {
                value = (T)resolved;
                return true;
            }

            if (TryResolveScoped(type, out var scoped))
            {
                value = (T)scoped;
                return true;
            }

            value = default;
            return false;
        }

        internal bool TryResolveInternal(Type type, bool useViewScope, out object value)
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

            // Fallback to scoped when using reflection-based injection.
            if (TryResolveScoped(type, out value))
            {
                return true;
            }

            value = null;
            return false;
        }

        private bool TryResolveScoped(Type type, out object value)
        {
            if (!_scopedTypes.ContainsKey(type))
            {
                value = null;
                return false;
            }

            var ctx = CurrentScopedContext;
            if (ctx == null)
            {
                throw new InvalidOperationException(
                    $"[MvcExpress] Type '{type.FullName}' is registered as scoped but no scoped resolution scope is active. " +
                    "Wrap command execution/injection in a scoped scope.");
            }

            if (ctx.Instances.TryGetValue(type, out value))
            {
                return true;
            }

            value = Activator.CreateInstance(type);
            ctx.Instances[type] = value;
            return true;
        }

        internal object Resolve(Type type, bool useViewScope)
        {
            if (TryResolveInternal(type ?? throw new ArgumentNullException(nameof(type)), useViewScope, out var value))
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

        private void DisposeRegistrations(ConcurrentDictionary<Type, object> dict)
        {
            foreach (var kvp in dict)
            {
                if (kvp.Value is IDisposable disposable)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception ex)
                    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                        MvcDebug.LogError($"Error disposing {kvp.Key.Name}: {ex.Message}");
#endif
                    }
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
            // Dispose all registered instances that implement IDisposable
            DisposeRegistrations(_logicObjects);
            DisposeRegistrations(_logicInterfaces);
            DisposeRegistrations(_viewObjects);
            DisposeRegistrations(_viewInterfaces);

            // Clear all dictionaries
            _logicObjects.Clear();
            _logicInterfaces.Clear();
            _viewObjects.Clear();
            _viewInterfaces.Clear();
            _transientTypes.Clear();
            _scopedTypes.Clear();

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
        /// Lifetime mode used by diagnostic registration snapshots.
        /// </summary>
        internal enum RegistrationLifecycle
        {
            Persistent,
            Transient,
            Scoped
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

            // Add scoped types that do not have instance entries.
            foreach (var kvp in _scopedTypes)
            {
                var t = kvp.Key;
                if (t == null) continue;

                // A scoped type can be registered to logic/view independently.
                // If it has an instance entry already (shouldn't happen), skip.
                if (_logicObjects.ContainsKey(t) || _logicInterfaces.ContainsKey(t) || _viewObjects.ContainsKey(t) || _viewInterfaces.ContainsKey(t))
                    continue;

                results.Add(new RegistrationSnapshot(t, instance: null, scope: RegistrationScope.Logic, lifecycle: RegistrationLifecycle.Scoped, isListBinding: false));
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
                var lifecycle = isScoped ? RegistrationLifecycle.Scoped : (IsTransient(t) ? RegistrationLifecycle.Transient : RegistrationLifecycle.Persistent);
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

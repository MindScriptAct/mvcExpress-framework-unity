using System;

namespace mvcExpress.Internal.Initialization
{
    /// <summary>
    /// Immutable snapshot of a <c>[Register]</c> attribute applied to a <see cref="Proxy"/> or
    /// <see cref="ProxyBehaviour"/> type, captured once during assembly scanning.
    /// </summary>
    /// <remarks>
    /// Validation is performed eagerly in the constructor so scan-time errors surface before
    /// any module tries to use the metadata. This avoids cryptic failures during module init.
    /// All properties are read-only; instances are shared across module initializations.
    /// Internal - consumed only by <see cref="ModuleInitializer"/>.
    /// </remarks>
    internal sealed class ProxyRegistrationMetadata
    {
        public Type ProxyType { get; }
        public Type TargetModuleType { get; }
        public bool RegisterToLogic { get; }
        public bool RegisterToView { get; }
        public Type LogicType { get; }
        public Type ViewType { get; }
        public RegistrationLifecycle Lifecycle { get; }

        /// <summary>
        /// Create metadata from type and attribute.
        /// Validates configuration during construction (fail-fast during scan).
        /// </summary>
        public ProxyRegistrationMetadata(Type proxyType, RegisterAttribute attribute)
        {
            if (proxyType == null)
                throw new ArgumentNullException(nameof(proxyType));
            if (attribute == null)
                throw new ArgumentNullException(nameof(attribute));

            ProxyType = proxyType;
            TargetModuleType = attribute.TargetModuleType;
            RegisterToLogic = attribute.RegisterToLogic;
            RegisterToView = attribute.RegisterToView;
            LogicType = attribute.LogicInterface ?? proxyType;
            ViewType = attribute.ViewInterface ?? proxyType;
            Lifecycle = attribute.Lifecycle;

            // Validate NOW (during scan) instead of later (during module init)
            // This provides fail-fast behavior with clear error messages
            attribute.Validate(proxyType);
        }
    }

    /// <summary>
    /// Immutable snapshot of a <c>[Register]</c> attribute applied to a service class (plain C# or
    /// MonoBehaviour, but not a Proxy/ProxyBehaviour), captured once during assembly scanning.
    /// </summary>
    /// <remarks>
    /// Validation is performed eagerly in the constructor (fail-fast during scan).
    /// Internal - consumed only by <see cref="ModuleInitializer"/>.
    /// </remarks>
    internal sealed class ServiceRegistrationMetadata
    {
        public Type ServiceType { get; }
        public Type TargetModuleType { get; }
        public bool RegisterToLogic { get; }
        public bool RegisterToView { get; }
        public Type LogicType { get; }
        public Type ViewType { get; }
        public RegistrationLifecycle Lifecycle { get; }

        /// <summary>
        /// Create metadata from type and attribute.
        /// Validates configuration during construction.
        /// </summary>
        public ServiceRegistrationMetadata(Type serviceType, RegisterAttribute attribute)
        {
            if (serviceType == null)
                throw new ArgumentNullException(nameof(serviceType));
            if (attribute == null)
                throw new ArgumentNullException(nameof(attribute));

            ServiceType = serviceType;
            TargetModuleType = attribute.TargetModuleType;
            RegisterToLogic = attribute.RegisterToLogic;
            RegisterToView = attribute.RegisterToView;
            LogicType = attribute.LogicInterface ?? serviceType;
            ViewType = attribute.ViewInterface ?? serviceType;
            Lifecycle = attribute.Lifecycle;

            // Validate configuration
            attribute.Validate(serviceType);
        }
    }

    /// <summary>
    /// Immutable snapshot of a <c>[Bind]</c> attribute applied to a command class, captured once
    /// during assembly scanning.
    /// </summary>
    /// <remarks>
    /// Unlike service/proxy/mediator metadata, command bindings always require a target module type;
    /// there is no universal (no-target) variant.
    /// Validation is performed eagerly in the constructor (fail-fast during scan).
    /// Internal - consumed only by <see cref="ModuleInitializer"/>.
    /// </remarks>
    internal sealed class CommandBindingMetadata
    {
        public Type CommandType { get; }
        public Type MessageType { get; }
        public Type TargetModuleType { get; }
        public bool IsAsync { get; }
        public uint PoolSize { get; }

        /// <summary>
        /// Create metadata from type and attribute.
        /// Validates configuration during construction (fail-fast during scan).
        /// </summary>
        public CommandBindingMetadata(Type commandType, BindAttribute attribute)
        {
            if (commandType == null)
                throw new ArgumentNullException(nameof(commandType));
            if (attribute == null)
                throw new ArgumentNullException(nameof(attribute));

            CommandType = commandType;
            MessageType = attribute.MessageType;
            TargetModuleType = attribute.TargetModuleType;
            IsAsync = attribute.IsAsync;
            PoolSize = attribute.PoolSize;

            // Validate NOW (during scan) instead of later (during module init)
            attribute.Validate(commandType);
        }

        /// <summary>
        /// Construct metadata directly from explicit parameters.
        /// Used in legacy or test scenarios where no attribute object is available.
        /// </summary>
        public CommandBindingMetadata(Type commandType, Type messageType, Type targetModuleType, uint poolSize, bool isAsync)
        {
            CommandType = commandType ?? throw new ArgumentNullException(nameof(commandType));
            MessageType = messageType ?? throw new ArgumentNullException(nameof(messageType));
            TargetModuleType = targetModuleType;
            PoolSize = poolSize;
            IsAsync = isAsync;
        }
    }

    /// <summary>
    /// Immutable snapshot of an <c>[Attach]</c> or <c>[AttachPrefab]</c> attribute applied to a
    /// <see cref="MediatorBehaviour"/> class, captured once during assembly scanning.
    /// </summary>
    /// <remarks>
    /// Three mutually-exclusive strategies determine how the mediator instance is located at init time:
    /// <list type="number">
    ///   <item><description><see cref="UsePrefabCatalog"/> - instantiated from the module's ViewPrefabCatalog.</description></item>
    ///   <item><description><see cref="IsPrefabBased"/> (PrefabPath set) - instantiated from a Resources path.</description></item>
    ///   <item><description><see cref="FindInScene"/> - found in the active scene via FindObjectOfType.</description></item>
    /// </list>
    /// Validation is performed eagerly in the constructor (fail-fast during scan).
    /// Internal - consumed only by <see cref="ModuleInitializer"/>.
    /// </remarks>
    internal sealed class MediatorAttachmentMetadata
    {
        public Type MediatorType { get; }
        public Type TargetModuleType { get; }
        public string PrefabPath { get; }
        public bool FindInScene { get; }
        public bool UsePrefabCatalog { get; }

        /// <summary>
        /// True if this mediator should be instantiated from a prefab.
        /// </summary>
        public bool IsPrefabBased => !string.IsNullOrEmpty(PrefabPath) || UsePrefabCatalog;

        /// <summary>
        /// Create metadata from type and attribute.
        /// Validates configuration during construction (fail-fast during scan).
        /// </summary>
        public MediatorAttachmentMetadata(Type mediatorType, AttachAttribute attribute)
        {
            if (mediatorType == null)
                throw new ArgumentNullException(nameof(mediatorType));
            if (attribute == null)
                throw new ArgumentNullException(nameof(attribute));

            MediatorType = mediatorType;
            TargetModuleType = attribute.TargetModuleType;
            PrefabPath = attribute.PrefabPath;
            FindInScene = attribute.FindInScene;
            UsePrefabCatalog = false;

            // Validate NOW (during scan) instead of later (during module init)
            attribute.Validate(mediatorType);
        }

        /// <summary>
        /// Construct metadata from a type and an <c>[AttachPrefab]</c> attribute.
        /// Sets <see cref="UsePrefabCatalog"/> to true; PrefabPath and FindInScene remain empty/false.
        /// </summary>
        public MediatorAttachmentMetadata(Type mediatorType, AttachPrefabAttribute attribute)
        {
            if (mediatorType == null)
                throw new ArgumentNullException(nameof(mediatorType));
            if (attribute == null)
                throw new ArgumentNullException(nameof(attribute));

            MediatorType = mediatorType;
            TargetModuleType = attribute.TargetModuleType;
            PrefabPath = null;
            FindInScene = false;
            UsePrefabCatalog = true;

            attribute.Validate(mediatorType);
        }
    }

    /// <summary>
    /// Immutable snapshot of a <c>[RegisterGlobal]</c> attribute applied to a plain C# class or a
    /// MonoBehaviour-derived <see cref="ProxyBehaviour"/>/service, captured once during assembly scanning.
    /// </summary>
    /// <remarks>
    /// Plain C# types are instantiated via <c>Activator.CreateInstance</c>; MonoBehaviour types are
    /// resolved to a scene instance (found or auto-created) by <see cref="MvcFacade"/> at drain time -
    /// see <see cref="IsMonoBehaviour"/>.
    /// Validation is performed eagerly in the constructor (fail-fast during scan).
    /// Internal - consumed only by <see cref="MvcFacade"/>.
    /// </remarks>
    internal sealed class GlobalRegistrationMetadata
    {
        /// <summary>Concrete type to instantiate/resolve and register.</summary>
        public Type ConcreteType { get; }

        /// <summary>Whether to register into the logic scope.</summary>
        public bool RegisterToLogic { get; }

        /// <summary>Whether to register into the view scope.</summary>
        public bool RegisterToView { get; }

        /// <summary>
        /// The type key used for logic-scope registration.
        /// Equals <see cref="ConcreteType"/> when no LogicInterface was specified.
        /// </summary>
        public Type LogicType { get; }

        /// <summary>
        /// The type key used for view-scope registration.
        /// Equals <see cref="ConcreteType"/> when no ViewInterface was specified.
        /// </summary>
        public Type ViewType { get; }

        /// <summary>True when the concrete type inherits from <see cref="Proxy"/> or <see cref="ProxyBehaviour"/>.</summary>
        public bool IsProxy { get; }

        /// <summary>True when the concrete type inherits from <see cref="UnityEngine.MonoBehaviour"/> (resolved to a scene instance rather than instantiated via <c>Activator.CreateInstance</c>).</summary>
        public bool IsMonoBehaviour { get; }

        /// <summary>
        /// Lifecycle of this dependency.
        /// Permanent: Lives for the application lifetime (default, recommended for most cases).
        /// Transient: Can be destroyed/unregistered dynamically.
        /// Scoped: Fresh instance per resolution scope.
        /// </summary>
        public RegistrationLifecycle Lifecycle { get; }

        /// <summary>
        /// Create metadata from type and attribute.
        /// Validates configuration during construction (fail-fast during scan).
        /// </summary>
        public GlobalRegistrationMetadata(Type concreteType, RegisterGlobalAttribute attr)
        {
            if (concreteType == null)
                throw new ArgumentNullException(nameof(concreteType));
            if (attr == null)
                throw new ArgumentNullException(nameof(attr));

            ConcreteType = concreteType;
            RegisterToLogic = attr.RegisterToLogic;
            RegisterToView = attr.RegisterToView;
            LogicType = attr.LogicInterface ?? concreteType;
            ViewType = attr.ViewInterface ?? concreteType;
            IsProxy = typeof(mvcExpress.Proxy).IsAssignableFrom(concreteType) || typeof(ProxyBehaviour).IsAssignableFrom(concreteType);
            IsMonoBehaviour = typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(concreteType);
            Lifecycle = attr.Lifecycle;

            attr.Validate(concreteType);
        }
    }

    /// <summary>
    /// Immutable snapshot of a <c>[StartupModule]</c> attribute applied to an
    /// <see cref="MvcModule"/> subclass, captured once during assembly scanning.
    /// </summary>
    /// <remarks>
    /// Validation is performed eagerly in the constructor (fail-fast during scan).
    /// Internal - consumed only by <see cref="MvcFacade"/>.
    /// </remarks>
    internal sealed class StartupModuleMetadata
    {
        /// <summary>Concrete module type to create at startup.</summary>
        public Type ModuleType { get; }

        /// <summary>
        /// Startup order relative to other <c>[StartupModule]</c> entries.
        /// Lower values start first.
        /// </summary>
        public int Order { get; }

        /// <summary>
        /// Create metadata from type and attribute.
        /// Validates configuration during construction (fail-fast during scan).
        /// </summary>
        public StartupModuleMetadata(Type moduleType, StartupModuleAttribute attr)
        {
            if (moduleType == null)
                throw new ArgumentNullException(nameof(moduleType));
            if (attr == null)
                throw new ArgumentNullException(nameof(attr));

            attr.Validate(moduleType);

            ModuleType = moduleType;
            Order = attr.Order;
        }
    }

}

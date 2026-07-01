using System;

namespace mvcExpress
{
    /// <summary>
    /// Marks a Service, <see cref="Proxy"/>, or <see cref="ProxyBehaviour"/> class for automatic
    /// registration into a module's DI container during initialization.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use <c>[Register]</c> when you want the type itself to declare which module and container
    /// scopes it belongs to, rather than wiring it in <see cref="MvcModule.RegisterServices"/> or
    /// <see cref="MvcModule.RegisterProxies"/>. This keeps registration co-located with the type
    /// definition and reduces boilerplate in module subclasses.
    /// </para>
    /// <para>
    /// Attribute registration runs after Unity registry registration (Inspector) and before module
    /// code registration. Assembly scanning happens once at <see cref="MvcFacade"/> initialization
    /// and results are cached, so attribute registration has negligible runtime cost.
    /// </para>
    /// <para><b>Registration Order (Three-Phase Pipeline):</b></para>
    /// <para>Each module component type follows this initialization sequence:</para>
    /// <list type="number">
    /// <item><description><b>Unity Registry</b> - Components registered via Unity Inspector (ProxyRegistry/ServiceRegistry)</description></item>
    /// <item><description><b>Attribute Registration</b> - Components marked with [Register] attribute (this attribute)</description></item>
    /// <item><description><b>Code Registration</b> - Manual registration in RegisterServices/RegisterProxies methods</description></item>
    /// </list>
    ///
    /// <para><b>Supported Types:</b></para>
    /// <list type="bullet">
    /// <item><description><b>Proxy</b> - Code-only proxy (non-MonoBehaviour)</description></item>
    /// <item><description><b>ProxyBehaviour</b> - MonoBehaviour-based proxy</description></item>
    /// <item><description><b>Service</b> - plain class or MonoBehaviour that is not a ProxyBehaviour</description></item>
    /// </list>
    /// 
    /// <para>For multi-module projects, explicitly specify <see cref="TargetModuleType"/> to avoid
    /// registering a dependency into modules that do not own it.
    /// </para>
    /// </remarks>
    /// <example>
    /// <b>Basic Proxy Registration (default = current module):</b>
    /// <code>
    /// [Register]
    /// public class GameStateProxy : Proxy 
    /// {
    /// }
    /// </code>
    /// 
    /// <b>Register to Logic and View layers:</b>
    /// <code>
    /// [Register(RegisterToLogic = true, RegisterToView = true)]
    /// public class PlayerDataProxy : Proxy 
    /// {
    /// }
    /// </code>
    /// 
    /// <b>Register to specific module:</b>
    /// <code>
    /// [Register(typeof(GameplayModule), RegisterToLogic = true)]
    /// public class GameStateProxy : Proxy 
    /// {
    /// }
    /// </code>
    /// 
    /// <b>Service (MonoBehaviour) registration:</b>
    /// <code>
    /// [Register(typeof(UIModule), RegisterToLogic = true)]
    /// public class InputService : MonoBehaviour 
    /// {
    /// }
    /// </code>
    /// 
    /// <b>Register to multiple modules (use multiple attributes):</b>
    /// <code>
    /// [Register(typeof(GameplayModule), RegisterToLogic = true)]
    /// [Register(typeof(UIModule), RegisterToLogic = true)]
    /// public class SharedConfigProxy : Proxy 
    /// {
    /// }
    /// </code>
    /// 
    /// <b>Interface-based registration (Logic layer):</b>
    /// <code>
    /// public interface IDataService
    /// {
    ///     void Save(string key, string value);
    /// }
    /// 
    /// [Register(typeof(GameplayModule), RegisterToLogic = true, LogicInterface = typeof(IDataService))]
    /// public class DataServiceProxy : Proxy, IDataService 
    /// {
    ///     public void Save(string key, string value) { }
    /// }
    /// </code>
    /// 
    /// <b>Interface-based registration (View layer - read-only interface):</b>
    /// <code>
    /// public interface IPlayerDataReadOnly
    /// {
    ///     int Score { get; }
    /// }
    /// 
    /// public interface IPlayerData : IPlayerDataReadOnly
    /// {
    ///     new int Score { get; set; }
    /// }
    /// 
    /// [Register(RegisterToLogic = true, RegisterToView = true, 
    ///           LogicInterface = typeof(IPlayerData), 
    ///           ViewInterface = typeof(IPlayerDataReadOnly))]
    /// public class PlayerDataProxy : Proxy, IPlayerData 
    /// {
    ///     public int Score { get; set; }
    /// }
    /// </code>
    /// 
    /// <b>Transient lifecycle (can be destroyed/unregistered):</b>
    /// <code>
    /// [Register(RegisterToLogic = true, Lifecycle = RegistrationLifecycle.Transient)]
    /// public class TemporaryProxy : Proxy 
    /// {
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class RegisterAttribute : Attribute
    {
        /// <summary>
        /// Module type that should receive this registration.
        /// </summary>
        /// <remarks>
        /// Null means the dependency is eligible for the module currently being initialized.
        /// For multi-module projects, specify a concrete module type.
        /// </remarks>
        public Type TargetModuleType { get; }

        /// <summary>
        /// Register this object to the logic layer (accessible to Commands and other Proxies).
        /// Default: true
        /// </summary>
        public bool RegisterToLogic { get; set; } = true;

        /// <summary>
        /// Register this object to the view layer (accessible to Mediators).
        /// Default: false
        /// </summary>
        public bool RegisterToView { get; set; } = false;

        /// <summary>
        /// Interface or base type to register in the logic layer.
        /// If null, registers as the concrete type (self).
        /// The type must implement this interface.
        /// Default: null (concrete type)
        /// </summary>
        public Type LogicInterface { get; set; }

        /// <summary>
        /// Interface or base type to register in the view layer.
        /// If null, registers as the concrete type (self).
        /// The type must implement this interface.
        /// Default: null (concrete type)
        /// </summary>
        public Type ViewInterface { get; set; }

        /// <summary>
        /// Lifecycle of this dependency.
        /// Persistent: Lives for the entire module lifetime (default, recommended for most cases).
        /// Transient: Can be destroyed/unregistered dynamically (use when lifecycle is tied to runtime state).
        /// Default: Persistent
        /// </summary>
        public RegistrationLifecycle Lifecycle { get; set; } = RegistrationLifecycle.Persistent;

        /// <summary>
        /// Creates a registration attribute for automatic module registration.
        /// </summary>
        /// <param name="targetModuleType">The module type to register to. Use typeof(YourModuleClass). If null, registers to current module.</param>
        public RegisterAttribute(Type targetModuleType = null)
        {
            TargetModuleType = targetModuleType;
        }

        /// <summary>
        /// Validates the attribute configuration.
        /// Called during assembly scanning to catch configuration errors early.
        /// </summary>
        /// <param name="targetType">The type this attribute is applied to.</param>
        /// <exception cref="ArgumentNullException">Thrown when targetType is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when attribute configuration is invalid.</exception>
        internal void Validate(Type targetType)
        {
            if (targetType == null)
                throw new ArgumentNullException(nameof(targetType));

            // Must register to at least one scope
            if (!RegisterToLogic && !RegisterToView)
            {
                throw new InvalidOperationException(
                    $"[Register] Type '{targetType.FullName}' must register to at least one scope. " +
                    $"Set RegisterToLogic = true or RegisterToView = true.");
            }

            // Validate logic interface
            if (LogicInterface != null && !LogicInterface.IsAssignableFrom(targetType))
            {
                throw new InvalidOperationException(
                    $"[Register] Type '{targetType.FullName}' does not implement LogicInterface '{LogicInterface.FullName}'.");
            }

            // Validate view interface
            if (ViewInterface != null && !ViewInterface.IsAssignableFrom(targetType))
            {
                throw new InvalidOperationException(
                    $"[Register] Type '{targetType.FullName}' does not implement ViewInterface '{ViewInterface.FullName}'.");
            }

            // Validate module type (if specified)
            if (TargetModuleType != null && !typeof(MvcModule).IsAssignableFrom(TargetModuleType))
            {
                throw new InvalidOperationException(
                    $"[Register] Target module type '{TargetModuleType.FullName}' must inherit from MvcModule. " +
                    $"Type: '{targetType.FullName}'");
            }
        }
    }

    /// <summary>
    /// Defines the lifecycle of a registered dependency.
    /// </summary>
    public enum RegistrationLifecycle
    {
        /// <summary>
        /// Persistent dependencies live for the entire module lifetime.
        /// They cannot be unregistered or destroyed.
        /// This is the default and recommended for most use cases.
        /// </summary>
        Persistent = 0,

        /// <summary>
        /// Transient dependencies can be destroyed/unregistered dynamically.
        /// Use when the dependency has a dynamic lifecycle tied to runtime state
        /// (e.g., temporary game objects, session-based data).
        /// </summary>
        Transient = 1
    }
}

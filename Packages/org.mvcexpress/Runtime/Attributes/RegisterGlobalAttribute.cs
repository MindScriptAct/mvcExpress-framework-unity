using System;

namespace mvcExpress
{
    /// <summary>
    /// Marks a plain C# service class, <see cref="Proxy"/>, or <see cref="ProxyBehaviour"/> class
    /// for automatic registration into the application-wide (<see cref="MvcFacade"/>) DI container,
    /// accessible to every module.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the Attribute-style equivalent of the <c>GlobalServiceRegistryBehaviour</c> /
    /// <c>GlobalProxyRegistryBehaviour</c> Inspector components. Use it when you want a global
    /// dependency to declare its own scope rather than wiring it in the scene hierarchy.
    /// </para>
    /// <para>
    /// Only plain C# types (non-MonoBehaviour) are supported. MonoBehaviour services and
    /// <see cref="ProxyBehaviour"/> types require a Unity GameObject context that cannot be created
    /// during assembly scanning; attempting to use <c>[RegisterGlobal]</c> on a MonoBehaviour logs
    /// an error and skips that type.
    /// </para>
    /// <para>
    /// Global registrations are always persistent (application lifetime). There is no
    /// <c>Lifecycle</c> option because global dependencies must survive across scene loads.
    /// </para>
    /// <para>
    /// Assembly scanning happens once at <see cref="MvcFacade"/> initialization and results are
    /// cached, so attribute registration has negligible runtime cost.
    /// </para>
    /// </remarks>
    /// <example>
    /// <b>Global plain C# Proxy:</b>
    /// <code>
    /// [RegisterGlobal]
    /// public class AnalyticsProxy : Proxy
    /// {
    /// }
    /// </code>
    ///
    /// <b>Global service with logic interface:</b>
    /// <code>
    /// [RegisterGlobal(RegisterToLogic = true, LogicInterface = typeof(ISettingsService))]
    /// public class SettingsService : ISettingsService
    /// {
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class RegisterGlobalAttribute : Attribute
    {
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

            // Must register to at least one scope.
            if (!RegisterToLogic && !RegisterToView)
            {
                throw new InvalidOperationException(
                    $"[RegisterGlobal] Type '{targetType.FullName}' must register to at least one scope. " +
                    $"Set RegisterToLogic = true or RegisterToView = true.");
            }

            // Validate logic interface.
            if (LogicInterface != null && !LogicInterface.IsAssignableFrom(targetType))
            {
                throw new InvalidOperationException(
                    $"[RegisterGlobal] Type '{targetType.FullName}' does not implement LogicInterface '{LogicInterface.FullName}'.");
            }

            // Validate view interface.
            if (ViewInterface != null && !ViewInterface.IsAssignableFrom(targetType))
            {
                throw new InvalidOperationException(
                    $"[RegisterGlobal] Type '{targetType.FullName}' does not implement ViewInterface '{ViewInterface.FullName}'.");
            }
        }
    }
}

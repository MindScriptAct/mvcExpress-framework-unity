using System;

namespace mvcExpress
{
    /// <summary>
    /// Marks a <see cref="MvcModule"/> subclass for automatic creation and startup when
    /// <see cref="MvcFacade"/> initializes.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the Attribute-style equivalent of the <c>MvcFacade._startupModules</c> serialized
    /// Inspector array. Use it when you want a module to self-declare that it should launch at
    /// startup, rather than wiring it in the Inspector.
    /// </para>
    /// <para>
    /// When both Inspector entries and <c>[StartupModule]</c> attributes exist for the same module
    /// type, the Inspector entry takes precedence and the attribute entry is skipped. This allows
    /// Inspector config (which may supply a prefab or view container) to override the code-only
    /// attribute path without duplication.
    /// </para>
    /// <para>
    /// Module instances are created as plain <c>AddComponent</c> GameObjects (code style). If you
    /// need a prefab, use the Inspector entry instead.
    /// </para>
    /// </remarks>
    /// <example>
    /// <b>Default order (0):</b>
    /// <code>
    /// [StartupModule]
    /// public class GameplayModule : MvcModule
    /// {
    /// }
    /// </code>
    ///
    /// <b>Explicit order - starts before other modules:</b>
    /// <code>
    /// [StartupModule(Order = -10)]
    /// public class CoreModule : MvcModule
    /// {
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class StartupModuleAttribute : Attribute
    {
        /// <summary>
        /// Execution order relative to other <c>[StartupModule]</c> classes.
        /// Lower values start first. Default: 0.
        /// </summary>
        public int Order { get; set; } = 0;

        /// <summary>
        /// Validates that this attribute is applied to an <see cref="MvcModule"/> subclass.
        /// Called during assembly scanning.
        /// </summary>
        /// <param name="moduleType">The type this attribute is applied to.</param>
        /// <exception cref="ArgumentNullException">Thrown when moduleType is null.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the type does not inherit from MvcModule.</exception>
        internal void Validate(Type moduleType)
        {
            if (moduleType == null)
                throw new ArgumentNullException(nameof(moduleType));

            if (!typeof(MvcModule).IsAssignableFrom(moduleType))
                throw new InvalidOperationException(
                    $"[StartupModule] Type '{moduleType.FullName}' must inherit from MvcModule.");
        }
    }
}

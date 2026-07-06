using System;
using mvcExpress.Logging;

namespace mvcExpress
{
    /// <summary>
    /// Marks a <see cref="MediatorBehaviour"/> for declarative attachment during module initialization.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use <c>[Attach]</c> when you want a mediator to declare its own module ownership instead of
    /// wiring it inside <see cref="MvcModule.AttachMediators"/>. This is the attribute-based
    /// registration method - it runs after Unity inspector registrations
    /// (<see cref="MediatorRegistryBehaviour"/>) and before code attachments in <c>AttachMediators</c>.
    /// </para>
    /// <para>
    /// Specify <b>exactly one</b> attachment strategy per attribute:
    /// <list type="bullet">
    /// <item><description><see cref="FindInScene"/> = <c>true</c> - locate an existing scene instance.</description></item>
    /// <item><description><see cref="PrefabPath"/> - load and instantiate from a Unity <c>Resources</c> path.</description></item>
    /// <item><description>Neither set - use <see cref="AttachPrefabAttribute"/> with a <see cref="ViewPrefabCatalog"/> instead.</description></item>
    /// </list>
    /// If both <see cref="PrefabPath"/> and <see cref="FindInScene"/> are set, <see cref="PrefabPath"/> wins
    /// and a warning is logged.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// // Find an existing scene instance
    /// [Attach(typeof(UIModule), FindInScene = true)]
    /// public sealed class GameHudMediator : MediatorBehaviour
    /// {
    ///     protected override void OnInitialized() { }
    /// }
    ///
    /// // Instantiate from Resources
    /// [Attach(typeof(GameplayModule), PrefabPath = "Prefabs/UI/GameHud")]
    /// public sealed class GameplayHudMediator : MediatorBehaviour
    /// {
    ///     protected override void OnInitialized() { }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class AttachAttribute : Attribute
    {
        /// <summary>
        /// Module type that should own this mediator.
        /// </summary>
        /// <remarks>
        /// Null means the mediator is eligible for the module currently being initialized.
        /// For multi-module projects, specify a concrete module type.
        /// </remarks>
        public Type TargetModuleType { get; }

        /// <summary>
        /// Optional Resources path used to instantiate the mediator prefab.
        /// </summary>
        /// <remarks>
        /// Example: <c>"Prefabs/UI/GameHud"</c>. If this is null, set
        /// <see cref="FindInScene"/> or use a Unity registry/prefab catalog path.
        /// </remarks>
        public string PrefabPath { get; set; }

        /// <summary>
        /// Gets or sets whether the initializer should find an existing scene instance.
        /// </summary>
        public bool FindInScene { get; set; }

        /// <summary>
        /// Creates a mediator attachment attribute for the specified module.
        /// </summary>
        /// <param name="targetModuleType">Concrete module type that should own the mediator.</param>
        public AttachAttribute(Type targetModuleType = null)
        {
            TargetModuleType = targetModuleType;
        }

        /// <summary>
        /// Validates the attribute configuration during assembly scanning.
        /// </summary>
        /// <param name="mediatorType">Mediator type decorated with this attribute.</param>
        internal void Validate(Type mediatorType)
        {
            if (mediatorType == null)
                throw new ArgumentNullException(nameof(mediatorType));

            // Mediator attributes are valid only on view actors.
            if (!typeof(MediatorBehaviour).IsAssignableFrom(mediatorType))
            {
                throw new InvalidOperationException(
                    $"[AttachAttribute] Type '{mediatorType.FullName}' must inherit from MediatorBehaviour.");
            }

            // TargetModuleType is optional for single-module style but must be a module when supplied.
            if (TargetModuleType != null && !typeof(MvcModule).IsAssignableFrom(TargetModuleType))
            {
                throw new InvalidOperationException(
                    $"[AttachAttribute] Target module type '{TargetModuleType.FullName}' must inherit from MvcModule. " +
                    $"Mediator: '{mediatorType.FullName}'");
            }

            // Resources prefabs take precedence when both declarative strategies are supplied.
            if (!string.IsNullOrEmpty(PrefabPath) && FindInScene)
            {
                MvcDebug.LogWarning(
                    $"Both PrefabPath and FindInScene are set for mediator '{mediatorType.FullName}'. " +
                    $"PrefabPath will be used and FindInScene will be ignored.");
            }
        }
    }
}

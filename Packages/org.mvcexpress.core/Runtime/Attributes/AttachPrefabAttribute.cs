using System;

namespace mvcExpress
{
    /// <summary>
    /// Marks a <see cref="MediatorBehaviour"/> for prefab-based attachment via a <see cref="ViewPrefabCatalog"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use <c>[AttachPrefab]</c> instead of <see cref="AttachAttribute"/> when the mediator prefab
    /// is managed in a <see cref="ViewPrefabCatalog"/> ScriptableObject rather than a hard-coded
    /// <c>Resources</c> path. This decouples the mediator type from the prefab asset path and allows
    /// the catalog to be swapped per platform or build configuration.
    /// </para>
    /// <para>
    /// The framework resolves the prefab by calling
    /// <see cref="ViewPrefabCatalog.TryGetMediatorPrefab"/> with the mediator's type at
    /// attachment time. If no matching entry exists in any registered catalog, attachment fails.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [AttachPrefab(typeof(UIModule))]
    /// public sealed class InventoryPanelMediator : MediatorBehaviour
    /// {
    ///     protected override void OnInitialized() { }
    /// }
    /// // The prefab for InventoryPanelMediator must be mapped in a ViewPrefabCatalog
    /// // referenced by UIModule (or MvcFacade).
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public sealed class AttachPrefabAttribute : Attribute
    {
        /// <summary>
        /// Module type that should own the prefab-created mediator.
        /// </summary>
        /// <remarks>
        /// Null means the mediator is eligible for the module currently being initialized.
        /// </remarks>
        public Type TargetModuleType { get; }

        /// <summary>
        /// Creates a prefab attachment attribute for the specified module.
        /// </summary>
        /// <param name="targetModuleType">Concrete module type that should own the mediator.</param>
        public AttachPrefabAttribute(Type targetModuleType = null)
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

            // Prefab attachment is meaningful only for mediator components.
            if (!typeof(MediatorBehaviour).IsAssignableFrom(mediatorType))
            {
                throw new InvalidOperationException(
                    $"[AttachPrefab] Type '{mediatorType.FullName}' must inherit from MediatorBehaviour.");
            }

            // TargetModuleType is optional for single-module style but must be a module when supplied.
            if (TargetModuleType != null && !typeof(MvcModule).IsAssignableFrom(TargetModuleType))
            {
                throw new InvalidOperationException(
                    $"[AttachPrefab] Target module type '{TargetModuleType.FullName}' must inherit from MvcModule. " +
                    $"Mediator: '{mediatorType.FullName}'");
            }
        }
    }
}

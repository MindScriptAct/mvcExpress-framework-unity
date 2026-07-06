using System;

namespace mvcExpress
{
    /// <summary>
    /// Marks a <see cref="MediatorBehaviour"/> whose prefab should be automatically collected into
    /// the project's auto-generated <see cref="ViewPrefabCatalog"/> by the editor-time catalog baker.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This attribute only controls whether a mediator's prefab is discovered and written into the
    /// generated catalog - it does not affect how the mediator is attached at runtime. Use it alongside
    /// <see cref="AttachPrefabAttribute"/> for a fully attribute-driven setup, or on its own if the
    /// mediator is attached some other way and only the catalog needs to be populated automatically.
    /// </para>
    /// <para>
    /// Exactly one prefab in the project must have this mediator type on its root GameObject. Zero
    /// matches or two-or-more matches are treated as errors by the baker and block entering Play Mode.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// [CatalogPrefab]
    /// [AttachPrefab(typeof(UIModule))]
    /// public sealed class InventoryPanelMediator : MediatorBehaviour
    /// {
    ///     protected override void OnInitialized() { }
    /// }
    /// </code>
    /// </example>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class CatalogPrefabAttribute : Attribute
    {
        /// <summary>
        /// Validates the attribute configuration during editor-time scanning.
        /// </summary>
        /// <param name="mediatorType">Mediator type decorated with this attribute.</param>
        internal void Validate(Type mediatorType)
        {
            if (mediatorType == null)
                throw new ArgumentNullException(nameof(mediatorType));

            if (!typeof(MediatorBehaviour).IsAssignableFrom(mediatorType))
            {
                throw new InvalidOperationException(
                    $"[CatalogPrefab] Type '{mediatorType.FullName}' must inherit from MediatorBehaviour.");
            }

            if (mediatorType.IsAbstract)
            {
                throw new InvalidOperationException(
                    $"[CatalogPrefab] Type '{mediatorType.FullName}' must be a concrete (non-abstract) class.");
            }
        }
    }
}

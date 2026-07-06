using System.Collections.Generic;

namespace mvcExpress.Editor.Core
{
    /// <summary>
    /// Writes a resolved set of [CatalogPrefab] mappings into a ViewPrefabCatalog instance,
    /// fully replacing its previous contents (a full resync, not an incremental merge).
    /// </summary>
    internal static class MvcCatalogPrefabWriter
    {
        /// <summary>
        /// Fully replaces <paramref name="catalog"/>'s mediator-prefab mappings with <paramref name="mappings"/>.
        /// No-ops when <paramref name="catalog"/> is null. An empty or null <paramref name="mappings"/> clears the catalog.
        /// </summary>
        public static void Write(ViewPrefabCatalog catalog, IReadOnlyList<CatalogPrefabMapping> mappings)
        {
            if (catalog == null)
                return;

            int count = mappings?.Count ?? 0;
            var entries = new MediatorPrefabMapping[count];
            for (int i = 0; i < count; i++)
            {
                var mapping = mappings[i];
                entries[i] = new MediatorPrefabMapping
                {
                    MediatorTypeName = mapping.MediatorType.AssemblyQualifiedName,
                    Prefab = mapping.Prefab
                };
            }

            catalog.MediatorPrefabs = entries;
        }
    }
}

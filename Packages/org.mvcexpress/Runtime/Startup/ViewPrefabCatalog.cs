﻿using System;
using UnityEngine;

namespace mvcExpress
{
    /// <summary>
    /// ScriptableObject catalog that maps <see cref="MediatorBehaviour"/> types to view prefabs.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Catalogs decouple mediator types from hard-coded prefab paths. Instead of every module
    /// knowing a Resources path, modules (or <c>MvcFacade</c>) reference one or more catalogs and
    /// call <see cref="TryGetMediatorPrefab"/> at runtime to resolve the correct prefab for a
    /// mediator type.
    /// </para>
    /// <para>
    /// This indirection lets you swap prefabs per platform or build variant by simply assigning
    /// a different catalog asset - no code changes required.
    /// </para>
    /// <para>
    /// <see cref="MvcPrefabCatalog"/> is an obsolete alias for this type.
    /// </para>
    /// </remarks>
    public class ViewPrefabCatalog : ScriptableObject
    {
        [SerializeField]
        private MediatorPrefabMapping[] _mediatorPrefabs = Array.Empty<MediatorPrefabMapping>();

        /// <summary>
        /// Gets or sets mediator prefab mappings stored in this catalog.
        /// </summary>
        public MediatorPrefabMapping[] MediatorPrefabs
        {
            get => _mediatorPrefabs;
            set => _mediatorPrefabs = value ?? Array.Empty<MediatorPrefabMapping>();
        }

        /// <summary>
        /// Attempts to resolve a prefab for the supplied mediator type.
        /// </summary>
        /// <param name="mediatorType">Mediator type requested by runtime attachment.</param>
        /// <param name="prefab">Mapped prefab when one is found.</param>
        /// <returns>True when a matching prefab mapping exists.</returns>
        public bool TryGetMediatorPrefab(Type mediatorType, out GameObject prefab)
        {
            prefab = null;

            if (mediatorType == null || _mediatorPrefabs == null)
                return false;

            for (int i = 0; i < _mediatorPrefabs.Length; i++)
            {
                var mapping = _mediatorPrefabs[i];
                if (mapping == null || mapping.Prefab == null)
                    continue;

                if (string.IsNullOrWhiteSpace(mapping.MediatorTypeName))
                    continue;

                var mappedType = Type.GetType(mapping.MediatorTypeName, throwOnError: false);
                if (mappedType == null)
                    continue;

                if (mappedType == mediatorType || mappedType.IsAssignableFrom(mediatorType) || mediatorType.IsAssignableFrom(mappedType))
                {
                    prefab = mapping.Prefab;
                    return true;
                }
            }

            return false;
        }

#if UNITY_EDITOR
        // Keep serialized type names current when scripts or prefabs are assigned in the editor.
        private void OnValidate()
        {
            if (_mediatorPrefabs == null)
                return;

            for (int i = 0; i < _mediatorPrefabs.Length; i++)
            {
                _mediatorPrefabs[i]?.EditorSyncTypeNameFromReferences();
            }
        }
#endif
    }
}

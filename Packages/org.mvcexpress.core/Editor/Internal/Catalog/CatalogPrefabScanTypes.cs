using System;
using System.Collections.Generic;
using UnityEngine;

namespace mvcExpress.Editor.Core
{
    /// <summary>One resolved (Mediator type, prefab) pair ready to be written into a ViewPrefabCatalog.</summary>
    internal readonly struct CatalogPrefabMapping
    {
        public readonly Type MediatorType;
        public readonly GameObject Prefab;

        public CatalogPrefabMapping(Type mediatorType, GameObject prefab)
        {
            MediatorType = mediatorType;
            Prefab = prefab;
        }
    }

    /// <summary>Outcome of evaluating [CatalogPrefab] candidates - valid mappings plus every error found.</summary>
    internal sealed class CatalogPrefabScanResult
    {
        public List<CatalogPrefabMapping> ValidMappings { get; } = new List<CatalogPrefabMapping>();
        public List<string> Errors { get; } = new List<string>();

        public bool HasErrors => Errors.Count > 0;
    }
}

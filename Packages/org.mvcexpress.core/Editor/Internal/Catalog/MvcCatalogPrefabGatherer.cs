using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.Core
{
    /// <summary>
    /// Scans the project for [CatalogPrefab]-marked Mediator types and the prefab(s) matching each,
    /// then hands the raw candidate data to <see cref="MvcCatalogPrefabEvaluator"/> for validation.
    /// </summary>
    internal static class MvcCatalogPrefabGatherer
    {
        /// <summary>
        /// Scans the whole project for [CatalogPrefab]-marked Mediator types and their matching
        /// prefab(s), and evaluates the result via <see cref="MvcCatalogPrefabEvaluator"/>.
        /// </summary>
        /// <returns>Valid (type, prefab) mappings plus every orphan/duplicate/validation error found.</returns>
        public static CatalogPrefabScanResult Scan()
        {
            var markedTypes = TypeCache.GetTypesWithAttribute<CatalogPrefabAttribute>();
            var candidatesByType = new Dictionary<Type, List<GameObject>>();
            var validationErrors = new List<string>();
            var validTypes = new List<Type>();

            foreach (var mediatorType in markedTypes)
            {
                if (mediatorType.IsAbstract)
                    continue;

                var attr = (CatalogPrefabAttribute)Attribute.GetCustomAttribute(mediatorType, typeof(CatalogPrefabAttribute));
                if (attr == null)
                    continue;

                try
                {
                    attr.Validate(mediatorType);
                }
                catch (InvalidOperationException ex)
                {
                    validationErrors.Add(ex.Message);
                    continue;
                }

                candidatesByType[mediatorType] = new List<GameObject>();
                validTypes.Add(mediatorType);
            }

            if (validTypes.Count > 0)
            {
                FindPrefabsWithComponents(validTypes, candidatesByType);
            }

            var result = MvcCatalogPrefabEvaluator.Evaluate(candidatesByType);
            result.Errors.AddRange(validationErrors);
            return result;
        }

        // Scans every prefab in the project exactly once, regardless of how many marked types
        // there are, instead of re-scanning the whole project per type.
        private static void FindPrefabsWithComponents(List<Type> componentTypes, Dictionary<Type, List<GameObject>> candidatesByType)
        {
            var guids = AssetDatabase.FindAssets("t:Prefab");

            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                    continue;

                for (int t = 0; t < componentTypes.Count; t++)
                {
                    var componentType = componentTypes[t];
                    if (prefab.GetComponent(componentType) != null)
                    {
                        candidatesByType[componentType].Add(prefab);
                    }
                }
            }
        }
    }
}

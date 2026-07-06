using System;
using System.Collections.Generic;
using UnityEngine;

namespace mvcExpress.Editor.Core
{
    /// <summary>
    /// Pure decision logic for [CatalogPrefab] baking: given candidate prefabs already found per
    /// mediator type, decides which types resolve cleanly and which are orphaned/duplicated.
    /// Contains no AssetDatabase/TypeCache access so it can be unit tested without a project scan.
    /// </summary>
    internal static class MvcCatalogPrefabEvaluator
    {
        /// <summary>
        /// Classifies each mediator type's candidate prefab list as a valid mapping (exactly one
        /// candidate), an orphan (zero candidates), or a duplicate (two or more candidates).
        /// </summary>
        /// <param name="candidatesByType">Prefabs found per mediator type; null is treated as empty.</param>
        /// <returns>Valid mappings plus one error message per orphaned or duplicated type.</returns>
        public static CatalogPrefabScanResult Evaluate(Dictionary<Type, List<GameObject>> candidatesByType)
        {
            var result = new CatalogPrefabScanResult();

            if (candidatesByType == null)
                return result;

            foreach (var kvp in candidatesByType)
            {
                var mediatorType = kvp.Key;
                var candidates = kvp.Value;

                if (candidates == null || candidates.Count == 0)
                {
                    result.Errors.Add(
                        $"[CatalogPrefab] '{mediatorType.FullName}' has no prefab in the project with that component on its root.");
                    continue;
                }

                if (candidates.Count > 1)
                {
                    var names = BuildNameList(candidates);
                    result.Errors.Add(
                        $"[CatalogPrefab] '{mediatorType.FullName}' matches multiple prefabs: {names}. " +
                        "Remove the component from all but one, or split the type.");
                    continue;
                }

                result.ValidMappings.Add(new CatalogPrefabMapping(mediatorType, candidates[0]));
            }

            return result;
        }

        private static string BuildNameList(List<GameObject> candidates)
        {
            var names = new string[candidates.Count];
            for (int i = 0; i < candidates.Count; i++)
                names[i] = candidates[i] != null ? candidates[i].name : "<null>";

            return string.Join(", ", names);
        }
    }
}

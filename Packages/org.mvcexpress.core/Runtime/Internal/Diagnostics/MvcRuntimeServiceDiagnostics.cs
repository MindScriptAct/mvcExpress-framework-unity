using mvcExpress.Internal.DependencyInjection;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace mvcExpress.Internal.Diagnostics
{
    /// <summary>
    /// Runtime-only helper that maps service registry GameObjects back to live DI registrations for tooling.
    /// </summary>
    internal static class MvcRuntimeServiceDiagnostics
    {
        private static readonly List<MvcDiContainer.RegistrationSnapshot> s_snapshot = new List<MvcDiContainer.RegistrationSnapshot>(128);

        internal static bool TryGetServiceRegistrations(ServiceRegistryBehaviour registry, List<MvcDiContainer.RegistrationSnapshot> results, out MvcModule module)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));

            results.Clear();
            module = null;

            if (registry == null)
                return false;

            if (!Application.isPlaying)
                return false;

            module = registry.GetComponentInParent<MvcModule>();
            if (module == null)
                return false;

            var container = module.DiContainer;
            if (container == null)
                return false;

            container.GetRegistrationSnapshot(s_snapshot);

            // Filter to services that are backed by Unity objects under this services container.
            // NOTE: This intentionally excludes code-only services since those are not attached to this registry GameObject.
            var root = registry.transform;
            for (int i = 0; i < s_snapshot.Count; i++)
            {
                var item = s_snapshot[i];
                if (item.Instance is UnityEngine.Object unityObj)
                {
                    var go = GetGameObject(unityObj);
                    if (go != null && IsUnderRoot(go.transform, root))
                    {
                        results.Add(item);
                    }
                }
            }

            return true;
        }

        private static GameObject GetGameObject(UnityEngine.Object obj)
        {
            if (obj is Component c) return c.gameObject;
            if (obj is GameObject go) return go;
            return null;
        }

        private static bool IsUnderRoot(Transform t, Transform root)
        {
            if (t == null || root == null) return false;
            while (t != null)
            {
                if (t == root) return true;
                t = t.parent;
            }
            return false;
        }
    }
}

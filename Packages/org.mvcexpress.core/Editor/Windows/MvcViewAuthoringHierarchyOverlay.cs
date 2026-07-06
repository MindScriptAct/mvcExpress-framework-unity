using System.Collections.Generic;
using System.Linq;
using mvcExpress;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.Inspectors
{
    /// <summary>
    /// Draws a Preview-Only label next to GameObjects in the Hierarchy window that are registered as preview entries under an MvcViewAuthoringRoot.
    /// </summary>
    [InitializeOnLoad]
    internal static class MvcViewAuthoringHierarchyOverlay
    {
#if UNITY_6000_4_OR_NEWER
        private static readonly Dictionary<EntityId, string> LabelsByEntityId = new Dictionary<EntityId, string>();
#else
        private static readonly Dictionary<int, string> LabelsByEntityId = new Dictionary<int, string>();
#endif

        static MvcViewAuthoringHierarchyOverlay()
        {
#if UNITY_6000_4_OR_NEWER
            EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyItemGUI;
#else
            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyItemGUI;
#endif
            EditorApplication.hierarchyChanged += RebuildCache;
            RebuildCache();
        }

        public static void RebuildCache()
        {
            LabelsByEntityId.Clear();

#if UNITY_6000_5_OR_NEWER
            foreach (var root in Object.FindObjectsByType<MvcViewAuthoringRoot>(FindObjectsInactive.Include))
#elif UNITY_2023_1_OR_NEWER
#pragma warning disable CS0618 // FindObjectsSortMode: deprecated in 6000.5, required before it
            foreach (var root in Object.FindObjectsByType<MvcViewAuthoringRoot>(FindObjectsInactive.Include, FindObjectsSortMode.None))
#pragma warning restore CS0618
#else
            foreach (var root in Object.FindObjectsOfType<MvcViewAuthoringRoot>(includeInactive: true))
#endif
            {
                foreach (var entry in root.PreviewEntries.Where(e => e != null))
                {
                    if (entry.PreviewObject != null)
                    {
#if UNITY_6000_4_OR_NEWER
                        LabelsByEntityId[entry.PreviewObject.GetEntityId()] = entry.ListItem ? "PreviewOnly 01" : "PreviewOnly";
#else
                        LabelsByEntityId[entry.PreviewObject.GetInstanceID()] = entry.ListItem ? "PreviewOnly 01" : "PreviewOnly";
#endif
                    }

                    for (int i = 0; i < entry.GeneratedCopies.Count; i++)
                    {
                        var copy = entry.GeneratedCopies[i];
                        if (copy != null)
                        {
#if UNITY_6000_4_OR_NEWER
                            LabelsByEntityId[copy.GetEntityId()] = $"PreviewOnly {i + 2:00}";
#else
                            LabelsByEntityId[copy.GetInstanceID()] = $"PreviewOnly {i + 2:00}";
#endif
                        }
                    }
                }
            }

            EditorApplication.RepaintHierarchyWindow();
        }

#if UNITY_6000_4_OR_NEWER
        private static void OnHierarchyItemGUI(EntityId entityId, Rect selectionRect)
#else
        private static void OnHierarchyItemGUI(int entityId, Rect selectionRect)
#endif
        {
            if (!LabelsByEntityId.TryGetValue(entityId, out var label))
                return;

            var rect = selectionRect;
            rect.x = rect.xMax - 108f;
            rect.width = 104f;

            var previousColor = GUI.color;
            GUI.color = new Color(0.35f, 0.75f, 1f, 0.9f);
            GUI.Label(rect, label, EditorStyles.miniBoldLabel);
            GUI.color = previousColor;
        }
    }
}

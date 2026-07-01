using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.Inspectors
{
    [CustomEditor(typeof(ViewPrefabCatalog), true)]
    public sealed class MvcPrefabCatalogEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(6f);
            DrawValidation((ViewPrefabCatalog)target);
        }

        private static void DrawValidation(ViewPrefabCatalog catalog)
        {
            var mappings = catalog != null ? catalog.MediatorPrefabs : null;
            if (mappings == null || mappings.Length == 0)
            {
                EditorGUILayout.HelpBox("View prefab catalog has no mediator prefabs.", MessageType.Info);
                return;
            }

            var seen = new HashSet<Type>();
            for (int i = 0; i < mappings.Length; i++)
            {
                var mapping = mappings[i];
                if (mapping == null)
                {
                    EditorGUILayout.HelpBox($"Mediator prefab entry {i} is empty.", MessageType.Error);
                    continue;
                }

                if (string.IsNullOrWhiteSpace(mapping.MediatorTypeName))
                {
                    EditorGUILayout.HelpBox($"Mediator prefab entry {i} has no mediator type.", MessageType.Error);
                    continue;
                }

                var mediatorType = Type.GetType(mapping.MediatorTypeName, throwOnError: false);
                if (mediatorType == null)
                {
                    EditorGUILayout.HelpBox($"Mediator prefab entry {i} has a stale or missing mediator type.", MessageType.Error);
                    continue;
                }

                if (!typeof(MediatorBehaviour).IsAssignableFrom(mediatorType) || mediatorType.IsAbstract)
                {
                    EditorGUILayout.HelpBox($"Mediator prefab entry {i} is not a concrete MediatorBehaviour.", MessageType.Error);
                    continue;
                }

                if (!seen.Add(mediatorType))
                {
                    EditorGUILayout.HelpBox($"Mediator type '{mediatorType.Name}' appears more than once.", MessageType.Error);
                }

                if (mapping.Prefab == null)
                {
                    EditorGUILayout.HelpBox($"Mediator type '{mediatorType.Name}' has no prefab assigned.", MessageType.Error);
                    continue;
                }

                var mediator = mapping.Prefab.GetComponent(mediatorType) as MediatorBehaviour;
                if (mediator == null)
                {
                    EditorGUILayout.HelpBox($"Prefab for mediator '{mediatorType.Name}' does not contain that component on its root.", MessageType.Error);
                }
            }
        }
    }
}

using mvcExpress.Editor.Core;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace mvcExpress.Editor.Inspectors
{
    /// <summary>
    /// Custom inspector for ViewPrefabCatalog that draws its mediator-type-to-prefab mapping list.
    /// </summary>
    [CustomEditor(typeof(ViewPrefabCatalog), true)]
    public sealed class MvcPrefabCatalogEditor : UnityEditor.Editor
    {
        private const string HeaderTitle = "View Prefab Catalog";

        private static GUIStyle s_headerTitleStyle;

        private SerializedProperty _mediatorPrefabsProperty;
        private ReorderableList _mediatorPrefabsList;
        private readonly MvcListPager _pager = new MvcListPager();

        private void OnEnable()
        {
            _mediatorPrefabsProperty = serializedObject.FindProperty("_mediatorPrefabs");
            _mediatorPrefabsList = MvcMediatorPrefabListDrawer.BuildList(serializedObject, _mediatorPrefabsProperty, _pager);
        }

        private void OnDisable()
        {
            if (_mediatorPrefabsList != null)
            {
                _mediatorPrefabsList.drawHeaderCallback = null;
                _mediatorPrefabsList.drawElementCallback = null;
                _mediatorPrefabsList.elementHeightCallback = null;
                _mediatorPrefabsList.onAddCallback = null;
                _mediatorPrefabsList = null;
            }

            _mediatorPrefabsProperty = null;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawTopHeader();
            DrawGeneratedCatalogNotice();
            DrawListHeader();

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                _pager.ClampToCount(_mediatorPrefabsProperty.arraySize);
                _mediatorPrefabsList.DoLayoutList();
                _pager.DrawControls(_mediatorPrefabsProperty.arraySize);
            }

            serializedObject.ApplyModifiedProperties();

            EditorGUILayout.Space(6f);
            DrawValidation((ViewPrefabCatalog)target);
        }

        private static void DrawTopHeader()
        {
            EditorGUILayout.Space(2f);
            GUILayout.Label(HeaderTitle, MvcEditorUtility.TopHeaderTitleStyle, GUILayout.Height(MvcEditorUtility.TopHeaderIconHeight));
            EditorGUILayout.Space(6f);
        }

        private void DrawGeneratedCatalogNotice()
        {
#if !MVC_EXPRESS_NO_ATTRIBUTE
            var path = AssetDatabase.GetAssetPath(target);
            if (path == MvcCatalogPrefabBaker.GeneratedCatalogPath)
            {
                EditorGUILayout.HelpBox(
                    "This catalog is auto-generated from [CatalogPrefab]-marked Mediators. " +
                    "Manual edits here will be overwritten the next time the catalog is baked (on entering Play Mode).",
                    MessageType.Warning);
                EditorGUILayout.Space(4f);
            }
#endif
        }

        private void DrawListHeader()
        {
            s_headerTitleStyle ??= new GUIStyle(MvcEditorUtility.SectionHeaderTitleStyle) { alignment = TextAnchor.MiddleLeft };

            float lineH = EditorGUIUtility.singleLineHeight;
            float headerH = (lineH * 2f) + (6f * 2f);
            var content = MvcEditorUtility.DrawHeaderBox(headerH, padX: 8f, padY: 6f);
            var titleLine = new Rect(content.x, content.center.y - (lineH * 0.5f), content.width, lineH);

            EditorGUI.LabelField(titleLine, $"Mediator Prefabs ({_mediatorPrefabsProperty.arraySize})", s_headerTitleStyle);

            EditorGUILayout.Space(2f);
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

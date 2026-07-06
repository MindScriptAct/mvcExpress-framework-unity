using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace mvcExpress.Editor.Core
{
    /// <summary>
    /// Builds and draws the "Mediator Prefabs" ReorderableList (Mediator/Prefab columns) shared by
    /// <c>MvcPrefabCatalogEditor</c> and any inline preview of a ViewPrefabCatalog (e.g. the Facade's
    /// View Prefab Catalogs row expander), so both stay visually and behaviorally identical.
    /// </summary>
    internal static class MvcMediatorPrefabListDrawer
    {
        private static readonly GUIContent MediatorHeader = new GUIContent("Mediator", "Mediator type resolved from the assigned prefab.");
        private static readonly GUIContent PrefabHeader = new GUIContent("Prefab", "Prefab/Prefab Variant containing the mediator on its root GameObject.");

        private static GUIStyle s_columnHeaderStyle;
        private static GUIStyle s_resolvedLinkStyle;

        public static ReorderableList BuildList(SerializedObject serializedObject, SerializedProperty mediatorPrefabsProperty, MvcListPager pager)
        {
            var list = new ReorderableList(serializedObject, mediatorPrefabsProperty, true, true, true, true);
            list.headerHeight = EditorGUIUtility.singleLineHeight;
            list.drawHeaderCallback = DrawColumns;
            list.elementHeightCallback = index =>
                pager.IsIndexVisible(index) ? EditorGUIUtility.singleLineHeight + 8f : 0f;
            list.drawElementCallback = (rect, index, active, focused) =>
            {
                if (!pager.IsIndexVisible(index))
                    return;
                DrawRow(rect, mediatorPrefabsProperty.GetArrayElementAtIndex(index));
            };
            list.onAddCallback = _ =>
            {
                serializedObject.Update();
                int idx = mediatorPrefabsProperty.arraySize;
                mediatorPrefabsProperty.InsertArrayElementAtIndex(idx);
                var el = mediatorPrefabsProperty.GetArrayElementAtIndex(idx);
                el.FindPropertyRelative("MediatorTypeName").stringValue = string.Empty;
                el.FindPropertyRelative("Prefab").objectReferenceValue = null;
                serializedObject.ApplyModifiedProperties();
                pager.GoToLastPage(mediatorPrefabsProperty.arraySize);
            };
            return list;
        }

        public static void DrawColumns(Rect rect)
        {
            s_columnHeaderStyle ??= new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold };

            float typeWidth = rect.width * 0.48f;
            float prefabWidth = rect.width - typeWidth - 4f;

            EditorGUI.LabelField(new Rect(rect.x, rect.y, typeWidth - 2f, rect.height), MediatorHeader, s_columnHeaderStyle);
            EditorGUI.LabelField(new Rect(rect.x + typeWidth + 4f, rect.y, prefabWidth - 2f, rect.height), PrefabHeader, s_columnHeaderStyle);
        }

        public static void DrawRow(Rect rect, SerializedProperty element)
        {
            rect.y += 2f;
            rect.height = EditorGUIUtility.singleLineHeight;

            var typeNameProp = element.FindPropertyRelative("MediatorTypeName");
            var prefabProp = element.FindPropertyRelative("Prefab");

            float typeWidth = rect.width * 0.48f;
            float prefabWidth = rect.width - typeWidth - 4f;

            var typeRect = new Rect(rect.x, rect.y, typeWidth - 2f, rect.height);
            var prefabRect = new Rect(rect.x + typeWidth + 4f, rect.y, prefabWidth - 2f, rect.height);

            DrawResolvedMediatorLabel(typeRect, typeNameProp);
            EditorGUI.PropertyField(prefabRect, prefabProp, GUIContent.none);
        }

        private static void DrawResolvedMediatorLabel(Rect rect, SerializedProperty typeNameProp)
        {
            var typeName = typeNameProp != null ? typeNameProp.stringValue : null;
            var resolvedType = string.IsNullOrEmpty(typeName) ? null : TypeResolutionUtility.SafeGetType(typeName);

            if (resolvedType == null)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUI.LabelField(rect, string.IsNullOrEmpty(typeName) ? "<assign a prefab>" : "<unresolved>", EditorStyles.centeredGreyMiniLabel);
                }
                return;
            }

            s_resolvedLinkStyle ??= new GUIStyle(EditorStyles.linkLabel) { alignment = TextAnchor.MiddleLeft };

            if (GUI.Button(rect, resolvedType.Name, s_resolvedLinkStyle))
            {
                MonoScriptCache.TryOpenScript(resolvedType);
            }
        }
    }
}

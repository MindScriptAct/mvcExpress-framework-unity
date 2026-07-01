using mvcExpress.Editor.Core;
using mvcExpress.Editor.Settings;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.Inspectors
{
    [CustomEditor(typeof(MvcFacade))]
    public sealed class MvcFacadeEditor : UnityEditor.Editor
    {
        private const string HeaderIconPath = "Packages/org.mvcexpress/Editor/Icons/mvc_facade_icon.png";
        private const string HeaderTitle = "MVC Express Facade";
        private static readonly string[] Tabs = { "Startup", "Globals", "Runtime", "Settings" };

        private SerializedProperty _startupModules;
        private SerializedProperty _viewPrefabCatalogs;
        private SerializedProperty _globalServiceRegistry;
        private SerializedProperty _globalProxyRegistry;

        private Texture2D _headerIcon;
        private MvcFacade _app;

        private static Texture2D s_hierarchyIcon;
        private static bool s_hierarchyCallbackRegistered;

        [InitializeOnLoadMethod]
        private static void InitializeHierarchyIcon()
        {
            if (!s_hierarchyCallbackRegistered)
            {
#if UNITY_6000_4_OR_NEWER
                EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyGUI;
#else
                EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
#endif
                s_hierarchyCallbackRegistered = true;
            }

            s_hierarchyIcon ??= AssetDatabase.LoadAssetAtPath<Texture2D>(HeaderIconPath);
        }

        private static void OnHierarchyGUI(
#if UNITY_6000_4_OR_NEWER
            EntityId entityId,
#else
            int instanceID,
#endif
            Rect selectionRect)
        {
            if (!MvcHierarchyUtils.ShowModuleFacadeIcons) return;
            if (s_hierarchyIcon == null) return;

#if UNITY_6000_4_OR_NEWER
            var obj = EditorUtility.EntityIdToObject(entityId) as GameObject;
#else
            var obj = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
#endif
            if (obj == null || obj.GetComponent<MvcFacade>() == null) return;

            var iconRect = new Rect(MvcHierarchyUtils.GetRightEdge(selectionRect) - 16, selectionRect.y, 16, 16);
            GUI.DrawTexture(iconRect, s_hierarchyIcon, ScaleMode.ScaleToFit);
        }

        private void OnEnable()
        {
            _app = (MvcFacade)target;
            _startupModules = serializedObject.FindProperty("_startupModules");
            _viewPrefabCatalogs = serializedObject.FindProperty("_viewPrefabCatalogs");
            _globalServiceRegistry = serializedObject.FindProperty("_globalServiceRegistry");
            _globalProxyRegistry = serializedObject.FindProperty("_globalProxyRegistry");
            _headerIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(HeaderIconPath);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawTopHeader();

            var tab = EditorPrefs.GetInt("org.mvcexpress.MvcFacade.inspector.tab", 0);
            tab = GUILayout.Toolbar(Mathf.Clamp(tab, 0, Tabs.Length - 1), Tabs);
            EditorPrefs.SetInt("org.mvcexpress.MvcFacade.inspector.tab", tab);

            EditorGUILayout.Space(8f);

            switch (tab)
            {
                case 0:
                    DrawStartupTab();
                    break;
                case 1:
                    DrawGlobalsTab();
                    break;
                case 2:
                    DrawRuntimeTab();
                    break;
                case 3:
                    DrawSettingsTab();
                    break;
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTopHeader()
        {
            EditorGUILayout.Space(2f);

            using (new EditorGUILayout.HorizontalScope())
            {
                var iconRect = GUILayoutUtility.GetRect(
                    MvcEditorUtility.TopHeaderIconWidth,
                    MvcEditorUtility.TopHeaderIconHeight,
                    GUILayout.Width(MvcEditorUtility.TopHeaderIconWidth),
                    GUILayout.Height(MvcEditorUtility.TopHeaderIconHeight));

                if (_headerIcon != null)
                {
                    GUI.DrawTexture(iconRect, _headerIcon, ScaleMode.ScaleToFit);
                }

                GUILayout.Space(10f);
                GUILayout.Label(HeaderTitle, MvcEditorUtility.TopHeaderTitleStyle, GUILayout.Height(MvcEditorUtility.TopHeaderIconHeight));
            }

            EditorGUILayout.Space(6f);
        }

        private static void DrawSectionHeader(string title)
        {
            var lineH = EditorGUIUtility.singleLineHeight;
            var headerH = (lineH * 2f) + (6f * 2f);
            var content = MvcEditorUtility.DrawHeaderBox(headerH, padX: 8f, padY: 6f);
            var titleLine = new Rect(content.x, content.center.y - (lineH * 0.5f), content.width, lineH);
            EditorGUI.LabelField(titleLine, title, MvcEditorUtility.SectionHeaderTitleStyle);
            EditorGUILayout.Space(2f);
        }

        private void DrawStartupTab()
        {
            DrawSectionHeader("Startup Modules");
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                EditorGUILayout.PropertyField(_startupModules, new GUIContent("Startup Modules"), includeChildren: true);
            }
            DrawStartupValidation();

            EditorGUILayout.Space(10f);
            DrawSectionHeader("View Prefab Catalogs");
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                EditorGUILayout.PropertyField(_viewPrefabCatalogs, new GUIContent("Catalogs"), includeChildren: true);
            }
            DrawCatalogValidation();
        }

        private void DrawGlobalsTab()
        {
            DrawSectionHeader("Global Registries");
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                DrawContainerRow<GlobalServiceRegistryBehaviour>(_globalServiceRegistry, "Global Services", 0);
                DrawContainerRow<GlobalProxyRegistryBehaviour>(_globalProxyRegistry, "Global Proxies", 1);
            }

            EditorGUILayout.Space(6f);
            EditorGUILayout.HelpBox(
                "Global services and proxies are configured through these registry components. " +
                "Direct global startup arrays and Startup Profiles have been removed.",
                MessageType.Info);
        }

        private void DrawRuntimeTab()
        {
            DrawSectionHeader("Registered Modules");

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter play mode to see registered modules.", MessageType.Info);
            }
            else
            {
                var modules = _app.Modules;
                EditorGUILayout.LabelField("Active Modules", modules.Count.ToString());

                if (modules.Count == 0)
                {
                    EditorGUILayout.HelpBox("No modules registered yet.", MessageType.Info);
                }
                else
                {
                    foreach (var kvp in modules)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            EditorGUILayout.LabelField($"[{kvp.Key.Name}] {kvp.Value.IdName}");
                            if (GUILayout.Button("Select", GUILayout.Width(70f)))
                            {
                                Selection.activeGameObject = kvp.Value.gameObject;
                            }
                        }
                    }
                }
            }

            EditorGUILayout.Space(10f);
            DrawSectionHeader("Debug Info");
            EditorGUILayout.LabelField("Facade Type", _app.GetType().Name);
            EditorGUILayout.LabelField("GameObject", _app.gameObject.name);
            EditorGUILayout.LabelField("Status", Application.isPlaying ? "Running (DontDestroyOnLoad)" : "Edit Mode");
        }

        private void DrawSettingsTab()
        {
            DrawSectionHeader("Logging");
            DrawGlobalLoggingControl();

            EditorGUILayout.Space(10f);
            DrawSectionHeader("Project Settings");
            if (GUILayout.Button("Open mvcExpress Settings", GUILayout.Height(24f)))
            {
                SettingsService.OpenProjectSettings("Project/mvcExpress");
            }
        }

        private void DrawContainerRow<T>(SerializedProperty prop, string label, int siblingIndex) where T : Component
        {
            if (prop == null)
                return;

            var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            const float buttonWidth = 70f;
            const float gap = 6f;

            var fieldRect = new Rect(rowRect.x, rowRect.y, rowRect.width - buttonWidth - gap, rowRect.height);
            var buttonRect = new Rect(fieldRect.xMax + gap, rowRect.y, buttonWidth, rowRect.height);

            EditorGUI.BeginProperty(fieldRect, new GUIContent(label), prop);
            prop.objectReferenceValue = EditorGUI.ObjectField(fieldRect, label, prop.objectReferenceValue, typeof(T), true);
            EditorGUI.EndProperty();

            using (new EditorGUI.DisabledScope(prop.objectReferenceValue != null))
            {
                if (GUI.Button(buttonRect, "Create"))
                {
                    CreateOrFindContainer<T>(prop, label, siblingIndex);
                }
            }
        }

        private void CreateOrFindContainer<T>(SerializedProperty prop, string defaultName, int siblingIndex) where T : Component
        {
            var existing = _app.GetComponentInChildren<T>(includeInactive: true);
            if (existing != null)
            {
                prop.objectReferenceValue = existing;
                serializedObject.ApplyModifiedProperties();
                return;
            }

            var foundTransform = _app.transform.Find(defaultName);
            if (foundTransform != null && foundTransform.TryGetComponent<T>(out var found))
            {
                prop.objectReferenceValue = found;
                serializedObject.ApplyModifiedProperties();
                return;
            }

            Undo.RegisterCompleteObjectUndo(_app.gameObject, "Create MVC Global Container");

            var go = new GameObject(defaultName);
            Undo.RegisterCreatedObjectUndo(go, "Create MVC Global Container");
            go.transform.SetParent(_app.transform, false);

            var component = Undo.AddComponent<T>(go);
            prop.objectReferenceValue = component;

            var index = Mathf.Clamp(siblingIndex, 0, _app.transform.childCount - 1);
            go.transform.SetSiblingIndex(index);

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(_app);
        }

        private void DrawStartupValidation()
        {
            if (_startupModules == null || !_startupModules.isArray || _startupModules.arraySize == 0)
            {
                EditorGUILayout.HelpBox("No startup modules are configured. Modules can still exist in scene or be spawned from code.", MessageType.Info);
                return;
            }

            var seen = new HashSet<Type>();
            for (int i = 0; i < _startupModules.arraySize; i++)
            {
                var entry = _startupModules.GetArrayElementAtIndex(i);
                if (entry == null)
                    continue;

                var moduleType = ResolveStartupModuleType(entry);
                if (moduleType == null)
                {
                    EditorGUILayout.HelpBox($"Startup module entry {i} cannot resolve a module type.", MessageType.Error);
                    continue;
                }

                if (!typeof(MvcModule).IsAssignableFrom(moduleType) || moduleType.IsAbstract)
                {
                    EditorGUILayout.HelpBox($"Startup module entry {i} is not a concrete MvcModule.", MessageType.Error);
                    continue;
                }

                if (!seen.Add(moduleType))
                {
                    EditorGUILayout.HelpBox($"Startup module '{moduleType.Name}' is listed more than once.", MessageType.Error);
                }

                var prefabProperty = entry.FindPropertyRelative("ModulePrefab");
                var prefab = prefabProperty != null ? prefabProperty.objectReferenceValue as GameObject : null;
                if (prefab == null)
                    continue;

                var prefabModule = prefab.GetComponent<MvcModule>();
                if (prefabModule == null)
                {
                    EditorGUILayout.HelpBox($"Startup module '{moduleType.Name}' has a prefab without MvcModule on its root.", MessageType.Error);
                }
                else if (prefabModule.ModuleType != moduleType)
                {
                    EditorGUILayout.HelpBox($"Startup module '{moduleType.Name}' points to prefab module '{prefabModule.ModuleType.Name}'.", MessageType.Error);
                }
            }
        }

        private void DrawCatalogValidation()
        {
            if (_viewPrefabCatalogs == null || !_viewPrefabCatalogs.isArray || _viewPrefabCatalogs.arraySize == 0)
            {
                EditorGUILayout.HelpBox("No app-level view prefab catalogs are assigned. Module-local mediator registries will still work.", MessageType.Info);
                return;
            }

            for (int i = 0; i < _viewPrefabCatalogs.arraySize; i++)
            {
                var catalog = _viewPrefabCatalogs.GetArrayElementAtIndex(i);
                if (catalog != null && catalog.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox($"View prefab catalog entry {i} is empty.", MessageType.Warning);
                }
            }
        }

        private static Type ResolveStartupModuleType(SerializedProperty entry)
        {
            var prefabProperty = entry.FindPropertyRelative("ModulePrefab");
            var prefab = prefabProperty != null ? prefabProperty.objectReferenceValue as GameObject : null;
            if (prefab != null)
            {
                var module = prefab.GetComponent<MvcModule>();
                if (module != null)
                    return module.ModuleType;
            }

            var typeNameProperty = entry.FindPropertyRelative("_moduleTypeName");
            return typeNameProperty != null && !string.IsNullOrWhiteSpace(typeNameProperty.stringValue)
                ? TypeResolutionUtility.SafeGetType(typeNameProperty.stringValue)
                : null;
        }

        private static void DrawGlobalLoggingControl()
        {
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (var scope = new EditorGUI.ChangeCheckScope())
                {
                    var globalLogging = EditorGUILayout.ToggleLeft(
                        new GUIContent(
                            "Enable Global Logging",
                            "Enable logging for all modules across mvcExpress."),
                        MvcExpressProjectSettings.GlobalLoggingEnabled);

                    if (scope.changed)
                    {
                        MvcExpressProjectSettings.GlobalLoggingEnabled = globalLogging;
                    }
                }

                EditorGUILayout.HelpBox(
                    MvcExpressProjectSettings.GlobalLoggingEnabled
                        ? "Global logging is enabled. All modules log regardless of individual LoggingEnabled values."
                        : "Global logging is disabled. Individual modules control logging with their LoggingEnabled value.",
                    MessageType.Info);
            }
        }
    }
}

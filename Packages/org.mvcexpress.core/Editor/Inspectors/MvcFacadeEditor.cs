using mvcExpress.Editor.Core;
using mvcExpress.Editor.Settings;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace mvcExpress.Editor.Inspectors
{
    /// <summary>
    /// Custom tabbed inspector for MvcFacade covering startup module configuration, global registries, runtime module state, and composition settings.
    /// </summary>
    [CustomEditor(typeof(MvcFacade))]
    public sealed class MvcFacadeEditor : UnityEditor.Editor
    {
        private const string HeaderIconPath = "Packages/org.mvcexpress.core/Editor/Icons/mvc_facade_icon.png";
        private const string HeaderTitle = "MVC Express Facade";
        private static readonly string[] Tabs = { "Startup", "Globals", "Runtime", "Settings" };

        private SerializedProperty _startupModules;
        private SerializedProperty _viewPrefabCatalogs;
        private SerializedProperty _globalServiceRegistry;
        private SerializedProperty _globalProxyRegistry;

        private ReorderableList _startupModulesList;
        private readonly MvcListPager _startupModulesPager = new MvcListPager();
        private List<Type> _moduleTypes;

        private static readonly GUIContent StartupAutoHeader = new GUIContent("Auto", "Whether this entry starts automatically when the facade launches configured modules.");
        private static readonly GUIContent StartupScriptHeader = new GUIContent("Module Script", "Module type used to resolve which MvcModule to spawn.");
        private static readonly GUIContent StartupPrefabHeader = new GUIContent("Module Prefab (Optional)", "Optional prefab whose root contains the module component. Leave empty to create the module purely from Module Script.");
        private static readonly GUIContent StartupResolvedHeader = new GUIContent("Resolved Type", "Module type resolved from Module Script or Module Prefab. Click to open the script.");

        private static GUIStyle s_startupColumnHeaderStyle;
        private static GUIStyle s_startupResolvedLinkStyle;

        private ReorderableList _viewPrefabCatalogsList;
        private readonly MvcListPager _viewPrefabCatalogsPager = new MvcListPager();

        private static readonly GUIContent CatalogHeader = new GUIContent("Catalog", "ViewPrefabCatalog asset providing mediator-to-prefab mappings for this facade. Catalogs are searched in list order - first match wins.");
        private static readonly GUIContent CatalogCountHeader = new GUIContent("Count", "Number of mediator-to-prefab mappings defined in this catalog (read-only).");

        private static GUIContent s_editCatalogButtonContent;
        private static GUIStyle s_catalogCountStyle;

        private sealed class CatalogPreviewState
        {
            public SerializedObject SerializedObject;
            public SerializedProperty MediatorPrefabsProperty;
            public ReorderableList List;
            public readonly MvcListPager Pager = new MvcListPager();
        }

        private readonly Dictionary<ViewPrefabCatalog, CatalogPreviewState> _catalogPreviewStates = new Dictionary<ViewPrefabCatalog, CatalogPreviewState>();

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
            _moduleTypes = MvcTypeCacheUtility.GetNonAbstractDerivedTypes<MvcModule>();

            _startupModulesList = new ReorderableList(serializedObject, _startupModules, true, true, true, true);
            _startupModulesList.headerHeight = EditorGUIUtility.singleLineHeight;
            _startupModulesList.drawHeaderCallback = DrawStartupModuleColumns;
            _startupModulesList.elementHeightCallback = index =>
                _startupModulesPager.IsIndexVisible(index) ? EditorGUIUtility.singleLineHeight + 4f : 0f;
            _startupModulesList.drawElementCallback = (rect, index, active, focused) =>
            {
                if (!_startupModulesPager.IsIndexVisible(index))
                    return;
                DrawStartupModuleRow(rect, _startupModules.GetArrayElementAtIndex(index));
            };
            _startupModulesList.onAddCallback = list =>
            {
                serializedObject.Update();
                int idx = _startupModules.arraySize;
                _startupModules.InsertArrayElementAtIndex(idx);
                var el = _startupModules.GetArrayElementAtIndex(idx);
                el.FindPropertyRelative("AutoStart").boolValue = true;
                el.FindPropertyRelative("ModulePrefab").objectReferenceValue = null;
                el.FindPropertyRelative("ModuleScript").objectReferenceValue = null;
                serializedObject.ApplyModifiedProperties();
                _startupModulesPager.GoToLastPage(_startupModules.arraySize);
            };

            _viewPrefabCatalogsList = new ReorderableList(serializedObject, _viewPrefabCatalogs, true, true, true, true);
            _viewPrefabCatalogsList.headerHeight = EditorGUIUtility.singleLineHeight;
            _viewPrefabCatalogsList.drawHeaderCallback = DrawViewPrefabCatalogColumns;
            _viewPrefabCatalogsList.elementHeightCallback = index =>
            {
                if (!_viewPrefabCatalogsPager.IsIndexVisible(index))
                    return 0f;

                float height = EditorGUIUtility.singleLineHeight + 4f;

                var catalog = _viewPrefabCatalogs.GetArrayElementAtIndex(index).objectReferenceValue as ViewPrefabCatalog;
                if (catalog != null && _catalogPreviewStates.TryGetValue(catalog, out var state))
                {
                    state.SerializedObject.Update();
                    height += 6f // gap between the base row and the preview panel
                        + 12f // panel top+bottom padding
                        + state.List.GetHeight()
                        + state.Pager.EstimateControlsHeight(state.MediatorPrefabsProperty.arraySize);
                }

                return height;
            };
            _viewPrefabCatalogsList.drawElementCallback = (rect, index, active, focused) =>
            {
                if (!_viewPrefabCatalogsPager.IsIndexVisible(index))
                    return;
                DrawViewPrefabCatalogRow(rect, _viewPrefabCatalogs.GetArrayElementAtIndex(index));
            };
            _viewPrefabCatalogsList.onAddCallback = list =>
            {
                serializedObject.Update();
                int idx = _viewPrefabCatalogs.arraySize;
                _viewPrefabCatalogs.InsertArrayElementAtIndex(idx);
                _viewPrefabCatalogs.GetArrayElementAtIndex(idx).objectReferenceValue = null;
                serializedObject.ApplyModifiedProperties();
                _viewPrefabCatalogsPager.GoToLastPage(_viewPrefabCatalogs.arraySize);
            };
        }

        private void OnDisable()
        {
            if (_startupModulesList != null)
            {
                _startupModulesList.drawHeaderCallback = null;
                _startupModulesList.drawElementCallback = null;
                _startupModulesList.elementHeightCallback = null;
                _startupModulesList.onAddCallback = null;
                _startupModulesList = null;
            }

            if (_viewPrefabCatalogsList != null)
            {
                _viewPrefabCatalogsList.drawHeaderCallback = null;
                _viewPrefabCatalogsList.drawElementCallback = null;
                _viewPrefabCatalogsList.elementHeightCallback = null;
                _viewPrefabCatalogsList.onAddCallback = null;
                _viewPrefabCatalogsList = null;
            }

            _moduleTypes = null;
            _catalogPreviewStates.Clear();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawTopHeader();

            var tab = EditorPrefs.GetInt("org.mvcexpress.core.MvcFacade.inspector.tab", 0);
            tab = GUILayout.Toolbar(Mathf.Clamp(tab, 0, Tabs.Length - 1), Tabs);
            EditorPrefs.SetInt("org.mvcexpress.core.MvcFacade.inspector.tab", tab);

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
            DrawSectionHeader($"Startup Modules ({_startupModules.arraySize})");
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                _startupModulesPager.ClampToCount(_startupModules.arraySize);
                _startupModulesList.DoLayoutList();
                _startupModulesPager.DrawControls(_startupModules.arraySize);
            }
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "Auto Start entries launch automatically when the facade starts configured modules. " +
                "Module Script identifies the module type. Module Prefab is optional - assign it only if the " +
                "module needs a prefab instance, otherwise it is created purely from code. " +
                "Resolved Type shows what will actually be spawned.",
                MessageType.Info);
            DrawStartupValidation();

            EditorGUILayout.Space(10f);
            DrawSectionHeader($"View Prefab Catalogs ({_viewPrefabCatalogs.arraySize})");
            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                _viewPrefabCatalogsPager.ClampToCount(_viewPrefabCatalogs.arraySize);
                _viewPrefabCatalogsList.DoLayoutList();
                _viewPrefabCatalogsPager.DrawControls(_viewPrefabCatalogs.arraySize);
            }
            EditorGUILayout.Space(4f);
            EditorGUILayout.HelpBox(
                "Catalogs are searched in list order when a module doesn't provide its own mediator prefab mapping - " +
                "the first catalog with a matching mediator type wins. Mappings shows how many mediator-to-prefab " +
                "entries each catalog contains.",
                MessageType.Info);
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

        private void DrawStartupModuleColumns(Rect rect)
        {
            s_startupColumnHeaderStyle ??= new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold };

            const float autoWidth = 34f;
            float remaining = rect.width - autoWidth;
            float scriptWidth = remaining * 0.34f;
            float prefabWidth = remaining * 0.34f;
            float resolvedWidth = remaining - scriptWidth - prefabWidth;

            float x = rect.x;
            EditorGUI.LabelField(new Rect(x, rect.y, autoWidth, rect.height), StartupAutoHeader, s_startupColumnHeaderStyle);
            x += autoWidth;
            EditorGUI.LabelField(new Rect(x, rect.y, scriptWidth - 2f, rect.height), StartupScriptHeader, s_startupColumnHeaderStyle);
            x += scriptWidth;
            EditorGUI.LabelField(new Rect(x, rect.y, prefabWidth - 2f, rect.height), StartupPrefabHeader, s_startupColumnHeaderStyle);
            x += prefabWidth;
            EditorGUI.LabelField(new Rect(x, rect.y, resolvedWidth - 2f, rect.height), StartupResolvedHeader, s_startupColumnHeaderStyle);
        }

        private void DrawStartupModuleRow(Rect rect, SerializedProperty entry)
        {
            rect.y += 2f;
            rect.height = EditorGUIUtility.singleLineHeight;

            var autoProp = entry.FindPropertyRelative("AutoStart");
            var prefabProp = entry.FindPropertyRelative("ModulePrefab");
            var scriptProp = entry.FindPropertyRelative("ModuleScript");

            var prefab = prefabProp.objectReferenceValue as GameObject;
            bool prefabAssigned = prefab != null;

            if (prefabAssigned)
            {
                var prefabModule = prefab.GetComponent<MvcModule>();
                if (prefabModule != null)
                {
                    var resolvedScript = MonoScript.FromMonoBehaviour(prefabModule);
                    if (scriptProp.objectReferenceValue != resolvedScript)
                        scriptProp.objectReferenceValue = resolvedScript;
                }
            }

            const float autoWidth = 34f;
            float remaining = rect.width - autoWidth;
            float scriptWidth = remaining * 0.34f;
            float prefabWidth = remaining * 0.34f;
            float resolvedWidth = remaining - scriptWidth - prefabWidth;

            float x = rect.x;

            var autoRect = new Rect(x + 8f, rect.y, autoWidth - 8f, rect.height);
            GUI.tooltip = StartupAutoHeader.tooltip;
            autoProp.boolValue = EditorGUI.Toggle(autoRect, autoProp.boolValue);
            GUI.tooltip = string.Empty;
            x += autoWidth;

            var scriptRect = new Rect(x, rect.y, scriptWidth - 4f, rect.height);
            GUI.tooltip = prefabAssigned
                ? "Derived from Module Prefab. Clear Module Prefab to pick a different script."
                : StartupScriptHeader.tooltip;
            using (new EditorGUI.DisabledScope(prefabAssigned))
            {
                DrawModuleScriptField(scriptRect, scriptProp);
            }
            GUI.tooltip = string.Empty;
            x += scriptWidth;

            var prefabRect = new Rect(x, rect.y, prefabWidth - 4f, rect.height);
            GUI.tooltip = StartupPrefabHeader.tooltip;
            EditorGUI.PropertyField(prefabRect, prefabProp, GUIContent.none);
            GUI.tooltip = string.Empty;
            x += prefabWidth;

            var resolvedRect = new Rect(x, rect.y, resolvedWidth - 2f, rect.height);
            DrawResolvedModuleTypeLabel(resolvedRect, ResolveStartupModuleType(entry));
        }

        private void DrawModuleScriptField(Rect rect, SerializedProperty scriptProp)
        {
            var currentScript = scriptProp.objectReferenceValue as MonoScript;
            var currentType = currentScript != null ? currentScript.GetClass() : null;
            var display = SearchablePopup.FormatTypeLabel(currentType, "(select module script)");

            SearchablePopup.Draw(
                rect,
                display,
                _moduleTypes,
                selectedType =>
                {
                    var script = MonoScriptCache.FindScriptForType(selectedType);
                    if (script == null)
                    {
                        EditorUtility.DisplayDialog("Module Script", $"Could not locate a script asset for '{selectedType.Name}'.", "OK");
                        return;
                    }

                    scriptProp.objectReferenceValue = script;
                    scriptProp.serializedObject.ApplyModifiedProperties();
                },
                getCurrentType: () => currentType,
                openType: type => MonoScriptCache.TryOpenScript(type),
                flat: true);
        }

        private static void DrawResolvedModuleTypeLabel(Rect rect, Type resolvedType)
        {
            if (resolvedType == null)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    GUI.tooltip = StartupResolvedHeader.tooltip;
                    EditorGUI.LabelField(rect, "<unresolved>", EditorStyles.centeredGreyMiniLabel);
                    GUI.tooltip = string.Empty;
                }
                return;
            }

            s_startupResolvedLinkStyle ??= new GUIStyle(EditorStyles.linkLabel) { alignment = TextAnchor.MiddleLeft };

            if (GUI.Button(rect, new GUIContent(resolvedType.Name, StartupResolvedHeader.tooltip), s_startupResolvedLinkStyle))
            {
                MonoScriptCache.TryOpenScript(resolvedType);
            }
        }

        private const float CatalogFoldoutWidth = 14f;
        private const float CatalogEditButtonWidth = 26f;
        private const float CatalogCountWidth = 30f;

        private void DrawViewPrefabCatalogColumns(Rect rect)
        {
            s_startupColumnHeaderStyle ??= new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold };

            float catalogWidth = rect.width - CatalogFoldoutWidth - CatalogEditButtonWidth - CatalogCountWidth - 12f;

            float x = rect.x + CatalogFoldoutWidth + 4f;
            EditorGUI.LabelField(new Rect(x, rect.y, catalogWidth - 2f, rect.height), CatalogHeader, s_startupColumnHeaderStyle);
            x += catalogWidth + 4f;
            // Edit button column intentionally has no header label - it's an icon-only action.
            x += CatalogEditButtonWidth + 4f;
            EditorGUI.LabelField(new Rect(x, rect.y, CatalogCountWidth, rect.height), CatalogCountHeader, s_startupColumnHeaderStyle);
        }

        private void DrawViewPrefabCatalogRow(Rect rect, SerializedProperty element)
        {
            var baseRect = new Rect(rect.x, rect.y + 2f, rect.width, EditorGUIUtility.singleLineHeight);

            var catalog = element.objectReferenceValue as ViewPrefabCatalog;
            bool isExpanded = catalog != null && _catalogPreviewStates.ContainsKey(catalog);

            var foldoutRect = new Rect(baseRect.x, baseRect.y, CatalogFoldoutWidth, baseRect.height);
            using (new EditorGUI.DisabledScope(catalog == null))
            {
                GUI.tooltip = "Expand to view and edit this catalog's mediator prefabs inline.";
                bool newExpanded = EditorGUI.Foldout(foldoutRect, isExpanded, GUIContent.none);
                GUI.tooltip = string.Empty;

                if (newExpanded != isExpanded && catalog != null)
                {
                    if (newExpanded) GetOrCreateCatalogPreviewState(catalog);
                    else CollapseCatalogPreview(catalog);
                    isExpanded = newExpanded;
                }
            }

            float catalogWidth = baseRect.width - CatalogFoldoutWidth - CatalogEditButtonWidth - CatalogCountWidth - 12f;
            var catalogRect = new Rect(foldoutRect.xMax + 4f, baseRect.y, catalogWidth - 4f, baseRect.height);
            GUI.tooltip = CatalogHeader.tooltip;
            EditorGUI.PropertyField(catalogRect, element, GUIContent.none);
            GUI.tooltip = string.Empty;

            var newCatalog = element.objectReferenceValue as ViewPrefabCatalog;
            if (newCatalog != catalog && catalog != null)
            {
                CollapseCatalogPreview(catalog);
                isExpanded = false;
            }

            var editRect = new Rect(catalogRect.xMax + 4f, baseRect.y, CatalogEditButtonWidth, baseRect.height);
            DrawEditCatalogButton(editRect, newCatalog);

            var countRect = new Rect(editRect.xMax + 4f, baseRect.y, CatalogCountWidth, baseRect.height);
            s_catalogCountStyle ??= new GUIStyle(EditorStyles.miniBoldLabel) { alignment = TextAnchor.MiddleCenter };
            GUI.tooltip = CatalogCountHeader.tooltip;
            var countText = newCatalog != null ? (newCatalog.MediatorPrefabs?.Length ?? 0).ToString() : "-";
            EditorGUI.LabelField(countRect, countText, s_catalogCountStyle);
            GUI.tooltip = string.Empty;

            if (isExpanded && newCatalog != null && _catalogPreviewStates.TryGetValue(newCatalog, out var state))
            {
                var panelRect = new Rect(rect.x, baseRect.yMax + 6f, rect.width, rect.yMax - baseRect.yMax - 6f);
                DrawCatalogPreviewPanel(panelRect, state);
            }
        }

        private CatalogPreviewState GetOrCreateCatalogPreviewState(ViewPrefabCatalog catalog)
        {
            if (_catalogPreviewStates.TryGetValue(catalog, out var existing))
                return existing;

            var so = new SerializedObject(catalog);
            var prop = so.FindProperty("_mediatorPrefabs");
            var state = new CatalogPreviewState { SerializedObject = so, MediatorPrefabsProperty = prop };
            state.List = MvcMediatorPrefabListDrawer.BuildList(so, prop, state.Pager);
            _catalogPreviewStates[catalog] = state;
            return state;
        }

        private void CollapseCatalogPreview(ViewPrefabCatalog catalog)
        {
            _catalogPreviewStates.Remove(catalog);
        }

        private static void DrawCatalogPreviewPanel(Rect rect, CatalogPreviewState state)
        {
            state.SerializedObject.Update();

            GUI.Box(rect, GUIContent.none, EditorStyles.helpBox);

            const float padding = 6f;
            var contentRect = new Rect(rect.x + padding, rect.y + padding, rect.width - (padding * 2f), rect.height - (padding * 2f));

            int totalCount = state.MediatorPrefabsProperty.arraySize;
            state.Pager.ClampToCount(totalCount);

            float listHeight = state.List.GetHeight();
            var listRect = new Rect(contentRect.x, contentRect.y, contentRect.width, listHeight);
            state.List.DoList(listRect);

            float pagerHeight = state.Pager.EstimateControlsHeight(totalCount);
            if (pagerHeight > 0f)
            {
                var pagerRect = new Rect(contentRect.x, listRect.yMax + 4f, contentRect.width, pagerHeight - 4f);
                state.Pager.DrawControls(pagerRect, totalCount);
            }

            state.SerializedObject.ApplyModifiedProperties();
        }

        private static void DrawEditCatalogButton(Rect rect, ViewPrefabCatalog catalog)
        {
            using (new EditorGUI.DisabledScope(catalog == null))
            {
                if (GUI.Button(rect, GetEditCatalogButtonContent()) && catalog != null)
                {
                    EditorUtility.OpenPropertyEditor(catalog);
                }
            }
        }

        private static GUIContent GetEditCatalogButtonContent()
        {
            if (s_editCatalogButtonContent == null)
            {
                var iconName = EditorGUIUtility.isProSkin ? "d_editicon.sml" : "editicon.sml";
                var icon = EditorGUIUtility.IconContent(iconName)?.image;
                s_editCatalogButtonContent = icon != null
                    ? new GUIContent(icon, "Open this catalog in its own Inspector window.")
                    : new GUIContent("Edit", "Open this catalog in its own Inspector window.");
            }

            return s_editCatalogButtonContent;
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

                var prefabProperty = entry.FindPropertyRelative("ModulePrefab");
                var prefab = prefabProperty != null ? prefabProperty.objectReferenceValue as GameObject : null;

                if (prefab != null && prefab.GetComponent<MvcModule>() == null)
                {
                    EditorGUILayout.HelpBox($"Startup module at position {i}: assigned prefab '{prefab.name}' has no MvcModule component on its root.", MessageType.Error);
                    continue;
                }

                var moduleType = ResolveStartupModuleType(entry);
                if (moduleType == null)
                {
                    EditorGUILayout.HelpBox($"Startup module at position {i}: could not resolve a module type. Assign a Module Script or Module Prefab.", MessageType.Error);
                    continue;
                }

                if (!typeof(MvcModule).IsAssignableFrom(moduleType) || moduleType.IsAbstract)
                {
                    EditorGUILayout.HelpBox($"Startup module at position {i}: '{moduleType.Name}' is not a module - it must be a concrete class derived from MvcModule.", MessageType.Error);
                    continue;
                }

                if (!seen.Add(moduleType))
                {
                    EditorGUILayout.HelpBox($"Startup module at position {i}: '{moduleType.Name}' is listed more than once.", MessageType.Error);
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
                    EditorGUILayout.HelpBox($"View prefab catalog at position {i} is empty.", MessageType.Warning);
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

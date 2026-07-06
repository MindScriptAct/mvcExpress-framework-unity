using mvcExpress.Editor.Core;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace mvcExpress.Editor.Inspectors
{
    /// <summary>
    /// Custom inspector for MediatorRegistryBehaviour; draws a distinguishing icon for it in the Hierarchy window.
    /// </summary>
    [CustomEditor(typeof(MediatorRegistryBehaviour))]
    public sealed class MediatorRegistryBehaviourEditor : UnityEditor.Editor
    {
        private const string HeaderIconPath = "Packages/org.mvcexpress.core/Editor/Icons/mvc_mediator_registry_icon.png";
        private const string HeaderTitle = "Mediator registry";

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
            if (!MvcHierarchyUtils.ShowRegistryIcons) return;
            if (s_hierarchyIcon == null) return;
#if UNITY_6000_4_OR_NEWER
            var obj = EditorUtility.EntityIdToObject(entityId) as GameObject;
#else
            var obj = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
#endif
            if (obj == null || obj.GetComponent<MediatorRegistryBehaviour>() == null) return;
            GUI.DrawTexture(new Rect(MvcHierarchyUtils.GetRightEdge(selectionRect) - 16, selectionRect.y, 16, 16), s_hierarchyIcon, ScaleMode.ScaleToFit);
        }

        private Texture2D headerIcon;

        private SerializedProperty sceneMediatorsProperty;
        private SerializedProperty mediatorPrefabsProperty;
        private SerializedProperty viewContainerProperty;

        private ReorderableList sceneMediatorsList;
        private ReorderableList prefabMappingsList;

        // Runtime mediators tracking
        private List<MediatorBehaviour> runtimeMediators = new List<MediatorBehaviour>();
        private bool wasPlaying;

        // Static cache for type/script lookups - shared across all editor instances
        private static Dictionary<string, Type> s_typeCache = new Dictionary<string, Type>();
        private static Dictionary<Type, MonoScript> s_scriptCache = new Dictionary<Type, MonoScript>();

        // Reusable dictionaries to avoid allocations in debug structure methods
        private static readonly Dictionary<string, SerializedProperty> s_tempMappingByTypeName = new Dictionary<string, SerializedProperty>(StringComparer.Ordinal);
        private static readonly Dictionary<string, Transform> s_tempSpawned = new Dictionary<string, Transform>(StringComparer.Ordinal);
        private static readonly Dictionary<string, List<SerializedProperty>> s_tempChildren = new Dictionary<string, List<SerializedProperty>>(StringComparer.Ordinal);

        [InitializeOnLoadMethod]
        private static void InitTypeCacheInvalidation()
        {
            AssemblyReloadEvents.afterAssemblyReload -= ClearTypeCaches;
            AssemblyReloadEvents.afterAssemblyReload += ClearTypeCaches;
        }

        private static void ClearTypeCaches()
        {
            s_typeCache.Clear();
            s_scriptCache.Clear();
        }

        // Reusable list to avoid allocations in FindAndAdd methods
        private static readonly List<MediatorBehaviour> s_tempMediatorList = new List<MediatorBehaviour>();

        private static readonly GUIContent MediatorHeader = new GUIContent("Mediator", "Detected mediator component type on prefab root.");
        private static readonly GUIContent PrefabHeader = new GUIContent("Prefab", "Prefab/Prefab Variant containing mediator on its root GameObject.");
        private static readonly GUIContent DebugHeader = new GUIContent("D?", "Add debug instance of prefab into stage. (its only for debugging. It will be removed on application start)");

        private static readonly GUIContent DebugStructureToggleShow = new GUIContent("Show structure", "Show/edit DebugParentMediatorTypeName and DebugParentContainerPath per mapping.");
        private static readonly GUIContent DebugStructureToggleHide = new GUIContent("Hide structure", "Hide DebugParentMediatorTypeName and DebugParentContainerPath per mapping.");

        private const string PrefShowDebugStructureKey = "mvcExpress.MediatorRegistryBehaviourEditor.ShowDebugStructure";
        private bool showDebugStructure;

        private static readonly GUIContent SaveDebugStructureButton = new GUIContent(
            "Save Structure",
            "Auto-detect debug structure from current scene hierarchy.\n\n" +
            "- If a mediator has a parent mediator: store parent type + container path under that parent.\n" +
            "- If no parent mediator and not under View root: store ViewPath under View root.");

        private static GUIStyle sceneMediatorsHeaderTitleStyle;
        private static GUIStyle sceneMediatorsHeaderButtonStyle;
        private static GUIStyle prefabMappingsHeaderTitleStyle;
        private static GUIStyle prefabMappingsHeaderButtonStyle;
        private static GUIStyle s_columnHeaderStyle;
        private static GUIStyle s_columnCenterStyle;

        private const string PrefVerboseDebugKey = "mvcExpress.MediatorRegistryBehaviourEditor.Verbose";
        private static bool VerboseDebug
        {
            get
            {
                return EditorPrefs.GetBool(PrefVerboseDebugKey, false);
            }
        }

        private void LogAlways(string message)
        {
        }

        private void LogWarningAlways(string message)
        {
        }

        private void DumpRegistryContext(MediatorRegistryBehaviour registry, string title)
        {
        }

        private void DumpMapKeys(Dictionary<string, SerializedProperty> map, string title, int max = 50)
        {
        }

        private static MediatorBehaviour FindMediatorInstanceByTypeNameInScene(MediatorRegistryBehaviour registry, string mediatorTypeName)
        {
            if (registry == null || string.IsNullOrEmpty(mediatorTypeName))
                return null;

            var norm = NormalizeAssemblyQualifiedTypeName(mediatorTypeName);

            var mediators = registry.GetComponentsInChildren<MediatorBehaviour>(true);
            for (int i = 0; i < mediators.Length; i++)
            {
                var m = mediators[i];
                if (m == null) continue;

                var aqn = m.GetType().AssemblyQualifiedName;
                if (string.Equals(aqn, mediatorTypeName, StringComparison.Ordinal))
                    return m;

                if (string.Equals(NormalizeAssemblyQualifiedTypeName(aqn), norm, StringComparison.Ordinal))
                    return m;
            }

            return null;
        }

        private static string TypeLabel(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return "<empty>";

            var comma = typeName.IndexOf(',');
            return comma >= 0 ? typeName.Substring(0, comma) : typeName;
        }

        private static string NormalizeAssemblyQualifiedTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return string.Empty;

            var parts = typeName.Split(',');
            if (parts.Length >= 2)
                return $"{parts[0].Trim()}, {parts[1].Trim()}";

            return typeName.Trim();
        }

        private static string ToFriendlyTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return string.Empty;

            var commaIdx = typeName.IndexOf(',');
            var trimmed = commaIdx >= 0 ? typeName.Substring(0, commaIdx) : typeName;

            var dotIdx = trimmed.LastIndexOf('.');
            if (dotIdx >= 0 && dotIdx < trimmed.Length - 1)
                return trimmed.Substring(dotIdx + 1);

            return trimmed;
        }

        private static string NormalizeTypeName(string userInput)
        {
            if (string.IsNullOrWhiteSpace(userInput))
                return string.Empty;

            if (userInput.IndexOf(',') >= 0)
                return userInput.Trim();

            var t = Type.GetType(userInput.Trim(), throwOnError: false);
            if (t != null)
                return t.AssemblyQualifiedName;

            var shortName = userInput.Trim();
            try
            {
                var asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    Type[] types;
                    try
                    {
                        types = asms[i].GetTypes();
                    }
                    catch (ReflectionTypeLoadException ex)
                    {
                        types = ex.Types;
                    }

                    if (types == null) continue;

                    for (int j = 0; j < types.Length; j++)
                    {
                        var tt = types[j];
                        if (tt == null) continue;
                        if (tt.Name == shortName)
                            return tt.AssemblyQualifiedName;
                    }
                }
            }
            catch
            {
            }

            return userInput.Trim();
        }

        private static void AddMappingKey(Dictionary<string, SerializedProperty> map, string typeName, SerializedProperty el)
        {
            if (map == null || el == null || string.IsNullOrEmpty(typeName))
                return;

            map[typeName] = el;

            var norm = NormalizeAssemblyQualifiedTypeName(typeName);
            if (!string.IsNullOrEmpty(norm) && !map.ContainsKey(norm))
                map[norm] = el;
        }

        private static bool TryGetMappingElementByTypeName(
            Dictionary<string, SerializedProperty> mappingByTypeName,
            string mediatorTypeName,
            out SerializedProperty mappingEl)
        {
            mappingEl = null;
            if (mappingByTypeName == null || string.IsNullOrWhiteSpace(mediatorTypeName))
                return false;

            if (mappingByTypeName.TryGetValue(mediatorTypeName, out mappingEl))
                return true;

            var norm = NormalizeAssemblyQualifiedTypeName(mediatorTypeName);
            if (mappingByTypeName.TryGetValue(norm, out mappingEl))
                return true;

            return false;
        }

        private void DumpAllMappings(string title)
        {
            if (!VerboseDebug)
                return;

            // Removed Debug.Log output.
            // Intentionally keep method for future diagnostics.
        }

        private void DumpSceneMediators(MediatorRegistryBehaviour registry, string title)
        {
            if (!VerboseDebug || registry == null)
                return;

            // Removed Debug.Log output.
            // Intentionally keep method for future diagnostics.
        }

        private void LogMappingDiagnostics(string requestedTypeName, Dictionary<string, SerializedProperty> map)
        {
            if (!VerboseDebug)
                return;

            var norm = NormalizeAssemblyQualifiedTypeName(requestedTypeName);
            var reqType = Type.GetType(requestedTypeName, throwOnError: false) ?? Type.GetType(norm, throwOnError: false);

            Debug.LogWarning(
                $"[MediatorRegistryEditor][Verbose] FAILED to resolve mapping.\n" +
                $"  requested: '{requestedTypeName}'\n" +
                $"  normalized: '{norm}'\n" +
                $"  requestedTypeResolved: {(reqType != null ? reqType.AssemblyQualifiedName : "<null>")}\n" +
                $"  mapKeyCount: {(map != null ? map.Count : 0)}");

            if (mediatorPrefabsProperty == null)
            {
                Debug.LogWarning("[MediatorRegistryEditor][Verbose] mediatorPrefabsProperty is null.");
                return;
            }

            Debug.LogWarning($"[MediatorRegistryEditor][Verbose] _mediatorPrefabs size = {mediatorPrefabsProperty.arraySize}");

            for (int i = 0; i < mediatorPrefabsProperty.arraySize; i++)
            {
                var el = mediatorPrefabsProperty.GetArrayElementAtIndex(i);
                var tn = el.FindPropertyRelative("MediatorTypeName")?.stringValue ?? string.Empty;
                var tnNorm = NormalizeAssemblyQualifiedTypeName(tn);
                var t = Type.GetType(tn, throwOnError: false) ?? Type.GetType(tnNorm, throwOnError: false);

                bool normEq = string.Equals(tnNorm, norm, StringComparison.Ordinal);
                bool typeEq = (reqType != null && t != null && t == reqType);

                Debug.LogWarning(
                    $"[MediatorRegistryEditor][Verbose] mapping[{i}]\n" +
                    $"  stored: '{tn}'\n" +
                    $"  storedNorm: '{tnNorm}'\n" +
                    $"  typeResolved: {(t != null ? t.AssemblyQualifiedName : "<null>")}\n" +
                    $"  normEqualsRequested: {normEq}\n" +
                    $"  typeEqualsRequested: {typeEq}");
            }
        }

        private static IEnumerable<string> GetFirstKeys(Dictionary<string, SerializedProperty> map, int max)
        {
            int i = 0;
            foreach (var k in map.Keys)
            {
                yield return TypeLabel(k);
                i++;
                if (i >= max) yield break;
            }
        }

        private bool TryResolveMapping(string mediatorTypeName, Dictionary<string, SerializedProperty> map, out SerializedProperty mappingEl)
        {
            mappingEl = null;
            if (string.IsNullOrEmpty(mediatorTypeName) || map == null)
                return false;

            if (map.TryGetValue(mediatorTypeName, out mappingEl))
                return true;

            var normKey = NormalizeAssemblyQualifiedTypeName(mediatorTypeName);
            if (!string.IsNullOrEmpty(normKey) && map.TryGetValue(normKey, out mappingEl))
                return true;

            var t = Type.GetType(mediatorTypeName, throwOnError: false);
            if (t == null && !string.IsNullOrEmpty(normKey))
                t = Type.GetType(normKey, throwOnError: false);

            if (t != null && mediatorPrefabsProperty != null)
            {
                for (int i = 0; i < mediatorPrefabsProperty.arraySize; i++)
                {
                    var el = mediatorPrefabsProperty.GetArrayElementAtIndex(i);
                    var tn = el.FindPropertyRelative("MediatorTypeName")?.stringValue;
                    if (string.IsNullOrEmpty(tn))
                        continue;

                    var tt = Type.GetType(tn, throwOnError: false);
                    if (tt == null)
                        tt = Type.GetType(NormalizeAssemblyQualifiedTypeName(tn), throwOnError: false);

                    if (tt != null && tt == t)
                    {
                        mappingEl = el;
                        map[mediatorTypeName] = el;
                        if (!string.IsNullOrEmpty(normKey) && !map.ContainsKey(normKey))
                            map[normKey] = el;
                        return true;
                    }
                }
            }

            LogMappingDiagnostics(mediatorTypeName, map);

            if (VerboseDebug)
            {
                Debug.LogWarning(
                    $"[MediatorRegistryEditor][Verbose] Mapping not found for '{mediatorTypeName}'. " +
                    $"norm='{normKey}'. mappedKeys={map.Count}. " +
                    $"examples=[{string.Join(", ", GetFirstKeys(map, 6))}]");
            }

            return false;
        }

        private void OnEnable()
        {
            showDebugStructure = EditorPrefs.GetBool(PrefShowDebugStructureKey, false);

            headerIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(HeaderIconPath);

            sceneMediatorsProperty = serializedObject.FindProperty("_sceneMediators");
            mediatorPrefabsProperty = serializedObject.FindProperty("_mediatorPrefabs");
            viewContainerProperty = serializedObject.FindProperty("_viewContainer");

            sceneMediatorsList = new ReorderableList(serializedObject, sceneMediatorsProperty, draggable: true, displayHeader: false, displayAddButton: true, displayRemoveButton: true);
            sceneMediatorsList.onAddCallback = list =>
            {
                serializedObject.Update();
                int idx = sceneMediatorsProperty.arraySize;
                sceneMediatorsProperty.InsertArrayElementAtIndex(idx);
                sceneMediatorsProperty.GetArrayElementAtIndex(idx).objectReferenceValue = null;
                serializedObject.ApplyModifiedProperties();
            };
            sceneMediatorsList.drawElementCallback = (rect, index, active, focused) =>
            {
                rect.y += 1f;
                rect.height = EditorGUIUtility.singleLineHeight;
                EditorGUI.PropertyField(rect, sceneMediatorsProperty.GetArrayElementAtIndex(index), GUIContent.none);
            };
            sceneMediatorsList.elementHeight = EditorGUIUtility.singleLineHeight + 6f;

            prefabMappingsList = new ReorderableList(serializedObject, mediatorPrefabsProperty, draggable: true, displayHeader: true, displayAddButton: true, displayRemoveButton: true);
            prefabMappingsList.onAddCallback = list =>
            {
                serializedObject.Update();
                int idx = mediatorPrefabsProperty.arraySize;
                mediatorPrefabsProperty.InsertArrayElementAtIndex(idx);
                var el = mediatorPrefabsProperty.GetArrayElementAtIndex(idx);
                el.FindPropertyRelative("MediatorTypeName").stringValue = string.Empty;
                el.FindPropertyRelative("Prefab").objectReferenceValue = null;
                el.FindPropertyRelative("AddDebugInstance").boolValue = false;
                serializedObject.ApplyModifiedProperties();
            };

            prefabMappingsList.headerHeight = EditorGUIUtility.singleLineHeight;
            prefabMappingsList.drawHeaderCallback = DrawPrefabHeaderRow;
            prefabMappingsList.drawElementCallback = (rect, index, active, focused) =>
            {
                DrawPrefabRow(rect, mediatorPrefabsProperty.GetArrayElementAtIndex(index));
            };
            prefabMappingsList.elementHeight = showDebugStructure
                ? (EditorGUIUtility.singleLineHeight * 2f + 10f)
                : (EditorGUIUtility.singleLineHeight + 8f);

            EditorApplication.update += OnEditorUpdate;
            wasPlaying = Application.isPlaying;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;

            // Clean up ReorderableLists to prevent memory leaks
            if (sceneMediatorsList != null)
            {
                sceneMediatorsList.drawElementCallback = null;
                sceneMediatorsList.onAddCallback = null;
                sceneMediatorsList = null;
            }

            if (prefabMappingsList != null)
            {
                prefabMappingsList.drawElementCallback = null;
                prefabMappingsList.drawHeaderCallback = null;
                prefabMappingsList.onAddCallback = null;
                prefabMappingsList = null;
            }

            // Clear cached data
            sceneMediatorsProperty = null;
            mediatorPrefabsProperty = null;
            headerIcon = null;
            runtimeMediators.Clear();
        }

        private void OnEditorUpdate()
        {
            if (!Application.isPlaying)
            {
                if (wasPlaying)
                {
                    runtimeMediators.Clear();
                    wasPlaying = false;
                    Repaint();
                }
                return;
            }

            if (!wasPlaying)
            {
                wasPlaying = true;
            }

            UpdateRuntimeMediators();
            Repaint();
        }

        private void UpdateRuntimeMediators()
        {
            var registry = target as MediatorRegistryBehaviour;
            if (registry == null)
            {
                runtimeMediators.Clear();
                return;
            }

            var module = registry.GetComponentInParent<MvcModule>();
            if (module == null)
            {
                runtimeMediators.Clear();
                return;
            }

            runtimeMediators.Clear();

            try
            {
                var initializerField = typeof(MvcModule).GetField("_initializer", BindingFlags.NonPublic | BindingFlags.Instance);
                if (initializerField == null) return;

                var initializer = initializerField.GetValue(module);
                if (initializer == null) return;

                var registrarProp = initializer.GetType().GetProperty("MediatorRegistrar", BindingFlags.Public | BindingFlags.Instance);
                if (registrarProp == null) return;

                var registrar = registrarProp.GetValue(initializer);
                if (registrar == null) return;

                var registeredProp = registrar.GetType().GetProperty("RegisteredMediators", BindingFlags.Public | BindingFlags.Instance);
                var runtimeProp = registrar.GetType().GetProperty("RuntimeMediators", BindingFlags.Public | BindingFlags.Instance);

                if (registeredProp != null)
                {
                    var registered = registeredProp.GetValue(registrar) as System.Collections.IEnumerable;
                    if (registered != null)
                    {
                        foreach (var m in registered)
                        {
                            var mediator = m as MediatorBehaviour;
                            if (mediator != null && mediator != null)
                                runtimeMediators.Add(mediator);
                        }
                    }
                }

                if (runtimeProp != null)
                {
                    var runtime = runtimeProp.GetValue(registrar) as System.Collections.IEnumerable;
                    if (runtime != null)
                    {
                        foreach (var m in runtime)
                        {
                            var mediator = m as MediatorBehaviour;
                            if (mediator != null && !runtimeMediators.Contains(mediator))
                                runtimeMediators.Add(mediator);
                        }
                    }
                }
            }
            catch
            {
                // Silently fail if reflection doesn't work
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawTopHeader();

            DrawViewContainerSection();

            DrawRuntimeMediatorsSection();

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                DrawSceneMediatorsHeader();
                sceneMediatorsList?.DoLayoutList();
                EditorGUILayout.Space();

                var registry = target as MediatorRegistryBehaviour;
                if (registry != null)
                    CleanupMissingDebugInstances(registry);

                DrawPrefabMappingsHeader();
                prefabMappingsList?.DoLayoutList();
                DrawPrefabMappingsFooterButtons();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private static GUIStyle s_runtimeHeaderTitleStyle;
        private static GUIStyle s_runtimeColumnHeaderStyle;
        private static GUIStyle s_runtimeHelpBoxStyle;

        private static readonly GUIContent RuntimeMediatorsHeaderTitle = new GUIContent("Runtime Attached Mediators", "Currently attached mediators in play mode");
        private static readonly GUIContent RuntimeMediatorTypeHeader = new GUIContent("Mediator Type", "Type of the mediator - double-click to open script");
        private static readonly GUIContent RuntimeGameObjectHeader = new GUIContent("GameObject", "GameObject this mediator is attached to - click to select in hierarchy");

        private void DrawRuntimeMediatorsSection()
        {
            s_runtimeHeaderTitleStyle ??= new GUIStyle(MvcEditorUtility.SectionHeaderTitleStyle)
            {
                fontSize = Mathf.Max(MvcEditorUtility.SectionHeaderTitleStyle.fontSize + 2, 14),
                alignment = TextAnchor.MiddleLeft
            };

            float lineH = EditorGUIUtility.singleLineHeight;
            float headerH = (lineH * 2f) + (6f * 2f);

            var content = MvcEditorUtility.DrawHeaderBox(headerH, padX: 8f, padY: 6f);
            var titleRect = new Rect(content.x, content.center.y - (lineH * 0.5f), content.width, lineH);
            EditorGUI.LabelField(titleRect, $"Runtime Attached Mediators ({runtimeMediators.Count})", s_runtimeHeaderTitleStyle);

            EditorGUILayout.Space(2f);

            if (!Application.isPlaying)
            {
                s_runtimeHelpBoxStyle ??= new GUIStyle(EditorStyles.helpBox)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(10, 10, 10, 10)
                };

                EditorGUILayout.LabelField("Run the application to see list of attached mediators", s_runtimeHelpBoxStyle, GUILayout.Height(40f));
            }
            else if (runtimeMediators.Count == 0)
            {
                s_runtimeHelpBoxStyle ??= new GUIStyle(EditorStyles.helpBox)
                {
                    fontSize = 12,
                    alignment = TextAnchor.MiddleCenter,
                    padding = new RectOffset(10, 10, 10, 10)
                };

                EditorGUILayout.LabelField("No mediators attached yet", s_runtimeHelpBoxStyle, GUILayout.Height(40f));
            }
            else
            {
                DrawRuntimeMediatorsTable();
            }

            EditorGUILayout.Space();
        }

        private void DrawRuntimeMediatorsTable()
        {
            s_runtimeColumnHeaderStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                var headerRect = GUILayoutUtility.GetRect(0, EditorGUIUtility.singleLineHeight);
                float typeWidth = headerRect.width * 0.5f;
                float gameObjectWidth = headerRect.width * 0.5f;

                var typeHeaderRect = new Rect(headerRect.x, headerRect.y, typeWidth, headerRect.height);
                var goHeaderRect = new Rect(headerRect.x + typeWidth, headerRect.y, gameObjectWidth, headerRect.height);

                EditorGUI.LabelField(typeHeaderRect, RuntimeMediatorTypeHeader, s_runtimeColumnHeaderStyle);
                EditorGUI.LabelField(goHeaderRect, RuntimeGameObjectHeader, s_runtimeColumnHeaderStyle);

                EditorGUILayout.Space(2f);

                for (int i = 0; i < runtimeMediators.Count; i++)
                {
                    var mediator = runtimeMediators[i];
                    if (mediator == null) continue;

                    DrawRuntimeMediatorRow(mediator, typeWidth, gameObjectWidth);
                }
            }
        }

        private void DrawRuntimeMediatorRow(MediatorBehaviour mediator, float typeWidth, float gameObjectWidth)
        {
            var rowRect = GUILayoutUtility.GetRect(0, EditorGUIUtility.singleLineHeight + 2f);
            
            var typeRect = new Rect(rowRect.x, rowRect.y, typeWidth - 2f, EditorGUIUtility.singleLineHeight);
            var goRect = new Rect(rowRect.x + typeWidth, rowRect.y, gameObjectWidth - 2f, EditorGUIUtility.singleLineHeight);

            var mediatorType = mediator.GetType();
            var script = GetCachedMonoScript(mediatorType);

            using (new EditorGUI.DisabledScope(true))
            {
                if (script != null)
                {
                    EditorGUI.ObjectField(typeRect, script, typeof(MonoScript), false);
                }
                else
                {
                    var typeName = ToFriendlyTypeName(mediatorType.AssemblyQualifiedName);
                    EditorGUI.TextField(typeRect, typeName);
                }
            }

            HandleTypeNavigation(typeRect, mediatorType);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.ObjectField(goRect, mediator.gameObject, typeof(GameObject), true);
            }

            HandleGameObjectSelection(goRect, mediator.gameObject);
        }

        private void HandleGameObjectSelection(Rect rect, GameObject go)
        {
            var e = Event.current;
            if (e == null || go == null)
                return;

            if (!rect.Contains(e.mousePosition))
                return;

            if (e.type == EventType.MouseDown && e.button == 0)
            {
                Selection.activeGameObject = go;
                EditorGUIUtility.PingObject(go);
                e.Use();
            }
        }

        private void DrawTopHeader()
        {
            EditorGUILayout.Space(2f);

            using (new EditorGUILayout.HorizontalScope())
            {
                var iconSizeX = MvcEditorUtility.TopHeaderIconWidth;
                var iconSizeY = MvcEditorUtility.TopHeaderIconHeight;
                var iconRect = GUILayoutUtility.GetRect(iconSizeX, iconSizeY, GUILayout.Width(iconSizeX), GUILayout.Height(iconSizeY));

                if (headerIcon != null)
                    GUI.DrawTexture(iconRect, headerIcon, ScaleMode.ScaleToFit);

                GUILayout.Space(10f);
                GUILayout.Label(HeaderTitle, MvcEditorUtility.TopHeaderTitleStyle, GUILayout.Height(iconSizeY));
            }

            EditorGUILayout.Space(6f);
        }

        private static GUIStyle s_viewContainerHeaderTitleStyle;
        private static readonly GUIContent ViewContainerHeaderTitle = new GUIContent("View Container", "Default container where mediators are instantiated");

        private void DrawViewContainerSection()
        {
            s_viewContainerHeaderTitleStyle ??= new GUIStyle(MvcEditorUtility.SectionHeaderTitleStyle)
            {
                fontSize = Mathf.Max(MvcEditorUtility.SectionHeaderTitleStyle.fontSize + 2, 14),
                alignment = TextAnchor.MiddleLeft
            };

            float lineH = EditorGUIUtility.singleLineHeight;
            float headerH = (lineH * 2f) + (6f * 2f);

            var content = MvcEditorUtility.DrawHeaderBox(headerH, padX: 8f, padY: 6f);
            var titleRect = new Rect(content.x, content.center.y - (lineH * 0.5f), content.width, lineH);
            EditorGUI.LabelField(titleRect, ViewContainerHeaderTitle, s_viewContainerHeaderTitleStyle);

            EditorGUILayout.Space(2f);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    const float clearButtonWidth = 50f;
                    
                    var registry = target as MediatorRegistryBehaviour;
                    
                    EditorGUI.BeginChangeCheck();
                    var newContainer = EditorGUILayout.ObjectField(
                        "Container",
                        viewContainerProperty.objectReferenceValue,
                        typeof(Transform),
                        true,
                        GUILayout.ExpandWidth(true));
                    
                    if (EditorGUI.EndChangeCheck())
                    {
                        // Validate: if null, reset to self
                        if (newContainer == null && registry != null)
                        {
                            newContainer = registry.transform;
                        }
                        
                        viewContainerProperty.objectReferenceValue = newContainer;
                    }
                    
                    // Clear button - resets to self
                    if (GUILayout.Button("Clear", GUILayout.Width(clearButtonWidth)))
                    {
                        if (registry != null)
                        {
                            viewContainerProperty.objectReferenceValue = registry.transform;
                        }
                    }
                }
                
                // Validate on every frame - ensure it's never null
                var registry2 = target as MediatorRegistryBehaviour;
                if (registry2 != null && viewContainerProperty.objectReferenceValue == null)
                {
                    viewContainerProperty.objectReferenceValue = registry2.transform;
                }
                
                // Show info about current container
                EditorGUILayout.HelpBox(
                    "Default container where mediators are instantiated. " +
                    "Defaults to this GameObject (View) if not set. " +
                    "Click 'Clear' to reset to default.",
                    MessageType.Info);
            }

            EditorGUILayout.Space();
        }

        private void DrawSceneMediatorsHeader()
        {
            sceneMediatorsHeaderTitleStyle ??= new GUIStyle(MvcEditorUtility.SectionHeaderTitleStyle)
            {
                fontSize = Mathf.Max(MvcEditorUtility.SectionHeaderTitleStyle.fontSize + 2, 14),
                alignment = TextAnchor.MiddleLeft
            };

            sceneMediatorsHeaderButtonStyle ??= new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.Max(GUI.skin.button.fontSize + 1, 12)
            };

            const float btnWidth = 140f;
            const float gap = 8f;

            float lineH = EditorGUIUtility.singleLineHeight;
            float headerH = (lineH * 2f) + (6f * 2f);

            var content = MvcEditorUtility.DrawHeaderBox(headerH, padX: 8f, padY: 6f);

            float btnH = lineH * 1.6f;
            var btnRect = new Rect(content.xMax - btnWidth, content.center.y - (btnH * 0.5f), btnWidth, btnH);

            var titleRect = new Rect(content.x, content.y, content.width - btnWidth - gap, content.height);
            var line1 = new Rect(titleRect.x, titleRect.center.y - (lineH * 0.5f), titleRect.width, lineH);

            EditorGUI.LabelField(line1, $"Scene Mediators ({sceneMediatorsProperty.arraySize})", sceneMediatorsHeaderTitleStyle);

            if (GUI.Button(btnRect, "Find & Add All", sceneMediatorsHeaderButtonStyle))
                FindAndAddMediators();

            EditorGUILayout.Space(2f);
        }

        private void DrawPrefabMappingsHeader()
        {
            prefabMappingsHeaderTitleStyle ??= new GUIStyle(MvcEditorUtility.SectionHeaderTitleStyle) { alignment = TextAnchor.MiddleLeft };
            prefabMappingsHeaderButtonStyle ??= new GUIStyle(GUI.skin.button) { fontSize = Mathf.Max(GUI.skin.button.fontSize + 1, 12) };

            float lineH = EditorGUIUtility.singleLineHeight;
            float headerH = (lineH * 2f) + (6f * 2f);

            var content = MvcEditorUtility.DrawHeaderBox(headerH, padX: 8f, padY: 6f);

            const float btnWidth = 140f;
            const float gap = 8f;

            float btnH = lineH * 1.6f;
            var btn2Rect = new Rect(content.xMax - btnWidth, content.center.y - (btnH * 0.5f), btnWidth, btnH);
            var btn1Rect = btn2Rect;
            btn1Rect.x -= (btnWidth + 8f);

            var titleRect = new Rect(content.x, content.y, content.width - (btnWidth * 2f) - (gap * 2f) - 8f, content.height);
            var titleLine = new Rect(titleRect.x, titleRect.center.y - (lineH * 0.5f), titleRect.width, lineH);
            EditorGUI.LabelField(titleLine, $"Mediator Prefabs ({mediatorPrefabsProperty.arraySize})", prefabMappingsHeaderTitleStyle);

            var saveBtnContent = new GUIContent("Save Structure", SaveDebugStructureButton.tooltip);
            var toggleBtnContent = showDebugStructure
                ? new GUIContent("Hide structure", DebugStructureToggleHide.tooltip)
                : new GUIContent("Show structure", DebugStructureToggleShow.tooltip);

            var prev = GUI.enabled;
            GUI.enabled = !Application.isPlaying;

            if (GUI.Button(btn1Rect, saveBtnContent, prefabMappingsHeaderButtonStyle))
                SaveDebugStructureFromScene();

            if (GUI.Button(btn2Rect, toggleBtnContent, prefabMappingsHeaderButtonStyle))
            {
                showDebugStructure = !showDebugStructure;
                EditorPrefs.SetBool(PrefShowDebugStructureKey, showDebugStructure);
                prefabMappingsList.elementHeight = showDebugStructure
                    ? (EditorGUIUtility.singleLineHeight * 2f + 10f)
                    : (EditorGUIUtility.singleLineHeight + 8f);
                Repaint();
            }

            GUI.enabled = prev;
            EditorGUILayout.Space(2f);
        }

        private void DrawPrefabMappingsFooterButtons()
        {
            EditorGUILayout.Space(2f);

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    if (GUILayout.Button("Debug Add All", GUILayout.Width(140f)))
                        BulkSetDebugForAll(true);

                    if (GUILayout.Button("Debug Remove All", GUILayout.Width(140f)))
                        BulkSetDebugForAll(false);

                    var prev = VerboseDebug;
                    var next = GUILayout.Toggle(prev, "Verbose", GUILayout.Width(80f));
                    if (prev != next)
                        EditorPrefs.SetBool(PrefVerboseDebugKey, next);
                }

                GUILayout.FlexibleSpace();
            }
        }

        private void BulkSetDebugForAll(bool enable)
        {
            var registry = target as MediatorRegistryBehaviour;
            if (registry == null)
                return;

            serializedObject.Update();

            // Reuse static dictionary
            s_tempMappingByTypeName.Clear();
            for (int i = 0; i < mediatorPrefabsProperty.arraySize; i++)
            {
                var el = mediatorPrefabsProperty.GetArrayElementAtIndex(i);
                var tn = el.FindPropertyRelative("MediatorTypeName")?.stringValue;
                if (!string.IsNullOrEmpty(tn))
                    AddMappingKey(s_tempMappingByTypeName, tn, el);
            }

            // Reuse static dictionary
            s_tempSpawned.Clear();
            bool changed = false;

            if (enable)
            {
                for (int i = 0; i < mediatorPrefabsProperty.arraySize; i++)
                {
                    var el = mediatorPrefabsProperty.GetArrayElementAtIndex(i);
                    var tnProp = el.FindPropertyRelative("MediatorTypeName");
                    var dbgProp = el.FindPropertyRelative("AddDebugInstance");
                    var prefabProp = el.FindPropertyRelative("Prefab");

                    if (tnProp == null || dbgProp == null || prefabProp == null)
                        continue;

                    if (prefabProp.objectReferenceValue == null)
                        continue;

                    var tn = tnProp.stringValue;
                    if (string.IsNullOrEmpty(tn))
                        continue;

                    if (!dbgProp.boolValue)
                    {
                        dbgProp.boolValue = true;
                        changed = true;
                    }

                    EnsureParentsChecked(tn);
                    EnsureSpawnedRecursive(registry, tn, s_tempMappingByTypeName, s_tempSpawned);
                }
            }
            else
            {
                for (int i = 0; i < mediatorPrefabsProperty.arraySize; i++)
                {
                    var el = mediatorPrefabsProperty.GetArrayElementAtIndex(i);
                    var dbgProp = el.FindPropertyRelative("AddDebugInstance");
                    if (dbgProp != null && dbgProp.boolValue)
                    {
                        dbgProp.boolValue = false;
                        changed = true;
                    }
                }

                for (int i = 0; i < mediatorPrefabsProperty.arraySize; i++)
                {
                    var tn = mediatorPrefabsProperty.GetArrayElementAtIndex(i).FindPropertyRelative("MediatorTypeName")?.stringValue;
                    if (!string.IsNullOrEmpty(tn))
                    {
                        RemoveDebugInstance(registry, tn);
                    }
                }
            }

            if (changed)
            {
                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(registry);
                Repaint();
            }
        }

        // Reusable list to avoid allocations in FindAndAdd methods
        private void FindAndAddMediators()
        {
            var view = (MediatorRegistryBehaviour)target;
            if (view == null) return;

            s_tempMediatorList.Clear();
            view.GetComponentsInChildren(true, s_tempMediatorList);

            if (s_tempMediatorList.Count == 0)
            {
                EditorUtility.DisplayDialog("Find Mediators", "No MediatorBehaviour components found under this View container.", "OK");
                return;
            }

            Undo.RecordObject(view, "Find & Add Mediators");
            serializedObject.Update();

            for (int i = 0; i < s_tempMediatorList.Count; i++)
            {
                var mb = s_tempMediatorList[i];
                if (mb == null) continue;
                if (ContainsMediator(mb)) continue;

                int insertIndex = sceneMediatorsProperty.arraySize;
                sceneMediatorsProperty.InsertArrayElementAtIndex(insertIndex);
                sceneMediatorsProperty.GetArrayElementAtIndex(insertIndex).objectReferenceValue = mb;
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(view);
        }

        private void DrawPrefabHeaderRow(Rect rect)
        {
            float w = rect.width;
            float debugW = 26f;
            float typeW = w * 0.45f;
            float prefabW = w - debugW - typeW - 8f;

            var c1 = new Rect(rect.x, rect.y, typeW, rect.height);
            var c2 = new Rect(rect.x + typeW + 4f, rect.y, prefabW, rect.height);
            var c3 = new Rect(c2.xMax + 4f, rect.y, debugW, rect.height);

            s_columnHeaderStyle ??= new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold };

            EditorGUI.LabelField(c1, MediatorHeader, s_columnHeaderStyle);
            EditorGUI.LabelField(c2, PrefabHeader, s_columnHeaderStyle);

            s_columnCenterStyle ??= new GUIStyle(EditorStyles.miniLabel) 
            { 
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter 
            };
            EditorGUI.LabelField(c3, DebugHeader, s_columnCenterStyle);
        }

        private bool ContainsMediator(MediatorBehaviour mb)
        {
            for (int i = 0; i < sceneMediatorsProperty.arraySize; i++)
            {
                if (sceneMediatorsProperty.GetArrayElementAtIndex(i).objectReferenceValue == mb)
                    return true;
            }
            return false;
        }

        private void DrawPrefabRow(Rect rect, SerializedProperty element)
        {
            rect.y += 2f;

            var typeNameProp = element.FindPropertyRelative("MediatorTypeName");
            var prefabProp = element.FindPropertyRelative("Prefab");
            var debugProp = element.FindPropertyRelative("AddDebugInstance");

            float w = rect.width;
            float debugW = 26f;
            float typeW = w * 0.45f;
            float prefabW = w - debugW - typeW - 8f;

            var line1 = new Rect(rect.x, rect.y, rect.width, EditorGUIUtility.singleLineHeight);
            var typeRect = new Rect(line1.x, line1.y, typeW, line1.height);
            var prefabRect = new Rect(line1.x + typeW + 4f, line1.y, prefabW, line1.height);
            var debugRect = new Rect(prefabRect.xMax + 4f + 5f, line1.y, 16f, line1.height);

            DrawDetectedMediatorCell(typeRect, typeNameProp, prefabProp.objectReferenceValue as GameObject);
            DrawPrefabCell(prefabRect, prefabProp, typeNameProp);
            DrawDebugCell(debugRect, debugProp, prefabProp.objectReferenceValue as GameObject, typeNameProp);

            if (!showDebugStructure)
                return;

            var line2 = new Rect(rect.x, rect.y + EditorGUIUtility.singleLineHeight + 4f, rect.width, EditorGUIUtility.singleLineHeight);

            var parentTypeProp = element.FindPropertyRelative("DebugParentMediatorTypeName");
            var pathProp = element.FindPropertyRelative("DebugParentContainerPath");
            var viewRootPathProp = element.FindPropertyRelative("DebugViewRootContainerPath");

            float labelW = 54f;
            float gap = 6f;
            float half = (line2.width - gap) * 0.5f;

            var leftRect = new Rect(line2.x, line2.y, half, line2.height);
            var rightRect = new Rect(line2.x + half + gap, line2.y, half, line2.height);

            var pLabel = new Rect(leftRect.x, leftRect.y, labelW, leftRect.height);
            var pField = new Rect(leftRect.x + labelW, leftRect.y, leftRect.width - labelW, leftRect.height);

            var cLabel = new Rect(rightRect.x, rightRect.y, labelW, rightRect.height);
            var cField = new Rect(rightRect.x + labelW, rightRect.y, rightRect.width - labelW, rightRect.height);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                EditorGUI.LabelField(pLabel, "Parent");
                if (parentTypeProp != null)
                {
                    var display = ToFriendlyTypeName(parentTypeProp.stringValue);
                    var newDisplay = EditorGUI.TextField(pField, display);
                    if (!string.Equals(newDisplay, display, StringComparison.Ordinal))
                        parentTypeProp.stringValue = NormalizeTypeName(newDisplay);
                }

                EditorGUI.LabelField(cLabel, string.IsNullOrEmpty(parentTypeProp?.stringValue) ? "ViewPath" : "Path");

                var hasParent = parentTypeProp != null && !string.IsNullOrEmpty(parentTypeProp.stringValue);
                if (!hasParent && viewRootPathProp != null)
                {
                    viewRootPathProp.stringValue = EditorGUI.TextField(cField, viewRootPathProp.stringValue);
                }
                else if (pathProp != null)
                {
                    pathProp.stringValue = EditorGUI.TextField(cField, pathProp.stringValue);
                }
            }
        }

        private static string BuildRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null)
                return string.Empty;

            if (root == target)
                return ".";

            var stack = new Stack<string>();
            var t = target;
            while (t != null && t != root)
            {
                stack.Push(t.name);
                t = t.parent;
            }

            if (t != root)
                return string.Empty;

            return string.Join("/", stack);
        }

        private void CleanupMissingDebugInstances(MediatorRegistryBehaviour registry)
        {
            if (registry == null)
                return;

            bool changed = false;

            for (int i = 0; i < mediatorPrefabsProperty.arraySize; i++)
            {
                var el = mediatorPrefabsProperty.GetArrayElementAtIndex(i);
                var tnProp = el.FindPropertyRelative("MediatorTypeName");
                var dbgProp = el.FindPropertyRelative("AddDebugInstance");
                if (tnProp == null || dbgProp == null)
                    continue;

                if (!dbgProp.boolValue)
                    continue;

                var tn = tnProp.stringValue;
                if (string.IsNullOrEmpty(tn))
                    continue;

                if (FindExistingDebugInstance(registry, tn) == null)
                {
                    dbgProp.boolValue = false;
                    changed = true;
                }
            }

            if (changed)
            {
                mediatorPrefabsProperty.serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(registry);
                Repaint();
            }
        }

        private void DrawDebugCell(Rect rect, SerializedProperty debugProp, GameObject prefab, SerializedProperty mediatorTypeNameProp)
        {
            using (new EditorGUI.DisabledScope(Application.isPlaying || prefab == null))
            {
                GUI.tooltip = DebugHeader.tooltip;
                EditorGUI.BeginChangeCheck();
                bool newVal = EditorGUI.Toggle(rect, debugProp.boolValue);
                if (EditorGUI.EndChangeCheck())
                {
                    debugProp.boolValue = newVal;
                    debugProp.serializedObject.ApplyModifiedProperties();

                    var registry = (MediatorRegistryBehaviour)target;
                    if (registry != null)
                    {
                        var tn = mediatorTypeNameProp.stringValue;
                        if (newVal)
                        {
                            EnsureParentsChecked(tn);
                            EnsureDebugInstanceWithParents(registry, tn);
                        }
                        else
                        {
                            ClearDebugForDescendants(tn);
                            RemoveDebugInstance(registry, tn);
                        }
                    }
                }
                GUI.tooltip = "";
            }
        }

        private void EnsureParentsChecked(string mediatorTypeName)
        {
            if (string.IsNullOrEmpty(mediatorTypeName))
                return;

            var current = mediatorTypeName;
            var visited = new HashSet<string>(StringComparer.Ordinal);

            while (true)
            {
                if (!visited.Add(current))
                    break;

                var parent = GetDebugParentTypeName(current);
                if (string.IsNullOrEmpty(parent))
                    break;

                TrySetDebugFlag(parent, true);
                current = parent;
            }

            serializedObject.ApplyModifiedProperties();
            Repaint();
        }

        private string GetDebugParentTypeName(string mediatorTypeName)
        {
            for (int i = 0; i < mediatorPrefabsProperty.arraySize; i++)
            {
                var el = mediatorPrefabsProperty.GetArrayElementAtIndex(i);
                var tnProp = el.FindPropertyRelative("MediatorTypeName");
                if (tnProp == null || tnProp.stringValue != mediatorTypeName)
                    continue;

                return el.FindPropertyRelative("DebugParentMediatorTypeName")?.stringValue;
            }

            return null;
        }

        private bool TrySetDebugFlag(string mediatorTypeName, bool value)
        {
            if (string.IsNullOrEmpty(mediatorTypeName))
                return false;

            for (int i = 0; i < mediatorPrefabsProperty.arraySize; i++)
            {
                var el = mediatorPrefabsProperty.GetArrayElementAtIndex(i);
                var tnProp = el.FindPropertyRelative("MediatorTypeName");
                if (tnProp == null || tnProp.stringValue != mediatorTypeName)
                    continue;

                var dbgProp = el.FindPropertyRelative("AddDebugInstance");
                if (dbgProp == null)
                    return false;

                if (dbgProp.boolValue == value)
                    return false;

                dbgProp.boolValue = value;
                mediatorPrefabsProperty.serializedObject.ApplyModifiedProperties();
                return true;
            }

            return false;
        }

        private void ClearDebugForDescendants(string parentTypeName)
        {
            if (string.IsNullOrEmpty(parentTypeName))
                return;

            serializedObject.Update();

            // Reuse static dictionary and clear nested lists
            s_tempChildren.Clear();
            for (int i = 0; i < mediatorPrefabsProperty.arraySize; i++)
            {
                var el = mediatorPrefabsProperty.GetArrayElementAtIndex(i);
                var tn = el.FindPropertyRelative("MediatorTypeName")?.stringValue;
                var parent = el.FindPropertyRelative("DebugParentMediatorTypeName")?.stringValue;
                if (string.IsNullOrEmpty(tn) || string.IsNullOrEmpty(parent))
                    continue;

                if (!s_tempChildren.TryGetValue(parent, out var list))
                {
                    list = new List<SerializedProperty>();
                    s_tempChildren[parent] = list;
                }
                list.Add(el);
            }

            var q = new Queue<string>();
            q.Enqueue(parentTypeName);

            while (q.Count > 0)
            {
                var cur = q.Dequeue();
                if (!s_tempChildren.TryGetValue(cur, out var list))
                    continue;

                for (int i = 0; i < list.Count; i++)
                {
                    var el = list[i];
                    var dbg = el.FindPropertyRelative("AddDebugInstance");
                    var tn = el.FindPropertyRelative("MediatorTypeName")?.stringValue;

                    if (dbg != null && dbg.boolValue)
                        dbg.boolValue = false;

                    if (!string.IsNullOrEmpty(tn))
                        q.Enqueue(tn);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void SaveDebugStructureFromScene()
        {
            var registry = (MediatorRegistryBehaviour)target;
            if (registry == null)
                return;

            // Reuse static dictionary
            s_tempMappingByTypeName.Clear();
            for (int i = 0; i < mediatorPrefabsProperty.arraySize; i++)
            {
                var el = mediatorPrefabsProperty.GetArrayElementAtIndex(i);
                var typeNameProp = el.FindPropertyRelative("MediatorTypeName");
                if (typeNameProp == null || string.IsNullOrEmpty(typeNameProp.stringValue))
                    continue;

                s_tempMappingByTypeName[typeNameProp.stringValue] = el;

                var norm = NormalizeAssemblyQualifiedTypeName(typeNameProp.stringValue);
                if (!string.IsNullOrEmpty(norm) && !s_tempMappingByTypeName.ContainsKey(norm))
                    s_tempMappingByTypeName[norm] = el;
            }

            if (VerboseDebug)
            {
                // Debug.Log removed
                DumpAllMappings("Before Save Structure");
                DumpSceneMediators(registry, "Before Save Structure - Scene");
            }

            var instances = registry.GetComponentsInChildren<MediatorBehaviour>(true);
            if (instances == null || instances.Length == 0)
            {
                EditorUtility.DisplayDialog("Save Structure", "No MediatorBehaviour instances found under this View container to infer hierarchy from.", "OK");
                return;
            }

            Undo.RecordObject(registry, "Save Structure");
            serializedObject.Update();

            int updated = 0;
            int missingMappings = 0;

            for (int i = 0; i < instances.Length; i++)
            {
                var m = instances[i];
                if (m == null) continue;

                var typeName = m.GetType().AssemblyQualifiedName;
                if (string.IsNullOrEmpty(typeName))
                    continue;

                if (!TryGetMappingElementByTypeName(s_tempMappingByTypeName, typeName, out var mappingEl))
                {
                    var prefabRoot = PrefabUtility.GetNearestPrefabInstanceRoot(m.gameObject);
                    if (prefabRoot != null)
                    {
                        var prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(prefabRoot) as GameObject;
                        if (prefabAsset != null)
                        {
                            int idx = mediatorPrefabsProperty.arraySize;
                            mediatorPrefabsProperty.InsertArrayElementAtIndex(idx);
                            mappingEl = mediatorPrefabsProperty.GetArrayElementAtIndex(idx);

                            mappingEl.FindPropertyRelative("MediatorTypeName").stringValue = typeName;
                            mappingEl.FindPropertyRelative("Prefab").objectReferenceValue = prefabAsset;
                            mappingEl.FindPropertyRelative("AddDebugInstance").boolValue = false;
                            mappingEl.FindPropertyRelative("DebugParentMediatorTypeName").stringValue = string.Empty;
                            mappingEl.FindPropertyRelative("DebugParentContainerPath").stringValue = string.Empty;
                            mappingEl.FindPropertyRelative("DebugViewRootContainerPath").stringValue = string.Empty;

                            s_tempMappingByTypeName[typeName] = mappingEl;
                            var norm = NormalizeAssemblyQualifiedTypeName(typeName);
                            if (!string.IsNullOrEmpty(norm) && !s_tempMappingByTypeName.ContainsKey(norm))
                                s_tempMappingByTypeName[norm] = mappingEl;

                            // removed Debug.Log for auto-created mapping
                        }
                    }
                }

                if (mappingEl == null)
                {
                    missingMappings++;
                    // Debug.Log removed (missing mapping)
                    continue;
                }

                var parentTypeProp = mappingEl.FindPropertyRelative("DebugParentMediatorTypeName");
                var parentPathProp = mappingEl.FindPropertyRelative("DebugParentContainerPath");
                var viewRootPathProp = mappingEl.FindPropertyRelative("DebugViewRootContainerPath");

                var parentMediator = m.transform.parent != null ? m.transform.parent.GetComponentInParent<MediatorBehaviour>() : null;

                if (parentMediator != null && parentMediator != m)
                {
                    if (parentTypeProp != null) parentTypeProp.stringValue = parentMediator.GetType().AssemblyQualifiedName;

                    var containerPath = BuildRelativePath(parentMediator.transform, m.transform.parent);
                    if (containerPath == ".") containerPath = string.Empty;
                    if (parentPathProp != null) parentPathProp.stringValue = containerPath;

                    if (viewRootPathProp != null) viewRootPathProp.stringValue = string.Empty;

                    // Debug.Log removed (parent/path)

                    updated++;
                    continue;
                }

                if (parentTypeProp != null) parentTypeProp.stringValue = string.Empty;
                if (parentPathProp != null) parentPathProp.stringValue = string.Empty;

                var immediateParent = m.transform.parent;
                if (immediateParent != null && immediateParent != registry.transform)
                {
                    var viewPath = BuildRelativePath(registry.transform, immediateParent);
                    if (viewPath == ".") viewPath = string.Empty;
                    if (viewRootPathProp != null) viewRootPathProp.stringValue = viewPath;

                    // Debug.Log removed (viewRootPath)
                }
                else
                {
                    if (viewRootPathProp != null) viewRootPathProp.stringValue = string.Empty;
                    // Debug.Log removed
                }

                updated++;
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(registry);

            CleanupMissingDebugInstances(registry);

            if (VerboseDebug)
                DumpAllMappings("After Save Structure");

            EditorUtility.DisplayDialog(
                "Save Structure",
                $"Updated {updated} mapping(s)." + (missingMappings > 0 ? $"\n\n{missingMappings} scene mediator instance(s) had no matching prefab mapping and were skipped." : string.Empty),
                "OK");
        }

        private void EnsureDebugInstanceWithParents(MediatorRegistryBehaviour registry, string mediatorTypeName)
        {
            if (registry == null || string.IsNullOrEmpty(mediatorTypeName))
                return;

            // Reuse static dictionary
            s_tempMappingByTypeName.Clear();
            for (int i = 0; i < mediatorPrefabsProperty.arraySize; i++)
            {
                var el = mediatorPrefabsProperty.GetArrayElementAtIndex(i);
                var tn = el.FindPropertyRelative("MediatorTypeName")?.stringValue;
                if (!string.IsNullOrEmpty(tn))
                    AddMappingKey(s_tempMappingByTypeName, tn, el);
            }

            if (VerboseDebug)
            {
                // Debug.Log removed
                DumpAllMappings("Before EnsureDebugInstanceWithParents");
            }

            // Reuse static dictionary
            s_tempSpawned.Clear();
            EnsureSpawnedRecursive(registry, mediatorTypeName, s_tempMappingByTypeName, s_tempSpawned);

            if (VerboseDebug)
            {
                // Debug.Log removed
            }
        }

        private Transform EnsureSpawnedRecursive(
            MediatorRegistryBehaviour registry,
            string mediatorTypeName,
            Dictionary<string, SerializedProperty> map,
            Dictionary<string, Transform> spawned)
        {
            if (spawned.TryGetValue(mediatorTypeName, out var existingSpawn))
                return existingSpawn;

            // 1) if a debug instance already exists in registry, reuse it
            var existingInScene = FindExistingDebugInstance(registry, mediatorTypeName);
            if (existingInScene != null)
            {
                spawned[mediatorTypeName] = existingInScene;
                return existingInScene;
            }

            // 2) if there is a real mediator instance in the scene (non-debug), use it as parent anchor
            var existingMediatorInstance = FindMediatorInstanceByTypeNameInScene(registry, mediatorTypeName);
            if (existingMediatorInstance != null)
            {
                spawned[mediatorTypeName] = existingMediatorInstance.transform;
                return existingMediatorInstance.transform;
            }

            if (!TryResolveMapping(mediatorTypeName, map, out var mappingEl))
            {
                Debug.LogWarning($"[MediatorRegistryEditor] No mapping found for mediator '{mediatorTypeName}'.");
                return null;
            }

            var prefabProp = mappingEl.FindPropertyRelative("Prefab");
            var prefab = prefabProp != null ? prefabProp.objectReferenceValue as GameObject : null;
            if (prefab == null)
            {
                Debug.LogWarning($"[MediatorRegistryEditor] Mapping '{mediatorTypeName}' has no prefab set.");
                return null;
            }

            var parentTypeName = mappingEl.FindPropertyRelative("DebugParentMediatorTypeName")?.stringValue;
            var containerPath = mappingEl.FindPropertyRelative("DebugParentContainerPath")?.stringValue;
            var viewRootPath = mappingEl.FindPropertyRelative("DebugViewRootContainerPath")?.stringValue;

            if (!string.IsNullOrEmpty(parentTypeName))
                parentTypeName = NormalizeAssemblyQualifiedTypeName(parentTypeName);

            Transform parent = registry.transform;

            if (!string.IsNullOrEmpty(parentTypeName))
            {
                var parentSpawn = EnsureSpawnedRecursive(registry, parentTypeName, map, spawned);
                if (parentSpawn != null)
                {
                    if (!string.IsNullOrEmpty(containerPath))
                    {
                        var container = parentSpawn.Find(containerPath);
                        parent = container != null ? container : parentSpawn;
                        if (container == null)
                            Debug.LogWarning($"[MediatorRegistryEditor] Container path '{containerPath}' not found under '{parentSpawn.name}'. Falling back to mediator root.");
                    }
                    else
                    {
                        parent = parentSpawn;
                    }
                }
            }
            else if (!string.IsNullOrEmpty(viewRootPath))
            {
                var staticContainer = registry.transform.Find(viewRootPath);
                if (staticContainer != null)
                {
                    parent = staticContainer;
                }
                else
                {
                    Debug.LogWarning($"[MediatorRegistryEditor] View root container path '{viewRootPath}' not found under '{registry.name}'. Spawning under View root.");
                }
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, parent);
            if (instance == null)
                return null;

            instance.name = GetDebugInstanceName(mediatorTypeName);
            instance.hideFlags = HideFlags.DontSaveInBuild;

            Undo.RegisterCreatedObjectUndo(instance, "Add Debug Mediator Prefab Instance");
            EditorUtility.SetDirty(registry);

            spawned[mediatorTypeName] = instance.transform;
            return instance.transform;
        }

        private void RemoveDebugInstance(MediatorRegistryBehaviour registry, string mediatorTypeName)
        {
            if (registry == null || string.IsNullOrEmpty(mediatorTypeName))
                return;

            var existing = FindExistingDebugInstance(registry, mediatorTypeName);
            if (existing != null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
                EditorUtility.SetDirty(registry);
            }

            if (TrySetDebugFlag(mediatorTypeName, false))
                Repaint();
        }

        private static Transform FindExistingDebugInstance(MediatorRegistryBehaviour registry, string mediatorTypeName)
        {
            if (registry == null) return null;
            var n = GetDebugInstanceName(mediatorTypeName);

            var t = registry.transform.Find(n);
            if (t != null) return t;

            var all = registry.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i] != null && all[i].name == n)
                    return all[i];
            }

            return null;
        }

        private static string GetDebugInstanceName(string mediatorTypeName)
        {
            var shortName = string.IsNullOrEmpty(mediatorTypeName) ? "Mediator" : mediatorTypeName;

            var commaIdx = shortName.IndexOf(',');
            if (commaIdx >= 0)
                shortName = shortName.Substring(0, commaIdx);

            var dotIdx = shortName.LastIndexOf('.');
            if (dotIdx >= 0 && dotIdx < shortName.Length - 1)
                shortName = shortName.Substring(dotIdx + 1);

            return $"<[{shortName}]>";
        }

        private MonoScript FindMonoScriptForType(Type type)
        {
            return MonoScriptCache.FindScriptForType(type);
        }

        private void OpenTypeInEditor(Type type)
        {
            if (type == null)
                return;

            MonoScriptCache.TryOpenScript(type);
        }

        private void HandleTypeNavigation(Rect rect, Type type)
        {
            var e = Event.current;
            if (e == null || type == null)
                return;

            if (!rect.Contains(e.mousePosition))
                return;

            if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2)
            {
                OpenTypeInEditor(type);
                e.Use();
                return;
            }

            if (e.type == EventType.ContextClick)
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Open Type"), false, () => OpenTypeInEditor(type));
                menu.ShowAsContext();
                e.Use();
            }
        }

        private void DrawDetectedMediatorCell(Rect rect, SerializedProperty typeNameProp, GameObject prefab)
        {
            string display = "(assign prefab)";
            Type t = null;
            MonoScript script = null;

            if (prefab != null)
            {
                // Use cached type lookup
                string typeName = typeNameProp.stringValue;
                if (!string.IsNullOrEmpty(typeName))
                {
                    t = GetCachedType(typeName);
                }
                
                display = t != null ? SearchablePopup.FormatTypeLabel(t, t.Name) : "(no mediator found)";
                
                if (t != null)
                {
                    // Use cached script lookup
                    script = GetCachedMonoScript(t);
                }
            }

            if (script != null)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUI.ObjectField(rect, script, typeof(MonoScript), false);
                }

                HandleTypeNavigation(rect, t);
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.TextField(rect, display);
            }

            if (t != null)
                HandleTypeNavigation(rect, t);
        }

        private Type GetCachedType(string typeName)
        {
            if (string.IsNullOrEmpty(typeName))
                return null;

            if (s_typeCache.TryGetValue(typeName, out Type cached))
                return cached;

            Type t = Type.GetType(typeName, throwOnError: false);
            s_typeCache[typeName] = t;
            return t;
        }

        private MonoScript GetCachedMonoScript(Type type)
        {
            if (type == null)
                return null;

            if (s_scriptCache.TryGetValue(type, out MonoScript cached))
                return cached;

            MonoScript script = MonoScriptCache.FindScriptForType(type);
            s_scriptCache[type] = script;
            return script;
        }

        private void DrawPrefabCell(Rect rect, SerializedProperty prefabProp, SerializedProperty mediatorTypeNameProp)
        {
            EditorGUI.BeginChangeCheck();
            var newObj = EditorGUI.ObjectField(rect, prefabProp.objectReferenceValue, typeof(GameObject), false);
            if (EditorGUI.EndChangeCheck())
            {
                prefabProp.objectReferenceValue = newObj;

                var go = newObj as GameObject;
                mediatorTypeNameProp.stringValue = string.Empty;

                if (go != null)
                {
                    var mediator = go.GetComponent<MediatorBehaviour>();
                    if (mediator != null)
                        mediatorTypeNameProp.stringValue = mediator.GetType().AssemblyQualifiedName;
                }

                prefabProp.serializedObject.ApplyModifiedProperties();
                
                // Clear cache entries for this specific type name to force refresh
                if (!string.IsNullOrEmpty(mediatorTypeNameProp.stringValue))
                {
                    s_typeCache.Remove(mediatorTypeNameProp.stringValue);
                    
                    // Also remove from script cache if we have the type
                    if (go != null)
                    {
                        var mediator = go.GetComponent<MediatorBehaviour>();
                        if (mediator != null)
                            s_scriptCache.Remove(mediator.GetType());
                    }
                }
            }

            if (prefabProp.objectReferenceValue != null && string.IsNullOrEmpty(mediatorTypeNameProp.stringValue))
            {
                var iconRect = new Rect(rect.xMax - 18f, rect.y + 1f, 16f, 16f);
                GUI.Label(iconRect, EditorGUIUtility.IconContent("console.erroricon"));
            }
        }
    }
}

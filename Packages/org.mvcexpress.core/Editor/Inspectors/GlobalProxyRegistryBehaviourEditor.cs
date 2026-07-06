using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using mvcExpress.Editor.Core;

namespace mvcExpress.Editor.Inspectors
{
    /// <summary>
    /// Custom inspector for GlobalProxyRegistryBehaviour; draws a distinguishing icon for it in the Hierarchy window.
    /// </summary>
    [CustomEditor(typeof(GlobalProxyRegistryBehaviour))]
    public sealed class GlobalProxyRegistryBehaviourEditor : UnityEditor.Editor
    {
        private const string HeaderIconPath = "Packages/org.mvcexpress.core/Editor/Icons/mvc_proxy_registry_icon.png";
        private const string HeaderTitle = "Global proxy registry";

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
            if (obj == null || obj.GetComponent<GlobalProxyRegistryBehaviour>() == null) return;
            GUI.DrawTexture(new Rect(MvcHierarchyUtils.GetRightEdge(selectionRect) - 16, selectionRect.y, 16, 16), s_hierarchyIcon, ScaleMode.ScaleToFit);
        }

        // Layout constants
        private const float ProxyColumnWidthPercent = 0.32f;
        private const float LogicTypeColumnWidthPercent = 0.28f;
        private const float ViewTypeColumnWidthPercent = 0.28f;
        private const float CheckboxWidth = 20f;
        private const float ColumnPadding = 2f;
        private const float HeaderButtonWidth = 140f;
        private const float HeaderButtonGap = 8f;
        private const float HeaderPaddingX = 8f;
        private const float HeaderPaddingY = 6f;
        private const float HeaderLineMultiplier = 2f;
        private const float HeaderButtonHeightMultiplier = 1.6f;
        private const float ElementHeightPadding = 2f;
        private const float HeaderVerticalNudge = 1f;
        private const float TopSpacing = 2f;
        private const float IconLabelSpacing = 10f;
        private const float SectionSpacing = 6f;

        private Texture2D headerIcon;

        private static readonly GUIContent ProxyHeader = new GUIContent("Proxy", "The proxy component (or prefab) to register");
        private static readonly GUIContent LogicCheckHeader = new GUIContent("L?", "Logic access (checked = commands/proxies can inject)");
        private static readonly GUIContent LogicTypeHeader = new GUIContent("Logic Type", "Type for commands/proxies");
        private static readonly GUIContent ViewCheckHeader = new GUIContent("V?", "View access (checked = mediators can inject)\nGlobal proxies are registered to global container (logic-only), view flag is ignored at runtime.");
        private static readonly GUIContent ViewTypeHeader = new GUIContent("View Type", "Type for mediators (ignored for global registration)");
        private static readonly GUIContent TransientHeader = new GUIContent("P/T", "Click the badge on each row to toggle Permanent/Transient. Scoped is code-only - not available from this Inspector.");

        private SerializedProperty proxyMappingsProperty;
        private ReorderableList proxyList;

        private static GUIStyle s_listHeaderTitleStyle;
        private static GUIStyle s_listHeaderButtonStyle;
        private static GUIStyle s_columnHeaderStyle;
        private static GUIStyle s_columnCenterStyle;

        // Cache for duplicate warnings - only recalculate on change
        private bool _hasDuplicates;
        private string _duplicateMessage;
        private int _lastArraySize = -1;
        private int _lastDataHash = 0;

        // Reusable StringBuilder to avoid allocations
        private static readonly System.Text.StringBuilder s_messageBuilder = new System.Text.StringBuilder(256);

        // Reusable list to avoid allocations in FindAndAdd methods
        private static readonly System.Collections.Generic.List<ProxyBehaviour> s_tempProxyList = new System.Collections.Generic.List<ProxyBehaviour>();

        // PERFORMANCE: Profiler markers for optimization analysis
        private static readonly EditorProfilerMarker s_onInspectorGUIMarker = new EditorProfilerMarker("GlobalProxyRegistryEditor.OnInspectorGUI");
        private static readonly EditorProfilerMarker s_duplicateCheckMarker = new EditorProfilerMarker("GlobalProxyRegistryEditor.DuplicateCheck");
        private static readonly EditorProfilerMarker s_findAndAddMarker = new EditorProfilerMarker("GlobalProxyRegistryEditor.FindAndAdd");

        private void OnEnable()
        {
            proxyMappingsProperty = serializedObject.FindProperty("_proxyMappings");
            headerIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(HeaderIconPath);

            proxyList = new ReorderableList(serializedObject, proxyMappingsProperty, draggable: true, displayHeader: true, displayAddButton: true, displayRemoveButton: true);
            proxyList.onAddCallback = list =>
            {
                serializedObject.Update();
                int idx = proxyMappingsProperty.arraySize;
                proxyMappingsProperty.InsertArrayElementAtIndex(idx);
                var el = proxyMappingsProperty.GetArrayElementAtIndex(idx);

                el.FindPropertyRelative("Proxy").objectReferenceValue = null;
                el.FindPropertyRelative("RegisterToLogic").boolValue = true;
                el.FindPropertyRelative("RegisterToView").boolValue = false;
                el.FindPropertyRelative("IsTransient").boolValue = false;
                el.FindPropertyRelative("LogicTypeName").stringValue = string.Empty;
                el.FindPropertyRelative("ViewTypeName").stringValue = string.Empty;

                serializedObject.ApplyModifiedProperties();
            };

            proxyList.headerHeight = EditorGUIUtility.singleLineHeight;
            proxyList.drawHeaderCallback = DrawProxyMappingColumns;

            proxyList.drawElementCallback = (rect, index, active, focused) =>
            {
                var el = proxyMappingsProperty.GetArrayElementAtIndex(index);
                rect.height = EditorGUI.GetPropertyHeight(el, true);
                EditorGUI.PropertyField(rect, el, GUIContent.none, true);
            };

            proxyList.elementHeightCallback = index =>
            {
                var el = proxyMappingsProperty.GetArrayElementAtIndex(index);
                return EditorGUI.GetPropertyHeight(el, true) + 2f;
            };
        }

        private void OnDisable()
        {
            // Clean up ReorderableList to prevent memory leaks
            if (proxyList != null)
            {
                proxyList.drawElementCallback = null;
                proxyList.drawHeaderCallback = null;
                proxyList.elementHeightCallback = null;
                proxyList.onAddCallback = null;
                proxyList = null;
            }

            // Clear cached data
            proxyMappingsProperty = null;
            headerIcon = null;

            // Reset duplicate detection cache
            _hasDuplicates = false;
            _duplicateMessage = null;
            _lastArraySize = -1;
            _lastDataHash = 0;
        }

        public override void OnInspectorGUI()
        {
            using (s_onInspectorGUIMarker.Auto())
            {
                serializedObject.Update();

                DrawTopHeader();

                using (new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    DrawListTitleHeader();
                    
                    // PERFORMANCE: Only recalculate duplicates when data actually changes
                    EditorGUI.BeginChangeCheck();
                    DrawDuplicateTypeWarnings();
                    proxyList?.DoLayoutList();
                    if (EditorGUI.EndChangeCheck())
                    {
                        // Mark that we need to recalculate on next draw
                        _lastDataHash = 0;
                    }
                }

                serializedObject.ApplyModifiedProperties();
            }
        }

        private void DrawDuplicateTypeWarnings()
        {
            using (s_duplicateCheckMarker.Auto())
            {
                if (proxyMappingsProperty == null)
                    return;

                int currentSize = proxyMappingsProperty.arraySize;
                int currentDataHash = ComputeProxyDataHash(currentSize);
                
                bool needsRecalculation = _lastArraySize != currentSize || _lastDataHash != currentDataHash;

                if (needsRecalculation)
                {
                    _lastArraySize = currentSize;
                    _lastDataHash = currentDataHash;

                    // PERFORMANCE: Use object pool to avoid allocations
                    var counts = EditorObjectPool.RentTypeCountDict();
                    try
                    {
                        for (int i = 0; i < currentSize; i++)
                        {
                            var el = proxyMappingsProperty.GetArrayElementAtIndex(i);
                            var proxyProp = el.FindPropertyRelative("Proxy");
                            var pb = proxyProp.objectReferenceValue as ProxyBehaviour;
                            if (pb == null)
                                continue;

                            var t = pb.GetType();
                            if (!counts.ContainsKey(t))
                                counts[t] = 0;
                            counts[t]++;
                        }

                        _hasDuplicates = false;
                        s_messageBuilder.Clear();
                        foreach (var kvp in counts)
                        {
                            if (kvp.Value > 1)
                            {
                                _hasDuplicates = true;
                                s_messageBuilder.Append("• ");
                                s_messageBuilder.Append(kvp.Key.Name);
                                s_messageBuilder.Append(" mapped ");
                                s_messageBuilder.Append(kvp.Value);
                                s_messageBuilder.AppendLine(" times");
                            }
                        }

                        _duplicateMessage = s_messageBuilder.ToString().TrimEnd();
                    }
                    finally
                    {
                        EditorObjectPool.ReturnTypeCountDict(counts);
                    }
                }

                if (_hasDuplicates)
                {
                    EditorGUILayout.HelpBox(
                        "Duplicate proxy types detected. Each proxy type should be mapped only once.\n\n" + _duplicateMessage,
                        MessageType.Error);
                    EditorGUILayout.Space(2f);
                }
            }
        }

        private int ComputeProxyDataHash(int arraySize)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + arraySize;
                
                for (int i = 0; i < arraySize; i++)
                {
                    var el = proxyMappingsProperty.GetArrayElementAtIndex(i);
                    var proxyProp = el.FindPropertyRelative("Proxy");
                    var obj = proxyProp.objectReferenceValue;
                    hash = hash * 31 + GetObjectIdHash(obj);
                }
                
                return hash;
            }
        }

        private static int GetObjectIdHash(UnityEngine.Object obj)
        {
            if (obj == null)
                return 0;

#if UNITY_6000_4_OR_NEWER
            return obj.GetEntityId().GetHashCode();
#else
            return obj.GetInstanceID();
#endif
        }
        private void DrawListTitleHeader()
        {
            s_listHeaderTitleStyle ??= new GUIStyle(MvcEditorUtility.SectionHeaderTitleStyle) { alignment = TextAnchor.MiddleLeft };
            s_listHeaderButtonStyle ??= new GUIStyle(GUI.skin.button) { fontSize = Mathf.Max(GUI.skin.button.fontSize + 1, 12) };

            float lineH = EditorGUIUtility.singleLineHeight;
            float headerH = (lineH * 2f) + (6f * 2f);

            var content = MvcEditorUtility.DrawHeaderBox(headerH, padX: 8f, padY: 6f);

            const float btnWidth = 140f;
            const float gap = 8f;

            float btnH = lineH * 1.6f;
            var btnRect = new Rect(content.xMax - btnWidth, content.center.y - (btnH * 0.5f), btnWidth, btnH);

            var titleRect = new Rect(content.x, content.y, content.width - btnWidth - gap, content.height);
            var titleLine = new Rect(titleRect.x, titleRect.center.y - (lineH * 0.5f), titleRect.width, lineH);
            EditorGUI.LabelField(titleLine, $"Global Proxy Registrations ({proxyMappingsProperty.arraySize})", s_listHeaderTitleStyle);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUI.Button(btnRect, "Find & Add All", s_listHeaderButtonStyle))
                {
                    FindAndAddProxies();
                }
            }

            EditorGUILayout.Space(2f);
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
                {
                    GUI.DrawTexture(iconRect, headerIcon, ScaleMode.ScaleToFit);
                }

                GUILayout.Space(10f);
                GUILayout.Label(HeaderTitle, MvcEditorUtility.TopHeaderTitleStyle, GUILayout.Height(iconSizeY));
            }

            EditorGUILayout.Space(6f);
        }

        private void DrawProxyMappingColumns(Rect rect)
        {
            s_columnHeaderStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            s_columnCenterStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            rect.y += 1f;

            float totalWidth = rect.width;
            float proxyWidth = totalWidth * 0.32f;
            float checkboxWidth = 20f;
            float logicTypeWidth = totalWidth * 0.28f;
            float viewTypeWidth = totalWidth * 0.28f;

            float x = rect.x;

            EditorGUI.LabelField(new Rect(x, rect.y, proxyWidth - 2f, rect.height), ProxyHeader, s_columnHeaderStyle);
            x += proxyWidth;

            EditorGUI.LabelField(new Rect(x, rect.y, checkboxWidth, rect.height), LogicCheckHeader, s_columnCenterStyle);
            x += checkboxWidth;

            EditorGUI.LabelField(new Rect(x, rect.y, logicTypeWidth - 2f, rect.height), LogicTypeHeader, s_columnHeaderStyle);
            x += logicTypeWidth;

            EditorGUI.LabelField(new Rect(x, rect.y, checkboxWidth, rect.height), ViewCheckHeader, s_columnCenterStyle);
            x += checkboxWidth;

            EditorGUI.LabelField(new Rect(x, rect.y, viewTypeWidth - 2f, rect.height), ViewTypeHeader, s_columnHeaderStyle);
            x += viewTypeWidth;

            EditorGUI.LabelField(new Rect(x, rect.y, checkboxWidth, rect.height), TransientHeader, s_columnCenterStyle);
        }

        private void FindAndAddProxies()
        {
            using (s_findAndAddMarker.Auto())
            {
                var model = (GlobalProxyRegistryBehaviour)target;
                if (model == null) return;

                s_tempProxyList.Clear();
                model.GetComponentsInChildren(true, s_tempProxyList);

                if (s_tempProxyList.Count == 0)
                {
                    EditorUtility.DisplayDialog("Find Proxies", "No ProxyBehaviour components found under this Global Proxies container.", "OK");
                    return;
                }

                Undo.RecordObject(model, "Find & Add Proxies");
                serializedObject.Update();

                for (int i = 0; i < s_tempProxyList.Count; i++)
                {
                    var pb = s_tempProxyList[i];
                    if (pb == null) continue;

                    if (ContainsProxy(pb))
                        continue;

                    int insertIndex = proxyMappingsProperty.arraySize;
                    proxyMappingsProperty.InsertArrayElementAtIndex(insertIndex);
                    var mappingProp = proxyMappingsProperty.GetArrayElementAtIndex(insertIndex);

                    // Cache property references to avoid redundant lookups
                    var proxyProp = mappingProp.FindPropertyRelative("Proxy");
                    var registerToLogicProp = mappingProp.FindPropertyRelative("RegisterToLogic");
                    var registerToViewProp = mappingProp.FindPropertyRelative("RegisterToView");
                    var isTransientProp = mappingProp.FindPropertyRelative("IsTransient");
                    var logicTypeNameProp = mappingProp.FindPropertyRelative("LogicTypeName");
                    var viewTypeNameProp = mappingProp.FindPropertyRelative("ViewTypeName");

                    proxyProp.objectReferenceValue = pb;
                    registerToLogicProp.boolValue = true;
                    registerToViewProp.boolValue = false;
                    isTransientProp.boolValue = false;

                    var proxyType = pb.GetType();
                    logicTypeNameProp.stringValue = proxyType.AssemblyQualifiedName;
                    viewTypeNameProp.stringValue = proxyType.AssemblyQualifiedName;
                }

                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(model);
            }
        }

        private bool ContainsProxy(ProxyBehaviour pb)
        {
            for (int i = 0; i < proxyMappingsProperty.arraySize; i++)
            {
                var element = proxyMappingsProperty.GetArrayElementAtIndex(i);
                var proxyProp = element.FindPropertyRelative("Proxy");
                if (proxyProp.objectReferenceValue == pb)
                    return true;
            }
            return false;
        }
    }
}

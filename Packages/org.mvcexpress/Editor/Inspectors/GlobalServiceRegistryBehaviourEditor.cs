using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using mvcExpress.Editor.Core;
using System.Collections.Generic;

namespace mvcExpress.Editor.Inspectors
{
    [CustomEditor(typeof(GlobalServiceRegistryBehaviour))]
    public sealed class GlobalServiceRegistryBehaviourEditor : UnityEditor.Editor
    {
        private const string HeaderIconPath = "Packages/org.mvcexpress/Editor/Icons/mvc_service_registry_icon.png";
        private const string HeaderTitle = "Global service registry";

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
            if (obj == null || obj.GetComponent<GlobalServiceRegistryBehaviour>() == null) return;
            GUI.DrawTexture(new Rect(MvcHierarchyUtils.GetRightEdge(selectionRect) - 16, selectionRect.y, 16, 16), s_hierarchyIcon, ScaleMode.ScaleToFit);
        }

        // Layout constants
        private const float ServiceColumnWidthPercent = 0.32f;
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

        private static readonly GUIContent ServiceHeader = new GUIContent("Service", "MonoBehaviour prefab/instance to register globally");
        private static readonly GUIContent LogicCheckHeader = new GUIContent("L?", "Logic access");
        private static readonly GUIContent LogicTypeHeader = new GUIContent("Logic Type", "Type exposed to Logic scope");
        private static readonly GUIContent ViewCheckHeader = new GUIContent("V?", "View access");
        private static readonly GUIContent ViewTypeHeader = new GUIContent("View Type", "Type exposed to View scope");
        private static readonly GUIContent TransientHeader = new GUIContent("T?", "Transient");

        private SerializedProperty mappingsProperty;
        private ReorderableList list;

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
        private static readonly System.Collections.Generic.List<MonoBehaviour> s_tempBehaviourList = new System.Collections.Generic.List<MonoBehaviour>();

        // PERFORMANCE: Profiler markers for optimization analysis
        private static readonly EditorProfilerMarker s_onInspectorGUIMarker = new EditorProfilerMarker("GlobalServiceRegistryEditor.OnInspectorGUI");
        private static readonly EditorProfilerMarker s_duplicateCheckMarker = new EditorProfilerMarker("GlobalServiceRegistryEditor.DuplicateCheck");
        private static readonly EditorProfilerMarker s_findAndAddMarker = new EditorProfilerMarker("GlobalServiceRegistryEditor.FindAndAdd");

        private void OnEnable()
        {
            mappingsProperty = serializedObject.FindProperty("_serviceMappings");
            headerIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(HeaderIconPath);

            list = new ReorderableList(serializedObject, mappingsProperty, draggable: true, displayHeader: true, displayAddButton: true, displayRemoveButton: true);
            list.onAddCallback = rl =>
            {
                serializedObject.Update();
                int idx = mappingsProperty.arraySize;
                mappingsProperty.InsertArrayElementAtIndex(idx);
                var el = mappingsProperty.GetArrayElementAtIndex(idx);

                el.FindPropertyRelative("Service").objectReferenceValue = null;
                el.FindPropertyRelative("RegisterToLogic").boolValue = true;
                el.FindPropertyRelative("RegisterToView").boolValue = false;
                el.FindPropertyRelative("IsTransient").boolValue = false;
                el.FindPropertyRelative("LogicTypeName").stringValue = string.Empty;
                el.FindPropertyRelative("ViewTypeName").stringValue = string.Empty;

                serializedObject.ApplyModifiedProperties();
            };

            list.headerHeight = EditorGUIUtility.singleLineHeight;
            list.drawHeaderCallback = DrawColumns;

            list.drawElementCallback = (rect, index, active, focused) =>
            {
                var el = mappingsProperty.GetArrayElementAtIndex(index);
                rect.height = EditorGUI.GetPropertyHeight(el, true);
                EditorGUI.PropertyField(rect, el, GUIContent.none, true);
            };

            list.elementHeightCallback = index =>
            {
                var el = mappingsProperty.GetArrayElementAtIndex(index);
                return EditorGUI.GetPropertyHeight(el, true) + 2f;
            };
        }

        private void OnDisable()
        {
            // Clean up ReorderableList to prevent memory leaks
            if (list != null)
            {
                list.drawElementCallback = null;
                list.drawHeaderCallback = null;
                list.elementHeightCallback = null;
                list.onAddCallback = null;
                list = null;
            }

            // Clear cached data
            mappingsProperty = null;
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
                    DrawListHeader();
                    
                    // PERFORMANCE: Only recalculate duplicates when data actually changes
                    EditorGUI.BeginChangeCheck();
                    DrawDuplicateTypeWarnings();
                    list?.DoLayoutList();
                    if (EditorGUI.EndChangeCheck())
                    {
                        // Mark that we need to recalculate on next draw
                        _lastDataHash = 0;
                    }
                }

                serializedObject.ApplyModifiedProperties();
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
                {
                    GUI.DrawTexture(iconRect, headerIcon, ScaleMode.ScaleToFit);
                }

                GUILayout.Space(10f);
                GUILayout.Label(HeaderTitle, MvcEditorUtility.TopHeaderTitleStyle, GUILayout.Height(iconSizeY));
            }

            EditorGUILayout.Space(6f);
        }

        private void DrawListHeader()
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
            EditorGUI.LabelField(titleLine, $"Global Service Registrations ({mappingsProperty.arraySize})", s_listHeaderTitleStyle);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUI.Button(btnRect, "Find & Add All", s_listHeaderButtonStyle))
                {
                    FindAndAddServices();
                }
            }

            EditorGUILayout.Space(2f);
        }

        private void DrawColumns(Rect rect)
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

            // Calculate available width after accounting for ReorderableList's internal padding
            // The drawer receives position.width which is already adjusted by ReorderableList
            // But header rect.width is the full width, so we need to subtract the same offset
            float reorderableListPadding = 20f; // Space for drag handle
            float availableWidth = rect.width - reorderableListPadding;
            
            // Match ServiceMappingDrawer layout exactly: 32%, 31%, 31%, 5% (with 1% margin)
            float padding = 4f;
            float serviceWidthPercent = 0.32f;
            float logicGroupWidthPercent = 0.31f;
            float viewGroupWidthPercent = 0.31f;
            float transGroupWidthPercent = 0.05f;
            
            float serviceWidth = availableWidth * serviceWidthPercent;
            float logicGroupWidth = availableWidth * logicGroupWidthPercent;
            float viewGroupWidth = availableWidth * viewGroupWidthPercent;
            float transGroupWidth = availableWidth * transGroupWidthPercent;

            float x = rect.x + reorderableListPadding;

            // Service header (32%)
            EditorGUI.LabelField(new Rect(x, rect.y, serviceWidth - padding, rect.height), ServiceHeader, s_columnHeaderStyle);
            x += serviceWidth;

            // Logic group (31%): L label (1.5%) + checkbox (1.5%) + type button (28%)
            float logicLabelWidthPercent = 0.015f;
            float logicCheckWidthPercent = 0.015f;
            
            float logicLabelWidth = availableWidth * logicLabelWidthPercent;
            float logicCheckWidth = availableWidth * logicCheckWidthPercent;
            float logicTypeWidthPercent = logicGroupWidthPercent - logicLabelWidthPercent - logicCheckWidthPercent - 0.005f; // 0.5% for gap
            float logicTypeWidth = availableWidth * logicTypeWidthPercent;
            
            // L? header positioned where the checkbox is
            EditorGUI.LabelField(new Rect(x + logicLabelWidth, rect.y, logicCheckWidth, rect.height), LogicCheckHeader, s_columnCenterStyle);
            
            // Logic Type header
            float logicTypeX = x + logicLabelWidth + logicCheckWidth + (availableWidth * 0.002f); // 0.2% gap
            EditorGUI.LabelField(new Rect(logicTypeX, rect.y, logicTypeWidth, rect.height), LogicTypeHeader, s_columnHeaderStyle);
            x += logicGroupWidth;

            // View group (31%): V label (1.5%) + checkbox (1.5%) + type button (28%)
            float viewLabelWidthPercent = 0.015f;
            float viewCheckWidthPercent = 0.015f;
            
            float viewLabelWidth = availableWidth * viewLabelWidthPercent;
            float viewCheckWidth = availableWidth * viewCheckWidthPercent;
            float viewTypeWidthPercent = viewGroupWidthPercent - viewLabelWidthPercent - viewCheckWidthPercent - 0.005f; // 0.5% for gap
            float viewTypeWidth = availableWidth * viewTypeWidthPercent;
            
            // V? header positioned where the checkbox is
            EditorGUI.LabelField(new Rect(x + viewLabelWidth, rect.y, viewCheckWidth, rect.height), ViewCheckHeader, s_columnCenterStyle);
            
            // View Type header
            float viewTypeX = x + viewLabelWidth + viewCheckWidth + (availableWidth * 0.002f); // 0.2% gap
            EditorGUI.LabelField(new Rect(viewTypeX, rect.y, viewTypeWidth, rect.height), ViewTypeHeader, s_columnHeaderStyle);
            x += viewGroupWidth;

            // Transient group (5%): T label (1.5%) + checkbox (1.5%)
            float transLabelWidthPercent = 0.015f;
            float transLabelWidth = availableWidth * transLabelWidthPercent;
            
            // T? header positioned where the checkbox is
            EditorGUI.LabelField(new Rect(x + transLabelWidth, rect.y, 15f, rect.height), TransientHeader, s_columnCenterStyle);
        }

        private void DrawDuplicateTypeWarnings()
        {
            using (s_duplicateCheckMarker.Auto())
            {
                if (mappingsProperty == null)
                    return;

                int currentSize = mappingsProperty.arraySize;
                int currentDataHash = ComputeServiceDataHash(currentSize);

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
                            var el = mappingsProperty.GetArrayElementAtIndex(i);
                            var svcProp = el.FindPropertyRelative("Service");
                            var mb = svcProp.objectReferenceValue as MonoBehaviour;
                            if (mb == null)
                                continue;

                            var t = mb.GetType();
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
                        "Duplicate service types detected. Each service type should be mapped only once.\n\n" + _duplicateMessage,
                        MessageType.Error);
                    EditorGUILayout.Space(2f);
                }
            }
        }

        private void FindAndAddServices()
        {
            using (s_findAndAddMarker.Auto())
            {
                var servicesContainer = (GlobalServiceRegistryBehaviour)target;
                if (servicesContainer == null) return;

                s_tempBehaviourList.Clear();
                servicesContainer.GetComponentsInChildren(true, s_tempBehaviourList);

                if (s_tempBehaviourList.Count == 0)
                {
                    EditorUtility.DisplayDialog("Find Services", "No MonoBehaviour components found under this Global Services container.", "OK");
                    return;
                }

                Undo.RecordObject(servicesContainer, "Find & Add Services");
                serializedObject.Update();

                for (int i = 0; i < s_tempBehaviourList.Count; i++)
                {
                    var mb = s_tempBehaviourList[i];
                    if (mb == null) continue;
                    if (mb == servicesContainer) continue;
                    if (mb is ProxyBehaviour) continue;

                    if (ContainsService(mb))
                        continue;

                    int insertIndex = mappingsProperty.arraySize;
                    mappingsProperty.InsertArrayElementAtIndex(insertIndex);
                    var mappingProp = mappingsProperty.GetArrayElementAtIndex(insertIndex);

                    // Cache property references to avoid redundant lookups
                    var serviceProp = mappingProp.FindPropertyRelative("Service");
                    var registerToLogicProp = mappingProp.FindPropertyRelative("RegisterToLogic");
                    var registerToViewProp = mappingProp.FindPropertyRelative("RegisterToView");
                    var isTransientProp = mappingProp.FindPropertyRelative("IsTransient");
                    var logicTypeNameProp = mappingProp.FindPropertyRelative("LogicTypeName");
                    var viewTypeNameProp = mappingProp.FindPropertyRelative("ViewTypeName");

                    serviceProp.objectReferenceValue = mb;
                    registerToLogicProp.boolValue = true;
                    registerToViewProp.boolValue = false;
                    isTransientProp.boolValue = false;

                    var type = mb.GetType();
                    logicTypeNameProp.stringValue = type.AssemblyQualifiedName;
                    viewTypeNameProp.stringValue = type.AssemblyQualifiedName;
                }

                serializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(servicesContainer);
            }
        }

        private bool ContainsService(MonoBehaviour mb)
        {
            for (int i = 0; i < mappingsProperty.arraySize; i++)
            {
                var element = mappingsProperty.GetArrayElementAtIndex(i);
                var svcProp = element.FindPropertyRelative("Service");
                if (svcProp.objectReferenceValue == mb)
                    return true;
            }
            return false;
        }

        private int ComputeServiceDataHash(int arraySize)
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + arraySize;

                for (int i = 0; i < arraySize; i++)
                {
                    var el = mappingsProperty.GetArrayElementAtIndex(i);
                    var svcProp = el.FindPropertyRelative("Service");
                    var obj = svcProp.objectReferenceValue;
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
    }
}

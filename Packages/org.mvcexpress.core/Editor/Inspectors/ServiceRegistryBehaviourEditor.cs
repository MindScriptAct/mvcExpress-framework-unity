using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using mvcExpress;
using mvcExpress.Editor.Core;
using System.Collections.Generic;
using System;
using mvcExpress.Internal.DependencyInjection;

namespace mvcExpress.Editor.Inspectors
{
    /// <summary>
    /// Custom inspector for ServiceRegistryBehaviour that lists the runtime-registered services (type, lifecycle, registration source) and draws a Hierarchy icon.
    /// </summary>
    [CustomEditor(typeof(ServiceRegistryBehaviour))]
    public sealed class ServiceRegistryBehaviourEditor : UnityEditor.Editor
    {
        private struct ServiceRuntimeRow
        {
            public readonly UnityEngine.Object ServiceObject;
            public readonly Type RegisteredType;
            public readonly RegistrationLifecycle Lifecycle;
            public readonly GameObject GameObject;
            public readonly MvcModule.RegistrationSource Source;
            public readonly bool IsRegisteredToLogic;
            public readonly bool IsRegisteredToView;

            public ServiceRuntimeRow(
                UnityEngine.Object serviceObject,
                Type registeredType,
                RegistrationLifecycle lifecycle,
                GameObject gameObject,
                MvcModule.RegistrationSource source,
                bool isRegisteredToLogic,
                bool isRegisteredToView)
            {
                ServiceObject = serviceObject;
                RegisteredType = registeredType;
                Lifecycle = lifecycle;
                GameObject = gameObject;
                Source = source;
                IsRegisteredToLogic = isRegisteredToLogic;
                IsRegisteredToView = isRegisteredToView;
            }
        }

        private const string HeaderIconPath = "Packages/org.mvcexpress.core/Editor/Icons/mvc_service_registry_icon.png";
        private const string HeaderTitle = "Service registry";

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
            if (obj == null || obj.GetComponent<ServiceRegistryBehaviour>() == null) return;
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

        private static readonly GUIContent ServiceHeader = new GUIContent("Service", "MonoBehaviour under this Services container");
        private static readonly GUIContent LogicCheckHeader = new GUIContent("L?", "Logic access");
        private static readonly GUIContent LogicTypeHeader = new GUIContent("Logic Type", "Type exposed to Logic scope");
        private static readonly GUIContent ViewCheckHeader = new GUIContent("V?", "View access");
        private static readonly GUIContent ViewTypeHeader = new GUIContent("View Type", "Type exposed to View scope");
        private static readonly GUIContent TransientHeader = new GUIContent("P/T", "Click the badge on each row to toggle Permanent/Transient. Scoped is code-only - not available from this Inspector.");

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
        private static readonly EditorProfilerMarker s_onInspectorGUIMarker = new EditorProfilerMarker("ServiceRegistryEditor.OnInspectorGUI");
        private static readonly EditorProfilerMarker s_duplicateCheckMarker = new EditorProfilerMarker("ServiceRegistryEditor.DuplicateCheck");
        private static readonly EditorProfilerMarker s_findAndAddMarker = new EditorProfilerMarker("ServiceRegistryEditor.FindAndAdd");

        private static readonly GUIContent RuntimeHeaderTitle = new GUIContent("Runtime Services", "Services currently registered in the DI container (Play Mode only)");
        private static readonly GUIContent RuntimeRunMessage = new GUIContent("Enter Play Mode to see the live list of currently registered services.");

        private static readonly GUIContent RuntimeColService = new GUIContent("Service", "Instance registered in the DI container");
        private static readonly GUIContent RuntimeColType = new GUIContent("Type", "Registered type");
        private static readonly GUIContent RuntimeColLogic = new GUIContent("L", "Registered to Logic scope");
        private static readonly GUIContent RuntimeColView = new GUIContent("V", "Registered to View scope");
        private static readonly GUIContent RuntimeColSource = new GUIContent("Source", "Registration source (Unity/Attribute/Code)");
        private static readonly GUIContent RuntimeColGameObject = new GUIContent("GameObject", "GameObject from which this service instance comes");

        private static GUIStyle s_runtimeTableHeaderStyle;
        private static GUIStyle s_runtimeTableCellStyle;
        private static GUIStyle s_runtimeTableCellCenterStyle;

        private static readonly List<MvcDiContainer.RegistrationSnapshot> s_runtimeSnapshot = new List<MvcDiContainer.RegistrationSnapshot>(128);
        private static readonly List<ServiceRuntimeRow> s_runtimeRows = new List<ServiceRuntimeRow>(128);

        private double _nextRepaintTime;

        private void OnEnable()
        {
            mappingsProperty = serializedObject.FindProperty("_serviceMappings");

            try
            {
                headerIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(HeaderIconPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("Failed to load header icon: " + ex.Message);
                headerIcon = null;
            }

            // Keep the ReorderableList header for the column labels only.
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

            list.onRemoveCallback = rl =>
            {
                ReorderableList.defaultBehaviours.DoRemoveButton(rl);
            };

            list.headerHeight = EditorGUIUtility.singleLineHeight;
            list.drawHeaderCallback = rect =>
            {
                DrawColumns(rect);
            };

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

            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
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
                list.onRemoveCallback = null;
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

            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (this == null)
                return;

            if (!Application.isPlaying)
                return;

            if (EditorApplication.timeSinceStartup < _nextRepaintTime)
                return;

            _nextRepaintTime = EditorApplication.timeSinceStartup + 0.25;

            if (target != null)
            {
                Repaint();
            }
        }

        public override void OnInspectorGUI()
        {
            using (s_onInspectorGUIMarker.Auto())
            {
                serializedObject.Update();

                DrawTopHeader();

                DrawRuntimeServicesSection();

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
            EditorGUILayout.Space(TopSpacing);

            using (new EditorGUILayout.HorizontalScope())
            {
                var iconSizeX = MvcEditorUtility.TopHeaderIconWidth;
                var iconSizeY = MvcEditorUtility.TopHeaderIconHeight;
                var iconRect = GUILayoutUtility.GetRect(iconSizeX, iconSizeY, GUILayout.Width(iconSizeX), GUILayout.Height(iconSizeY));

                if (headerIcon != null)
                {
                    GUI.DrawTexture(iconRect, headerIcon, ScaleMode.ScaleToFit);
                }

                GUILayout.Space(IconLabelSpacing);
                GUILayout.Label(HeaderTitle, MvcEditorUtility.TopHeaderTitleStyle, GUILayout.Height(iconSizeY));
            }

            EditorGUILayout.Space(SectionSpacing);
        }

        private void DrawListHeader()
        {
            s_listHeaderTitleStyle ??= new GUIStyle(MvcEditorUtility.SectionHeaderTitleStyle) { alignment = TextAnchor.MiddleLeft };
            s_listHeaderButtonStyle ??= new GUIStyle(GUI.skin.button) { fontSize = Mathf.Max(GUI.skin.button.fontSize + 1, 12) };

            float lineH = EditorGUIUtility.singleLineHeight;
            float headerH = (lineH * HeaderLineMultiplier) + (HeaderPaddingY * 2f);

            var content = MvcEditorUtility.DrawHeaderBox(headerH, padX: HeaderPaddingX, padY: HeaderPaddingY);

            float btnH = lineH * HeaderButtonHeightMultiplier;
            var btnRect = new Rect(content.xMax - HeaderButtonWidth, content.center.y - (btnH * 0.5f), HeaderButtonWidth, btnH);

            var titleRect = new Rect(content.x, content.y, content.width - HeaderButtonWidth - HeaderButtonGap, content.height);
            var titleLine = new Rect(titleRect.x, titleRect.center.y - (lineH * 0.5f), titleRect.width, lineH);
            EditorGUI.LabelField(titleLine, $"Service Registrations ({mappingsProperty.arraySize})", s_listHeaderTitleStyle);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUI.Button(btnRect, "Find & Add All", s_listHeaderButtonStyle))
                {
                    FindAndAddServices();
                }
            }

            EditorGUILayout.Space(SectionSpacing);
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

            rect.y += HeaderVerticalNudge;

            float totalWidth = rect.width;
            float col1 = totalWidth * ServiceColumnWidthPercent;
            float colLogic = totalWidth * LogicTypeColumnWidthPercent;
            float colView = totalWidth * ViewTypeColumnWidthPercent;

            float x = rect.x;

            EditorGUI.LabelField(new Rect(x, rect.y, col1 - ColumnPadding, rect.height), ServiceHeader, s_columnHeaderStyle);
            x += col1;

            EditorGUI.LabelField(new Rect(x, rect.y, CheckboxWidth, rect.height), LogicCheckHeader, s_columnCenterStyle);
            x += CheckboxWidth;

            EditorGUI.LabelField(new Rect(x, rect.y, colLogic - ColumnPadding, rect.height), LogicTypeHeader, s_columnHeaderStyle);
            x += colLogic;

            EditorGUI.LabelField(new Rect(x, rect.y, CheckboxWidth, rect.height), ViewCheckHeader, s_columnCenterStyle);
            x += CheckboxWidth;

            EditorGUI.LabelField(new Rect(x, rect.y, colView - ColumnPadding, rect.height), ViewTypeHeader, s_columnHeaderStyle);
            x += colView;

            EditorGUI.LabelField(new Rect(x, rect.y, CheckboxWidth, rect.height), TransientHeader, s_columnCenterStyle);
        }

        private void FindAndAddServices()
        {
            var servicesContainer = (ServiceRegistryBehaviour)target;
            if (servicesContainer == null) return;

            s_tempBehaviourList.Clear();
            servicesContainer.GetComponentsInChildren(true, s_tempBehaviourList);

            if (s_tempBehaviourList.Count == 0)
            {
                EditorUtility.DisplayDialog("Find Services", "No MonoBehaviour components found under this Services container.", "OK");
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

                // CHANGE: Leave type names empty to use concrete type (rename-safe)
                // Only store custom type names when user explicitly selects an interface or base type
                logicTypeNameProp.stringValue = string.Empty;
                viewTypeNameProp.stringValue = string.Empty;
            }

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(servicesContainer);
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

                    var counts = new System.Collections.Generic.Dictionary<System.Type, int>();

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

                if (_hasDuplicates)
                {
                    EditorGUILayout.HelpBox(
                        "Duplicate service types detected. Each service type should be mapped only once.\n\n" + _duplicateMessage,
                        MessageType.Error);
                    EditorGUILayout.Space(2f);
                }
            }
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
        private void DrawRuntimeServicesSection()
        {
            s_listHeaderTitleStyle ??= new GUIStyle(MvcEditorUtility.SectionHeaderTitleStyle) { alignment = TextAnchor.MiddleLeft };

            float lineH = EditorGUIUtility.singleLineHeight;
            float headerH = (lineH * 2f) + (HeaderPaddingY * 2f);
            var content = MvcEditorUtility.DrawHeaderBox(headerH, padX: HeaderPaddingX, padY: HeaderPaddingY);

            var titleLine = new Rect(content.x, content.center.y - (lineH * 0.5f), content.width, lineH);
            EditorGUI.LabelField(titleLine, RuntimeHeaderTitle, s_listHeaderTitleStyle);

            EditorGUILayout.Space(SectionSpacing);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(RuntimeRunMessage.text, MessageType.Info);
                EditorGUILayout.Space(SectionSpacing);
                return;
            }

            BuildRuntimeRows();
            DrawRuntimeTable(s_runtimeRows);

            EditorGUILayout.Space(SectionSpacing);
        }

        private void BuildRuntimeRows()
        {
            s_runtimeRows.Clear();

            var registry = (ServiceRegistryBehaviour)target;
            if (registry == null || !Application.isPlaying)
                return;

            var module = registry.GetComponentInParent<MvcModule>();
            if (module == null)
                return;

#if UNITY_EDITOR
            // Use a HashSet to avoid showing the same instance multiple times
            var processedInstances = new System.Collections.Generic.HashSet<object>();

            // Use the new API to get services with registration source information
            foreach (var serviceInfo in module.GetAllServices())
            {
                // Skip if we've already processed this instance
                if (!processedInstances.Add(serviceInfo.Instance))
                    continue;

                // Default scope assumptions (we'll enhance this later with actual container queries)
                // Most services are registered to Logic scope by default
                bool isLogic = true;
                bool isView = false;

                // For MonoBehaviour services, check if they're under this registry's hierarchy
                if (serviceInfo.Instance is UnityEngine.Object unityObj)
                {
                    var go = unityObj as GameObject;
                    if (go == null && unityObj is Component c)
                        go = c.gameObject;

                    // Only include MonoBehaviours that are under this registry
                    if (go != null)
                    {
                        var root = registry.transform;
                        if (!IsUnderRoot(go.transform, root))
                            continue;

                        s_runtimeRows.Add(new ServiceRuntimeRow(
                            unityObj,
                            serviceInfo.Type,
                            RegistrationLifecycle.Permanent,
                            go,
                            serviceInfo.Source,
                            isLogic,
                            isView));
                    }
                }
                else
                {
                    // Non-MonoBehaviour services (plain C# classes) - always include them
                    s_runtimeRows.Add(new ServiceRuntimeRow(
                        null,
                        serviceInfo.Type,
                        RegistrationLifecycle.Permanent,
                        null,
                        serviceInfo.Source,
                        isLogic,
                        isView));
                }
            }
#endif
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

        private void DrawRuntimeTable(List<ServiceRuntimeRow> rows)
        {
            s_runtimeTableHeaderStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            s_runtimeTableCellStyle ??= new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleLeft
            };

            s_runtimeTableCellCenterStyle ??= new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter
            };

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField($"Registered services: {rows.Count}");
            }

            var headerRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            DrawRuntimeTableHeader(headerRect);

            for (int i = 0; i < rows.Count; i++)
            {
                var r = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                DrawRuntimeTableRow(r, rows[i]);
            }
        }

        private void DrawRuntimeTableHeader(Rect rect)
        {
            float total = rect.width;

            float colService = total * 0.24f;
            float colType = total * 0.24f;
            float colLogic = 20f;
            float colView = 20f;
            float colSource = total * 0.14f;
            float colGo = total - colService - colType - colLogic - colView - colSource;

            float x = rect.x;
            EditorGUI.LabelField(new Rect(x, rect.y, colService - ColumnPadding, rect.height), RuntimeColService, s_runtimeTableHeaderStyle);
            x += colService;
            EditorGUI.LabelField(new Rect(x, rect.y, colType - ColumnPadding, rect.height), RuntimeColType, s_runtimeTableHeaderStyle);
            x += colType;
            EditorGUI.LabelField(new Rect(x, rect.y, colLogic, rect.height), RuntimeColLogic, s_runtimeTableCellCenterStyle);
            x += colLogic;
            EditorGUI.LabelField(new Rect(x, rect.y, colView, rect.height), RuntimeColView, s_runtimeTableCellCenterStyle);
            x += colView;
            EditorGUI.LabelField(new Rect(x, rect.y, colSource - ColumnPadding, rect.height), RuntimeColSource, s_runtimeTableHeaderStyle);
            x += colSource;
            EditorGUI.LabelField(new Rect(x, rect.y, colGo - ColumnPadding, rect.height), RuntimeColGameObject, s_runtimeTableHeaderStyle);
        }

        private void DrawRuntimeTableRow(Rect rect, ServiceRuntimeRow row)
        {
            float total = rect.width;

            float colService = total * 0.24f;
            float colType = total * 0.24f;
            float colLogic = 20f;
            float colView = 20f;
            float colSource = total * 0.14f;
            float colGo = total - colService - colType - colLogic - colView - colSource;

            float x = rect.x;

            var svcRect = new Rect(x, rect.y, colService - ColumnPadding, rect.height);
            x += colService;

            var typeRect = new Rect(x, rect.y, colType - ColumnPadding, rect.height);
            x += colType;

            var logicRect = new Rect(x, rect.y, colLogic, rect.height);
            x += colLogic;

            var viewRect = new Rect(x, rect.y, colView, rect.height);
            x += colView;

            var sourceRect = new Rect(x, rect.y, colSource - ColumnPadding, rect.height);
            x += colSource;

            var goRect = new Rect(x, rect.y, colGo - ColumnPadding, rect.height);

            // For non-MonoBehaviour services (plain C# classes), show type name instead of object reference
            if (row.ServiceObject != null)
            {
                DrawClickableObjectField(svcRect, row.ServiceObject, typeof(UnityEngine.Object));
            }
            else
            {
                // Plain C# service - show type name
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUI.LabelField(svcRect, row.RegisteredType != null ? row.RegisteredType.Name : "<unknown>", s_runtimeTableCellStyle);
                }
            }

            DrawClickableTypeLabel(typeRect, row.RegisteredType);

            // Draw Logic checkmark
            DrawCheckmark(logicRect, row.IsRegisteredToLogic);

            // Draw View checkmark
            DrawCheckmark(viewRect, row.IsRegisteredToView);

            // Draw source badge with color coding
            DrawSourceBadge(sourceRect, row.Source);

            // Only draw GameObject field if one exists
            if (row.GameObject != null)
            {
                DrawClickableGameObjectField(goRect, row.GameObject);
            }
            else
            {
                // Show "N/A" for plain C# services
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUI.LabelField(goRect, "N/A", s_runtimeTableCellCenterStyle);
                }
            }
        }

        private static void DrawCheckmark(Rect rect, bool isChecked)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.Toggle(rect, isChecked);
            }
        }

        private static void DrawSourceBadge(Rect rect, MvcModule.RegistrationSource source)
        {
            var sourceColor = source switch
            {
                MvcModule.RegistrationSource.Unity => new Color(0.3f, 0.7f, 1f), // Blue
                MvcModule.RegistrationSource.Attribute => new Color(0.3f, 1f, 0.3f), // Green
                MvcModule.RegistrationSource.Code => new Color(1f, 0.7f, 0.3f), // Orange
                _ => Color.gray
            };

            var previousColor = GUI.backgroundColor;
            GUI.backgroundColor = sourceColor;
            
            var buttonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10
            };

            using (new EditorGUI.DisabledScope(true))
            {
                GUI.Button(rect, source.ToString(), buttonStyle);
            }

            GUI.backgroundColor = previousColor;
        }

        private static void DrawClickableTypeLabel(Rect rect, Type type)
        {
            var text = type != null ? type.Name : "<unknown>";

            if (GUI.Button(rect, text, EditorStyles.linkLabel) && type != null)
            {
                // Try to open the implementation source.
                var script = FindMonoScript(type);
                if (script != null)
                {
                    AssetDatabase.OpenAsset(script);
                }
            }
        }

        private static MonoScript FindMonoScript(Type type)
        {
            if (type == null)
                return null;

            var name = type.Name;
            var guids = AssetDatabase.FindAssets($"t:MonoScript {name}");
            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null)
                    continue;

                if (script.GetClass() == type)
                    return script;
            }

            return null;
        }

        private static void DrawClickableObjectField(Rect rect, UnityEngine.Object obj, Type objType)
        {
#if UNITY_6000_0_OR_NEWER
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.ObjectField(rect, obj, objType, allowSceneObjects: true);
            }
#else
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.ObjectField(rect, obj, objType, allowSceneObjects: true);
            }
#endif

            if (obj == null)
                return;

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition) && Event.current.button == 0)
            {
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
                Event.current.Use();
            }
        }

        private static void DrawClickableGameObjectField(Rect rect, GameObject go)
        {
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.ObjectField(rect, go, typeof(GameObject), allowSceneObjects: true);
            }

            if (go == null)
                return;

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition) && Event.current.button == 0)
            {
                Selection.activeGameObject = go;
                EditorGUIUtility.PingObject(go);
                Event.current.Use();
            }
        }
    }
}

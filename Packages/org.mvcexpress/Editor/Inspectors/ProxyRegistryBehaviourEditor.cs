using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using mvcExpress.Editor.Core;
using System;
using System.Collections.Generic;
using mvcExpress.Internal.DependencyInjection;

namespace mvcExpress.Editor.Inspectors
{
    [CustomEditor(typeof(ProxyRegistryBehaviour))]
    public sealed class ProxyRegistryBehaviourEditor : UnityEditor.Editor
    {
        private struct ProxyRuntimeRow
        {
            public readonly UnityEngine.Object ProxyObject;
            public readonly Type RegisteredType;
            public readonly MvcDiContainer.RegistrationLifecycle Lifecycle;
            public readonly GameObject GameObject;
            public readonly MvcModule.RegistrationSource Source;
            public readonly bool IsRegisteredToLogic;
            public readonly bool IsRegisteredToView;

            public ProxyRuntimeRow(
                UnityEngine.Object proxyObject, 
                Type registeredType, 
                MvcDiContainer.RegistrationLifecycle lifecycle, 
                GameObject gameObject,
                MvcModule.RegistrationSource source,
                bool isRegisteredToLogic,
                bool isRegisteredToView)
            {
                ProxyObject = proxyObject;
                RegisteredType = registeredType;
                Lifecycle = lifecycle;
                GameObject = gameObject;
                Source = source;
                IsRegisteredToLogic = isRegisteredToLogic;
                IsRegisteredToView = isRegisteredToView;
            }
        }

        private const string HeaderIconPath = "Packages/org.mvcexpress/Editor/Icons/mvc_proxy_registry_icon.png";
        private const string HeaderTitle = "Proxy registry";

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
            if (obj == null || obj.GetComponent<ProxyRegistryBehaviour>() == null) return;
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

        private static readonly GUIContent ProxyHeader = new GUIContent("Proxy", "The proxy component to register");
        private static readonly GUIContent LogicCheckHeader = new GUIContent("L?", "Logic access (checked = commands/proxies can inject)");
        private static readonly GUIContent LogicTypeHeader = new GUIContent("Logic Type", "Type for commands/proxies");
        private static readonly GUIContent ViewCheckHeader = new GUIContent("V?", "View access (checked = mediators can inject)");
        private static readonly GUIContent ViewTypeHeader = new GUIContent("View Type", "Type for mediators");
        private static readonly GUIContent TransientHeader = new GUIContent("T?", "Transient: When CHECKED, can be destroyed/unregistered during gameplay.\nPersistent: When UNCHECKED, lives with the module lifetime.");

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
        private static readonly EditorProfilerMarker s_onInspectorGUIMarker = new EditorProfilerMarker("ProxyRegistryEditor.OnInspectorGUI");
        private static readonly EditorProfilerMarker s_duplicateCheckMarker = new EditorProfilerMarker("ProxyRegistryEditor.DuplicateCheck");
        private static readonly EditorProfilerMarker s_findAndAddMarker = new EditorProfilerMarker("ProxyRegistryEditor.FindAndAdd");

        private static readonly GUIContent RuntimeHeaderTitle = new GUIContent("Runtime Proxies", "Proxies currently registered in the DI container (Play Mode only)");
        private static readonly GUIContent RuntimeRunMessage = new GUIContent("Enter Play Mode to see the live list of currently registered proxies.");

        private static readonly GUIContent RuntimeColProxy = new GUIContent("Proxy", "Instance registered in the DI container");
        private static readonly GUIContent RuntimeColType = new GUIContent("Type", "Registered type");
        private static readonly GUIContent RuntimeColLogic = new GUIContent("L", "Registered to Logic scope");
        private static readonly GUIContent RuntimeColView = new GUIContent("V", "Registered to View scope");
        private static readonly GUIContent RuntimeColSource = new GUIContent("Source", "Registration source (Unity/Attribute/Code)");
        private static readonly GUIContent RuntimeColGameObject = new GUIContent("GameObject", "GameObject from which this proxy instance comes");

        private static GUIStyle s_runtimeTableHeaderStyle;
        private static GUIStyle s_runtimeTableCellStyle;
        private static GUIStyle s_runtimeTableCellCenterStyle;

        private static readonly List<MvcDiContainer.RegistrationSnapshot> s_runtimeSnapshot = new List<MvcDiContainer.RegistrationSnapshot>(128);
        private static readonly List<ProxyRuntimeRow> s_runtimeRows = new List<ProxyRuntimeRow>(128);

        private double _nextRepaintTime;

        private void OnEnable()
        {
            proxyMappingsProperty = serializedObject.FindProperty("_proxyMappings");

            try
            {
                headerIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(HeaderIconPath);
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning("Failed to load header icon: " + ex.Message);
                headerIcon = null;
            }

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
            proxyList.drawHeaderCallback = rect => { DrawProxyMappingColumns(rect); };

            proxyList.drawElementCallback = (rect, index, active, focused) =>
            {
                var el = proxyMappingsProperty.GetArrayElementAtIndex(index);
                rect.height = EditorGUI.GetPropertyHeight(el, true);
                EditorGUI.PropertyField(rect, el, GUIContent.none, true);
            };

            proxyList.elementHeightCallback = index =>
            {
                var el = proxyMappingsProperty.GetArrayElementAtIndex(index);
                return EditorGUI.GetPropertyHeight(el, true) + ElementHeightPadding;
            };

            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
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

                DrawRuntimeProxiesSection();

                using (new EditorGUI.DisabledScope(Application.isPlaying))
                {
                    DrawListTitleHeader();

                    // PERFORMANCE: Only recalculate duplicates when data actually changes
                    EditorGUI.BeginChangeCheck();
                    DrawDuplicateTypeWarnings();
                    if (proxyList != null)
                        proxyList.DoLayoutList();
                    if (EditorGUI.EndChangeCheck())
                    {
                        // Mark that we need to recalculate on next draw
                        _lastDataHash = 0;
                    }
                }

                serializedObject.ApplyModifiedProperties();
            }
        }

        private void DrawRuntimeProxiesSection()
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

            var registry = (ProxyRegistryBehaviour)target;
            if (registry == null || !Application.isPlaying)
                return;

            var module = registry.GetComponentInParent<MvcModule>();
            if (module == null)
                return;

#if UNITY_EDITOR
            // Use a HashSet to avoid showing the same instance multiple times
            var processedInstances = new System.Collections.Generic.HashSet<object>();

            // Use the new API to get proxies with registration source information
            foreach (var proxyInfo in module.GetAllProxies())
            {
                // Skip if we've already processed this instance
                if (!processedInstances.Add(proxyInfo.Instance))
                    continue;

                // Default scope assumptions
                bool isLogic = true;
                bool isView = false;

                // For ProxyBehaviour proxies, check if they're under this registry's hierarchy
                if (proxyInfo.Instance is UnityEngine.Object unityObj)
                {
                    var go = unityObj as GameObject;
                    if (go == null && unityObj is Component c)
                        go = c.gameObject;

                    // Only include ProxyBehaviours that are under this registry
                    if (go != null)
                    {
                        var root = registry.transform;
                        if (!IsUnderRoot(go.transform, root))
                            continue;

                        s_runtimeRows.Add(new ProxyRuntimeRow(
                            unityObj,
                            proxyInfo.Type,
                            MvcDiContainer.RegistrationLifecycle.Persistent,
                            go,
                            proxyInfo.Source,
                            isLogic,
                            isView));
                    }
                }
                else
                {
                    // Code-only Proxy (non-MonoBehaviour) - always include them
                    s_runtimeRows.Add(new ProxyRuntimeRow(
                        null,
                        proxyInfo.Type,
                        MvcDiContainer.RegistrationLifecycle.Persistent,
                        null,
                        proxyInfo.Source,
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

        private void DrawRuntimeTable(List<ProxyRuntimeRow> rows)
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
                EditorGUILayout.LabelField($"Registered proxies: {rows.Count}");
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

            float colProxy = total * 0.24f;
            float colType = total * 0.24f;
            float colLogic = 20f;
            float colView = 20f;
            float colSource = total * 0.14f;
            float colGo = total - colProxy - colType - colLogic - colView - colSource;

            float x = rect.x;
            EditorGUI.LabelField(new Rect(x, rect.y, colProxy - ColumnPadding, rect.height), RuntimeColProxy, s_runtimeTableHeaderStyle);
            x += colProxy;
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

        private void DrawRuntimeTableRow(Rect rect, ProxyRuntimeRow row)
        {
            float total = rect.width;

            float colProxy = total * 0.24f;
            float colType = total * 0.24f;
            float colLogic = 20f;
            float colView = 20f;
            float colSource = total * 0.14f;
            float colGo = total - colProxy - colType - colLogic - colView - colSource;

            float x = rect.x;

            var proxyRect = new Rect(x, rect.y, colProxy - ColumnPadding, rect.height);
            x += colProxy;

            var typeRect = new Rect(x, rect.y, colType - ColumnPadding, rect.height);
            x += colType;

            var logicRect = new Rect(x, rect.y, colLogic, rect.height);
            x += colLogic;

            var viewRect = new Rect(x, rect.y, colView, rect.height);
            x += colView;

            var sourceRect = new Rect(x, rect.y, colSource - ColumnPadding, rect.height);
            x += colSource;

            var goRect = new Rect(x, rect.y, colGo - ColumnPadding, rect.height);

            // For code-only Proxy (non-MonoBehaviour), show type name instead of object reference
            if (row.ProxyObject != null)
            {
                DrawClickableObjectField(proxyRect, row.ProxyObject, typeof(UnityEngine.Object));
            }
            else
            {
                // Code-only Proxy - show type name
                using (new EditorGUI.DisabledScope(true))
                {
                    var cellStyle = new GUIStyle(EditorStyles.label) { alignment = TextAnchor.MiddleLeft };
                    EditorGUI.LabelField(proxyRect, row.RegisteredType != null ? row.RegisteredType.Name : "<unknown>", cellStyle);
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
                // Show "N/A" for code-only proxies
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

                    var counts = new System.Collections.Generic.Dictionary<System.Type, int>();

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
            float headerH = (lineH * HeaderLineMultiplier) + (HeaderPaddingY * 2f);

            var content = MvcEditorUtility.DrawHeaderBox(headerH, padX: HeaderPaddingX, padY: HeaderPaddingY);

            float btnH = lineH * HeaderButtonHeightMultiplier;
            var btnRect = new Rect(content.xMax - HeaderButtonWidth, content.center.y - (btnH * 0.5f), HeaderButtonWidth, btnH);

            var titleRect = new Rect(content.x, content.y, content.width - HeaderButtonWidth - HeaderButtonGap, content.height);
            var titleLine = new Rect(titleRect.x, titleRect.center.y - (lineH * 0.5f), titleRect.width, lineH);
            EditorGUI.LabelField(titleLine, "Proxy Registrations (" + proxyMappingsProperty.arraySize + ")", s_listHeaderTitleStyle);

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                if (GUI.Button(btnRect, "Find & Add All", s_listHeaderButtonStyle))
                {
                    FindAndAddProxies();
                }
            }

            EditorGUILayout.Space(SectionSpacing);
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

            rect.y += HeaderVerticalNudge;

            float totalWidth = rect.width;
            float proxyWidth = totalWidth * ProxyColumnWidthPercent;
            float logicTypeWidth = totalWidth * LogicTypeColumnWidthPercent;
            float viewTypeWidth = totalWidth * ViewTypeColumnWidthPercent;

            float x = rect.x;

            EditorGUI.LabelField(new Rect(x, rect.y, proxyWidth - ColumnPadding, rect.height), ProxyHeader, s_columnHeaderStyle);
            x += proxyWidth;

            EditorGUI.LabelField(new Rect(x, rect.y, CheckboxWidth, rect.height), LogicCheckHeader, s_columnCenterStyle);
            x += CheckboxWidth;

            EditorGUI.LabelField(new Rect(x, rect.y, logicTypeWidth - ColumnPadding, rect.height), LogicTypeHeader, s_columnHeaderStyle);
            x += logicTypeWidth;

            EditorGUI.LabelField(new Rect(x, rect.y, CheckboxWidth, rect.height), ViewCheckHeader, s_columnCenterStyle);
            x += CheckboxWidth;

            EditorGUI.LabelField(new Rect(x, rect.y, viewTypeWidth - ColumnPadding, rect.height), ViewTypeHeader, s_columnHeaderStyle);
            x += viewTypeWidth;

            EditorGUI.LabelField(new Rect(x, rect.y, CheckboxWidth, rect.height), TransientHeader, s_columnCenterStyle);
        }

        private void FindAndAddProxies()
        {
            var model = (ProxyRegistryBehaviour)target;
            if (model == null) return;

            s_tempProxyList.Clear();
            model.GetComponentsInChildren(true, s_tempProxyList);

            if (s_tempProxyList.Count == 0)
            {
                EditorUtility.DisplayDialog("Find Proxies", "No ProxyBehaviour components found under this Model container.", "OK");
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

        private static void DrawClickableTypeLabel(Rect rect, Type type)
        {
            var text = type != null ? type.Name : "<unknown>";

            if (GUI.Button(rect, text, EditorStyles.linkLabel) && type != null)
            {
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
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.ObjectField(rect, obj, objType, allowSceneObjects: true);
            }

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

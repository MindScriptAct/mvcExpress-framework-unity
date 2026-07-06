using UnityEngine;
using UnityEditor;
using mvcExpress.Editor.Core;
using System.Reflection;

namespace mvcExpress.Editor.Inspectors
{
    [CustomEditor(typeof(MvcModule), true)]
    public class MvcModuleEditor : UnityEditor.Editor
    {
        private const string ModuleIconPath = "Packages/org.mvcexpress.core/Editor/Icons/mvc_module_icon.png";

        private static readonly GUIContent ActorRegistriesHeaderTitle = new GUIContent("MVC Actor registries", "MVC containers for this module (Model / Services / Controller / View).");
        private static readonly GUIContent StatisticsHeaderTitle = new GUIContent("Statistics", "Current registration counts (edit-time).");
        private static readonly GUIContent SettingsHeaderTitle = new GUIContent("Settings", "mvcExpress editor settings");
        private static readonly GUIContent OpenSettingsButton = new GUIContent("Open mvcExpress Settings", "Open Project Settings for mvcExpress.");

        private static GUIStyle s_sectionTitleStyle;

        private SerializedProperty modelContainerProperty;
        private SerializedProperty servicesContainerProperty;
        private SerializedProperty controllerContainerProperty;
        private SerializedProperty viewContainerProperty;

        private Texture2D moduleIcon;

        // Static icon for hierarchy drawing
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

            if (s_hierarchyIcon == null)
            {
                s_hierarchyIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(ModuleIconPath);
            }
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

            GameObject obj = null;
#if UNITY_6000_4_OR_NEWER
            obj = EditorUtility.EntityIdToObject(entityId) as GameObject;
#else
            obj = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
#endif
            if (obj == null) return;
            if (obj.GetComponent<MvcModule>() == null) return;

            var iconRect = new Rect(MvcHierarchyUtils.GetRightEdge(selectionRect) - 16, selectionRect.y, 16, 16);
            GUI.DrawTexture(iconRect, s_hierarchyIcon, ScaleMode.ScaleToFit);
        }

        private void OnEnable()
        {
            modelContainerProperty = serializedObject.FindProperty("_modelContainer");
            servicesContainerProperty = serializedObject.FindProperty("_servicesContainer");
            controllerContainerProperty = serializedObject.FindProperty("_controllerContainer");
            viewContainerProperty = serializedObject.FindProperty("_viewContainer");

            moduleIcon = MvcEditorUtility.Load(ModuleIconPath);
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (!HasRequiredModuleProperties())
            {
                EditorGUILayout.HelpBox(
                    "mvcExpress module internals could not be resolved by the custom inspector. Drawing the default inspector instead.",
                    MessageType.Warning);
                DrawDefaultInspector();
                serializedObject.ApplyModifiedProperties();
                return;
            }

            DrawHeader();
            EditorGUILayout.Space();
            EditorGUILayout.Space();
            EditorGUILayout.Space();

            DrawActorRegistriesSection();

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                DrawContainerRow<ServiceRegistryBehaviour>(servicesContainerProperty, "Services", 0);
                DrawContainerRow<ProxyRegistryBehaviour>(modelContainerProperty, "Model", 1);
                DrawContainerRow<CommandBindingsBehaviour>(controllerContainerProperty, "Controller", 2);
                DrawContainerRow<MediatorRegistryBehaviour>(viewContainerProperty, "View", 3);
            }

            // Draw user fields declared on derived modules (anything serialized that is not part of mvcExpress internals)
            EditorGUILayout.Space();
            DrawUserReferencesSection();

            EditorGUILayout.Space();
            DrawStats();

            EditorGUILayout.Space();
            DrawSettingsSection();

            serializedObject.ApplyModifiedProperties();
        }

        private bool HasRequiredModuleProperties()
        {
            return modelContainerProperty != null &&
                   servicesContainerProperty != null &&
                   controllerContainerProperty != null &&
                   viewContainerProperty != null;
        }

        private void DrawActorRegistriesSection()
        {
            if (s_sectionTitleStyle == null)
                s_sectionTitleStyle = new GUIStyle(MvcEditorUtility.SectionHeaderTitleStyle) { alignment = TextAnchor.MiddleLeft };

            float lineH = EditorGUIUtility.singleLineHeight;
            float headerH = (lineH * 2f) + (6f * 2f);

            var content = MvcEditorUtility.DrawHeaderBox(headerH, padX: 8f, padY: 6f);
            var titleLine = new Rect(content.x, content.center.y - (lineH * 0.5f), content.width, lineH);
            EditorGUI.LabelField(titleLine, ActorRegistriesHeaderTitle, s_sectionTitleStyle);
            EditorGUILayout.Space(2f);
        }

        private void DrawContainerRow<T>(SerializedProperty prop, string label, int siblingIndex) where T : Component
        {
            var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);

            var prevLabelWidth = EditorGUIUtility.labelWidth;
            EditorGUIUtility.labelWidth = 80f;

            const float buttonWidth = 70f;
            const float gap = 6f;

            var fieldRect = new Rect(rowRect.x, rowRect.y, rowRect.width - buttonWidth - gap, rowRect.height);
            var buttonRect = new Rect(fieldRect.xMax + gap, rowRect.y, buttonWidth, rowRect.height);

            // Draw a pure object field (single line) to avoid Unity laying out a multi-line property.
            EditorGUI.BeginProperty(fieldRect, new GUIContent(label), prop);
            prop.objectReferenceValue = EditorGUI.ObjectField(fieldRect, label, prop.objectReferenceValue, typeof(T), true);
            EditorGUI.EndProperty();

            EditorGUIUtility.labelWidth = prevLabelWidth;

            bool hasRef = prop.objectReferenceValue != null;
            using (new EditorGUI.DisabledScope(hasRef))
            {
                if (GUI.Button(buttonRect, "Create"))
                {
                    CreateOrFindContainer<T>(prop, label, siblingIndex);
                }
            }
        }

        private void CreateOrFindContainer<T>(SerializedProperty prop, string defaultName, int siblingIndex) where T : Component
        {
            if (target == null)
                return;

            var module = target as MvcModule;
            if (module == null)
                return;

            // 1) scan children for existing container component
            var existing = module.GetComponentInChildren<T>(includeInactive: true);
            if (existing != null)
            {
                prop.objectReferenceValue = existing;
                serializedObject.ApplyModifiedProperties();
                return;
            }

            // 2) find by name
            var foundT = module.transform.Find(defaultName);
            if (foundT != null)
            {
                var foundC = foundT.GetComponent<T>();
                if (foundC != null)
                {
                    prop.objectReferenceValue = foundC;
                    serializedObject.ApplyModifiedProperties();
                    return;
                }
            }

            // 3) create
            Undo.RegisterCompleteObjectUndo(module.gameObject, "Create MVC Container");

            var go = new GameObject(defaultName);
            Undo.RegisterCreatedObjectUndo(go, "Create MVC Container");
            go.transform.SetParent(module.transform, false);

            var c = Undo.AddComponent<T>(go);
            prop.objectReferenceValue = c;

            // Order them properly
            int index = Mathf.Clamp(siblingIndex, 0, module.transform.childCount - 1);
            go.transform.SetSiblingIndex(index);

            // Also best-effort re-order the other known containers if they exist
            TrySetSiblingIndex(servicesContainerProperty, 0);
            TrySetSiblingIndex(modelContainerProperty, 1);
            TrySetSiblingIndex(controllerContainerProperty, 2);
            TrySetSiblingIndex(viewContainerProperty, 3);

            serializedObject.ApplyModifiedProperties();
            EditorUtility.SetDirty(module);
        }

        private void TrySetSiblingIndex(SerializedProperty containerProp, int index)
        {
            if (containerProp == null) return;
            var c = containerProp.objectReferenceValue as Component;
            if (c == null) return;
            if (c.transform == null) return;

            index = Mathf.Clamp(index, 0, c.transform.parent.childCount - 1);
            c.transform.SetSiblingIndex(index);
        }

        private new void DrawHeader()
        {
            float rightBlockHeight = (EditorGUIUtility.singleLineHeight * 2f) + EditorGUIUtility.standardVerticalSpacing;
            float iconSizeX = MvcEditorUtility.TopHeaderIconWidth;
            float iconSizeY = MvcEditorUtility.TopHeaderIconHeight;

            EditorGUILayout.BeginHorizontal();

            // Fixed-size icon area so it doesn't reserve extra vertical space.
            var iconRect = GUILayoutUtility.GetRect(iconSizeX, iconSizeY, GUILayout.Width(iconSizeX), GUILayout.Height(iconSizeY));
            if (moduleIcon != null)
            {
                GUI.DrawTexture(iconRect, moduleIcon, ScaleMode.ScaleToFit);
            }

            EditorGUILayout.BeginVertical(GUILayout.Height(Mathf.Max(rightBlockHeight, iconSizeY)));
            using (new EditorGUI.DisabledScope(true))
            {
                MonoScript script = MonoScript.FromMonoBehaviour((MonoBehaviour)target);
                EditorGUILayout.ObjectField(script, typeof(MonoScript), false);
            }

            // Display module type with Copy type button.
            var module = target as MvcModule;
            if (module != null)
            {
                var rowRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                
                var prevLabelWidth = EditorGUIUtility.labelWidth;
                EditorGUIUtility.labelWidth = 110f;
                
                const float buttonWidth = 78f;
                const float gap = 4f;
                
                var fieldRect = new Rect(rowRect.x, rowRect.y, rowRect.width - buttonWidth - gap, rowRect.height);
                var buttonRect = new Rect(fieldRect.xMax + gap, rowRect.y, buttonWidth, rowRect.height);
                
                // Get type directly instead of using cached property
                var moduleType = module.GetType();
                var typeName = moduleType.Name;
                
                EditorGUI.BeginDisabledGroup(true);
                EditorGUI.TextField(fieldRect, new GUIContent("Module Type:", "The runtime type of this module"), typeName);
                EditorGUI.EndDisabledGroup();
                
                if (GUI.Button(buttonRect, new GUIContent("Copy typeof", "Copy typeof(typename) to clipboard")))
                {
                    var fullTypeName = moduleType.FullName ?? typeName;
                    var typeofCode = $"typeof({fullTypeName})";
                    EditorGUIUtility.systemCopyBuffer = typeofCode;
                }
                
                EditorGUIUtility.labelWidth = prevLabelWidth;
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.EndHorizontal();
        }

        private void DrawStats()
        {
            int proxyCount = 0;
            int mediatorCount = 0;
            int prefabCount = 0;
            int commandBindingCount = 0;
            int serviceCount = 0;

            var model = modelContainerProperty?.objectReferenceValue as ProxyRegistryBehaviour;
            var services = servicesContainerProperty?.objectReferenceValue as ServiceRegistryBehaviour;
            var controller = controllerContainerProperty?.objectReferenceValue as CommandBindingsBehaviour;
            var view = viewContainerProperty?.objectReferenceValue as MediatorRegistryBehaviour;

            // Count Unity registry registrations
            int unityProxyCount = model?.ProxyMappings?.Length ?? 0;
            int unityServiceCount = services?.ServiceMappings?.Length ?? 0;
            mediatorCount = view?.SceneMediators?.Length ?? 0;
            prefabCount = view?.MediatorPrefabs?.Length ?? 0;
            commandBindingCount = controller?.CommandBindings?.Length ?? 0;

            // In Play mode, count all registered instances in DI container
            if (Application.isPlaying && target is MvcModule module)
            {
                // Use reflection to access internal DI container
                var diContainerField = typeof(MvcModule).GetField("_diContainer",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                if (diContainerField != null)
                {
                    var diContainer = diContainerField.GetValue(module);
                    if (diContainer != null)
                    {
                        // Count total logic registrations (services + proxies)
                        int totalLogicCount = CountDictionaryEntries(diContainer, "_logicObjects");
                        totalLogicCount += CountDictionaryEntries(diContainer, "_logicInterfaces");

                        // Proxies are those that derive from Proxy or ProxyBehaviour
                        int codeProxyCount = CountInstancesOfType(diContainer, "_logicObjects", typeof(Proxy));
                        codeProxyCount += CountInstancesOfType(diContainer, "_logicObjects", typeof(ProxyBehaviour));
                        codeProxyCount += CountInstancesOfType(diContainer, "_logicInterfaces", typeof(Proxy));
                        codeProxyCount += CountInstancesOfType(diContainer, "_logicInterfaces", typeof(ProxyBehaviour));

                        // Services are everything else in logic scope
                        int codeServiceCount = totalLogicCount - codeProxyCount;

                        // Combine with Unity registry counts
                        serviceCount = unityServiceCount + codeServiceCount;
                        proxyCount = unityProxyCount + codeProxyCount;
                    }
                }
            }
            else
            {
                // Edit mode - only show Unity registry counts
                serviceCount = unityServiceCount;
                proxyCount = unityProxyCount;
            }

            if (s_sectionTitleStyle == null)
                s_sectionTitleStyle = new GUIStyle(MvcEditorUtility.SectionHeaderTitleStyle) { alignment = TextAnchor.MiddleLeft };

            float lineH = EditorGUIUtility.singleLineHeight;
            float headerH = (lineH * 2f) + (6f * 2f);

            var content = MvcEditorUtility.DrawHeaderBox(headerH, padX: 8f, padY: 6f);
            var titleLine = new Rect(content.x, content.center.y - (lineH * 0.5f), content.width, lineH);
            EditorGUI.LabelField(titleLine, StatisticsHeaderTitle, s_sectionTitleStyle);

            EditorGUILayout.Space(2f);

            // Services first, then Proxies (swapped order)
            EditorGUILayout.LabelField("Service registrations", serviceCount.ToString());
            EditorGUILayout.LabelField("Proxy registrations", proxyCount.ToString());
            EditorGUILayout.LabelField("Command bindings", commandBindingCount.ToString());
            EditorGUILayout.LabelField("Scene mediators", mediatorCount.ToString());
            EditorGUILayout.LabelField("Mediator prefabs", prefabCount.ToString());
        }

        /// <summary>
        /// Count total entries in a DI container dictionary.
        /// </summary>
        private int CountDictionaryEntries(object container, string fieldName)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var field = container.GetType().GetField(fieldName, flags);

            if (field == null) return 0;

            var dict = field.GetValue(container) as System.Collections.IDictionary;
            if (dict == null) return 0;

            return dict.Count;
        }

        /// <summary>
        /// Count instances in a DI container dictionary that match a specific type or derive from it.
        /// </summary>
        private int CountInstancesOfType(object container, string fieldName, System.Type targetType)
        {
            var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
            var field = container.GetType().GetField(fieldName, flags);

            if (field == null) return 0;

            var dict = field.GetValue(container) as System.Collections.IDictionary;
            if (dict == null) return 0;

            int count = 0;
            foreach (System.Collections.DictionaryEntry entry in dict)
            {
                var instance = entry.Value;
                if (instance == null) continue;

                // Check if instance is of targetType or derives from it
                if (targetType.IsAssignableFrom(instance.GetType()))
                {
                    count++;
                }
            }

            return count;
        }

        private void DrawUserReferencesSection()
        {
            // Check if there are any user-defined properties to show
            bool hasUserProperties = false;
            SerializedProperty iterator = serializedObject.GetIterator();
            if (iterator.NextVisible(true))
            {
                do
                {
                    // Skip internal mvcExpress properties
                    if (iterator.name == "m_Script" ||
                        iterator.name == "_loggingEnabled" ||
                        iterator.name == "_modelContainer" ||
                        iterator.name == "_servicesContainer" ||
                        iterator.name == "_controllerContainer" ||
                        iterator.name == "_viewContainer" ||
                        iterator.name == "_moduleViewContainer")
                    {
                        continue;
                    }

                    hasUserProperties = true;
                    break;
                } while (iterator.NextVisible(false));
            }

            // Always draw the section header
            if (s_sectionTitleStyle == null)
                s_sectionTitleStyle = new GUIStyle(MvcEditorUtility.SectionHeaderTitleStyle) { alignment = TextAnchor.MiddleLeft };

            float lineH = EditorGUIUtility.singleLineHeight;
            float headerH = (lineH * 2f) + (6f * 2f);

            var content = MvcEditorUtility.DrawHeaderBox(headerH, padX: 8f, padY: 6f);
            var titleLine = new Rect(content.x, content.center.y - (lineH * 0.5f), content.width, lineH);
            EditorGUI.LabelField(titleLine, new GUIContent("Custom References", "Custom fields defined in your module class"), s_sectionTitleStyle);

            EditorGUILayout.Space(2f);

            if (hasUserProperties)
            {
                // Draw all user-defined properties
                DrawPropertiesExcluding(
                    serializedObject,
                    "m_Script",
                    "_loggingEnabled",
                    "_modelContainer",
                    "_servicesContainer",
                    "_controllerContainer",
                    "_viewContainer",
                    "_moduleViewContainer");
            }
            else
            {
                // Show a helpful message when no custom references are found
                EditorGUILayout.HelpBox(
                    "No custom references found.",
                    MessageType.Info);
            }
        }

        private void DrawSettingsSection()
        {
            if (s_sectionTitleStyle == null)
                s_sectionTitleStyle = new GUIStyle(MvcEditorUtility.SectionHeaderTitleStyle) { alignment = TextAnchor.MiddleLeft };

            float lineH = EditorGUIUtility.singleLineHeight;
            float headerH = (lineH * 2f) + (6f * 2f);

            var content = MvcEditorUtility.DrawHeaderBox(headerH, padX: 8f, padY: 6f);
            var titleLine = new Rect(content.x, content.center.y - (lineH * 0.5f), content.width, lineH);
            EditorGUI.LabelField(titleLine, SettingsHeaderTitle, s_sectionTitleStyle);

            EditorGUILayout.Space(4f);

            // Logging controls
            DrawLoggingControls();

            EditorGUILayout.Space(4f);
            if (GUILayout.Button(OpenSettingsButton, GUILayout.ExpandWidth(true), GUILayout.Height(24f)))
            {
                SettingsService.OpenProjectSettings("Project/mvcExpress");
            }
        }

        private void DrawLoggingControls()
        {
            var module = target as MvcModule;
            if (module == null) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Logging", EditorStyles.boldLabel);

            // Global logging checkbox (mirrors Project Settings)
            bool globalLoggingEnabled = mvcExpress.Editor.Settings.MvcExpressProjectSettings.GlobalLoggingEnabled;
            EditorGUI.BeginChangeCheck();
            bool newGlobalLogging = EditorGUILayout.Toggle(
                new GUIContent("Global Logging (Project Settings)", "Enable/disable logging for ALL modules in mvcExpress. This setting is stored in Project Settings ? mvcExpress."),
                globalLoggingEnabled);
            if (EditorGUI.EndChangeCheck())
            {
                mvcExpress.Editor.Settings.MvcExpressProjectSettings.GlobalLoggingEnabled = newGlobalLogging;
            }

            // Module-specific logging checkbox
            var loggingEnabledProp = serializedObject.FindProperty("_loggingEnabled");
            if (loggingEnabledProp == null)
            {
                EditorGUILayout.HelpBox("Module logging setting could not be resolved.", MessageType.Warning);
                EditorGUILayout.EndVertical();
                return;
            }

            bool moduleLoggingEnabled = loggingEnabledProp.boolValue;

            EditorGUI.BeginDisabledGroup(globalLoggingEnabled);
            EditorGUI.BeginChangeCheck();

            GUIContent moduleLoggingLabel;
            if (globalLoggingEnabled)
            {
                moduleLoggingLabel = new GUIContent(
                    "Module Logging (Overridden)",
                    "Module logging is controlled by Global Logging in Project Settings. Disable Global Logging to control per-module.");
            }
            else
            {
                moduleLoggingLabel = new GUIContent(
                    "Module Logging",
                    "Enable/disable logging for this specific module. This setting is serialized with the scene.");
            }

            bool newModuleLogging = EditorGUILayout.Toggle(moduleLoggingLabel, moduleLoggingEnabled || globalLoggingEnabled);

            if (EditorGUI.EndChangeCheck() && !globalLoggingEnabled)
            {
                loggingEnabledProp.boolValue = newModuleLogging;
            }

            EditorGUI.EndDisabledGroup();

            // Help text
            EditorGUILayout.Space(2f);
            if (globalLoggingEnabled)
            {
                EditorGUILayout.HelpBox(
                    "Global logging is enabled in Project Settings. All modules will log regardless of their individual settings. " +
                    "Go to Edit ? Project Settings ? mvcExpress to change this.",
                    MessageType.Info);
            }
            else if (moduleLoggingEnabled)
            {
                EditorGUILayout.HelpBox(
                    "This module's logging is enabled. Open Console Pro (Window ? mvcExpress ? Console Pro) to see logs.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Logging is disabled for this module. Enable 'Module Logging' or 'Global Logging' (in Project Settings) to see logs in Console Pro.",
                    MessageType.Info);
            }

            EditorGUILayout.EndVertical();
        }
    }
}

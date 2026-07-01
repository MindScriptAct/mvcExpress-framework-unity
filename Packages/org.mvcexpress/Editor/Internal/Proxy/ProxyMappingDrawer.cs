using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using mvcExpress.Internal.Proxy;

namespace mvcExpress.Editor.Core
{
    /// <summary>
    /// Custom property drawer for ProxyMapping.
    /// Displays proxy reference, type dropdowns, and lifecycle options in a compact layout.
    /// </summary>
    [CustomPropertyDrawer(typeof(ProxyMapping))]
    public class ProxyMappingDrawer : PropertyDrawer
    {
        private const float LINE_HEIGHT = 20f;
        private const float SPACING = 2f;
        private const float CHECKBOX_WIDTH = 16f;
        private const float CHECKBOX_PADDING = 4f;

        private static GUIStyle s_errorStyle;

        // Tooltips
        private static readonly GUIContent ProxyTooltip = new GUIContent("", "The proxy component to register in the DI container");
        private static readonly GUIContent LogicCheckTooltip = new GUIContent("L?", "Logic Access: When CHECKED, commands/proxies can inject this. When UNCHECKED, logic layer cannot access");
        private static readonly GUIContent LogicTooltip = new GUIContent("", "Type for logic layer (commands/proxies). Choose interface for abstraction or concrete type for full access");
        private static readonly GUIContent ViewCheckTooltip = new GUIContent("V?", "View Access: When CHECKED, mediators can inject this. When UNCHECKED, view layer cannot access");
        private static readonly GUIContent ViewTooltip = new GUIContent("", "Type for view layer (mediators). Typically use read-only interface to restrict access");
        private static readonly GUIContent TransientTooltip = new GUIContent(
            "T?",
            "Transient: When CHECKED, can be destroyed/unregistered during gameplay.\n" +
            "Persistent: When UNCHECKED, lives with the module lifetime.");

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var proxyProp = property.FindPropertyRelative("Proxy");
            var proxy = proxyProp.objectReferenceValue as ProxyBehaviour;

            bool isDuplicate = proxy != null && IsDuplicateProxy(property, proxy);

            float height = LINE_HEIGHT + SPACING;

            // Headers are now drawn by the container inspector (paged list header), not per-element.

            if (isDuplicate)
            {
                height += LINE_HEIGHT + SPACING;
            }

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var proxyProp = property.FindPropertyRelative("Proxy");
            var registerToLogicProp = property.FindPropertyRelative("RegisterToLogic");
            var registerToViewProp = property.FindPropertyRelative("RegisterToView");
            var isTransientProp = property.FindPropertyRelative("IsTransient");
            var logicTypeNameProp = property.FindPropertyRelative("LogicTypeName");
            var viewTypeNameProp = property.FindPropertyRelative("ViewTypeName");

            var logicScriptProp = property.FindPropertyRelative("LogicScript");
            var viewScriptProp = property.FindPropertyRelative("ViewScript");

            var proxyBehaviour = proxyProp.objectReferenceValue as ProxyBehaviour;
            bool isDuplicate = proxyBehaviour != null && IsDuplicateProxy(property, proxyBehaviour);

            var rect = new Rect(position.x, position.y, position.width, LINE_HEIGHT);

            float totalWidth = rect.width;
            float proxyWidth = totalWidth * 0.32f;
            float checkboxWidth = CHECKBOX_WIDTH + CHECKBOX_PADDING;
            float logicTypeWidth = totalWidth * 0.28f;
            float viewTypeWidth = totalWidth * 0.28f;

            float currentX = rect.x;

            // 1. Proxy Field (32%)
            var proxyRect = new Rect(currentX, rect.y, proxyWidth - 2f, rect.height);
            EditorGUI.PropertyField(proxyRect, proxyProp, ProxyTooltip);
            currentX += proxyWidth;

            if (proxyBehaviour == null)
            {
                var placeholderRect = new Rect(currentX, rect.y, totalWidth - proxyWidth, rect.height);
                EditorGUI.LabelField(placeholderRect, "? Assign a proxy", EditorStyles.centeredGreyMiniLabel);
                EditorGUI.EndProperty();
                return;
            }

            // Check for duplicate
            if (isDuplicate)
            {
                var errorRect = new Rect(currentX, rect.y, totalWidth - proxyWidth, rect.height);
                
                s_errorStyle ??= new GUIStyle(EditorStyles.label)
                {
                    normal = { textColor = new Color(1f, 0.1f, 0.1f) },
                    fontStyle = FontStyle.Bold
                };
                EditorGUI.LabelField(errorRect, "? ERROR: Duplicate proxy!", s_errorStyle);

                var errorBoxRect = new Rect(rect.x, rect.y + LINE_HEIGHT + SPACING, rect.width, LINE_HEIGHT);
                EditorGUI.HelpBox(errorBoxRect, "This proxy is already mapped. Remove this duplicate or select a different proxy.", MessageType.Error);

                EditorGUI.EndProperty();
                return;
            }

            // Get available types
            var availableTypes = GetAvailableTypes(proxyBehaviour);

            if (availableTypes.Count == 0)
            {
                var warningRect = new Rect(currentX, rect.y, totalWidth - proxyWidth, rect.height);
                EditorGUI.HelpBox(warningRect, "No injectable types found", MessageType.Warning);
                EditorGUI.EndProperty();
                return;
            }

            // Initialize type names if empty
            if (string.IsNullOrEmpty(logicTypeNameProp.stringValue) && availableTypes.Count > 0)
            {
                logicTypeNameProp.stringValue = availableTypes[0].AssemblyQualifiedName;
                if (logicScriptProp != null)
                    logicScriptProp.objectReferenceValue = FindMonoScriptForType(availableTypes[0]);
            }

            if (string.IsNullOrEmpty(viewTypeNameProp.stringValue) && availableTypes.Count > 0)
            {
                viewTypeNameProp.stringValue = availableTypes[0].AssemblyQualifiedName;
                if (viewScriptProp != null)
                    viewScriptProp.objectReferenceValue = FindMonoScriptForType(availableTypes[0]);
            }

            // If we have script references, ensure the string fields stay in sync in a safe context.
            // (No direct access to the element instance here; rely on script->string update below when dropdown changes.)

            bool hasLogicAccess = registerToLogicProp.boolValue;
            bool hasViewAccess = registerToViewProp.boolValue;

            // 2. Logic Checkbox (L?)
            var logicCheckRect = new Rect(currentX + CHECKBOX_PADDING / 2, rect.y, CHECKBOX_WIDTH, rect.height);
            GUI.tooltip = LogicCheckTooltip.tooltip;
            EditorGUI.BeginChangeCheck();
            bool newLogicAccess = EditorGUI.Toggle(logicCheckRect, hasLogicAccess);
            if (EditorGUI.EndChangeCheck())
            {
                // Ensure at least one is enabled
                if (!newLogicAccess && !hasViewAccess)
                {
                    // Cannot disable both - keep logic enabled
                    newLogicAccess = true;
                }
                registerToLogicProp.boolValue = newLogicAccess;
            }
            GUI.tooltip = "";
            currentX += checkboxWidth;

            // 3. Logic Type Dropdown (28%)
            var logicTypeRect = new Rect(currentX, rect.y, logicTypeWidth - 2f, rect.height);

            if (!registerToLogicProp.boolValue)
            {
                EditorGUI.BeginDisabledGroup(true);
                var disabledContent = new GUIContent("(no logic access)", "Logic access disabled");
                EditorGUI.LabelField(logicTypeRect, disabledContent, EditorStyles.centeredGreyMiniLabel);
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                DrawTypeDropdownCompact(logicTypeRect, logicTypeNameProp, availableTypes, LogicTooltip);
                if (EditorGUI.EndChangeCheck())
                {
                    // Only sync script reference when type actually changes
                    if (logicScriptProp != null)
                    {
                        var selectedType = TypeResolutionUtility.SafeGetType(logicTypeNameProp.stringValue);
                        var ms = selectedType != null ? FindMonoScriptForType(selectedType) : null;
                        logicScriptProp.objectReferenceValue = ms;
                    }
                }
            }
            currentX += logicTypeWidth;

            // 4. View Checkbox (V?)
            var viewCheckRect = new Rect(currentX + CHECKBOX_PADDING / 2, rect.y, CHECKBOX_WIDTH, rect.height);
            GUI.tooltip = ViewCheckTooltip.tooltip;
            EditorGUI.BeginChangeCheck();
            bool newViewAccess = EditorGUI.Toggle(viewCheckRect, hasViewAccess);
            if (EditorGUI.EndChangeCheck())
            {
                // Ensure at least one is enabled
                if (!newViewAccess && !registerToLogicProp.boolValue)
                {
                    // Cannot disable both - keep view enabled
                    newViewAccess = true;
                }
                registerToViewProp.boolValue = newViewAccess;
            }
            GUI.tooltip = "";
            currentX += checkboxWidth;

            // 5. View Type Dropdown (28%)
            var viewTypeRect = new Rect(currentX, rect.y, viewTypeWidth - 2f, rect.height);

            if (!registerToViewProp.boolValue)
            {
                EditorGUI.BeginDisabledGroup(true);
                var disabledContent = new GUIContent("(no view access)", "View access disabled");
                EditorGUI.LabelField(viewTypeRect, disabledContent, EditorStyles.centeredGreyMiniLabel);
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                EditorGUI.BeginChangeCheck();
                DrawTypeDropdownCompact(viewTypeRect, viewTypeNameProp, availableTypes, ViewTooltip);
                if (EditorGUI.EndChangeCheck())
                {
                    // Only sync script reference when type actually changes
                    if (viewScriptProp != null)
                    {
                        var selectedType = TypeResolutionUtility.SafeGetType(viewTypeNameProp.stringValue);
                        var ms = selectedType != null ? FindMonoScriptForType(selectedType) : null;
                        viewScriptProp.objectReferenceValue = ms;
                    }
                }
            }
            currentX += viewTypeWidth;

            // 6. Transient Checkbox (T?)
            var transientCheckRect = new Rect(currentX + CHECKBOX_PADDING / 2, rect.y, CHECKBOX_WIDTH, rect.height);
            GUI.tooltip = TransientTooltip.tooltip;
            EditorGUI.BeginChangeCheck();
            bool newTransientValue = EditorGUI.Toggle(transientCheckRect, isTransientProp.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                isTransientProp.boolValue = newTransientValue;
            }
            GUI.tooltip = "";

            EditorGUI.EndProperty();
        }

        private bool IsDuplicateProxy(SerializedProperty currentProperty, ProxyBehaviour proxy)
        {
            if (proxy == null) return false;

            var path = currentProperty.propertyPath;
            var arrayPath = path.Substring(0, path.LastIndexOf('['));
            var arrayProp = currentProperty.serializedObject.FindProperty(arrayPath);

            if (arrayProp == null) return false;

            int matchCount = 0;
            for (int i = 0; i < arrayProp.arraySize; i++)
            {
                var element = arrayProp.GetArrayElementAtIndex(i);
                var proxyProp = element.FindPropertyRelative("Proxy");

                if (proxyProp.objectReferenceValue == proxy)
                {
                    matchCount++;
                    if (matchCount > 1)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private void DrawTypeDropdownCompact(Rect rect, SerializedProperty typeNameProp, List<Type> availableTypes, GUIContent tooltip)
        {
            var currentTypeName = typeNameProp.stringValue;
            var currentTypeIndex = availableTypes.FindIndex(t => t.AssemblyQualifiedName == currentTypeName);

            if (currentTypeIndex == -1)
            {
                currentTypeIndex = 0;
                if (availableTypes.Count > 0)
                {
                    typeNameProp.stringValue = availableTypes[0].AssemblyQualifiedName;
                }
            }

            // Replace LINQ with for-loop to avoid allocations
            var displayOptions = new GUIContent[availableTypes.Count];
            for (int i = 0; i < availableTypes.Count; i++)
            {
                displayOptions[i] = new GUIContent(GetDisplayName(availableTypes[i]), GetTypeTooltip(availableTypes[i]));
            }

            EditorGUI.BeginChangeCheck();
            var newIndex = EditorGUI.Popup(rect, tooltip, currentTypeIndex, displayOptions);

            if (EditorGUI.EndChangeCheck() && newIndex >= 0 && newIndex < availableTypes.Count)
            {
                typeNameProp.stringValue = availableTypes[newIndex].AssemblyQualifiedName;
                currentTypeIndex = newIndex;
            }

            // Allow quick navigation to the selected type by double-clicking the field.
            // Also provide a context menu.
            var selectedType = (currentTypeIndex >= 0 && currentTypeIndex < availableTypes.Count)
                ? availableTypes[currentTypeIndex]
                : null;

            HandleTypeNavigation(rect, selectedType);
        }

        private void HandleTypeNavigation(Rect rect, Type type)
        {
            if (type == null)
                return;

            var e = Event.current;
            if (e == null)
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

        private void OpenTypeInEditor(Type type)
        {
            if (!MonoScriptCache.TryOpenScript(type))
            {
                EditorUtility.DisplayDialog(
                    "Open Type",
                    $"Could not locate script for type '{type.FullName}'.\n\n" +
                    "If this is a generic/nested type or the filename doesn't match the type name, Unity may not be able to resolve it.",
                    "OK");
            }
        }

        private MonoScript FindMonoScriptForType(Type type)
        {
            return MonoScriptCache.FindScriptForType(type);
        }

        private List<Type> GetAvailableTypes(ProxyBehaviour proxy)
        {
            var types = new List<Type>();
            var proxyType = proxy.GetType();

            // Concrete types in inheritance chain
            var currentType = proxyType;
            while (currentType != null && currentType != typeof(ProxyBehaviour) && currentType != typeof(MonoBehaviour) && currentType != typeof(object))
            {
                types.Add(currentType);
                currentType = currentType.BaseType;
            }

            // Only user-defined interfaces (filter out System/Unity/mvcExpress)
            // Replace LINQ with for-loop
            var interfaces = proxyType.GetInterfaces();
            for (int i = 0; i < interfaces.Length; i++)
            {
                var iface = interfaces[i];
                if (!MvcTypeCacheUtility.IsFrameworkInterface(iface) && iface.Name != "ISerializationCallbackReceiver")
                {
                    types.Add(iface);
                }
            }

            return types;
        }

        private string GetDisplayName(Type type)
        {
            return string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Name} ({type.Namespace})";
        }

        private string GetTypeTooltip(Type type)
        {
            if (type.IsInterface)
            {
                return $"Interface: {type.FullName}\nProvides abstraction and encapsulation";
            }
            else
            {
                return $"Concrete Type: {type.FullName}\nProvides full access to all members";
            }
        }
    }
}

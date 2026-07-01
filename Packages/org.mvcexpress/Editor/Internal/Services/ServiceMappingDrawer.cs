using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using mvcExpress.Internal.Services;

namespace mvcExpress.Editor.Core
{
    /// <summary>
    /// Custom property drawer for ServiceMapping.
    /// Mirrors ProxyMappingDrawer UI/behavior, but service selection is limited to child MonoBehaviours.
    /// If a ProxyBehaviour is selected, shows an error asking to add it to Model instead.
    /// </summary>
    [CustomPropertyDrawer(typeof(ServiceMapping))]
    public sealed class ServiceMappingDrawer : PropertyDrawer
    {
        private const float LINE_HEIGHT = 20f;
        private const float SPACING = 2f;
        private const float CHECKBOX_WIDTH = 16f;
        private const float CHECKBOX_PADDING = 4f;

        private static readonly GUIContent ServiceTooltip = new GUIContent("", "Service MonoBehaviour to register");
        private static readonly GUIContent LogicCheckTooltip = new GUIContent("L?", "Logic Access: When CHECKED, commands/proxies can inject this. When UNCHECKED, logic layer cannot access");
        private static readonly GUIContent LogicTooltip = new GUIContent("", "Type for logic layer (commands/proxies). Choose interface for abstraction or concrete type for full access");
        private static readonly GUIContent ViewCheckTooltip = new GUIContent("V?", "View Access: When CHECKED, mediators can inject this. When UNCHECKED, view layer cannot access");
        private static readonly GUIContent ViewTooltip = new GUIContent("", "Type for view layer (mediators). Typically use restricted interface for safety");
        private static readonly GUIContent TransientTooltip = new GUIContent(
            "T?",
            "Transient: When CHECKED, can be destroyed/unregistered during gameplay.\n" +
            "Persistent: When UNCHECKED, lives with the module lifetime.");

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var svcProp = property.FindPropertyRelative("Service");
            var svc = svcProp.objectReferenceValue as MonoBehaviour;

            float height = LINE_HEIGHT + SPACING;

            if (svc is ProxyBehaviour)
            {
                height += LINE_HEIGHT + SPACING;
            }

            return height;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var serviceProp = property.FindPropertyRelative("Service");
            var registerToLogicProp = property.FindPropertyRelative("RegisterToLogic");
            var registerToViewProp = property.FindPropertyRelative("RegisterToView");
            var isTransientProp = property.FindPropertyRelative("IsTransient");
            var logicTypeNameProp = property.FindPropertyRelative("LogicTypeName");
            var viewTypeNameProp = property.FindPropertyRelative("ViewTypeName");

            // Default flags similar to proxies: Logic ON, View OFF.
            if (!registerToLogicProp.boolValue && !registerToViewProp.boolValue)
            {
                registerToLogicProp.boolValue = true;
                registerToViewProp.boolValue = false;
            }

            var service = serviceProp.objectReferenceValue as MonoBehaviour;

            var rowRect = new Rect(position.x, position.y, position.width, LINE_HEIGHT);

            float totalWidth = rowRect.width;
            float serviceWidth = totalWidth * 0.32f;
            float checkboxWidth = CHECKBOX_WIDTH + CHECKBOX_PADDING;
            float logicTypeWidth = totalWidth * 0.28f;
            float viewTypeWidth = totalWidth * 0.28f;

            float currentX = rowRect.x;

            var svcRect = new Rect(currentX, rowRect.y, serviceWidth - 2f, rowRect.height);
            DrawServiceDropdown(svcRect, property, serviceProp, registerToLogicProp, registerToViewProp, logicTypeNameProp, viewTypeNameProp);
            currentX += serviceWidth;

            service = serviceProp.objectReferenceValue as MonoBehaviour;

            if (service == null)
            {
                var placeholderRect = new Rect(currentX, rowRect.y, totalWidth - serviceWidth, rowRect.height);
                EditorGUI.LabelField(placeholderRect, "? Assign a service", EditorStyles.centeredGreyMiniLabel);
                EditorGUI.EndProperty();
                return;
            }

            if (service is ProxyBehaviour)
            {
                DrawProxySelectedError(position);
                EditorGUI.EndProperty();
                return;
            }

            var availableTypes = GetAvailableTypes(service);
            if (availableTypes.Count == 0)
            {
                var warningRect = new Rect(currentX, rowRect.y, totalWidth - serviceWidth, rowRect.height);
                EditorGUI.HelpBox(warningRect, "No injectable types found", MessageType.Warning);
                EditorGUI.EndProperty();
                return;
            }

            if (string.IsNullOrEmpty(logicTypeNameProp.stringValue))
            {
                logicTypeNameProp.stringValue = availableTypes[0].AssemblyQualifiedName;
            }

            if (string.IsNullOrEmpty(viewTypeNameProp.stringValue))
            {
                viewTypeNameProp.stringValue = availableTypes[0].AssemblyQualifiedName;
            }

            bool hasLogicAccess = registerToLogicProp.boolValue;
            bool hasViewAccess = registerToViewProp.boolValue;

            // Logic checkbox
            var logicCheckRect = new Rect(currentX + CHECKBOX_PADDING / 2, rowRect.y, CHECKBOX_WIDTH, rowRect.height);
            GUI.tooltip = LogicCheckTooltip.tooltip;
            EditorGUI.BeginChangeCheck();
            bool newLogicAccess = EditorGUI.Toggle(logicCheckRect, hasLogicAccess);
            if (EditorGUI.EndChangeCheck())
            {
                if (!newLogicAccess && !hasViewAccess)
                {
                    newLogicAccess = true;
                }
                registerToLogicProp.boolValue = newLogicAccess;
            }
            GUI.tooltip = "";
            currentX += checkboxWidth;

            // Logic type
            var logicTypeRect = new Rect(currentX, rowRect.y, logicTypeWidth - 2f, rowRect.height);
            if (!registerToLogicProp.boolValue)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUI.LabelField(logicTypeRect, new GUIContent("(no logic access)", "Logic access disabled"), EditorStyles.centeredGreyMiniLabel);
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                DrawTypeDropdownCompact(logicTypeRect, logicTypeNameProp, availableTypes, LogicTooltip);
            }
            currentX += logicTypeWidth;

            // View checkbox
            var viewCheckRect = new Rect(currentX + CHECKBOX_PADDING / 2, rowRect.y, CHECKBOX_WIDTH, rowRect.height);
            GUI.tooltip = ViewCheckTooltip.tooltip;
            EditorGUI.BeginChangeCheck();
            bool newViewAccess = EditorGUI.Toggle(viewCheckRect, hasViewAccess);
            if (EditorGUI.EndChangeCheck())
            {
                if (!newViewAccess && !registerToLogicProp.boolValue)
                {
                    newViewAccess = true;
                }
                registerToViewProp.boolValue = newViewAccess;
            }
            GUI.tooltip = "";
            currentX += checkboxWidth;

            // View type
            var viewTypeRect = new Rect(currentX, rowRect.y, viewTypeWidth - 2f, rowRect.height);
            if (!registerToViewProp.boolValue)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUI.LabelField(viewTypeRect, new GUIContent("(no view access)", "View access disabled"), EditorStyles.centeredGreyMiniLabel);
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                DrawTypeDropdownCompact(viewTypeRect, viewTypeNameProp, availableTypes, ViewTooltip);
            }
            currentX += viewTypeWidth;

            // Transient
            var transientCheckRect = new Rect(currentX + CHECKBOX_PADDING / 2, rowRect.y, CHECKBOX_WIDTH, rowRect.height);
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

        private void DrawServiceDropdown(
            Rect rect,
            SerializedProperty mappingProperty,
            SerializedProperty serviceProp,
            SerializedProperty registerToLogicProp,
            SerializedProperty registerToViewProp,
            SerializedProperty logicTypeNameProp,
            SerializedProperty viewTypeNameProp)
        {
            EditorGUI.BeginChangeCheck();
            var next = EditorGUI.ObjectField(rect, new GUIContent("", ServiceTooltip.tooltip), serviceProp.objectReferenceValue, typeof(MonoBehaviour), true) as MonoBehaviour;
            if (EditorGUI.EndChangeCheck())
            {
                serviceProp.objectReferenceValue = next;

                if (next != null && !(next is ProxyBehaviour))
                {
                    var t = next.GetType();
                    registerToLogicProp.boolValue = true;
                    registerToViewProp.boolValue = false;
                    logicTypeNameProp.stringValue = t.AssemblyQualifiedName;
                    viewTypeNameProp.stringValue = t.AssemblyQualifiedName;
                }

                serviceProp.serializedObject.ApplyModifiedProperties();
            }
        }

        private void DrawProxySelectedError(Rect position)
        {
            var row2 = new Rect(position.x, position.y + LINE_HEIGHT + SPACING, position.width, LINE_HEIGHT);
            EditorGUI.HelpBox(row2, "Selected component is a ProxyBehaviour. Add it under Model container instead.", MessageType.Error);
        }

        private static List<Type> GetAvailableTypes(MonoBehaviour service)
        {
            var types = new List<Type>();
            var serviceType = service.GetType();

            var currentType = serviceType;
            while (currentType != null && currentType != typeof(MonoBehaviour) && currentType != typeof(Behaviour) && currentType != typeof(Component) && currentType != typeof(object))
            {
                types.Add(currentType);
                currentType = currentType.BaseType;
            }

            // Replace LINQ with for-loop
            var interfaces = serviceType.GetInterfaces();
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

        private static void DrawTypeDropdownCompact(Rect rect, SerializedProperty typeNameProp, List<Type> availableTypes, GUIContent tooltip)
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
            }
        }

        private static string GetDisplayName(Type type)
        {
            return string.IsNullOrEmpty(type.Namespace) ? type.Name : $"{type.Name} ({type.Namespace})";
        }

        private static string GetTypeTooltip(Type type)
        {
            if (type.IsInterface)
            {
                return $"Interface: {type.FullName}\nProvides abstraction and encapsulation";
            }

            return $"Concrete Type: {type.FullName}\nProvides full access to all members";
        }
    }
}

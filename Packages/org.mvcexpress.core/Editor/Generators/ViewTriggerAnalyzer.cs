using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace mvcExpress.Editor.Generators
{
    /// <summary>
    /// Analyzes GameObjects to detect UI event triggers that can be generated into mediator handlers.
    /// </summary>
    internal static class ViewTriggerAnalyzer
    {
        /// <summary>
        /// Scans a GameObject and its children for UI components with UnityEvents.
        /// </summary>
        public static List<ViewTriggerInfo> AnalyzeGameObject(GameObject root)
        {
            if (root == null)
                return new List<ViewTriggerInfo>();

            var triggers = new List<ViewTriggerInfo>();

            // Get all components on this GameObject and children
            var allComponents = root.GetComponentsInChildren<Component>(true);

            foreach (var component in allComponents)
            {
                if (component == null)
                    continue;

                // Skip Transform and other basic components
                if (component is Transform)
                    continue;

                // Analyze this component for UnityEvents
                AnalyzeComponent(component, triggers);
            }

            return triggers;
        }

        private static void AnalyzeComponent(Component component, List<ViewTriggerInfo> triggers)
        {
            var componentType = component.GetType();

            // Check for well-known UI components first (faster)
            if (TryAnalyzeKnownUIComponent(component, triggers))
                return;

            // Fall back to reflection-based analysis for custom components
            AnalyzeComponentWithReflection(component, componentType, triggers);
        }

        private static bool TryAnalyzeKnownUIComponent(Component component, List<ViewTriggerInfo> triggers)
        {
            // Button
            if (component is Button button)
            {
                if (button.onClick != null)
                {
                    triggers.Add(new ViewTriggerInfo(component, "onClick", Array.Empty<Type>()));
                }
                return true;
            }

            // Toggle
            if (component is Toggle toggle)
            {
                if (toggle.onValueChanged != null)
                {
                    triggers.Add(new ViewTriggerInfo(component, "onValueChanged", new[] { typeof(bool) }));
                }
                return true;
            }

            // Slider
            if (component is Slider slider)
            {
                if (slider.onValueChanged != null)
                {
                    triggers.Add(new ViewTriggerInfo(component, "onValueChanged", new[] { typeof(float) }));
                }
                return true;
            }

            // InputField
            if (component is InputField inputField)
            {
                if (inputField.onEndEdit != null)
                {
                    triggers.Add(new ViewTriggerInfo(component, "onEndEdit", new[] { typeof(string) }));
                }
                if (inputField.onValueChanged != null)
                {
                    triggers.Add(new ViewTriggerInfo(component, "onValueChanged", new[] { typeof(string) }));
                }
                return true;
            }

            // Dropdown
            if (component is Dropdown dropdown)
            {
                if (dropdown.onValueChanged != null)
                {
                    triggers.Add(new ViewTriggerInfo(component, "onValueChanged", new[] { typeof(int) }));
                }
                return true;
            }

            // Scrollbar
            if (component is Scrollbar scrollbar)
            {
                if (scrollbar.onValueChanged != null)
                {
                    triggers.Add(new ViewTriggerInfo(component, "onValueChanged", new[] { typeof(float) }));
                }
                return true;
            }

            // ScrollRect
            if (component is ScrollRect scrollRect)
            {
                if (scrollRect.onValueChanged != null)
                {
                    triggers.Add(new ViewTriggerInfo(component, "onValueChanged", new[] { typeof(Vector2) }));
                }
                return true;
            }

            return false;
        }

        // Unity-internal events that should not be exposed as UI triggers.
        private static readonly HashSet<string> s_blacklistedEvents = new HashSet<string>(StringComparer.Ordinal)
        {
            "onCullStateChanged",   // MaskableGraphic internal masking event (TMP, Image, etc.)
            "onDirty",              // Graphic.onDirty — internal layout invalidation
        };

        private static void AnalyzeComponentWithReflection(Component component, Type componentType, List<ViewTriggerInfo> triggers)
        {
            // Look for public fields/properties of type UnityEvent or UnityEvent<T>
            var fields = componentType.GetFields(BindingFlags.Public | BindingFlags.Instance);

            foreach (var field in fields)
            {
                if (s_blacklistedEvents.Contains(field.Name))
                    continue;
                if (!IsUnityEventType(field.FieldType))
                    continue;

                var fieldValue = field.GetValue(component);
                if (fieldValue == null)
                    continue;

                // Get parameter types from UnityEvent<T, T2, ...>
                var paramTypes = GetUnityEventParameterTypes(field.FieldType);

                triggers.Add(new ViewTriggerInfo(component, field.Name, paramTypes));
            }

            var properties = componentType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var property in properties)
            {
                if (s_blacklistedEvents.Contains(property.Name))
                    continue;
                if (!property.CanRead)
                    continue;

                if (!IsUnityEventType(property.PropertyType))
                    continue;

                try
                {
                    var propValue = property.GetValue(component);
                    if (propValue == null)
                        continue;

                    var paramTypes = GetUnityEventParameterTypes(property.PropertyType);

                    triggers.Add(new ViewTriggerInfo(component, property.Name, paramTypes));
                }
                catch
                {
                    // Skip properties that throw on access
                    continue;
                }
            }
        }

        private static bool IsUnityEventType(Type type)
        {
            if (type == null)
                return false;

            // Check if it's UnityEvent or derived from it
            if (type == typeof(UnityEvent))
                return true;

            if (type.IsGenericType)
            {
                var genericTypeDef = type.GetGenericTypeDefinition();

                if (genericTypeDef == typeof(UnityEvent<>))
                    return true;

                if (genericTypeDef == typeof(UnityEvent<,>))
                    return true;

                if (genericTypeDef == typeof(UnityEvent<,,>))
                    return true;

                if (genericTypeDef == typeof(UnityEvent<,,,>))
                    return true;
            }

            // Check base type
            if (type.BaseType != null && IsUnityEventType(type.BaseType))
                return true;

            return false;
        }

        private static Type[] GetUnityEventParameterTypes(Type unityEventType)
        {
            if (unityEventType == typeof(UnityEvent))
                return Array.Empty<Type>();

            if (unityEventType.IsGenericType)
            {
                return unityEventType.GetGenericArguments();
            }

            // For custom derived UnityEvent types, check base type
            if (unityEventType.BaseType != null && unityEventType.BaseType.IsGenericType)
            {
                return unityEventType.BaseType.GetGenericArguments();
            }

            return Array.Empty<Type>();
        }
    }
}

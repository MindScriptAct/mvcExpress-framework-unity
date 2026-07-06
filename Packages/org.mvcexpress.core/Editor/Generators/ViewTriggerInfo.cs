using System;
using UnityEngine;

namespace mvcExpress.Editor.Generators
{
    /// <summary>
    /// Represents a detected UI event trigger that can generate mediator handler code.
    /// </summary>
    [Serializable]
    internal sealed class ViewTriggerInfo
    {
        /// <summary>
        /// The component that contains the event (e.g., Button, Slider, Toggle).
        /// </summary>
        public Component Component;

        /// <summary>
        /// Name of the component's GameObject (used for naming).
        /// </summary>
        public string ComponentName;

        /// <summary>
        /// Type of the component (e.g., "Button", "Slider").
        /// </summary>
        public string ComponentTypeName;

        /// <summary>
        /// Name of the event (e.g., "onClick", "onValueChanged").
        /// </summary>
        public string EventName;

        /// <summary>
        /// Full event path for display (e.g., "PlayButton.onClick").
        /// </summary>
        public string DisplayName;

        /// <summary>
        /// Parameter types for the event callback (e.g., empty for onClick, [float] for Slider.onValueChanged).
        /// </summary>
        public Type[] ParameterTypes;

        /// <summary>
        /// Whether this trigger is selected for generation.
        /// </summary>
        public bool IsSelected;

        /// <summary>
        /// Suggested message name based on component and event names.
        /// </summary>
        public string SuggestedMessageName;

        /// <summary>
        /// Suggested handler method name in the mediator.
        /// </summary>
        public string SuggestedHandlerName;

        /// <summary>
        /// Suggested command name.
        /// </summary>
        public string SuggestedCommandName;

        public ViewTriggerInfo(
            Component component,
            string eventName,
            Type[] parameterTypes)
        {
            Component = component;
            ComponentName = component.gameObject.name;
            ComponentTypeName = component.GetType().Name;
            EventName = eventName;
            DisplayName = $"{ComponentName}.{eventName}";
            ParameterTypes = parameterTypes ?? Array.Empty<Type>();
            IsSelected = true; // Selected by default

            // Generate suggested names
            SuggestedMessageName = GenerateMessageName(ComponentName, eventName);
            SuggestedHandlerName = GenerateHandlerName(ComponentName, eventName);
            SuggestedCommandName = GenerateCommandName(ComponentName, eventName);
        }

        private static string GenerateMessageName(string componentName, string eventName)
        {
            var cleanName = ToIdentifier(componentName)
                .Replace("Button", "")
                .Replace("Slider", "")
                .Replace("Toggle", "")
                .Replace("Input", "")
                .Replace("Dropdown", "");

            var action = GetEventAction(eventName);
            return $"{cleanName}{action}Message";
        }

        private static string GenerateHandlerName(string componentName, string eventName)
        {
            return $"On{ToIdentifier(componentName)}{GetEventAction(eventName)}";
        }

        private static string GenerateCommandName(string componentName, string eventName)
        {
            var cleanName = ToIdentifier(componentName)
                .Replace("Button", "")
                .Replace("Slider", "")
                .Replace("Toggle", "")
                .Replace("Input", "")
                .Replace("Dropdown", "");

            return $"{cleanName}{GetEventAction(eventName)}Command";
        }

        // Converts an event name like "onClick" → "Clicked", "onValueChanged" → "Changed".
        internal static string GetEventAction(string eventName)
        {
            return eventName
                .Replace("onClick", "Clicked")
                .Replace("onValueChanged", "Changed")
                .Replace("onEndEdit", "Submitted")
                .Replace("onSubmit", "Submitted")
                .Replace("on", "");
        }

        // Strips characters that are invalid in C# identifiers, using title-casing at boundaries.
        internal static string ToIdentifier(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Unknown";
            var sb = new System.Text.StringBuilder(s.Length);
            bool nextUpper = false;
            foreach (var c in s)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    sb.Append(nextUpper ? char.ToUpperInvariant(c) : c);
                    nextUpper = false;
                }
                else
                {
                    nextUpper = true;
                }
            }
            if (sb.Length > 0 && char.IsDigit(sb[0]))
                sb.Insert(0, '_');
            return sb.Length > 0 ? sb.ToString() : "Unknown";
        }

        public string GetParameterTypesString()
        {
            if (ParameterTypes == null || ParameterTypes.Length == 0)
                return string.Empty;

            var typeNames = new string[ParameterTypes.Length];
            for (int i = 0; i < ParameterTypes.Length; i++)
            {
                typeNames[i] = GetFriendlyTypeName(ParameterTypes[i]);
            }

            return $" ({string.Join(", ", typeNames)})";
        }

        private static string GetFriendlyTypeName(Type type)
        {
            if (type == typeof(int)) return "int";
            if (type == typeof(float)) return "float";
            if (type == typeof(bool)) return "bool";
            if (type == typeof(string)) return "string";
            return type.Name;
        }
    }
}

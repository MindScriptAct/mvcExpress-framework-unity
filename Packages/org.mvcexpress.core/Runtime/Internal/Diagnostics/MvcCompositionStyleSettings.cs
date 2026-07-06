using mvcExpress.Logging;
using System.Collections.Generic;
using UnityEngine;

namespace mvcExpress
{
    /// <summary>
    /// Registration styles supported by mvcExpress composition.
    /// </summary>
    public enum MvcCompositionStyle
    {
        Unity = 0,
        Attribute = 1,
        Code = 2
    }

    /// <summary>
    /// Stores editor preferences that allow teams to discourage or disable composition styles.
    /// </summary>
    public static class MvcCompositionStyleSettings
    {
        private const string RootKey = "org.mvcexpress.core";

        private static string MakeKey(string leafKey) => $"{RootKey}.{leafKey}";

        public static string GetLabel(MvcCompositionStyle style)
        {
            return style switch
            {
                MvcCompositionStyle.Unity => "Unity Registration",
                MvcCompositionStyle.Attribute => "Attribute Registration",
                MvcCompositionStyle.Code => "Code Registration",
                _ => style.ToString()
            };
        }

        public static string GetSoftSettingLabel(MvcCompositionStyle style)
        {
            return $"Allow {GetLabel(style)}";
        }

        public static string GetHardSettingLabel(MvcCompositionStyle style)
        {
            return $"Compile {GetLabel(style)}";
        }

#if UNITY_EDITOR
        public static bool IsSoftAllowed(MvcCompositionStyle style)
        {
            return UnityEditor.EditorPrefs.GetBool(GetSoftKey(style), true);
        }

        public static void SetSoftAllowed(MvcCompositionStyle style, bool value)
        {
            UnityEditor.EditorPrefs.SetBool(GetSoftKey(style), value);
        }

        public static bool IsHardIncluded(MvcCompositionStyle style)
        {
            return UnityEditor.EditorPrefs.GetBool(GetHardKey(style), true);
        }

        public static void SetHardIncluded(MvcCompositionStyle style, bool value)
        {
            UnityEditor.EditorPrefs.SetBool(GetHardKey(style), value);
        }

        private static string GetSoftKey(MvcCompositionStyle style)
        {
            return style switch
            {
                MvcCompositionStyle.Unity => MakeKey("composition.soft.unity"),
                MvcCompositionStyle.Attribute => MakeKey("composition.soft.attribute"),
                MvcCompositionStyle.Code => MakeKey("composition.soft.code"),
                _ => MakeKey($"composition.soft.{style}")
            };
        }

        private static string GetHardKey(MvcCompositionStyle style)
        {
            return style switch
            {
                MvcCompositionStyle.Unity => MakeKey("composition.hard.unity"),
                MvcCompositionStyle.Attribute => MakeKey("composition.hard.attribute"),
                MvcCompositionStyle.Code => MakeKey("composition.hard.code"),
                _ => MakeKey($"composition.hard.{style}")
            };
        }

        /// <summary>
        /// Returns true when a style counts as restricted - i.e. either soft or hard is disabled.
        /// Two restricted styles locks the third.
        /// </summary>
        public static bool IsRestricted(MvcCompositionStyle style)
            => !IsSoftAllowed(style) || !IsHardIncluded(style);

        /// <summary>
        /// Returns the one style that is currently locked because the other two are restricted.
        /// Returns null when no style is locked.
        /// </summary>
        public static MvcCompositionStyle? GetLockedStyle()
        {
            var styles = new[] { MvcCompositionStyle.Unity, MvcCompositionStyle.Attribute, MvcCompositionStyle.Code };
            var restricted = System.Array.FindAll(styles, s => IsRestricted(s));
            if (restricted.Length >= 2)
            {
                foreach (var s in styles)
                    if (!IsRestricted(s)) return s;
            }
            return null;
        }
#else
        public static bool IsSoftAllowed(MvcCompositionStyle style) => true;
        public static void SetSoftAllowed(MvcCompositionStyle style, bool value) { }
        public static bool IsHardIncluded(MvcCompositionStyle style) => true;
        public static void SetHardIncluded(MvcCompositionStyle style, bool value) { }
#endif
    }

    /// <summary>
    /// Emits one-time editor warnings when code uses a disabled composition style.
    /// </summary>
    internal static class MvcCompositionStyleWarning
    {
#if UNITY_EDITOR
        private static readonly HashSet<string> ShownWarnings = new HashSet<string>();

        public static void WarnIfDisabled(
            MvcCompositionStyle style,
            string usage,
            Object context = null)
        {
            if (MvcCompositionStyleSettings.IsSoftAllowed(style))
            {
                return;
            }

            var key = $"{style}:{usage}";
            if (!ShownWarnings.Add(key))
            {
                return;
            }

            var label = MvcCompositionStyleSettings.GetLabel(style);
            var softLabel = MvcCompositionStyleSettings.GetSoftSettingLabel(style);
            var message =
                $"[mvcExpress] {label} is disabled in Project Settings > mvcExpress > Composition, " +
                $"but {usage} is using it.\n" +
                $"Enable '{softLabel}' or move this setup to an enabled registration style. " +
                "This is an editor-only warning; runtime behavior is unchanged.";

            MvcDebug.LogWarning(message);
        }
#else
        public static void WarnIfDisabled(MvcCompositionStyle style, string usage, Object context = null) { }
#endif
    }
}

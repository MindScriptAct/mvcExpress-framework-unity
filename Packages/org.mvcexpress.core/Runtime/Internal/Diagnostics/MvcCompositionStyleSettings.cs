using mvcExpress.Logging;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// Stores project-wide policy that allows teams to discourage or disable composition styles.
    /// </summary>
    public static class MvcCompositionStyleSettings
    {
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
            return MvcCompositionProjectSettings.instance.GetSoftAllowed(style);
        }

        public static void SetSoftAllowed(MvcCompositionStyle style, bool value)
        {
            MvcCompositionProjectSettings.instance.SetSoftAllowed(style, value);
        }

        public static bool IsHardIncluded(MvcCompositionStyle style)
        {
            return MvcCompositionProjectSettings.instance.GetHardIncluded(style);
        }

        public static void SetHardIncluded(MvcCompositionStyle style, bool value)
        {
            MvcCompositionProjectSettings.instance.SetHardIncluded(style, value);
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

#if UNITY_EDITOR
    /// <summary>
    /// Project-owned, version-controlled backing store for <see cref="MvcCompositionStyleSettings"/>'s
    /// soft/hard composition-style policy. Lives under <c>ProjectSettings/</c> (like
    /// <c>EditorBuildSettings.asset</c>/<c>TagManager.asset</c>) so it is committed to source control
    /// and shared by the whole team, instead of the per-machine <c>EditorPrefs</c> this used to sit
    /// on - see COR-audit CMP-04/EDT-01. No migration from any prior per-machine EditorPrefs value is
    /// performed; a project upgrading from the EditorPrefs-backed version starts this asset at
    /// defaults (all styles fully allowed) as if configured for the first time.
    /// </summary>
    [UnityEditor.FilePath("ProjectSettings/mvcExpressCompositionSettings.asset", UnityEditor.FilePathAttribute.Location.ProjectFolder)]
    internal sealed class MvcCompositionProjectSettings : UnityEditor.ScriptableSingleton<MvcCompositionProjectSettings>
    {
        [SerializeField] private bool _softAllowedUnity = true;
        [SerializeField] private bool _softAllowedAttribute = true;
        [SerializeField] private bool _softAllowedCode = true;

        [SerializeField] private bool _hardIncludedUnity = true;
        [SerializeField] private bool _hardIncludedAttribute = true;
        [SerializeField] private bool _hardIncludedCode = true;

        public bool GetSoftAllowed(MvcCompositionStyle style)
        {
            switch (style)
            {
                case MvcCompositionStyle.Unity: return _softAllowedUnity;
                case MvcCompositionStyle.Attribute: return _softAllowedAttribute;
                case MvcCompositionStyle.Code: return _softAllowedCode;
                default: return true;
            }
        }

        public void SetSoftAllowed(MvcCompositionStyle style, bool value)
        {
            switch (style)
            {
                case MvcCompositionStyle.Unity: _softAllowedUnity = value; break;
                case MvcCompositionStyle.Attribute: _softAllowedAttribute = value; break;
                case MvcCompositionStyle.Code: _softAllowedCode = value; break;
                default: return;
            }
            Save(true);
        }

        public bool GetHardIncluded(MvcCompositionStyle style)
        {
            switch (style)
            {
                case MvcCompositionStyle.Unity: return _hardIncludedUnity;
                case MvcCompositionStyle.Attribute: return _hardIncludedAttribute;
                case MvcCompositionStyle.Code: return _hardIncludedCode;
                default: return true;
            }
        }

        public void SetHardIncluded(MvcCompositionStyle style, bool value)
        {
            switch (style)
            {
                case MvcCompositionStyle.Unity: _hardIncludedUnity = value; break;
                case MvcCompositionStyle.Attribute: _hardIncludedAttribute = value; break;
                case MvcCompositionStyle.Code: _hardIncludedCode = value; break;
                default: return;
            }
            Save(true);
        }
    }
#endif

    /// <summary>
    /// Emits one-time editor warnings when code uses a disabled composition style.
    /// </summary>
    /// <remarks>
    /// This class used to have two <c>#if UNITY_EDITOR</c>-split bodies for <see cref="WarnIfDisabled"/> - a real
    /// one and an empty no-op. That still left a real call instruction at every call site in every
    /// build, and since C# always evaluates a method's arguments before the call regardless of what
    /// the callee does, every call site's interpolated <c>usage</c> string (allocating, building
    /// caller-specific text via <c>typeof(...).Name</c>/<c>GetType().Name</c>) was still built and
    /// then thrown away on a no-op in production. <c>[Conditional("UNITY_EDITOR")]</c>, matching the
    /// pattern <see cref="mvcExpress.Logging.MvcLogInternal"/> already uses, makes the compiler
    /// remove the entire call statement - including argument evaluation - at every non-Editor call
    /// site, so there is exactly one implementation instead of two to keep in sync.
    /// </remarks>
    internal static class MvcCompositionStyleWarning
    {
        private static readonly HashSet<string> ShownWarnings = new HashSet<string>();

        [Conditional("UNITY_EDITOR")]
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
    }
}

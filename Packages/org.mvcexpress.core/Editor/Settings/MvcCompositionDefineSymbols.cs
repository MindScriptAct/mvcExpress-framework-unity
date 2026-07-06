using UnityEditor;
#if UNITY_2021_2_OR_NEWER
using UnityEditor.Build;
#endif
using System.Collections.Generic;
using System.Linq;

namespace mvcExpress.Editor.Settings
{
    internal static class MvcCompositionDefineSymbols
    {
        private const string NoUnity     = "MVC_EXPRESS_NO_UNITY";
        private const string NoAttribute = "MVC_EXPRESS_NO_ATTRIBUTE";
        private const string NoCode      = "MVC_EXPRESS_NO_CODE";

        internal static string GetSymbol(MvcCompositionStyle style) => style switch
        {
            MvcCompositionStyle.Unity     => NoUnity,
            MvcCompositionStyle.Attribute => NoAttribute,
            MvcCompositionStyle.Code      => NoCode,
            _                             => null
        };

        /// <summary>
        /// Adds or removes the hard-enforcement define symbol for the given style.
        /// </summary>
        internal static void Apply(MvcCompositionStyle style, bool hardIncluded)
        {
            var symbol = GetSymbol(style);
            if (symbol == null) return;

#if UNITY_2021_2_OR_NEWER
            var namedTarget = NamedBuildTarget.FromBuildTargetGroup(EditorUserBuildSettings.selectedBuildTargetGroup);
            var existing = PlayerSettings.GetScriptingDefineSymbols(namedTarget);
#else
            var target = EditorUserBuildSettings.selectedBuildTargetGroup;
            var existing = PlayerSettings.GetScriptingDefineSymbolsForGroup(target);
#endif
            var defines = existing
                .Split(';')
                .Select(d => d.Trim())
                .Where(d => !string.IsNullOrEmpty(d))
                .ToList();

            if (hardIncluded)
                defines.Remove(symbol);
            else if (!defines.Contains(symbol))
                defines.Add(symbol);

#if UNITY_2021_2_OR_NEWER
            PlayerSettings.SetScriptingDefineSymbols(namedTarget, string.Join(";", defines));
#else
            PlayerSettings.SetScriptingDefineSymbolsForGroup(target, string.Join(";", defines));
#endif
        }

        /// <summary>
        /// Synchronises ALL three define symbols with current stored hard settings.
        /// Call on domain reload or settings window open to keep symbols consistent.
        /// </summary>
        internal static void SyncAll()
        {
            Apply(MvcCompositionStyle.Unity,     MvcCompositionStyleSettings.IsHardIncluded(MvcCompositionStyle.Unity));
            Apply(MvcCompositionStyle.Attribute, MvcCompositionStyleSettings.IsHardIncluded(MvcCompositionStyle.Attribute));
            Apply(MvcCompositionStyle.Code,      MvcCompositionStyleSettings.IsHardIncluded(MvcCompositionStyle.Code));
        }
    }

    [InitializeOnLoad]
    internal static class MvcCompositionDefineSymbolsSyncer
    {
        static MvcCompositionDefineSymbolsSyncer()
        {
            MvcCompositionDefineSymbols.SyncAll();
        }
    }
}

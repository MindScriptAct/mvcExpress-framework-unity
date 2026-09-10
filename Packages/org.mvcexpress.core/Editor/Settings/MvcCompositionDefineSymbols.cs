using UnityEditor;
#if UNITY_2021_2_OR_NEWER
using UnityEditor.Build;
#endif
using System;
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
        /// Adds or removes the hard-enforcement define symbol for the given style, across every
        /// build target group the Editor knows about - not just whichever one happens to be
        /// selected right now. The settings UI presents this as one project-wide policy (see
        /// EDT-05), so it must actually apply project-wide, or switching the active build target
        /// after configuring it silently re-exposes a style that was supposed to be excluded.
        /// </summary>
        internal static void Apply(MvcCompositionStyle style, bool hardIncluded)
        {
            var symbol = GetSymbol(style);
            if (symbol == null) return;

            foreach (BuildTargetGroup group in Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (group == BuildTargetGroup.Unknown) continue;
                ApplyToGroup(group, symbol, hardIncluded);
            }
        }

        // BuildTargetGroup also enumerates targets this Editor install has no module for (or that
        // are obsolete/removed) - PlayerSettings throws for those. There is nothing to define for a
        // target this Editor can't build anyway, so skip it rather than aborting every other group.
        private static void ApplyToGroup(BuildTargetGroup group, string symbol, bool hardIncluded)
        {
            try
            {
#if UNITY_2021_2_OR_NEWER
                var namedTarget = NamedBuildTarget.FromBuildTargetGroup(group);
                var existing = PlayerSettings.GetScriptingDefineSymbols(namedTarget);
#else
                var existing = PlayerSettings.GetScriptingDefineSymbolsForGroup(group);
#endif
                if (!TryComputeUpdatedDefines(existing, symbol, hardIncluded, out var updated)) return;

#if UNITY_2021_2_OR_NEWER
                PlayerSettings.SetScriptingDefineSymbols(namedTarget, updated);
#else
                PlayerSettings.SetScriptingDefineSymbolsForGroup(group, updated);
#endif
            }
            catch (Exception)
            {
                // Not a valid/installed target on this Editor - nothing to keep in sync.
            }
        }

        /// <summary>
        /// Pure define-list edit, split out from <see cref="ApplyToGroup"/> so it can be unit
        /// tested without touching real <c>PlayerSettings</c> state. Returns false (and leaves
        /// <paramref name="updatedDefines"/> equal to <paramref name="existingDefines"/>) when the
        /// symbol is already in the state <paramref name="hardIncluded"/> implies, so callers can
        /// skip writing anything back.
        /// </summary>
        internal static bool TryComputeUpdatedDefines(string existingDefines, string symbol, bool hardIncluded, out string updatedDefines)
        {
            var defines = (existingDefines ?? string.Empty)
                .Split(';')
                .Select(d => d.Trim())
                .Where(d => !string.IsNullOrEmpty(d))
                .ToList();

            bool changed;
            if (hardIncluded)
            {
                changed = defines.Remove(symbol);
            }
            else if (!defines.Contains(symbol))
            {
                defines.Add(symbol);
                changed = true;
            }
            else
            {
                changed = false;
            }

            updatedDefines = changed ? string.Join(";", defines) : existingDefines;
            return changed;
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

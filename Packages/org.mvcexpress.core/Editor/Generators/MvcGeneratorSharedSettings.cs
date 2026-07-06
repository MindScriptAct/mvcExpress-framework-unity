using System.Collections.Generic;
using UnityEditor;

namespace mvcExpress.Editor.Generators
{
    // Settings that are identical across ALL generator windows.
    // Reading or writing any property here updates all open generator windows.
    /// <summary>
    /// EditorPrefs-backed settings (namespace and documentation options) shared across all generator windows, so changing one setting updates every open window.
    /// </summary>
    internal static class MvcGeneratorSharedSettings
    {
        private const string Prefix = "MvcGenerators";
        private const int MaxHistory = 10;

        private static string Key(string name) =>
            MvcScriptGeneratorWindowBase.MakeSharedKey($"{Prefix}_{name}");

        // ── Namespace / documentation settings ──────────────────────────────

        public static bool WithDocumentation
        {
            get => EditorPrefs.GetBool(Key("WithDocumentation"), false);
            set => EditorPrefs.SetBool(Key("WithDocumentation"), value);
        }

        public static bool UseNamespace
        {
            get => EditorPrefs.GetBool(Key("UseNamespace"), true);
            set => EditorPrefs.SetBool(Key("UseNamespace"), value);
        }

        public static bool UseCustomNamespace
        {
            get => EditorPrefs.GetBool(Key("UseCustomNamespace"), false);
            set => EditorPrefs.SetBool(Key("UseCustomNamespace"), value);
        }

        public static string CustomNamespace
        {
            get => EditorPrefs.GetString(Key("CustomNamespace"), string.Empty);
            set => EditorPrefs.SetString(Key("CustomNamespace"), value);
        }

        public static string NamespacePrefix
        {
            get => EditorPrefs.GetString(Key("NamespacePrefix"), string.Empty);
            set => EditorPrefs.SetString(Key("NamespacePrefix"), value);
        }

        public static int SkipFolderLevels
        {
            get => EditorPrefs.GetInt(Key("SkipFolderLevels"), 0);
            set => EditorPrefs.SetInt(Key("SkipFolderLevels"), value);
        }

        // ── Namespace history (shared — one list for all windows) ────────────

        private static List<string> _nsHistory;

        public static List<string> NamespaceHistory
        {
            get { if (_nsHistory == null) LoadNsHistory(); return _nsHistory; }
        }

        private static void LoadNsHistory()
        {
            _nsHistory = new List<string>();
            var raw = EditorPrefs.GetString(Key("NamespaceHistory"), string.Empty);
            if (string.IsNullOrEmpty(raw)) return;
            foreach (var e in raw.Split('|'))
            {
                var t = e.Trim();
                if (!string.IsNullOrEmpty(t)) _nsHistory.Add(t);
            }
        }

        public static void PushNamespaceToHistory(string ns)
        {
            if (string.IsNullOrWhiteSpace(ns)) return;
            var h = NamespaceHistory;
            h.Remove(ns);
            h.Insert(0, ns);
            if (h.Count > MaxHistory) h.RemoveAt(h.Count - 1);
            EditorPrefs.SetString(Key("NamespaceHistory"), string.Join("|", h));
        }

        // ── Validation ───────────────────────────────────────────────────────

        public static bool IsValidNamespace(string ns)
        {
            if (string.IsNullOrWhiteSpace(ns)) return false;
            if (ns.StartsWith(".") || ns.EndsWith(".") || ns.Contains("..")) return false;
            foreach (var seg in ns.Split('.'))
            {
                if (string.IsNullOrEmpty(seg)) return false;
                if (char.IsDigit(seg[0])) return false;
                foreach (var c in seg)
                    if (!char.IsLetterOrDigit(c) && c != '_') return false;
            }
            return true;
        }
    }
}

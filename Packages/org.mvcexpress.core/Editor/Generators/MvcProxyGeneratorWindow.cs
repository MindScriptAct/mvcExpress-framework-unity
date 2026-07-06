using System.IO;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.Generators
{
    /// <summary>
    /// Editor window that generates a new plain C# Proxy script on disk.
    /// </summary>
    internal sealed class MvcProxyGeneratorWindow : MvcScriptGeneratorWindowBase
    {
        protected override string PrefPrefix      => "MvcProxyGen";
        protected override string WindowTitle     => "Proxy Generator";
        protected override Vector2 WindowSize     => new Vector2(SettingsPanelWidth + BarWidth, 380f);
        protected override string DefaultFileName => "NewProxy.cs";
        protected override string DefaultClassName => string.Empty;
        protected override string FolderActorKey  => "MvcProxy";

        private string _namePrefix = string.Empty;

        public static void ShowWindow()
        {
            var w = GetWindow<MvcProxyGeneratorWindow>(true, "Proxy Generator", true);
            w.EnsureWindowSizing();
            if (w.position.width <= SettingsPanelWidth + BarWidth + 20f)
            {
                var r = w.position;
                r.width  = SettingsPanelWidth + BarWidth + 340f;
                r.height = Mathf.Max(r.height, 420f);
                w.position = r;
            }
            w.Show();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (IsRestoredFromGeneration) return;
            _namePrefix = string.Empty;
            className   = BuildClassName(_namePrefix);
        }

        private void OnGUI()
        {
            if (DrawIfGenerating()) return;
            InitBaseStyles();

            GUILayout.BeginArea(new Rect(0f, 0f, GetMainWidth(), position.height));

            EditorGUILayout.Space(8f);
            DrawNameRow();
            EditorGUILayout.Space(5f);
            DrawNamespaceFieldBase();
            EditorGUILayout.Space(8f);
            DrawPreviewBoxBase(BuildPreviewText());
            EditorGUILayout.Space(6f);
            DrawCreateBtn();
            EditorGUILayout.Space(8f);

            GUILayout.EndArea();
            DrawSettingsPanelBase(GetSettingsLeft());
            DrawActivationBarBase();
        }

        private void DrawNameRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Name", GUILayout.Width(60));
                var prev = _namePrefix;
                _namePrefix = EditorGUILayout.TextField(_namePrefix ?? string.Empty);
                if (_namePrefix != prev) className = BuildClassName(_namePrefix);
                EditorGUILayout.LabelField("Proxy", GUILayout.Width(55));
            }
            if (string.IsNullOrWhiteSpace(_namePrefix))
                EditorGUILayout.HelpBox("Enter a proxy name.", MessageType.Error);

            DrawFolderFieldBase();
        }

        private void DrawCreateBtn()
        {
            bool nsInvalid = useNamespace && useCustomNamespace && !MvcGeneratorSharedSettings.IsValidNamespace(customNamespace);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_namePrefix) || nsInvalid))
            {
                if (GUILayout.Button("Create Proxy", GUILayout.Height(36)))
                {
                    PushFolderToHistory(selectedFolderPath);
                    SavePreferences();
                    className = BuildClassName(_namePrefix);
                    CreateScriptInSelectedFolder(GetTemplate);
                    BeginGeneration(BuildClassName(_namePrefix));
                }
            }
        }

        private string BuildPreviewText()
        {
            var cn  = BuildClassName(_namePrefix);
            var ns  = ResolveNamespaceForPath(
                Path.Combine(string.IsNullOrWhiteSpace(selectedFolderPath) ? "Assets" : selectedFolderPath,
                    cn + ".cs").Replace("\\", "/"));
            var doc = withDocumentation ? "    /// <summary>Code-only proxy (model actor).</summary>\n" : string.Empty;
            var init = (withDocumentation ? "        /// <summary>Called when proxy is ready.</summary>\n" : string.Empty) +
                       "        protected override void OnInitialized() { }";

            if (string.IsNullOrEmpty(ns))
                return doc + $"public class {cn} : Proxy\n" + "{\n" + init + "\n}";

            return $"namespace {ns}\n" + "{\n" + doc +
                   $"    public class {cn} : Proxy\n" + "    {\n" + init + "\n    }\n}";
        }

        private string GetTemplate(string _)
        {
            var cn = BuildClassName(_namePrefix);
            return "using mvcExpress;\n\n" +
                   "${NAMESPACE_BEGIN}\n" +
                   (withDocumentation ? "    /// <summary>Code-only proxy (model actor).</summary>\n" : string.Empty) +
                   $"    public class {cn} : Proxy\n" +
                   "    {\n" +
                   (withDocumentation ? "        /// <summary>Called when proxy is ready.</summary>\n" : string.Empty) +
                   "        protected override void OnInitialized()\n" +
                   "        {\n" +
                   "        }\n" +
                   "    }\n" +
                   "${NAMESPACE_END}\n";
        }

        private static string BuildClassName(string prefix)
        {
            var p = (prefix ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(p)) return "Proxy";
            if (p.EndsWith("Proxy", System.StringComparison.Ordinal)) return p;
            return p + "Proxy";
        }
    }
}

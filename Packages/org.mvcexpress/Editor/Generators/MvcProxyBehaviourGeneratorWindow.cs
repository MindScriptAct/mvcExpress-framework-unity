using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.Generators
{
    internal sealed class MvcProxyBehaviourGeneratorWindow : MvcScriptGeneratorWindowBase
    {
        protected override string PrefPrefix       => "MvcProxyBehaviourGen";
        protected override string WindowTitle      => "ProxyBehaviour Generator";
        protected override Vector2 WindowSize      => new Vector2(SettingsPanelWidth + BarWidth, 420f);
        protected override string DefaultFileName  => "NewProxyBehaviour.cs";
        protected override string DefaultClassName => string.Empty;
        // Shares folder history with MvcProxyGeneratorWindow — same actor type.
        protected override string FolderActorKey   => "MvcProxy";

        private string _namePrefix    = string.Empty;
        private bool   _addInterface  = false;
        private bool   _tryAddToContainer = true;
        private bool   _createAsChild = false;
        private GameObject _parent;

        // 1 = parent IS a ProxyRegistryBehaviour  (Create-as-child toggle enabled, defaults on)
        // 2 = parent is INSIDE a ProxyRegistryBehaviour (attach to current GO, auto-name)
        // 3 = unrelated GO / no context
        // 0 = Project mode
        private int _contextCase = 0;

        private const string TryAddKey = "MvcGenerators_MvcProxy_TryAddToContainer";

        // ── Open ──────────────────────────────────────────────────────────────

        internal static void ShowWindow()
        {
            var w = GetWindow<MvcProxyBehaviourGeneratorWindow>(true, "ProxyBehaviour Generator", true);
            w._parent = null;
            w._contextCase = 0;
            w.InitializeContext(GeneratorContextMode.Project);
            w.EnsureWindowSizing();
            OpenAt(w);
            w.Show();
        }

        internal static void ShowWindow(GameObject parent)
        {
            var w = GetWindow<MvcProxyBehaviourGeneratorWindow>(true, "ProxyBehaviour Generator", true);
            w._parent = parent;
            w.InitializeContext(GeneratorContextMode.Hierarchy);
            w.DetectContext();
            w.EnsureWindowSizing();
            OpenAt(w);
            w.Show();
        }

        private static void OpenAt(MvcProxyBehaviourGeneratorWindow w)
        {
            if (w.position.width <= SettingsPanelWidth + BarWidth + 20f)
            {
                var r = w.position;
                r.width  = SettingsPanelWidth + BarWidth + 340f;
                r.height = Mathf.Max(r.height, 460f);
                w.position = r;
            }
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            if (IsRestoredFromGeneration) return;
            _namePrefix        = string.Empty;
            _addInterface      = EditorPrefs.GetBool(MakeSharedKey($"{PrefPrefix}_AddInterface"), false);
            _tryAddToContainer = EditorPrefs.GetBool(MakeSharedKey(TryAddKey), true);
            className          = BuildClassName(_namePrefix);
        }

        // ── Context detection ─────────────────────────────────────────────────

        private void DetectContext()
        {
            if (_parent == null) { _contextCase = 3; _createAsChild = false; return; }

            if (HasProxyRegistry(_parent))
            {
                _contextCase = 1;
                _createAsChild = true;
            }
            else if (ParentHasProxyRegistry(_parent.transform))
            {
                _contextCase = 2;
                _createAsChild = false;
                _namePrefix = StripSuffix(_parent.name, "ProxyBehaviour", "Proxy");
                className = BuildClassName(_namePrefix);
            }
            else
            {
                _contextCase = 3;
                _createAsChild = false;
            }
        }

        // Checks both module-scoped and global proxy registries.
        private static bool HasProxyRegistry(GameObject go) =>
            go.GetComponent<ProxyRegistryBehaviour>()       != null ||
            go.GetComponent<GlobalProxyRegistryBehaviour>() != null;

        private static bool ParentHasProxyRegistry(Transform t)
        {
            var p = t.parent;
            while (p != null) { if (HasProxyRegistry(p.gameObject)) return true; p = p.parent; }
            return false;
        }

        private static string StripSuffix(string name, string longSuffix, string shortSuffix)
        {
            if (name.EndsWith(longSuffix,  StringComparison.Ordinal)) return name.Substring(0, name.Length - longSuffix.Length);
            if (name.EndsWith(shortSuffix, StringComparison.Ordinal)) return name.Substring(0, name.Length - shortSuffix.Length);
            return name;
        }

        // ── Settings panel extras ─────────────────────────────────────────────

        protected override void DrawWindowSettingsItems()
        {
            GUILayout.Label("ProxyBehaviour Options", SettingsSectionStyle);
            using (new EditorGUI.DisabledScope(_parent == null))
            {
                DrawPanelToggle(ref _tryAddToContainer, "Try Add to Container",
                    _parent == null
                        ? "Not applicable when opened from the Project folder."
                        : "Automatically registers this proxy in the nearest ProxyRegistryBehaviour.");
            }
        }

        // ── OnGUI ─────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (DrawIfGenerating()) return;
            InitBaseStyles();

            GUILayout.BeginArea(new Rect(0f, 0f, GetMainWidth(), position.height));

            if (_contextCase == 3 && _parent != null)
            {
                EditorGUILayout.HelpBox(
                    "For best results, create this ProxyBehaviour on a proxy registry " +
                    "(ProxyRegistryBehaviour or GlobalProxyRegistryBehaviour) or as a direct child of one. " +
                    "The script will still be attached to the selected GameObject.",
                    MessageType.Warning);
            }
            else if (_contextCase == 2)
            {
                EditorGUILayout.HelpBox(
                    $"Name auto-filled from selected GameObject '{_parent?.name}'. " +
                    "The script will be attached to this GameObject.",
                    MessageType.Info);
            }

            if (_parent != null)
            {
                EditorGUILayout.Space(6f);
                DrawCreateAsChildToggle(ref _createAsChild);
            }

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
                EditorGUILayout.LabelField("ProxyBehaviour", GUILayout.Width(110));
            }
            if (string.IsNullOrWhiteSpace(_namePrefix))
                EditorGUILayout.HelpBox("Enter a ProxyBehaviour name.", MessageType.Error);

            _addInterface = EditorGUILayout.ToggleLeft("Add Interface", _addInterface);

            DrawFolderFieldBase();
        }

        private void DrawCreateBtn()
        {
            bool nsInvalid = useNamespace && useCustomNamespace && !MvcGeneratorSharedSettings.IsValidNamespace(customNamespace);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_namePrefix) || nsInvalid))
            {
                if (GUILayout.Button("Create ProxyBehaviour", GUILayout.Height(36)))
                {
                    EditorPrefs.SetBool(MakeSharedKey($"{PrefPrefix}_AddInterface"), _addInterface);
                    EditorPrefs.SetBool(MakeSharedKey(TryAddKey), _tryAddToContainer);
                    PushFolderToHistory(selectedFolderPath);
                    SavePreferences();

                    className = BuildClassName(_namePrefix);
                    var scriptPath = CreateScriptInSelectedFolderReturningPath(GetTemplate);
                    if (string.IsNullOrEmpty(scriptPath)) return;

                    var attachTarget = ResolveAttachTarget();
                    if (attachTarget != null)
                        MvcHierarchyScaffoldUtility.AddComponentAfterCompile(
                            attachTarget, GetFullTypeName(scriptPath),
                            _tryAddToContainer
                                ? MvcHierarchyScaffoldUtility.PostAddAction.AddProxyToModelContainer
                                : MvcHierarchyScaffoldUtility.PostAddAction.None);
                    BeginGeneration(className, attachTarget?.name);
                }
            }
        }

        private GameObject ResolveAttachTarget()
        {
            if (_parent == null) return null;
            if (_createAsChild)
            {
                var child = new GameObject(className);
                Undo.RegisterCreatedObjectUndo(child, "Create mvcExpress ProxyBehaviour");
                GameObjectUtility.SetParentAndAlign(child, _parent);
                Selection.activeGameObject = child;
                return child;
            }
            Selection.activeGameObject = _parent;
            return _parent;
        }

        private string GetFullTypeName(string assetPath)
        {
            var ns = ResolveNamespaceForPath(assetPath);
            return string.IsNullOrEmpty(ns) ? className : ns + "." + className;
        }

        // ── Preview ───────────────────────────────────────────────────────────

        private string BuildPreviewText()
        {
            var cn    = BuildClassName(_namePrefix);
            var iface = "I" + cn;
            var ns    = ResolveNamespaceForPath(
                Path.Combine(string.IsNullOrWhiteSpace(selectedFolderPath) ? "Assets" : selectedFolderPath,
                    cn + ".cs").Replace("\\", "/"));
            var doc   = withDocumentation ? "    /// <summary>MonoBehaviour proxy (model actor).</summary>\n" : string.Empty;
            var decl  = _addInterface ? $"public class {cn} : ProxyBehaviour, {iface}" : $"public class {cn} : ProxyBehaviour";
            var init  = (withDocumentation ? "        /// <summary>Called when proxy is ready.</summary>\n" : string.Empty)
                      + "        protected override void OnInitialized() { }";
            var ifaceBlock = _addInterface ? $"\n    internal interface {iface} {{ }}" : string.Empty;

            if (string.IsNullOrEmpty(ns))
                return doc + $"public class {cn} : ProxyBehaviour\n" + "{\n" + init + "\n}" + ifaceBlock;
            return $"namespace {ns}\n" + "{\n" + doc + $"    {decl}\n" + "    {\n" + init + "\n    }" + ifaceBlock + "\n}";
        }

        // ── Template ──────────────────────────────────────────────────────────

        private string GetTemplate(string _)
        {
            var cn    = BuildClassName(_namePrefix);
            var iface = "I" + cn;
            var doc   = withDocumentation ? "    /// <summary>MonoBehaviour proxy (model actor).</summary>\n" : string.Empty;
            var decl  = _addInterface ? $"    public class {cn} : ProxyBehaviour, {iface}" : $"    public class {cn} : ProxyBehaviour";
            var init  = (withDocumentation ? "        /// <summary>Called when proxy is ready.</summary>\n" : string.Empty)
                      + "        protected override void OnInitialized()\n        {\n        }\n";
            var ifaceBlock = _addInterface
                ? "\n" + (withDocumentation ? $"    /// <summary>Interface for {cn}.</summary>\n" : string.Empty)
                  + $"    internal interface {iface}\n    {{\n    }}\n"
                : string.Empty;

            return "using mvcExpress;\n" +
                   "using UnityEngine;\n\n" +
                   "${NAMESPACE_BEGIN}\n" +
                   doc + decl + "\n    {\n" + init + "    }\n" +
                   ifaceBlock +
                   "${NAMESPACE_END}\n";
        }

        // ── Scaffold callback ─────────────────────────────────────────────────

        internal static void TryAddProxyToContainerForScaffold(Transform from, ProxyBehaviour proxy)
        {
            var model = FindParentRegistry<ProxyRegistryBehaviour>(from);
            if (model == null) return;

            var so  = new SerializedObject(model);
            var arr = so.FindProperty("_proxyMappings");
            if (arr == null || !arr.isArray) return;
            for (int i = 0; i < arr.arraySize; i++)
            {
                var p = arr.GetArrayElementAtIndex(i).FindPropertyRelative("Proxy");
                if (p != null && p.objectReferenceValue == proxy) return;
            }
            int idx = arr.arraySize;
            arr.InsertArrayElementAtIndex(idx);
            var el = arr.GetArrayElementAtIndex(idx);
            el.FindPropertyRelative("Proxy").objectReferenceValue        = proxy;
            el.FindPropertyRelative("RegisterToLogic").boolValue         = true;
            el.FindPropertyRelative("RegisterToView").boolValue          = false;
            el.FindPropertyRelative("IsTransient").boolValue             = false;
            el.FindPropertyRelative("LogicTypeName").stringValue         = string.Empty;
            el.FindPropertyRelative("ViewTypeName").stringValue          = string.Empty;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(model);
        }

        private static T FindParentRegistry<T>(Transform from) where T : Component
        {
            var t = from;
            while (t != null) { var c = t.GetComponent<T>(); if (c != null) return c; t = t.parent; }
            return null;
        }

        private static string BuildClassName(string prefix)
        {
            var p = (prefix ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(p)) return "ProxyBehaviour";
            if (p.EndsWith("ProxyBehaviour", StringComparison.Ordinal)) return p;
            if (p.EndsWith("Proxy", StringComparison.Ordinal)) p = p.Substring(0, p.Length - 5);
            return p + "ProxyBehaviour";
        }
    }
}

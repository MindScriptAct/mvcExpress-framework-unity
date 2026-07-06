using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.Generators
{
    /// <summary>
    /// Editor window that generates a new MediatorBehaviour script and optionally attaches it to a GameObject in the scene hierarchy.
    /// </summary>
    internal sealed class MvcMediatorBehaviourGeneratorWindow : MvcScriptGeneratorWindowBase
    {
        protected override string PrefPrefix       => "MvcMediatorBehaviourGen";
        protected override string WindowTitle      => "MediatorBehaviour Generator";
        protected override Vector2 WindowSize      => new Vector2(SettingsPanelWidth + BarWidth, 420f);
        protected override string DefaultFileName  => "NewMediatorBehaviour.cs";
        protected override string DefaultClassName => string.Empty;
        protected override string FolderActorKey   => "MvcMediator";

        private string _namePrefix    = string.Empty;
        private bool   _addInterface  = false;
        private bool   _tryAddToContainer = true;

        // Free toggle — mediators are almost always attached to a view-tree GameObject
        // rather than created as a child, so this is not derived from _contextCase like
        // it is for Service/Proxy. Instead the user's last choice is simply remembered.
        private bool   _createAsChild = false;
        private GameObject _parent;

        // 1 = parent IS a MediatorRegistryBehaviour
        // 2 = parent is INSIDE a MediatorRegistryBehaviour (attach to current GO, auto-name)
        // 3 = unrelated GO / no context
        // 0 = Project mode
        private int _contextCase = 0;

        private const string TryAddKey = "MvcGenerators_MvcMediator_TryAddToContainer";

        // ── Open ──────────────────────────────────────────────────────────────

        internal static void ShowWindow()
        {
            var w = GetWindow<MvcMediatorBehaviourGeneratorWindow>(true, "MediatorBehaviour Generator", true);
            w._parent = null;
            w._contextCase = 0;
            w.InitializeContext(GeneratorContextMode.Project);
            w.EnsureWindowSizing();
            OpenAt(w);
            w.Show();
        }

        internal static void ShowWindow(GameObject parent)
        {
            var w = GetWindow<MvcMediatorBehaviourGeneratorWindow>(true, "MediatorBehaviour Generator", true);
            w._parent = parent;
            w.InitializeContext(GeneratorContextMode.Hierarchy);
            w.DetectContext();
            w.EnsureWindowSizing();
            OpenAt(w);
            w.Show();
        }

        private static void OpenAt(MvcMediatorBehaviourGeneratorWindow w)
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
            _createAsChild     = EditorPrefs.GetBool(MakeSharedKey($"{PrefPrefix}_CreateAsChild"), false);
            className          = Generator.BuildClassName(_namePrefix);
        }

        // ── Context detection ─────────────────────────────────────────────────

        private void DetectContext()
        {
            if (_parent == null) { _contextCase = 3; return; }

            if (_parent.GetComponent<MediatorRegistryBehaviour>() != null)
            {
                _contextCase = 1;
            }
            else if (HasRegistryInParents<MediatorRegistryBehaviour>(_parent.transform))
            {
                _contextCase = 2;
                _namePrefix = StripSuffix(_parent.name, "MediatorBehaviour", "Mediator");
                className = Generator.BuildClassName(_namePrefix);
            }
            else
            {
                _contextCase = 3;
            }
        }

        private static bool HasRegistryInParents<T>(Transform t) where T : Component
        {
            var p = t.parent;
            while (p != null) { if (p.GetComponent<T>() != null) return true; p = p.parent; }
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
            GUILayout.Label("MediatorBehaviour Options", SettingsSectionStyle);
            using (new EditorGUI.DisabledScope(_parent == null))
            {
                DrawPanelToggle(ref _tryAddToContainer, "Try Add to Container",
                    _parent == null
                        ? "Not applicable when opened from the Project folder."
                        : "Automatically registers this mediator in the nearest MediatorRegistryBehaviour.");
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
                    "For best results, create this MediatorBehaviour on a MediatorRegistryBehaviour " +
                    "or as a direct child of one. The script will still be attached to the selected GameObject.",
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
                if (_namePrefix != prev) className = Generator.BuildClassName(_namePrefix);
                EditorGUILayout.LabelField("MediatorBehaviour", GUILayout.Width(120));
            }
            if (string.IsNullOrWhiteSpace(_namePrefix))
                EditorGUILayout.HelpBox("Enter a MediatorBehaviour name.", MessageType.Error);

            _addInterface = EditorGUILayout.ToggleLeft("Add Interface", _addInterface);

            DrawFolderFieldBase();
        }

        private void DrawCreateBtn()
        {
            bool nsInvalid = useNamespace && useCustomNamespace && !MvcGeneratorSharedSettings.IsValidNamespace(customNamespace);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_namePrefix) || nsInvalid))
            {
                if (GUILayout.Button("Create MediatorBehaviour", GUILayout.Height(36)))
                {
                    EditorPrefs.SetBool(MakeSharedKey($"{PrefPrefix}_AddInterface"), _addInterface);
                    EditorPrefs.SetBool(MakeSharedKey(TryAddKey), _tryAddToContainer);
                    EditorPrefs.SetBool(MakeSharedKey($"{PrefPrefix}_CreateAsChild"), _createAsChild);
                    PushFolderToHistory(selectedFolderPath);
                    SavePreferences();

                    className = Generator.BuildClassName(_namePrefix);
                    var scriptPath = CreateScriptInSelectedFolderReturningPath(GetTemplate);
                    if (string.IsNullOrEmpty(scriptPath)) return;

                    var attachTarget = ResolveAttachTarget();
                    if (attachTarget != null)
                        MvcHierarchyScaffoldUtility.AddComponentAfterCompile(
                            attachTarget, GetFullTypeName(scriptPath),
                            _tryAddToContainer
                                ? MvcHierarchyScaffoldUtility.PostAddAction.AddMediatorToViewContainer
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
                Undo.RegisterCreatedObjectUndo(child, "Create mvcExpress MediatorBehaviour");
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
            return Generator.BuildPreviewText(_namePrefix, _addInterface, withDocumentation, selectedFolderPath, ResolveNamespaceForPath);
        }

        // ── Template ──────────────────────────────────────────────────────────

        private string GetTemplate(string _)
        {
            return Generator.Generate(_namePrefix, _addInterface, withDocumentation);
        }

        public static class Generator
        {
            public static string Generate(string namePrefix, bool addInterface, bool withDocumentation)
            {
                var cn    = BuildClassName(namePrefix);
                var iface = "I" + cn;
                var doc   = withDocumentation ? "    /// <summary>View actor bridging Unity UI to the framework.</summary>\n" : string.Empty;
                var decl  = addInterface ? $"    public class {cn} : MediatorBehaviour, {iface}" : $"    public class {cn} : MediatorBehaviour";
                var init  = (withDocumentation ? "        /// <summary>Subscribe to messages here.</summary>\n" : string.Empty)
                          + "        protected override void OnInitialized()\n        {\n        }\n";
                var ifaceBlock = addInterface
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

            public static string BuildClassName(string prefix)
            {
                var p = (prefix ?? string.Empty).Trim();
                if (string.IsNullOrEmpty(p)) return "MediatorBehaviour";
                if (p.EndsWith("MediatorBehaviour", StringComparison.Ordinal)) return p;
                if (p.EndsWith("Mediator", StringComparison.Ordinal)) p = p.Substring(0, p.Length - 8);
                return p + "MediatorBehaviour";
            }

            public static string BuildPreviewText(string namePrefix, bool addInterface, bool withDocumentation, string folderPath, Func<string, string> nsResolver)
            {
                var cn    = BuildClassName(namePrefix);
                var iface = "I" + cn;
                var ns    = nsResolver?.Invoke(
                    Path.Combine(string.IsNullOrWhiteSpace(folderPath) ? "Assets" : folderPath,
                        cn + ".cs").Replace("\\", "/"));
                var doc   = withDocumentation ? $"    /// <summary>View actor bridging Unity UI to the framework.</summary>\n" : string.Empty;
                var decl  = addInterface ? $"public class {cn} : MediatorBehaviour, {iface}" : $"public class {cn} : MediatorBehaviour";
                var init  = (withDocumentation ? "        /// <summary>Subscribe to messages here.</summary>\n" : string.Empty)
                          + "        protected override void OnInitialized() { }";
                var ifaceBlock = addInterface ? $"\n    internal interface {iface} {{ }}" : string.Empty;

                if (string.IsNullOrEmpty(ns))
                    return doc + $"public class {cn} : MediatorBehaviour\n" + "{\n" + init + "\n}" + ifaceBlock;
                return $"namespace {ns}\n" + "{\n" + doc + $"    {decl}\n" + "    {\n" + init + "\n    }" + ifaceBlock + "\n}";
            }
        }

        // ── Scaffold callback ─────────────────────────────────────────────────

        internal static void TryAddMediatorToContainerForScaffold(Transform from, MediatorBehaviour mediator)
        {
            if (mediator == null) return;
            var view = FindParentRegistry<MediatorRegistryBehaviour>(from);
            if (view == null) return;

            var so  = new SerializedObject(view);
            var arr = so.FindProperty("_sceneMediators");
            if (arr == null || !arr.isArray) return;
            for (int i = 0; i < arr.arraySize; i++)
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue == mediator) return;

            int idx = arr.arraySize;
            arr.InsertArrayElementAtIndex(idx);
            arr.GetArrayElementAtIndex(idx).objectReferenceValue = mediator;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(view);
        }

        private static T FindParentRegistry<T>(Transform from) where T : Component
        {
            var t = from;
            while (t != null) { var c = t.GetComponent<T>(); if (c != null) return c; t = t.parent; }
            return null;
        }


    }
}

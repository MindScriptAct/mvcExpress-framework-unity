using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.Generators
{
    /// <summary>
    /// Editor window that generates a new ServiceBehaviour script and optionally attaches it to a GameObject in the scene hierarchy.
    /// </summary>
    internal sealed class MvcServiceBehaviourGeneratorWindow : MvcScriptGeneratorWindowBase
    {
        protected override string PrefPrefix       => "MvcServiceBehaviourGen";
        protected override string WindowTitle      => "ServiceBehaviour Generator";
        protected override Vector2 WindowSize      => new Vector2(SettingsPanelWidth + BarWidth, 420f);
        protected override string DefaultFileName  => "NewServiceBehaviour.cs";
        protected override string DefaultClassName => string.Empty;
        protected override string FolderActorKey   => "MvcService";

        private string _namePrefix       = string.Empty;
        private bool   _addInterface     = false;
        private bool   _addIMvcLifecycle = false;
        private bool   _tryAddToContainer = true;
        private bool   _createAsChild    = false;
        private GameObject _parent;

        // 1 = parent IS a ServiceRegistryBehaviour  (Create-as-child toggle enabled, defaults on)
        // 2 = parent is INSIDE a ServiceRegistryBehaviour (attach to current GO, auto-name)
        // 3 = unrelated GO (inform user, attach to current GO)
        // 0 = Project mode (no context)
        private int _contextCase = 0;

        private const string TryAddKey = "MvcGenerators_MvcService_TryAddToContainer";

        // ── Open ──────────────────────────────────────────────────────────────

        internal static void ShowWindow()
        {
            var w = GetWindow<MvcServiceBehaviourGeneratorWindow>(true, "ServiceBehaviour Generator", true);
            w._parent = null;
            w._contextCase = 0;
            w.InitializeContext(GeneratorContextMode.Project);
            w.EnsureWindowSizing();
            OpenAt(w);
            w.Show();
        }

        internal static void ShowWindow(GameObject parent)
        {
            var w = GetWindow<MvcServiceBehaviourGeneratorWindow>(true, "ServiceBehaviour Generator", true);
            w._parent = parent;
            w.InitializeContext(GeneratorContextMode.Hierarchy);
            w.DetectContext();
            w.EnsureWindowSizing();
            OpenAt(w);
            w.Show();
        }

        private static void OpenAt(MvcServiceBehaviourGeneratorWindow w)
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
            _addIMvcLifecycle  = EditorPrefs.GetBool(MakeSharedKey($"{PrefPrefix}_AddIMvcLifecycle"), false);
            _tryAddToContainer = EditorPrefs.GetBool(MakeSharedKey(TryAddKey), true);
            className          = BuildClassName(_namePrefix);
        }

        // ── Context detection ─────────────────────────────────────────────────

        private void DetectContext()
        {
            if (_parent == null) { _contextCase = 3; _createAsChild = false; return; }

            if (HasServiceRegistry(_parent))
            {
                _contextCase = 1;
                _createAsChild = true;
                // No auto-name — user types the name, child GO will be named after it.
            }
            else if (ParentHasServiceRegistry(_parent.transform))
            {
                _contextCase = 2;
                _createAsChild = false;
                // Auto-fill from current GO name.
                _namePrefix = StripSuffix(_parent.name, "ServiceBehaviour", "Service");
                className = BuildClassName(_namePrefix);
            }
            else
            {
                _contextCase = 3;
                _createAsChild = false;
            }
        }

        // Checks both module-scoped and global service registries.
        private static bool HasServiceRegistry(GameObject go) =>
            go.GetComponent<ServiceRegistryBehaviour>()       != null ||
            go.GetComponent<GlobalServiceRegistryBehaviour>() != null;

        private static bool ParentHasServiceRegistry(Transform t)
        {
            var p = t.parent;
            while (p != null) { if (HasServiceRegistry(p.gameObject)) return true; p = p.parent; }
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
            GUILayout.Label("ServiceBehaviour Options", SettingsSectionStyle);
            using (new EditorGUI.DisabledScope(_parent == null))
            {
                DrawPanelToggle(ref _tryAddToContainer, "Try Add to Container",
                    _parent == null
                        ? "Not applicable when opened from the Project folder."
                        : "Automatically registers this service in the nearest ServiceRegistryBehaviour.");
            }
        }

        // ── OnGUI ─────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (DrawIfGenerating()) return;
            InitBaseStyles();

            GUILayout.BeginArea(new Rect(0f, 0f, GetMainWidth(), position.height));

            // Context advisory — shown at the very top when the GO is unrelated.
            if (_contextCase == 3 && _parent != null)
            {
                EditorGUILayout.HelpBox(
                    "For best results, create this ServiceBehaviour on a service registry " +
                    "(ServiceRegistryBehaviour or GlobalServiceRegistryBehaviour) or as a direct child of one. " +
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
                EditorGUILayout.LabelField("ServiceBehaviour", GUILayout.Width(115));
            }
            if (string.IsNullOrWhiteSpace(_namePrefix))
                EditorGUILayout.HelpBox("Enter a ServiceBehaviour name.", MessageType.Error);

            // Interface toggles — right after Name row.
            _addInterface     = EditorGUILayout.ToggleLeft("Add Interface", _addInterface);
            _addIMvcLifecycle = EditorGUILayout.ToggleLeft("Add IMvcLifecycle", _addIMvcLifecycle);

            DrawFolderFieldBase();
        }

        private void DrawCreateBtn()
        {
            bool nsInvalid = useNamespace && useCustomNamespace && !MvcGeneratorSharedSettings.IsValidNamespace(customNamespace);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_namePrefix) || nsInvalid))
            {
                if (GUILayout.Button("Create ServiceBehaviour", GUILayout.Height(36)))
                {
                    EditorPrefs.SetBool(MakeSharedKey($"{PrefPrefix}_AddInterface"),    _addInterface);
                    EditorPrefs.SetBool(MakeSharedKey($"{PrefPrefix}_AddIMvcLifecycle"), _addIMvcLifecycle);
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
                                ? MvcHierarchyScaffoldUtility.PostAddAction.AddServiceToServicesContainer
                                : MvcHierarchyScaffoldUtility.PostAddAction.None);
                    BeginGeneration(className, attachTarget?.name);
                }
            }
        }

        // Determines the GameObject the component will be attached to.
        // _createAsChild true: creates a new child GO under _parent.
        // _createAsChild false: attaches directly to _parent.
        private GameObject ResolveAttachTarget()
        {
            if (_parent == null) return null;

            if (_createAsChild)
            {
                var child = new GameObject(className);
                Undo.RegisterCreatedObjectUndo(child, "Create mvcExpress ServiceBehaviour");
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

            var interfaces = BuildInterfaceList(cn);
            var doc        = withDocumentation ? $"    /// <summary>MonoBehaviour registered as a service.</summary>\n" : string.Empty;
            var decl       = $"public class {cn} : MonoBehaviour{interfaces}";

            var body = BuildMethodStubs("    ");

            var ifaceBlock = BuildInterfaceBlock(cn, "    ");

            if (string.IsNullOrEmpty(ns))
                return doc + $"{decl}\n{{\n{body}}}" + ifaceBlock;

            return $"namespace {ns}\n{{\n{doc}    {decl}\n    {{\n{body}    }}" + ifaceBlock + "\n}";
        }

        private string BuildMethodStubs(string indent)
        {
            var sb = new System.Text.StringBuilder();
            if (_addIMvcLifecycle)
            {
                if (withDocumentation) sb.AppendLine($"{indent}    /// <summary>Called after all dependencies are injected.</summary>");
                sb.AppendLine($"{indent}    public void OnInitialized() {{ }}");
                sb.AppendLine();
                if (withDocumentation) sb.AppendLine($"{indent}    /// <summary>Called before the module's DI container is torn down.</summary>");
                sb.AppendLine($"{indent}    public void OnCleanup() {{ }}");
            }
            return sb.ToString();
        }

        private string BuildInterfaceList(string cn)
        {
            var parts = new System.Collections.Generic.List<string>();
            if (_addIMvcLifecycle) parts.Add("IMvcLifecycle");
            if (_addInterface) parts.Add("I" + cn);
            return parts.Count == 0 ? string.Empty : ", " + string.Join(", ", parts);
        }

        private string BuildInterfaceBlock(string cn, string indent)
        {
            if (!_addInterface) return string.Empty;
            var doc = withDocumentation ? $"\n{indent}/// <summary>Interface for {cn}.</summary>\n" : "\n";
            return doc + $"{indent}internal interface I{cn}\n{indent}{{\n{indent}}}";
        }

        // ── Template ──────────────────────────────────────────────────────────

        private string GetTemplate(string _)
        {
            var cn         = BuildClassName(_namePrefix);
            var iface      = "I" + cn;
            var interfaces = BuildInterfaceList(cn);
            var doc        = withDocumentation ? "    /// <summary>MonoBehaviour registered as a service.</summary>\n" : string.Empty;
            var decl       = $"    public class {cn} : MonoBehaviour{interfaces}";
            var using_mvc  = _addIMvcLifecycle ? "using mvcExpress;\n" : string.Empty;

            var methods = BuildMethodStubsTemplate("        ");

            var ifaceBlock = _addInterface
                ? "\n" + (withDocumentation ? $"    /// <summary>Interface for {cn}.</summary>\n" : string.Empty)
                  + $"    internal interface {iface}\n    {{\n    }}\n"
                : string.Empty;

            return "using UnityEngine;\n" + using_mvc + "\n" +
                   "${NAMESPACE_BEGIN}\n" +
                   doc + decl + "\n    {\n" +
                   methods +
                   "    }\n" +
                   ifaceBlock +
                   "${NAMESPACE_END}\n";
        }

        private string BuildMethodStubsTemplate(string indent)
        {
            if (!_addIMvcLifecycle) return string.Empty;
            var sb = new System.Text.StringBuilder();
            if (withDocumentation) sb.AppendLine($"{indent}/// <summary>Called after all dependencies are injected.</summary>");
            sb.AppendLine($"{indent}public void OnInitialized()");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}}}");
            sb.AppendLine();
            if (withDocumentation) sb.AppendLine($"{indent}/// <summary>Called before the module's DI container is torn down.</summary>");
            sb.AppendLine($"{indent}public void OnCleanup()");
            sb.AppendLine($"{indent}{{");
            sb.AppendLine($"{indent}}}");
            return sb.ToString();
        }

        // ── Scaffold callback ─────────────────────────────────────────────────

        internal static void TryAddServiceToContainerForScaffold(Transform from, MonoBehaviour service)
        {
            if (service == null || service is ProxyBehaviour) return;
            var container = FindParentRegistry<ServiceRegistryBehaviour>(from);
            if (container == null) return;

            var so  = new SerializedObject(container);
            var arr = so.FindProperty("_serviceMappings");
            if (arr == null || !arr.isArray) return;
            for (int i = 0; i < arr.arraySize; i++)
            {
                var p = arr.GetArrayElementAtIndex(i).FindPropertyRelative("Service");
                if (p != null && p.objectReferenceValue == service) return;
            }
            int idx = arr.arraySize;
            arr.InsertArrayElementAtIndex(idx);
            var el = arr.GetArrayElementAtIndex(idx);
            el.FindPropertyRelative("Service").objectReferenceValue        = service;
            el.FindPropertyRelative("RegisterToLogic").boolValue           = true;
            el.FindPropertyRelative("RegisterToView").boolValue            = false;
            el.FindPropertyRelative("IsTransient").boolValue               = false;
            el.FindPropertyRelative("LogicTypeName").stringValue           = string.Empty;
            el.FindPropertyRelative("ViewTypeName").stringValue            = string.Empty;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(container);
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
            if (string.IsNullOrEmpty(p)) return "ServiceBehaviour";
            if (p.EndsWith("ServiceBehaviour", StringComparison.Ordinal)) return p;
            if (p.EndsWith("Service", StringComparison.Ordinal)) p = p.Substring(0, p.Length - 7);
            return p + "ServiceBehaviour";
        }
    }
}

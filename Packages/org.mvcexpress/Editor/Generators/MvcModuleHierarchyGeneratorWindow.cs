using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.Generators
{
    internal sealed class MvcModuleHierarchyGeneratorWindow : MvcScriptGeneratorWindowBase
    {
        protected override string PrefPrefix       => "MvcModuleHierarchyGen";
        protected override string WindowTitle      => "Module Generator";
        protected override Vector2 WindowSize      => new Vector2(SettingsPanelWidth + BarWidth, 400f);
        protected override string DefaultFileName  => "NewModule.cs";
        protected override string DefaultClassName => string.Empty;
        protected override string FolderActorKey   => "MvcModule";

        // Module-specific state
        private string     _namePrefix;
        private bool       _full = true;
        private GameObject _parent;

        private enum HierarchyAttachMode { AttachToSelected = 0, CreateChildUnderSelected = 1 }
        private HierarchyAttachMode _hierarchyAttachMode;

        // ── Open ──────────────────────────────────────────────────────────────

        internal static void ShowWindow(GameObject parent)
        {
            var w = GetWindow<MvcModuleHierarchyGeneratorWindow>(true, "Module Generator", true);
            w._parent = parent;
            w.EnsureWindowSizing();
            if (w.position.width <= SettingsPanelWidth + BarWidth + 20f)
            {
                var r = w.position;
                r.width  = SettingsPanelWidth + BarWidth + 360f;
                r.height = Mathf.Max(r.height, 480f);
                w.position = r;
            }
            w.Show();
        }

        // ── Lifecycle ─────────────────────────────────────────────────────────

        protected override void OnEnable()
        {
            base.OnEnable();
            if (IsRestoredFromGeneration) return;
            _namePrefix      = string.Empty;
            _full            = true;
            _hierarchyAttachMode = (HierarchyAttachMode)EditorPrefs.GetInt(
                MakeSharedKey($"{PrefPrefix}_HierarchyAttachMode"), 0);
            className = BuildClassName(_namePrefix);
        }

        // ── Settings panel items (module-specific) ────────────────────────────

        protected override void DrawWindowSettingsItems()
        {
            GUILayout.Label("Module Options", SettingsSectionStyle);
            DrawPanelToggle(ref _full, "Create Child Containers",
                "Creates Services, Model, Controller, and View child GameObjects under the Module, " +
                "each pre-wired with their registry component. Disable for a bare Module script.");
        }

        // ── OnGUI ─────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            if (DrawIfGenerating()) return;
            InitBaseStyles();

            float mainWidth    = GetMainWidth();
            float settingsLeft = GetSettingsLeft();

            GUILayout.BeginArea(new Rect(0f, 0f, mainWidth, position.height));

            EditorGUILayout.Space(8f);
            DrawNameRow();
            EditorGUILayout.Space(5f);
            DrawNamespaceFieldBase();
            EditorGUILayout.Space(5f);
            DrawFolderFieldBase();

            if (_parent != null)
            {
                EditorGUILayout.Space(8f);
                DrawHierarchyOptions();
            }

            EditorGUILayout.Space(8f);
            DrawPreviewBoxBase(BuildPreviewText());
            EditorGUILayout.Space(6f);
            DrawCreateButton();
            EditorGUILayout.Space(8f);

            GUILayout.EndArea();

            DrawSettingsPanelBase(settingsLeft);
            DrawActivationBarBase();
        }

        // ── Main content sections ─────────────────────────────────────────────

        private void DrawNameRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Name", GUILayout.Width(60));
                var prev = _namePrefix;
                _namePrefix = EditorGUILayout.TextField(_namePrefix ?? string.Empty);
                if (_namePrefix != prev) className = BuildClassName(_namePrefix);
                EditorGUILayout.LabelField("Module", GUILayout.Width(55));
            }
            if (string.IsNullOrWhiteSpace(_namePrefix))
                EditorGUILayout.HelpBox("Enter a module name.", MessageType.Error);
        }

        private void DrawHierarchyOptions()
        {
            EditorGUILayout.LabelField("Hierarchy", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                bool attachSel = GUILayout.Toggle(
                    _hierarchyAttachMode == HierarchyAttachMode.AttachToSelected,
                    $"Attach to {_parent.name}", EditorStyles.radioButton);
                if (attachSel) _hierarchyAttachMode = HierarchyAttachMode.AttachToSelected;

                using (new EditorGUI.DisabledScope(_hierarchyAttachMode != HierarchyAttachMode.AttachToSelected))
                {
                    if (GUILayout.Button($"Use name '{_parent.name}'", GUILayout.Width(160)))
                    {
                        _namePrefix = _parent.name;
                        className   = BuildClassName(_namePrefix);
                    }
                }
            }

            bool createSel = GUILayout.Toggle(
                _hierarchyAttachMode == HierarchyAttachMode.CreateChildUnderSelected,
                $"Create child under {_parent.name}", EditorStyles.radioButton);
            if (createSel) _hierarchyAttachMode = HierarchyAttachMode.CreateChildUnderSelected;

            EditorPrefs.SetInt(MakeSharedKey($"{PrefPrefix}_HierarchyAttachMode"), (int)_hierarchyAttachMode);
        }

        private string BuildPreviewText()
        {
            var cn  = BuildClassName(_namePrefix);
            var ns  = ResolveCurrentNamespace();
            var doc = withDocumentation ? "    /// <summary>Module = composition root...</summary>\n" : string.Empty;
            var i   = string.IsNullOrEmpty(ns) ? string.Empty : "    ";

            string M(string name, string hint) =>
                (withDocumentation ? $"{i}    /// <summary>{hint}</summary>\n" : string.Empty) +
                $"{i}    protected override void {name}() {{ }}";

            var methods =
                M("RegisterServices", "Register Services here.") + "\n" +
                M("RegisterProxies",  "Register Proxies here.")  + "\n" +
                M("BindCommands",     "Bind Commands here.")     + "\n" +
                M("AttachMediators",  "Attach Mediators here.")  + "\n" +
                M("OnInitialized",    "Module is fully ready.");

            if (string.IsNullOrEmpty(ns))
                return doc + $"public sealed class {cn} : MvcModule\n" + "{\n" + methods + "\n}";

            return $"namespace {ns}\n" + "{\n" + doc +
                   $"    public sealed class {cn} : MvcModule\n" + "    {\n" + methods + "\n    }\n}";
        }

        private void DrawCreateButton()
        {
            bool nsInvalid = useNamespace && useCustomNamespace && !MvcGeneratorSharedSettings.IsValidNamespace(customNamespace);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_namePrefix) || nsInvalid))
            {
                if (GUILayout.Button("Create Module", GUILayout.Height(36)))
                {
                    PushFolderToHistory(selectedFolderPath);
                    SavePreferences();

                    className = BuildClassName(_namePrefix);
                    var scriptPath = CreateScriptInSelectedFolderReturningPath(GetTemplate);
                    if (!string.IsNullOrEmpty(scriptPath))
                        CreateModuleGameObject(scriptPath);

                    BeginGeneration(className, _parent?.name);
                }
            }
        }

        // ── Namespace resolution ──────────────────────────────────────────────

        private string ResolveCurrentNamespace()
        {
            var folder    = string.IsNullOrWhiteSpace(selectedFolderPath) ? "Assets" : selectedFolderPath;
            var assetPath = Path.Combine(folder, BuildClassName(_namePrefix) + ".cs").Replace("\\", "/");
            return MvcNamespaceUtility.Resolve(
                assetPath, useNamespace, useCustomNamespace, customNamespace, defaultNamespace, skipFolderLevels);
        }

        private string GetFullTypeName(string assetPath, string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName) || typeName.IndexOf('.') >= 0) return typeName;
            var ns = ResolveNamespaceForPath(assetPath);
            return string.IsNullOrEmpty(ns) ? typeName : ns + "." + typeName;
        }

        // ── Template ─────────────────────────────────────────────────────────

        private string GetTemplate(string cn)
        {
            var ns   = ResolveCurrentNamespace();
            var classDoc = withDocumentation
                ? "    /// <summary>\n    /// Module = composition root for one feature.\n    /// </summary>\n"
                : string.Empty;

            string Method(string indent, string name, string hint) =>
                (withDocumentation ? $"{indent}/// <summary>{hint}</summary>\n" : string.Empty) +
                $"{indent}protected override void {name}()\n{indent}{{\n{indent}}}\n";

            if (string.IsNullOrEmpty(ns))
            {
                var i = "    ";
                return "using mvcExpress;\nusing UnityEngine;\n\n" +
                       (withDocumentation ? "/// <summary>\n/// Module = composition root.\n/// </summary>\n" : string.Empty) +
                       $"public sealed class {cn} : MvcModule\n{{\n" +
                       Method(i, "RegisterServices", "Register Services here.") + "\n" +
                       Method(i, "RegisterProxies",  "Register Proxies here.")  + "\n" +
                       Method(i, "BindCommands",     "Bind Commands here.")     + "\n" +
                       Method(i, "AttachMediators",  "Attach Mediators here.")  + "\n" +
                       Method(i, "OnInitialized",    "Module is fully initialized.") +
                       "}\n";
            }

            {
                var i = "        ";
                return "using mvcExpress;\nusing UnityEngine;\n\n" +
                       $"namespace {ns}\n{{\n" +
                       classDoc +
                       $"    public sealed class {cn} : MvcModule\n    {{\n" +
                       Method(i, "RegisterServices", "Register Services here.") + "\n" +
                       Method(i, "RegisterProxies",  "Register Proxies here.")  + "\n" +
                       Method(i, "BindCommands",     "Bind Commands here.")     + "\n" +
                       Method(i, "AttachMediators",  "Attach Mediators here.")  + "\n" +
                       Method(i, "OnInitialized",    "Module is fully initialized.") +
                       "    }\n}\n";
            }
        }

        // ── GameObject / hierarchy creation (unchanged logic) ─────────────────

        private void CreateModuleGameObject(string scriptAssetPath)
        {
            var moduleClassName = className;
            var attachTarget    = _parent;

            if (_parent != null && _hierarchyAttachMode == HierarchyAttachMode.CreateChildUnderSelected)
            {
                var child = new GameObject(moduleClassName);
                GameObjectUtility.SetParentAndAlign(child, _parent);
                Undo.RegisterCreatedObjectUndo(child, "Create mvcExpress Module");
                Selection.activeGameObject = child;
                attachTarget = child;
            }
            else if (_parent != null)
            {
                attachTarget = _parent;
                Undo.RegisterCompleteObjectUndo(attachTarget, "Create mvcExpress Module");
                Selection.activeGameObject = attachTarget;
            }
            else
            {
                var go = new GameObject(moduleClassName);
                Undo.RegisterCreatedObjectUndo(go, "Create mvcExpress Module");
                Selection.activeGameObject = go;
                attachTarget = go;
            }

            if (_full)
            {
                CreateOrGetDirectChild(attachTarget.transform, "Services",   typeof(ServiceRegistryBehaviour),  0);
                CreateOrGetDirectChild(attachTarget.transform, "Model",      typeof(ProxyRegistryBehaviour),    1);
                CreateOrGetDirectChild(attachTarget.transform, "Controller", typeof(CommandBindingsBehaviour),  2);
                CreateOrGetDirectChild(attachTarget.transform, "View",       typeof(MediatorRegistryBehaviour), 3);
            }

            var fullTypeName = GetFullTypeName(scriptAssetPath, moduleClassName);
            MvcHierarchyScaffoldUtility.AddComponentAfterCompile(
                attachTarget, fullTypeName, MvcHierarchyScaffoldUtility.PostAddAction.FillModuleContainers);
        }

        private static GameObject CreateOrGetDirectChild(Transform parent, string name, Type componentType, int sibling)
        {
            var existingT = parent.Find(name);
            if (existingT != null)
            {
                if (existingT.GetComponent(componentType) == null) existingT.gameObject.AddComponent(componentType);
                existingT.SetSiblingIndex(Mathf.Clamp(sibling, 0, parent.childCount - 1));
                return existingT.gameObject;
            }
            var child = new GameObject(name);
            Undo.RegisterCreatedObjectUndo(child, "Create mvcExpress Container");
            child.transform.SetParent(parent, false);
            child.transform.SetSiblingIndex(Mathf.Clamp(sibling, 0, parent.childCount - 1));
            child.AddComponent(componentType);
            return child;
        }

        internal static void FillModuleContainerReferencesForScaffold(MvcModule module)
        {
            if (module == null) return;
            var model      = FindChild<ProxyRegistryBehaviour>(module.transform,    "Model");
            var services   = FindChild<ServiceRegistryBehaviour>(module.transform,  "Services");
            var controller = FindChild<CommandBindingsBehaviour>(module.transform,  "Controller");
            var view       = FindChild<MediatorRegistryBehaviour>(module.transform, "View");

            if (model      == null) model      = module.GetComponentInChildren<ProxyRegistryBehaviour>(true);
            if (services   == null) services   = module.GetComponentInChildren<ServiceRegistryBehaviour>(true);
            if (controller == null) controller = module.GetComponentInChildren<CommandBindingsBehaviour>(true);
            if (view       == null) view       = module.GetComponentInChildren<MediatorRegistryBehaviour>(true);

            var so = new SerializedObject(module);
            so.FindProperty("_modelContainer").objectReferenceValue      = model;
            so.FindProperty("_servicesContainer").objectReferenceValue   = services;
            so.FindProperty("_controllerContainer").objectReferenceValue = controller;
            so.FindProperty("_viewContainer").objectReferenceValue       = view;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(module);
        }

        private static T FindChild<T>(Transform t, string childName) where T : Component
        {
            if (t == null) return null;
            var c = t.Find(childName);
            return c == null ? null : c.GetComponent<T>();
        }

        private static string BuildClassName(string prefix)
        {
            var p = (prefix ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(p)) return "Module";
            if (p.EndsWith("Module", StringComparison.Ordinal)) return p;
            return p + "Module";
        }
    }
}

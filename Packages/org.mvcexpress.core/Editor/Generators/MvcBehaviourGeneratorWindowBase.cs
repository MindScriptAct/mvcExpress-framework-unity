using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.Generators
{
    internal abstract class MvcBehaviourGeneratorWindowBase : MvcScriptGeneratorWindowBase
    {
        protected abstract string BehaviourSuffix { get; }
        protected abstract string BaseTypeName { get; }
        protected abstract string UsingLine { get; }
        protected abstract MvcHierarchyScaffoldUtility.PostAddAction PostAddAction { get; }
        protected abstract Type RecommendedParentContainerType { get; }

        protected string namePrefix;
        protected bool addInterface;
        protected bool tryAddToContainer;
        protected GameObject parent;

        protected virtual bool SupportsInterface => true;

        private enum HierarchyAttachMode
        {
            AttachToSelected = 0,
            CreateChildUnderSelected = 1,
        }

        private HierarchyAttachMode _hierarchyAttachMode = HierarchyAttachMode.AttachToSelected;

        protected override void OnEnable()
        {
            // Default to Project context when opened via Assets menu.
            contextMode = GeneratorContextMode.Project;
            base.OnEnable();
            namePrefix = string.Empty;
            addInterface = EditorPrefs.GetBool(MakeProjectSpecificKey($"{PrefPrefix}_AddInterface"), false);
            tryAddToContainer = EditorPrefs.GetBool(MakeProjectSpecificKey($"{PrefPrefix}_TryAddToContainer"), true);
            className = BuildClassName(namePrefix, BehaviourSuffix);
            _hierarchyAttachMode = (HierarchyAttachMode)EditorPrefs.GetInt(MakeProjectSpecificKey($"{PrefPrefix}_HierarchyAttachMode"), (int)HierarchyAttachMode.AttachToSelected);
        }

        protected virtual void OnDisable()
        {
            EditorPrefs.SetBool(MakeProjectSpecificKey($"{PrefPrefix}_AddInterface"), addInterface);
            EditorPrefs.SetBool(MakeProjectSpecificKey($"{PrefPrefix}_TryAddToContainer"), tryAddToContainer);
            EditorPrefs.SetInt(MakeProjectSpecificKey($"{PrefPrefix}_HierarchyAttachMode"), (int)_hierarchyAttachMode);
        }

        protected void DrawDefaultNameAndFolderGUI(float suffixLabelWidth = 130f)
        {
            EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Name", GUILayout.Width(80));
            namePrefix = EditorGUILayout.TextField(namePrefix ?? string.Empty);
            EditorGUILayout.LabelField(BehaviourSuffix, GUILayout.Width(suffixLabelWidth));
            EditorGUILayout.EndHorizontal();

            var cn = BuildClassName(namePrefix, BehaviourSuffix);
            var iface = "I" + cn;

            if (SupportsInterface && addInterface)
                EditorGUILayout.LabelField($"Preview: {cn} : {BaseTypeName}, {iface}", EditorStyles.miniLabel);
            else
                EditorGUILayout.LabelField($"Preview: {cn} : {BaseTypeName}", EditorStyles.miniLabel);

            if (string.IsNullOrWhiteSpace(namePrefix))
            {
                EditorGUILayout.HelpBox($"Enter a {BehaviourSuffix} name (prefix).", MessageType.Error);
            }

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Folder", GUILayout.Width(80));

            EditorGUI.BeginChangeCheck();
            selectedFolderPath = EditorGUILayout.TextField(selectedFolderPath);
            if (EditorGUI.EndChangeCheck())
            {
                selectedFolderPath = (selectedFolderPath ?? string.Empty).Replace("\\", "/").Trim();
                if (!string.IsNullOrWhiteSpace(selectedFolderPath))
                    EditorPrefs.SetString(MakeProjectSpecificKey($"{PrefPrefix}_LastFolder"), selectedFolderPath);
            }

            if (GUILayout.Button("Pick", GUILayout.Width(60)))
            {
                if (PickFolderInProject(ref selectedFolderPath))
                {
                    EditorPrefs.SetString(MakeProjectSpecificKey($"{PrefPrefix}_LastFolder"), selectedFolderPath);
                }
            }
            EditorGUILayout.EndHorizontal();

            // Warn if folder doesn't exist yet (it will be created on Create).
            if (!string.IsNullOrWhiteSpace(selectedFolderPath) && selectedFolderPath.StartsWith("Assets", StringComparison.Ordinal))
            {
                var absFolder = Path.GetFullPath(selectedFolderPath);
                if (!Directory.Exists(absFolder))
                    EditorGUILayout.HelpBox("Directory does not exist and will be created on Create.", MessageType.Warning);
            }

            EditorGUILayout.Space(5);
        }

        protected void DrawDefaultOptionsGUI()
        {
            EditorGUILayout.Space(6);
            if (SupportsInterface)
                addInterface = EditorGUILayout.ToggleLeft("Add interface", addInterface);

            // Hierarchy options go right after Add interface.
            if (contextMode == GeneratorContextMode.Hierarchy)
            {
                if (parent != null)
                {
                    EditorGUILayout.Space(6);
                    EditorGUILayout.LabelField("Hierarchy", EditorStyles.boldLabel);

                    var attachLabel = $"Attach to {parent.name}";
                    var createLabel = $"Create Child under {parent.name}";

                    // Row 1: radio + "Use name <go>" button
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        var attachSelected = GUILayout.Toggle(_hierarchyAttachMode == HierarchyAttachMode.AttachToSelected, attachLabel, EditorStyles.radioButton);
                        if (attachSelected)
                            _hierarchyAttachMode = HierarchyAttachMode.AttachToSelected;

                        using (new EditorGUI.DisabledScope(_hierarchyAttachMode != HierarchyAttachMode.AttachToSelected))
                        {
                            var btnText = $"Use name {parent.name}";
                            if (GUILayout.Button(btnText, GUILayout.Width(150)))
                            {
                                namePrefix = parent.name;
                            }
                        }
                    }

                    // Row 2: radio
                    var createSelected = GUILayout.Toggle(_hierarchyAttachMode == HierarchyAttachMode.CreateChildUnderSelected, createLabel, EditorStyles.radioButton);
                    if (createSelected)
                        _hierarchyAttachMode = HierarchyAttachMode.CreateChildUnderSelected;

                    // Ensure one is always selected.
                    if (_hierarchyAttachMode != HierarchyAttachMode.AttachToSelected &&
                        _hierarchyAttachMode != HierarchyAttachMode.CreateChildUnderSelected)
                    {
                        _hierarchyAttachMode = HierarchyAttachMode.AttachToSelected;
                    }

                    EditorPrefs.SetInt(MakeProjectSpecificKey($"{PrefPrefix}_HierarchyAttachMode"), (int)_hierarchyAttachMode);
                }

                EditorGUILayout.Space(6);
                DrawNamespaceOptions();

                // Options label is last
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);

                tryAddToContainer = EditorGUILayout.ToggleLeft("Try to add to Container", tryAddToContainer);
                withDocumentation = EditorGUILayout.ToggleLeft("Include documentation", withDocumentation);

                if (parent != null && tryAddToContainer)
                {
                    if (!HasParentContainer(parent, RecommendedParentContainerType))
                    {
                        EditorGUILayout.HelpBox("Creating it under container is recomended.", MessageType.Warning);
                    }
                }
            }
            else
            {
                // Project context
                EditorGUILayout.Space(6);
                DrawNamespaceOptions();

                // Options label is last
                EditorGUILayout.Space(8);
                EditorGUILayout.LabelField("Options", EditorStyles.boldLabel);
                withDocumentation = EditorGUILayout.ToggleLeft("Include documentation", withDocumentation);

                // Project context: not applicable.
                tryAddToContainer = false;
            }
        }

        protected virtual void DrawCreateButtonGUI(string buttonText = "Create")
        {
            EditorGUILayout.Space(12);

            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(namePrefix)))
            {
                if (GUILayout.Button(buttonText, GUILayout.Height(32)))
                {
                    if (!string.IsNullOrWhiteSpace(selectedFolderPath))
                    {
                        EditorPrefs.SetString(MakeProjectSpecificKey($"{PrefPrefix}_LastFolder"), selectedFolderPath);
                    }

                    SavePreferences();
                    className = BuildClassName(namePrefix, BehaviourSuffix);

                    var scriptPath = CreateScriptInSelectedFolderReturningPath(GetTemplate);
                    if (!string.IsNullOrEmpty(scriptPath) && parent != null)
                    {
                        var attachTarget = parent;

                        // Optionally create a child GO; otherwise attach to the selected GO.
                        if (_hierarchyAttachMode == HierarchyAttachMode.CreateChildUnderSelected)
                        {
                            var goName = GetDefaultGameObjectName(className);
                            var goNameKey = MakeProjectSpecificKey($"{PrefPrefix}_GameObjectName");
                            if (EditorPrefs.HasKey(goNameKey))
                            {
                                var prefName = EditorPrefs.GetString(goNameKey, string.Empty);
                                if (!string.IsNullOrEmpty(prefName))
                                    goName = prefName;
                                EditorPrefs.DeleteKey(goNameKey);
                            }

                            var child = new GameObject(goName);
                            GameObjectUtility.SetParentAndAlign(child, parent);
                            Undo.RegisterCreatedObjectUndo(child, $"Create mvcExpress {BehaviourSuffix}");
                            Selection.activeGameObject = child;
                            attachTarget = child;
                        }
                        else
                        {
                            // Ensure selection stays on target object.
                            Selection.activeGameObject = attachTarget;

                            // If a GO name override was prepared by a derived generator (e.g. Mediator),
                            // it is irrelevant when attaching to an existing object.
                            var goNameKey = MakeProjectSpecificKey($"{PrefPrefix}_GameObjectName");
                            if (EditorPrefs.HasKey(goNameKey))
                                EditorPrefs.DeleteKey(goNameKey);
                        }

                        var fullTypeName = GetFullTypeName(scriptPath, className);
                        var action = tryAddToContainer ? PostAddAction : MvcHierarchyScaffoldUtility.PostAddAction.None;
                        MvcHierarchyScaffoldUtility.AddComponentAfterCompile(attachTarget, fullTypeName, action);
                    }

                    Close();
                }
            }
        }

        private string GetDefaultGameObjectName(string generatedClassName)
        {
            if (string.IsNullOrEmpty(generatedClassName))
                return string.Empty;

            var name = generatedClassName;

            // Drop only the generic suffix for hierarchy naming.
            // Examples:
            // - SingleModuleUserProxyBehaviour -> SingleModuleUserProxy
            // - InventoryServiceBehaviour -> InventoryService
            if (name.EndsWith("Behaviour", StringComparison.Ordinal))
                name = name.Substring(0, name.Length - "Behaviour".Length);

            return string.IsNullOrEmpty(name) ? generatedClassName : name;
        }

        protected string GetFullTypeName(string assetPath, string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return typeName;

            // If already qualified, don't prefix again.
            if (typeName.IndexOf('.') >= 0)
                return typeName;

            var ns = ResolveNamespaceForPath(assetPath);
            return string.IsNullOrEmpty(ns) ? typeName : ns + "." + typeName;
        }

        protected abstract string GetTemplate(string _);

        protected static string BuildClassName(string prefix, string suffix)
        {
            var p = (prefix ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(p))
                return suffix;

            if (p.EndsWith(suffix, StringComparison.Ordinal))
                return p;

            // Avoid ProxyProxyBehaviour etc.
            if (suffix == "ProxyBehaviour" && p.EndsWith("Proxy", StringComparison.Ordinal))
                p = p.Substring(0, p.Length - "Proxy".Length);

            if (suffix == "MediatorBehaviour" && p.EndsWith("Mediator", StringComparison.Ordinal))
                p = p.Substring(0, p.Length - "Mediator".Length);

            if (suffix == "ServiceBehaviour" && p.EndsWith("Service", StringComparison.Ordinal))
                p = p.Substring(0, p.Length - "Service".Length);

            return p + suffix;
        }

        private static bool HasParentContainer(GameObject from, Type containerType)
        {
            if (from == null || containerType == null) return false;
            var t = from.transform;
            while (t != null)
            {
                if (t.GetComponent(containerType) != null)
                    return true;
                t = t.parent;
            }
            return false;
        }
    }
}

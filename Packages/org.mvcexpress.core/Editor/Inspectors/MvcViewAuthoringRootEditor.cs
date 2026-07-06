using System;
using System.Collections.Generic;
using System.Linq;
using mvcExpress;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace mvcExpress.Editor.Inspectors
{
    /// <summary>
    /// Custom tabbed inspector for MvcViewAuthoringRoot that lets a designer preview nested view prefabs in-scene without editing the parent prefab directly.
    /// </summary>
    [CustomEditor(typeof(MvcViewAuthoringRoot))]
    internal sealed class MvcViewAuthoringRootEditor : UnityEditor.Editor
    {
        private const string PreviewSuffix = " [PreviewOnly]";

        private MvcViewAuthoringRoot _root;
        private Vector2 _scroll;
        private Report _report;
        private int _tab;

        private void OnEnable()
        {
            _root = TryGetRootTarget();
        }

        public override void OnInspectorGUI()
        {
            _root = TryGetRootTarget();
            if (_root == null)
            {
                EditorGUILayout.HelpBox("View authoring root target is not available.", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField("View Authoring", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Use an authoring root to preview nested view prefabs without applying preview-only children into parent prefabs.",
                MessageType.Info);

            _tab = GUILayout.Toolbar(_tab, new[] { "Setup", "Changes" });
            EditorGUILayout.Space(6f);

            if (_tab == 0)
            {
                DrawSetupTab();
            }
            else
            {
                DrawChangesTab();
            }
        }

        private MvcViewAuthoringRoot TryGetRootTarget()
        {
            try
            {
                var editorTargets = targets;
                if (editorTargets == null || editorTargets.Length == 0)
                    return null;

                return editorTargets[0] as MvcViewAuthoringRoot;
            }
            catch (IndexOutOfRangeException)
            {
                return null;
            }
        }

        private void DrawSetupTab()
        {
            EditorGUILayout.HelpBox(
                "Select nested prefab instances under this root, then mark them as preview-only. Configure list previews here.",
                MessageType.None);

            if (GUILayout.Button("Mark Selected Preview Only"))
            {
                MarkSelectedPreviewOnly();
            }

            DrawPreviewEntries();
        }

        private void DrawChangesTab()
        {
            EditorGUILayout.HelpBox(
                "Refresh to inspect prefab changes. Safe apply skips preview-only children and applies prefab overrides to their own prefab assets.",
                MessageType.None);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Refresh Changes"))
                {
                    RefreshReport();
                }
                
                if (GUILayout.Button("Apply All Safe Changes"))
                {
                    ApplyAllSafeChanges();
                }
            }

            DrawReport();
        }

        private void DrawPreviewEntries()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Preview Only Objects", EditorStyles.boldLabel);

            if (_root.PreviewEntries.Count == 0)
            {
                EditorGUILayout.HelpBox("No preview-only objects are registered under this root.", MessageType.None);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll, GUILayout.MinHeight(120), GUILayout.MaxHeight(220));

            for (int i = _root.PreviewEntries.Count - 1; i >= 0; i--)
            {
                var entry = _root.PreviewEntries[i];
                if (entry == null)
                {
                    _root.PreviewEntries.RemoveAt(i);
                    continue;
                }

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUI.BeginChangeCheck();
                        var preview = EditorGUILayout.ObjectField("Object", entry.PreviewObject, typeof(GameObject), true) as GameObject;
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(_root, "Change Preview Object");
                            entry.PreviewObject = preview;
                            MarkRootDirty();
                        }

                        if (GUILayout.Button("Select", GUILayout.Width(58)) && entry.PreviewObject != null)
                        {
                            Selection.activeGameObject = entry.PreviewObject;
                        }

                        if (GUILayout.Button("Remove", GUILayout.Width(68)))
                        {
                            Undo.RecordObject(_root, "Remove Preview Only Object");
                            CleanupGeneratedCopies(entry);
                            _root.PreviewEntries.RemoveAt(i);
                            MarkRootDirty();
                            MvcViewAuthoringHierarchyOverlay.RebuildCache();
                            continue;
                        }
                    }

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUI.BeginChangeCheck();
                        var listItem = EditorGUILayout.ToggleLeft("List Item", entry.ListItem, GUILayout.Width(100));
                        var count = EditorGUILayout.IntSlider("Preview Count", entry.PreviewCount, 1, 20);
                        if (EditorGUI.EndChangeCheck())
                        {
                            Undo.RecordObject(_root, "Change Preview List Settings");
                            entry.ListItem = listItem;
                            entry.PreviewCount = count;
                            RenamePreviewEntry(entry);
                            MarkRootDirty();
                            MvcViewAuthoringHierarchyOverlay.RebuildCache();
                        }

                        using (new EditorGUI.DisabledScope(!entry.ListItem || entry.PreviewObject == null))
                        {
                            if (GUILayout.Button("Rebuild Copies", GUILayout.Width(112)))
                            {
                                RebuildListCopies(entry);
                            }
                        }
                    }
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawReport()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("Change Report", EditorStyles.boldLabel);

            if (_report == null)
            {
                EditorGUILayout.HelpBox("Click Refresh Changes to inspect this authoring root.", MessageType.None);
                return;
            }

            DrawReportGroup("Errors", _report.Errors, MessageType.Error);
            DrawReportGroup("Safe Changes", _report.SafeChanges, MessageType.Info);
            DrawReportGroup("Skipped Preview Changes", _report.SkippedChanges, MessageType.Info);
            DrawReportGroup("Manual Review", _report.ManualReview, MessageType.Warning);
            DrawReportGroup("Info", _report.Info, MessageType.None);

            if (_report.AccidentalApplies.Count > 0)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button("Fix Accidental Applies", GUILayout.Width(180)))
                    {
                        FixAccidentalApplies(_report.AccidentalApplies);
                    }
                }
            }
        }

        private static void DrawReportGroup(string title, List<string> messages, MessageType type)
        {
            if (messages.Count == 0)
                return;

            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            foreach (var message in messages)
            {
                EditorGUILayout.HelpBox(message, type);
            }
        }

        private void MarkSelectedPreviewOnly()
        {
            if (_root == null)
                return;

            var selectedObjects = Selection.gameObjects;
            if (selectedObjects == null || selectedObjects.Length == 0)
            {
                EditorUtility.DisplayDialog("mvcExpress View Authoring", "Select one or more prefab instances under the authoring root.", "OK");
                return;
            }

            Undo.RecordObject(_root, "Mark Preview Only");
            var added = 0;

            foreach (var selected in selectedObjects)
            {
                if (selected == null || selected == _root.gameObject)
                    continue;

                var previewObject = PrefabUtility.GetNearestPrefabInstanceRoot(selected);
                if (previewObject == null)
                {
                    Debug.LogWarning($"[mvcExpress] '{selected.name}' is not inside a prefab instance.");
                    continue;
                }

                if (!IsUnderRoot(previewObject))
                {
                    Debug.LogWarning($"[mvcExpress] '{previewObject.name}' is not under authoring root '{_root.name}'.");
                    continue;
                }

                if (FindEntry(previewObject) != null || IsGeneratedCopy(previewObject))
                    continue;

                var entry = new MvcViewAuthoringPreviewEntry { PreviewObject = previewObject };
                _root.PreviewEntries.Add(entry);
                RenamePreviewEntry(entry);
                added++;
            }

            if (added > 0)
            {
                MarkRootDirty();
                MvcViewAuthoringHierarchyOverlay.RebuildCache();
                RefreshReport();
            }
        }

        private void RefreshReport()
        {
            _report = BuildReport();
        }

        private Report BuildReport()
        {
            var report = new Report();
            if (_root == null)
                return report;

            CleanupNullEntries();

            var entries = _root.PreviewEntries;
            var previewObjects = new HashSet<GameObject>(entries.Where(e => e?.PreviewObject != null).Select(e => e.PreviewObject));
            var generatedCopies = new HashSet<GameObject>(entries.SelectMany(e => e?.GeneratedCopies ?? new List<GameObject>()).Where(go => go != null));

            foreach (var entry in entries)
            {
                ValidateEntry(entry, report);
            }

            foreach (var instanceRoot in GetPrefabInstanceRootsUnderAuthoringRoot())
            {
                if (instanceRoot == null || generatedCopies.Contains(instanceRoot))
                    continue;

                var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
                if (string.IsNullOrWhiteSpace(assetPath))
                    continue;

                var isPreviewRoot = previewObjects.Contains(instanceRoot);
                var objectOverrideCount = PrefabUtility.GetObjectOverrides(instanceRoot, false)
                    .Count(o => isPreviewRoot || !BelongsToPreviewSubtree(o.instanceObject, previewObjects, generatedCopies));
                var addedComponentCount = PrefabUtility.GetAddedComponents(instanceRoot)
                    .Count(c => isPreviewRoot || !BelongsToPreviewSubtree(c.instanceComponent, previewObjects, generatedCopies));
                var removedComponentCount = PrefabUtility.GetRemovedComponents(instanceRoot).Count;
                var addedGameObjects = PrefabUtility.GetAddedGameObjects(instanceRoot);
                var skippedPreviewChildren = CountPreviewAddedGameObjects(addedGameObjects, previewObjects, generatedCopies);
                var nonPreviewAddedChildren = Mathf.Max(0, addedGameObjects.Count - skippedPreviewChildren);

                if (objectOverrideCount > 0 || addedComponentCount > 0)
                {
                    report.SafeChanges.Add($"{instanceRoot.name}: {objectOverrideCount} object/property override(s), {addedComponentCount} added component(s).");
                }

                if (skippedPreviewChildren > 0)
                {
                    report.SkippedChanges.Add($"{instanceRoot.name}: {skippedPreviewChildren} preview-only added child object(s) will not be applied to the parent prefab.");
                }

                if (removedComponentCount > 0)
                {
                    report.ManualReview.Add($"{instanceRoot.name}: {removedComponentCount} removed component override(s). Apply these manually for now.");
                }

                if (nonPreviewAddedChildren > 0)
                {
                    report.ManualReview.Add($"{instanceRoot.name}: {nonPreviewAddedChildren} non-preview added child object(s). Apply these manually for now.");
                }
            }

            if (!report.HasMessages)
            {
                report.Info.Add("No prefab changes found under this authoring root.");
            }

            return report;
        }

        private void ValidateEntry(MvcViewAuthoringPreviewEntry entry, Report report)
        {
            if (entry == null)
                return;

            var preview = entry.PreviewObject;
            if (preview == null)
            {
                report.Errors.Add("A preview-only entry has no object assigned.");
                return;
            }

            if (!IsUnderRoot(preview))
            {
                report.Errors.Add($"{preview.name}: preview-only object is not under authoring root '{_root.name}'.");
            }

            if (!PrefabUtility.IsAnyPrefabInstanceRoot(preview))
            {
                report.Errors.Add($"{preview.name}: preview-only object must be a prefab instance root.");
            }

            if (entry.ListItem)
            {
                var expectedCopies = Mathf.Max(0, entry.PreviewCount - 1);
                var actualCopies = entry.GeneratedCopies.Count(go => go != null);
                if (actualCopies != expectedCopies)
                {
                    report.Info.Add($"{preview.name}: list preview wants {expectedCopies} generated copy/copies, current scene has {actualCopies}. Use Rebuild Copies.");
                }

                for (int i = 0; i < entry.GeneratedCopies.Count; i++)
                {
                    var copy = entry.GeneratedCopies[i];
                    if (copy == null)
                        continue;

                    if (HasPrefabChanges(copy))
                    {
                        report.Errors.Add($"{copy.name}: generated list copies are not editable. Move changes to item 01, then rebuild copies.");
                    }
                }
            }

            var accidentalApply = TryCreateAccidentalApplyInfo(entry);
            if (accidentalApply != null)
            {
                report.AccidentalApplies.Add(accidentalApply);
                report.Errors.Add($"{preview.name}: preview-only object appears to be applied inside parent prefab '{accidentalApply.ParentPrefabRoot.name}'. Use Fix Accidental Applies.");
            }
        }

        private void ApplyAllSafeChanges()
        {
            if (_root == null)
                return;

            RefreshReport();
            if (_report.Errors.Count > 0)
            {
                EditorUtility.DisplayDialog("mvcExpress View Authoring", "Fix report errors before applying changes.", "OK");
                return;
            }

            var applied = 0;
            var previewObjects = new HashSet<GameObject>(_root.PreviewEntries.Where(e => e.PreviewObject != null).Select(e => e.PreviewObject));
            var generatedCopies = new HashSet<GameObject>(_root.PreviewEntries.SelectMany(e => e.GeneratedCopies).Where(go => go != null));

            foreach (var instanceRoot in GetPrefabInstanceRootsUnderAuthoringRoot())
            {
                if (instanceRoot == null || generatedCopies.Contains(instanceRoot))
                    continue;

                var assetPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instanceRoot);
                if (string.IsNullOrWhiteSpace(assetPath))
                    continue;

                var isPreviewRoot = previewObjects.Contains(instanceRoot);
                foreach (var objectOverride in PrefabUtility.GetObjectOverrides(instanceRoot, false))
                {
                    if (!isPreviewRoot && BelongsToPreviewSubtree(objectOverride.instanceObject, previewObjects, generatedCopies))
                        continue;

                    PrefabUtility.ApplyObjectOverride(objectOverride.instanceObject, assetPath, InteractionMode.UserAction);
                    applied++;
                }

                foreach (var addedComponent in PrefabUtility.GetAddedComponents(instanceRoot))
                {
                    if (!isPreviewRoot && BelongsToPreviewSubtree(addedComponent.instanceComponent, previewObjects, generatedCopies))
                        continue;

                    PrefabUtility.ApplyAddedComponent(addedComponent.instanceComponent, assetPath, InteractionMode.UserAction);
                    applied++;
                }
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(_root.gameObject.scene);
            RefreshReport();
            Debug.Log($"[mvcExpress] Applied {applied} safe prefab override(s) under '{_root.name}'.");
        }

        private void FixAccidentalApplies(List<AccidentalApplyInfo> applies)
        {
            if (applies == null || applies.Count == 0)
                return;

            if (!EditorUtility.DisplayDialog(
                    "mvcExpress View Authoring",
                    "This will remove preview-only children from their parent prefab assets and recreate them in the authoring scene. Continue?",
                    "Fix",
                    "Cancel"))
            {
                return;
            }

            var fixedCount = 0;

            foreach (var apply in applies.ToArray())
            {
                if (apply == null || apply.Entry?.PreviewObject == null || apply.SourcePrefab == null)
                    continue;

                var sceneParentRoot = apply.ParentPrefabRoot;
                var sceneParent = sceneParentRoot != null ? sceneParentRoot.transform.Find(apply.SceneParentPath) : null;
                if (sceneParent == null)
                {
                    Debug.LogWarning($"[mvcExpress] Could not find scene parent path '{apply.SceneParentPath}' for '{apply.Entry.PreviewObject.name}'.");
                    continue;
                }

                var localPosition = apply.Entry.PreviewObject.transform.localPosition;
                var localRotation = apply.Entry.PreviewObject.transform.localRotation;
                var localScale = apply.Entry.PreviewObject.transform.localScale;
                var siblingIndex = apply.Entry.PreviewObject.transform.GetSiblingIndex();
                var objectName = apply.Entry.PreviewObject.name;

                var prefabContents = PrefabUtility.LoadPrefabContents(apply.ParentPrefabPath);
                try
                {
                    var appliedChild = prefabContents.transform.Find(apply.NestedObjectPath);
                    if (appliedChild == null)
                    {
                        Debug.LogWarning($"[mvcExpress] Could not find '{apply.NestedObjectPath}' in prefab asset '{apply.ParentPrefabPath}'.");
                        continue;
                    }

                    DestroyImmediate(appliedChild.gameObject);
                    PrefabUtility.SaveAsPrefabAsset(prefabContents, apply.ParentPrefabPath);
                }
                finally
                {
                    PrefabUtility.UnloadPrefabContents(prefabContents);
                }

                var restored = PrefabUtility.InstantiatePrefab(apply.SourcePrefab, sceneParent) as GameObject;
                if (restored == null)
                    continue;

                Undo.RegisterCreatedObjectUndo(restored, "Restore Preview Only Object");
                restored.name = objectName;
                restored.transform.localPosition = localPosition;
                restored.transform.localRotation = localRotation;
                restored.transform.localScale = localScale;
                restored.transform.SetSiblingIndex(Mathf.Min(siblingIndex, sceneParent.childCount - 1));
                apply.Entry.PreviewObject = restored;
                fixedCount++;
            }

            if (fixedCount > 0)
            {
                AssetDatabase.SaveAssets();
                MarkRootDirty();
                MvcViewAuthoringHierarchyOverlay.RebuildCache();
                RefreshReport();
            }

            Debug.Log($"[mvcExpress] Fixed {fixedCount} accidentally applied preview-only object(s).");
        }

        private void RebuildListCopies(MvcViewAuthoringPreviewEntry entry)
        {
            if (_root == null || entry?.PreviewObject == null)
                return;

            CleanupGeneratedCopies(entry);

            if (!entry.ListItem || entry.PreviewCount <= 1)
            {
                RenamePreviewEntry(entry);
                MarkRootDirty();
                MvcViewAuthoringHierarchyOverlay.RebuildCache();
                return;
            }

            var prefabAsset = PrefabUtility.GetCorrespondingObjectFromSource(entry.PreviewObject);
            if (prefabAsset == null)
            {
                EditorUtility.DisplayDialog("mvcExpress View Authoring", "Could not find the source prefab for the selected list item.", "OK");
                return;
            }

            var parent = entry.PreviewObject.transform.parent;
            var baseSiblingIndex = entry.PreviewObject.transform.GetSiblingIndex();

            RenamePreviewEntry(entry);

            for (int i = 2; i <= entry.PreviewCount; i++)
            {
                var copy = PrefabUtility.InstantiatePrefab(prefabAsset, parent) as GameObject;
                if (copy == null)
                    continue;

                Undo.RegisterCreatedObjectUndo(copy, "Create Preview Copy");
                CopyLocalTransform(entry.PreviewObject.transform, copy.transform);
                copy.transform.SetSiblingIndex(baseSiblingIndex + i - 1);
                copy.name = $"{GetPrefabBaseName(entry.PreviewObject)} [PreviewOnly {i:00}]";
                entry.GeneratedCopies.Add(copy);
            }

            MarkRootDirty();
            MvcViewAuthoringHierarchyOverlay.RebuildCache();
            RefreshReport();
        }

        private void CleanupGeneratedCopies(MvcViewAuthoringPreviewEntry entry)
        {
            if (entry == null)
                return;

            for (int i = entry.GeneratedCopies.Count - 1; i >= 0; i--)
            {
                var copy = entry.GeneratedCopies[i];
                entry.GeneratedCopies.RemoveAt(i);
                if (copy != null)
                {
                    Undo.DestroyObjectImmediate(copy);
                }
            }
        }

        private void CleanupNullEntries()
        {
            for (int i = _root.PreviewEntries.Count - 1; i >= 0; i--)
            {
                var entry = _root.PreviewEntries[i];
                if (entry == null)
                {
                    _root.PreviewEntries.RemoveAt(i);
                    continue;
                }

                entry.GeneratedCopies.RemoveAll(go => go == null);
            }
        }

        private IEnumerable<GameObject> GetPrefabInstanceRootsUnderAuthoringRoot()
        {
            if (_root == null)
                return Array.Empty<GameObject>();

            return _root.GetComponentsInChildren<Transform>(true)
                .Select(t => t.gameObject)
                .Where(PrefabUtility.IsAnyPrefabInstanceRoot)
                .Distinct();
        }

        private int CountPreviewAddedGameObjects(
            IList<AddedGameObject> addedGameObjects,
            HashSet<GameObject> previewObjects,
            HashSet<GameObject> generatedCopies)
        {
            var count = 0;
            foreach (var added in addedGameObjects)
            {
                var go = added.instanceGameObject;
                if (go == null)
                    continue;

                if (previewObjects.Contains(go) || generatedCopies.Contains(go) || HasPreviewAncestor(go, previewObjects, generatedCopies))
                {
                    count++;
                }
            }

            return count;
        }

        private static bool HasPreviewAncestor(GameObject go, HashSet<GameObject> previewObjects, HashSet<GameObject> generatedCopies)
        {
            var current = go.transform.parent;
            while (current != null)
            {
                if (previewObjects.Contains(current.gameObject) || generatedCopies.Contains(current.gameObject))
                    return true;

                current = current.parent;
            }

            return false;
        }

        private static bool BelongsToPreviewSubtree(
            UnityEngine.Object obj,
            HashSet<GameObject> previewObjects,
            HashSet<GameObject> generatedCopies)
        {
            var go = obj switch
            {
                GameObject gameObject => gameObject,
                Component component => component.gameObject,
                _ => null
            };

            if (go == null)
                return false;

            if (previewObjects.Contains(go) || generatedCopies.Contains(go))
                return true;

            return HasPreviewAncestor(go, previewObjects, generatedCopies);
        }

        private AccidentalApplyInfo TryCreateAccidentalApplyInfo(MvcViewAuthoringPreviewEntry entry)
        {
            var preview = entry?.PreviewObject;
            if (preview == null)
                return null;

            var parentPrefabRoot = GetNearestParentPrefabInstanceRoot(preview);
            if (parentPrefabRoot == null)
                return null;

            var addedGameObjects = PrefabUtility.GetAddedGameObjects(parentPrefabRoot);
            if (addedGameObjects.Any(added => added.instanceGameObject == preview || IsChildOf(preview, added.instanceGameObject)))
                return null;

            var parentPrefabPath = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(parentPrefabRoot);
            var sourcePrefab = PrefabUtility.GetCorrespondingObjectFromSource(preview);
            if (string.IsNullOrWhiteSpace(parentPrefabPath) || sourcePrefab == null)
                return null;

            return new AccidentalApplyInfo
            {
                Entry = entry,
                ParentPrefabRoot = parentPrefabRoot,
                ParentPrefabPath = parentPrefabPath,
                SourcePrefab = sourcePrefab,
                NestedObjectPath = GetTransformPath(parentPrefabRoot.transform, preview.transform),
                SceneParentPath = GetTransformPath(parentPrefabRoot.transform, preview.transform.parent)
            };
        }

        private static GameObject GetNearestParentPrefabInstanceRoot(GameObject go)
        {
            var current = go != null ? go.transform.parent : null;
            while (current != null)
            {
                if (PrefabUtility.IsAnyPrefabInstanceRoot(current.gameObject))
                    return current.gameObject;

                current = current.parent;
            }

            return null;
        }

        private static bool IsChildOf(GameObject child, GameObject possibleParent)
        {
            return child != null && possibleParent != null && child.transform.IsChildOf(possibleParent.transform);
        }

        private static string GetTransformPath(Transform root, Transform target)
        {
            if (root == null || target == null || root == target)
                return string.Empty;

            var names = new Stack<string>();
            var current = target;
            while (current != null && current != root)
            {
                names.Push(current.name);
                current = current.parent;
            }

            return string.Join("/", names);
        }

        private bool IsUnderRoot(GameObject go)
        {
            return _root != null && go != null && go.transform.IsChildOf(_root.transform);
        }

        private MvcViewAuthoringPreviewEntry FindEntry(GameObject previewObject)
        {
            return _root.PreviewEntries.FirstOrDefault(e => e != null && e.PreviewObject == previewObject);
        }

        private bool IsGeneratedCopy(GameObject go)
        {
            return _root != null && _root.PreviewEntries.Any(e => e != null && e.GeneratedCopies.Contains(go));
        }

        private static bool HasPrefabChanges(GameObject instanceRoot)
        {
            if (instanceRoot == null || !PrefabUtility.IsAnyPrefabInstanceRoot(instanceRoot))
                return false;

            return PrefabUtility.GetObjectOverrides(instanceRoot, false).Count > 0
                || PrefabUtility.GetAddedComponents(instanceRoot).Count > 0
                || PrefabUtility.GetRemovedComponents(instanceRoot).Count > 0
                || PrefabUtility.GetAddedGameObjects(instanceRoot).Count > 0;
        }

        private void RenamePreviewEntry(MvcViewAuthoringPreviewEntry entry)
        {
            if (entry?.PreviewObject == null)
                return;

            var baseName = GetPrefabBaseName(entry.PreviewObject);
            var suffix = entry.ListItem ? " [PreviewOnly 01]" : PreviewSuffix;
            entry.PreviewObject.name = baseName + suffix;
        }

        private static string GetPrefabBaseName(GameObject go)
        {
            var source = PrefabUtility.GetCorrespondingObjectFromSource(go);
            if (source != null)
                return source.name;

            var name = go.name;
            var index = name.IndexOf(" [PreviewOnly", StringComparison.Ordinal);
            return index >= 0 ? name.Substring(0, index) : name;
        }

        private static void CopyLocalTransform(Transform source, Transform target)
        {
            target.localPosition = source.localPosition;
            target.localRotation = source.localRotation;
            target.localScale = source.localScale;
        }

        private void MarkRootDirty()
        {
            if (_root == null)
                return;

            EditorUtility.SetDirty(_root);
            EditorSceneManager.MarkSceneDirty(_root.gameObject.scene);
        }

        private sealed class Report
        {
            public readonly List<string> Errors = new List<string>();
            public readonly List<string> SafeChanges = new List<string>();
            public readonly List<string> SkippedChanges = new List<string>();
            public readonly List<string> ManualReview = new List<string>();
            public readonly List<string> Info = new List<string>();
            public readonly List<AccidentalApplyInfo> AccidentalApplies = new List<AccidentalApplyInfo>();

            public bool HasMessages =>
                Errors.Count > 0 ||
                SafeChanges.Count > 0 ||
                SkippedChanges.Count > 0 ||
                ManualReview.Count > 0 ||
                Info.Count > 0;
        }

        private sealed class AccidentalApplyInfo
        {
            public MvcViewAuthoringPreviewEntry Entry;
            public GameObject ParentPrefabRoot;
            public string ParentPrefabPath;
            public GameObject SourcePrefab;
            public string NestedObjectPath;
            public string SceneParentPath;
        }
    }
}

using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace mvcExpress.Editor.Windows
{
    /// <summary>
    /// Editor window (Tools/mvcExpress/Set StartUp Scene) that lets a developer pick and persist which scene the editor auto-opens on startup.
    /// </summary>
    [InitializeOnLoad]
    internal sealed class MvcStartupSceneWindow : EditorWindow
    {
        private const string EditorPrefsSceneGuidKey = "mvcExpress.StartupScene.Guid";
        private const string MenuPath = "Tools/mvcExpress/SetStartUp Scene";

        private const float SceneButtonWidth = 180f;
        private const float SceneButtonHeight = 28f;
        private const float SceneButtonSpacing = 4f;

        // Set by SceneAssetPostprocessor when .unity files are imported, deleted, or moved.
        private static bool s_sceneListDirty = true;

        private List<SceneAsset> _scenes;
        private bool _isScanning;
        private Vector2 _scrollPosition;

        static MvcStartupSceneWindow()
        {
            // Defer until after domain reload completes — EditorPrefs is not accessible from a static constructor.
            EditorApplication.delayCall += ApplyStoredStartupScene;
        }

        [MenuItem(MenuPath, priority = 1900)]
        private static void Open()
        {
            var window = GetWindow<MvcStartupSceneWindow>(true, "mvcExpress Startup Scene");
            window.minSize = new Vector2(460, 340);
            window.Show();
        }

        private void OnEnable()
        {
            ScheduleScan();
        }

        private void OnGUI()
        {
            if (s_sceneListDirty && !_isScanning)
                ScheduleScan();

            DrawHeader();
            EditorGUILayout.Space(8f);
            DrawCurrentState();
            EditorGUILayout.Space(8f);
            DrawSceneGrid();
        }

        // ----- Header -----

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Play Mode Startup Scene", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "When set, Unity enters Play Mode from this scene only, regardless of the scenes currently open for editing. Clear it to restore normal Unity Play behavior.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            var currentScene = EditorSceneManager.playModeStartScene;
            var nextScene = EditorGUILayout.ObjectField("Startup Scene", currentScene, typeof(SceneAsset), false) as SceneAsset;
            if (EditorGUI.EndChangeCheck())
                SetStartupScene(nextScene);

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Use Active Scene"))
                    SetStartupScene(GetActiveSceneAsset());

                using (new EditorGUI.DisabledScope(EditorSceneManager.playModeStartScene == null))
                {
                    if (GUILayout.Button("Clear"))
                        ClearStartupScene();
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(_isScanning))
                {
                    if (GUILayout.Button("Refresh", GUILayout.Width(70f)))
                        ScheduleScan();
                }
            }
        }

        // ----- Current state label -----

        private static void DrawCurrentState()
        {
            var scene = EditorSceneManager.playModeStartScene;
            if (scene == null)
            {
                EditorGUILayout.HelpBox("No startup scene is set. Unity will start Play Mode normally.", MessageType.None);
                return;
            }

            var path = AssetDatabase.GetAssetPath(scene);
            EditorGUILayout.HelpBox($"Play Mode will start from:\n{path}", MessageType.None);
        }

        // ----- Scene grid -----

        private void DrawSceneGrid()
        {
            if (_isScanning)
            {
                EditorGUILayout.HelpBox("Searching for scenes...", MessageType.None);
                return;
            }

            if (_scenes == null || _scenes.Count == 0)
            {
                EditorGUILayout.HelpBox("No scenes found in the project.", MessageType.None);
                return;
            }

            EditorGUILayout.LabelField($"All Scenes ({_scenes.Count})", EditorStyles.boldLabel);

            var activeScene = EditorSceneManager.playModeStartScene;
            var activePath = activeScene != null ? AssetDatabase.GetAssetPath(activeScene) : null;

            float available = EditorGUIUtility.currentViewWidth - 24f;
            int columns = Mathf.Max(1, Mathf.FloorToInt(available / (SceneButtonWidth + SceneButtonSpacing)));

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            int col = 0;
            EditorGUILayout.BeginHorizontal();

            foreach (var scene in _scenes)
            {
                if (scene == null) continue;

                var path = AssetDatabase.GetAssetPath(scene);
                bool isActive = path == activePath;

                var prevColor = GUI.backgroundColor;
                if (isActive) GUI.backgroundColor = new Color(0.45f, 0.85f, 0.45f);

                var label = isActive ? $"✓  {scene.name}" : scene.name;
                if (GUILayout.Button(label, GUILayout.Width(SceneButtonWidth), GUILayout.Height(SceneButtonHeight)))
                {
                    SetStartupScene(scene);
                    Repaint();
                }

                GUI.backgroundColor = prevColor;

                col++;
                if (col >= columns)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                    col = 0;
                }
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
        }

        // ----- Scan -----

        private void ScheduleScan()
        {
            _isScanning = true;
            s_sceneListDirty = false;
            _scenes = null;
            Repaint();
            EditorApplication.delayCall += ExecuteScan;
        }

        private void ExecuteScan()
        {
            EditorApplication.delayCall -= ExecuteScan;

            if (this == null) return; // window was closed before the deferred call fired

            var result = new List<SceneAsset>();
            var guids = AssetDatabase.FindAssets("t:Scene");

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(path);
                if (scene != null)
                    result.Add(scene);
            }

            result.Sort((a, b) => string.Compare(a.name, b.name, System.StringComparison.OrdinalIgnoreCase));

            _scenes = result;
            _isScanning = false;
            Repaint();
        }

        // ----- Static helpers -----

        private static SceneAsset GetActiveSceneAsset()
        {
            var activeScene = SceneManager.GetActiveScene();
            if (string.IsNullOrWhiteSpace(activeScene.path))
            {
                EditorUtility.DisplayDialog(
                    "mvcExpress Startup Scene",
                    "The active scene has not been saved yet. Save it before using it as a startup scene.",
                    "OK");
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<SceneAsset>(activeScene.path);
        }

        private static void SetStartupScene(SceneAsset scene)
        {
            if (scene == null)
            {
                ClearStartupScene();
                return;
            }

            var path = AssetDatabase.GetAssetPath(scene);
            var guid = AssetDatabase.AssetPathToGUID(path);
            if (string.IsNullOrWhiteSpace(guid))
            {
                Debug.LogWarning($"[mvcExpress] Could not resolve startup scene asset path '{path}'.");
                return;
            }

            EditorPrefs.SetString(EditorPrefsSceneGuidKey, guid);
            EditorSceneManager.playModeStartScene = scene;
        }

        private static void ClearStartupScene()
        {
            EditorPrefs.DeleteKey(EditorPrefsSceneGuidKey);
            EditorSceneManager.playModeStartScene = null;
        }

        private static void ApplyStoredStartupScene()
        {
            if (!EditorPrefs.HasKey(EditorPrefsSceneGuidKey))
            {
                EditorSceneManager.playModeStartScene = null;
                return;
            }

            var guid = EditorPrefs.GetString(EditorPrefsSceneGuidKey);
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var scene = !string.IsNullOrWhiteSpace(path)
                ? AssetDatabase.LoadAssetAtPath<SceneAsset>(path)
                : null;

            if (scene == null)
            {
                Debug.LogWarning("[mvcExpress] Stored startup scene could not be found. Startup scene was cleared.");
                ClearStartupScene();
                return;
            }

            EditorSceneManager.playModeStartScene = scene;
        }

        // ----- Asset postprocessor for auto-refresh -----

        private class SceneAssetPostprocessor : AssetPostprocessor
        {
            static void OnPostprocessAllAssets(
                string[] importedAssets,
                string[] deletedAssets,
                string[] movedAssets,
                string[] movedFromAssetPaths)
            {
                if (ContainsSceneFile(importedAssets) || ContainsSceneFile(deletedAssets) || ContainsSceneFile(movedAssets))
                    s_sceneListDirty = true;
            }

            private static bool ContainsSceneFile(string[] paths)
            {
                foreach (var p in paths)
                    if (p.EndsWith(".unity"))
                        return true;
                return false;
            }
        }
    }
}

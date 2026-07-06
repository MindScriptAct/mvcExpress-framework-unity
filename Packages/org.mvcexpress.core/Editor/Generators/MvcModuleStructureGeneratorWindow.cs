using System.IO;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.Generators
{
    /// <summary>
    /// Editor window that scaffolds a complete module folder structure (Services/Model/View/Controller subfolders) on disk for a new feature.
    /// </summary>
    public class MvcModuleStructureGeneratorWindow : EditorWindow
    {
        private const string PrefPrefix = "MvcModuleStructureGen";

        private string moduleName = string.Empty;
        private string selectedFolderPath;

        private bool createModuleFolder = true;
        private bool createServices = true;
        private bool createModel = true;
        private bool createView = true;
        private bool createController = true;

        public static void ShowWindow()
        {
            var window = GetWindow<MvcModuleStructureGeneratorWindow>(true, "Module Structure Generator", true);
            window.minSize = new Vector2(420, 320);
            window.maxSize = new Vector2(10000, 10000);
            window.Show();
        }

        private void OnEnable()
        {
            moduleName = EditorPrefs.GetString($"{PrefPrefix}_ModuleName", string.Empty);
            createModuleFolder = EditorPrefs.GetBool($"{PrefPrefix}_CreateModuleFolder", true);
            createServices = EditorPrefs.GetBool($"{PrefPrefix}_CreateServices", true);
            createModel = EditorPrefs.GetBool($"{PrefPrefix}_CreateModel", true);
            createView = EditorPrefs.GetBool($"{PrefPrefix}_CreateView", true);
            createController = EditorPrefs.GetBool($"{PrefPrefix}_CreateController", true);

            selectedFolderPath = GetSelectedFolderPath();
        }

        private void OnDisable()
        {
            EditorPrefs.SetString($"{PrefPrefix}_ModuleName", moduleName ?? string.Empty);
            EditorPrefs.SetBool($"{PrefPrefix}_CreateModuleFolder", createModuleFolder);
            EditorPrefs.SetBool($"{PrefPrefix}_CreateServices", createServices);
            EditorPrefs.SetBool($"{PrefPrefix}_CreateModel", createModel);
            EditorPrefs.SetBool($"{PrefPrefix}_CreateView", createView);
            EditorPrefs.SetBool($"{PrefPrefix}_CreateController", createController);
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Module Structure Generator", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Generate a complete module folder structure with selected subdirectories.", MessageType.Info);
            EditorGUILayout.Space(10);

            // Location first
            DrawFolderSelection();
            
            // Module name with checkbox
            DrawModuleNameField();
            
            // Subfolders
            DrawFolderOptions();

            EditorGUILayout.Space(15);

            using (new EditorGUI.DisabledScope(!CanGenerate()))
            {
                if (GUILayout.Button("Generate Module Structure", GUILayout.Height(34)))
                {
                    GenerateModuleStructure();
                }
            }

            EditorGUILayout.Space(5);
        }

        private void DrawFolderSelection()
        {
            EditorGUILayout.LabelField("Location", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Folder", GUILayout.Width(80));
            EditorGUILayout.TextField(selectedFolderPath);
            if (GUILayout.Button("Pick", GUILayout.Width(60)))
            {
                selectedFolderPath = GetSelectedFolderPath();
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
        }

        private void DrawModuleNameField()
        {
            EditorGUILayout.LabelField("Module", EditorStyles.boldLabel);

            // Module name with checkbox on the same line
            EditorGUILayout.BeginHorizontal();
            createModuleFolder = EditorGUILayout.Toggle(createModuleFolder, GUILayout.Width(16));
            EditorGUILayout.LabelField("Module name", GUILayout.Width(84));
            
            using (new EditorGUI.DisabledScope(!createModuleFolder))
            {
                moduleName = EditorGUILayout.TextField(moduleName ?? string.Empty);
            }
            EditorGUILayout.EndHorizontal();

            if (createModuleFolder)
            {
                if (string.IsNullOrWhiteSpace(moduleName))
                {
                    EditorGUILayout.HelpBox("Enter a module name.", MessageType.Error);
                }
                else if (!IsValidFolderName(moduleName))
                {
                    EditorGUILayout.HelpBox("Module name contains invalid characters for a folder name.", MessageType.Error);
                }
                else
                {
                    var folder = string.IsNullOrWhiteSpace(selectedFolderPath) ? "Assets" : selectedFolderPath;
                    EditorGUILayout.LabelField($"Will create: {folder}/{moduleName}", EditorStyles.miniLabel);
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Subfolders will be created directly in the selected location.", MessageType.Info);
            }

            EditorGUILayout.Space(5);
        }

        private void DrawFolderOptions()
        {
            EditorGUILayout.LabelField("Subfolders", EditorStyles.boldLabel);

            createServices = EditorGUILayout.ToggleLeft("Services", createServices);
            createModel = EditorGUILayout.ToggleLeft("Model", createModel);
            createView = EditorGUILayout.ToggleLeft("View", createView);
            createController = EditorGUILayout.ToggleLeft("Controller", createController);

            if (!createServices && !createModel && !createView && !createController)
            {
                EditorGUILayout.HelpBox("Select at least one subfolder to create.", MessageType.Warning);
            }

            EditorGUILayout.Space(5);
        }

        private bool CanGenerate()
        {
            // If creating module folder, validate module name
            if (createModuleFolder)
            {
                if (string.IsNullOrWhiteSpace(moduleName))
                    return false;

                if (!IsValidFolderName(moduleName))
                    return false;
            }

            // Must have at least one subfolder selected
            if (!createServices && !createModel && !createView && !createController)
                return false;

            return true;
        }

        private void GenerateModuleStructure()
        {
            if (!CanGenerate())
                return;

            var folder = string.IsNullOrWhiteSpace(selectedFolderPath) ? "Assets" : selectedFolderPath;
            folder = folder.Replace("\\", "/").TrimEnd('/');

            if (!folder.StartsWith("Assets", System.StringComparison.Ordinal))
            {
                EditorUtility.DisplayDialog("Invalid Folder", "Selected folder must be under 'Assets'.", "OK");
                return;
            }

            string targetFolderPath;
            string absoluteTargetPath;

            if (createModuleFolder)
            {
                // Create module folder and subfolders inside it
                targetFolderPath = $"{folder}/{moduleName}";
                absoluteTargetPath = Path.GetFullPath(targetFolderPath);

                if (Directory.Exists(absoluteTargetPath))
                {
                    if (!EditorUtility.DisplayDialog("Folder Exists",
                        $"Module folder already exists:\n{targetFolderPath}\n\nCreate subfolders anyway?",
                        "Continue", "Cancel"))
                    {
                        return;
                    }
                }
                else
                {
                    Directory.CreateDirectory(absoluteTargetPath);
                }
            }
            else
            {
                // Create subfolders directly in selected location (no module folder)
                targetFolderPath = folder;
                absoluteTargetPath = Path.GetFullPath(targetFolderPath);

                if (!Directory.Exists(absoluteTargetPath))
                {
                    EditorUtility.DisplayDialog("Invalid Folder", $"Target folder does not exist:\n{targetFolderPath}", "OK");
                    return;
                }

                // Check if any subfolders already exist
                bool hasExisting = false;
                string existingFolders = string.Empty;

                if (createServices && Directory.Exists(Path.Combine(absoluteTargetPath, "Services")))
                {
                    hasExisting = true;
                    existingFolders += "Services\n";
                }
                if (createModel && Directory.Exists(Path.Combine(absoluteTargetPath, "Model")))
                {
                    hasExisting = true;
                    existingFolders += "Model\n";
                }
                if (createView && Directory.Exists(Path.Combine(absoluteTargetPath, "View")))
                {
                    hasExisting = true;
                    existingFolders += "View\n";
                }
                if (createController && Directory.Exists(Path.Combine(absoluteTargetPath, "Controller")))
                {
                    hasExisting = true;
                    existingFolders += "Controller\n";
                }

                if (hasExisting)
                {
                    if (!EditorUtility.DisplayDialog("Folders Exist",
                        $"Some folders already exist in:\n{targetFolderPath}\n\n{existingFolders}\nCreate remaining folders?",
                        "Continue", "Cancel"))
                    {
                        return;
                    }
                }
            }

            // Create selected subfolders
            if (createServices)
                CreateFolder(targetFolderPath, "Services");

            if (createModel)
                CreateFolder(targetFolderPath, "Model");

            if (createView)
                CreateFolder(targetFolderPath, "View");

            if (createController)
                CreateFolder(targetFolderPath, "Controller");

            AssetDatabase.Refresh();

            Close();
        }

        private void CreateFolder(string parentPath, string folderName)
        {
            var folderPath = $"{parentPath}/{folderName}";
            var absolutePath = Path.GetFullPath(folderPath);

            if (!Directory.Exists(absolutePath))
            {
                Directory.CreateDirectory(absolutePath);
            }
        }

        private string GetSelectedFolderPath()
        {
            var path = "Assets";
            var obj = Selection.activeObject;
            if (obj != null)
            {
                var selectedPath = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(selectedPath))
                {
                    if (Directory.Exists(selectedPath))
                        path = selectedPath;
                    else
                        path = Path.GetDirectoryName(selectedPath)?.Replace("\\", "/");
                }
            }
            return path;
        }

        private bool IsValidFolderName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var invalidChars = Path.GetInvalidFileNameChars();
            for (int i = 0; i < name.Length; i++)
            {
                for (int j = 0; j < invalidChars.Length; j++)
                {
                    if (name[i] == invalidChars[j])
                        return false;
                }
            }

            return true;
        }
    }
}

using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.Settings
{
    internal static class MvcExpressSettingsProvider
    {
        private const string ProjectSettingsPath = "Project/mvcExpress";

        private static readonly string[] Tabs = { "General", "Composition", "Messaging", "Performance", "Console", "Plugins" };

        private const string PrefKeyMessagingGenericFoldout = "org.mvcexpress.settings.messaging.samples.generic";
        private const string PrefKeyMessagingPayloadFoldout = "org.mvcexpress.settings.messaging.samples.payload";
        private const string PrefKeyMessagingCustomFoldout = "org.mvcexpress.settings.messaging.samples.custom";

        private static GUIStyle s_headerStyle;
        private static GUIStyle s_subHeaderStyle;
        private static GUIStyle s_codeStyle;

        /// <summary>
        /// Checks if the MVC Console package is installed by looking for the MvcConsoleWindow type.
        /// </summary>
        private static bool IsConsolePackageInstalled()
        {
            var consoleType = System.Type.GetType("mvcExpress.Console.MvcConsoleWindow, org.mvcexpress.console.Editor");
            return consoleType != null;
        }

        /// <summary>
        /// Resets all Console settings to their default values.
        /// </summary>
        private static void ResetConsoleSettingsToDefaults()
        {
            // Memory Management defaults
            EditorPrefs.SetBool("MvcConsole_EnableDiskArchive", true);
            EditorPrefs.SetInt("MvcConsole_MemoryThreshold", 50000);
            EditorPrefs.SetInt("MvcConsole_ArchiveChunkSize", 10000);
            EditorPrefs.SetInt("MvcConsole_TargetMemorySize", 40000);

            // Display Options defaults
            EditorPrefs.SetBool("MvcConsole_ShowTimestamps", false);
            EditorPrefs.SetBool("MvcConsole_ShowFrameNumbers", false);
            EditorPrefs.SetBool("MvcConsole_ShowModule", false);
            EditorPrefs.SetBool("MvcConsole_AutoScroll", true);

            // Clear Options defaults
            EditorPrefs.SetBool("MvcConsolePro_ClearOnPlay", false);
            EditorPrefs.SetBool("MvcConsolePro_ClearOnBuild", false);
            EditorPrefs.SetBool("MvcConsolePro_ClearOnRecompile", false);

            Debug.Log("[mvcExpress] Console settings reset to defaults.");
        }

        [SettingsProvider]
        private static SettingsProvider Create()
        {
            var provider = new SettingsProvider(ProjectSettingsPath, SettingsScope.Project)
            {
                label = "mvcExpress",
                guiHandler = _ => OnGUI(),
                keywords = new HashSet<string>(new[] { "mvcExpress", "composition", "registration", "styles", "messaging", "plugins", "help", "inspector", "logging", "global", "console", "pro", "unity", "attribute", "code", "forward", "output", "debug", "fallback", "silent" })
            };

            return provider;
        }

        private static void OnGUI()
        {
            GUILayout.Space(4);

            var tab = EditorPrefs.GetInt("org.mvcexpress.settings.tab", 0);
            tab = GUILayout.Toolbar(tab, Tabs);
            tab = Mathf.Clamp(tab, 0, Tabs.Length - 1);
            EditorPrefs.SetInt("org.mvcexpress.settings.tab", tab);

            GUILayout.Space(8);

            EnsureStyles();

            switch (tab)
            {
                case 0:
                    DrawGeneral();
                    break;
                case 1:
                    DrawComposition();
                    break;
                case 2:
                    DrawMessaging();
                    break;
                case 3:
                    DrawPerformance();
                    break;
                case 4:
                    DrawConsole();
                    break;
                case 5:
                    DrawPlugins();
                    break;
            }
        }

        private static void EnsureStyles()
        {
            if (s_headerStyle == null)
            {
                s_headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    wordWrap = true
                };
            }

            if (s_subHeaderStyle == null)
            {
                s_subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    wordWrap = true
                };
            }

            if (s_codeStyle == null)
            {
                s_codeStyle = new GUIStyle(EditorStyles.textArea)
                {
                    wordWrap = false,
                    font = EditorStyles.miniFont
                };
            }
        }

        private static void DrawGeneral()
        {
            EditorGUILayout.LabelField("Inspector", s_subHeaderStyle);
            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                var value = EditorGUILayout.ToggleLeft(
                    new GUIContent("Show Help In Inspectors", "Display mvcExpress help boxes and guidance in inspectors (MvcModule, MvcFacade, registries)."),
                    MvcExpressProjectSettings.HelpInInspector);

                if (scope.changed)
                {
                    MvcExpressProjectSettings.HelpInInspector = value;
                }
            }

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Hierarchy Icons", s_subHeaderStyle);

            bool anyIconChanged = false;

            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                var v = EditorGUILayout.ToggleLeft(
                    new GUIContent("Module & Facade Icons",
                        "Draw icons next to MvcFacade and MvcModule GameObjects in the Hierarchy window."),
                    MvcExpressProjectSettings.HierarchyIconsModuleFacade);
                if (scope.changed) { MvcExpressProjectSettings.HierarchyIconsModuleFacade = v; anyIconChanged = true; }
            }

            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                var v = EditorGUILayout.ToggleLeft(
                    new GUIContent("Registry Icons",
                        "Draw icons next to registry containers (Services / Model / Controller / View) in the Hierarchy window."),
                    MvcExpressProjectSettings.HierarchyIconsRegistries);
                if (scope.changed) { MvcExpressProjectSettings.HierarchyIconsRegistries = v; anyIconChanged = true; }
            }

            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                var v = EditorGUILayout.ToggleLeft(
                    new GUIContent("Actor Icons",
                        "Draw icons next to MediatorBehaviour, ProxyBehaviour, and code-proxy debug wrapper GameObjects in the Hierarchy window."),
                    MvcExpressProjectSettings.HierarchyIconsActors);
                if (scope.changed) { MvcExpressProjectSettings.HierarchyIconsActors = v; anyIconChanged = true; }
            }

            if (anyIconChanged)
                EditorApplication.RepaintHierarchyWindow();

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Logging", s_subHeaderStyle);
            
            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                var globalLogging = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "Enable Global Logging", 
                        "Enable logging for ALL modules across the entire mvcExpress framework. " +
                        "When enabled, all modules will log regardless of their individual LoggingEnabled settings. " +
                        "This setting persists across Unity sessions."),
                    MvcExpressProjectSettings.GlobalLoggingEnabled);

                if (scope.changed)
                {
                    MvcExpressProjectSettings.GlobalLoggingEnabled = globalLogging;
                }
            }

            EditorGUILayout.Space(4);
            if (MvcExpressProjectSettings.GlobalLoggingEnabled)
            {
                EditorGUILayout.HelpBox(
                    "Global logging is ENABLED. All modules will log to Console Pro. " +
                    "Individual module LoggingEnabled settings are overridden. " +
                    "Open Console Pro (Window ? mvcExpress ? Console Pro) to see logs.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Global logging is DISABLED. Each module controls its own logging via the LoggingEnabled property. " +
                    "Enable 'Module Logging' on individual modules (in their Inspector) to see their logs.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(8);
            
            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                var forwardUnityLogs = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "Forward Unity Logs to Console Pro", 
                        "Forward all Unity logs (Debug.Log, Debug.LogWarning, Debug.LogError) to MVC Console Pro. " +
                        "These logs will appear in the 'Output' filter category. " +
                        "This setting persists across Unity sessions."),
                    MvcExpressProjectSettings.ForwardUnityLogs);

                if (scope.changed)
                {
                    MvcExpressProjectSettings.ForwardUnityLogs = forwardUnityLogs;
                }
            }

            EditorGUILayout.Space(4);
            if (MvcExpressProjectSettings.ForwardUnityLogs)
            {
                EditorGUILayout.HelpBox(
                    "Unity log forwarding is ENABLED. All Debug.Log(), Debug.LogWarning(), and Debug.LogError() calls " +
                    "will appear in Console Pro under the 'Output' filter. " +
                    "Open Console Pro (Window ? mvcExpress ? Console Pro) to see Unity logs alongside mvcExpress logs.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Unity log forwarding is DISABLED. Unity logs will only appear in Unity's default Console window. " +
                    "Enable this option to centralize all logs in Console Pro for easier debugging.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(8);
            
            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                var useDebugFallback = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        "Use Unity Debug.Log Fallback",
                        "When no custom logger is injected and Console Pro is not active, framework logs will use Unity's Debug.Log. " +
                        "Disable this for silent operation (logs will only go to custom logger or Console Pro)."),
                    MvcExpressProjectSettings.UseUnityDebugLogFallback);

                if (scope.changed)
                {
                    MvcExpressProjectSettings.UseUnityDebugLogFallback = useDebugFallback;
                }
            }

            EditorGUILayout.Space(4);
            if (MvcExpressProjectSettings.UseUnityDebugLogFallback)
            {
                EditorGUILayout.HelpBox(
                    "Unity Debug.Log fallback is ENABLED. Framework logs will appear in Unity Console when no custom logger is set. " +
                    "This is useful for debugging but can create noise in the Unity Console.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Unity Debug.Log fallback is DISABLED. Framework logs will be silent unless you: " +
                    "1) Inject a custom IMvcLogger, or 2) Open Console Pro. " +
                    "Enable this option to see logs in Unity Console as fallback.",
                    MessageType.Info);
            }
        }

        private static void DrawMessaging()
        {
            EditorGUILayout.LabelField("Messaging", s_headerStyle);

            var richWrap = new GUIStyle(EditorStyles.wordWrappedLabel) { richText = true };
            EditorGUILayout.LabelField(
                "mvcExpress currently provides multiple messaging APIs. <b>Ability to customize it comming soon.</b>",
                richWrap);
            EditorGUILayout.Space(8);

            DrawMessagingOption(
                title: "Generic messaging (enabled)",
                description:
                    "Messages are identified by a message type and carry parameters through a generated generic signature. " +
                    "This avoids allocation for payload objects and is optimized for frequent dispatch.",
                checkboxLabel: "Generic messaging",
                checkboxValue: true,
                checkboxTooltip: "This messaging system is currently included.",
                foldoutKey: PrefKeyMessagingGenericFoldout,
                sampleProvider: BuildGenericMessagingSamples);

            EditorGUILayout.Space(10);

            DrawMessagingOption(
                title: "Payload messaging (enabled)",
                description:
                    "Messages are defined as plain payload objects (classes/structs) with fields or properties. " +
                    "This is straightforward to read and evolve, but it allocates each time a new message payload object is created.",
                checkboxLabel: "Payload messaging",
                checkboxValue: true,
                checkboxTooltip: "This messaging system is currently included.",
                foldoutKey: PrefKeyMessagingPayloadFoldout,
                sampleProvider: BuildPayloadMessagingSamples);

            EditorGUILayout.Space(10);

            DrawMessagingOption(
                title: "Custom messaging (disabled)",
                description:
                    "You can disable built-in messaging and provide your own API by extending mvcExpress via partial classes. " +
                    "This keeps your project’s preferred message signatures without carrying unused editor suggestions.",
                checkboxLabel: "Custom messaging",
                checkboxValue: false,
                checkboxTooltip: "Custom messaging is not provided by the framework. You can implement it in your project using partials.",
                foldoutKey: PrefKeyMessagingCustomFoldout,
                sampleProvider: BuildCustomMessagingSamples);
        }


        private static void DrawComposition()
        {
            EditorGUILayout.LabelField("Composition Styles", s_headerStyle);
            EditorGUILayout.LabelField(
                "Soft enforcement controls editor warnings only. If a style is unchecked, using that style still works but produces a clear editor warning.",
                EditorStyles.wordWrappedLabel);

            var lockedStyle = MvcCompositionStyleSettings.GetLockedStyle();

            if (lockedStyle.HasValue)
            {
                if (!MvcCompositionStyleSettings.IsSoftAllowed(lockedStyle.Value))
                    MvcCompositionStyleSettings.SetSoftAllowed(lockedStyle.Value, true);
                if (!MvcCompositionStyleSettings.IsHardIncluded(lockedStyle.Value))
                {
                    MvcCompositionStyleSettings.SetHardIncluded(lockedStyle.Value, true);
                    MvcCompositionDefineSymbols.Apply(lockedStyle.Value, hardIncluded: true);
                }
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Soft Enforcement", s_subHeaderStyle);
            DrawSoftStyleToggle(
                MvcCompositionStyle.Unity,
                "Unity style includes scene/inspector registries, prefab-backed startup modules, and serialized command or mediator setup.",
                lockedStyle == MvcCompositionStyle.Unity);
            DrawSoftStyleToggle(
                MvcCompositionStyle.Attribute,
                "Attribute style includes [Register], [Bind] (use [Bind(PoolSize=N)] for pooled commands), and [Attach].",
                lockedStyle == MvcCompositionStyle.Attribute);
            DrawSoftStyleToggle(
                MvcCompositionStyle.Code,
                "Code style includes Register(...), Commander.Bind(...), manual mediator attachment, and code-created startup modules.",
                lockedStyle == MvcCompositionStyle.Code);

            EditorGUILayout.Space(12);
            EditorGUILayout.LabelField("Hard Enforcement", s_subHeaderStyle);
            DrawHardStyleToggle(MvcCompositionStyle.Unity, lockedStyle == MvcCompositionStyle.Unity);
            DrawHardStyleToggle(MvcCompositionStyle.Attribute, lockedStyle == MvcCompositionStyle.Attribute);
            DrawHardStyleToggle(MvcCompositionStyle.Code, lockedStyle == MvcCompositionStyle.Code);
        }

        private static void DrawSoftStyleToggle(MvcCompositionStyle style, string tooltip, bool locked)
        {
            if (locked)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ToggleLeft(
                        new GUIContent(MvcCompositionStyleSettings.GetSoftSettingLabel(style),
                            "Locked - enable at least one other style fully before disabling this one."),
                        true);
                }
                return;
            }

            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                var value = EditorGUILayout.ToggleLeft(
                    new GUIContent(MvcCompositionStyleSettings.GetSoftSettingLabel(style), tooltip),
                    MvcCompositionStyleSettings.IsSoftAllowed(style));

                if (scope.changed)
                {
                    MvcCompositionStyleSettings.SetSoftAllowed(style, value);
                }
            }
        }

        private static void DrawHardStyleToggle(MvcCompositionStyle style, bool locked)
        {
            if (locked)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.ToggleLeft(
                        new GUIContent(MvcCompositionStyleSettings.GetHardSettingLabel(style),
                            "Locked - enable at least one other style fully before disabling this one."),
                        true);
                }
                return;
            }

            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                var value = EditorGUILayout.ToggleLeft(
                    new GUIContent(
                        MvcCompositionStyleSettings.GetHardSettingLabel(style),
                        "When unchecked, adds a scripting define symbol (MVC_EXPRESS_NO_*) that excludes this style from compilation."),
                    MvcCompositionStyleSettings.IsHardIncluded(style));

                if (scope.changed)
                {
                    MvcCompositionStyleSettings.SetHardIncluded(style, value);
                    MvcCompositionDefineSymbols.Apply(style, value);
                }
            }
        }
        private static void DrawPlugins()
        {
            EditorGUILayout.HelpBox("Plugin settings will be added here.", MessageType.Info);
        }

        private static void DrawPerformance()
        {
            EditorGUILayout.HelpBox("Performance profiling settings will be added here.", MessageType.Info);
        }

        private static void DrawConsole()
        {
            if (!IsConsolePackageInstalled())
            {
                EditorGUILayout.HelpBox(
                    "MVC Console package is not installed.\n\n" +
                    "MVC Console provides advanced logging, timeline view, dependency inspection, and architecture visualization tools.",
                    MessageType.Info);

                EditorGUILayout.Space(10);

                EditorGUILayout.LabelField("Features:", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("• Advanced log filtering and search", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("• Timeline view with frame-by-frame analysis", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("• Dependency inspector and circular dependency detection", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("• Architecture visualization (list and graph views)", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("• Performance profiling", EditorStyles.wordWrappedLabel);
                EditorGUILayout.LabelField("• Memory-managed log archiving", EditorStyles.wordWrappedLabel);

                EditorGUILayout.Space(10);

                if (GUILayout.Button("Install MVC Console Package", GUILayout.Height(30)))
                {
                    EditorUtility.DisplayDialog(
                        "Install MVC Console",
                        "To install MVC Console:\n\n" +
                        "1. Open Package Manager (Window > Package Manager)\n" +
                        "2. Click the '+' button\n" +
                        "3. Select 'Add package from git URL'\n" +
                        "4. Enter the package URL or select from disk\n\n" +
                        "Or add the package manually to your manifest.json",
                        "OK");
                }

                return;
            }

            // Console package is installed - show settings
            EditorGUILayout.LabelField("Console Settings", s_headerStyle);
            EditorGUILayout.Space(8);

            // Memory Management Section
            EditorGUILayout.LabelField("Memory Management", s_subHeaderStyle);
            using (new EditorGUI.IndentLevelScope())
            {
                using (var scope = new EditorGUI.ChangeCheckScope())
                {
                    var enableDiskArchive = EditorPrefs.GetBool("MvcConsole_EnableDiskArchive", true);
                    enableDiskArchive = EditorGUILayout.ToggleLeft(
                        new GUIContent(
                            "Enable Disk Archive",
                            "If enabled, old logs are archived to disk. If disabled, uses circular buffer and discards old entries."),
                        enableDiskArchive);

                    if (scope.changed)
                    {
                        EditorPrefs.SetBool("MvcConsole_EnableDiskArchive", enableDiskArchive);
                    }
                }

                using (var scope = new EditorGUI.ChangeCheckScope())
                {
                    var memoryThreshold = EditorPrefs.GetInt("MvcConsole_MemoryThreshold", 50000);
                    memoryThreshold = EditorGUILayout.IntSlider(
                        new GUIContent(
                            "Memory Threshold",
                            "Maximum number of log entries kept in memory before archiving to disk"),
                        memoryThreshold,
                        10000,
                        100000);

                    if (scope.changed)
                    {
                        EditorPrefs.SetInt("MvcConsole_MemoryThreshold", memoryThreshold);
                    }
                }

                var enableDiskArchiveValue = EditorPrefs.GetBool("MvcConsole_EnableDiskArchive", true);
                if (enableDiskArchiveValue)
                {
                    using (var scope = new EditorGUI.ChangeCheckScope())
                    {
                        var archiveChunkSize = EditorPrefs.GetInt("MvcConsole_ArchiveChunkSize", 10000);
                        archiveChunkSize = EditorGUILayout.IntSlider(
                            new GUIContent(
                                "Archive Chunk Size",
                                "Number of entries to archive to disk when threshold is reached"),
                            archiveChunkSize,
                            1000,
                            50000);

                        if (scope.changed)
                        {
                            EditorPrefs.SetInt("MvcConsole_ArchiveChunkSize", archiveChunkSize);
                        }
                    }

                    using (var scope = new EditorGUI.ChangeCheckScope())
                    {
                        var targetMemorySize = EditorPrefs.GetInt("MvcConsole_TargetMemorySize", 40000);
                        targetMemorySize = EditorGUILayout.IntSlider(
                            new GUIContent(
                                "Target Memory Size",
                                "Target number of entries to keep in memory after archiving"),
                            targetMemorySize,
                            5000,
                            90000);

                        if (scope.changed)
                        {
                            EditorPrefs.SetInt("MvcConsole_TargetMemorySize", targetMemorySize);
                        }
                    }

                    EditorGUILayout.Space(4);
                    EditorGUILayout.HelpBox(
                        "When memory threshold is reached, the oldest entries are archived to disk. " +
                        "Archived logs can be loaded back into Console for investigation.",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.Space(4);
                    EditorGUILayout.HelpBox(
                        "Disk archive is disabled. Console will use a circular buffer and discard old entries when memory limit is reached.",
                        MessageType.Warning);
                }

                EditorGUILayout.Space(8);
                
                if (GUILayout.Button("Reset to Defaults", GUILayout.Height(24)))
                {
                    if (EditorUtility.DisplayDialog(
                        "Reset Console Settings",
                        "Reset all Console settings to their default values?\n\n" +
                        "This will reset:\n" +
                        "• Memory Management settings\n" +
                        "• Display Options\n" +
                        "• Clear Options",
                        "Reset",
                        "Cancel"))
                    {
                        ResetConsoleSettingsToDefaults();
                    }
                }
            }

            EditorGUILayout.Space(12);

            // Display Options Section
            EditorGUILayout.LabelField("Display Options", s_subHeaderStyle);
            using (new EditorGUI.IndentLevelScope())
            {
                using (var scope = new EditorGUI.ChangeCheckScope())
                {
                    var showTimestamps = EditorPrefs.GetBool("MvcConsole_ShowTimestamps", false);
                    showTimestamps = EditorGUILayout.ToggleLeft(
                        new GUIContent("Show Timestamps", "Display timestamps in console"),
                        showTimestamps);

                    if (scope.changed)
                    {
                        EditorPrefs.SetBool("MvcConsole_ShowTimestamps", showTimestamps);
                    }
                }

                using (var scope = new EditorGUI.ChangeCheckScope())
                {
                    var showFrameNumbers = EditorPrefs.GetBool("MvcConsole_ShowFrameNumbers", false);
                    showFrameNumbers = EditorGUILayout.ToggleLeft(
                        new GUIContent("Show Frame Numbers", "Display frame numbers in console"),
                        showFrameNumbers);

                    if (scope.changed)
                    {
                        EditorPrefs.SetBool("MvcConsole_ShowFrameNumbers", showFrameNumbers);
                    }
                }

                using (var scope = new EditorGUI.ChangeCheckScope())
                {
                    var showModule = EditorPrefs.GetBool("MvcConsole_ShowModule", false);
                    showModule = EditorGUILayout.ToggleLeft(
                        new GUIContent("Show Module Names", "Display module names in console"),
                        showModule);

                    if (scope.changed)
                    {
                        EditorPrefs.SetBool("MvcConsole_ShowModule", showModule);
                    }
                }

                using (var scope = new EditorGUI.ChangeCheckScope())
                {
                    var autoScroll = EditorPrefs.GetBool("MvcConsole_AutoScroll", true);
                    autoScroll = EditorGUILayout.ToggleLeft(
                        new GUIContent("Auto-Scroll", "Automatically scroll to latest entry"),
                        autoScroll);

                    if (scope.changed)
                    {
                        EditorPrefs.SetBool("MvcConsole_AutoScroll", autoScroll);
                    }
                }
            }

            EditorGUILayout.Space(12);

            // Clear Options Section
            EditorGUILayout.LabelField("Clear Options", s_subHeaderStyle);
            using (new EditorGUI.IndentLevelScope())
            {
                using (var scope = new EditorGUI.ChangeCheckScope())
                {
                    var clearOnPlay = EditorPrefs.GetBool("MvcConsolePro_ClearOnPlay", false);
                    clearOnPlay = EditorGUILayout.ToggleLeft(
                        new GUIContent("Clear on Play", "Clear console when entering play mode"),
                        clearOnPlay);

                    if (scope.changed)
                    {
                        EditorPrefs.SetBool("MvcConsolePro_ClearOnPlay", clearOnPlay);
                    }
                }

                using (var scope = new EditorGUI.ChangeCheckScope())
                {
                    var clearOnBuild = EditorPrefs.GetBool("MvcConsolePro_ClearOnBuild", false);
                    clearOnBuild = EditorGUILayout.ToggleLeft(
                        new GUIContent("Clear on Build", "Clear console when building"),
                        clearOnBuild);

                    if (scope.changed)
                    {
                        EditorPrefs.SetBool("MvcConsolePro_ClearOnBuild", clearOnBuild);
                    }
                }

                using (var scope = new EditorGUI.ChangeCheckScope())
                {
                    var clearOnRecompile = EditorPrefs.GetBool("MvcConsolePro_ClearOnRecompile", false);
                    clearOnRecompile = EditorGUILayout.ToggleLeft(
                        new GUIContent("Clear on Recompile", "Clear console when recompiling"),
                        clearOnRecompile);

                    if (scope.changed)
                    {
                        EditorPrefs.SetBool("MvcConsolePro_ClearOnRecompile", clearOnRecompile);
                    }
                }
            }
        }

        private static void DrawMessagingOption(
            string title,
            string description,
            string checkboxLabel,
            bool checkboxValue,
            string checkboxTooltip,
            string foldoutKey,
            System.Func<(string copyAll, (string title, string description, string code)[] blocks)> sampleProvider)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(title, s_subHeaderStyle);
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(description, EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(6);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ToggleLeft(new GUIContent(checkboxLabel, checkboxTooltip), checkboxValue);
            }

            EditorGUILayout.Space(6);

            bool expanded = EditorPrefs.GetBool(foldoutKey, false);
            using (var scope = new EditorGUI.ChangeCheckScope())
            {
                expanded = EditorGUILayout.Foldout(expanded, "Samples", true);
                if (scope.changed)
                {
                    EditorPrefs.SetBool(foldoutKey, expanded);
                }
            }

            if (expanded)
            {
                EditorGUILayout.Space(6);

                var samples = sampleProvider();

                foreach (var block in samples.blocks)
                {
                    DrawSampleBlock(block.title, block.description, block.code);
                    EditorGUILayout.Space(8);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private static void DrawSampleBlock(string title, string description, string code)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            if (!string.IsNullOrWhiteSpace(description))
            {
                EditorGUILayout.LabelField(description, EditorStyles.wordWrappedLabel);
                EditorGUILayout.Space(4);
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Copy", GUILayout.Width(70)))
                {
                    EditorGUIUtility.systemCopyBuffer = code;
                }
            }

            EditorGUILayout.TextArea(code, s_codeStyle);

            EditorGUILayout.EndVertical();
        }

        private static (string copyAll, (string title, string description, string code)[] blocks) BuildGenericMessagingSamples()
        {
            var blocks = new List<(string title, string description, string code)>
            {
                (
                    "How to define messages",
                    "Principle: the message type is the identifier; the generic signature defines the parameter list at compile time (no payload instance needed). "+
                    "Define message types. One message without parameters and one message with three parameters (float, string, Vector2).",
                    "using mvcExpress.Messaging;\nusing UnityEngine;\n\n// No parameters\npublic readonly struct PingMessage : IMessage { }\n\n// 3 parameters (float, string, Vector2)\npublic readonly struct PlayerMovedMessage : IMessage<float, string, Vector2> { }\n"
                ),
                (
                    "How to send messages",
                    "Publish messages from any mvcExpress actor that exposes the message publisher (for example a Proxy, Mediator or Command).",
                    "// No parameters\nPublish<PingMessage>();\n\n// 3 parameters\nPublish<PlayerMovedMessage, float, string, Vector2>(1.5f, \"Player_01\", new Vector2(10f, 3f));\n"
                ),
                (
                    "How to subscribe / unsubscribe",
                    "Principle: subscriptions can be tracked using an optional token, or you can unsubscribe by passing the original handler reference.",
                    "// Subscribe\nvar pingToken = Subscribe<PingMessage>(OnPing);\nSubscribe<PlayerMovedMessage, float, string, Vector2>(OnPlayerMoved);\n\nvoid OnPing()\n{\n    // ...\n}\n\nvoid OnPlayerMoved(float speed, string playerId, Vector2 pos)\n{\n    // ...\n}\n\n// Unsubscribe with token (optional)\nUnsubscribe(pingToken);\n\n// Unsubscribe by handler reference\nUnsubscribe<PlayerMovedMessage, float, string, Vector2>(OnPlayerMoved);\n"
                )
            };

            return (BuildCopyAll(blocks), blocks.ToArray());
        }

        private static (string copyAll, (string title, string description, string code)[] blocks) BuildPayloadMessagingSamples()
        {
            var blocks = new List<(string title, string description, string code)>
            {
                (
                    "How to define messages",
                    "Principle: the payload type is the identifier; fields/properties on the payload carry the data. "+
                    "Define payload types. One message without parameters and one payload with three fields (float, string, Vector2).",
                    "using UnityEngine;\n\n// No parameters (empty payload)\npublic readonly struct PingPayload { }\n\n// 3 parameters\npublic struct PlayerMovedPayload\n{\n    public float Speed;\n    public string PlayerId;\n    public Vector2 Position;\n}\n"
                ),
                (
                    "How to send messages",
                    "Create a payload instance and publish it.",
                    "// No parameters\nPublish(new PingPayload());\n\n// 3 parameters\nPublish(new PlayerMovedPayload\n{\n    Speed = 1.5f,\n    PlayerId = \"Player_01\",\n    Position = new Vector2(10f, 3f)\n});\n"
                ),
                (
                    "How to subscribe / unsubscribe",
                    "Principle: handlers receive the payload instance; you can unsubscribe using an optional token or by passing the original handler reference.",
                    "// Subscribe\nvar pingToken = Subscribe<PingPayload>(OnPing);\nSubscribe<PlayerMovedPayload>(OnPlayerMoved);\n\nvoid OnPing(PingPayload payload)\n{\n    // ...\n}\n\nvoid OnPlayerMoved(PlayerMovedPayload payload)\n{\n    // payload.Speed, payload.PlayerId, payload.Position\n}\n\n// Unsubscribe with token (optional)\nUnsubscribe(pingToken);\n\n// Unsubscribe by handler reference\nUnsubscribe<PlayerMovedPayload>(OnPlayerMoved);\n"
                )
            };

            return (BuildCopyAll(blocks), blocks.ToArray());
        }

        private static (string copyAll, (string title, string description, string code)[] blocks) BuildCustomMessagingSamples()
        {
            var blocks = new List<(string title, string description, string code)>
            {
                (
                    "What to extend (overview)",
                    "Create partial extensions in your project to add your own messaging API. The framework can compile without built-in messaging, and your project provides the methods you prefer.",
                    "// Create these files in your project (any assembly that references mvcExpress core):\n// - MvcModule.CustomMessaging.cs\n// - Proxy.CustomMessaging.cs\n// - MediatorBehaviour.CustomMessaging.cs\n// - CommandBase.CustomMessaging.cs\n\n// Each file adds methods via partial classes.\n"
                ),
                (
                    "Example: extend MvcModule",
                    "Add your own publish/subscribe surface to the module. Implement the internals by delegating to your own bus, Unity events, or another library.",
                    "using mvcExpress;\n\nnamespace YourGame\n{\n    public partial class MvcModule\n    {\n        // TODO: implement your custom messaging API surface here.\n        // Example:\n        // public void PublishCustom(/* your signature */) { }\n        // public object SubscribeCustom(/* your signature */) { return null; }\n        // public void UnsubscribeCustom(object token) { }\n    }\n}\n"
                ),
                (
                    "Example: extend Proxy",
                    "Expose the same API on Proxy so gameplay code can publish/subscribe from actors.",
                    "using mvcExpress;\n\nnamespace YourGame\n    {\n    public partial class Proxy\n    {\n        // TODO: wire to the module’s custom messaging implementation.\n        // Example:\n        // protected void PublishCustom(/* your signature */) => Module.PublishCustom(...);\n    }\n}\n"
                ),
                (
                    "Example: extend MediatorBehaviour",
                    "Expose messaging helpers on mediators.",
                    "using mvcExpress;\n\nnamespace YourGame\n    {\n    public partial class MediatorBehaviour\n    {\n        // TODO: wire to the module’s custom messaging implementation.\n    }\n}\n"
                ),
                (
                    "Example: extend MvcCommandBase",
                    "Expose messaging helpers on commands.",
                    "using mvcExpress;\n\nnamespace YourGame\n    {\n    public partial class MvcCommandBase\n    {\n        // TODO: wire to the module’s custom messaging implementation.\n    }\n}\n"
                )
            };

            return (BuildCopyAll(blocks), blocks.ToArray());
        }

        private static string BuildCopyAll(List<(string title, string description, string code)> blocks)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < blocks.Count; i++)
            {
                var block = blocks[i];
                sb.AppendLine(block.title);
                sb.AppendLine(new string('-', block.title.Length));
                if (!string.IsNullOrWhiteSpace(block.description))
                {
                    sb.AppendLine(block.description);
                }

                sb.AppendLine();
                sb.AppendLine(block.code.TrimEnd());

                if (i < blocks.Count - 1)
                {
                    sb.AppendLine();
                    sb.AppendLine();
                }
            }
            return sb.ToString();
        }
    }
}

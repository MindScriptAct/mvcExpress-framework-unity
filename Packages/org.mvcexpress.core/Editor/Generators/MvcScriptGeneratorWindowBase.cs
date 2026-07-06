using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.Generators
{
    /// <summary>
    /// Abstract base class for all mvcExpress code-generator EditorWindows.
    /// Inherit from this class to create a custom generator that integrates with the shared
    /// namespace, file-path, and documentation settings used by the built-in generators.
    /// </summary>
    /// <remarks>
    /// Override the abstract properties (<see cref="PrefPrefix"/>, <see cref="WindowTitle"/>,
    /// <see cref="WindowSize"/>, <see cref="DefaultFileName"/>, <see cref="DefaultClassName"/>)
    /// to configure the window identity and defaults. The shared settings panel (namespace,
    /// folder-level skipping, documentation toggle) is rendered automatically by the base class.
    /// </remarks>
    public abstract class MvcScriptGeneratorWindowBase : EditorWindow
    {
        protected enum GeneratorContextMode { Project, Hierarchy }

        protected abstract string PrefPrefix      { get; }
        protected abstract string WindowTitle     { get; }
        protected abstract Vector2 WindowSize     { get; }
        protected abstract string DefaultFileName { get; }
        protected abstract string DefaultClassName { get; }

        // Shared settings — loaded from MvcGeneratorSharedSettings so all windows agree.
        protected bool   useNamespace       = true;
        protected bool   useCustomNamespace = false;
        protected string customNamespace    = string.Empty;
        protected string defaultNamespace   = string.Empty;
        protected int    skipFolderLevels   = 0;
        protected bool   withDocumentation  = false;

        protected string className;
        protected string selectedFolderPath;
        protected GeneratorContextMode contextMode = GeneratorContextMode.Project;

        // ── Project-specific key helpers ──────────────────────────────────────

        private static string s_cachedProjectKey;

        private static string GetProjectKey()
        {
            if (s_cachedProjectKey != null) return s_cachedProjectKey;
            using var md5 = MD5.Create();
            var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(Application.dataPath));
            s_cachedProjectKey = BitConverter.ToString(hash).Replace("-", "").Substring(0, 8);
            return s_cachedProjectKey;
        }

        protected static string MakeProjectSpecificKey(string baseKey) =>
            string.IsNullOrEmpty(baseKey) ? baseKey : $"{baseKey}_{GetProjectKey()}";

        // Accessible to MvcGeneratorSharedSettings (same assembly, internal).
        internal static string MakeSharedKey(string baseKey) => MakeProjectSpecificKey(baseKey);

        // ── Lifecycle ─────────────────────────────────────────────────────────

        protected virtual void OnEnable()
        {
            if (_isGenerating)
            {
                // We survived a domain reload, which means compilation finished
                // and the scaffold utility has already run.
                _isGenerating = false;
                _showDone     = true;
                _doneAt       = EditorApplication.timeSinceStartup;
                return; // skip normal init; subclasses must check _showDone too
            }

            className = DefaultClassName;
            LoadPreferences();
            LoadFolderHistoryBase();
            selectedFolderPath = GetInitialFolderPath(contextMode);
            _panelLastTime = EditorApplication.timeSinceStartup;
        }

        // True immediately after a domain reload that followed a generation click.
        // Subclass OnEnable() methods should check this and return early if set.
        protected bool IsRestoredFromGeneration => _showDone;

        protected void InitializeContext(GeneratorContextMode mode)
        {
            contextMode = mode;
            selectedFolderPath = GetInitialFolderPath(contextMode);
        }

        // Hierarchy mode: most recently used folder for this actor type, or empty if none yet.
        // Project mode: derived from whatever's currently selected in the Project panel.
        private string GetInitialFolderPath(GeneratorContextMode mode)
        {
            if (mode == GeneratorContextMode.Hierarchy)
                return _folderHistory.Count > 0 ? _folderHistory[0] : string.Empty;
            return GetSelectedFolderPath();
        }

        // ── Shared settings load / save ───────────────────────────────────────

        protected void LoadPreferences()
        {
            withDocumentation  = MvcGeneratorSharedSettings.WithDocumentation;
            useNamespace       = MvcGeneratorSharedSettings.UseNamespace;
            useCustomNamespace = MvcGeneratorSharedSettings.UseCustomNamespace;
            customNamespace    = MvcGeneratorSharedSettings.CustomNamespace;
            defaultNamespace   = MvcGeneratorSharedSettings.NamespacePrefix;
            skipFolderLevels   = MvcGeneratorSharedSettings.SkipFolderLevels;
        }

        protected void SavePreferences()
        {
            MvcGeneratorSharedSettings.WithDocumentation  = withDocumentation;
            MvcGeneratorSharedSettings.UseNamespace       = useNamespace;
            MvcGeneratorSharedSettings.UseCustomNamespace = useCustomNamespace;
            MvcGeneratorSharedSettings.CustomNamespace    = customNamespace;
            MvcGeneratorSharedSettings.NamespacePrefix    = defaultNamespace;
            MvcGeneratorSharedSettings.SkipFolderLevels   = skipFolderLevels;
            if (useNamespace && useCustomNamespace && MvcGeneratorSharedSettings.IsValidNamespace(customNamespace))
                MvcGeneratorSharedSettings.PushNamespaceToHistory(customNamespace);
        }

        // ══════════════════════════════════════════════════════════════════════
        // PANEL INFRASTRUCTURE
        // Each generator OnGUI:
        //   InitBaseStyles();
        //   GUILayout.BeginArea(new Rect(0,0, GetMainWidth(), position.height));
        //   ... content ...
        //   GUILayout.EndArea();
        //   DrawSettingsPanelBase(GetSettingsLeft());
        //   DrawActivationBarBase();
        // ══════════════════════════════════════════════════════════════════════

        protected const float BarWidth           = 24f;
        protected const float SettingsPanelWidth = 300f;

        // ── Generation state — survives domain reload via [SerializeField] ───
        // When the user clicks Create the window stays open and shows "Compiling…".
        // After Unity reloads the domain (compilation done) OnEnable detects the
        // flag, switches to "Done" for a moment, then auto-closes.
        [SerializeField] private bool   _isGenerating     = false;
        [SerializeField] private string _pendingClassName = string.Empty;
        [SerializeField] private string _pendingGoName    = string.Empty;

        // Non-serialized: only lives within a single domain session.
        private bool   _showDone = false;
        private double _doneAt   = -1.0;
        private const double DoneDisplaySeconds = 2.5;

        protected float  _panelExpand    = 0f;
        protected bool   _settingsLocked = false;
        private   double _panelLastTime;
        private   Vector2 _settingsScroll;

        // Per-actor folder history key — override to share between Proxy+ProxyBehaviour.
        protected virtual string FolderActorKey => PrefPrefix;

        protected readonly List<string> _folderHistory = new List<string>();
        private const int MaxFolderHistory = 10;
        protected Rect _folderDropdownRect;
        protected Rect _nsDropdownRect;

        // ── Styles ────────────────────────────────────────────────────────────

        private bool _stylesInit;
        protected GUIStyle CodePreviewStyle;
        protected GUIStyle SettingsHeaderStyle;
        protected GUIStyle SettingsSectionStyle;
        protected GUIStyle BarLabelStyle;
        protected GUIStyle CloseBtnStyle;

        protected void InitBaseStyles()
        {
            if (_stylesInit) return;
            _stylesInit = true;

            CodePreviewStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize  = 11,
                fontStyle = FontStyle.Bold,
                padding   = new RectOffset(10, 10, 8, 8),
                wordWrap  = false,
                richText  = false,
            };
            SettingsHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                normal   = { textColor = new Color(0.88f, 0.88f, 0.96f) },
            };
            SettingsSectionStyle = new GUIStyle(EditorStyles.miniBoldLabel)
            {
                normal = { textColor = new Color(0.72f, 0.72f, 0.88f) },
            };
            BarLabelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize  = 9,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = new Color(0.70f, 0.70f, 0.82f) },
            };
            CloseBtnStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize  = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter,
                normal    = { textColor = Color.white },
                hover     = { textColor = Color.white },
                active    = { textColor = Color.white },
            };
        }

        // ── Animation (Update runs via Unity reflection on the concrete type) ──

        private void Update()
        {
            double now = EditorApplication.timeSinceStartup;
            float dt   = Mathf.Clamp((float)(now - _panelLastTime), 0f, 0.05f);
            _panelLastTime = now;

            // Panel animation — only needed when not in generating/done state.
            if (!_isGenerating && !_showDone)
            {
                float target = _settingsLocked ? SettingsPanelWidth : 0f;
                float prev   = _panelExpand;
                _panelExpand  = Mathf.MoveTowards(_panelExpand, target, 900f * dt);
                if (!Mathf.Approximately(_panelExpand, prev)) Repaint();
            }

            // Generating: repaint continuously to animate the dots.
            if (_isGenerating)
                Repaint();

            // Done: repaint for the countdown, then auto-close.
            if (_showDone)
            {
                Repaint();
                if (now - _doneAt >= DoneDisplaySeconds)
                    Close();
            }
        }

        protected float GetMainWidth()     => Mathf.Max(1f, position.width - BarWidth - _panelExpand);
        protected float GetSettingsLeft()  => position.width - BarWidth - _panelExpand;

        // ── Generation state helpers ──────────────────────────────────────────

        // Call instead of Close() after a successful Create.
        // Keeps the window open with a "Compiling…" message until the domain
        // reloads (compilation done), then shows "Done" for DoneDisplaySeconds.
        protected void BeginGeneration(string pendingClassName, string pendingGoName = null)
        {
            _isGenerating     = true;
            _pendingClassName = pendingClassName ?? string.Empty;
            _pendingGoName    = pendingGoName   ?? string.Empty;
            Repaint();
        }

        // Call at the very top of each generator's OnGUI().
        // Returns true if the generating/done overlay was drawn — caller should
        // return immediately in that case so normal controls are not rendered.
        protected bool DrawIfGenerating()
        {
            if (!_isGenerating && !_showDone) return false;

            InitBaseStyles();
            EditorGUILayout.Space(20f);

            if (_showDone)
            {
                var secondsLeft = (float)(DoneDisplaySeconds - (EditorApplication.timeSinceStartup - _doneAt));
                var line1 = string.IsNullOrEmpty(_pendingGoName)
                    ? $"{_pendingClassName} created successfully."
                    : $"{_pendingClassName}";
                var line2 = string.IsNullOrEmpty(_pendingGoName)
                    ? string.Empty
                    : $"Added to \"{_pendingGoName}\".";
                var msg = string.IsNullOrEmpty(line2) ? line1 : $"{line1}\n{line2}";

                EditorGUILayout.HelpBox(msg, MessageType.Info);
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField(
                    $"Closing in {Mathf.CeilToInt(secondsLeft)} s…",
                    EditorStyles.centeredGreyMiniLabel);
            }
            else if (!EditorApplication.isCompiling && EditorUtility.scriptCompilationFailed)
            {
                EditorGUILayout.HelpBox(
                    $"Compilation failed — can't finish creating {_pendingClassName}.\n\n" +
                    "Fix the errors shown in the Console. Unity will recompile automatically " +
                    "and this window will finish on its own once it succeeds.",
                    MessageType.Error);
                EditorGUILayout.Space(10f);
                if (GUILayout.Button("Cancel", GUILayout.Height(24f)))
                    CancelGeneration();
            }
            else
            {
                // Animate trailing dots: "Compiling." → ".." → "..." → ""
                int d = (int)(EditorApplication.timeSinceStartup * 2.5) % 4;
                EditorGUILayout.HelpBox(
                    $"Creating {_pendingClassName}{new string('.', d)}\n\n" +
                    "Waiting for Unity to finish compiling the script.\n" +
                    "The window will close automatically when done.",
                    MessageType.None);
            }

            return true;
        }

        // Call from the error state's Cancel button. Discards the "waiting for
        // compile" state and closes the window; the generated script file itself
        // (already written to disk) is left untouched.
        protected void CancelGeneration()
        {
            _isGenerating     = false;
            _pendingClassName = string.Empty;
            _pendingGoName    = string.Empty;
            Close();
        }

        // ── Activation bar ────────────────────────────────────────────────────

        protected void DrawActivationBarBase()
        {
            var barRect = new Rect(position.width - BarWidth, 0f, BarWidth, position.height);

            EditorGUI.DrawRect(barRect, _settingsLocked
                ? new Color(0.22f, 0.22f, 0.45f, 1f)
                : new Color(0.21f, 0.21f, 0.27f, 1f));
            EditorGUI.DrawRect(new Rect(barRect.x, 0f, 1f, position.height),
                new Color(0.5f, 0.5f, 0.62f, 0.7f));

            if (_settingsLocked)
            {
                var xRect = new Rect(barRect.x + 2f, 4f, BarWidth - 4f, BarWidth - 4f);
                var prev  = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.78f, 0.15f, 0.15f, 1f);
                if (GUI.Button(xRect, "✕", CloseBtnStyle)) { _settingsLocked = false; Repaint(); }
                GUI.backgroundColor = prev;
            }

            var saved = GUI.matrix;
            var pivot = barRect.center;
            GUIUtility.RotateAroundPivot(90f, pivot);
            GUI.Label(
                new Rect(pivot.x - barRect.height * 0.5f, pivot.y - barRect.width * 0.5f,
                         barRect.height, barRect.width),
                "SETTINGS", BarLabelStyle);
            GUI.matrix = saved;

            if (Event.current.type == EventType.MouseDown && barRect.Contains(Event.current.mousePosition))
            {
                _settingsLocked = !_settingsLocked;
                Event.current.Use();
                Repaint();
            }
        }

        // ── Settings panel ────────────────────────────────────────────────────

        protected void DrawSettingsPanelBase(float panelLeft)
        {
            if (_panelExpand < 0.5f) return;

            if (Event.current.type == EventType.Repaint)
            {
                EditorGUI.DrawRect(new Rect(panelLeft, 0f, _panelExpand, position.height),
                    new Color(0.17f, 0.17f, 0.21f, 1f));
                EditorGUI.DrawRect(new Rect(panelLeft, 0f, 1f, position.height),
                    new Color(0.44f, 0.44f, 0.56f, 1f));
            }

            if (_panelExpand > 10f)
            {
                GUILayout.BeginArea(new Rect(panelLeft + 10f, 8f,
                                             _panelExpand - 14f, position.height - 16f));
                using (new EditorGUI.DisabledScope(_panelExpand < SettingsPanelWidth * 0.5f))
                {
                    _settingsScroll = GUILayout.BeginScrollView(
                        _settingsScroll, false, false, GUIStyle.none, GUIStyle.none);
                    DrawSharedSettingsPanelContent();
                    DrawWindowSettingsItems();
                    GUILayout.EndScrollView();
                }
                GUILayout.EndArea();
            }
        }

        // Override to add window-specific settings items below the shared namespace/docs block.
        protected virtual void DrawWindowSettingsItems() { }

        private void DrawSharedSettingsPanelContent()
        {
            GUILayout.Label("Settings", SettingsHeaderStyle);
            GUILayout.Space(4f);

            DrawPanelToggle(ref withDocumentation, "Add Documentation",
                "Adds XML summary comments to the generated class.");
            GUILayout.Space(8f);

            GUILayout.Label("Namespace", SettingsSectionStyle);
            GUILayout.Space(2f);
            DrawPanelToggle(ref useNamespace, "Use Namespace",
                "Wraps the generated class in a namespace derived from the folder path.");

            if (useNamespace)
            {
                GUILayout.Space(4f);
                DrawPanelToggle(ref useCustomNamespace, "Override (full namespace)",
                    "Uses the Namespace field on the main screen as the complete, literal namespace.");

                if (!useCustomNamespace)
                {
                    GUILayout.Space(6f);
                    GUILayout.Label("Namespace Prefix", SettingsSectionStyle);
                    EditorGUILayout.HelpBox(
                        "Prepended before the folder-derived namespace.\n" +
                        "Example: prefix 'MyGame' + folder 'Assets/Core' → 'MyGame.Assets.Core'.",
                        MessageType.None);
                    defaultNamespace = GUILayout.TextField(defaultNamespace ?? string.Empty);

                    GUILayout.Space(6f);
                    GUILayout.Label("Ignore First N Folders", SettingsSectionStyle);
                    EditorGUILayout.HelpBox(
                        "Strips the first N folder segments.\n" +
                        "Example: 'Assets/Scripts/Game' with N=2 → 'Game'.",
                        MessageType.None);
                    using (new GUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("-", GUILayout.Width(30)) && skipFolderLevels > 0)
                        { skipFolderLevels--; Repaint(); }
                        GUILayout.Label(skipFolderLevels.ToString(),
                            new GUIStyle(EditorStyles.centeredGreyMiniLabel)
                            {
                                fontSize  = 13,
                                alignment = TextAnchor.MiddleCenter,
                                normal    = { textColor = Color.white },
                            }, GUILayout.Width(36));
                        if (GUILayout.Button("+", GUILayout.Width(30)))
                        { skipFolderLevels++; Repaint(); }
                    }
                }
            }
            GUILayout.Space(8f);
        }

        protected static void DrawPanelToggle(ref bool value, string label, string info)
        {
            value = GUILayout.Toggle(value, label);
            EditorGUILayout.HelpBox(info, MessageType.None);
        }

        // ── Create-as-child toggle (main panel) ───────────────────────────────

        // Shared by the Service/Proxy/Mediator behaviour generators so the label and
        // info line stay identical across all three windows.
        protected static void DrawCreateAsChildToggle(ref bool createAsChild)
        {
            createAsChild = EditorGUILayout.ToggleLeft("Create as child", createAsChild);

            EditorGUILayout.HelpBox(
                createAsChild
                    ? "Child GameObject will be created, and component added."
                    : "Component will be added to current GameObject.",
                MessageType.None);
        }

        // ── Namespace field (main screen) ─────────────────────────────────────

        protected void DrawNamespaceFieldBase()
        {
            if (!useNamespace) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Namespace", GUILayout.Width(80));
                if (useCustomNamespace)
                {
                    customNamespace = EditorGUILayout.TextField(customNamespace ?? string.Empty);
                    using (new EditorGUI.DisabledScope(MvcGeneratorSharedSettings.NamespaceHistory.Count == 0))
                    {
                        if (GUILayout.Button("▼", GUILayout.Width(22)))
                            PopupWindow.Show(_nsDropdownRect, new StringHistoryPopup(
                                MvcGeneratorSharedSettings.NamespaceHistory, ns =>
                                { customNamespace = ns; Repaint(); }));
                        if (Event.current.type == EventType.Repaint)
                            _nsDropdownRect = GUILayoutUtility.GetLastRect();
                    }
                }
                else
                {
                    var path    = Path.Combine(
                        string.IsNullOrWhiteSpace(selectedFolderPath) ? "Assets" : selectedFolderPath,
                        (className ?? "X") + ".cs").Replace("\\", "/");
                    var derived = ResolveNamespaceForPath(path);
                    using (new EditorGUI.DisabledScope(true))
                        EditorGUILayout.TextField(string.IsNullOrEmpty(derived) ? "(derived from folder)" : derived);
                }
            }

            if (useCustomNamespace && !MvcGeneratorSharedSettings.IsValidNamespace(customNamespace))
                EditorGUILayout.HelpBox(
                    "Invalid namespace — use dot-separated identifiers (e.g. MyGame.Core).",
                    MessageType.Error);
        }

        // ── Folder field (main screen) ────────────────────────────────────────

        protected void DrawFolderFieldBase()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Folder", GUILayout.Width(60));
                EditorGUI.BeginChangeCheck();
                selectedFolderPath = EditorGUILayout.TextField(selectedFolderPath);
                if (EditorGUI.EndChangeCheck())
                    selectedFolderPath = (selectedFolderPath ?? string.Empty).Replace("\\", "/").Trim();

                using (new EditorGUI.DisabledScope(_folderHistory.Count == 0))
                {
                    if (GUILayout.Button("▼", GUILayout.Width(22)))
                        PopupWindow.Show(_folderDropdownRect, new FlatListPopup(_folderHistory, path =>
                        { selectedFolderPath = path; PushFolderToHistory(path); Repaint(); }));
                    if (Event.current.type == EventType.Repaint)
                        _folderDropdownRect = GUILayoutUtility.GetLastRect();
                }
                if (GUILayout.Button("Pick", GUILayout.Width(46)))
                    PickFolderInProject(ref selectedFolderPath);
            }

            if (!string.IsNullOrWhiteSpace(selectedFolderPath))
            {
                if (selectedFolderPath.IndexOfAny(Path.GetInvalidPathChars()) >= 0)
                    EditorGUILayout.HelpBox("Path contains illegal characters.", MessageType.Error);
                else if (selectedFolderPath.StartsWith("Assets", StringComparison.Ordinal)
                         && !Directory.Exists(Path.GetFullPath(selectedFolderPath)))
                    EditorGUILayout.HelpBox("Folder does not exist — will be created.", MessageType.Warning);
            }
        }

        // ── Preview box (main screen) ─────────────────────────────────────────

        protected void DrawPreviewBoxBase(string text)
        {
            GUILayout.Label(text, CodePreviewStyle,
                GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
        }

        // ── Folder history ────────────────────────────────────────────────────

        private string FolderHistoryEditorKey =>
            MakeSharedKey($"MvcGenerators_{FolderActorKey}_FolderHistory");

        protected void LoadFolderHistoryBase()
        {
            _folderHistory.Clear();
            var raw = EditorPrefs.GetString(FolderHistoryEditorKey, string.Empty);
            if (string.IsNullOrEmpty(raw)) return;
            foreach (var e in raw.Split('|'))
            {
                var t = e.Trim();
                if (!string.IsNullOrEmpty(t)) _folderHistory.Add(t);
            }
        }

        protected void PushFolderToHistory(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            _folderHistory.Remove(path);
            _folderHistory.Insert(0, path);
            if (_folderHistory.Count > MaxFolderHistory) _folderHistory.RemoveAt(_folderHistory.Count - 1);
            EditorPrefs.SetString(FolderHistoryEditorKey, string.Join("|", _folderHistory));
        }

        // ── Flat-list popup (shared by folder + namespace dropdowns) ──────────

        protected sealed class FlatListPopup : PopupWindowContent
        {
            private readonly List<string> _items;
            private readonly Action<string> _onSelect;
            private const float ItemH = 22f, Width = 380f;

            internal FlatListPopup(List<string> items, Action<string> onSelect)
            { _items = items; _onSelect = onSelect; }

            public override Vector2 GetWindowSize() => new Vector2(Width, _items.Count * ItemH + 7f);
            public override void OnGUI(Rect rect)
            {
                string picked = null;
                foreach (var item in _items)
                    if (GUILayout.Button(item, EditorStyles.label, GUILayout.Height(ItemH)))
                    { picked = item; break; }
                if (picked != null) { _onSelect(picked); editorWindow.Close(); }
            }
        }

        protected sealed class StringHistoryPopup : PopupWindowContent
        {
            private readonly List<string> _items;
            private readonly Action<string> _onSelect;
            private const float ItemH = 22f, Width = 300f;

            internal StringHistoryPopup(List<string> items, Action<string> onSelect)
            { _items = items; _onSelect = onSelect; }

            public override Vector2 GetWindowSize() => new Vector2(Width, _items.Count * ItemH + 7f);
            public override void OnGUI(Rect rect)
            {
                string picked = null;
                foreach (var item in _items)
                    if (GUILayout.Button(item, EditorStyles.label, GUILayout.Height(ItemH)))
                    { picked = item; break; }
                if (picked != null) { _onSelect(picked); editorWindow.Close(); }
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        // LEGACY HELPERS (kept for backward compatibility — not used by new OnGUIs)
        // ══════════════════════════════════════════════════════════════════════

        protected void DrawNamespaceOptions()
        {
            EditorGUILayout.LabelField("Namespace", EditorStyles.boldLabel);
            useNamespace = EditorGUILayout.ToggleLeft("Use namespace", useNamespace);
            using (new EditorGUI.DisabledScope(!useNamespace))
            {
                useCustomNamespace = EditorGUILayout.ToggleLeft("Use custom namespace (full)", useCustomNamespace);
                using (new EditorGUI.DisabledScope(!useCustomNamespace))
                    customNamespace = EditorGUILayout.TextField("Custom namespace", customNamespace);
                using (new EditorGUI.DisabledScope(useCustomNamespace))
                {
                    defaultNamespace = EditorGUILayout.TextField("Default root", defaultNamespace);
                    skipFolderLevels = EditorGUILayout.IntField("Skip folder levels", Mathf.Max(0, skipFolderLevels));
                }
            }
            EditorGUILayout.Space(5);
        }

        protected void DrawCommonHeader(string description)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(WindowTitle, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(description, MessageType.Info);
            EditorGUILayout.Space();
        }

        protected void DrawClassAndFolder()
        {
            EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Class Name", GUILayout.Width(80));
            className = EditorGUILayout.TextField(className);
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Folder", GUILayout.Width(80));
            EditorGUILayout.TextField(selectedFolderPath);
            if (GUILayout.Button("Pick", GUILayout.Width(60)))
                selectedFolderPath = GetSelectedFolderPath();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(5);
        }

        protected string ResolveNamespaceForPath(string assetPath)
        {
            if (!useNamespace) return null;
            if (useCustomNamespace) return customNamespace;
            assetPath = assetPath.Replace("\\", "/");
            var dir = Path.GetDirectoryName(assetPath)?.Replace("\\", "/");
            if (string.IsNullOrEmpty(dir)) return defaultNamespace;
            var parts = dir.Split('/');
            int start = Mathf.Clamp(skipFolderLevels, 0, parts.Length);
            if (start >= parts.Length) return defaultNamespace;
            var tail = string.Join(".", parts, start, parts.Length - start);
            if (string.IsNullOrWhiteSpace(defaultNamespace)) return tail;
            if (string.IsNullOrWhiteSpace(tail)) return defaultNamespace;
            return defaultNamespace + "." + tail;
        }

        protected string GetSelectedFolderPath()
        {
            var path = "Assets";
            var obj  = Selection.activeObject;
            if (obj != null)
            {
                var sel = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(sel))
                    path = Directory.Exists(sel) ? sel : Path.GetDirectoryName(sel).Replace("\\", "/");
            }
            return path;
        }

        protected void CreateScriptFromTemplate(Func<string, string> templateFunc)
        {
            var path = EditorUtility.SaveFilePanelInProject("Create Script", DefaultFileName, "cs",
                "Choose location for generated script.", selectedFolderPath);
            if (string.IsNullOrEmpty(path)) return;
            if (string.IsNullOrWhiteSpace(className))
                className = Path.GetFileNameWithoutExtension(path);
            var ns       = ResolveNamespaceForPath(path);
            var contents = ApplyNamespace(templateFunc(className), ns);
            WriteAndRefresh(path, contents);
        }

        protected void CreateScriptInSelectedFolder(Func<string, string> templateFunc)
        {
            if (!ValidateAndGetPath(out var path)) return;
            var ns       = ResolveNamespaceForPath(path);
            var contents = ApplyNamespace(templateFunc(className), ns);
            WriteAndRefresh(path, contents);
        }

        protected string CreateScriptInSelectedFolderReturningPath(Func<string, string> templateFunc)
        {
            if (!ValidateAndGetPath(out var path)) return null;
            Directory.CreateDirectory(Path.GetFullPath(Path.GetDirectoryName(path)));
            var ns       = ResolveNamespaceForPath(path);
            var contents = ApplyNamespace(templateFunc(className), ns);
            WriteAndRefresh(path, contents);
            return path;
        }

        private bool ValidateAndGetPath(out string path)
        {
            path = null;
            if (string.IsNullOrWhiteSpace(className))
            { EditorUtility.DisplayDialog("Invalid Class Name", "Please enter a class name.", "OK"); return false; }
            var folder = (string.IsNullOrWhiteSpace(selectedFolderPath) ? "Assets" : selectedFolderPath)
                .Replace("\\", "/");
            if (!folder.StartsWith("Assets", StringComparison.Ordinal))
            { EditorUtility.DisplayDialog("Invalid Folder", "Selected folder must be under 'Assets'.", "OK"); return false; }
            path = $"{folder.TrimEnd('/')}/{className}.cs";
            if (File.Exists(path) &&
                !EditorUtility.DisplayDialog("File Exists", $"File already exists:\n{path}\n\nOverwrite?", "Overwrite", "Cancel"))
                return false;
            return true;
        }

        private static string ApplyNamespace(string contents, string ns)
        {
            if (!string.IsNullOrEmpty(ns))
            {
                contents = contents
                    .Replace("${NAMESPACE}", ns)
                    .Replace("${NAMESPACE_BEGIN}", $"namespace {ns}\n{{")
                    .Replace("${NAMESPACE_END}", "}");
            }
            else
            {
                contents = RemoveNamespaceBlock(contents).Replace("${NAMESPACE}", string.Empty);
            }
            return contents.Replace("\r\n", "\n").Replace("\r", "\n");
        }

        private static void WriteAndRefresh(string path, string contents)
        {
            File.WriteAllText(path, contents);
            AssetDatabase.Refresh();
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset != null) { Selection.activeObject = asset; EditorGUIUtility.PingObject(asset); }
        }

        private static string RemoveNamespaceBlock(string code)
        {
            const string begin = "${NAMESPACE_BEGIN}", end = "${NAMESPACE_END}";
            if (code.Contains(begin) && code.Contains(end))
            {
                code = code.Replace(begin, string.Empty).Replace(end, string.Empty);
                return FixIndentation(code);
            }
            const string token = "namespace ${NAMESPACE}";
            int idx = code.IndexOf(token, StringComparison.Ordinal);
            if (idx < 0) return FixIndentation(code.Replace("${NAMESPACE}", string.Empty));
            int braceOpen = code.IndexOf('{', idx);
            if (braceOpen < 0)
                return FixIndentation(code.Replace(token, string.Empty).Replace("${NAMESPACE}", string.Empty));
            code = code.Remove(idx, braceOpen - idx + 1);
            int last = code.LastIndexOf('}');
            if (last >= 0) code = code.Remove(last, 1);
            return FixIndentation(code);
        }

        private static string FixIndentation(string code)
        {
            using var reader = new StringReader(code);
            var sb = new StringBuilder(code.Length);
            string line;
            while ((line = reader.ReadLine()) != null)
                sb.AppendLine(line.StartsWith("    ", StringComparison.Ordinal) ? line.Substring(4) : line);
            return sb.ToString();
        }

        protected void EnsureWindowSizing()
        {
            minSize = WindowSize;
            maxSize = new Vector2(10000, 10000);
        }

        protected bool PickFolderInProject(ref string folderPath)
        {
            var abs = EditorUtility.OpenFolderPanel("Select Folder", Application.dataPath, "");
            if (string.IsNullOrEmpty(abs)) return false;
            abs = abs.Replace("\\", "/");
            var dataAbs = Application.dataPath.Replace("\\", "/");
            if (!abs.StartsWith(dataAbs, StringComparison.Ordinal))
            {
                EditorUtility.DisplayDialog("Invalid Folder", "Folder must be inside this project's Assets folder.", "OK");
                return false;
            }
            folderPath = "Assets" + abs.Substring(dataAbs.Length);
            return true;
        }
    }
}

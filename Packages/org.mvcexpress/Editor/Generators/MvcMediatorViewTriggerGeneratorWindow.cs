using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.Generators
{
    /// <summary>
    /// Four-step wizard: View Elements → Select Triggers → Messages → Commands.
    /// </summary>
    public sealed class MvcMediatorViewTriggerGeneratorWindow : EditorWindow
    {
        // ── Inner types ───────────────────────────────────────────────────────

        private enum WizardStep
        {
            ViewElements    = 0,
            SelectTriggers  = 1,
            ConfigureMessages = 2,
            ConfigureCommands = 3
        }

        private class ViewElementInfo
        {
            public Component Component;
            public int HierarchyOrder;      // DFS index for sorting
            public string GoName;           // display only (grayed)
            public string ComponentTypeName; // exact C# type name for [SerializeField]
            public string FieldName;        // user-editable, starts with _
            public bool IsSelected;
            public bool AlreadyInMediator;  // detected from script
        }

        // ── Target ───────────────────────────────────────────────────────────

        private GameObject _targetGameObject;
        private MediatorBehaviour _targetMediator;
        private string _mediatorScriptPath;
        private string _mediatorScriptCode;

        // ── Step 0: View Elements ─────────────────────────────────────────────

        private List<ViewElementInfo> _viewElements = new List<ViewElementInfo>();
        private Dictionary<Component, ViewElementInfo> _elementByComponent = new Dictionary<Component, ViewElementInfo>();

        // ── Step 1: Triggers ──────────────────────────────────────────────────

        private List<ViewTriggerInfo> _allTriggers = new List<ViewTriggerInfo>();

        // ── Step 2: Messages ──────────────────────────────────────────────────

        private bool _generateMessages = true;
        private string _messagesFolder = "Assets/Scripts/Messages";
        private string _messagesFileName = "Messages.cs";

        // ── Step 3: Commands ──────────────────────────────────────────────────

        private bool _generateCommands = false;
        private string _commandsFolder = "Assets/Scripts/Commands";
        private MvcModule _targetModule;
        private bool _autoBindCommands = true;

        // ── Folder history ────────────────────────────────────────────────────

        private const string MessagesFolderHistoryKey = "MvcMediatorGen_MessagesFolderHistory";
        private const string CommandsFolderHistoryKey = "MvcMediatorGen_CommandsFolderHistory";
        private const int MaxFolderHistory = 10;
        private List<string> _messagesFolderHistory = new List<string>();
        private List<string> _commandsFolderHistory = new List<string>();
        private Rect _msgFolderBtnRect;
        private Rect _cmdFolderBtnRect;

        // ── UI state ──────────────────────────────────────────────────────────

        private WizardStep _currentStep = WizardStep.ViewElements;
        private Vector2 _mainScroll;
        private const float NavBarHeight = 50f;

        // ── Entry point ───────────────────────────────────────────────────────

        private void OnEnable()
        {
            _messagesFolderHistory = LoadFolderHistory(MessagesFolderHistoryKey);
            _commandsFolderHistory = LoadFolderHistory(CommandsFolderHistoryKey);
        }

        internal static void ShowWindow(GameObject target)
        {
            var w = GetWindow<MvcMediatorViewTriggerGeneratorWindow>(true, "Mediator Handler Generator", true);
            w.minSize = new Vector2(680, 560);
            w.maxSize = new Vector2(10000, 10000);
            w._targetGameObject = target;
            w.Initialize();
            w.Show();
        }

        private void Initialize()
        {
            _viewElements.Clear();
            _elementByComponent.Clear();
            _allTriggers.Clear();
            _mediatorScriptCode = string.Empty;

            if (_targetGameObject == null) return;

            _targetMediator = _targetGameObject.GetComponent<MediatorBehaviour>();
            if (_targetMediator != null)
            {
                _mediatorScriptPath = FindScriptPath(_targetMediator.GetType());
                if (!string.IsNullOrEmpty(_mediatorScriptPath))
                    _mediatorScriptCode = File.ReadAllText(_mediatorScriptPath);
            }

            _allTriggers = ViewTriggerAnalyzer.AnalyzeGameObject(_targetGameObject);
            _messagesFileName = $"{_targetGameObject.name}Messages.cs";

            BuildViewElements();
            RederiveTriggerNames();
        }

        // ── View element construction ─────────────────────────────────────────

        private void BuildViewElements()
        {
            // Collect unique components in DFS hierarchy order.
            int order = 0;
            var orderMap = new Dictionary<Component, int>();
            CollectHierarchyOrder(_targetGameObject.transform, ref order, orderMap);

            // One ViewElementInfo per unique Component reference.
            var seen = new HashSet<Component>();
            var byType = new Dictionary<string, int>(StringComparer.Ordinal); // base name → count

            foreach (var trigger in _allTriggers)
            {
                if (!seen.Add(trigger.Component)) continue;

                var typeName = trigger.Component.GetType().Name;
                var baseName = ComponentTypeToFieldBase(typeName);
                byType.TryGetValue(baseName, out int count);
                byType[baseName] = count + 1;

                var fieldName = count == 0 ? $"_{baseName}" : $"_{baseName}{count + 1}";

                orderMap.TryGetValue(trigger.Component, out int hierarchyOrder);

                var element = new ViewElementInfo
                {
                    Component = trigger.Component,
                    HierarchyOrder = hierarchyOrder,
                    GoName = trigger.Component.gameObject.name,
                    ComponentTypeName = typeName,
                    FieldName = fieldName,
                    IsSelected = true,
                };

                // Detect if a field of this component type already exists in the mediator.
                if (!string.IsNullOrEmpty(_mediatorScriptCode))
                {
                    var searchPattern = $"private {typeName} ";
                    int idx = _mediatorScriptCode.IndexOf(searchPattern, StringComparison.Ordinal);
                    if (idx >= 0)
                    {
                        element.AlreadyInMediator = true;
                        element.IsSelected = false; // already wired up

                        // Try to extract the actual field name.
                        int nameStart = idx + searchPattern.Length;
                        int nameEnd = nameStart;
                        while (nameEnd < _mediatorScriptCode.Length &&
                               (char.IsLetterOrDigit(_mediatorScriptCode[nameEnd]) || _mediatorScriptCode[nameEnd] == '_'))
                            nameEnd++;
                        var extracted = _mediatorScriptCode.Substring(nameStart, nameEnd - nameStart).Trim();
                        if (!string.IsNullOrEmpty(extracted))
                            element.FieldName = extracted;
                    }
                }

                _viewElements.Add(element);
                _elementByComponent[trigger.Component] = element;
            }

            // Sort by hierarchy order.
            _viewElements.Sort((a, b) => a.HierarchyOrder.CompareTo(b.HierarchyOrder));
        }

        private static void CollectHierarchyOrder(Transform t, ref int order, Dictionary<Component, int> map)
        {
            foreach (var c in t.GetComponents<Component>())
            {
                if (c != null && !(c is Transform))
                    map[c] = order++;
            }
            for (int i = 0; i < t.childCount; i++)
                CollectHierarchyOrder(t.GetChild(i), ref order, map);
        }

        // Maps a component type name to a simple camelCase base for field naming.
        private static string ComponentTypeToFieldBase(string typeName)
        {
            var name = typeName;
            if (name.StartsWith("TMP_", StringComparison.Ordinal)) name = name.Substring(4);
            if (name.EndsWith("UGUI", StringComparison.Ordinal))   name = name.Substring(0, name.Length - 4);
            if (name.Length == 0) return "component";
            return char.ToLowerInvariant(name[0]) + name.Substring(1);
        }

        // After ViewElementInfo field names are known, rederive trigger suggested names from them.
        // Handler names keep the component context (OnPlayButtonClicked).
        // Message/command names are action-only (ClickedMessage) — simpler and user-renameable.
        // Duplicates get a number suffix: Clicked2Message, Clicked3Message, etc.
        private void RederiveTriggerNames()
        {
            var usedMsgNames = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var trigger in _allTriggers)
            {
                if (!_elementByComponent.TryGetValue(trigger.Component, out var el)) continue;
                var friendly = FieldToFriendly(el.FieldName);
                var action   = ViewTriggerInfo.GetEventAction(trigger.EventName);

                trigger.SuggestedHandlerName = $"On{friendly}{action}";

                // Simple names: action only, numbered on collision.
                var baseMsg = $"{action}Message";
                usedMsgNames.TryGetValue(baseMsg, out int count);
                usedMsgNames[baseMsg] = count + 1;

                trigger.SuggestedMessageName = count == 0 ? baseMsg : $"{action}{count + 1}Message";
                trigger.SuggestedCommandName = count == 0 ? $"{action}Command" : $"{action}{count + 1}Command";
            }
        }

        // "_playButton" → "PlayButton"
        private static string FieldToFriendly(string fieldName)
        {
            var s = fieldName.TrimStart('_');
            if (s.Length == 0) return "Field";
            return char.ToUpperInvariant(s[0]) + s.Substring(1);
        }

        // ── OnGUI ─────────────────────────────────────────────────────────────

        private void OnGUI()
        {
            GUILayout.BeginArea(new Rect(0, 0, position.width, position.height - NavBarHeight));

            DrawHeader();
            EditorGUILayout.Space(6);
            DrawStepIndicator();
            EditorGUILayout.Space(6);

            _mainScroll = EditorGUILayout.BeginScrollView(_mainScroll, GUILayout.ExpandHeight(true));
            switch (_currentStep)
            {
                case WizardStep.ViewElements:      DrawViewElementsStep();     break;
                case WizardStep.SelectTriggers:    DrawSelectTriggersStep();   break;
                case WizardStep.ConfigureMessages: DrawConfigureMessagesStep(); break;
                case WizardStep.ConfigureCommands: DrawConfigureCommandsStep(); break;
            }
            EditorGUILayout.EndScrollView();

            GUILayout.EndArea();

            EditorGUI.DrawRect(new Rect(0, position.height - NavBarHeight - 1, position.width, 1),
                new Color(0.15f, 0.15f, 0.15f, 1f));

            GUILayout.BeginArea(new Rect(8, position.height - NavBarHeight + 8, position.width - 16, NavBarHeight - 8));
            DrawNavigationButtons();
            GUILayout.EndArea();
        }

        private void DrawHeader()
        {
            EditorGUILayout.LabelField("Mediator Handler Generator", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Target", _targetGameObject, typeof(GameObject), true);
                EditorGUILayout.ObjectField("Mediator", _targetMediator, typeof(MediatorBehaviour), true);
            }
            if (_targetMediator == null)
                EditorGUILayout.HelpBox("No MediatorBehaviour on target GameObject.", MessageType.Warning);
            else if (string.IsNullOrEmpty(_mediatorScriptPath))
                EditorGUILayout.HelpBox("Mediator script not found. Handlers cannot be generated.", MessageType.Warning);
        }

        private void DrawStepIndicator()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                DrawStepButton("0. View Elements", WizardStep.ViewElements);
                DrawStepButton("1. Triggers",      WizardStep.SelectTriggers);
                DrawStepButton("2. Messages",       WizardStep.ConfigureMessages);
                DrawStepButton("3. Commands",       WizardStep.ConfigureCommands);
            }
        }

        private void DrawStepButton(string label, WizardStep step)
        {
            var isActive = _currentStep == step;
            if (isActive) GUI.backgroundColor = Color.cyan;
            if (GUILayout.Button(label, isActive ? EditorStyles.toolbarButton : EditorStyles.label, GUILayout.Height(24)))
                _currentStep = step;
            if (isActive) GUI.backgroundColor = Color.white;
        }

        // ── Step 0: View Elements ─────────────────────────────────────────────

        private void DrawViewElementsStep()
        {
            EditorGUILayout.LabelField("Step 0: View Elements", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Select UI components to wire up. Rename the field if needed. Already-added fields are shown grayed.",
                EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            if (_viewElements.Count == 0)
            {
                EditorGUILayout.HelpBox("No UI components found on this GameObject or its children.", MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select All"))
                    foreach (var e in _viewElements) e.IsSelected = true;
                if (GUILayout.Button("Select None"))
                    foreach (var e in _viewElements) e.IsSelected = false;
                if (GUILayout.Button("Select New"))
                    foreach (var e in _viewElements) e.IsSelected = !e.AlreadyInMediator;
                GUILayout.FlexibleSpace();
                int sel = _viewElements.Count(e => e.IsSelected);
                EditorGUILayout.LabelField($"Selected: {sel} / {_viewElements.Count}", GUILayout.Width(110));
            }

            EditorGUILayout.Space(4);

            foreach (var el in _viewElements)
            {
                using (new EditorGUI.DisabledScope(el.AlreadyInMediator))
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    el.IsSelected = EditorGUILayout.Toggle(el.IsSelected, GUILayout.Width(18));

                    // Component type name (bold)
                    EditorGUILayout.LabelField(el.ComponentTypeName, EditorStyles.boldLabel, GUILayout.Width(160));

                    // Editable field name
                    EditorGUI.BeginChangeCheck();
                    var newName = EditorGUILayout.TextField(el.FieldName, GUILayout.Width(160));
                    if (EditorGUI.EndChangeCheck() && IsValidFieldName(newName))
                    {
                        el.FieldName = newName;
                        RederiveTriggerNames();
                    }

                    GUILayout.FlexibleSpace();

                    // GO name (grayed, right-aligned)
                    var prevColor = GUI.color;
                    GUI.color = Color.gray;
                    EditorGUILayout.LabelField(el.GoName, EditorStyles.miniLabel, GUILayout.Width(130));
                    GUI.color = prevColor;

                    // "Already added" badge
                    if (el.AlreadyInMediator)
                    {
                        var prevBg = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(0.4f, 0.7f, 0.4f, 1f);
                        GUILayout.Label("  in script  ", EditorStyles.miniButton, GUILayout.Width(72));
                        GUI.backgroundColor = prevBg;
                    }
                }
            }
        }

        // ── Step 1: Triggers ──────────────────────────────────────────────────

        private void DrawSelectTriggersStep()
        {
            EditorGUILayout.LabelField("Step 1: Select Event Triggers", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Only triggers for selected view elements are shown.",
                EditorStyles.miniLabel);
            EditorGUILayout.Space(4);

            // Only triggers whose component is in a selected view element.
            var visible = _allTriggers
                .Where(t => _elementByComponent.TryGetValue(t.Component, out var e) && e.IsSelected)
                .ToList();

            if (visible.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    _viewElements.Count == 0
                        ? "No UI event triggers found."
                        : "No view elements are selected. Go back to Step 0 to select components.",
                    MessageType.Info);
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Select All"))
                    foreach (var t in visible) t.IsSelected = true;
                if (GUILayout.Button("Select None"))
                    foreach (var t in visible) t.IsSelected = false;
                GUILayout.FlexibleSpace();
                int sel = visible.Count(t => t.IsSelected);
                EditorGUILayout.LabelField($"Selected: {sel} / {visible.Count}", GUILayout.Width(110));
            }

            EditorGUILayout.Space(4);

            foreach (var trigger in visible)
            {
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    trigger.IsSelected = EditorGUILayout.Toggle(trigger.IsSelected, GUILayout.Width(18));
                    EditorGUILayout.LabelField(
                        trigger.DisplayName + trigger.GetParameterTypesString(),
                        GUILayout.Width(220));
                    EditorGUILayout.LabelField("->", GUILayout.Width(18));
                    EditorGUILayout.LabelField(trigger.SuggestedHandlerName, EditorStyles.miniLabel, GUILayout.Width(200));
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.ObjectField(trigger.Component, trigger.Component.GetType(), true, GUILayout.Width(150));
                }
            }
        }

        // ── Step 2: Messages ──────────────────────────────────────────────────

        private void DrawConfigureMessagesStep()
        {
            EditorGUILayout.LabelField("Step 2: Configure Messages", EditorStyles.boldLabel);
            _generateMessages = EditorGUILayout.ToggleLeft("Generate Messages", _generateMessages);

            if (!_generateMessages)
            {
                EditorGUILayout.HelpBox("Messages will not be generated.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Messages File", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Folder", GUILayout.Width(80));
                _messagesFolder = EditorGUILayout.TextField(_messagesFolder);
                using (new EditorGUI.DisabledScope(_messagesFolderHistory.Count == 0))
                {
                    if (GUILayout.Button("▼", GUILayout.Width(22)))
                        ShowFolderHistoryMenu(_messagesFolderHistory, p => { _messagesFolder = p; Repaint(); });
                    if (Event.current.type == EventType.Repaint)
                        _msgFolderBtnRect = GUILayoutUtility.GetLastRect();
                }
                if (GUILayout.Button("Browse", GUILayout.Width(60)))
                {
                    var sel = EditorUtility.OpenFolderPanel("Select Messages Folder", "Assets", "");
                    if (!string.IsNullOrEmpty(sel)) _messagesFolder = MakeRelativeToProject(sel);
                }
            }

            _messagesFileName = EditorGUILayout.TextField("File Name", _messagesFileName);
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Messages  (edit names; pure type declarations — no fields or ctors):", EditorStyles.boldLabel);

            var selectedTriggers = SelectedTriggers();
            if (selectedTriggers.Count == 0)
            {
                EditorGUILayout.HelpBox("No triggers selected.", MessageType.Info);
                return;
            }

            foreach (var trigger in selectedTriggers)
            {
                var iface = trigger.ParameterTypes.Length == 0
                    ? "IMessage"
                    : $"IMessage<{string.Join(", ", trigger.ParameterTypes.Select(GetFriendlyTypeName))}>";

                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("struct", EditorStyles.miniLabel, GUILayout.Width(36));
                    EditorGUI.BeginChangeCheck();
                    trigger.SuggestedMessageName = EditorGUILayout.TextField(trigger.SuggestedMessageName);
                    if (EditorGUI.EndChangeCheck() &&
                        trigger.SuggestedMessageName.EndsWith("Message", StringComparison.Ordinal))
                    {
                        // Keep command name in sync when message name is edited.
                        trigger.SuggestedCommandName =
                            trigger.SuggestedMessageName.Substring(0, trigger.SuggestedMessageName.Length - 7) + "Command";
                    }
                    EditorGUILayout.LabelField($": {iface}", EditorStyles.miniLabel, GUILayout.Width(180));
                }
            }
        }

        // ── Step 3: Commands ──────────────────────────────────────────────────

        private void DrawConfigureCommandsStep()
        {
            EditorGUILayout.LabelField("Step 3: Configure Commands", EditorStyles.boldLabel);
            _generateCommands = EditorGUILayout.ToggleLeft("Generate Commands", _generateCommands);

            if (!_generateCommands)
            {
                EditorGUILayout.HelpBox("Commands will not be generated.", MessageType.Info);
                return;
            }

            EditorGUILayout.Space(4);
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Folder", GUILayout.Width(80));
                _commandsFolder = EditorGUILayout.TextField(_commandsFolder);
                using (new EditorGUI.DisabledScope(_commandsFolderHistory.Count == 0))
                {
                    if (GUILayout.Button("▼", GUILayout.Width(22)))
                        ShowFolderHistoryMenu(_commandsFolderHistory, p => { _commandsFolder = p; Repaint(); });
                    if (Event.current.type == EventType.Repaint)
                        _cmdFolderBtnRect = GUILayoutUtility.GetLastRect();
                }
                if (GUILayout.Button("Browse", GUILayout.Width(60)))
                {
                    var sel = EditorUtility.OpenFolderPanel("Select Commands Folder", "Assets", "");
                    if (!string.IsNullOrEmpty(sel)) _commandsFolder = MakeRelativeToProject(sel);
                }
            }

            EditorGUILayout.Space(4);
            _targetModule = EditorGUILayout.ObjectField("Target Module", _targetModule, typeof(MvcModule), true) as MvcModule;

            if (_targetModule == null)
                EditorGUILayout.HelpBox("Select a module to enable auto-bind.", MessageType.Info);

            using (new EditorGUI.DisabledScope(_targetModule == null))
                _autoBindCommands = EditorGUILayout.ToggleLeft("Auto-bind in module via Commander.Bind", _autoBindCommands);

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Commands  (edit names; duplicates shown as errors):", EditorStyles.boldLabel);

            var selectedTriggers = SelectedTriggers();
            if (selectedTriggers.Count == 0)
            {
                EditorGUILayout.HelpBox("No triggers selected.", MessageType.Info);
                return;
            }

            // Collect duplicate command names for error highlighting.
            var cmdNameCounts = selectedTriggers
                .GroupBy(t => t.SuggestedCommandName, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

            foreach (var trigger in selectedTriggers)
            {
                var isDuplicate = cmdNameCounts.TryGetValue(trigger.SuggestedCommandName, out int cnt) && cnt > 1;
                var cmdBase     = CommandBaseClass(trigger);

                if (isDuplicate) GUI.backgroundColor = new Color(1f, 0.45f, 0.45f, 1f);

                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.LabelField("class", EditorStyles.miniLabel, GUILayout.Width(36));
                    trigger.SuggestedCommandName = EditorGUILayout.TextField(trigger.SuggestedCommandName);
                    EditorGUILayout.LabelField($": {cmdBase}", EditorStyles.miniLabel, GUILayout.Width(160));
                    if (isDuplicate)
                    {
                        var prev = GUI.color;
                        GUI.color = new Color(1f, 0.5f, 0.5f, 1f);
                        EditorGUILayout.LabelField("  duplicate!", EditorStyles.boldLabel, GUILayout.Width(76));
                        GUI.color = prev;
                    }
                }

                if (_autoBindCommands && _targetModule != null)
                    EditorGUILayout.LabelField(
                        $"    Commander.Bind<{trigger.SuggestedCommandName}, {trigger.SuggestedMessageName}{BuildTypeArgs(trigger)}>();",
                        EditorStyles.miniLabel);
            }
        }

        // ── Navigation ────────────────────────────────────────────────────────

        private void DrawNavigationButtons()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_currentStep == WizardStep.ViewElements))
                {
                    if (GUILayout.Button("< Previous", GUILayout.Width(100)))
                        _currentStep = (WizardStep)((int)_currentStep - 1);
                }

                GUILayout.FlexibleSpace();

                if (_currentStep == WizardStep.ConfigureCommands)
                {
                    var selected = SelectedTriggers();
                    var hasDuplicateCommands = _generateCommands && selected
                        .GroupBy(t => t.SuggestedCommandName, StringComparer.Ordinal)
                        .Any(g => g.Count() > 1);
                    var blocked = selected.Count == 0 || _targetMediator == null || hasDuplicateCommands;
                    using (new EditorGUI.DisabledScope(blocked))
                    {
                        if (GUILayout.Button(
                            hasDuplicateCommands
                                ? "Duplicate command names — fix first"
                                : $"Generate All ({selected.Count} trigger{(selected.Count == 1 ? "" : "s")})",
                            GUILayout.Height(36), GUILayout.Width(220)))
                            GenerateAll();
                    }
                }
                else
                {
                    if (GUILayout.Button("Next >", GUILayout.Width(100)))
                        _currentStep = (WizardStep)((int)_currentStep + 1);
                }
            }
        }

        // ── Code generation ───────────────────────────────────────────────────

        private void GenerateAll()
        {
            var selected = SelectedTriggers();
            if (selected.Count == 0) return;

            try
            {
                if (_generateMessages)
                    GenerateMessages(selected);

                GenerateMediatorHandlers(selected);

                if (_generateCommands)
                {
                    GenerateCommands(selected);
                    if (_autoBindCommands && _targetModule != null)
                        BindCommandsInModule(selected);
                }

                AssetDatabase.Refresh();

                if (_generateMessages)
                    PushFolderHistory(MessagesFolderHistoryKey, _messagesFolderHistory, _messagesFolder);
                if (_generateCommands)
                    PushFolderHistory(CommandsFolderHistoryKey, _commandsFolderHistory, _commandsFolder);

                EditorUtility.DisplayDialog("Success",
                    $"Generated:\n" +
                    $"- {selected.Count} mediator handler(s)\n" +
                    (_generateMessages ? $"- {selected.Count} message(s)\n" : "") +
                    (_generateCommands ? $"- {selected.Count} command(s)\n" : ""),
                    "OK");

                Close();
            }
            catch (Exception ex)
            {
                EditorUtility.DisplayDialog("Error", $"Generation failed:\n{ex.Message}", "OK");
                Debug.LogException(ex);
            }
        }

        private void GenerateMessages(List<ViewTriggerInfo> triggers)
        {
            Directory.CreateDirectory(Path.GetFullPath(_messagesFolder));

            var filePath    = Path.Combine(_messagesFolder, _messagesFileName).Replace("\\", "/");
            var fullPath    = Path.GetFullPath(filePath);
            var existing    = File.Exists(fullPath) ? File.ReadAllText(fullPath) : string.Empty;

            var code = new StringBuilder();
            foreach (var trigger in triggers)
            {
                if (existing.Contains($"struct {trigger.SuggestedMessageName}", StringComparison.Ordinal))
                {
                    Debug.LogWarning($"[MediatorHandlerGenerator] Message '{trigger.SuggestedMessageName}' already exists. Skipping.");
                    continue;
                }
                if (code.Length > 0) code.AppendLine();
                // Messages are pure type declarations — no fields or constructors.
                var iface = trigger.ParameterTypes.Length == 0
                    ? "IMessage"
                    : $"IMessage<{string.Join(", ", trigger.ParameterTypes.Select(GetFriendlyTypeName))}>";
                code.AppendLine($"    public struct {trigger.SuggestedMessageName} : {iface} {{ }}");
            }

            if (code.Length == 0) return;

            var extraUsings = CollectRequiredUsings(triggers);
            string final = string.IsNullOrEmpty(existing)
                ? GenerateMessagesFile(ResolveNamespaceForPath(filePath), code.ToString(), extraUsings)
                : InsertMessagesIntoFile(existing, code.ToString(), extraUsings);

            File.WriteAllText(fullPath, final.Replace("\r\n", "\n").Replace("\r", "\n"));
            AssetDatabase.ImportAsset(filePath);
        }

        private void GenerateMediatorHandlers(List<ViewTriggerInfo> triggers)
        {
            if (string.IsNullOrEmpty(_mediatorScriptPath))
            {
                Debug.LogWarning("[MediatorHandlerGenerator] Mediator script not found. Skipping.");
                return;
            }

            var selectedElements = _viewElements.Where(e => e.IsSelected).ToList();
            var code    = File.ReadAllText(_mediatorScriptPath);
            var updated = InsertMediatorCode(code, _targetMediator.GetType(), selectedElements, triggers, _generateMessages, _elementByComponent);
            File.WriteAllText(_mediatorScriptPath, updated.Replace("\r\n", "\n").Replace("\r", "\n"));
            AssetDatabase.ImportAsset(_mediatorScriptPath);
        }

        private void GenerateCommands(List<ViewTriggerInfo> triggers)
        {
            Directory.CreateDirectory(Path.GetFullPath(_commandsFolder));
            var ns = ResolveNamespaceForPath(_commandsFolder);

            foreach (var trigger in triggers)
            {
                var filePath  = Path.Combine(_commandsFolder, $"{trigger.SuggestedCommandName}.cs").Replace("\\", "/");
                var fullPath  = Path.GetFullPath(filePath);
                if (File.Exists(fullPath))
                {
                    Debug.LogWarning($"[MediatorHandlerGenerator] Command '{trigger.SuggestedCommandName}' already exists. Skipping.");
                    continue;
                }
                File.WriteAllText(fullPath, GenerateCommandCode(trigger, ns).Replace("\r\n", "\n").Replace("\r", "\n"));
                AssetDatabase.ImportAsset(filePath);
            }
        }

        private void BindCommandsInModule(List<ViewTriggerInfo> triggers)
        {
            if (_targetModule == null || !_autoBindCommands) return;

            var modulePath = FindScriptPath(_targetModule.GetType());
            if (string.IsNullOrEmpty(modulePath))
            {
                Debug.LogWarning("[MediatorHandlerGenerator] Module script not found. Skipping auto-bind.");
                return;
            }

            var moduleCode = File.ReadAllText(modulePath);
            var bindings   = new StringBuilder();
            foreach (var trigger in triggers)
                bindings.AppendLine($"            Commander.Bind<{trigger.SuggestedCommandName}, {trigger.SuggestedMessageName}{BuildTypeArgs(trigger)}>();");

            var updated = InsertCommandBindings(moduleCode, bindings.ToString());
            File.WriteAllText(modulePath, updated.Replace("\r\n", "\n").Replace("\r", "\n"));
            AssetDatabase.ImportAsset(modulePath);
        }

        // ── Mediator code insertion ───────────────────────────────────────────

        private static string InsertMediatorCode(
            string originalCode,
            Type mediatorType,
            List<ViewElementInfo> selectedElements,
            List<ViewTriggerInfo> triggers,
            bool generateMessages,
            Dictionary<Component, ViewElementInfo> elementByComponent)
        {
            var code = originalCode.Replace("\r\n", "\n").Replace("\r", "\n");

            var classOpen = FindClassBodyOpen(code, mediatorType.Name);
            if (classOpen < 0)
            {
                Debug.LogError($"[MediatorHandlerGenerator] Class '{mediatorType.Name}' not found.");
                return originalCode;
            }
            var classClose = FindMatchingBrace(code, classOpen);
            if (classClose < 0)
            {
                Debug.LogError("[MediatorHandlerGenerator] Class closing brace not found.");
                return originalCode;
            }

            var memberIndent = DetectMemberIndent(code, classOpen, classClose);
            var bodyIndent   = memberIndent + "    ";

            var onInitOpen   = FindMethodBodyOpen(code, "OnInitialized", classOpen, classClose);
            var onCleanupOpen = FindMethodBodyOpen(code, "OnCleanup",    classOpen, classClose);

            var newFields       = new StringBuilder();
            var addListeners    = new StringBuilder();
            var removeListeners = new StringBuilder();
            var handlerMethods  = new StringBuilder();

            // Fields: one per selected view element (not per trigger — avoids duplicates).
            foreach (var el in selectedElements)
            {
                if (!ContainsInRange(code, el.FieldName, classOpen, classClose))
                    newFields.AppendLine($"{memberIndent}[SerializeField] private {el.ComponentTypeName} {el.FieldName};");
            }

            // Handlers + listener wiring: one per trigger.
            foreach (var trigger in triggers)
            {
                if (!elementByComponent.TryGetValue(trigger.Component, out var el)) continue;
                var fieldName    = el.FieldName;
                var handlerName  = trigger.SuggestedHandlerName;
                var messageName  = trigger.SuggestedMessageName;

                if (!ContainsInRange(code, $"AddListener({handlerName})", classOpen, classClose))
                    addListeners.AppendLine($"{bodyIndent}{fieldName}.{trigger.EventName}.AddListener({handlerName});");

                if (!ContainsInRange(code, $"RemoveListener({handlerName})", classOpen, classClose))
                    removeListeners.AppendLine($"{bodyIndent}{fieldName}.{trigger.EventName}.RemoveListener({handlerName});");

                if (!ContainsInRange(code, handlerName + "(", classOpen, classClose))
                {
                    var paramList    = BuildHandlerParams(trigger);
                    var publishCall  = generateMessages
                        ? BuildPublishCall(messageName, trigger)
                        : $"// TODO: Messenger.Publish<{messageName}>()";

                    handlerMethods.AppendLine();
                    handlerMethods.AppendLine($"{memberIndent}private void {handlerName}({paramList})");
                    handlerMethods.AppendLine($"{memberIndent}{{");
                    handlerMethods.AppendLine($"{bodyIndent}{publishCall}");
                    handlerMethods.AppendLine($"{memberIndent}}}");
                }
            }

            var breaks = new SortedList<int, string>();
            void AddBreak(int pos, string text)
            {
                breaks.TryGetValue(pos, out var ex);
                breaks[pos] = (ex ?? string.Empty) + text;
            }

            if (newFields.Length > 0)
                AddBreak(FindInsertAfterOpenBrace(code, classOpen), "\n" + newFields);

            if (addListeners.Length > 0)
            {
                if (onInitOpen >= 0)
                    AddBreak(FindInsertAfterOpenBrace(code, onInitOpen), "\n" + addListeners);
                else
                {
                    var sb = new StringBuilder();
                    sb.AppendLine();
                    sb.AppendLine($"{memberIndent}protected override void OnInitialized()");
                    sb.AppendLine($"{memberIndent}{{");
                    sb.Append(addListeners.ToString().TrimEnd('\n', '\r'));
                    sb.AppendLine();
                    sb.AppendLine($"{bodyIndent}// TODO: Subscribe<TResponseMessage>(OnResponseReceived);");
                    sb.AppendLine($"{memberIndent}}}");
                    AddBreak(classClose, sb.ToString());
                }
            }

            if (removeListeners.Length > 0)
            {
                if (onCleanupOpen >= 0)
                    AddBreak(FindInsertAfterOpenBrace(code, onCleanupOpen), "\n" + removeListeners);
                else
                {
                    var sb = new StringBuilder();
                    sb.AppendLine();
                    sb.AppendLine($"{memberIndent}protected override void OnCleanup()");
                    sb.AppendLine($"{memberIndent}{{");
                    sb.Append(removeListeners.ToString().TrimEnd('\n', '\r'));
                    sb.AppendLine();
                    sb.AppendLine($"{memberIndent}}}");
                    AddBreak(classClose, sb.ToString());
                }
            }

            if (handlerMethods.Length > 0)
                AddBreak(classClose, handlerMethods.ToString());

            var result  = new StringBuilder(code.Length + 2048);
            int lastPos = 0;
            foreach (var kvp in breaks)
            {
                result.Append(code, lastPos, kvp.Key - lastPos);
                result.Append(kvp.Value);
                lastPos = kvp.Key;
            }
            result.Append(code, lastPos, code.Length - lastPos);

            // Inject any missing using directives so the mediator compiles immediately.
            return EnsureUsings(result.ToString(), CollectRequiredUsings(triggers));
        }

        // ── Module binding insertion ──────────────────────────────────────────

        private static string InsertCommandBindings(string moduleCode, string bindings)
        {
            var code = moduleCode.Replace("\r\n", "\n").Replace("\r", "\n");
            var methodOpen = FindMethodBodyOpen(code, "BindCommands", 0, code.Length);
            if (methodOpen < 0)
            {
                Debug.LogWarning("[MediatorHandlerGenerator] BindCommands method not found. Skipping auto-bind.");
                return moduleCode;
            }
            return code.Insert(FindInsertAfterOpenBrace(code, methodOpen), "\n" + bindings);
        }

        // ── Messages file insertion ───────────────────────────────────────────

        private static string InsertMessagesIntoFile(string existing, string messagesCode,
            IEnumerable<string> extraUsings = null)
        {
            var code = existing.Replace("\r\n", "\n").Replace("\r", "\n");
            if (!code.Contains("using mvcExpress;", StringComparison.Ordinal))
                code = "using mvcExpress;\n" + code;
            // Add any extra usings that aren't already present.
            if (extraUsings != null)
                foreach (var u in extraUsings)
                {
                    var directive = $"using {u};";
                    if (!code.Contains(directive, StringComparison.Ordinal))
                        code = directive + "\n" + code;
                }

            if (IsFileScopedNamespace(code))
                return code.TrimEnd() + "\n\n" + messagesCode.TrimEnd() + "\n";

            if (code.Contains("namespace ", StringComparison.Ordinal))
            {
                var lastBrace = code.LastIndexOf('}');
                if (lastBrace > 0)
                    return code.Insert(lastBrace, "\n" + messagesCode + "\n");
            }

            return code.TrimEnd() + "\n\n" + messagesCode.TrimEnd() + "\n";
        }

        // ── Code builders ─────────────────────────────────────────────────────

        private static string GenerateCommandCode(ViewTriggerInfo trigger, string ns)
        {
            var sb     = new StringBuilder();
            var indent = string.IsNullOrEmpty(ns) ? "" : "    ";
            var cmdBase = CommandBaseClass(trigger);

            sb.AppendLine("using mvcExpress;");
            foreach (var u in CollectRequiredUsings(new[] { trigger }))
                sb.AppendLine($"using {u};");
            sb.AppendLine();

            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine($"namespace {ns}");
                sb.AppendLine("{");
            }

            sb.AppendLine($"{indent}public class {trigger.SuggestedCommandName} : {cmdBase}");
            sb.AppendLine($"{indent}{{");

            // Execute signature depends on payload arity.
            if (trigger.ParameterTypes.Length == 0)
            {
                sb.AppendLine($"{indent}    public override void Execute()");
                sb.AppendLine($"{indent}    {{");
                sb.AppendLine($"{indent}        // TODO: implement");
                sb.AppendLine($"{indent}    }}");
            }
            else
            {
                var execParams = string.Join(", ", trigger.ParameterTypes
                    .Select((t, i) => $"{GetFriendlyTypeName(t)} p{i + 1}"));
                sb.AppendLine($"{indent}    public override void Execute({execParams})");
                sb.AppendLine($"{indent}    {{");
                sb.AppendLine($"{indent}        // TODO: implement");
                sb.AppendLine($"{indent}    }}");
            }

            sb.AppendLine($"{indent}}}");

            if (!string.IsNullOrEmpty(ns))
                sb.AppendLine("}");

            return sb.ToString();
        }

        private static string GenerateMessagesFile(string ns, string messagesCode,
            IEnumerable<string> extraUsings = null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("using mvcExpress;");
            if (extraUsings != null)
                foreach (var u in extraUsings) sb.AppendLine($"using {u};");
            sb.AppendLine();
            if (!string.IsNullOrEmpty(ns))
            {
                sb.AppendLine($"namespace {ns}");
                sb.AppendLine("{");
                sb.Append(messagesCode);
                sb.AppendLine("}");
            }
            else
            {
                sb.Append(messagesCode.TrimStart());
            }
            return sb.ToString();
        }

        // ── API helpers ───────────────────────────────────────────────────────

        // Command<T1> base: "Command" for no payload, "Command<float>" for one payload, etc.
        private static string CommandBaseClass(ViewTriggerInfo trigger)
        {
            if (trigger.ParameterTypes.Length == 0) return "Command";
            return $"Command<{string.Join(", ", trigger.ParameterTypes.Select(GetFriendlyTypeName))}>";
        }

        // "": no extra type args; ", float": for Commander.Bind<Cmd, Msg, float>()
        private static string BuildTypeArgs(ViewTriggerInfo trigger)
        {
            if (trigger.ParameterTypes.Length == 0) return string.Empty;
            return ", " + string.Join(", ", trigger.ParameterTypes.Select(GetFriendlyTypeName));
        }

        // Messenger.Publish<Msg>() or Messenger.Publish<Msg, float>(p1)
        private static string BuildPublishCall(string messageName, ViewTriggerInfo trigger)
        {
            if (trigger.ParameterTypes.Length == 0)
                return $"Messenger.Publish<{messageName}>();";

            var typeArgs = string.Join(", ", trigger.ParameterTypes.Select(GetFriendlyTypeName));
            var valArgs  = string.Join(", ", Enumerable.Range(1, trigger.ParameterTypes.Length)
                .Select(i => $"param{i}"));
            return $"Messenger.Publish<{messageName}, {typeArgs}>({valArgs});";
        }

        // Handler method parameter list: "" or "float param1, bool param2"
        private static string BuildHandlerParams(ViewTriggerInfo trigger)
        {
            if (trigger.ParameterTypes.Length == 0) return string.Empty;
            return string.Join(", ", trigger.ParameterTypes
                .Select((t, i) => $"{GetFriendlyTypeName(t)} param{i + 1}"));
        }

        // ── Using-directive injection ─────────────────────────────────────────

        // Inserts any `using X;` directives that are not already present in the file.
        private static string EnsureUsings(string code, IEnumerable<string> usings)
        {
            foreach (var ns in usings)
            {
                var directive = $"using {ns};";
                if (!code.Contains(directive, StringComparison.Ordinal))
                    code = InsertUsingDirective(code, directive);
            }
            return code;
        }

        private static string InsertUsingDirective(string code, string directive)
        {
            var normalized = code.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines      = normalized.Split('\n');
            var list       = new List<string>(lines);

            int lastUsing = -1;
            for (int i = 0; i < list.Count; i++)
            {
                var l = list[i].TrimStart();
                if (l.StartsWith("using ", StringComparison.Ordinal))
                    lastUsing = i;
                else if (lastUsing >= 0)
                    break;
            }

            list.Insert(lastUsing >= 0 ? lastUsing + 1 : 0, directive);
            return string.Join("\n", list);
        }

        // ── Brace-counting helpers (unchanged) ────────────────────────────────

        private static int FindClassBodyOpen(string code, string typeName)
        {
            int searchFrom = 0;
            while (true)
            {
                int bestIdx = -1;
                foreach (var kw in new[] { "class " + typeName, "struct " + typeName })
                {
                    int c = code.IndexOf(kw, searchFrom, StringComparison.Ordinal);
                    if (c >= 0 && (bestIdx < 0 || c < bestIdx)) bestIdx = c;
                }
                if (bestIdx < 0) return -1;

                int kwLen   = code[bestIdx] == 'c' ? 6 : 7;
                int nameEnd = bestIdx + kwLen + typeName.Length;
                if (nameEnd < code.Length && (char.IsLetterOrDigit(code[nameEnd]) || code[nameEnd] == '_'))
                { searchFrom = bestIdx + 1; continue; }

                return IndexOfBrace(code, bestIdx, '{');
            }
        }

        private static int FindMethodBodyOpen(string code, string methodName, int searchFrom, int searchTo)
        {
            int idx = searchFrom;
            while (idx < searchTo)
            {
                int found = code.IndexOf(methodName, idx, searchTo - idx, StringComparison.Ordinal);
                if (found < 0) return -1;

                if (found > 0 && (char.IsLetterOrDigit(code[found - 1]) || code[found - 1] == '_'))
                { idx = found + 1; continue; }
                int nameEnd = found + methodName.Length;
                if (nameEnd < code.Length && (char.IsLetterOrDigit(code[nameEnd]) || code[nameEnd] == '_'))
                { idx = nameEnd; continue; }

                int scan = nameEnd;
                while (scan < searchTo && code[scan] != '(' && code[scan] != '\n') scan++;
                if (scan >= searchTo || code[scan] != '(') { idx = found + 1; continue; }

                int paren = scan, parenDepth = 0;
                for (; paren < searchTo; paren++)
                {
                    if (code[paren] == '(') parenDepth++;
                    else if (code[paren] == ')') { parenDepth--; if (parenDepth == 0) break; }
                }
                if (paren >= searchTo) return -1;

                int braceIdx = IndexOfBrace(code, paren + 1, '{');
                if (braceIdx < 0 || braceIdx >= searchTo) { idx = found + 1; continue; }
                return braceIdx;
            }
            return -1;
        }

        private static int IndexOfBrace(string code, int from, char brace)
        {
            int i = from;
            while (i < code.Length)
            {
                i = SkipStringOrComment(code, i, out bool skipped);
                if (skipped) continue;
                if (code[i] == brace) return i;
                i++;
            }
            return -1;
        }

        private static int FindMatchingBrace(string code, int openBraceIdx)
        {
            int depth = 0, i = openBraceIdx;
            while (i < code.Length)
            {
                i = SkipStringOrComment(code, i, out bool skipped);
                if (skipped) continue;
                if (code[i] == '{') depth++;
                else if (code[i] == '}') { depth--; if (depth == 0) return i; }
                i++;
            }
            return -1;
        }

        private static int SkipStringOrComment(string code, int i, out bool skipped)
        {
            skipped = true;
            if (i + 1 < code.Length && code[i] == '/' && code[i + 1] == '/')
            { i += 2; while (i < code.Length && code[i] != '\n') i++; return i; }
            if (i + 1 < code.Length && code[i] == '/' && code[i + 1] == '*')
            { i += 2; while (i + 1 < code.Length && !(code[i] == '*' && code[i + 1] == '/')) i++; return i + 2; }
            if (i + 1 < code.Length && code[i] == '@' && code[i + 1] == '"')
            {
                i += 2;
                while (i < code.Length)
                {
                    if (code[i] == '"') { if (i + 1 < code.Length && code[i + 1] == '"') { i += 2; continue; } return i + 1; }
                    i++;
                }
                return i;
            }
            if (code[i] == '"')
            { i++; while (i < code.Length && code[i] != '"') { if (code[i] == '\\') i++; i++; } return i + 1; }
            if (code[i] == '\'')
            { i++; if (i < code.Length && code[i] == '\\') i++; i++; if (i < code.Length && code[i] == '\'') i++; return i; }
            skipped = false;
            return i;
        }

        private static int FindInsertAfterOpenBrace(string code, int openBraceIdx)
        {
            int pos = openBraceIdx + 1;
            while (pos < code.Length && code[pos] != '\n') pos++;
            if (pos < code.Length) pos++;
            return pos;
        }

        private static string DetectMemberIndent(string code, int classBodyOpen, int classBodyClose)
        {
            int pos = classBodyOpen + 1;
            while (pos < classBodyClose)
            {
                if (code[pos] == '\n')
                {
                    pos++;
                    int indentStart = pos;
                    while (pos < classBodyClose && (code[pos] == ' ' || code[pos] == '\t')) pos++;
                    if (pos < classBodyClose && code[pos] != '\n' && code[pos] != '\r')
                        return code.Substring(indentStart, pos - indentStart);
                }
                else pos++;
            }
            return "        ";
        }

        private static bool ContainsInRange(string code, string token, int start, int end)
            => code.IndexOf(token, start, end - start, StringComparison.Ordinal) >= 0;

        private static bool IsFileScopedNamespace(string code)
        {
            var idx = code.IndexOf("namespace ", StringComparison.Ordinal);
            if (idx < 0) return false;
            idx += "namespace ".Length;
            while (idx < code.Length && char.IsWhiteSpace(code[idx])) idx++;
            while (idx < code.Length && (char.IsLetterOrDigit(code[idx]) || code[idx] == '_' || code[idx] == '.')) idx++;
            while (idx < code.Length && (code[idx] == ' ' || code[idx] == '\t')) idx++;
            return idx < code.Length && code[idx] == ';';
        }

        // ── Utility helpers ───────────────────────────────────────────────────

        private List<ViewTriggerInfo> SelectedTriggers() =>
            _allTriggers
                .Where(t => t.IsSelected
                    && _elementByComponent.TryGetValue(t.Component, out var e)
                    && e.IsSelected)
                .ToList();

        private static string GetFriendlyTypeName(Type type)
        {
            if (type == null) return "object";
            if (type == typeof(int))     return "int";
            if (type == typeof(float))   return "float";
            if (type == typeof(bool))    return "bool";
            if (type == typeof(string))  return "string";
            if (type == typeof(Vector2)) return "Vector2";
            if (type == typeof(Vector3)) return "Vector3";
            // Nested types (e.g. TouchScreenKeyboard.Status): use "DeclaringType.Name" form.
            if (type.IsNested)
                return $"{type.DeclaringType.Name}.{type.Name}";
            return type.Name;
        }

        // Returns the namespace to add as a `using` for the given type, or null if none needed.
        private static string GetRequiredUsing(Type type)
        {
            if (type == null) return null;
            if (type.IsPrimitive || type == typeof(string)) return null;
            var root = type.IsNested ? type.DeclaringType : type;
            var ns   = root?.Namespace;
            if (string.IsNullOrEmpty(ns)) return null;
            if (ns == "mvcExpress" || ns == "System") return null;
            return ns;
        }

        private static IEnumerable<string> CollectRequiredUsings(IEnumerable<ViewTriggerInfo> triggers)
        {
            var usings = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var trigger in triggers)
                foreach (var t in trigger.ParameterTypes)
                {
                    var u = GetRequiredUsing(t);
                    if (u != null) usings.Add(u);
                }
            return usings;
        }

        private static string FindScriptPath(Type type)
        {
            var guids = AssetDatabase.FindAssets($"t:MonoScript {type.Name}");
            foreach (var guid in guids)
            {
                var path   = AssetDatabase.GUIDToAssetPath(guid);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script != null && script.GetClass() == type) return path;
            }
            return null;
        }

        private static string MakeRelativeToProject(string abs)
        {
            var data = Application.dataPath;
            return abs.StartsWith(data, StringComparison.Ordinal)
                ? "Assets" + abs.Substring(data.Length)
                : abs;
        }

        private static string ResolveNamespaceForPath(string assetPath)
        {
            var parts = assetPath.Replace("\\", "/").Split('/')
                .Where(p => p != "Assets" && p != "Scripts" && !p.Contains('.'))
                .ToArray();
            return parts.Length > 0 ? string.Join(".", parts) : string.Empty;
        }

        private static bool IsValidFieldName(string s)
        {
            if (string.IsNullOrEmpty(s) || s[0] != '_') return false;
            for (int i = 1; i < s.Length; i++)
                if (!char.IsLetterOrDigit(s[i]) && s[i] != '_') return false;
            return s.Length > 1;
        }

        // ── Folder history helpers ─────────────────────────────────────────────

        private static List<string> LoadFolderHistory(string key)
        {
            var result = new List<string>();
            var raw = EditorPrefs.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(raw)) return result;
            foreach (var entry in raw.Split('|'))
            {
                var t = entry.Trim();
                if (!string.IsNullOrEmpty(t)) result.Add(t);
            }
            return result;
        }

        private static void PushFolderHistory(string key, List<string> history, string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return;
            history.Remove(path);
            history.Insert(0, path);
            if (history.Count > MaxFolderHistory) history.RemoveAt(history.Count - 1);
            EditorPrefs.SetString(key, string.Join("|", history));
        }

        private static void ShowFolderHistoryMenu(List<string> history, Action<string> onSelect)
        {
            var menu = new GenericMenu();
            foreach (var path in history)
            {
                var p = path;
                menu.AddItem(new GUIContent(p), false, () => onSelect(p));
            }
            menu.ShowAsContext();
        }
    }
}

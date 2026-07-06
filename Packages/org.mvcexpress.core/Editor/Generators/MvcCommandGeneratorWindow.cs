using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.Generators
{
    /// <summary>
    /// Editor window for generating <c>Command</c> and <c>CommandAsync</c> script files.
    /// Open via <b>Window &gt; mvcExpress &gt; Generators &gt; Command</b>.
    /// </summary>
    public class MvcCommandGeneratorWindow : MvcScriptGeneratorWindowBase
    {
        protected override string PrefPrefix       => "MvcCommandGen";
        protected override string WindowTitle      => isAsync ? "Async Command Generator" : "Command Generator";
        protected override Vector2 WindowSize      => new Vector2(SettingsPanelWidth + BarWidth, 420f);
        protected override string DefaultFileName  => "NewCommand.cs";
        protected override string DefaultClassName => string.Empty;
        protected override string FolderActorKey   => "MvcCommand";

        private bool   isAsync               = false;
        private int    paramCount            = 0;
        private bool   deriveParamsFromMessage = false;
        private string namePrefix            = string.Empty;
        private int    selectedMessageTypeIndex = 0;

        private readonly List<Type> messageTypeCandidates = new();

        public static void ShowWindow(bool isAsync = false)
        {
            var w = GetWindow<MvcCommandGeneratorWindow>(true,
                isAsync ? "Async Command Generator" : "Command Generator", true);
            w.isAsync = isAsync;
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

        protected override void OnEnable()
        {
            base.OnEnable();
            if (IsRestoredFromGeneration) return;
            isAsync                 = EditorPrefs.GetBool(MakeSharedKey($"{PrefPrefix}_IsAsync"), false);
            paramCount              = EditorPrefs.GetInt(MakeSharedKey($"{PrefPrefix}_ParamCount"), 0);
            deriveParamsFromMessage = EditorPrefs.GetBool(MakeSharedKey($"{PrefPrefix}_DeriveParams"), false);
            selectedMessageTypeIndex = EditorPrefs.GetInt(MakeSharedKey($"{PrefPrefix}_MsgIndex"), 0);
            namePrefix              = string.Empty;
            RebuildMessageTypeCandidates();
            selectedMessageTypeIndex = Mathf.Clamp(selectedMessageTypeIndex, 0, Mathf.Max(0, messageTypeCandidates.Count - 1));
            className = Generator.BuildCN(namePrefix, isAsync);
        }

        private void OnDisable()
        {
            SavePreferences();
            EditorPrefs.SetBool(MakeSharedKey($"{PrefPrefix}_IsAsync"), isAsync);
            EditorPrefs.SetInt(MakeSharedKey($"{PrefPrefix}_ParamCount"), paramCount);
            EditorPrefs.SetBool(MakeSharedKey($"{PrefPrefix}_DeriveParams"), deriveParamsFromMessage);
            EditorPrefs.SetInt(MakeSharedKey($"{PrefPrefix}_MsgIndex"), selectedMessageTypeIndex);
        }

        private void OnGUI()
        {
            if (DrawIfGenerating()) return;
            InitBaseStyles();

            GUILayout.BeginArea(new Rect(0f, 0f, GetMainWidth(), position.height));

            EditorGUILayout.Space(8f);
            DrawNameRow();
            EditorGUILayout.Space(5f);
            DrawNamespaceFieldBase();
            EditorGUILayout.Space(8f);
            DrawParameterSection();
            EditorGUILayout.Space(8f);
            DrawPreviewBoxBase(BuildPreviewText());
            EditorGUILayout.Space(6f);
            DrawCreateBtn();
            EditorGUILayout.Space(8f);

            GUILayout.EndArea();
            DrawSettingsPanelBase(GetSettingsLeft());
            DrawActivationBarBase();
        }

        // The command generator adds its own items (async toggle) in the settings panel.
        protected override void DrawWindowSettingsItems()
        {
            GUILayout.Label("Command Options", SettingsSectionStyle);
            DrawPanelToggle(ref isAsync, "Async Command",
                "Generates a CommandAsync instead of Command. The Execute method becomes async Task ExecuteAsync.");
        }

        private void DrawNameRow()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Name", GUILayout.Width(60));
                var prev = namePrefix;
                namePrefix = EditorGUILayout.TextField(namePrefix ?? string.Empty);
                if (namePrefix != prev) className = Generator.BuildCN(namePrefix, isAsync);
                EditorGUILayout.LabelField(isAsync ? "AsyncCommand" : "Command", GUILayout.Width(100));
            }
            if (string.IsNullOrWhiteSpace(namePrefix))
                EditorGUILayout.HelpBox("Enter a command name.", MessageType.Error);

            DrawFolderFieldBase();
        }

        private void DrawParameterSection()
        {
            EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);

            deriveParamsFromMessage = EditorGUILayout.ToggleLeft(
                "Derive from Message type (IMessage<...>)", deriveParamsFromMessage);

            if (deriveParamsFromMessage)
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField("Message", GUILayout.Width(70));
                    var names = messageTypeCandidates.Count == 0
                        ? new[] { "<no IMessage types found>" }
                        : messageTypeCandidates.Select(t => t.FullName).ToArray();
                    using (new EditorGUI.DisabledScope(messageTypeCandidates.Count == 0))
                        selectedMessageTypeIndex = EditorGUILayout.Popup(selectedMessageTypeIndex, names);
                    if (GUILayout.Button("Re-scan", GUILayout.Width(70)))
                    {
                        RebuildMessageTypeCandidates();
                        selectedMessageTypeIndex = Mathf.Clamp(selectedMessageTypeIndex, 0,
                            Mathf.Max(0, messageTypeCandidates.Count - 1));
                    }
                }
                if (messageTypeCandidates.Count > 0)
                {
                    var args  = Generator.TryGetMessageGenericArgs(messageTypeCandidates[selectedMessageTypeIndex]);
                    EditorGUILayout.LabelField($"Detected: {args?.Length ?? 0} parameter(s)", EditorStyles.miniLabel);
                }
                else
                {
                    EditorGUILayout.HelpBox("No compiled IMessage types found.", MessageType.Info);
                }
            }
            else
            {
                paramCount = EditorGUILayout.IntSlider("Count", paramCount, 0, 12);
                EditorGUILayout.LabelField("Parameter types default to int — edit after generation.", EditorStyles.miniLabel);
            }
        }

        private void DrawCreateBtn()
        {
            bool nsInvalid = useNamespace && useCustomNamespace && !MvcGeneratorSharedSettings.IsValidNamespace(customNamespace);
            bool canGen    = !string.IsNullOrWhiteSpace(namePrefix) && !nsInvalid && CanGenerate();
            using (new EditorGUI.DisabledScope(!canGen))
            {
                if (GUILayout.Button("Generate", GUILayout.Height(36)))
                {
                    PushFolderToHistory(selectedFolderPath);
                    SavePreferences();
                    EditorPrefs.SetBool(MakeSharedKey($"{PrefPrefix}_IsAsync"), isAsync);
                    EditorPrefs.SetInt(MakeSharedKey($"{PrefPrefix}_ParamCount"), paramCount);
                    EditorPrefs.SetBool(MakeSharedKey($"{PrefPrefix}_DeriveParams"), deriveParamsFromMessage);
                    EditorPrefs.SetInt(MakeSharedKey($"{PrefPrefix}_MsgIndex"), selectedMessageTypeIndex);
                    className = Generator.BuildCN(namePrefix, isAsync);
                    CreateScriptInSelectedFolder(GetTemplate);
                    BeginGeneration(className);
                }
            }
        }

        private bool CanGenerate()
        {
            if (!deriveParamsFromMessage) return true;
            return messageTypeCandidates.Count > 0
                && selectedMessageTypeIndex >= 0
                && selectedMessageTypeIndex < messageTypeCandidates.Count;
        }

        // ── Preview ───────────────────────────────────────────────────────────

        private string BuildPreviewText()
        {
            var cn = Generator.BuildCN(namePrefix, isAsync);
            var ns = ResolveNamespaceForPath(
                Path.Combine(string.IsNullOrWhiteSpace(selectedFolderPath) ? "Assets" : selectedFolderPath, cn + ".cs").Replace("\\", "/"));

            Type msgType = null;
            if (deriveParamsFromMessage && messageTypeCandidates.Count > 0 && selectedMessageTypeIndex >= 0 && selectedMessageTypeIndex < messageTypeCandidates.Count)
            {
                msgType = messageTypeCandidates[selectedMessageTypeIndex];
            }

            var resolved = Generator.ResolveSignature(deriveParamsFromMessage, msgType, paramCount, out _);
            var baseName = Generator.GetBaseTypeName(isAsync, resolved.ParamCount, resolved.ParamTypes);
            var execute  = isAsync
                ? (resolved.ParamCount == 0
                    ? "        public override async Task ExecuteAsync() { }"
                    : $"        public override async Task ExecuteAsync({Generator.GetParamList(resolved.ParamCount, resolved.ParamTypes)}) {{ }}")
                : (resolved.ParamCount == 0
                    ? "        public override void Execute() { }"
                    : $"        public override void Execute({Generator.GetParamList(resolved.ParamCount, resolved.ParamTypes)}) {{ }}");

            var classDecl = $"public class {cn} : {baseName}";
            if (string.IsNullOrEmpty(ns))
                return $"{classDecl}\n{{\n{execute}\n}}";

            return $"namespace {ns}\n{{\n    {classDecl}\n    {{\n    {execute}\n    }}\n}}";
        }

        // ── Template ──────────────────────────────────────────────────────────

        private string GetTemplate(string _)
        {
            Type msgType = null;
            if (deriveParamsFromMessage && messageTypeCandidates.Count > 0 && selectedMessageTypeIndex >= 0 && selectedMessageTypeIndex < messageTypeCandidates.Count)
            {
                msgType = messageTypeCandidates[selectedMessageTypeIndex];
            }
            return Generator.Generate(namePrefix, isAsync, deriveParamsFromMessage, msgType, paramCount);
        }

        public static class Generator
        {
            public static string Generate(string namePrefix, bool isAsync, bool deriveParamsFromMessage, Type msgType, int paramCount)
            {
                var cn       = BuildCN(namePrefix, isAsync);
                var resolved = ResolveSignature(deriveParamsFromMessage, msgType, paramCount, out var usings);
                return isAsync
                    ? BuildAsyncTemplate(cn, resolved.ParamCount, resolved.ParamTypes, usings)
                    : BuildSyncTemplate(cn, resolved.ParamCount, resolved.ParamTypes, usings);
            }

            public static (int ParamCount, string[] ParamTypes) ResolveSignature(bool deriveParamsFromMessage, Type msgType, int fallbackCount, out HashSet<string> usings)
            {
                usings = new HashSet<string>(StringComparer.Ordinal);
                if (!deriveParamsFromMessage)
                    return (fallbackCount, Enumerable.Repeat("int", Mathf.Max(0, fallbackCount)).ToArray());
                if (msgType == null) return (0, Array.Empty<string>());
                var args = TryGetMessageGenericArgs(msgType) ?? Array.Empty<Type>();
                AddUsings(usings, args);
                return (args.Length, args.Select(ToCSharpTypeName).ToArray());
            }

            public static string BuildSyncTemplate(string cn, int count, string[] types, HashSet<string> usings)
            {
                var sb = new StringBuilder("using mvcExpress;\n");
                foreach (var u in usings.OrderBy(x => x)) sb.AppendLine($"using {u};");
                sb.AppendLine($"\n${{NAMESPACE_BEGIN}}");
                sb.AppendLine($"    public class {cn} : {GetBaseTypeName(false, count, types)}");
                sb.AppendLine("    {");
                sb.AppendLine(count == 0
                    ? "        public override void Execute()\n        {\n        }"
                    : $"        public override void Execute({GetParamList(count, types)})\n        {{\n        }}");
                sb.AppendLine("    }");
                sb.AppendLine("${NAMESPACE_END}");
                return sb.ToString();
            }

            public static string BuildAsyncTemplate(string cn, int count, string[] types, HashSet<string> usings)
            {
                var sb = new StringBuilder("using System.Threading.Tasks;\nusing mvcExpress;\n");
                foreach (var u in usings.OrderBy(x => x)) sb.AppendLine($"using {u};");
                sb.AppendLine($"\n${{NAMESPACE_BEGIN}}");
                sb.AppendLine($"    public class {cn} : {GetBaseTypeName(true, count, types)}");
                sb.AppendLine("    {");
                sb.AppendLine(count == 0
                    ? "        public override async Task ExecuteAsync()\n        {\n            await Task.CompletedTask;\n        }"
                    : $"        public override async Task ExecuteAsync({GetParamList(count, types)})\n        {{\n            await Task.CompletedTask;\n        }}");
                sb.AppendLine("    }");
                sb.AppendLine("${NAMESPACE_END}");
                return sb.ToString();
            }

            public static string BuildCN(string prefix, bool async)
            {
                var p = (prefix ?? string.Empty).Trim();
                var suffix = async ? "AsyncCommand" : "Command";
                if (string.IsNullOrEmpty(p)) return suffix;
                if (p.EndsWith(suffix, StringComparison.Ordinal)) return p;
                if (!async && p.EndsWith("AsyncCommand", StringComparison.Ordinal))
                    p = p.Substring(0, p.Length - 12);
                else if (async && p.EndsWith("Command", StringComparison.Ordinal) &&
                         !p.EndsWith("AsyncCommand", StringComparison.Ordinal))
                    p = p.Substring(0, p.Length - 7);
                return p + suffix;
            }

            public static string GetBaseTypeName(bool async, int count, string[] types)
            {
                var baseName = async ? "CommandAsync" : "Command";
                if (count <= 0) return baseName;
                return $"{baseName}<{string.Join(", ", types.Select(Friendly))}>";
            }

            public static string GetParamList(int count, string[] types)
            {
                var sb = new StringBuilder();
                for (int i = 1; i <= count; i++)
                {
                    if (i > 1) sb.Append(", ");
                    sb.Append(Friendly(i - 1 < types.Length ? types[i - 1] : "int"));
                    sb.Append(" p").Append(i);
                }
                return sb.ToString();
            }

            public static string Friendly(string t) => t switch
            {
                "System.Int32"   => "int",
                "System.Single"  => "float",
                "System.Double"  => "double",
                "System.Boolean" => "bool",
                "System.String"  => "string",
                "System.Int64"   => "long",
                "System.Object"  => "object",
                _ => t.Contains('.') ? t.Split('.').Last() : t,
            };

            public static string ToCSharpTypeName(Type t)
            {
                if (t == null) return "object";
                if (t == typeof(int))    return "int";
                if (t == typeof(float))  return "float";
                if (t == typeof(double)) return "double";
                if (t == typeof(bool))   return "bool";
                if (t == typeof(string)) return "string";
                if (t == typeof(long))   return "long";
                if (t.IsArray) return ToCSharpTypeName(t.GetElementType()) + "[]";
                if (t.IsGenericType)
                {
                    var n = t.Name;
                    var tick = n.IndexOf('`');
                    if (tick >= 0) n = n.Substring(0, tick);
                    return n + "<" + string.Join(", ", t.GetGenericArguments().Select(ToCSharpTypeName)) + ">";
                }
                return t.Name;
            }

            public static void AddUsings(HashSet<string> usings, Type[] types)
            {
                if (types == null) return;
                foreach (var t in types)
                {
                    var ns = t?.Namespace;
                    if (string.IsNullOrWhiteSpace(ns)) continue;
                    if (ns == "UnityEngine" || ns.StartsWith("UnityEngine.", StringComparison.Ordinal))
                        usings.Add("UnityEngine");
                    if (ns == "UnityEngine.UI" || ns.StartsWith("UnityEngine.UI.", StringComparison.Ordinal))
                        usings.Add("UnityEngine.UI");
                    if (ns == "UnityEngine.Events" || ns.StartsWith("UnityEngine.Events.", StringComparison.Ordinal))
                        usings.Add("UnityEngine.Events");
                }
            }

            public static Type[] TryGetMessageGenericArgs(Type msgType)
            {
                if (msgType == null) return null;
                foreach (var iface in msgType.GetInterfaces())
                {
                    if (!iface.IsGenericType) continue;
                    var def = iface.GetGenericTypeDefinition();
                    if (def == typeof(IMessage<>)   || def == typeof(IMessage<,>)  || def == typeof(IMessage<,,>)  || def == typeof(IMessage<,,,>) ||
                        def == typeof(IMessage<,,,,>) || def == typeof(IMessage<,,,,,>) || def == typeof(IMessage<,,,,,,>) || def == typeof(IMessage<,,,,,,,>) ||
                        def == typeof(IMessage<,,,,,,,,>) || def == typeof(IMessage<,,,,,,,,,>) || def == typeof(IMessage<,,,,,,,,,,>) || def == typeof(IMessage<,,,,,,,,,,,>))
                        return iface.GetGenericArguments();
                }
                return Array.Empty<Type>();
            }
        }

        // ── Message type scanning ─────────────────────────────────────────────

        private void RebuildMessageTypeCandidates()
        {
            messageTypeCandidates.Clear();
            try
            {
                var zero = TypeCache.GetTypesDerivedFrom<IMessage>();
                foreach (var t in zero)
                    if (t != null && !t.IsAbstract && !t.IsGenericTypeDefinition && !messageTypeCandidates.Contains(t))
                        messageTypeCandidates.Add(t);
            }
            catch { }

            foreach (var gt in new[]
            {
                typeof(IMessage<>), typeof(IMessage<,>), typeof(IMessage<,,>), typeof(IMessage<,,,>),
                typeof(IMessage<,,,,>), typeof(IMessage<,,,,,>), typeof(IMessage<,,,,,,>), typeof(IMessage<,,,,,,,>),
                typeof(IMessage<,,,,,,,,>), typeof(IMessage<,,,,,,,,,>), typeof(IMessage<,,,,,,,,,,>), typeof(IMessage<,,,,,,,,,,,>),
            })
                AddDerived(gt);

            messageTypeCandidates.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));
        }

        private void AddDerived(Type openGeneric)
        {
            try
            {
                foreach (var t in TypeCache.GetTypesDerivedFrom(openGeneric))
                    if (t != null && !t.IsAbstract && !t.IsGenericTypeDefinition && !messageTypeCandidates.Contains(t))
                        messageTypeCandidates.Add(t);
            }
            catch { }
        }


    }
}

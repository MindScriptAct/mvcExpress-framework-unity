using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace mvcExpress.Editor.Generators
{
    internal sealed class MessageGeneratorWindow : EditorWindow
    {
        [Serializable]
        private class MessageDefinition
        {
            public string Name = string.Empty;
            public string Comment = string.Empty;
            public int ParamCount = 0;
            public bool UsePayload = false;
            public int[] SelectedCommonTypeIndexByParam = Array.Empty<int>();
            public string[] CustomTypeNameByParam = Array.Empty<string>();
            public int[] SelectedAllTypeIndexByParam = Array.Empty<int>();
            public string[] ParamNameByParam = Array.Empty<string>();

            public void ResizeParamArrays()
            {
                SelectedCommonTypeIndexByParam = ResizeInts(SelectedCommonTypeIndexByParam, ParamCount);
                CustomTypeNameByParam = ResizeStrings(CustomTypeNameByParam, ParamCount);
                SelectedAllTypeIndexByParam = ResizeInts(SelectedAllTypeIndexByParam, ParamCount);
                ParamNameByParam = ResizeStrings(ParamNameByParam, ParamCount);
            }

            private static int[] ResizeInts(int[] arr, int size)
            {
                if (arr == null) return new int[size];
                if (arr.Length == size) return arr;
                var n = new int[size];
                Array.Copy(arr, n, Math.Min(arr.Length, size));
                return n;
            }

            private static string[] ResizeStrings(string[] arr, int size)
            {
                if (arr == null) return new string[size];
                if (arr.Length == size) return arr;
                var n = new string[size];
                Array.Copy(arr, n, Math.Min(arr.Length, size));
                return n;
            }
        }

        private readonly struct TypeOption
        {
            public readonly string Label;
            public readonly string TypeName;

            public TypeOption(string label, string typeName)
            {
                Label = label;
                TypeName = typeName;
            }
        }

        private string _assetPath;
        private List<MessageDefinition> _messages = new List<MessageDefinition>();

        private readonly TypeOption[] _commonTypeOptions =
        {
            new TypeOption("int", "int"),
            new TypeOption("float", "float"),
            new TypeOption("double", "double"),
            new TypeOption("bool", "bool"),
            new TypeOption("string", "string"),
            new TypeOption("long", "long"),
            new TypeOption("Vector2", "UnityEngine.Vector2"),
            new TypeOption("Vector3", "UnityEngine.Vector3"),
            new TypeOption("Vector4", "UnityEngine.Vector4"),
            new TypeOption("Quaternion", "UnityEngine.Quaternion"),
            new TypeOption("Color", "UnityEngine.Color"),
            new TypeOption("GameObject", "UnityEngine.GameObject"),
            new TypeOption("Transform", "UnityEngine.Transform"),
        };

        private string[] _allTypeDisplay = Array.Empty<string>();
        private string[] _allTypeFullName = Array.Empty<string>();
        private string[] _commonTypeLabels;

        private bool _useSearchType;
        private Vector2 _paramsScroll;
        private int _selectedMessageIndex = -1;

        private GUIStyle _previewStyle;

        internal static void ShowForAsset(string assetPath)
        {
            var w = GetWindow<MessageGeneratorWindow>(true, "Message Generator", true);
            w._assetPath = assetPath;
            w.minSize = new Vector2(620, 560);
            w.maxSize = new Vector2(10000, 10000);
            w.Initialize();
            w.Show();
        }

        private void Initialize()
        {
            _messages.Clear();
            _messages.Add(new MessageDefinition());
            _selectedMessageIndex = 0;
            _useSearchType = false;
            BuildAllTypesCacheBestEffort();
        }

        private void OnGUI()
        {
            EnsureStyles();

            EditorGUILayout.LabelField("Target", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("File", _assetPath ?? string.Empty);

            EditorGUILayout.Space(8);
            DrawMessageList();
            EditorGUILayout.Space(8);

            var selectedMsg = _selectedMessageIndex >= 0 && _selectedMessageIndex < _messages.Count
                ? _messages[_selectedMessageIndex]
                : null;

            if (selectedMsg != null)
                DrawMessageEditor(selectedMsg);

            EditorGUILayout.Space(6);

            if (selectedMsg != null)
                DrawPreview(selectedMsg);

            EditorGUILayout.Space(10);

            using (new EditorGUI.DisabledScope(!CanGenerateAll()))
            {
                if (GUILayout.Button($"Generate All ({_messages.Count} message{(_messages.Count == 1 ? "" : "s")})", GUILayout.Height(34)))
                    GenerateAllIntoFile();
            }
        }

        private void EnsureStyles()
        {
            if (_previewStyle != null) return;
            _previewStyle = new GUIStyle(EditorStyles.helpBox)
            {
                fontSize = 11,
                wordWrap = false,
                richText = false,
                padding = new RectOffset(8, 8, 6, 6),
            };
        }

        private void DrawMessageList()
        {
            EditorGUILayout.LabelField("Messages", EditorStyles.boldLabel);

            using (new EditorGUILayout.HorizontalScope())
            {
                var messageNames = _messages.Select((m, i) =>
                    string.IsNullOrWhiteSpace(m.Name) ? $"<unnamed {i + 1}>" : m.Name).ToArray();

                var newIndex = EditorGUILayout.Popup("Current", _selectedMessageIndex, messageNames);
                if (newIndex != _selectedMessageIndex)
                    _selectedMessageIndex = newIndex;

                if (GUILayout.Button("+", GUILayout.Width(30)))
                {
                    _messages.Add(new MessageDefinition());
                    _selectedMessageIndex = _messages.Count - 1;
                }

                using (new EditorGUI.DisabledScope(_messages.Count <= 1))
                {
                    if (GUILayout.Button("-", GUILayout.Width(30)) && _messages.Count > 1)
                    {
                        _messages.RemoveAt(_selectedMessageIndex);
                        _selectedMessageIndex = Mathf.Clamp(_selectedMessageIndex, 0, _messages.Count - 1);
                    }
                }
            }

            EditorGUILayout.Space(4);
        }

        private void DrawMessageEditor(MessageDefinition msg)
        {
            EditorGUILayout.LabelField("Message Details", EditorStyles.boldLabel);

            msg.Name = EditorGUILayout.TextField("Name", msg.Name ?? string.Empty);
            msg.Comment = EditorGUILayout.TextField("Comment (optional)", msg.Comment ?? string.Empty);

            using (new EditorGUILayout.HorizontalScope())
            {
                var newCount = EditorGUILayout.IntSlider("Parameters", msg.ParamCount, 0, 12);
                if (newCount != msg.ParamCount)
                {
                    msg.ParamCount = newCount;
                    msg.ResizeParamArrays();
                }

                if (GUILayout.Button("Re-scan types", GUILayout.Width(110)))
                    BuildAllTypesCacheBestEffort();
            }

            msg.UsePayload = EditorGUILayout.ToggleLeft("Use payload struct (fields + ctor)", msg.UsePayload);
            _useSearchType = EditorGUILayout.ToggleLeft("Use type search (slower UI; uses cached list)", _useSearchType);

            if (!IsValidIdentifier(msg.Name))
                EditorGUILayout.HelpBox("Message name must be a valid C# identifier.", MessageType.Error);

            if (msg.ParamCount > 0)
            {
                EditorGUILayout.Space(6);
                _paramsScroll = EditorGUILayout.BeginScrollView(_paramsScroll, GUILayout.MaxHeight(160));

                for (int i = 0; i < msg.ParamCount; i++)
                {
                    DrawParamRow(msg, i);
                    EditorGUILayout.Space(4);
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawParamRow(MessageDefinition msg, int index)
        {
            EditorGUILayout.LabelField($"Param {index + 1}", EditorStyles.miniBoldLabel);

            if (_useSearchType)
            {
                if (_allTypeDisplay.Length == 0)
                {
                    EditorGUILayout.HelpBox("Type cache is empty. Click 'Re-scan types'.", MessageType.Warning);
                    return;
                }

                using (new EditorGUILayout.HorizontalScope())
                {
                    msg.SelectedAllTypeIndexByParam[index] = EditorGUILayout.Popup("Type", msg.SelectedAllTypeIndexByParam[index], _allTypeDisplay);
                    msg.ParamNameByParam[index] = EditorGUILayout.TextField("Name", msg.ParamNameByParam[index] ?? string.Empty, GUILayout.Width(160));
                }

                return;
            }

            if (_commonTypeLabels == null)
                _commonTypeLabels = _commonTypeOptions.Select(t => t.Label).ToArray();

            using (new EditorGUILayout.HorizontalScope())
            {
                msg.SelectedCommonTypeIndexByParam[index] = EditorGUILayout.Popup("Type", msg.SelectedCommonTypeIndexByParam[index], _commonTypeLabels);
                msg.ParamNameByParam[index] = EditorGUILayout.TextField("Name", msg.ParamNameByParam[index] ?? string.Empty, GUILayout.Width(160));
            }

            msg.CustomTypeNameByParam[index] = EditorGUILayout.TextField("Custom type (optional)", msg.CustomTypeNameByParam[index] ?? string.Empty);

            if (!string.IsNullOrWhiteSpace(msg.CustomTypeNameByParam[index]))
                EditorGUILayout.HelpBox("Custom type expects a resolvable C# type name (e.g. MyNamespace.MyType).", MessageType.None);
        }

        private void DrawPreview(MessageDefinition msg)
        {
            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            EditorGUILayout.TextArea(BuildPreviewCode(msg), _previewStyle,
                GUILayout.MinHeight(70), GUILayout.ExpandWidth(true));
        }

        private string BuildPreviewCode(MessageDefinition msg)
        {
            if (!IsValidIdentifier(msg.Name))
                return "// Enter a valid message name to see a preview.";

            var types = ResolveParamTypes(msg);
            var paramNames = ResolveParamNames(msg);

            var code = msg.UsePayload
                ? GeneratePayloadStruct(msg.Name, types, paramNames)
                : GenerateGenericMarkerStruct(msg.Name, types);

            if (!string.IsNullOrWhiteSpace(msg.Comment))
                code = "// " + msg.Comment.Trim() + "\n" + code;

            return code;
        }

        private bool CanGenerateAll()
        {
            if (string.IsNullOrWhiteSpace(_assetPath))
                return false;

            foreach (var msg in _messages)
                if (!IsValidIdentifier(msg.Name))
                    return false;

            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var msg in _messages)
                if (!names.Add(msg.Name))
                    return false;

            return true;
        }

        private void GenerateAllIntoFile()
        {
            var fullDiskPath = Path.GetFullPath(_assetPath);
            if (!File.Exists(fullDiskPath))
            {
                EditorUtility.DisplayDialog("Missing file", "Selected file does not exist on disk.", "OK");
                return;
            }

            var original = File.ReadAllText(fullDiskPath);

            foreach (var msg in _messages)
            {
                if (original.Contains("struct " + msg.Name, StringComparison.Ordinal) ||
                    original.Contains("class " + msg.Name, StringComparison.Ordinal))
                {
                    EditorUtility.DisplayDialog("Message exists", $"File already contains a type named '{msg.Name}'.", "OK");
                    return;
                }
            }

            original = EnsureUsingMvcExpress(original);

            var sb = new StringBuilder();
            foreach (var msg in _messages)
            {
                if (sb.Length > 0)
                    sb.AppendLine();
                sb.Append(GenerateMessageStructCode(original, msg));
            }

            var updated = InsertIntoFile(original, sb.ToString());
            File.WriteAllText(fullDiskPath, updated.Replace("\r\n", "\n").Replace("\r", "\n"));
            AssetDatabase.Refresh();

            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(_assetPath);
            Close();
        }

        private string GenerateMessageStructCode(string existingFile, MessageDefinition msg)
        {
            var ns = TryResolveNamespace(existingFile);
            var types = ResolveParamTypes(msg);
            var paramNames = ResolveParamNames(msg);

            var messageBlock = msg.UsePayload
                ? GeneratePayloadStruct(msg.Name, types, paramNames)
                : GenerateGenericMarkerStruct(msg.Name, types);

            var header = string.IsNullOrWhiteSpace(msg.Comment) ? string.Empty : "// " + msg.Comment.Trim() + "\n";

            var sb = new StringBuilder();
            sb.Append(header);

            // File-scoped namespaces (namespace Foo;) have no braces — the struct is
            // implicitly inside the namespace when appended, so no wrapper is needed.
            if (!string.IsNullOrWhiteSpace(ns) && !IsFileScopedNamespace(existingFile))
            {
                sb.Append("namespace ").Append(ns).AppendLine();
                sb.AppendLine("{");
                sb.Append(IndentBlock(messageBlock, "    "));
                sb.AppendLine("}");
            }
            else
            {
                sb.Append(messageBlock.TrimEnd());
                sb.AppendLine();
            }

            return sb.ToString().TrimEnd();
        }

        private string[] ResolveParamTypes(MessageDefinition msg)
        {
            if (msg.ParamCount == 0)
                return Array.Empty<string>();

            var types = new string[msg.ParamCount];
            for (int i = 0; i < msg.ParamCount; i++)
            {
                if (_useSearchType)
                {
                    var idx = Mathf.Clamp(msg.SelectedAllTypeIndexByParam[i], 0, Math.Max(0, _allTypeFullName.Length - 1));
                    types[i] = _allTypeFullName.Length == 0 ? "object" : _allTypeFullName[idx];
                }
                else
                {
                    var common = _commonTypeOptions[Mathf.Clamp(msg.SelectedCommonTypeIndexByParam[i], 0, _commonTypeOptions.Length - 1)].TypeName;
                    var custom = (msg.CustomTypeNameByParam[i] ?? string.Empty).Trim();
                    types[i] = string.IsNullOrWhiteSpace(custom) ? common : custom;
                }
            }

            return types;
        }

        private static string[] ResolveParamNames(MessageDefinition msg)
        {
            if (msg.ParamCount == 0)
                return Array.Empty<string>();

            var names = new string[msg.ParamCount];
            for (int i = 0; i < msg.ParamCount; i++)
            {
                var raw = (msg.ParamNameByParam[i] ?? string.Empty).Trim();
                names[i] = IsValidIdentifier(raw) ? raw : null;
            }
            return names;
        }

        private static bool IsValidIdentifier(string s)
        {
            if (string.IsNullOrWhiteSpace(s))
                return false;
            if (!(char.IsLetter(s[0]) || s[0] == '_'))
                return false;
            for (int i = 1; i < s.Length; i++)
            {
                var c = s[i];
                if (!(char.IsLetterOrDigit(c) || c == '_'))
                    return false;
            }
            return true;
        }

        private static string EnsureUsingMvcExpress(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return "using mvcExpress;\n\n";

            if (code.Contains("using mvcExpress;", StringComparison.Ordinal))
                return code;

            var normalized = code.Replace("\r\n", "\n").Replace("\r", "\n");
            var lines = normalized.Split('\n');

            int lastUsingLine = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                var l = lines[i].TrimStart();
                if (l.StartsWith("using ", StringComparison.Ordinal))
                    lastUsingLine = i;
                else if (lastUsingLine >= 0)
                    break;
            }

            var list = lines.ToList();
            if (lastUsingLine >= 0)
            {
                list.Insert(lastUsingLine + 1, "using mvcExpress;");
            }
            else
            {
                // No using directives — insert at top, after blank lines and file header comments.
                int insertAt = 0;
                while (insertAt < list.Count)
                {
                    var l = list[insertAt].TrimStart();
                    if (l.Length == 0 || l.StartsWith("//") || l.StartsWith("/*") || l.StartsWith("*"))
                        insertAt++;
                    else
                        break;
                }
                list.Insert(insertAt, "using mvcExpress;");
            }

            return string.Join("\n", list);
        }

        private static string GenerateGenericMarkerStruct(string messageName, string[] types)
        {
            return $"public readonly struct {messageName} : {BuildMessageInterface(types)} {{ }}";
        }

        private static string GeneratePayloadStruct(string messageName, string[] types, string[] paramNames)
        {
            if (types == null || types.Length == 0)
                return $"public readonly struct {messageName} : IMessage {{ }}";

            var fields = BuildFields(types, paramNames);
            var ctor = BuildCtor(messageName, types, paramNames);

            var sb = new StringBuilder();
            sb.AppendLine($"public readonly struct {messageName} : IMessage");
            sb.AppendLine("{");
            sb.Append(IndentBlock(fields, "    "));
            sb.AppendLine();
            sb.Append(IndentBlock(ctor, "    "));
            sb.AppendLine("}");
            return sb.ToString().TrimEnd();
        }

        private static string BuildFields(string[] types, string[] paramNames)
        {
            if (types == null || types.Length == 0)
                return string.Empty;

            var sb = new StringBuilder();
            for (int i = 0; i < types.Length; i++)
                sb.Append("public readonly ").Append(ToCSharpFriendlyTypeName(types[i])).Append(' ').Append(FieldNameFor(paramNames, i)).AppendLine(";");

            return sb.ToString().TrimEnd();
        }

        private static string BuildCtor(string messageName, string[] types, string[] paramNames)
        {
            if (types == null || types.Length == 0)
                return string.Empty;

            var args = new List<string>(types.Length);
            for (int i = 0; i < types.Length; i++)
                args.Add(ToCSharpFriendlyTypeName(types[i]) + " " + ArgNameFor(paramNames, i));

            var sb = new StringBuilder();
            sb.Append("public ").Append(messageName).Append('(').Append(string.Join(", ", args)).AppendLine(")");
            sb.AppendLine("{");
            for (int i = 0; i < types.Length; i++)
                sb.Append("    ").Append(FieldNameFor(paramNames, i)).Append(" = ").Append(ArgNameFor(paramNames, i)).AppendLine(";");
            sb.AppendLine("}");
            return sb.ToString().TrimEnd();
        }

        // PascalCase public field: "speed" -> "Speed", null -> "Param1"
        private static string FieldNameFor(string[] names, int i)
        {
            var raw = names != null && i < names.Length ? names[i] : null;
            if (string.IsNullOrWhiteSpace(raw)) return "Param" + (i + 1);
            return char.ToUpperInvariant(raw[0]) + raw.Substring(1);
        }

        // camelCase constructor arg: "Speed" -> "speed", null -> "param1"
        private static string ArgNameFor(string[] names, int i)
        {
            var raw = names != null && i < names.Length ? names[i] : null;
            if (string.IsNullOrWhiteSpace(raw)) return "param" + (i + 1);
            return char.ToLowerInvariant(raw[0]) + raw.Substring(1);
        }

        private static string BuildMessageInterface(string[] types)
        {
            if (types == null || types.Length == 0) return "IMessage";
            return $"IMessage<{string.Join(", ", types.Select(ToCSharpFriendlyTypeName))}>";
        }

        private static string ToCSharpFriendlyTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return "object";
            return typeName switch
            {
                "System.Int32" => "int",
                "System.Single" => "float",
                "System.Double" => "double",
                "System.Boolean" => "bool",
                "System.String" => "string",
                "System.Int64" => "long",
                "System.Object" => "object",
                _ => typeName.Contains(".", StringComparison.Ordinal) ? typeName.Split('.').Last() : typeName,
            };
        }

        // Returns true for C# 10 file-scoped namespaces: "namespace Foo.Bar;" (no braces).
        private static bool IsFileScopedNamespace(string code)
        {
            var idx = code.IndexOf("namespace ", StringComparison.Ordinal);
            if (idx < 0) return false;
            idx += "namespace ".Length;
            while (idx < code.Length && char.IsWhiteSpace(code[idx])) idx++;
            while (idx < code.Length && (char.IsLetterOrDigit(code[idx]) || code[idx] == '_' || code[idx] == '.'))
                idx++;
            while (idx < code.Length && (code[idx] == ' ' || code[idx] == '\t')) idx++;
            return idx < code.Length && code[idx] == ';';
        }

        private static string InsertIntoFile(string originalFile, string block)
        {
            if (string.IsNullOrWhiteSpace(originalFile))
                return block + "\n";

            // File-scoped namespaces have no closing brace to insert before — just append.
            if (IsFileScopedNamespace(originalFile))
                return originalFile.TrimEnd() + "\n\n" + block.Trim() + "\n";

            var hasNamespace = originalFile.Contains("namespace ", StringComparison.Ordinal);
            if (!hasNamespace)
                return originalFile.TrimEnd() + "\n\n" + StripOuterNamespaceWrapper(block).Trim() + "\n";

            // Block-scoped namespace: insert the struct before the outermost closing brace.
            var content = originalFile;
            var lastNonWs = content.Length - 1;
            while (lastNonWs >= 0 && char.IsWhiteSpace(content[lastNonWs])) lastNonWs--;

            if (lastNonWs >= 0 && content[lastNonWs] == '}')
            {
                var innerBlock = IndentLines(StripOuterNamespaceWrapper(block).TrimEnd(), "    ");
                return content.Insert(lastNonWs, "\n\n" + innerBlock + "\n");
            }

            return originalFile.TrimEnd() + "\n\n" + block.Trim() + "\n";
        }

        private static string StripOuterNamespaceWrapper(string code)
        {
            var idxNs = code.IndexOf("namespace ", StringComparison.Ordinal);
            if (idxNs < 0) return code;
            if (IsFileScopedNamespace(code)) return code;

            var braceOpen = code.IndexOf('{', idxNs);
            if (braceOpen < 0) return code;

            var lastBrace = code.LastIndexOf('}');
            if (lastBrace < 0 || lastBrace <= braceOpen) return code;

            return code.Substring(braceOpen + 1, lastBrace - braceOpen - 1).Trim();
        }

        private static string IndentLines(string code, string indent)
        {
            var lines = code.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length > 0)
                    lines[i] = indent + lines[i];
            }
            return string.Join("\n", lines);
        }

        private static string IndentBlock(string code, string indent)
        {
            if (string.IsNullOrWhiteSpace(code)) return string.Empty;
            return IndentLines(code.TrimEnd(), indent) + "\n";
        }

        private static string TryResolveNamespace(string fileContents)
        {
            if (string.IsNullOrWhiteSpace(fileContents)) return null;

            var idx = fileContents.IndexOf("namespace ", StringComparison.Ordinal);
            if (idx < 0) return null;

            idx += "namespace ".Length;
            while (idx < fileContents.Length && char.IsWhiteSpace(fileContents[idx])) idx++;

            var start = idx;
            while (idx < fileContents.Length)
            {
                var c = fileContents[idx];
                // Stop at brace (block namespace), semicolon (file-scoped), or line end.
                if (c == '{' || c == ';' || c == '\r' || c == '\n') break;
                idx++;
            }

            var ns = fileContents.Substring(start, idx - start).Trim();
            return string.IsNullOrWhiteSpace(ns) ? null : ns;
        }

        private void BuildAllTypesCacheBestEffort()
        {
            try
            {
                var typeSet = new Dictionary<string, string>(StringComparer.Ordinal);
                var assemblies = CompilationPipeline.GetAssemblies();

                foreach (var asm in assemblies)
                {
                    var loaded = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, asm.name, StringComparison.Ordinal));
                    if (loaded == null) continue;

                    Type[] types;
                    try { types = loaded.GetTypes(); }
                    catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(x => x != null).ToArray(); }
                    catch { continue; }

                    foreach (var t in types)
                    {
                        if (t == null || t.IsGenericTypeDefinition || t.IsNested) continue;
                        var full = t.FullName;
                        if (!string.IsNullOrWhiteSpace(full) && !typeSet.ContainsKey(full))
                            typeSet.Add(full, full);
                    }
                }

                foreach (var c in _commonTypeOptions)
                    typeSet[c.TypeName] = c.TypeName;

                _allTypeFullName = typeSet.Keys.OrderBy(s => s, StringComparer.Ordinal).ToArray();
                _allTypeDisplay = _allTypeFullName;
            }
            catch
            {
                _allTypeFullName = Array.Empty<string>();
                _allTypeDisplay = Array.Empty<string>();
            }

            foreach (var msg in _messages)
            {
                for (int i = 0; i < msg.SelectedAllTypeIndexByParam.Length; i++)
                    msg.SelectedAllTypeIndexByParam[i] = Mathf.Clamp(msg.SelectedAllTypeIndexByParam[i], 0, Math.Max(0, _allTypeFullName.Length - 1));
            }
        }
    }
}

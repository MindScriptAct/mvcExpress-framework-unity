using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace mvcExpress.Editor.Generators
{
    /// <summary>
    /// Editor window that generates a C# interface file from the selected members of an existing type.
    /// </summary>
    internal sealed class InterfaceFromTypeGeneratorWindow : EditorWindow
    {
        private sealed class MemberRow
        {
            public MemberInfo Member;
            public bool Selected;
            public bool Getter;
            public bool Setter;
        }

        private string _assetPath;
        private string _typeName;
        private Type _type;

        private bool _externalFile;
        private string _interfaceName;
        private bool _readOnlyPostfix;
        private bool _interfacePublic;

        private Vector2 _scroll;
        private readonly List<MemberRow> _rows = new();

        private readonly List<Type> _typeCandidates = new();
        private int _selectedTypeIndex;

        internal static void ShowForAsset(string assetPath)
        {
            var w = GetWindow<InterfaceFromTypeGeneratorWindow>(true, "Interface Generator", true);
            w._assetPath = assetPath;
            w.minSize = new Vector2(600, 460);
            w.maxSize = new Vector2(10000, 10000);
            w.Initialize();
            w.Show();
        }

        private void Initialize()
        {
            _rows.Clear();
            _typeCandidates.Clear();
            _selectedTypeIndex = 0;
            _type = null;
            _typeName = null;

            if (string.IsNullOrWhiteSpace(_assetPath))
                return;

            _typeName = System.IO.Path.GetFileNameWithoutExtension(_assetPath);

            _typeCandidates.AddRange(FindTypesByMonoScript(_assetPath));

            if (_typeCandidates.Count == 0)
                _typeCandidates.AddRange(FindTypesByScriptFileName(_assetPath));

            if (_typeCandidates.Count == 0)
            {
                var t = FindTypeByName(_typeName);
                if (t != null) _typeCandidates.Add(t);
            }

            _type = _typeCandidates.Count > 0 ? _typeCandidates[0] : null;

            if (_type == null)
                return;

            _interfaceName = "I" + _type.Name;
            _readOnlyPostfix = false;
            BuildRows();
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Source", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.TextField("File", _assetPath ?? string.Empty);

            if (_typeCandidates.Count > 1)
            {
                var names = _typeCandidates.Select(t => t.FullName).ToArray();
                var newIndex = EditorGUILayout.Popup("Type", _selectedTypeIndex, names);
                if (newIndex != _selectedTypeIndex)
                {
                    _selectedTypeIndex = newIndex;
                    _type = _typeCandidates[_selectedTypeIndex];
                    _interfaceName = "I" + _type.Name;
                    BuildRows();
                }
            }
            else
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.TextField("Type", _type != null ? _type.FullName : "<not found>");
            }

            if (_type == null)
            {
                EditorGUILayout.HelpBox(
                    "Could not resolve a compiled Type for the selected file.\n\n" +
                    "Reflection mode requires the script to compile. If the file name does not match the class name, use the Type dropdown (when available) or rename the file/class.",
                    MessageType.Warning);

                if (GUILayout.Button("Re-scan"))
                    Initialize();
                return;
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Settings", EditorStyles.boldLabel);

            _externalFile = EditorGUILayout.ToggleLeft("External file", _externalFile);
            _interfaceName = EditorGUILayout.TextField("Interface name", _interfaceName ?? string.Empty);

            _readOnlyPostfix = EditorGUILayout.ToggleLeft("Add ReadOnly postfix", _readOnlyPostfix);
            if (_readOnlyPostfix)
            {
                if (!string.IsNullOrWhiteSpace(_interfaceName) && !_interfaceName.EndsWith("ReadOnly", StringComparison.Ordinal))
                {
                    _interfaceName += "ReadOnly";
                    GUI.FocusControl(null);
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(_interfaceName) && _interfaceName.EndsWith("ReadOnly", StringComparison.Ordinal))
                {
                    _interfaceName = _interfaceName.Substring(0, _interfaceName.Length - "ReadOnly".Length);
                    GUI.FocusControl(null);
                }
            }

            _interfacePublic = EditorGUILayout.ToggleLeft("Public visibility (default: internal)", _interfacePublic);

            if (string.IsNullOrWhiteSpace(_interfaceName) || !IsValidIdentifier(_interfaceName))
                EditorGUILayout.HelpBox("Interface name must be a valid C# identifier.", MessageType.Error);

            EditorGUILayout.Space(8);
            DrawSelectionButtons();

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Members", EditorStyles.boldLabel);

            if (_rows.Count == 0)
                EditorGUILayout.HelpBox("No public instance methods/properties found on this type.", MessageType.Info);

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var row in _rows)
                DrawRow(row);
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space(10);

            using (new EditorGUI.DisabledScope(!CanGenerate()))
            {
                if (GUILayout.Button("Generate", GUILayout.Height(34)))
                    Generate();
            }
        }

        private void DrawSelectionButtons()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select none"))
            {
                foreach (var r in _rows) { r.Selected = false; r.Getter = false; r.Setter = false; }
            }
            if (GUILayout.Button("Select all"))
            {
                foreach (var r in _rows)
                {
                    r.Selected = true;
                    if (r.Member is PropertyInfo) { r.Getter = true; r.Setter = true; }
                }
            }
            if (GUILayout.Button("Select getters"))
            {
                foreach (var r in _rows)
                {
                    if (r.Member is PropertyInfo) { r.Selected = true; r.Getter = true; }
                    else if (r.Member is MethodInfo mi && mi.ReturnType != typeof(void)) r.Selected = true;
                }
            }
            if (GUILayout.Button("Select setters"))
            {
                foreach (var r in _rows)
                {
                    if (r.Member is PropertyInfo) { r.Selected = true; r.Setter = true; }
                    else if (r.Member is MethodInfo mi && mi.ReturnType == typeof(void)) r.Selected = true;
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawRow(MemberRow row)
        {
            if (row.Member is MethodInfo mi)
            {
                EditorGUILayout.BeginHorizontal();
                row.Selected = EditorGUILayout.Toggle(row.Selected, GUILayout.Width(18));
                EditorGUILayout.LabelField(FormatMethodSignature(mi), EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
                return;
            }

            if (row.Member is PropertyInfo pi)
            {
                EditorGUILayout.BeginHorizontal();
                row.Selected = EditorGUILayout.Toggle(row.Selected, GUILayout.Width(18));
                EditorGUILayout.LabelField(FormatPropertyHeader(pi), EditorStyles.miniLabel);
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!row.Selected || pi.GetMethod == null || !pi.GetMethod.IsPublic))
                    row.Getter = GUILayout.Toggle(row.Getter, "get", GUILayout.Width(40));

                using (new EditorGUI.DisabledScope(!row.Selected || pi.SetMethod == null || !pi.SetMethod.IsPublic))
                    row.Setter = GUILayout.Toggle(row.Setter, "set", GUILayout.Width(40));

                EditorGUILayout.EndHorizontal();
            }
        }

        private bool CanGenerate()
        {
            if (_type == null) return false;
            if (string.IsNullOrWhiteSpace(_interfaceName) || !IsValidIdentifier(_interfaceName)) return false;
            return _rows.Any(r => r.Selected && (r.Member is MethodInfo || (r.Member is PropertyInfo && (r.Getter || r.Setter))));
        }

        private void Generate()
        {
            var ifaceCode = GenerateInterfaceCode();

            if (_externalFile)
            {
                var savePath = EditorUtility.SaveFilePanelInProject("Save interface", _interfaceName + ".cs", "cs", "Select location for interface file");
                if (string.IsNullOrEmpty(savePath))
                    return;

                if (System.IO.File.Exists(savePath))
                {
                    if (!EditorUtility.DisplayDialog("File exists", $"File already exists:\n{savePath}\n\nOverwrite?", "Overwrite", "Cancel"))
                        return;
                }

                System.IO.File.WriteAllText(savePath, ifaceCode.Replace("\r\n", "\n").Replace("\r", "\n"));
                AssetDatabase.Refresh();
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(savePath);
                Close();
                return;
            }

            var fullDiskPath = System.IO.Path.GetFullPath(_assetPath);
            var original = System.IO.File.ReadAllText(fullDiskPath);

            if (original.Contains("interface " + _interfaceName, StringComparison.Ordinal))
            {
                if (!EditorUtility.DisplayDialog("Interface exists", $"File already contains interface '{_interfaceName}'.\n\nOverwrite it?", "Overwrite", "Cancel"))
                    return;

                original = RemoveExistingInterfaceBlock(original, _interfaceName);
            }

            var updated = AddInterfaceToClassDeclaration(original, _type, _interfaceName);
            updated = InsertInterfaceAsSibling(updated, ifaceCode);

            System.IO.File.WriteAllText(fullDiskPath, updated.Replace("\r\n", "\n").Replace("\r", "\n"));
            AssetDatabase.Refresh();

            Selection.activeObject = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(_assetPath);
            Close();
        }

        private static string RemoveExistingInterfaceBlock(string code, string interfaceName)
        {
            if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(interfaceName))
                return code;

            var marker = "interface " + interfaceName;
            var idx = code.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0) return code;

            // Walk back to start of the interface's line.
            var start = idx;
            while (start > 0 && code[start - 1] != '\n' && code[start - 1] != '\r')
                start--;

            // Include the preceding blank line (the separator we inserted) if present.
            if (start >= 1)
            {
                var prevChar = start - 1;
                if (prevChar >= 0 && code[prevChar] == '\n') prevChar--;
                if (prevChar >= 0 && code[prevChar] == '\r') prevChar--;
                var prevLineStart = prevChar;
                while (prevLineStart > 0 && code[prevLineStart - 1] != '\n' && code[prevLineStart - 1] != '\r')
                    prevLineStart--;
                if (string.IsNullOrWhiteSpace(code.Substring(prevLineStart, prevChar - prevLineStart + 1)))
                    start = prevLineStart;
            }

            // Find the opening brace.
            var braceOpen = code.IndexOf('{', idx);
            if (braceOpen < 0) return code;

            // Walk braces to find the matching close.
            int depth = 0, i = braceOpen;
            for (; i < code.Length; i++)
            {
                if (code[i] == '{') depth++;
                else if (code[i] == '}') { depth--; if (depth == 0) { i++; break; } }
            }
            if (i <= braceOpen) return code;

            // Consume trailing horizontal whitespace then at most one line ending.
            var end = i;
            while (end < code.Length && (code[end] == ' ' || code[end] == '\t')) end++;
            if (end < code.Length && code[end] == '\r') end++;
            if (end < code.Length && code[end] == '\n') end++;

            return code.Remove(start, end - start);
        }

        // Adds ": IFoo" (or ", IFoo") to the class/struct/record declaration line.
        // Handles generic type arity, 'where' constraints, and already-present interfaces.
        private static string AddInterfaceToClassDeclaration(string code, Type type, string interfaceName)
        {
            if (type == null || string.IsNullOrWhiteSpace(interfaceName))
                return code;

            // Strip generic arity suffix: Foo`1 -> Foo.
            var typeName = type.Name;
            var tick = typeName.IndexOf('`');
            if (tick >= 0) typeName = typeName.Substring(0, tick);

            // Find the declaration line — try class, struct, record.
            int tokenIdx = -1;
            foreach (var kw in new[] { "class ", "struct ", "record " })
            {
                var c = code.IndexOf(kw + typeName, StringComparison.Ordinal);
                if (c >= 0) { tokenIdx = c; break; }
            }
            if (tokenIdx < 0) return code;

            var lineStart = tokenIdx;
            while (lineStart > 0 && code[lineStart - 1] != '\n' && code[lineStart - 1] != '\r')
                lineStart--;

            var lineEnd = code.IndexOf('\n', tokenIdx);
            if (lineEnd < 0) lineEnd = code.Length;

            var line = code.Substring(lineStart, lineEnd - lineStart);

            // Already on this declaration line?
            if (line.Contains(interfaceName, StringComparison.Ordinal))
                return code;

            // Insert before 'where' keyword if present (outside angle brackets), otherwise at end of line.
            var whereIdx = FindTokenInLine(line, "where");
            string newLine;
            if (whereIdx >= 0)
            {
                var before = line.Substring(0, whereIdx).TrimEnd();
                var after = " " + line.Substring(whereIdx).TrimStart();
                newLine = before.Contains(':')
                    ? before + ", " + interfaceName + after
                    : before + " : " + interfaceName + after;
            }
            else
            {
                newLine = line.Contains(':')
                    ? line + ", " + interfaceName
                    : line + " : " + interfaceName;
            }

            return code.Substring(0, lineStart) + newLine + code.Substring(lineEnd);
        }

        // Finds a keyword token in a line, ignoring occurrences inside < > angle brackets.
        private static int FindTokenInLine(string line, string token)
        {
            int depth = 0;
            for (int i = 0; i <= line.Length - token.Length; i++)
            {
                if (line[i] == '<') { depth++; continue; }
                if (line[i] == '>') { depth--; continue; }
                if (depth != 0) continue;
                if (string.Compare(line, i, token, 0, token.Length, StringComparison.Ordinal) != 0) continue;
                var prevOk = i == 0 || (!char.IsLetterOrDigit(line[i - 1]) && line[i - 1] != '_');
                var nextOk = i + token.Length >= line.Length || (!char.IsLetterOrDigit(line[i + token.Length]) && line[i + token.Length] != '_');
                if (prevOk && nextOk) return i;
            }
            return -1;
        }

        private static string InsertInterfaceAsSibling(string originalFile, string ifaceCode)
        {
            // File-scoped namespaces have no closing brace to insert before — just append.
            if (IsFileScopedNamespace(originalFile))
                return originalFile.TrimEnd() + "\n\n" + StripOuterNamespaceWrapper(ifaceCode).Trim() + "\n";

            var hasNamespace = originalFile.Contains("namespace ", StringComparison.Ordinal);

            if (!hasNamespace)
                return originalFile.TrimEnd() + "\n\n" + StripOuterNamespaceWrapper(ifaceCode).Trim() + "\n";

            // Block-scoped namespace: insert before the outermost closing brace.
            var content = originalFile;
            var lastNonWs = content.Length - 1;
            while (lastNonWs >= 0 && char.IsWhiteSpace(content[lastNonWs])) lastNonWs--;

            if (lastNonWs >= 0 && content[lastNonWs] == '}')
            {
                var innerIface = IndentLines(StripOuterNamespaceWrapper(ifaceCode).TrimEnd(), "    ");
                return content.Insert(lastNonWs, "\n\n" + innerIface + "\n");
            }

            return originalFile.TrimEnd() + "\n\n" + ifaceCode.Trim() + "\n";
        }

        private static string StripOuterNamespaceWrapper(string ifaceCode)
        {
            var idxNs = ifaceCode.IndexOf("namespace ", StringComparison.Ordinal);
            if (idxNs < 0) return ifaceCode;
            if (IsFileScopedNamespace(ifaceCode)) return ifaceCode;

            var braceOpen = ifaceCode.IndexOf('{', idxNs);
            if (braceOpen < 0) return ifaceCode;

            var lastBrace = ifaceCode.LastIndexOf('}');
            if (lastBrace < 0 || lastBrace <= braceOpen) return ifaceCode;

            return ifaceCode.Substring(braceOpen + 1, lastBrace - braceOpen - 1).Trim();
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

        private string GenerateInterfaceCode()
        {
            var ns = _type.Namespace;
            var visibility = _interfacePublic ? "public" : "internal";
            var sb = new StringBuilder();

            sb.AppendLine("// Generated by mvcExpress Interface Generator");

            if (!string.IsNullOrWhiteSpace(ns))
            {
                sb.Append("namespace ").Append(ns).AppendLine();
                sb.AppendLine("{");
                sb.AppendLine($"    {visibility} interface {_interfaceName}");
                sb.AppendLine("    {");
                AppendMembers(sb, indent: "        ");
                sb.AppendLine("    }");
                sb.AppendLine("}");
            }
            else
            {
                sb.AppendLine($"{visibility} interface {_interfaceName}");
                sb.AppendLine("{");
                AppendMembers(sb, indent: "    ");
                sb.AppendLine("}");
            }

            return sb.ToString();
        }

        private void AppendMembers(StringBuilder sb, string indent)
        {
            foreach (var row in _rows)
            {
                if (!row.Selected) continue;

                if (row.Member is MethodInfo mi)
                {
                    sb.Append(indent).Append(FormatMethodSignature(mi)).AppendLine(";");
                }
                else if (row.Member is PropertyInfo pi)
                {
                    var accessors = new List<string>(2);
                    if (row.Getter && pi.GetMethod != null && pi.GetMethod.IsPublic) accessors.Add("get;");
                    if (row.Setter && pi.SetMethod != null && pi.SetMethod.IsPublic) accessors.Add("set;");
                    if (accessors.Count == 0) continue;
                    sb.Append(indent).Append(FormatPropertyHeader(pi)).Append(" { ").Append(string.Join(" ", accessors)).AppendLine(" }");
                }
            }
        }

        private void BuildRows()
        {
            _rows.Clear();
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly;

            foreach (var p in _type.GetProperties(flags).OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (p.GetIndexParameters().Length != 0) continue;
                var hasPublicGetter = p.GetMethod != null && p.GetMethod.IsPublic;
                var hasPublicSetter = p.SetMethod != null && p.SetMethod.IsPublic;
                if (!hasPublicGetter && !hasPublicSetter) continue;
                _rows.Add(new MemberRow { Member = p, Selected = true, Getter = hasPublicGetter, Setter = hasPublicSetter });
            }

            foreach (var m in _type.GetMethods(flags).OrderBy(m => m.Name, StringComparer.Ordinal))
            {
                if (m.IsSpecialName) continue;
                if (IsUnityMessageMethod(m.Name)) continue;
                _rows.Add(new MemberRow { Member = m, Selected = true });
            }
        }

        private static bool IsUnityMessageMethod(string name) =>
            name is "Awake" or "Start" or "Update" or "FixedUpdate" or "LateUpdate"
                 or "OnEnable" or "OnDisable" or "OnDestroy";

        private static string FormatMethodSignature(MethodInfo mi)
        {
            var sb = new StringBuilder();
            sb.Append(FormatTypeName(mi.ReturnType)).Append(' ').Append(mi.Name);

            if (mi.IsGenericMethodDefinition)
            {
                var args = mi.GetGenericArguments();
                sb.Append('<').Append(string.Join(", ", args.Select(a => a.Name))).Append('>');
            }

            sb.Append('(');
            var ps = mi.GetParameters();
            for (int i = 0; i < ps.Length; i++)
            {
                if (i != 0) sb.Append(", ");
                var p = ps[i];
                if (p.IsOut) sb.Append("out ");
                else if (p.ParameterType.IsByRef) sb.Append("ref ");
                var pt = p.ParameterType.IsByRef ? p.ParameterType.GetElementType() : p.ParameterType;
                sb.Append(FormatTypeName(pt)).Append(' ').Append(p.Name);
            }
            sb.Append(')');
            return sb.ToString();
        }

        private static string FormatPropertyHeader(PropertyInfo pi) =>
            $"{FormatTypeName(pi.PropertyType)} {pi.Name}";

        private static string FormatTypeName(Type t)
        {
            if (t == null || t == typeof(void)) return "void";
            if (t == typeof(int)) return "int";
            if (t == typeof(float)) return "float";
            if (t == typeof(double)) return "double";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(string)) return "string";
            if (t == typeof(object)) return "object";
            if (t == typeof(byte)) return "byte";
            if (t == typeof(char)) return "char";
            if (t == typeof(long)) return "long";
            if (t == typeof(short)) return "short";
            if (t == typeof(uint)) return "uint";
            if (t == typeof(ulong)) return "ulong";
            if (t == typeof(ushort)) return "ushort";

            if (t.IsArray)
                return FormatTypeName(t.GetElementType()) + "[]";

            if (t.IsGenericType)
            {
                if (t.GetGenericTypeDefinition() == typeof(Nullable<>))
                    return FormatTypeName(t.GetGenericArguments()[0]) + "?";

                var defName = t.Name;
                var tick = defName.IndexOf('`');
                if (tick >= 0) defName = defName.Substring(0, tick);
                return defName + "<" + string.Join(", ", t.GetGenericArguments().Select(FormatTypeName)) + ">";
            }

            return t.Name;
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

        private static Type FindTypeByName(string simpleName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(x => x != null).ToArray(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (string.Equals(t.Name, simpleName, StringComparison.Ordinal))
                        return t;
                }
            }
            return null;
        }

        private static IEnumerable<Type> FindTypesByScriptFileName(string assetPath)
        {
            var className = System.IO.Path.GetFileNameWithoutExtension(assetPath);

            foreach (var asm in CompilationPipeline.GetAssemblies())
            {
                if (asm.sourceFiles == null) continue;

                for (int i = 0; i < asm.sourceFiles.Length; i++)
                {
                    var sf = asm.sourceFiles[i].Replace('\\', '/');
                    if (!string.Equals(sf, assetPath, StringComparison.OrdinalIgnoreCase)) continue;

                    var loaded = AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => string.Equals(a.GetName().Name, asm.name, StringComparison.Ordinal));
                    if (loaded == null) break;

                    Type[] types;
                    try { types = loaded.GetTypes(); }
                    catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(x => x != null).ToArray(); }

                    return types.Where(t => t != null && string.Equals(t.Name, className, StringComparison.Ordinal)).ToArray();
                }
            }

            var t2 = FindTypeByName(className);
            if (t2 != null) return new[] { t2 };
            return Array.Empty<Type>();
        }

        private static IEnumerable<Type> FindTypesByMonoScript(string assetPath)
        {
            var ms = AssetDatabase.LoadAssetAtPath<MonoScript>(assetPath);
            if (ms == null) return Array.Empty<Type>();
            var cls = ms.GetClass();
            if (cls == null) return Array.Empty<Type>();
            return new[] { cls };
        }

        private static bool IsValidIdentifier(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            if (!(char.IsLetter(s[0]) || s[0] == '_')) return false;
            for (int i = 1; i < s.Length; i++)
            {
                var c = s[i];
                if (!(char.IsLetterOrDigit(c) || c == '_')) return false;
            }
            return true;
        }
    }
}

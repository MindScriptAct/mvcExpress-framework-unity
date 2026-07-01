using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.Inspectors
{
    [CustomEditor(typeof(MonoBehaviour), true)]
    internal sealed class DebugCodeProxyEditor : UnityEditor.Editor
    {
        private bool _foldout = true;
        private bool _invokeFoldout = true;
        private bool _propertiesFoldout = true;

        private const int MaxInvokeParameterCount = 6;

        private System.Reflection.MethodInfo[] _cachedInvokeMethods;
        private string[] _cachedInvokeMethodNames;
        private int _selectedInvokeIndex;
        private object[] _invokeArgs;

        // ── Return value tree (services only) ────────────────────────────────
        private object _lastReturnValue;
        private string _lastReturnMethodName;
        private bool   _returnValueFoldout = true;
        private readonly Dictionary<string, bool> _rvFoldouts = new Dictionary<string, bool>();
        private Vector2 _returnValueScroll;
        private const int MaxTreeDepth   = 6;
        private const int MaxListItems   = 50;

        public override bool RequiresConstantRepaint()
        {
            return Application.isPlaying &&
                   (IsProxyDebugBehaviour(target) || IsServiceDebugBehaviour(target));
        }

        public override void OnInspectorGUI()
        {
            // ── Service debug path ────────────────────────────────────────────
            if (IsServiceDebugBehaviour(target))
            {
                if (!Application.isPlaying)
                {
                    EditorGUILayout.HelpBox("Enter play mode to inspect runtime service data.", MessageType.Info);
                    return;
                }

                var svc = GetServiceInstance(target);
                if (svc == null)
                {
                    EditorGUILayout.HelpBox("No service instance linked.", MessageType.Warning);
                    return;
                }

                var svcType = svc.GetType();
                DrawTopHeader(svc, svcType);
                DrawInternalStateIfAvailable(svc);

                _foldout = EditorGUILayout.Foldout(_foldout, "Fields", true);
                if (_foldout)
                {
                    EditorGUI.indentLevel++;
                    foreach (var f in svcType.GetFields(
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance))
                    {
                        if (f.Name.Contains("<") || f.Name.Contains(">")) continue;
                        DrawField(f.Name, f.FieldType, f.GetValue(svc), v => f.SetValue(svc, v));
                    }
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space();

                _propertiesFoldout = EditorGUILayout.Foldout(_propertiesFoldout, "Properties", true);
                if (_propertiesFoldout)
                {
                    EditorGUI.indentLevel++;
                    foreach (var p in svcType.GetProperties(
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance))
                    {
                        if (p.Name.Contains("<") || p.Name.Contains(">")) continue;
                        if (p.Name == "ModuleType" || IsLegacyModuleIdentifierProperty(p.Name)) continue;
                        if (p.GetIndexParameters().Length != 0) continue;
                        object value = null;
                        try { if (p.CanRead) value = p.GetValue(svc); } catch { value = "<exception>"; }
                        if (p.CanWrite)
                            DrawField(p.Name, p.PropertyType, value, v => { try { p.SetValue(svc, v); } catch { } });
                        else
                            EditorGUILayout.LabelField(p.Name, value != null ? value.ToString() : "null", EditorStyles.miniLabel);
                    }
                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space();
                DrawInvokeSectionWithReturnValue(svc, svcType);

                // ── Return value tree ─────────────────────────────────────────
                if (_lastReturnValue != null)
                {
                    EditorGUILayout.Space(6);
                    _returnValueFoldout = EditorGUILayout.Foldout(
                        _returnValueFoldout,
                        $"Return value  [{_lastReturnMethodName}]", true);

                    if (_returnValueFoldout)
                    {
                        _returnValueScroll = EditorGUILayout.BeginScrollView(
                            _returnValueScroll, GUILayout.MaxHeight(300));
                        _rvFoldouts.Clear();
                        DrawReturnValueTree(_lastReturnValue, "result", 0);
                        EditorGUILayout.EndScrollView();
                    }
                }

                return;
            }

            // ── Proxy debug path (original) ───────────────────────────────────
            if (!IsProxyDebugBehaviour(target))
            {
                base.OnInspectorGUI();
                return;
            }

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter play mode to inspect runtime proxy data.", MessageType.Info);
                return;
            }

            var proxy = GetProxyInstance(target);
            if (proxy == null)
            {
                EditorGUILayout.HelpBox("No proxy instance linked.", MessageType.Warning);
                return;
            }

            var proxyType = proxy.GetType();
            DrawTopHeader(proxy, proxyType);

            DrawInternalStateIfAvailable(proxy);

            // keep UI compact (header already provides separation)

            _foldout = EditorGUILayout.Foldout(_foldout, "Fields", true);
            if (_foldout)
            {
                EditorGUI.indentLevel++;

                var flags = System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Instance;

                var fields = proxyType.GetFields(flags);
                foreach (var field in fields)
                {
                    if (field.Name.Contains("<") || field.Name.Contains(">"))
                        continue;

                    DrawField(field.Name, field.FieldType, field.GetValue(proxy), v => field.SetValue(proxy, v));
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            _propertiesFoldout = EditorGUILayout.Foldout(_propertiesFoldout, "Properties", true);
            if (_propertiesFoldout)
            {
                EditorGUI.indentLevel++;

                var flags = System.Reflection.BindingFlags.Public |
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Instance;

                var props = proxyType.GetProperties(flags);
                foreach (var prop in props)
                {
                    if (prop.Name.Contains("<") || prop.Name.Contains(">"))
                        continue;

                    if (prop.Name == "ModuleType" || IsLegacyModuleIdentifierProperty(prop.Name))
                        continue;

                    if (prop.GetIndexParameters().Length != 0)
                        continue;

                    object value = null;
                    try
                    {
                        if (prop.CanRead)
                        {
                            value = prop.GetValue(proxy);
                        }
                    }
                    catch
                    {
                        value = "<exception>";
                    }

                    if (prop.CanWrite)
                    {
                        DrawField(prop.Name, prop.PropertyType, value, v =>
                        {
                            try
                            {
                                prop.SetValue(proxy, v);
                            }
                            catch
                            {
                                // ignore
                            }
                        });
                    }
                    else
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField(prop.Name, GUILayout.Width(160));
                        EditorGUILayout.LabelField(value != null ? value.ToString() : "null");
                        EditorGUILayout.EndHorizontal();
                    }
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            DrawInvokeSection(proxy, proxyType);
        }

        private static void DrawTopHeader(object proxy, System.Type proxyType)
        {
            var rect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            var bg = new GUIStyle("HelpBox") { padding = new RectOffset(4, 4, 2, 2) };
            GUI.Box(rect, GUIContent.none, bg);

            var contentRect = new Rect(rect.x + 4f, rect.y + 1f, rect.width - 8f, rect.height - 2f);

            var totalWidth = Mathf.Max(0f, contentRect.width);
            var rightWidth = Mathf.Clamp(totalWidth * 0.18f, 80f, 220f);
            var leftWidth = Mathf.Max(0f, totalWidth - rightWidth - 6f);

            var leftRect = new Rect(contentRect.x, contentRect.y, leftWidth, contentRect.height);
            var rightRect = new Rect(leftRect.xMax + 6f, contentRect.y, rightWidth, contentRect.height);

            var script = FindMonoScriptForType(proxyType);
            if (script != null)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUI.ObjectField(leftRect, script, typeof(MonoScript), false);
                EditorGUI.EndDisabledGroup();
            }
            else
            {
                EditorGUI.LabelField(leftRect, proxyType.Name, EditorStyles.boldLabel);
            }

            if (TryGetModuleName(proxy, out var moduleName))
            {
                var rightAligned = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
                EditorGUI.LabelField(rightRect, moduleName ?? "", rightAligned);
            }
        }

        private static MonoScript FindMonoScriptForType(System.Type type)
        {
            if (type == null)
                return null;

            var guids = AssetDatabase.FindAssets($"{type.Name} t:MonoScript");
            for (int i = 0; i < guids.Length; i++)
            {
                var path = AssetDatabase.GUIDToAssetPath(guids[i]);
                var script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null)
                    continue;

                var scriptClass = script.GetClass();
                if (scriptClass == type)
                    return script;
            }

            return null;
        }

        private static bool TryGetModuleName(object proxy, out string moduleName)
        {
            moduleName = null;
            if (proxy == null)
                return false;

            var prop = proxy.GetType().GetProperty(
                "ModuleType",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

            if (prop != null && prop.CanRead)
            {
                try
                {
                    var v = prop.GetValue(proxy);
                    if (v is System.Type moduleType)
                    {
                        moduleName = moduleType.Name;
                        return true;
                    }

                    if (v != null)
                    {
                        moduleName = v.ToString();
                        return true;
                    }
                }
                catch
                {
                    return false;
                }
            }

            return false;
        }

        private static bool IsLegacyModuleIdentifierProperty(string propertyName)
        {
            return string.Equals(propertyName, "Module" + "Id", System.StringComparison.Ordinal);
        }

        // ── Service invoke (captures return value) ────────────────────────────

        private void DrawInvokeSectionWithReturnValue(object service, System.Type serviceType)
        {
            _invokeFoldout = EditorGUILayout.Foldout(_invokeFoldout, "Methods", true);
            if (!_invokeFoldout) return;

            EditorGUI.indentLevel++;
            EnsureInvokeCache(serviceType);

            if (_cachedInvokeMethods == null || _cachedInvokeMethods.Length == 0)
            {
                EditorGUILayout.HelpBox("No invokable methods found.", MessageType.Info);
                EditorGUI.indentLevel--;
                return;
            }

            _selectedInvokeIndex = Mathf.Clamp(_selectedInvokeIndex, 0, _cachedInvokeMethods.Length - 1);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Method", GUILayout.Width(60));
                _selectedInvokeIndex = EditorGUILayout.Popup(_selectedInvokeIndex, _cachedInvokeMethodNames);
            }

            var method     = _cachedInvokeMethods[_selectedInvokeIndex];
            var parameters = method.GetParameters();

            if (_invokeArgs == null || _invokeArgs.Length != parameters.Length)
                _invokeArgs = new object[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
                _invokeArgs[i] = DrawParameter(parameters[i].Name, parameters[i].ParameterType, _invokeArgs[i]);

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Invoke"))
                {
                    try
                    {
                        var args   = ConvertArgs(parameters, _invokeArgs);
                        var result = method.Invoke(service, args);

                        if (method.ReturnType != typeof(void))
                        {
                            _lastReturnValue     = result;
                            _lastReturnMethodName = method.Name;
                            _rvFoldouts.Clear();
                            _returnValueFoldout = true;
                        }
                    }
                    catch (System.Exception ex) { Debug.LogException(ex); }
                }
            }

            EditorGUI.indentLevel--;
        }

        // ── Recursive return-value tree ───────────────────────────────────────

        private void DrawReturnValueTree(object value, string path, int depth)
        {
            if (depth > MaxTreeDepth)
            {
                EditorGUILayout.LabelField("(max depth)", EditorStyles.miniLabel);
                return;
            }

            if (value == null)
            {
                EditorGUILayout.LabelField("null", EditorStyles.miniLabel);
                return;
            }

            var type = value.GetType();

            // Primitives + string
            if (type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal))
            {
                EditorGUILayout.LabelField(value.ToString(), EditorStyles.miniLabel);
                return;
            }

            // Unity Objects (drag-and-drop display)
            if (value is UnityEngine.Object uObj)
            {
                using (new EditorGUI.DisabledScope(true))
                    EditorGUILayout.ObjectField(uObj, type, true);
                return;
            }

            // Collections
            if (value is System.Collections.IEnumerable enumerable)
            {
                int i = 0;
                foreach (var item in enumerable)
                {
                    var itemPath = $"{path}[{i}]";
                    _rvFoldouts.TryGetValue(itemPath, out bool open);
                    bool newOpen = EditorGUILayout.Foldout(open, $"[{i}]  {(item == null ? "null" : item.GetType().Name)}", true);
                    if (newOpen != open) _rvFoldouts[itemPath] = newOpen;
                    if (newOpen)
                    {
                        EditorGUI.indentLevel++;
                        DrawReturnValueTree(item, itemPath, depth + 1);
                        EditorGUI.indentLevel--;
                    }

                    if (++i >= MaxListItems)
                    {
                        EditorGUILayout.LabelField($"... (showing {MaxListItems} of more)", EditorStyles.miniLabel);
                        break;
                    }
                }
                return;
            }

            // Complex object — foldout with public fields and properties
            var displayName = $"{type.Name}: {value}";
            _rvFoldouts.TryGetValue(path, out bool expanded);
            // Auto-expand the root node
            if (depth == 0 && !_rvFoldouts.ContainsKey(path)) expanded = true;

            bool newExpanded = EditorGUILayout.Foldout(expanded, displayName, true);
            if (newExpanded != expanded) _rvFoldouts[path] = newExpanded;

            if (!newExpanded) return;

            EditorGUI.indentLevel++;

            // Fields
            foreach (var f in type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUILayout.LabelField(f.Name, GUILayout.Width(150));
                    DrawReturnValueTree(f.GetValue(value), $"{path}.{f.Name}", depth + 1);
                }
            }

            // Properties
            foreach (var p in type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;
                try
                {
                    var pv = p.GetValue(value);
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField(p.Name, GUILayout.Width(150));
                        DrawReturnValueTree(pv, $"{path}.{p.Name}", depth + 1);
                    }
                }
                catch { }
            }

            EditorGUI.indentLevel--;
        }

        // ── Proxy invoke (original — no return value capture) ─────────────────

        private void DrawInvokeSection(object proxy, System.Type proxyType)
        {
            _invokeFoldout = EditorGUILayout.Foldout(_invokeFoldout, "Methods", true);
            if (!_invokeFoldout)
            {
                return;
            }

            EditorGUI.indentLevel++;

            EnsureInvokeCache(proxyType);

            if (_cachedInvokeMethods == null || _cachedInvokeMethods.Length == 0)
            {
                EditorGUILayout.HelpBox("No invokable methods found.", MessageType.Info);
                EditorGUI.indentLevel--;
                return;
            }

            _selectedInvokeIndex = Mathf.Clamp(_selectedInvokeIndex, 0, _cachedInvokeMethods.Length - 1);

            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Method", GUILayout.Width(60));
                _selectedInvokeIndex = EditorGUILayout.Popup(_selectedInvokeIndex, _cachedInvokeMethodNames);
            }

            var method = _cachedInvokeMethods[_selectedInvokeIndex];
            var parameters = method.GetParameters();

            if (_invokeArgs == null || _invokeArgs.Length != parameters.Length)
            {
                _invokeArgs = new object[parameters.Length];
            }

            for (int i = 0; i < parameters.Length; i++)
            {
                var p = parameters[i];
                _invokeArgs[i] = DrawParameter(p.Name, p.ParameterType, _invokeArgs[i]);
            }

            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                if (GUILayout.Button("Invoke"))
                {
                    try
                    {
                        var args = ConvertArgs(parameters, _invokeArgs);
                        method.Invoke(proxy, args);
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }
            }

            EditorGUI.indentLevel--;
        }

        private void EnsureInvokeCache(System.Type proxyType)
        {
            if (_cachedInvokeMethods != null)
            {
                return;
            }

            var flags = System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Instance;

            var methods = proxyType.GetMethods(flags);

            var list = new System.Collections.Generic.List<System.Reflection.MethodInfo>(methods.Length);
            for (int i = 0; i < methods.Length; i++)
            {
                var m = methods[i];

                if (m.IsSpecialName)
                    continue;

                if (m.DeclaringType == typeof(object))
                    continue;

                if (m.ContainsGenericParameters)
                    continue;

                var ps = m.GetParameters();
                if (ps.Length > MaxInvokeParameterCount)
                    continue;

                bool supported = true;
                for (int p = 0; p < ps.Length; p++)
                {
                    if (!IsSupportedParameterType(ps[p].ParameterType))
                    {
                        supported = false;
                        break;
                    }
                }

                if (!supported)
                    continue;

                list.Add(m);
            }

            list.Sort((a, b) => string.Compare(GetMethodDisplayName(a), GetMethodDisplayName(b), System.StringComparison.Ordinal));

            _cachedInvokeMethods = list.ToArray();
            _cachedInvokeMethodNames = new string[_cachedInvokeMethods.Length];
            for (int i = 0; i < _cachedInvokeMethods.Length; i++)
            {
                _cachedInvokeMethodNames[i] = GetMethodDisplayName(_cachedInvokeMethods[i]);
            }

            _selectedInvokeIndex = Mathf.Clamp(_selectedInvokeIndex, 0, _cachedInvokeMethods.Length - 1);
        }

        private static string GetMethodDisplayName(System.Reflection.MethodInfo m)
        {
            var ps = m.GetParameters();
            if (ps == null || ps.Length == 0)
                return m.Name + "()";

            var sb = new System.Text.StringBuilder();
            sb.Append(m.Name);
            sb.Append('(');
            for (int i = 0; i < ps.Length; i++)
            {
                if (i > 0) sb.Append(", ");
                sb.Append(FriendlyTypeName(ps[i].ParameterType));
                sb.Append(' ');
                sb.Append(ps[i].Name);
            }
            sb.Append(')');
            return sb.ToString();
        }

        private static string FriendlyTypeName(System.Type t)
        {
            if (t == typeof(int)) return "int";
            if (t == typeof(float)) return "float";
            if (t == typeof(double)) return "double";
            if (t == typeof(long)) return "long";
            if (t == typeof(bool)) return "bool";
            if (t == typeof(string)) return "string";
            if (t == typeof(Vector2)) return "Vector2";
            if (t == typeof(Vector3)) return "Vector3";
            if (t == typeof(Color)) return "Color";
            if (t.IsEnum) return t.Name;
            return t.Name;
        }

        private static bool IsSupportedParameterType(System.Type t)
        {
            return t == typeof(int)
                || t == typeof(float)
                || t == typeof(double)
                || t == typeof(long)
                || t == typeof(bool)
                || t == typeof(string)
                || t == typeof(Vector2)
                || t == typeof(Vector3)
                || t == typeof(Color)
                || t.IsEnum;
        }

        private static object DrawParameter(string name, System.Type type, object value)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(name, GUILayout.Width(160));

            object newValue = value;

            if (type == typeof(int))
            {
                int cur = value is int v ? v : 0;
                newValue = EditorGUILayout.IntField(cur);
            }
            else if (type == typeof(float))
            {
                float cur = value is float v ? v : 0f;
                newValue = EditorGUILayout.FloatField(cur);
            }
            else if (type == typeof(double))
            {
                double cur = value is double v ? v : 0d;
                newValue = EditorGUILayout.DoubleField(cur);
            }
            else if (type == typeof(long))
            {
                long cur = value is long v ? v : 0L;
                newValue = EditorGUILayout.LongField(cur);
            }
            else if (type == typeof(string))
            {
                string cur = value as string ?? "";
                newValue = EditorGUILayout.TextField(cur);
            }
            else if (type == typeof(bool))
            {
                bool cur = value is bool v && v;
                newValue = EditorGUILayout.Toggle(cur);
            }
            else if (type == typeof(Vector2))
            {
                Vector2 cur = value is Vector2 v ? v : Vector2.zero;
                newValue = EditorGUILayout.Vector2Field("", cur);
            }
            else if (type == typeof(Vector3))
            {
                Vector3 cur = value is Vector3 v ? v : Vector3.zero;
                newValue = EditorGUILayout.Vector3Field("", cur);
            }
            else if (type == typeof(Color))
            {
                Color cur = value is Color v ? v : Color.white;
                newValue = EditorGUILayout.ColorField(cur);
            }
            else if (type.IsEnum)
            {
                System.Enum cur = value as System.Enum;
                if (cur == null)
                {
                    var vals = System.Enum.GetValues(type);
                    if (vals.Length > 0) cur = (System.Enum)vals.GetValue(0);
                }

                newValue = EditorGUILayout.EnumPopup(cur);
            }
            else
            {
                EditorGUILayout.LabelField($"Unsupported ({type.Name})");
            }

            EditorGUILayout.EndHorizontal();
            return newValue;
        }

        private static object[] ConvertArgs(System.Reflection.ParameterInfo[] parameters, object[] input)
        {
            if (parameters == null || parameters.Length == 0)
                return null;

            var result = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var t = parameters[i].ParameterType;
                var v = input[i];

                if (v == null)
                {
                    result[i] = t.IsValueType ? System.Activator.CreateInstance(t) : null;
                    continue;
                }

                if (t.IsInstanceOfType(v))
                {
                    result[i] = v;
                    continue;
                }

                result[i] = System.Convert.ChangeType(v, t);
            }

            return result;
        }

        private static void DrawInternalStateIfAvailable(object proxy)
        {
            var m = proxy.GetType().GetMethod(
                "GetDebugState",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

            if (m == null)
            {
                return;
            }

            object state = null;
            try
            {
                state = m.Invoke(proxy, null);
            }
            catch
            {
                return;
            }

            if (state == null)
            {
                return;
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Internal State", EditorStyles.boldLabel);
            EditorGUI.indentLevel++;

            var props = state.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            foreach (var p in props)
            {
                var v = p.GetValue(state);
                EditorGUILayout.LabelField(p.Name, v != null ? v.ToString() : "null", EditorStyles.miniLabel);
            }

            EditorGUI.indentLevel--;
        }

        private static bool IsProxyDebugBehaviour(Object obj)
        {
            return obj != null && obj.GetType().FullName == "mvcExpress.Internal.ProxyDebug.ProxyDebugBehaviour";
        }

        private static object GetProxyInstance(Object debugBehaviour)
        {
            var prop = debugBehaviour.GetType().GetProperty("ProxyInstance",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            return prop?.GetValue(debugBehaviour);
        }

        private static bool IsServiceDebugBehaviour(Object obj)
        {
            return obj != null && obj.GetType().FullName == "mvcExpress.Internal.ServiceDebug.ServiceDebugBehaviour";
        }

        private static object GetServiceInstance(Object debugBehaviour)
        {
            var prop = debugBehaviour.GetType().GetProperty("ServiceInstance",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
            return prop?.GetValue(debugBehaviour);
        }

        private static void DrawField(string name, System.Type type, object value, System.Action<object> set)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(name, GUILayout.Width(160));

            object newValue = value;
            bool changed = false;

            if (type == typeof(int))
            {
                int cur = value != null ? (int)value : 0;
                int next = EditorGUILayout.IntField(cur);
                if (next != cur) { newValue = next; changed = true; }
            }
            else if (type == typeof(float))
            {
                float cur = value != null ? (float)value : 0f;
                float next = EditorGUILayout.FloatField(cur);
                if (Mathf.Abs(next - cur) > 0.0001f) { newValue = next; changed = true; }
            }
            else if (type == typeof(string))
            {
                string cur = value as string ?? "";
                string next = EditorGUILayout.TextField(cur);
                if (next != cur) { newValue = next; changed = true; }
            }
            else if (type == typeof(bool))
            {
                bool cur = value != null && (bool)value;
                bool next = EditorGUILayout.Toggle(cur);
                if (next != cur) { newValue = next; changed = true; }
            }
            else if (type == typeof(Vector2))
            {
                Vector2 cur = value != null ? (Vector2)value : Vector2.zero;
                Vector2 next = EditorGUILayout.Vector2Field("", cur);
                if (next != cur) { newValue = next; changed = true; }
            }
            else if (type == typeof(Vector3))
            {
                Vector3 cur = value != null ? (Vector3)value : Vector3.zero;
                Vector3 next = EditorGUILayout.Vector3Field("", cur);
                if (next != cur) { newValue = next; changed = true; }
            }
            else if (type == typeof(Color))
            {
                Color cur = value != null ? (Color)value : Color.white;
                Color next = EditorGUILayout.ColorField(cur);
                if (next != cur) { newValue = next; changed = true; }
            }
            else if (type.IsEnum)
            {
                System.Enum cur = value as System.Enum;
                if (cur == null)
                {
                    var vals = System.Enum.GetValues(type);
                    if (vals.Length > 0) cur = (System.Enum)vals.GetValue(0);
                }

                var next = EditorGUILayout.EnumPopup(cur);
                if (!Equals(next, cur)) { newValue = next; changed = true; }
            }
            else
            {
                EditorGUILayout.LabelField(value?.ToString() ?? "null");
            }

            if (changed)
            {
                set(newValue);
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}

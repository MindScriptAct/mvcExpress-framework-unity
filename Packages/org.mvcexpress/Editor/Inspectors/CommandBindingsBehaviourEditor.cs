using mvcExpress.Editor.Core;
using mvcExpress.Internal.Commands;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace mvcExpress.Editor.Inspectors
{
    [CustomEditor(typeof(CommandBindingsBehaviour))]
    public sealed class CommandBindingsBehaviourEditor : UnityEditor.Editor
    {
        private struct RuntimeBindingRow
        {
            public readonly Type CommandType;
            public readonly Type MessageType;
            public readonly MvcCommandProcessor.CommandBindingMode Mode;
            public readonly int PoolCurrent;
            public readonly int PoolMax;

            public RuntimeBindingRow(Type commandType, Type messageType, MvcCommandProcessor.CommandBindingMode mode, int poolCurrent, int poolMax)
            {
                CommandType = commandType;
                MessageType = messageType;
                Mode = mode;
                PoolCurrent = poolCurrent;
                PoolMax = poolMax;
            }
        }

        private const string HeaderIconPath = "Packages/org.mvcexpress/Editor/Icons/mvc_command_registry_icon.png";
        private const string HeaderTitle = "Command bindings";

        private static Texture2D s_hierarchyIcon;
        private static bool s_hierarchyCallbackRegistered;

        [InitializeOnLoadMethod]
        private static void InitializeHierarchyIcon()
        {
            if (!s_hierarchyCallbackRegistered)
            {
#if UNITY_6000_4_OR_NEWER
                EditorApplication.hierarchyWindowItemByEntityIdOnGUI += OnHierarchyGUI;
#else
                EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
#endif
                s_hierarchyCallbackRegistered = true;
            }
            s_hierarchyIcon ??= AssetDatabase.LoadAssetAtPath<Texture2D>(HeaderIconPath);
        }

        private static void OnHierarchyGUI(
#if UNITY_6000_4_OR_NEWER
            EntityId entityId,
#else
            int instanceID,
#endif
            Rect selectionRect)
        {
            if (!MvcHierarchyUtils.ShowRegistryIcons) return;
            if (s_hierarchyIcon == null) return;
#if UNITY_6000_4_OR_NEWER
            var obj = EditorUtility.EntityIdToObject(entityId) as GameObject;
#else
            var obj = EditorUtility.InstanceIDToObject(instanceID) as GameObject;
#endif
            if (obj == null || obj.GetComponent<CommandBindingsBehaviour>() == null) return;
            GUI.DrawTexture(new Rect(MvcHierarchyUtils.GetRightEdge(selectionRect) - 16, selectionRect.y, 16, 16), s_hierarchyIcon, ScaleMode.ScaleToFit);
        }

        private Texture2D headerIcon;

        private SerializedProperty commandBindingsProperty;
        private ReorderableList bindingsList;

        private static GUIStyle s_listHeaderTitleStyle;
        private static GUIStyle s_columnHeaderStyle;

        private static readonly GUIContent CommandHeader = new GUIContent("Command", "Drag & drop a Command/Command<T...> script here.");
        private static readonly GUIContent MessageHeader = new GUIContent("Message", "Drag & drop a message (IMessage...) script here.");
        private static readonly GUIContent AsyncHeader = new GUIContent("Async", "Inferred: command derives from CommandAsync... (read-only)");
        private static readonly GUIContent PoolHeader = new GUIContent("Pool", "Pool size for this binding. 0 = default/no pooling override.");

        private List<Type> allCommands;
        private List<Type> messageTypes;

        private const float OpenButtonWidth = 18f;

        private static readonly GUIContent RuntimeHeaderTitle = new GUIContent("Runtime Command Bindings", "Command bindings currently active at runtime (Play Mode only)");
        private static readonly GUIContent RuntimeRunMessage = new GUIContent("Enter Play Mode to see the live list of currently bound command bindings.");

        private static readonly GUIContent RuntimeColCommand = new GUIContent("Command", "Bound command type");
        private static readonly GUIContent RuntimeColMessage = new GUIContent("Message", "Message type this command is bound to");
        private static readonly GUIContent RuntimeColMode = new GUIContent("Type", "Sync / Async");
        private static readonly GUIContent RuntimeColPool = new GUIContent("Pool", "Current/Max pool sizes. 0 = no pooling.");

        private static GUIStyle s_runtimeTableHeaderStyle;
        private static GUIStyle s_runtimeTableCellCenterStyle;

        private static readonly List<MvcCommandProcessor.CommandBindingSnapshot> s_runtimeSnapshot = new List<MvcCommandProcessor.CommandBindingSnapshot>(128);
        private static readonly List<RuntimeBindingRow> s_runtimeRows = new List<RuntimeBindingRow>(128);

        private double _nextRepaintTime;

        private void OnEnable()
        {
            commandBindingsProperty = serializedObject.FindProperty("_commandBindings");

            headerIcon = AssetDatabase.LoadAssetAtPath<Texture2D>(HeaderIconPath);

            var derived = MvcTypeCacheUtility.GetNonAbstractDerivedTypes<MvcCommandBase>();
            allCommands = new List<Type>(derived);
            allCommands.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));

            // Collect IMessage implementations for 0..12 params.
            var msgSet = new HashSet<Type>();
            AddMessageTypes(msgSet, TypeCache.GetTypesDerivedFrom<IMessage>());
            AddMessageTypes(msgSet, TypeCache.GetTypesDerivedFrom(typeof(IMessage<>)));
            AddMessageTypes(msgSet, TypeCache.GetTypesDerivedFrom(typeof(IMessage<,>)));
            AddMessageTypes(msgSet, TypeCache.GetTypesDerivedFrom(typeof(IMessage<,,>)));
            AddMessageTypes(msgSet, TypeCache.GetTypesDerivedFrom(typeof(IMessage<,,,>)));
            AddMessageTypes(msgSet, TypeCache.GetTypesDerivedFrom(typeof(IMessage<,,,,>)));
            AddMessageTypes(msgSet, TypeCache.GetTypesDerivedFrom(typeof(IMessage<,,,,,>)));
            AddMessageTypes(msgSet, TypeCache.GetTypesDerivedFrom(typeof(IMessage<,,,,,,>)));
            AddMessageTypes(msgSet, TypeCache.GetTypesDerivedFrom(typeof(IMessage<,,,,,,,>)));
            AddMessageTypes(msgSet, TypeCache.GetTypesDerivedFrom(typeof(IMessage<,,,,,,,,>)));
            AddMessageTypes(msgSet, TypeCache.GetTypesDerivedFrom(typeof(IMessage<,,,,,,,,,>)));
            AddMessageTypes(msgSet, TypeCache.GetTypesDerivedFrom(typeof(IMessage<,,,,,,,,,,>)));
            AddMessageTypes(msgSet, TypeCache.GetTypesDerivedFrom(typeof(IMessage<,,,,,,,,,,,>)));

            messageTypes = new List<Type>(msgSet);
            messageTypes.Sort((a, b) => string.CompareOrdinal(a.FullName, b.FullName));

            bindingsList = new ReorderableList(serializedObject, commandBindingsProperty, draggable: true, displayHeader: true, displayAddButton: true, displayRemoveButton: true);
            bindingsList.onAddCallback = list =>
            {
                serializedObject.Update();
                int idx = commandBindingsProperty.arraySize;
                commandBindingsProperty.InsertArrayElementAtIndex(idx);
                var el = commandBindingsProperty.GetArrayElementAtIndex(idx);
                el.FindPropertyRelative("CommandTypeName").stringValue = string.Empty;
                el.FindPropertyRelative("MessageTypeName").stringValue = string.Empty;

                // Kept for backward compatibility (runtime ignores it now), but keep the property present.
                el.FindPropertyRelative("IsAsync").boolValue = false;

                el.FindPropertyRelative("PoolSize").intValue = 0;

                var cmdScriptProp = el.FindPropertyRelative("CommandScript");
                if (cmdScriptProp != null) cmdScriptProp.objectReferenceValue = null;
                var msgScriptProp = el.FindPropertyRelative("MessageScript");
                if (msgScriptProp != null) msgScriptProp.objectReferenceValue = null;

                serializedObject.ApplyModifiedProperties();
            };

            bindingsList.headerHeight = EditorGUIUtility.singleLineHeight;
            bindingsList.drawHeaderCallback = rect =>
            {
                DrawHeaderRow(rect);
            };

            bindingsList.drawElementCallback = (rect, index, active, focused) =>
            {
                var el = commandBindingsProperty.GetArrayElementAtIndex(index);
                DrawRow(rect, el);
            };

            bindingsList.elementHeight = EditorGUIUtility.singleLineHeight + 6f;

            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            // Clean up ReorderableList to prevent memory leaks
            if (bindingsList != null)
            {
                bindingsList.drawElementCallback = null;
                bindingsList.drawHeaderCallback = null;
                bindingsList.onAddCallback = null;
                bindingsList = null;
            }

            // Clear cached data
            commandBindingsProperty = null;
            headerIcon = null;

            // Clear type lists
            allCommands?.Clear();
            messageTypes?.Clear();
            allCommands = null;
            messageTypes = null;

            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            if (this == null)
                return;

            if (!Application.isPlaying)
                return;

            if (EditorApplication.timeSinceStartup < _nextRepaintTime)
                return;

            _nextRepaintTime = EditorApplication.timeSinceStartup + 0.25;

            if (target != null)
                Repaint();
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawTopHeader();

            DrawRuntimeBindingsSection();

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                DrawSectionHeader();
                bindingsList?.DoLayoutList();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawTopHeader()
        {
            EditorGUILayout.Space(2f);

            using (new EditorGUILayout.HorizontalScope())
            {
                var iconSizeX = MvcEditorUtility.TopHeaderIconWidth;
                var iconSizeY = MvcEditorUtility.TopHeaderIconHeight;
                var iconRect = GUILayoutUtility.GetRect(iconSizeX, iconSizeY, GUILayout.Width(iconSizeX), GUILayout.Height(iconSizeY));

                if (headerIcon != null)
                {
                    GUI.DrawTexture(iconRect, headerIcon, ScaleMode.ScaleToFit);
                }

                GUILayout.Space(10f);
                GUILayout.Label(HeaderTitle, MvcEditorUtility.TopHeaderTitleStyle, GUILayout.Height(iconSizeY));
            }

            EditorGUILayout.Space(6f);
        }

        private void DrawSectionHeader()
        {
            if (s_listHeaderTitleStyle == null)
                s_listHeaderTitleStyle = new GUIStyle(MvcEditorUtility.SectionHeaderTitleStyle) { alignment = TextAnchor.MiddleLeft };

            float lineH = EditorGUIUtility.singleLineHeight;
            float headerH = (lineH * 2f) + (6f * 2f);

            var content = MvcEditorUtility.DrawHeaderBox(headerH, padX: 8f, padY: 6f);

            var titleRect = new Rect(content.x, content.y, content.width, content.height);
            var titleLine = new Rect(titleRect.x, titleRect.center.y - (lineH * 0.5f), titleRect.width, lineH);
            EditorGUI.LabelField(titleLine, $"Command Bindings ({commandBindingsProperty.arraySize})", s_listHeaderTitleStyle);

            EditorGUILayout.Space(2f);
        }

        private void DrawHeaderRow(Rect rect)
        {
            float w = rect.width;
            float cmdW = w * 0.48f;
            float msgW = w * 0.40f;
            float asyncW = 44f;
            float poolW = w - cmdW - msgW - asyncW - 8f;

            var cmdRect = new Rect(rect.x, rect.y, cmdW, rect.height);
            var msgRect = new Rect(rect.x + cmdW + 4f, rect.y, msgW, rect.height);
            var asyncRect = new Rect(rect.x + cmdW + 4f + msgW + 4f, rect.y, asyncW, rect.height);
            var poolRect = new Rect(asyncRect.x + asyncW + 4f, rect.y, poolW, rect.height);

            s_columnHeaderStyle ??= new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold };

            var cmdLabelRect = cmdRect;
            cmdLabelRect.xMax -= OpenButtonWidth + 2f;
            var msgLabelRect = msgRect;
            msgLabelRect.xMax -= OpenButtonWidth + 2f;

            EditorGUI.LabelField(cmdLabelRect, CommandHeader, s_columnHeaderStyle);
            EditorGUI.LabelField(msgLabelRect, MessageHeader, s_columnHeaderStyle);
            EditorGUI.LabelField(asyncRect, AsyncHeader, s_columnHeaderStyle);
            EditorGUI.LabelField(poolRect, PoolHeader, s_columnHeaderStyle);

            var openIcon = EditorGUIUtility.IconContent("d_editicon.sml", "Open type");
            var openStyle = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleCenter, fontStyle = FontStyle.Bold };

            var cmdOpenRect = cmdRect;
            cmdOpenRect.x = cmdLabelRect.xMax + 2f;
            cmdOpenRect.width = OpenButtonWidth;
            EditorGUI.LabelField(cmdOpenRect, openIcon, openStyle);

            var msgOpenRect = msgRect;
            msgOpenRect.x = msgLabelRect.xMax + 2f;
            msgOpenRect.width = OpenButtonWidth;
            EditorGUI.LabelField(msgOpenRect, openIcon, openStyle);
        }

        private void DrawRow(Rect rect, SerializedProperty element)
        {
            rect.y += 2f;
            rect.height = EditorGUIUtility.singleLineHeight;

            var cmdTypeProp = element.FindPropertyRelative("CommandTypeName");
            var msgTypeProp = element.FindPropertyRelative("MessageTypeName");
            var isAsyncProp = element.FindPropertyRelative("IsAsync");
            var poolProp = element.FindPropertyRelative("PoolSize");

            var cmdScriptProp = element.FindPropertyRelative("CommandScript");
            var msgScriptProp = element.FindPropertyRelative("MessageScript");

            float w = rect.width;
            float cmdW = w * 0.48f;
            float msgW = w * 0.40f;
            float asyncW = 44f;
            float poolW = w - cmdW - msgW - asyncW - 8f;

            var cmdRect = new Rect(rect.x, rect.y, cmdW, rect.height);
            var msgRect = new Rect(rect.x + cmdW + 4f, rect.y, msgW, rect.height);
            var asyncRect = new Rect(rect.x + cmdW + 4f + msgW + 4f, rect.y, asyncW, rect.height);
            var poolRect = new Rect(asyncRect.x + asyncW + 4f, rect.y, poolW, rect.height);

            // Commands: allow both sync and async commands. We infer async from the chosen type.
            DrawMonoScriptCell(cmdRect, cmdScriptProp, cmdTypeProp, allCommands, CommandHeader);

            // Messages: type dropdown
            DrawMessageTypeCellWithOpen(msgRect, msgTypeProp, msgScriptProp);

            var cmdType = GetTypeFromProperty(cmdTypeProp);

            // Inferred async indicator + keep IsAsync in sync for older serialized data.
            bool inferredAsync = cmdType != null && typeof(MvcAsyncCommandBase).IsAssignableFrom(cmdType);
            if (isAsyncProp != null && isAsyncProp.boolValue != inferredAsync)
                isAsyncProp.boolValue = inferredAsync;

            using (new EditorGUI.DisabledScope(true))
            {
                GUI.tooltip = AsyncHeader.tooltip;
                EditorGUI.Toggle(asyncRect, inferredAsync);
                GUI.tooltip = "";
            }

            GUI.tooltip = PoolHeader.tooltip;
            poolProp.intValue = EditorGUI.IntField(poolRect, poolProp.intValue);
            GUI.tooltip = "";
        }

        private void DrawMessageTypeCellWithOpen(Rect rect, SerializedProperty msgTypeProp, SerializedProperty msgScriptProp)
        {
            var buttonRect = rect;
            buttonRect.xMax -= OpenButtonWidth + 2f;

            var openRect = rect;
            openRect.x = buttonRect.xMax + 2f;
            openRect.width = OpenButtonWidth;

            // Type dropdown - detect changes
            EditorGUI.BeginChangeCheck();
            DrawTypeCell(buttonRect, msgTypeProp, messageTypes, "(select message)");

            // Only sync MessageScript when the type actually changes (not every frame!)
            if (EditorGUI.EndChangeCheck())
            {
                if (msgScriptProp != null)
                {
                    var msgType = GetTypeFromProperty(msgTypeProp);
                    MonoScript scriptForType = null;
                    if (msgType != null)
                    {
                        scriptForType = FindOrCacheMonoScriptForType(msgType);
                    }
                    msgScriptProp.objectReferenceValue = scriptForType;
                    msgScriptProp.serializedObject.ApplyModifiedProperties();
                }
            }

            // Get type for open button (lightweight - just reads the property)
            var currentType = GetTypeFromProperty(msgTypeProp);

            // Open button uses the selected type
            using (new EditorGUI.DisabledScope(currentType == null))
            {
                var content = EditorGUIUtility.IconContent("d_editicon.sml", "Open type");
                if (GUI.Button(openRect, content, GUIStyle.none))
                {
                    OpenTypeInEditor(currentType);
                }
            }
        }

        private static MonoScript FindOrCacheMonoScriptForType(Type type)
        {
            return MonoScriptCache.FindScriptForType(type);
        }

        private void DrawMonoScriptCell(Rect rect, SerializedProperty scriptProp, SerializedProperty typeNameProp, List<Type> allowedTypes, GUIContent label)
        {
            if (scriptProp == null)
            {
                DrawTypeCellWithOpen(rect, typeNameProp, allowedTypes, "(select type)");
                return;
            }

            EditorGUI.BeginProperty(rect, label, scriptProp);

            var prev = scriptProp.objectReferenceValue as MonoScript;
            var next = (MonoScript)EditorGUI.ObjectField(rect, prev, typeof(MonoScript), false);

            if (next != prev)
            {
                if (next == null)
                {
                    scriptProp.objectReferenceValue = null;
                    typeNameProp.stringValue = string.Empty;
                    scriptProp.serializedObject.ApplyModifiedProperties();
                }
                else
                {
                    if (!TryResolveAllowedTypeFromScript(next, allowedTypes, out var resolvedType, out var error))
                    {
                        EditorUtility.DisplayDialog("Invalid Script", error, "OK");
                    }
                    else
                    {
                        scriptProp.objectReferenceValue = next;
                        typeNameProp.stringValue = resolvedType.AssemblyQualifiedName;
                        scriptProp.serializedObject.ApplyModifiedProperties();
                    }
                }
            }
            else
            {
                // Keep string in sync if someone edited/merged yaml.
                if (prev != null && string.IsNullOrEmpty(typeNameProp.stringValue))
                {
                    if (TryResolveAllowedTypeFromScript(prev, allowedTypes, out var resolvedType, out _))
                    {
                        typeNameProp.stringValue = resolvedType.AssemblyQualifiedName;
                        scriptProp.serializedObject.ApplyModifiedProperties();
                    }
                }
            }

            EditorGUI.EndProperty();
        }

        private static bool TryResolveAllowedTypeFromScript(MonoScript script, List<Type> allowedTypes, out Type resolvedType, out string error)
        {
            resolvedType = null;
            error = null;

            if (script == null)
            {
                error = "No script selected.";
                return false;
            }

            // 1) Best case: Unity can tell us which class this script represents.
            var cls = script.GetClass();
            if (cls != null)
            {
                if (!IsAllowedType(cls, allowedTypes))
                {
                    error = $"Type '{cls.FullName}' is not valid for this field.";
                    return false;
                }

                resolvedType = cls;
                return true;
            }

            // 2) MonoScript.GetClass() commonly returns null when the file contains multiple types (e.g. many message structs).
            // In that case, pick a type from the file by scanning for declarations and matching against allowedTypes.
            var path = AssetDatabase.GetAssetPath(script);
            if (string.IsNullOrEmpty(path) || !path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                error = "Selected asset is not a C# script.";
                return false;
            }

            string source;
            try
            {
                source = File.ReadAllText(path);
            }
            catch
            {
                error = "Could not read script file.";
                return false;
            }

            var matches = new List<Type>();
            if (allowedTypes != null)
            {
                for (int i = 0; i < allowedTypes.Count; i++)
                {
                    var t = allowedTypes[i];
                    if (t == null)
                        continue;

                    // Check if the source contains this type by trying to resolve via cache
                    var foundScript = MonoScriptCache.FindScriptForType(t);
                    if (foundScript != null && AssetDatabase.GetAssetPath(foundScript) == path)
                        matches.Add(t);
                }
            }

            if (matches.Count == 0)
            {
                error = "Selected script does not contain a compatible type for this field.";
                return false;
            }

            if (matches.Count == 1)
            {
                resolvedType = matches[0];
                return true;
            }

            // Multiple compatible types in this file. Use a simple selector.
            var labels = new string[matches.Count];
            for (int i = 0; i < matches.Count; i++)
                labels[i] = matches[i].FullName;

            int chosen = EditorUtility.DisplayDialogComplex(
                "Multiple Types Found",
                "This script contains multiple compatible types. Choose which one to bind by selecting it in the type dropdown (tree) for this row.",
                "OK",
                "Cancel",
                "");

            if (chosen != 0)
            {
                error = "Selection canceled.";
                return false;
            }

            // Default to first match; user can refine via the existing type popup using the string field.
            resolvedType = matches[0];
            return true;
        }

        private static bool IsAllowedType(Type cls, List<Type> allowedTypes)
        {
            if (cls == null)
                return false;

            // Robust validation: accept any non-abstract derived command type.
            // The prebuilt lists are used for dropdown display, but equality checks can fail
            // in some Unity editor edge cases (domain reload/typecache timing). Use inheritance rules.
            if (typeof(MvcCommandBase).IsAssignableFrom(cls) && !cls.IsAbstract)
                return true;

            if (allowedTypes == null)
                return true;

            for (int i = 0; i < allowedTypes.Count; i++)
            {
                if (allowedTypes[i] == cls)
                    return true;
            }

            return false;
        }

        private static bool IsSyncCommandType(Type t)
        {
            if (t == null)
                return false;

            if (t == typeof(Command))
                return true;

            return InheritsFromGenericDefinition(t, typeof(Command<>))
                || InheritsFromGenericDefinition(t, typeof(Command<,>))
                || InheritsFromGenericDefinition(t, typeof(Command<,,>))
                || InheritsFromGenericDefinition(t, typeof(Command<,,,>))
                || InheritsFromGenericDefinition(t, typeof(Command<,,,,>))
                || InheritsFromGenericDefinition(t, typeof(Command<,,,,,>))
                || InheritsFromGenericDefinition(t, typeof(Command<,,,,,,>))
                || InheritsFromGenericDefinition(t, typeof(Command<,,,,,,,>))
                || InheritsFromGenericDefinition(t, typeof(Command<,,,,,,,,>))
                || InheritsFromGenericDefinition(t, typeof(Command<,,,,,,,,,>))
                || InheritsFromGenericDefinition(t, typeof(Command<,,,,,,,,,,>))
                || InheritsFromGenericDefinition(t, typeof(Command<,,,,,,,,,,,>));
        }

        private static bool IsAsyncCommandType(Type t)
        {
            if (t == null)
                return false;

            if (t == typeof(CommandAsync))
                return true;

            return InheritsFromGenericDefinition(t, typeof(CommandAsync<>))
                || InheritsFromGenericDefinition(t, typeof(CommandAsync<,>))
                || InheritsFromGenericDefinition(t, typeof(CommandAsync<,,>))
                || InheritsFromGenericDefinition(t, typeof(CommandAsync<,,,>))
                || InheritsFromGenericDefinition(t, typeof(CommandAsync<,,,,>))
                || InheritsFromGenericDefinition(t, typeof(CommandAsync<,,,,,>))
                || InheritsFromGenericDefinition(t, typeof(CommandAsync<,,,,,,>))
                || InheritsFromGenericDefinition(t, typeof(CommandAsync<,,,,,,,>))
                || InheritsFromGenericDefinition(t, typeof(CommandAsync<,,,,,,,,>))
                || InheritsFromGenericDefinition(t, typeof(CommandAsync<,,,,,,,,,>))
                || InheritsFromGenericDefinition(t, typeof(CommandAsync<,,,,,,,,,,>))
                || InheritsFromGenericDefinition(t, typeof(CommandAsync<,,,,,,,,,,,>));
        }

        private static bool InheritsFromGenericDefinition(Type type, Type genericDefinition)
        {
            if (type == null || genericDefinition == null)
                return false;

            var current = type;
            while (current != null && current != typeof(object))
            {
                var check = current.IsGenericType ? current.GetGenericTypeDefinition() : current;
                if (check == genericDefinition)
                    return true;

                current = current.BaseType;
            }

            return false;
        }

        private void DrawTypeCellWithOpen(Rect rect, SerializedProperty typeNameProp, List<Type> types, string noneLabel)
        {
            var buttonRect = rect;
            buttonRect.xMax -= OpenButtonWidth + 2f;

            var openRect = rect;
            openRect.x = buttonRect.xMax + 2f;
            openRect.width = OpenButtonWidth;

            DrawTypeCell(buttonRect, typeNameProp, types, noneLabel);

            var type = GetTypeFromProperty(typeNameProp);
            using (new EditorGUI.DisabledScope(type == null))
            {
                var content = EditorGUIUtility.IconContent("d_editicon.sml", "Open type");
                if (GUI.Button(openRect, content, GUIStyle.none))
                {
                    OpenTypeInEditor(type);
                }
            }
        }

        private void DrawTypeCell(Rect rect, SerializedProperty typeNameProp, List<Type> types, string noneLabel)
        {
            string current = typeNameProp.stringValue;
            string display = noneLabel;

            Type currentType = null;
            if (!string.IsNullOrEmpty(current))
            {
                currentType = TypeResolutionUtility.SafeGetType(current);
                display = SearchablePopup.FormatTypeLabel(currentType, noneLabel);
            }

            SearchablePopup.Draw(
                rect,
                display,
                types,
                t =>
                {
                    typeNameProp.stringValue = t.AssemblyQualifiedName;
                    typeNameProp.serializedObject.ApplyModifiedProperties();
                },
                getCurrentType: () => currentType,
                openType: OpenTypeInEditor);
        }

        private void OpenTypeInEditor(Type type)
        {
            if (type == null)
                return;

            if (TryOpenTypeDefinition(type))
                return;

            EditorUtility.DisplayDialog(
                "Open Type",
                $"Could not locate script for type '{type.FullName}'.\n\n" +
                "If this is a generic/nested type or the filename doesn't match the type name, Unity may not be able to resolve it.",
                "OK");
        }

        private static bool TryOpenTypeDefinition(Type type)
        {
            return MonoScriptCache.TryOpenScript(type);
        }

        private static MonoScript FindMonoScriptForType(Type type)
        {
            return MonoScriptCache.FindScriptForType(type);
        }

        private static Type GetTypeFromProperty(SerializedProperty typeNameProp)
        {
            if (typeNameProp == null)
                return null;

            var current = typeNameProp.stringValue;
            if (string.IsNullOrEmpty(current))
                return null;

            return TypeResolutionUtility.SafeGetType(current);
        }

        private static void AddMessageTypes(HashSet<Type> set, System.Collections.Generic.IList<Type> types)
        {
            if (types == null)
                return;

            for (int i = 0; i < types.Count; i++)
            {
                var t = types[i];
                if (t == null || t.IsAbstract || t.IsGenericTypeDefinition)
                    continue;

                set.Add(t);
            }
        }

        private void DrawRuntimeBindingsSection()
        {
            if (s_listHeaderTitleStyle == null)
                s_listHeaderTitleStyle = new GUIStyle(MvcEditorUtility.SectionHeaderTitleStyle) { alignment = TextAnchor.MiddleLeft };

            float lineH = EditorGUIUtility.singleLineHeight;
            float headerH = (lineH * 2f) + (6f * 2f);

            var content = MvcEditorUtility.DrawHeaderBox(headerH, padX: 8f, padY: 6f);
            var titleLine = new Rect(content.x, content.center.y - (lineH * 0.5f), content.width, lineH);
            EditorGUI.LabelField(titleLine, RuntimeHeaderTitle, s_listHeaderTitleStyle);

            EditorGUILayout.Space(6f);

            if (!Application.isPlaying)
            {
                EditorGUILayout.HelpBox(RuntimeRunMessage.text, MessageType.Info);
                EditorGUILayout.Space(6f);
                return;
            }

            BuildRuntimeRows();
            DrawRuntimeTable(s_runtimeRows);

            EditorGUILayout.Space(6f);
        }

        private void BuildRuntimeRows()
        {
            s_runtimeRows.Clear();

            var registry = (CommandBindingsBehaviour)target;
            if (registry == null || !Application.isPlaying)
                return;

            var module = registry.GetComponentInParent<MvcModule>();
            if (module == null)
                return;

            var processor = module.CommandProcessor;
            if (processor == null)
                return;

            processor.GetCommandBindingSnapshot(s_runtimeSnapshot);

            for (int i = 0; i < s_runtimeSnapshot.Count; i++)
            {
                var s = s_runtimeSnapshot[i];
                s_runtimeRows.Add(new RuntimeBindingRow(s.CommandType, s.MessageType, s.Mode, s.PoolCurrent, s.PoolMax));
            }
        }

        private void DrawRuntimeTable(List<RuntimeBindingRow> rows)
        {
            s_runtimeTableHeaderStyle ??= new GUIStyle(EditorStyles.miniLabel)
            {
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            s_runtimeTableCellCenterStyle ??= new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleCenter
            };

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField($"Bound commands: {rows.Count}");
            }

            var headerRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
            DrawRuntimeTableHeader(headerRect);

            for (int i = 0; i < rows.Count; i++)
            {
                var r = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight);
                DrawRuntimeTableRow(r, rows[i]);
            }
        }

        private void DrawRuntimeTableHeader(Rect rect)
        {
            float total = rect.width;

            float colCmd = total * 0.36f;
            float colMsg = total * 0.36f;
            float colMode = total * 0.14f;
            float colPool = total - colCmd - colMsg - colMode;

            float x = rect.x;
            EditorGUI.LabelField(new Rect(x, rect.y, colCmd - 2f, rect.height), RuntimeColCommand, s_runtimeTableHeaderStyle);
            x += colCmd;
            EditorGUI.LabelField(new Rect(x, rect.y, colMsg - 2f, rect.height), RuntimeColMessage, s_runtimeTableHeaderStyle);
            x += colMsg;
            EditorGUI.LabelField(new Rect(x, rect.y, colMode - 2f, rect.height), RuntimeColMode, s_runtimeTableHeaderStyle);
            x += colMode;
            EditorGUI.LabelField(new Rect(x, rect.y, colPool - 2f, rect.height), RuntimeColPool, s_runtimeTableHeaderStyle);
        }

        private void DrawRuntimeTableRow(Rect rect, RuntimeBindingRow row)
        {
            float total = rect.width;

            float colCmd = total * 0.36f;
            float colMsg = total * 0.36f;
            float colMode = total * 0.14f;
            float colPool = total - colCmd - colMsg - colMode;

            float x = rect.x;

            var cmdRect = new Rect(x, rect.y, colCmd - 2f, rect.height);
            x += colCmd;

            var msgRect = new Rect(x, rect.y, colMsg - 2f, rect.height);
            x += colMsg;

            var modeRect = new Rect(x, rect.y, colMode - 2f, rect.height);
            x += colMode;

            var poolRect = new Rect(x, rect.y, colPool - 2f, rect.height);

            DrawClickableTypeLabel(cmdRect, row.CommandType);
            DrawClickableTypeLabel(msgRect, row.MessageType);

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUI.LabelField(modeRect, ModeLabel(row.Mode), s_runtimeTableCellCenterStyle);
                EditorGUI.LabelField(poolRect, PoolLabel(row.Mode, row.PoolCurrent, row.PoolMax), s_runtimeTableCellCenterStyle);
            }
        }

        private static string ModeLabel(MvcCommandProcessor.CommandBindingMode mode)
        {
            switch (mode)
            {
                case MvcCommandProcessor.CommandBindingMode.Sync:
                    return "Sync";
                case MvcCommandProcessor.CommandBindingMode.Async:
                    return "Async";
                default:
                    return mode.ToString();
            }
        }

        private static string PoolLabel(MvcCommandProcessor.CommandBindingMode mode, int current, int max)
        {
            if (max > 0)
                return $"{current}/{max}";

            // Unknown / pooling disabled / not created yet
            return current.ToString();
        }

        private static void DrawClickableTypeLabel(Rect rect, Type type)
        {
            var text = type != null ? type.Name : "<unknown>";
            if (GUI.Button(rect, text, EditorStyles.linkLabel) && type != null)
            {
                if (!MonoScriptCache.TryOpenScript(type))
                {
                    var script = MonoScriptCache.FindScriptForType(type);
                    if (script != null)
                        AssetDatabase.OpenAsset(script);
                }
            }
        }
    }
}

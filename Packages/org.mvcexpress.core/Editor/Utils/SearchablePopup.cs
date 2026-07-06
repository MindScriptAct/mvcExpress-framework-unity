using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace mvcExpress.Editor.Core
{
    /// <summary>
    /// Reusable searchable dropdown (built on AdvancedDropdown) for picking a Type from a candidate list, optionally grouped by namespace.
    /// </summary>
    internal static class SearchablePopup
    {
        private sealed class TypeAdvancedDropdown : AdvancedDropdown
        {
            private readonly List<Type> _types;
            private readonly Action<Type> _onSelected;
            private readonly bool _flat;

            public TypeAdvancedDropdown(AdvancedDropdownState state, List<Type> types, Action<Type> onSelected, bool flat)
                : base(state)
            {
                _types = types ?? new List<Type>(0);
                _onSelected = onSelected;
                _flat = flat;
                minimumSize = new Vector2(420f, 320f);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem("Types");

                if (_types.Count == 0)
                {
                    root.AddChild(new AdvancedDropdownItem("(no types found)") { enabled = false });
                    return root;
                }

                if (_flat)
                {
                    foreach (var t in _types)
                    {
                        if (t == null) continue;
                        root.AddChild(new TypeItem(t, useFullPath: true));
                    }

                    return root;
                }

                // Group by namespace path to keep it navigable.
                var lookup = new Dictionary<string, AdvancedDropdownItem>(StringComparer.Ordinal);

                foreach (var t in _types)
                {
                    if (t == null) continue;

                    var ns = t.Namespace ?? string.Empty;
                    var parent = root;

                    if (!string.IsNullOrEmpty(ns))
                    {
                        var parts = ns.Split('.');
                        var currentPath = "";
                        for (int i = 0; i < parts.Length; i++)
                        {
                            currentPath = currentPath.Length == 0 ? parts[i] : currentPath + "." + parts[i];

                            if (!lookup.TryGetValue(currentPath, out var group))
                            {
                                group = new AdvancedDropdownItem(parts[i]);
                                lookup[currentPath] = group;
                                parent.AddChild(group);
                            }

                            parent = group;
                        }
                    }

                    parent.AddChild(new TypeItem(t, useFullPath: false));
                }

                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (item is TypeItem ti)
                {
                    _onSelected?.Invoke(ti.Type);
                }
            }

            private sealed class TypeItem : AdvancedDropdownItem
            {
                public Type Type { get; }

                public TypeItem(Type type, bool useFullPath)
                    : base(useFullPath ? GetFullPathDisplayName(type) : GetDisplayName(type))
                {
                    Type = type;
                }
            }
        }

        private static readonly AdvancedDropdownState State = new AdvancedDropdownState();

        public static bool Draw(Rect rect, string buttonLabel, List<Type> types, Action<Type> onSelected)
        {
            return Draw(rect, buttonLabel, types, onSelected, null, null);
        }

        public static bool Draw(
            Rect rect,
            string buttonLabel,
            List<Type> types,
            Action<Type> onSelected,
            Func<Type> getCurrentType,
            Action<Type> openType,
            bool flat = false)
        {
            if (GUI.Button(rect, buttonLabel, EditorStyles.popup))
            {
                var dd = new TypeAdvancedDropdown(State, types, onSelected, flat);
                dd.Show(rect);
                return true;
            }

            if (getCurrentType != null && openType != null)
            {
                HandleTypeNavigation(rect, getCurrentType(), openType);
            }

            return false;
        }

        public static string FormatTypeLabel(Type t, string noneLabel)
        {
            if (t == null) return noneLabel;
            return GetDisplayName(t);
        }

        private static string GetDisplayName(Type t)
        {
            if (t == null) return string.Empty;
            return string.IsNullOrEmpty(t.Namespace) ? t.Name : $"{t.Name} ({t.Namespace})";
        }

        private static string GetFullPathDisplayName(Type t)
        {
            if (t == null) return string.Empty;
            return string.IsNullOrEmpty(t.FullName) ? t.Name : t.FullName;
        }

        private static void HandleTypeNavigation(Rect rect, Type type, Action<Type> openType)
        {
            if (type == null) return;
            var e = Event.current;
            if (e == null) return;
            if (!rect.Contains(e.mousePosition)) return;

            if (e.type == EventType.MouseDown && e.button == 0 && e.clickCount == 2)
            {
                openType(type);
                e.Use();
                return;
            }

            if (e.type == EventType.ContextClick)
            {
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("Open Type"), false, () => openType(type));
                menu.ShowAsContext();
                e.Use();
            }
        }
    }
}

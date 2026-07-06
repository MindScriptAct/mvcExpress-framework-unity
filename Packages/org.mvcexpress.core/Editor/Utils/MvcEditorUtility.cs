using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.Core
{
    internal static class MvcEditorUtility
    {
        public const float TopHeaderIconWidth = 64f;
        public const float TopHeaderIconHeight = 48f;

        private static GUIStyle s_topHeaderTitleStyle;
        private static GUIStyle s_sectionHeaderTitleStyle;
        private static GUIStyle s_lifecycleBadgeStyle;

        private static bool TryCreateFromBoldLabel(out GUIStyle style)
        {
            style = null;

            try
            {
                // Can throw / be null during editor init or domain reload.
                var baseStyle = EditorStyles.boldLabel;
                if (baseStyle == null)
                    return false;

                style = new GUIStyle(baseStyle);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static Texture2D Load(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath)) return null;
            return AssetDatabase.LoadAssetAtPath<Texture2D>(relativePath);
        }

        public static GUIStyle TopHeaderTitleStyle
        {
            get
            {
                if (s_topHeaderTitleStyle == null)
                {
                    if (!TryCreateFromBoldLabel(out s_topHeaderTitleStyle))
                    {
                        // Fallback; will be recreated next GUI call once EditorStyles is ready.
                        return GUIStyle.none;
                    }

                    s_topHeaderTitleStyle = new GUIStyle(s_topHeaderTitleStyle)
                    {
                        fontSize = 32,
                        alignment = TextAnchor.MiddleLeft,
                        wordWrap = false,
                        clipping = TextClipping.Overflow,
                        fontStyle = FontStyle.BoldAndItalic
                    };

                    // Ensure descenders (g, y, p, q) don't get clipped.
                    var pad = s_topHeaderTitleStyle.padding;
                    pad.top += 2;
                    pad.bottom += 3;
                    s_topHeaderTitleStyle.padding = pad;
                }

                return s_topHeaderTitleStyle;
            }
        }

        public static GUIStyle SectionHeaderTitleStyle
        {
            get
            {
                if (s_sectionHeaderTitleStyle == null)
                {
                    if (!TryCreateFromBoldLabel(out s_sectionHeaderTitleStyle))
                    {
                        // Fallback; will be recreated next GUI call once EditorStyles is ready.
                        return GUIStyle.none;
                    }

                    s_sectionHeaderTitleStyle = new GUIStyle(s_sectionHeaderTitleStyle)
                    {
                        fontSize = 20,
                        alignment = TextAnchor.MiddleLeft,
                        wordWrap = false,
                        clipping = TextClipping.Overflow,
                        fontStyle = FontStyle.Bold
                    };

                    // Ensure descenders (g, y, p, q) don't get clipped.
                    var pad = s_sectionHeaderTitleStyle.padding;
                    pad.top += 2;
                    pad.bottom += 3;
                    s_sectionHeaderTitleStyle.padding = pad;
                }

                return s_sectionHeaderTitleStyle;
            }
        }

        public static Rect DrawHeaderBox(float height, float padX = 8f, float padY = 6f)
        {
            var rect = GUILayoutUtility.GetRect(0f, height, GUILayout.ExpandWidth(true));

            var bg = EditorGUIUtility.isProSkin
                ? new Color(0.20f, 0.20f, 0.20f, 1f)
                : new Color(0.80f, 0.80f, 0.80f, 1f);
            var outline = EditorGUIUtility.isProSkin
                ? new Color(0f, 0f, 0f, 0.45f)
                : new Color(0f, 0f, 0f, 0.30f);

            EditorGUI.DrawRect(rect, bg);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), outline);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), outline);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1f, rect.height), outline);
            EditorGUI.DrawRect(new Rect(rect.xMax - 1f, rect.y, 1f, rect.height), outline);

            return new Rect(rect.x + padX, rect.y + padY, rect.width - (padX * 2f), rect.height - (padY * 2f));
        }

        /// <summary>
        /// Draws a colored, clickable Permanent/Transient badge (green "P" / blue "T") and returns
        /// the new value of <paramref name="isTransient"/> after handling a click. Mirrors the
        /// read-only source badge pattern used elsewhere in the Editor UI, but interactive.
        /// </summary>
        /// <param name="rect">Rect to draw the badge in.</param>
        /// <param name="isTransient">Current value: true = Transient, false = Permanent.</param>
        /// <param name="actorNoun">What this badge governs, for the tooltip - e.g. "Proxy" or "Service".</param>
        public static bool DrawLifecycleBadge(Rect rect, bool isTransient, string actorNoun)
        {
            s_lifecycleBadgeStyle ??= new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 10
            };

            var badgeColor = isTransient
                ? new Color(0.3f, 0.7f, 1f)  // Blue: Transient
                : new Color(0.3f, 1f, 0.3f); // Green: Permanent

            var previousColor = GUI.backgroundColor;
            GUI.backgroundColor = badgeColor;

            var tooltip = isTransient
                ? $"Transient - {actorNoun} can be created/destroyed dynamically at any time."
                : $"Permanent - {actorNoun} can only be destroyed when the module is destroyed.";
            var content = new GUIContent(isTransient ? "T" : "P", tooltip);

            bool clicked = GUI.Button(rect, content, s_lifecycleBadgeStyle);

            GUI.backgroundColor = previousColor;

            return clicked ? !isTransient : isTransient;
        }
    }
}

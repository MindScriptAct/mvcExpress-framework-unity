using System;
using UnityEditor;
using UnityEngine;

namespace mvcExpress.Editor.Core
{
    /// <summary>
    /// Reusable paging state and controls for large <see cref="UnityEditorInternal.ReorderableList"/>-backed
    /// inspectors. Keeps the wrapped list's drag/add/remove behavior intact by hiding off-page rows
    /// (zero height, no draw) instead of slicing the underlying serialized array.
    /// </summary>
    internal sealed class MvcListPager
    {
        private static readonly int[] PageSizes = { 10, 25, 50, 100 };
        private static readonly string[] PageSizeLabels = { "10", "25", "50", "100" };

        private static readonly GUIContent PrevContent = new GUIContent("< Prev");
        private static readonly GUIContent NextContent = new GUIContent("Next >");
        private static readonly GUIContent PageSizeHeader = new GUIContent("Page size", "Items shown per page.");

        private int _pageIndex;
        private int _pageSize;

        public MvcListPager(int defaultPageSize = 10)
        {
            _pageSize = ClampToKnownSize(defaultPageSize);
        }

        public int PageIndex => _pageIndex;
        public int PageSize => _pageSize;

        /// <summary>Whether the element at <paramref name="index"/> falls on the current page.</summary>
        public bool IsIndexVisible(int index)
        {
            int start = _pageIndex * _pageSize;
            return index >= start && index < start + _pageSize;
        }

        /// <summary>Clamps the current page to fit <paramref name="totalCount"/> items. Returns the page count.</summary>
        public int ClampToCount(int totalCount)
        {
            int totalPages = Mathf.Max(1, Mathf.CeilToInt(totalCount / (float)_pageSize));
            _pageIndex = Mathf.Clamp(_pageIndex, 0, totalPages - 1);
            return totalPages;
        }

        /// <summary>Jumps to the page containing the last item - call after appending a new row.</summary>
        public void GoToLastPage(int totalCount)
        {
            int totalPages = Mathf.Max(1, Mathf.CeilToInt(totalCount / (float)_pageSize));
            _pageIndex = totalPages - 1;
        }

        /// <summary>Height <see cref="DrawControls"/> will occupy for <paramref name="totalCount"/> items - 0 when it draws nothing.</summary>
        public float EstimateControlsHeight(int totalCount)
        {
            return totalCount > _pageSize ? EditorGUIUtility.singleLineHeight + 4f : 0f;
        }

        /// <summary>Draws Prev/Next + page-size controls. No-op when everything fits on one page.</summary>
        public void DrawControls(int totalCount)
        {
            int totalPages = ClampToCount(totalCount);
            if (totalCount <= _pageSize)
                return;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_pageIndex <= 0))
                {
                    if (GUILayout.Button(PrevContent, GUILayout.Width(70f)))
                        _pageIndex--;
                }

                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField(
                    $"Page {_pageIndex + 1} / {totalPages}  ({totalCount} items)",
                    EditorStyles.centeredGreyMiniLabel,
                    GUILayout.Width(160f));
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(_pageIndex >= totalPages - 1))
                {
                    if (GUILayout.Button(NextContent, GUILayout.Width(70f)))
                        _pageIndex++;
                }

                GUILayout.Space(8f);
                EditorGUILayout.LabelField(PageSizeHeader, GUILayout.Width(60f));

                int sizeIndex = Array.IndexOf(PageSizes, _pageSize);
                if (sizeIndex < 0) sizeIndex = 0;

                int newSizeIndex = EditorGUILayout.Popup(sizeIndex, PageSizeLabels, GUILayout.Width(50f));
                if (newSizeIndex != sizeIndex)
                {
                    _pageSize = PageSizes[newSizeIndex];
                    _pageIndex = 0;
                }
            }
        }

        /// <summary>
        /// Rect-based equivalent of <see cref="DrawControls(int)"/> for contexts where auto-layout
        /// isn't available - e.g. drawn from inside another control's raw-Rect draw callback.
        /// </summary>
        public void DrawControls(Rect rect, int totalCount)
        {
            int totalPages = ClampToCount(totalCount);
            if (totalCount <= _pageSize)
                return;

            const float btnWidth = 70f;
            const float pageSizeDropdownWidth = 50f;
            const float pageSizeLabelWidth = 60f;
            const float gap = 4f;

            var prevRect = new Rect(rect.x, rect.y, btnWidth, rect.height);
            using (new EditorGUI.DisabledScope(_pageIndex <= 0))
            {
                if (GUI.Button(prevRect, PrevContent))
                    _pageIndex--;
            }

            var nextRect = new Rect(rect.xMax - btnWidth, rect.y, btnWidth, rect.height);
            using (new EditorGUI.DisabledScope(_pageIndex >= totalPages - 1))
            {
                if (GUI.Button(nextRect, NextContent))
                    _pageIndex++;
            }

            var pageSizeDropdownRect = new Rect(nextRect.x - pageSizeDropdownWidth - gap, rect.y, pageSizeDropdownWidth, rect.height);
            var pageSizeLabelRect = new Rect(pageSizeDropdownRect.x - pageSizeLabelWidth - gap, rect.y, pageSizeLabelWidth, rect.height);
            var pageInfoRect = new Rect(prevRect.xMax + gap, rect.y, pageSizeLabelRect.x - gap - (prevRect.xMax + gap), rect.height);

            EditorGUI.LabelField(pageInfoRect, $"Page {_pageIndex + 1} / {totalPages}  ({totalCount} items)", EditorStyles.centeredGreyMiniLabel);
            EditorGUI.LabelField(pageSizeLabelRect, PageSizeHeader);

            int sizeIndex = Array.IndexOf(PageSizes, _pageSize);
            if (sizeIndex < 0) sizeIndex = 0;

            int newSizeIndex = EditorGUI.Popup(pageSizeDropdownRect, sizeIndex, PageSizeLabels);
            if (newSizeIndex != sizeIndex)
            {
                _pageSize = PageSizes[newSizeIndex];
                _pageIndex = 0;
            }
        }

        private static int ClampToKnownSize(int size)
        {
            for (int i = 0; i < PageSizes.Length; i++)
            {
                if (PageSizes[i] == size)
                    return size;
            }
            return PageSizes[0];
        }
    }
}

using System;
using System.Collections.Generic;
using UnityEngine;

namespace mvcExpress
{
    /// <summary>
    /// Editor-only authoring helper that manages preview copies of mediator view content inside
    /// a prefab or scene hierarchy.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When designing UI layouts that repeat mediator views (e.g., a list of inventory items),
    /// designers need to see representative content in the Editor without those copies
    /// becoming live runtime actors. <c>MvcViewAuthoringRoot</c> stores
    /// <see cref="MvcViewAuthoringPreviewEntry"/> entries; editor tooling generates preview
    /// GameObjects from them at edit time and removes them before Play mode starts.
    /// </para>
    /// <para>
    /// This component has <b>no runtime overhead</b> - it carries no <c>Update</c>, and the
    /// generated copies it tracks are editor-only GameObjects.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    [AddComponentMenu("mvcExpress/View Authoring Root")]
    public sealed class MvcViewAuthoringRoot : MonoBehaviour
    {
        [SerializeField] private List<MvcViewAuthoringPreviewEntry> _previewEntries = new List<MvcViewAuthoringPreviewEntry>();

        /// <summary>
        /// Gets preview entries configured for this authoring root.
        /// </summary>
        public List<MvcViewAuthoringPreviewEntry> PreviewEntries => _previewEntries;
    }

    /// <summary>
    /// Serializable configuration for one preview source inside a <see cref="MvcViewAuthoringRoot"/>.
    /// </summary>
    /// <remarks>
    /// Each entry describes a single preview object (e.g., a list-item prefab) and how many
    /// copies to generate at edit time. The editor tooling uses <see cref="GeneratedCopies"/>
    /// to track which GameObjects it owns so it can clean them up on demand or before Play mode.
    /// </remarks>
    [Serializable]
    public sealed class MvcViewAuthoringPreviewEntry
    {
        [SerializeField] private GameObject _previewObject;
        [SerializeField] private bool _listItem;
        [SerializeField, Min(1)] private int _previewCount = 1;
        [SerializeField] private List<GameObject> _generatedCopies = new List<GameObject>();

        /// <summary>
        /// The source GameObject cloned to produce preview copies.
        /// Typically a mediator prefab or a child view template.
        /// </summary>
        public GameObject PreviewObject
        {
            get => _previewObject;
            set => _previewObject = value;
        }

        /// <summary>
        /// When true, <see cref="PreviewCount"/> copies are generated to simulate a list layout.
        /// When false, a single copy is shown (or none if <see cref="PreviewCount"/> == 0).
        /// </summary>
        public bool ListItem
        {
            get => _listItem;
            set => _listItem = value;
        }

        /// <summary>
        /// Number of preview copies to generate. Clamped to a minimum of 1.
        /// </summary>
        public int PreviewCount
        {
            get => Mathf.Max(1, _previewCount);
            set => _previewCount = Mathf.Max(1, value);
        }

        /// <summary>
        /// Editor-generated preview GameObjects tracked for cleanup. Managed by editor tooling only.
        /// </summary>
        public List<GameObject> GeneratedCopies => _generatedCopies;
    }
}

using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace mvcExpress
{
    /// <summary>
    /// Inspector-friendly registry for scene <see cref="MediatorBehaviour"/> instances and
    /// mediator prefab mappings owned by a module.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the Unity-behaviour registration method for Mediators - the first of three methods
    /// (Inspector → Attribute → Code). Add this component as a child of your <see cref="MvcModule"/>
    /// GameObject. The module initializer reads it during the <c>AttachMediators</c> phase:
    /// <list type="bullet">
    /// <item><description><see cref="SceneMediators"/> - existing scene instances; attached and injected immediately.</description></item>
    /// <item><description><see cref="MediatorPrefabs"/> - prefab mappings consumed later by <c>MediatorHubApi</c> for runtime instantiation.</description></item>
    /// </list>
    /// </para>
    /// <para>
    /// <see cref="ViewContainer"/> sets where new mediator instances are parented when the
    /// module's hub spawns them. Defaults to this component's own <c>Transform</c>.
    /// </para>
    /// </remarks>
    [DisallowMultipleComponent]
    public sealed class MediatorRegistryBehaviour : MonoBehaviour
    {
        [SerializeField]
        private MediatorBehaviour[] _sceneMediators = Array.Empty<MediatorBehaviour>();

        [SerializeField]
        private MediatorPrefabMapping[] _mediatorPrefabs = Array.Empty<MediatorPrefabMapping>();

        // View Container - where mediators are instantiated by default
        [SerializeField, Tooltip("Default container for mediator instances. Defaults to this GameObject if not set.")]
        private Transform _viewContainer;

        /// <summary>
        /// Scene mediator instances configured for the owning module.
        /// </summary>
        public MediatorBehaviour[] SceneMediators => _sceneMediators;

        /// <summary>
        /// Mediator prefab mappings configured for runtime prefab attachment.
        /// </summary>
        public MediatorPrefabMapping[] MediatorPrefabs => _mediatorPrefabs;

        /// <summary>
        /// The default container where mediators will be instantiated.
        /// Always returns a valid Transform (self if not configured).
        /// </summary>
        public Transform ViewContainer
        {
            get
            {
                // Inline validation for performance - avoid method call overhead
                if (_viewContainer == null)
                {
                    _viewContainer = transform;
                }
                return _viewContainer;
            }
        }

        /// <summary>
        /// Manually set the view container.
        /// If null, resets to self.
        /// </summary>
        public void SetViewContainer(Transform container)
        {
            _viewContainer = container != null ? container : transform;
        }

        private void Awake()
        {
            // Validate and fix view container reference
            // Use property to ensure validation happens
            _ = ViewContainer;

            // Ensure debug instances never make it into play mode / builds.
            // They are editor-time helpers to see the prefab in the scene.
            RemoveDebugPrefabInstances();
        }

        private void RemoveDebugPrefabInstances()
        {
            // Early exit for performance
            if (_mediatorPrefabs == null || _mediatorPrefabs.Length == 0)
                return;

            // Cache transform for multiple Find calls
            var selfTransform = transform;

            // Use for loop for better performance than foreach
            for (int i = 0; i < _mediatorPrefabs.Length; i++)
            {
                var m = _mediatorPrefabs[i];
                
                // Skip null or non-debug entries early
                if (m == null || !m.AddDebugInstance)
                    continue;

                var expectedName = m.GetDebugInstanceName();

                // Find is relatively expensive - only call if we know we need to
                var t = selfTransform.Find(expectedName);
                if (t != null)
                {
                    Destroy(t.gameObject);
                }
            }
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            // Auto-fix null container in editor
            // Use property to ensure consistent validation
            _ = ViewContainer;
        }
#endif
    }

    /// <summary>
    /// Serializable mapping between a mediator type and its prefab.
    /// </summary>
    [Serializable]
    public sealed class MediatorPrefabMapping
    {
        /// <summary>
        /// Assembly-qualified mediator type name used at runtime.
        /// </summary>
        [SerializeField]
        public string MediatorTypeName;

#if UNITY_EDITOR
        /// <summary>
        /// Editor-only script reference used to keep the mediator type name synchronized after renames.
        /// </summary>
        [SerializeField]
        [Tooltip("Editor-only script reference used to keep MediatorTypeName stable across renames.")]
        public MonoScript MediatorScript;
#endif

        /// <summary>
        /// Prefab containing the mediator component.
        /// </summary>
        [SerializeField]
        public GameObject Prefab;

        /// <summary>
        /// Whether the editor should spawn a preview instance for authoring nested mediator layouts.
        /// </summary>
        [SerializeField]
        public bool AddDebugInstance;

        /// <summary>
        /// Debug-only parent mediator type name used by the editor preview spawner.
        /// </summary>
        [SerializeField]
        public string DebugParentMediatorTypeName;

        /// <summary>
        /// Relative container path under the debug parent mediator instance.
        /// </summary>
        [SerializeField]
        public string DebugParentContainerPath;

        /// <summary>
        /// Optional root container path used when the debug preview has no parent mediator.
        /// </summary>
        [SerializeField]
        public string DebugViewRootContainerPath;

        // Cache for debug instance name to avoid recalculation
        private string _cachedDebugInstanceName;

        internal string GetDebugInstanceName()
        {
            // Cache the result for performance (called potentially multiple times)
            if (_cachedDebugInstanceName != null)
                return _cachedDebugInstanceName;

            var typeName = string.IsNullOrEmpty(MediatorTypeName) ? "Mediator" : MediatorTypeName;
            var shortName = typeName;

            // Use IndexOf instead of multiple operations where possible
            var commaIdx = shortName.IndexOf(',');
            if (commaIdx >= 0)
            {
                shortName = shortName.Substring(0, commaIdx);
            }

            var dotIdx = shortName.LastIndexOf('.');
            if (dotIdx >= 0 && dotIdx < shortName.Length - 1)
            {
                shortName = shortName.Substring(dotIdx + 1);
            }

            _cachedDebugInstanceName = $"<[{shortName}]>";
            return _cachedDebugInstanceName;
        }

#if UNITY_EDITOR
        internal void EditorSyncTypeNameFromReferences()
        {
            if (Prefab != null)
            {
                var mediator = Prefab.GetComponent<MediatorBehaviour>();
                if (mediator != null)
                {
                    MediatorTypeName = mediator.GetType().AssemblyQualifiedName;
                    MediatorScript = MonoScript.FromMonoBehaviour(mediator);
                    _cachedDebugInstanceName = null;
                    return;
                }
            }

            if (MediatorScript != null)
            {
                var type = MediatorScript.GetClass();
                if (type != null && typeof(MediatorBehaviour).IsAssignableFrom(type))
                {
                    MediatorTypeName = type.AssemblyQualifiedName;
                    _cachedDebugInstanceName = null;
                }
            }
        }
#endif
    }
}

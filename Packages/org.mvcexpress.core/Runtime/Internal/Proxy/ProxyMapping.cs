using System;
using mvcExpress.Logging;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace mvcExpress.Internal.Proxy
{
    /// <summary>
    /// Serializable Inspector mapping for registering a <see cref="ProxyBehaviour"/>.
    /// </summary>
    /// <remarks>
    /// Proxy mappings let prefab-authored or scene-authored proxies participate in the
    /// module registration phase while choosing logic/view scopes and optional interface types.
    /// </remarks>
    [Serializable]
    public sealed class ProxyMapping
    {
        /// <summary>
        /// Proxy behaviour instance to register.
        /// </summary>
        [SerializeField]
        [Tooltip("The proxy component to register in the DI container")]
        public ProxyBehaviour Proxy;

        /// <summary>
        /// Gets or sets whether commands and proxies can resolve this proxy.
        /// </summary>
        [SerializeField]
        [Tooltip("When checked: Logic layer (commands/proxies) can access this proxy")]
        public bool RegisterToLogic = true;

        /// <summary>
        /// Gets or sets whether mediators can resolve this proxy.
        /// </summary>
        [SerializeField]
        [Tooltip("When checked: View layer (mediators) can access this proxy")]
        public bool RegisterToView = false;

        /// <summary>
        /// Gets or sets whether this proxy uses transient lifetime.
        /// </summary>
        [SerializeField]
        [Tooltip("When checked: Transient (can be destroyed). Unchecked: Permanent (cannot be destroyed)")]
        public bool IsTransient;

        /// <summary>
        /// Assembly-qualified type name used for logic registration.
        /// </summary>
        [SerializeField]
        public string LogicTypeName;

        /// <summary>
        /// Assembly-qualified type name used for view registration.
        /// </summary>
        [SerializeField]
        public string ViewTypeName;

        /// <summary>
        /// Cached resolved logic type.
        /// </summary>
        [NonSerialized]
        private Type _logicType;

        /// <summary>
        /// Cached resolved view type.
        /// </summary>
        [NonSerialized]
        private Type _viewType;

#if UNITY_EDITOR
        [SerializeField]
        [UnityEngine.Tooltip("Editor-only script reference for the selected logic registration type. Used to keep type names stable across renames.")]
        public MonoScript LogicScript;

        [SerializeField]
        [UnityEngine.Tooltip("Editor-only script reference for the selected view registration type. Used to keep type names stable across renames.")]
        public MonoScript ViewScript;

        internal void EditorSyncTypeNamesFromScripts()
        {
            // Called from editor UI code (PropertyDrawer) where MonoScript.GetClass() is allowed.
            if (LogicScript != null)
            {
                var cls = LogicScript.GetClass();
                if (cls != null)
                    LogicTypeName = cls.AssemblyQualifiedName;
            }

            if (ViewScript != null)
            {
                var cls = ViewScript.GetClass();
                if (cls != null)
                    ViewTypeName = cls.AssemblyQualifiedName;
            }

            _logicType = null;
            _viewType = null;
        }
#endif

        /// <summary>
        /// Resolves and caches the logic registration type.
        /// </summary>
        public Type ResolveLogicType()
        {
            if (_logicType == null && !string.IsNullOrEmpty(LogicTypeName))
            {
                _logicType = Type.GetType(LogicTypeName);
                if (_logicType == null)
                {
                    MvcDebug.LogWarning($"Failed to resolve proxy logic type: {LogicTypeName}");
                }
            }
            return _logicType;
        }

        /// <summary>
        /// Resolves and caches the view registration type.
        /// </summary>
        public Type ResolveViewType()
        {
            if (_viewType == null && !string.IsNullOrEmpty(ViewTypeName))
            {
                _viewType = Type.GetType(ViewTypeName);
                if (_viewType == null)
                {
                    MvcDebug.LogWarning($"Failed to resolve proxy view type: {ViewTypeName}");
                }
            }
            return _viewType;
        }

        /// <summary>
        /// Returns whether this mapping has enough data to register a proxy.
        /// </summary>
        public bool IsValid()
        {
            // Must have at least one scope enabled
            if (!RegisterToLogic && !RegisterToView)
            {
                return false;
            }

            // Must have proxy reference
            if (Proxy == null)
            {
                return false;
            }

            // Must have logic type if registering to logic
            if (RegisterToLogic && string.IsNullOrEmpty(LogicTypeName))
            {
                return false;
            }

            // Must have view type if registering to view
            if (RegisterToView && string.IsNullOrEmpty(ViewTypeName))
            {
                return false;
            }

            return true;
        }
    }
}

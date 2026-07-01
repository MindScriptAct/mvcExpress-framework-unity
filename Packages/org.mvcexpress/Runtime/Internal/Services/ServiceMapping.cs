using System;
using mvcExpress.Logging;
using UnityEngine;

namespace mvcExpress.Internal.Services
{
    /// <summary>
    /// Serializable Inspector mapping for registering a MonoBehaviour service.
    /// </summary>
    /// <remarks>
    /// Service mappings let scene-authored services participate in the module registration
    /// phase while choosing logic/view scopes and optional interface types.
    /// </remarks>
    [Serializable]
    public sealed class ServiceMapping
    {
        /// <summary>
        /// Service component instance to register.
        /// </summary>
        [SerializeField]
        [Tooltip("The MonoBehaviour service component to register")]
        public MonoBehaviour Service;

        /// <summary>
        /// Gets or sets whether commands and proxies can resolve this service.
        /// </summary>
        [SerializeField]
        [Tooltip("When checked: Logic layer (commands/proxies) can access this service")]
        public bool RegisterToLogic = true;

        /// <summary>
        /// Gets or sets whether mediators can resolve this service.
        /// </summary>
        [SerializeField]
        [Tooltip("When checked: View layer (mediators) can access this service")]
        public bool RegisterToView = true;

        /// <summary>
        /// Gets or sets whether this service uses transient lifetime.
        /// </summary>
        [SerializeField]
        [Tooltip("When checked: Transient (can be destroyed). Unchecked: Persistent (cannot be destroyed)")]
        public bool IsTransient;

        /// <summary>
        /// Gets or sets whether advanced type/interface options are shown in custom editors.
        /// </summary>
        [SerializeField]
        [Tooltip("Show advanced options for custom type/interface selection")]
        public bool ShowAdvancedOptions;

        /// <summary>
        /// Optional assembly-qualified type name used for logic registration.
        /// </summary>
        [SerializeField]
        [Tooltip("(Optional) Specific type/interface to register in logic layer. Leave empty to use Service's concrete type.")]
        public string LogicTypeName;

        /// <summary>
        /// Optional assembly-qualified type name used for view registration.
        /// </summary>
        [SerializeField]
        [Tooltip("(Optional) Specific type/interface to register in view layer. Leave empty to use Service's concrete type.")]
        public string ViewTypeName;

        [NonSerialized]
        private Type _logicType;

        [NonSerialized]
        private Type _viewType;

        /// <summary>
        /// Resolves the logic registration type, falling back to the service's concrete type.
        /// </summary>
        public Type ResolveLogicType(Type moduleType = null)
        {
            if (_logicType != null)
                return _logicType;

            // Strategy 1: If no custom type name specified, use Service's actual type (rename-safe!)
            if (string.IsNullOrEmpty(LogicTypeName))
            {
                if (Service != null)
                {
                    _logicType = Service.GetType();
                    return _logicType;
                }
                
                MvcDebug.LogError(
                    $"Cannot resolve logic type - Service is null.\n" +
                    $"Module: {moduleType?.FullName ?? "<unknown>"}\n" +
                    $"Fix: Assign a Service component in the ServiceRegistry.");
                return null;
            }

            // Strategy 2: Custom type name specified - try to resolve it
            _logicType = Type.GetType(LogicTypeName);
            
            if (_logicType != null)
                return _logicType;

            // Strategy 3: Type name resolution failed
            if (Service != null)
            {
                var actualType = Service.GetType();
                var actualTypeName = actualType.FullName;
                var serializedTypeName = LogicTypeName.Split(',')[0].Trim();
                
                var moduleInfo = moduleType != null ? $"Module: {moduleType.FullName}" : "Module: <unknown>";
                
                // Check if it's a namespace mismatch (type moved/renamed)
                if (actualTypeName != serializedTypeName)
                {
                    MvcDebug.LogError(
                        $"Critical: Custom LOGIC type could not be resolved.\n" +
                        $"{moduleInfo}\n" +
                        $"GameObject: '{Service.gameObject.name}'\n" +
                        $"Service Type: {actualTypeName}\n" +
                        $"Expected Custom Type: {serializedTypeName}\n" +
                        $"\n" +
                        $"CAUSE: The interface/base type was renamed, moved, or deleted.\n" +
                        $"\n" +
                        $"FIX OPTIONS:\n" +
                        $"  1. Open ServiceRegistry Inspector\n" +
                        $"  2. Find the row with GameObject '{Service.gameObject.name}'\n" +
                        $"  3. Click the Logic Type button and re-select the correct type\n" +
                        $"  4. Or select '{actualType.Name} (concrete)' to use the service's actual type\n" +
                        $"\n" +
                        $"Module will FAIL to initialize until this is fixed.");
                }
                else
                {
                    MvcDebug.LogError(
                        $"Critical: Custom LOGIC type resolution failed.\n" +
                        $"{moduleInfo}\n" +
                        $"Service: {actualTypeName} on GameObject '{Service.gameObject.name}'\n" +
                        $"Custom Type Name: {LogicTypeName}\n" +
                        $"\n" +
                        $"POSSIBLE CAUSES:\n" +
                        $"  1. Type name is incorrect\n" +
                        $"  2. Type no longer exists (deleted)\n" +
                        $"  3. Assembly is not loaded\n" +
                        $"  4. Compilation errors preventing type resolution\n" +
                        $"\n" +
                        $"FIX: Open ServiceRegistry Inspector and re-select the type.\n" +
                        $"\n" +
                        $"Module will FAIL to initialize until this is fixed.");
                }
                
                return null;
            }
            
            // Both strategies failed
            MvcDebug.LogError(
                $"Critical: Failed to resolve LOGIC type.\n" +
                $"Module: {moduleType?.FullName ?? "<unknown>"}\n" +
                $"Type Name: {LogicTypeName}\n" +
                $"Service: <null>\n" +
                $"\n" +
                $"FIX: Assign a Service component in the ServiceRegistry.\n" +
                $"\n" +
                $"Module will FAIL to initialize until this is fixed.");
            
            return null;
        }

        /// <summary>
        /// Resolves the view registration type, falling back to the service's concrete type.
        /// </summary>
        public Type ResolveViewType(Type moduleType = null)
        {
            if (_viewType != null)
                return _viewType;

            // Strategy 1: If no custom type name specified, use Service's actual type (rename-safe!)
            if (string.IsNullOrEmpty(ViewTypeName))
            {
                if (Service != null)
                {
                    _viewType = Service.GetType();
                    return _viewType;
                }
                
                MvcDebug.LogError(
                    $"Cannot resolve view type - Service is null.\n" +
                    $"Module: {moduleType?.FullName ?? "<unknown>"}\n" +
                    $"Fix: Assign a Service component in the ServiceRegistry.");
                return null;
            }

            // Strategy 2: Custom type name specified - try to resolve it
            _viewType = Type.GetType(ViewTypeName);
            
            if (_viewType != null)
                return _viewType;

            // Strategy 3: Type name resolution failed
            if (Service != null)
            {
                var actualType = Service.GetType();
                var actualTypeName = actualType.FullName;
                var serializedTypeName = ViewTypeName.Split(',')[0].Trim();
                
                var moduleInfo = moduleType != null ? $"Module: {moduleType.FullName}" : "Module: <unknown>";
                
                // Check if it's a namespace mismatch (type moved/renamed)
                if (actualTypeName != serializedTypeName)
                {
                    MvcDebug.LogError(
                        $"Critical: Custom VIEW type could not be resolved.\n" +
                        $"{moduleInfo}\n" +
                        $"GameObject: '{Service.gameObject.name}'\n" +
                        $"Service Type: {actualTypeName}\n" +
                        $"Expected Custom Type: {serializedTypeName}\n" +
                        $"\n" +
                        $"CAUSE: The interface/base type was renamed, moved, or deleted.\n" +
                        $"\n" +
                        $"FIX OPTIONS:\n" +
                        $"  1. Open ServiceRegistry Inspector\n" +
                        $"  2. Find the row with GameObject '{Service.gameObject.name}'\n" +
                        $"  3. Click the View Type button and re-select the correct type\n" +
                        $"  4. Or select '{actualType.Name} (concrete)' to use the service's actual type\n" +
                        $"\n" +
                        $"Module will FAIL to initialize until this is fixed.");
                }
                else
                {
                    MvcDebug.LogError(
                        $"Critical: Custom VIEW type resolution failed.\n" +
                        $"{moduleInfo}\n" +
                        $"Service: {actualTypeName} on GameObject '{Service.gameObject.name}'\n" +
                        $"Custom Type Name: {ViewTypeName}\n" +
                        $"\n" +
                        $"POSSIBLE CAUSES:\n" +
                        $"  1. Type name is incorrect\n" +
                        $"  2. Type no longer exists (deleted)\n" +
                        $"  3. Assembly is not loaded\n" +
                        $"  4. Compilation errors preventing type resolution\n" +
                        $"\n" +
                        $"FIX: Open ServiceRegistry Inspector and re-select the type.\n" +
                        $"\n" +
                        $"Module will FAIL to initialize until this is fixed.");
                }
                
                return null;
            }
            
            // Both strategies failed
            MvcDebug.LogError(
                $"Critical: Failed to resolve VIEW type.\n" +
                $"Module: {moduleType?.FullName ?? "<unknown>"}\n" +
                $"Type Name: {ViewTypeName}\n" +
                $"Service: <null>\n" +
                $"\n" +
                $"FIX: Assign a Service component in the ServiceRegistry.\n" +
                $"\n" +
                $"Module will FAIL to initialize until this is fixed.");
            
            return null;
        }

        /// <summary>
        /// Returns <c>true</c> if this mapping is ready to be registered.
        /// A mapping is invalid if the <see cref="Service"/> reference is null, or if both
        /// <see cref="RegisterToLogic"/> and <see cref="RegisterToView"/> are <c>false</c>
        /// (which would make registration a no-op). Type names are optional and do not affect validity.
        /// </summary>
        public bool IsValid()
        {
            if (!RegisterToLogic && !RegisterToView)
            {
                return false;
            }

            if (Service == null)
            {
                return false;
            }

            // NOTE: We no longer require LogicTypeName/ViewTypeName to be set
            // They are optional - if empty, we use Service's actual type
            // This makes the system resilient to namespace changes

            return true;
        }
    }
}

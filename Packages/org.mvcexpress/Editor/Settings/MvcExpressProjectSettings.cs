using UnityEditor;

namespace mvcExpress.Editor.Settings
{
    public static class MvcExpressProjectSettings
    {
        private const string RootKey = "org.mvcexpress";

        private static string MakeKey(string leafKey) => $"{RootKey}.{leafKey}";

        public static bool HelpInInspector
        {
            get => EditorPrefs.GetBool(MakeKey("help_in_inspector"), true);
            set => EditorPrefs.SetBool(MakeKey("help_in_inspector"), value);
        }

        public static bool GlobalLoggingEnabled
        {
            get => EditorPrefs.GetBool(MakeKey("global_logging_enabled"), false);
            set
            {
                EditorPrefs.SetBool(MakeKey("global_logging_enabled"), value);
                // Update runtime value immediately
                mvcExpress.Logging.MvcLogInternal.LoggingEnabled = value;
            }
        }

        /// <summary>
        /// When enabled, all Unity logs (Debug.Log, Debug.LogWarning, Debug.LogError) 
        /// are forwarded to MVC Console Pro and categorized under the "Output" filter.
        /// </summary>
        public static bool ForwardUnityLogs
        {
            get => EditorPrefs.GetBool(MakeKey("forward_unity_logs"), false);
            set => EditorPrefs.SetBool(MakeKey("forward_unity_logs"), value);
        }

        /// <summary>
        /// When enabled, framework logs will use Unity's Debug.Log as fallback
        /// when no custom logger is injected and Console Pro is not active.
        /// Disable this for silent operation (logs will only go to custom logger or Console Pro).
        /// </summary>
        public static bool UseUnityDebugLogFallback
        {
            get => EditorPrefs.GetBool(MakeKey("use_unity_debug_fallback"), true); // Default: ON
            set
            {
                EditorPrefs.SetBool(MakeKey("use_unity_debug_fallback"), value);
                // Update runtime value immediately
                mvcExpress.Logging.MvcLogInternal.UseUnityDebugFallback = value;
            }
        }

        // ── Hierarchy icon visibility ─────────────────────────────────────────

        /// <summary>Show icons next to MvcFacade and MvcModule GameObjects in the Hierarchy.</summary>
        public static bool HierarchyIconsModuleFacade
        {
            get => EditorPrefs.GetBool(MakeKey("hierarchy_icons_module_facade"), true);
            set => EditorPrefs.SetBool(MakeKey("hierarchy_icons_module_facade"), value);
        }

        /// <summary>Show icons next to registry containers (Services, Model, Controller, View) in the Hierarchy.</summary>
        public static bool HierarchyIconsRegistries
        {
            get => EditorPrefs.GetBool(MakeKey("hierarchy_icons_registries"), true);
            set => EditorPrefs.SetBool(MakeKey("hierarchy_icons_registries"), value);
        }

        /// <summary>Show icons next to Mediators, ProxyBehaviours, and code-proxy debug wrappers in the Hierarchy.</summary>
        public static bool HierarchyIconsActors
        {
            get => EditorPrefs.GetBool(MakeKey("hierarchy_icons_actors"), true);
            set => EditorPrefs.SetBool(MakeKey("hierarchy_icons_actors"), value);
        }
    }
}

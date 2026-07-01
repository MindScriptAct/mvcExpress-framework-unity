using UnityEditor;
using mvcExpress.Logging;

namespace mvcExpress.Editor.Settings
{
    /// <summary>
    /// Initializes mvcExpress logging settings from EditorPrefs when the editor loads.
    /// </summary>
    [InitializeOnLoad]
    internal static class MvcLoggingInitializer
    {
        static MvcLoggingInitializer()
        {
            // Load persistent global logging setting and apply it
            var globalLoggingEnabled = MvcExpressProjectSettings.GlobalLoggingEnabled;
            MvcLogInternal.LoggingEnabled = globalLoggingEnabled;
        }
    }
}

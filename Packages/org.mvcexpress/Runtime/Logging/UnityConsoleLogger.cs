using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace mvcExpress.Logging
{
    /// <summary>
    /// Default <see cref="IMvcLogger"/> implementation that forwards log entries to Unity's
    /// <c>Debug.Log</c> / <c>Debug.LogWarning</c> / <c>Debug.LogError</c> API.
    /// </summary>
    /// <remarks>
    /// When <c>MvcLogInternal.UseUnityDebugFallback</c> is <c>false</c>, all methods are no-ops
    /// so that the custom logger (e.g. MvcConsole) can suppress Unity console noise.
    ///
    /// Clickable navigation:
    /// Unity's console navigates to a source file when the log message contains a line of the form
    /// <c>"TypeName.Method (at /path/to/file:lineNumber)"</c>. This logger injects that line
    /// using the <see cref="MvcLogContext.FilePath"/> and <see cref="MvcLogContext.LineNumber"/>
    /// captured at the call site, so double-clicking the entry opens the user's code rather than
    /// a framework internal.
    ///
    /// The <c>CreateClickableLogObject</c> method is a placeholder; Unity cannot dynamically
    /// create objects pointing to arbitrary source locations, so it returns null and falls back
    /// to stack-trace-based navigation.
    /// </remarks>
    public sealed class UnityConsoleLogger : IMvcLogger
    {
        /// <summary>
        /// Writes an informational message to Unity's console when fallback logging is enabled.
        /// </summary>
#if UNITY_2023_1_OR_NEWER
        [HideInCallstack]
#endif
        public void Log(string message, MvcLogContext context)
        {
            LogWithContext(message, context, LogType.Log);
        }

        /// <summary>
        /// Writes a warning message to Unity's console when fallback logging is enabled.
        /// </summary>
#if UNITY_2023_1_OR_NEWER
        [HideInCallstack]
#endif
        public void LogWarning(string message, MvcLogContext context)
        {
            LogWithContext(message, context, LogType.Warning);
        }

        /// <summary>
        /// Writes an error message to Unity's console when fallback logging is enabled.
        /// </summary>
#if UNITY_2023_1_OR_NEWER
        [HideInCallstack]
#endif
        public void LogError(string message, MvcLogContext context)
        {
            LogWithContext(message, context, LogType.Error);
        }

#if UNITY_2023_1_OR_NEWER
        [HideInCallstack]
#endif
        private void LogWithContext(string message, MvcLogContext context, LogType logType)
        {
            // Unity's stack trace system:
            // - Automatically adds stack trace after the message
            // - Double-click navigates to the first valid file:line in the log entry
            // 
            // To make double-click navigate to the actual caller (e.g., SimpleViewMediatorBehaviour.cs:20),
            // we need to format the message so that the caller location appears FIRST.

            if (!string.IsNullOrEmpty(context.FilePath) && context.LineNumber > 0)
            {
                // Format as Unity expects: "Message\nMethodInfo (at FilePath:LineNumber)"
                // This makes the FilePath:Line appear at the top of the entry
                var location = $"{context.SourceTypeName ?? "Unknown"}.{context.MemberName}";
                var formattedMessage = $"{message}\n{location} (at {context.FilePath}:{context.LineNumber})";
                
                LogInternal(formattedMessage, logType);
            }
            else
            {
                LogInternal(message, logType);
            }
        }

        private UnityEngine.Object CreateClickableLogObject(MvcLogContext context)
        {
            // Unity will try to navigate to the ScriptableObject asset when double-clicked
            // Unfortunately, we can't dynamically create an object that points to arbitrary code
            // The only way to make Unity navigate to our code is through stack traces
            
            // Return null - Unity will fall back to stack trace navigation
            return null;
        }

#if UNITY_2023_1_OR_NEWER
        [HideInCallstack]
#endif
        private void LogInternal(string message, LogType logType)
        {
            // Check if Unity Debug.Log fallback is enabled
            if (!mvcExpress.Logging.MvcLogInternal.UseUnityDebugFallback)
                return; // Silent if fallback is disabled

            switch (logType)
            {
                case LogType.Error:
                    UnityEngine.Debug.LogError(message);
                    break;
                case LogType.Warning:
                    UnityEngine.Debug.LogWarning(message);
                    break;
                default:
                    UnityEngine.Debug.Log(message);
                    break;
            }
        }
    }
}

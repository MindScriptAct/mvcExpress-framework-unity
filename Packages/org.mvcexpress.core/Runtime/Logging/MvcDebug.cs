using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace mvcExpress.Logging
{
    /// <summary>
    /// Public logging facade for mvcExpress user code - the only logging class application code
    /// should interact with directly.
    /// </summary>
    /// <remarks>
    /// All methods are decorated with <c>[Conditional("UNITY_EDITOR")]</c>,
    /// <c>[Conditional("DEVELOPMENT_BUILD")]</c>, <c>[Conditional("UNITY_INCLUDE_TESTS")]</c>,
    /// and <c>[Conditional("MVC_LOGGING")]</c>, so every call site compiles to nothing in
    /// production builds unless the <c>MVC_LOGGING</c> scripting define is set. This means zero overhead -
    /// zero code - in clean release builds.
    ///
    /// Severity tiers:
    /// <list type="bullet">
    ///   <item><c>Log</c> / <c>LogFormat</c> - verbose framework lifecycle events. Only emitted
    ///   when per-module or global logging is enabled.</item>
    ///   <item><c>LogWarning</c> / <c>LogWarningFormat</c> - code smells and unusual states that
    ///   indicate possible misconfiguration.</item>
    ///   <item><c>LogError</c> / <c>LogErrorFormat</c> - non-critical problems the framework can
    ///   survive but the developer must know about. Stripped in clean release alongside all other
    ///   methods; use <c>throw</c> for errors that make further execution meaningless.</item>
    /// </list>
    ///
    /// Four operational modes (controlled by scripting define symbols):
    /// <list type="number">
    ///   <item><b>Editor default</b> (<c>UNITY_EDITOR</c>) - full logging to Unity console.</item>
    ///   <item><b>Clean release</b> (no symbols) - zero logging; only <c>throw</c> remains.</item>
    ///   <item><b>Editor + MvcConsole</b> (<c>UNITY_EDITOR</c> + <c>MVC_CONSOLE</c>) - full
    ///   logging routed to MvcConsole.</item>
    ///   <item><b>Release + MvcConsole</b> (<c>MVC_LOGGING</c> + <c>MVC_CONSOLE</c>) - full
    ///   logging in release, routed to MvcConsole.</item>
    /// </list>
    ///
    /// All calls capture caller file, line, and member name via <c>[CallerFilePath]</c>,
    /// <c>[CallerLineNumber]</c>, and <c>[CallerMemberName]</c> so the Unity console double-click
    /// navigates to the actual call site, not into the framework internals.
    ///
    /// On Unity 2023.1+ <c>[HideInCallstack]</c> is applied so the Unity console strips these
    /// wrapper frames from the displayed call stack.
    /// </remarks>
    public static class MvcDebug
    {
        /// <summary>
        /// Logs an informational message. Stripped in clean release builds.
        /// </summary>
#if UNITY_2023_1_OR_NEWER
        [HideInCallstack]
#endif
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_INCLUDE_TESTS"), Conditional("MVC_LOGGING")]
        public static void Log(
            object message,
            Type moduleType = default,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            if (message == null) return;

            var context = new MvcLogContext(
                filePath,
                lineNumber,
                memberName,
                moduleType: moduleType,
                category: MvcLogContext.LogCategory.Output);

            var msg = message.ToString();

            MvcLogInternal.InternalLog(msg, context);
        }

        /// <summary>
        /// Logs a warning message. Stripped in clean release builds.
        /// </summary>
#if UNITY_2023_1_OR_NEWER
        [HideInCallstack]
#endif
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_INCLUDE_TESTS"), Conditional("MVC_LOGGING")]
        public static void LogWarning(
            object message,
            Type moduleType = default,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            if (message == null) return;

            var context = new MvcLogContext(
                filePath,
                lineNumber,
                memberName,
                moduleType: moduleType,
                category: MvcLogContext.LogCategory.Output);

            var msg = message.ToString();

            MvcLogInternal.InternalLogWarning(msg, context);
        }

        /// <summary>
        /// Logs an error message. Stripped in clean release builds; use <c>throw</c> for
        /// errors that make further execution meaningless. Enable with <c>MVC_LOGGING</c>
        /// or <c>UNITY_EDITOR</c> to surface non-critical problems.
        /// </summary>
#if UNITY_2023_1_OR_NEWER
        [HideInCallstack]
#endif
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_INCLUDE_TESTS"), Conditional("MVC_LOGGING")]
        public static void LogError(
            object message,
            Type moduleType = default,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            if (message == null) return;

            var context = new MvcLogContext(
                filePath,
                lineNumber,
                memberName,
                moduleType: moduleType,
                category: MvcLogContext.LogCategory.Output);

            var msg = message.ToString();

            MvcLogInternal.InternalLogError(msg, context);
        }

        /// <summary>
        /// Logs a formatted informational message. Stripped in clean release builds.
        /// </summary>
#if UNITY_2023_1_OR_NEWER
        [HideInCallstack]
#endif
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_INCLUDE_TESTS"), Conditional("MVC_LOGGING")]
        public static void LogFormat(
            string format,
            Type moduleType = default,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "",
            params object[] args)
        {
            if (string.IsNullOrEmpty(format)) return;

            var context = new MvcLogContext(
                filePath,
                lineNumber,
                memberName,
                moduleType: moduleType,
                category: MvcLogContext.LogCategory.Output);

            var msg = string.Format(format, args);

            MvcLogInternal.InternalLog(msg, context);
        }

        /// <summary>
        /// Logs a formatted warning message. Stripped in clean release builds.
        /// </summary>
#if UNITY_2023_1_OR_NEWER
        [HideInCallstack]
#endif
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_INCLUDE_TESTS"), Conditional("MVC_LOGGING")]
        public static void LogWarningFormat(
            string format,
            Type moduleType = default,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "",
            params object[] args)
        {
            if (string.IsNullOrEmpty(format)) return;

            var context = new MvcLogContext(
                filePath,
                lineNumber,
                memberName,
                moduleType: moduleType,
                category: MvcLogContext.LogCategory.Output);

            var msg = string.Format(format, args);

            MvcLogInternal.InternalLogWarning(msg, context);
        }

        /// <summary>
        /// Logs a formatted error message. Stripped in clean release builds.
        /// </summary>
#if UNITY_2023_1_OR_NEWER
        [HideInCallstack]
#endif
        [Conditional("UNITY_EDITOR"), Conditional("DEVELOPMENT_BUILD"), Conditional("UNITY_INCLUDE_TESTS"), Conditional("MVC_LOGGING")]
        public static void LogErrorFormat(
            string format,
            Type moduleType = default,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "",
            params object[] args)
        {
            if (string.IsNullOrEmpty(format)) return;

            var context = new MvcLogContext(
                filePath,
                lineNumber,
                memberName,
                moduleType: moduleType,
                category: MvcLogContext.LogCategory.Output);

            var msg = string.Format(format, args);

            MvcLogInternal.InternalLogError(msg, context);
        }
    }
}

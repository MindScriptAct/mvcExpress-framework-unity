using System;

namespace mvcExpress.Logging
{
    /// <summary>
    /// Primary logger interface. Exactly one implementation is active at a time in
    /// <see cref="MvcLogInternal"/>. The default implementation is <see cref="UnityConsoleLogger"/>.
    /// </summary>
    /// <remarks>
    /// Replace the active logger by calling <c>MvcLogInternal.SetLogger(myLogger)</c> during
    /// application startup. This is useful for routing framework log output to a custom analytics
    /// backend, a test spy, or a rich editor tool.
    ///
    /// If you only need to observe log events without replacing Unity console output, implement
    /// <see cref="IMvcLoggerPlugin"/> instead and register with
    /// <c>MvcLogInternal.RegisterPlugin(myPlugin)</c>.
    /// </remarks>
    public interface IMvcLogger
    {
        /// <summary>
        /// Receives an informational log entry.
        /// </summary>
        void Log(string message, MvcLogContext context);

        /// <summary>
        /// Receives a warning log entry.
        /// </summary>
        void LogWarning(string message, MvcLogContext context);

        /// <summary>
        /// Receives an error log entry.
        /// </summary>
        void LogError(string message, MvcLogContext context);
    }
}

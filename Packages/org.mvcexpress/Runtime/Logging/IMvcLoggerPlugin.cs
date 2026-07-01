namespace mvcExpress.Logging
{
    /// <summary>
    /// Observer interface for tools that want to receive framework log events without replacing
    /// the primary <see cref="IMvcLogger"/>.
    /// </summary>
    /// <remarks>
    /// Multiple plugins can be registered simultaneously via <c>MvcLogInternal.RegisterPlugin</c>.
    /// Plugins receive every log entry that passes the global/module logging-enabled gate,
    /// in addition to the primary logger. This is used by the MvcConsole Pro Debugger to display
    /// structured framework events without intercepting Unity console output.
    ///
    /// Method names use the <c>On</c> prefix to distinguish them from <see cref="IMvcLogger"/>'s
    /// plain <c>Log</c>/<c>LogWarning</c>/<c>LogError</c> names and prevent accidental confusion
    /// when a class implements both interfaces.
    /// </remarks>
    [System.Obsolete("IMvcLoggerPlugin is replaced by mvcExpress.Plugins.ILogObserver. Register via MvcPluginBus.Register(observer).")]
    public interface IMvcLoggerPlugin
    {
        /// <summary>
        /// Called when an informational message is recorded.
        /// </summary>
        void OnLog(string message, MvcLogContext context);

        /// <summary>
        /// Called when a warning is recorded.
        /// </summary>
        void OnWarning(string message, MvcLogContext context);

        /// <summary>
        /// Called when an error is recorded.
        /// </summary>
        void OnError(string message, MvcLogContext context);
    }
}

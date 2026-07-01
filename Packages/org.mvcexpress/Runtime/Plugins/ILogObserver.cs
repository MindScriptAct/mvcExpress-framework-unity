namespace mvcExpress.Plugins
{
    /// <summary>
    /// Observer for framework log events. Production-safe - implement this to receive
    /// log, warning, and error entries from the framework (e.g. to forward to Sentry or a file).
    /// </summary>
    /// <remarks>
    /// Register via <see cref="MvcPluginBus.Register(ILogObserver)"/>.
    /// Replaces the obsolete <c>IMvcLoggerPlugin</c>.
    /// </remarks>
    public interface ILogObserver
    {
        /// <summary>Called when the framework emits an informational log entry.</summary>
        /// <param name="message">Formatted log message.</param>
        /// <param name="context">Metadata about the actor and module that produced the entry.</param>
        void OnLog(string message, mvcExpress.Logging.MvcLogContext context);

        /// <summary>Called when the framework emits a warning log entry.</summary>
        /// <param name="message">Formatted warning message.</param>
        /// <param name="context">Metadata about the actor and module that produced the entry.</param>
        void OnWarning(string message, mvcExpress.Logging.MvcLogContext context);

        /// <summary>Called when the framework emits an error log entry.</summary>
        /// <param name="message">Formatted error message.</param>
        /// <param name="context">Metadata about the actor and module that produced the entry.</param>
        void OnError(string message, mvcExpress.Logging.MvcLogContext context);
    }
}

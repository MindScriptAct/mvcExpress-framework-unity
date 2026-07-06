#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System;

namespace mvcExpress.Plugins
{
    /// <summary>
    /// Observer for command execution events. Editor/dev builds only.
    /// </summary>
    public interface ICommandObserver
    {
        /// <summary>Called each time a command finishes executing (both sync and async, after the task completes).</summary>
        /// <param name="commandType">Type of the command that executed.</param>
        /// <param name="messageType">Message type that triggered the command.</param>
        /// <param name="moduleType">Module that owns the command binding.</param>
        void OnCommandExecuted(Type commandType, Type messageType, Type moduleType);
    }
}
#endif

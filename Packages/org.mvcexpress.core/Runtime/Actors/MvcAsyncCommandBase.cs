using System.Threading;

namespace mvcExpress
{
    /// <summary>
    /// Marker base for all asynchronous commands, including generic <c>CommandAsync{T...}</c> variants.
    /// </summary>
    /// <remarks>
    /// Also carries <see cref="CancelToken"/> - the sole member available to async commands and
    /// not to synchronous ones. This class's other purpose is letting the command processor
    /// distinguish asynchronous from synchronous commands at bind time without any runtime
    /// reflection or virtual-dispatch overhead. When <c>BindCommand&lt;TCmd, TMsg&gt;()</c> is called,
    /// the binder checks <c>is MvcAsyncCommandBase</c> vs <c>is MvcSyncCommandBase</c> once and stores
    /// the appropriate async dispatch delegate, keeping the hot-path allocation-free.
    /// Prefer <see cref="MvcSyncCommandBase"/> (i.e. <c>Command</c>) for operations that do not
    /// need to await I/O or coroutines; async dispatch carries a <c>Task</c> allocation per execution.
    /// </remarks>
    public abstract class MvcAsyncCommandBase : MvcCommandBase
    {
        /// <summary>
        /// Cancelled when this command's owning module is destroyed. Refreshed on every
        /// execution, so pooled/reused command instances always read the current module's token.
        /// Pass this to any cancellable async API (<c>Task.Delay(ms, CancelToken)</c>,
        /// <c>HttpClient</c> calls, etc.) or poll <c>CancelToken.IsCancellationRequested</c>
        /// in a loop.
        /// </summary>
        protected CancellationToken CancelToken => CancelTokenInternal;
    }
}

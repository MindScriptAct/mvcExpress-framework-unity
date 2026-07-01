namespace mvcExpress
{
    /// <summary>
    /// Marker base for all asynchronous commands, including generic <c>CommandAsync{T...}</c> variants.
    /// </summary>
    /// <remarks>
    /// This class carries no members - its sole purpose is to let the command processor distinguish
    /// asynchronous from synchronous commands at bind time without any runtime reflection or
    /// virtual-dispatch overhead. When <c>BindCommand&lt;TCmd, TMsg&gt;()</c> is called, the binder
    /// checks <c>is MvcAsyncCommandBase</c> vs <c>is MvcSyncCommandBase</c> once and stores the
    /// appropriate async dispatch delegate, keeping the hot-path allocation-free.
    /// Prefer <see cref="MvcSyncCommandBase"/> (i.e. <c>Command</c>) for operations that do not
    /// need to await I/O or coroutines; async dispatch carries a <c>Task</c> allocation per execution.
    /// </remarks>
    public abstract class MvcAsyncCommandBase : MvcCommandBase
    {
    }
}

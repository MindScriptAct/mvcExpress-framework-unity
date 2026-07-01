namespace mvcExpress
{
    /// <summary>
    /// Marker base for all synchronous commands, including generic <c>Command{T...}</c> variants.
    /// </summary>
    /// <remarks>
    /// This class carries no members - its sole purpose is to let the command processor distinguish
    /// synchronous from asynchronous commands at bind time without any runtime reflection or
    /// virtual-dispatch overhead. When <c>BindCommand&lt;TCmd, TMsg&gt;()</c> is called, the binder
    /// checks <c>is MvcSyncCommandBase</c> vs <c>is MvcAsyncCommandBase</c> once and stores the
    /// appropriate dispatch delegate, keeping the hot-path allocation-free.
    /// </remarks>
    public abstract class MvcSyncCommandBase : MvcCommandBase
    {
    }
}

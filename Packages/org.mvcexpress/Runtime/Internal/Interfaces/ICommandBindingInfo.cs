namespace mvcExpress.Internal.Interfaces
{
    /// <summary>
    /// Lightweight binding-query interface.
    /// Intended for tests/debug/introspection without introducing new tracking structures.
    /// </summary>
    public interface ICommandBindingInfo
    {
        bool HasMessageBindings<TMessage>() where TMessage : IMessageBase;
        bool HasBindings(System.Type messageType);

        bool IsBound<TCommand, TMessage>()
            where TCommand : MvcCommandBase
            where TMessage : IMessageBase;

        bool IsBound(System.Type commandType, System.Type messageType);

        int GetCommandBindingCount<TCommand>() where TCommand : MvcCommandBase;
        int GetBindingCountForCommand(System.Type commandType);

        int GetBoundMessageCount();
    }
}

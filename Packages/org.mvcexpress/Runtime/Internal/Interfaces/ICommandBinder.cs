using mvcExpress;

namespace mvcExpress.Internal.Interfaces
{
    /// <summary>
    /// Generic command binding interface for zero-allocation, type-safe command execution.
    /// Commands are registered directly as message handlers - no separate dictionaries needed.
    /// Supports pooled commands and singleton commands.
    /// </summary>
    public interface ICommandBinder
    {
        // ==================== Bind Methods ====================

        void BindCommand<TCommand, TMessage>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage;

        void BindCommand<TCommand, TMessage, T1>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1>;

        void BindCommand<TCommand, TMessage, T1, T2>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2>;

        void BindCommand<TCommand, TMessage, T1, T2, T3>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3>;

        void BindCommand<TCommand, TMessage, T1, T2, T3, T4>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4>;

        void BindCommand<TCommand, TMessage, T1, T2, T3, T4, T5>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5>;

        void BindCommand<TCommand, TMessage, T1, T2, T3, T4, T5, T6>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6>;

        void BindCommand<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7>;

        void BindCommand<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8>;

        void BindCommand<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9>;

        void BindCommand<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>;

        void BindCommand<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>;

        void BindCommand<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>;

        // ==================== Singleton Bind Methods ====================

        // ==================== Unbind Method ====================

        void UnbindCommand<TCommand, TMessage>()
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage;
    }
}

using mvcExpress.Internal.Interfaces;

namespace mvcExpress.Internal.Interfaces
{
    /// <summary>
    /// Generic command binding interface for zero-allocation, type-safe command execution.
    /// Commands are registered directly as message handlers - no separate dictionaries needed.
    /// Supports pooled async commands and singleton async commands.
    /// </summary>
    public interface ICommandAsyncBinder
    {
        // ==================== Bind Methods ====================

        void BindCommandAsync<TCommand, TMessage>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage;

        void BindCommandAsync<TCommand, TMessage, T1>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1>;

        void BindCommandAsync<TCommand, TMessage, T1, T2>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2>;

        void BindCommandAsync<TCommand, TMessage, T1, T2, T3>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3>;

        void BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4>;

        void BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4, T5>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5>;

        void BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6>;

        void BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7>;

        void BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8>;

        void BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9>;

        void BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10>;

        void BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11>;

        void BindCommandAsync<TCommand, TMessage, T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>(uint poolSize = 0)
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage<T1, T2, T3, T4, T5, T6, T7, T8, T9, T10, T11, T12>;

        // ==================== Unbind Method ====================
        void UnbindCommandAsync<TCommand, TMessage>()
            where TCommand : MvcCommandBase, new()
            where TMessage : IMessage;
    }
}

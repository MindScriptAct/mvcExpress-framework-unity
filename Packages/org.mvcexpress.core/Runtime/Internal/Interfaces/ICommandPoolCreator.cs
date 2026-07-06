namespace mvcExpress.Internal.Interfaces
{
    /// <summary>
    /// Interface for command pool creator.
    /// </summary>
    public interface ICommandPoolCreator
    {
        // ==================== Create Pool ====================

        /// <summary>
        /// Create or reconfigure a pool for the specified command type.
        /// If pool already exists, it will be cleared and replaced with new configuration.
        /// </summary>
        /// <typeparam name="TCommand">The command type to pool.</typeparam>
        /// <param name="poolSize">Maximum pool size (0 = no pooling, objects discarded after use).</param>
        void CreatePool<TCommand>(uint poolSize) where TCommand : MvcCommandBase, new();
    }
}

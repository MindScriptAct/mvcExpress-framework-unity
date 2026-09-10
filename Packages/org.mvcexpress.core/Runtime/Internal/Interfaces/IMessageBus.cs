namespace mvcExpress.Internal.Interfaces
{
    /// <summary>
    /// Complete interface for the application's single message bus, combining subscription and publishing.
    /// </summary>
    /// <remarks>
    /// The bus is owned by <see cref="MvcFacade"/> and shared by every module. Implementations do
    /// not represent module-local or target-module messaging scopes.
    /// </remarks>
    public interface IMessageBus : IMessageSubscribeManager, IMessagePublisher { }
}
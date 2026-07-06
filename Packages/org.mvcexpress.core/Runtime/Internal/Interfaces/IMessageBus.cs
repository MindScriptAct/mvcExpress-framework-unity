namespace mvcExpress.Internal.Interfaces
{
    /// <summary>
    /// Complete message bus interface combining subscription and publishing.
    /// </summary>
    public interface IMessageBus : IMessageSubscribeManager, IMessagePublisher { }
}
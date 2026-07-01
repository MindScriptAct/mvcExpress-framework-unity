namespace mvcExpress.Internal.Interfaces
{
    /// <summary>
    /// Processes commands by binding them to messages and executing them directly.
    /// </summary>
    public interface ICommandProcessor : ICommandBinder, ICommandRunner, ICommandPoolCreator, ICommandBindingInfo { }
}

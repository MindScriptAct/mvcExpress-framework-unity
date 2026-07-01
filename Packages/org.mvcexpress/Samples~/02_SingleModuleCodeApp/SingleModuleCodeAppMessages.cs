using mvcExpress;

namespace mvcExpress.Samples.SingleModuleCodeApp
{
    // View intent: the player clicked a button that should add score.
    public readonly struct CodeAddScoreClickedMessage : IMessage<int> { }

    // View intent: the player clicked the reset button.
    public readonly struct CodeResetScoreClickedMessage : IMessage { }

    // Model update: the score value changed inside the proxy.
    public readonly struct CodeScoreChangedMessage : IMessage<int> { }
}

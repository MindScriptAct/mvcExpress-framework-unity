using mvcExpress;

namespace mvcExpress.Samples.SingleModuleUnityApp
{
    // View intent: the player clicked a button that should add score.
    public readonly struct AddScoreClickedMessage : IMessage<int> { }

    // View intent: the player clicked the reset button.
    public readonly struct ResetScoreClickedMessage : IMessage { }

    // Model update: the score value changed inside the proxy.
    public readonly struct ScoreChangedMessage : IMessage<int> { }
}

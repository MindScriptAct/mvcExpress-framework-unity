using mvcExpress;

namespace mvcExpress.Samples.SingleModuleAttributeApp
{
    // View intent: the player clicked a button that should add score.
    public readonly struct AttributeAddScoreClickedMessage : IMessage<int> { }

    // View intent: the player clicked the reset button.
    public readonly struct AttributeResetScoreClickedMessage : IMessage { }

    // Model update: the score value changed inside the proxy.
    public readonly struct AttributeScoreChangedMessage : IMessage<int> { }
}

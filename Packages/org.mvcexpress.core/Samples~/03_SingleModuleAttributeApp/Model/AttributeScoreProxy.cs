using mvcExpress;

namespace mvcExpress.Samples.SingleModuleAttributeApp.Model
{
    // [Register] creates and registers this proxy for the target module.
    [Register(
        typeof(SingleModuleAttributeAppModule),
        RegisterToLogic = true,
        RegisterToView = true,
        ViewInterface = typeof(IAttributeScoreReadModel))]
    public sealed class AttributeScoreProxy : Proxy, IAttributeScoreReadModel
    {
        private const int StartScore = 0;

        public int Score { get; private set; }

        protected override void OnInitialized()
        {
            Score = StartScore;
        }

        public void Add(int amount)
        {
            Score += amount;
            PublishScoreChanged();
        }

        public void ResetScore()
        {
            Score = StartScore;
            PublishScoreChanged();
        }

        private void PublishScoreChanged()
        {
            // Data-change messages come from the model, not from commands.
            Messenger.Publish<AttributeScoreChangedMessage, int>(Score);
        }
    }

    // The view can read the current score, but it cannot mutate the model.
    public interface IAttributeScoreReadModel
    {
        int Score { get; }
    }
}

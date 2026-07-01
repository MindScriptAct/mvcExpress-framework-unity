using mvcExpress;

namespace mvcExpress.Samples.SingleModuleCodeApp.Model
{
    // Code-only Proxy owns score state and sends messages when the state changes.
    public sealed class CodeScoreProxy : Proxy, ICodeScoreReadModel
    {
        private readonly int _startScore;

        public CodeScoreProxy(int startScore)
        {
            _startScore = startScore;
        }

        public int Score { get; private set; }

        protected override void OnInitialized()
        {
            Score = _startScore;
        }

        public void Add(int amount)
        {
            Score += amount;
            PublishScoreChanged();
        }

        public void ResetScore()
        {
            Score = _startScore;
            PublishScoreChanged();
        }

        private void PublishScoreChanged()
        {
            // Data-change messages come from the model, not from commands.
            Messenger.Publish<CodeScoreChangedMessage, int>(Score);
        }
    }


    // The view can read the current score, but it cannot mutate the model.
    public interface ICodeScoreReadModel
    {
        int Score { get; }
    }
}
using mvcExpress;
using UnityEngine;

namespace mvcExpress.Samples.SingleModuleUnityApp.Model
{
    // ProxyBehaviour is the model layer for Unity-backed state.
    // It owns the score and notifies the app when that state changes.
    public sealed class SampleScoreProxyBehaviour : ProxyBehaviour
    {
        [SerializeField] private int _startScore;

        public int Score { get; private set; }

        protected override void OnInitialized()
        {
            Score = _startScore;
        }

        public int Add(int amount)
        {
            Score += amount;
            PublishScoreChanged();
            return Score;
        }

        public int ResetScore()
        {
            Score = _startScore;
            PublishScoreChanged();
            return Score;
        }

        private void PublishScoreChanged()
        {
            // Data-change messages come from the model, not from commands.
            Messenger.Publish<ScoreChangedMessage, int>(Score);
        }
    }
}

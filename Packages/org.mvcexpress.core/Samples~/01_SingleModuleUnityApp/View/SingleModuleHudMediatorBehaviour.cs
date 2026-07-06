using mvcExpress;
using mvcExpress.Samples.SingleModuleUnityApp.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace mvcExpress.Samples.SingleModuleUnityApp.View
{
    // Mediators translate Unity UI events into messages and react to model updates.
    public sealed class SingleModuleHudMediatorBehaviour : MediatorBehaviour
    {
        [SerializeField] private TMP_Text _scoreLabel;
        [SerializeField] private Button _addOneButton;
        [SerializeField] private Button _addFiveButton;
        [SerializeField] private Button _resetButton;

        [Inject] private SampleScoreFormatterService _formatter;

        protected override void OnInitialized()
        {
            // Listen for model changes published by the proxy.
            Messenger.Subscribe<ScoreChangedMessage, int>(OnScoreChanged);

            if (_addOneButton != null)
            {
                _addOneButton.onClick.AddListener(AddOne);
            }

            if (_addFiveButton != null)
            {
                _addFiveButton.onClick.AddListener(AddFive);
            }

            if (_resetButton != null)
            {
                _resetButton.onClick.AddListener(ResetScore);
            }

            SetScoreText(_formatter.Format(0));
        }


        public void AddOne()
        {
            // UI intent is sent as a message; a command handles the action.
            Messenger.Publish<AddScoreClickedMessage, int>(1);
        }

        public void AddFive()
        {
            // Same command, different payload.
            Messenger.Publish<AddScoreClickedMessage, int>(5);
        }

        public void ResetScore()
        {
            // Parameterless intent messages are useful for simple UI actions.
            Messenger.Publish<ResetScoreClickedMessage>();
        }

        private void OnScoreChanged(int score)
        {
            SetScoreText(_formatter.Format(score));
        }

        private void SetScoreText(string text)
        {
            if (_scoreLabel != null)
            {
                _scoreLabel.text = text;
            }
        }


        protected override void OnCleanup()
        {
            if (_addOneButton != null)
            {
                _addOneButton.onClick.RemoveListener(AddOne);
            }

            if (_addFiveButton != null)
            {
                _addFiveButton.onClick.RemoveListener(AddFive);
            }

            if (_resetButton != null)
            {
                _resetButton.onClick.RemoveListener(ResetScore);
            }
        }
    }
}

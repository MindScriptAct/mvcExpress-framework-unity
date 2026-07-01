using mvcExpress;
using mvcExpress.Samples.SingleModuleAttributeApp.Model;
using mvcExpress.Samples.SingleModuleAttributeApp.Services;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace mvcExpress.Samples.SingleModuleAttributeApp.View
{
    // [Attach] finds the HUD mediator when it already exists in the scene/container at module startup.
    [Attach(typeof(SingleModuleAttributeAppModule), FindInScene = true)]
    public sealed class SingleModuleAttributeHudMediatorBehaviour : MediatorBehaviour
    {
        [SerializeField] private TMP_Text _scoreLabel;
        [SerializeField] private Button _addOneButton;
        [SerializeField] private Button _addFiveButton;
        [SerializeField] private Button _resetButton;

        [Inject] private IAttributeScoreReadModel _scoreModel;
        [Inject] private AttributeScoreFormatterService _formatter;

        protected override void OnInitialized()
        {
            // Listen for model changes published by the proxy.
            Messenger.Subscribe<AttributeScoreChangedMessage, int>(OnScoreChanged);

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

            SetScoreText(_formatter.Format(_scoreModel.Score));
        }

        public void AddOne()
        {
            // UI intent is sent as a message; an attribute-bound command handles it.
            Messenger.Publish<AttributeAddScoreClickedMessage, int>(1);
        }

        public void AddFive()
        {
            // Same command, different payload.
            Messenger.Publish<AttributeAddScoreClickedMessage, int>(5);
        }

        public void ResetScore()
        {
            // Parameterless intent messages are useful for simple UI actions.
            Messenger.Publish<AttributeResetScoreClickedMessage>();
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

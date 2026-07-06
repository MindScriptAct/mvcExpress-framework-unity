using mvcExpress;
using UnityEngine;

namespace mvcExpress.Samples.SingleModuleUnityApp.Services
{
    // Services contain reusable logic that is not itself application state.
    public sealed class SampleScoreFormatterService : MonoBehaviour, IMvcLifecycle
    {
        [SerializeField] private string _label = "Score";

        private string _prefix;

        public void OnInitialized()
        {
            _prefix = string.IsNullOrWhiteSpace(_label) ? "Score" : _label.Trim();
        }

        public void OnCleanup() { }

        public string Format(int score)
        {
            return $"{_prefix}: {score}";
        }
    }
}

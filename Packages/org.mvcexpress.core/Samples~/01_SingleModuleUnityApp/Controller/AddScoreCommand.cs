using mvcExpress;
using mvcExpress.Samples.SingleModuleUnityApp.Model;

namespace mvcExpress.Samples.SingleModuleUnityApp.Controller
{
    // Commands handle one action. They do not own state and do not update the UI.
    public sealed class AddScoreCommand : Command<int>
    {
        [Inject] private SampleScoreProxyBehaviour _scoreProxy;

        public override void Execute(int amount)
        {
            // The proxy owns score state and publishes the data-change message.
            _scoreProxy.Add(amount);
        }
    }
}

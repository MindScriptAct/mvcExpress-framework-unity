using mvcExpress;
using mvcExpress.Samples.SingleModuleUnityApp.Model;

namespace mvcExpress.Samples.SingleModuleUnityApp.Controller
{
    // A command is a small action triggered by a message.
    public sealed class ResetScoreCommand : Command
    {
        [Inject] private SampleScoreProxyBehaviour _scoreProxy;

        public override void Execute()
        {
            // Resetting state still goes through the proxy, keeping model ownership clear.
            _scoreProxy.ResetScore();
        }
    }
}

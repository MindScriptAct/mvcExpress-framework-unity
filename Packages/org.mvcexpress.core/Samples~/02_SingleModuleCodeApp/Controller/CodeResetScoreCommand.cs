using mvcExpress;
using mvcExpress.Samples.SingleModuleCodeApp.Model;

namespace mvcExpress.Samples.SingleModuleCodeApp.Controller
{
    // A command is a small action triggered by a message.
    public sealed class CodeResetScoreCommand : Command
    {
        [Inject] private CodeScoreProxy _scoreProxy;

        public override void Execute()
        {
            // Resetting state still goes through the proxy, keeping model ownership clear.
            _scoreProxy.ResetScore();
        }
    }
}

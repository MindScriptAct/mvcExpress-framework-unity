using mvcExpress;
using mvcExpress.Samples.SingleModuleCodeApp.Model;

namespace mvcExpress.Samples.SingleModuleCodeApp.Controller
{
    // Commands handle one action. They do not own state and do not update the UI.
    public sealed class CodeAddScoreCommand : Command<int>
    {
        [Inject] private CodeScoreProxy _scoreProxy;

        public override void Execute(int amount)
        {
            // The proxy owns score state and publishes the data-change message.
            _scoreProxy.Add(amount);
        }
    }
}

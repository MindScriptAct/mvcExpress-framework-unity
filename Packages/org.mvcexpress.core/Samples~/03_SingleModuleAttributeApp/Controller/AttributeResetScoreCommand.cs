using mvcExpress;
using mvcExpress.Samples.SingleModuleAttributeApp.Model;

namespace mvcExpress.Samples.SingleModuleAttributeApp.Controller
{
    // This command is discovered and bound through its [Bind] attribute.
    [Bind(typeof(AttributeResetScoreClickedMessage), typeof(SingleModuleAttributeAppModule))]
    public sealed class AttributeResetScoreCommand : Command
    {
        [Inject] private AttributeScoreProxy _scoreProxy;

        public override void Execute()
        {
            // Resetting state still goes through the proxy, keeping model ownership clear.
            _scoreProxy.ResetScore();
        }
    }
}

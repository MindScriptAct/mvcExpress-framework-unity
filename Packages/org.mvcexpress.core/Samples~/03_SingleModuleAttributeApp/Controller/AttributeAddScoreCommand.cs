using mvcExpress;
using mvcExpress.Samples.SingleModuleAttributeApp.Model;

namespace mvcExpress.Samples.SingleModuleAttributeApp.Controller
{
    // [Bind] maps this command to a message for the target module.
    [Bind(typeof(AttributeAddScoreClickedMessage), typeof(SingleModuleAttributeAppModule))]
    public sealed class AttributeAddScoreCommand : Command<int>
    {
        [Inject] private AttributeScoreProxy _scoreProxy;

        public override void Execute(int amount)
        {
            // The proxy owns score state and publishes the data-change message.
            _scoreProxy.Add(amount);
        }
    }
}

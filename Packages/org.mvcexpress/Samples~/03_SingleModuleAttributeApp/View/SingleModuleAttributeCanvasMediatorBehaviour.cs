using mvcExpress;

namespace mvcExpress.Samples.SingleModuleAttributeApp.View
{
    // Root view container mediator. Code decides when this container prefab enters the module.
    [AttachPrefab(typeof(SingleModuleAttributeAppModule))]
    public sealed class SingleModuleAttributeCanvasMediatorBehaviour : MediatorBehaviour
    {
    }
}

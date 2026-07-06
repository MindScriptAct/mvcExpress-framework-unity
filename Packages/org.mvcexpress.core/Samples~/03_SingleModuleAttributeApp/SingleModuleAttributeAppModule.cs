using mvcExpress;

namespace mvcExpress.Samples.SingleModuleAttributeApp
{
    // Attribute-first module: the module stays almost empty.
    // [Register], [Bind], and [Attach] attributes provide the composition.
    [StartupModule]
    public sealed class SingleModuleAttributeAppModule : MvcModule
    {
    }
}

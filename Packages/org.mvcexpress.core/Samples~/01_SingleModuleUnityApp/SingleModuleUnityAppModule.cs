using mvcExpress;

namespace mvcExpress.Samples.SingleModuleUnityApp
{
    // The module is the composition root for this sample scene.
    // Services, proxies, commands, and mediators are wired through the Unity registries.
    public sealed class SingleModuleUnityAppModule : MvcModule
    {
        // No BindCommands override here on purpose:
        // this sample demonstrates command binding through the Command registry.
    }
}

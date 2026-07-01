using mvcExpress;

namespace mvcExpress.Tests.Fakes
{
    // Overrides Awake/OnDestroy with empty bodies so no MvcFacade singleton is created.
    // Use when tests need a MvcModule reference without full Unity initialization.
    public class FakeModule : MvcModule
    {
        protected override void Awake()     {}
        protected override void OnDestroy() {}
    }
}

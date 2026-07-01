using mvcExpress;

namespace mvcExpress.Tests.Fakes
{
    public class SpyMediator : MediatorBehaviour
    {
        public int OnInitializedCount { get; private set; }
        public int OnCleanupCount { get; private set; }

        protected override void OnInitialized() => OnInitializedCount++;
        protected override void OnCleanup() => OnCleanupCount++;
    }
}

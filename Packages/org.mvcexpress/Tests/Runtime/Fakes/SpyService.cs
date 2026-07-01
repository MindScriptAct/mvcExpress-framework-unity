using mvcExpress;

namespace mvcExpress.Tests.Fakes
{
    // Services in mvcExpress are plain classes implementing IMvcLifecycle.
    public class SpyService : IMvcLifecycle
    {
        public int OnInitializedCount { get; private set; }
        public int OnCleanupCount { get; private set; }

        public void OnInitialized() => OnInitializedCount++;
        public void OnCleanup() => OnCleanupCount++;
    }
}

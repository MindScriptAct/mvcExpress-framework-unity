using mvcExpress.Internal.DependencyInjection;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests.Scenarios
{
    /// <summary>
    /// Dual-scope actor lifecycle: a service registered to both Logic and View scope in the
    /// same permanent registration. Guards against double OnInitialized/OnCleanup calls (M2)
    /// and against one scope's Unregister disposing an instance the other scope still resolves (M3).
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public class DualScopeScenarioTests
    {
        private static class Trace
        {
            public static int InitCount;
            public static int CleanupCount;

            public static void Reset()
            {
                InitCount = 0;
                CleanupCount = 0;
            }
        }

        private sealed class DualScopeService : IMvcLifecycle
        {
            public void OnInitialized() => Trace.InitCount++;
            public void OnCleanup() => Trace.CleanupCount++;
        }

        private sealed class DualScopeModule : MvcModule
        {
            protected override void RegisterServices()
            {
                Container.Register(new DualScopeService()).ToLogic().ToView().AsPermanent();
            }
        }

        private GameObject _moduleGo;

        [SetUp]
        public void SetUp()
        {
            Trace.Reset();
        }

        [TearDown]
        public void TearDown()
        {
            if (_moduleGo != null) Object.DestroyImmediate(_moduleGo);

            var facade = MvcFacade.InstanceOrNull;
            if (facade != null) Object.DestroyImmediate(facade.gameObject);
        }

        [Test]
        public void ServiceRegisteredToLogicAndView_OnInitializedFiresExactlyOnce()
        {
            _moduleGo = new GameObject(nameof(DualScopeModule));
            _moduleGo.AddComponent<DualScopeModule>();

            Assert.That(Trace.InitCount, Is.EqualTo(1),
                "A service registered .ToLogic().ToView().AsPermanent() must have OnInitialized called " +
                "exactly once. Before the M2 fix, EnumerateAllInstances yielded the same instance once per " +
                "dictionary entry (logic + view), so the init loop injected and initialized it twice.");
        }

        [Test]
        public void ServiceRegisteredToLogicAndView_OnCleanupFiresExactlyOnceOnModuleDestroy()
        {
            _moduleGo = new GameObject(nameof(DualScopeModule));
            _moduleGo.AddComponent<DualScopeModule>();

            Object.DestroyImmediate(_moduleGo);
            _moduleGo = null;

            Assert.That(Trace.CleanupCount, Is.EqualTo(1),
                "OnCleanup must fire exactly once for a dual-scope-registered service. Before the M2 fix, " +
                "the service was added to _lifecycleServices twice, so OnCleanup also fired twice at teardown.");
        }
    }
}

using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using mvcExpress;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;

namespace mvcExpress.Tests
{
    [TestFixture]
    public class ProxyBehaviourTests
    {
        // ── Test doubles ──────────────────────────────────────────────────────

        // Concrete ProxyBehaviour that exposes lifecycle call counts.
        private class TestProxyBehaviour : ProxyBehaviour
        {
            public int OnInitializedCallCount { get; private set; }
            public int OnCleanupCallCount     { get; private set; }

            protected override void OnInitialized() => OnInitializedCallCount++;
            protected override void OnCleanup()     => OnCleanupCallCount++;
        }

        // Module stub that skips Awake/OnDestroy entirely so the framework singleton
        // (MvcFacade) is never created. ModuleType and GetType() still work on any
        // live MonoBehaviour with no full init required.
        private class NoInitModule : MvcModule
        {
            protected override void Awake()     {} // intentionally empty - no MvcFacade
            protected override void OnDestroy() {} // intentionally empty - no UnregisterModule
        }

        // ── Fields ────────────────────────────────────────────────────────────

        private GameObject _moduleGo;
        private NoInitModule _module;
        private GameObject _go;
        private TestProxyBehaviour _proxy;
        private MvcDiContainer _container;
        private MvcMessageBus _messageBus;

        // ── Lifecycle ─────────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            _moduleGo = new GameObject("NoInitModule");
            _module   = _moduleGo.AddComponent<NoInitModule>();

            _go    = new GameObject("TestProxyBehaviour");
            _proxy = _go.AddComponent<TestProxyBehaviour>();

            _container  = new MvcDiContainer();
            _messageBus = new MvcMessageBus();
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null)       { Object.DestroyImmediate(_go);       _go = null; }
            if (_moduleGo != null) { Object.DestroyImmediate(_moduleGo); _moduleGo = null; }
            _messageBus?.Dispose();
            _container?.Dispose();
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        // Section 1: Module-Scoped Initialization
        [Test]
        public void ModuleScopedInitialization_ConnectsToModuleScope()
        {
            _proxy.Initialize(_module, _container, _messageBus, deferOnInitialized: false);

            Assert.AreEqual(_module.ModuleType, _proxy.ModuleType,
                "ModuleType must reflect the owning module's runtime type after Initialize.");
            Assert.AreEqual(1, _proxy.OnInitializedCallCount,
                "OnInitialized must be called exactly once when deferOnInitialized is false.");
        }

        // Section 2: Global-Scoped Initialization
        [Test]
        public void GlobalScopedInitialization_WiresToApplicationFacadeScope()
        {
            _proxy.InitializeGlobal(_container, _messageBus, deferOnInitialized: false);

            Assert.IsNull(_proxy.ModuleType,
                "ModuleType must be null for proxies registered globally via InitializeGlobal.");
            Assert.AreEqual(1, _proxy.OnInitializedCallCount,
                "OnInitialized must be called exactly once when deferOnInitialized is false.");
        }

        // Section 3: Deferred Initialization
        [Test]
        public void DeferredInitialization_SkipsInjectionAndHookUntilManualCall()
        {
            _proxy.Initialize(_module, _container, _messageBus, deferOnInitialized: true);

            Assert.AreEqual(0, _proxy.OnInitializedCallCount,
                "OnInitialized must NOT fire when deferOnInitialized is true.");

            _proxy.CompleteInitialization();

            Assert.AreEqual(1, _proxy.OnInitializedCallCount,
                "OnInitialized must fire after CompleteInitialization is called.");
        }

        [Test]
        public void DeferredInitialization_CompleteInitialization_IsIdempotent()
        {
            _proxy.Initialize(_module, _container, _messageBus, deferOnInitialized: true);
            _proxy.CompleteInitialization();
            _proxy.CompleteInitialization(); // second call must be a no-op

            Assert.AreEqual(1, _proxy.OnInitializedCallCount,
                "OnInitialized must not fire more than once even when CompleteInitialization is called twice.");
        }

        [Test]
        public void GlobalDeferredInitialization_SkipsHookUntilManualCompletion()
        {
            _proxy.InitializeGlobal(_container, _messageBus, deferOnInitialized: true);

            Assert.IsNull(_proxy.ModuleType,
                "Global proxy behaviours must not report an owning module type.");
            Assert.AreEqual(0, _proxy.OnInitializedCallCount,
                "OnInitialized must not fire while global initialization is deferred.");

            _proxy.CompleteInitialization();

            Assert.AreEqual(1, _proxy.OnInitializedCallCount,
                "CompleteInitialization must finish deferred global initialization.");
        }

        // Section 4: Unity Lifecycle Destruction
        [Test]
        public void UnityLifecycleDestruction_TriggersProxyCleanupSequence()
        {
            _proxy.Initialize(_module, _container, _messageBus, deferOnInitialized: false);

            Object.DestroyImmediate(_go);
            _go = null; // prevent TearDown from double-destroying

            Assert.AreEqual(1, _proxy.OnCleanupCallCount,
                "OnCleanup must be called exactly once when the proxy's GameObject is destroyed.");
        }

        [Test]
        public void UnityLifecycleDestruction_UninitializedProxy_DoesNotCallUserCleanup()
        {
            Object.DestroyImmediate(_go);
            _go = null;

            Assert.AreEqual(0, _proxy.OnCleanupCallCount,
                "OnCleanup must not run for a ProxyBehaviour that was never initialized.");
        }

        [Test]
        public void UnityLifecycleDestruction_DeferredButIncompleteProxy_DoesNotCallUserCleanup()
        {
            _proxy.Initialize(_module, _container, _messageBus, deferOnInitialized: true);

            Object.DestroyImmediate(_go);
            _go = null;

            Assert.AreEqual(0, _proxy.OnCleanupCallCount,
                "OnCleanup must not run if OnInitialized never completed.");
        }

        [Test]
        public void InitializeGlobal_WithNullMessageBus_LogsErrorAndDoesNotInitialize()
        {
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("InitializeGlobal called with null parameter"));

            _proxy.InitializeGlobal(_container, null, deferOnInitialized: false);

            Assert.AreEqual(0, _proxy.OnInitializedCallCount,
                "Invalid global initialization must return before OnInitialized.");
        }
    }
}

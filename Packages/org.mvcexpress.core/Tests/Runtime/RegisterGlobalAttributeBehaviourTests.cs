using NUnit.Framework;
using mvcExpress;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Initialization;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests
{
    /// <summary>
    /// End-to-end behavior tests for the [RegisterGlobal] attribute feature.
    /// Verifies that types decorated with [RegisterGlobal] are resolvable from
    /// the global DI container after MvcFacade initializes.
    /// </summary>
    /// <remarks>
    /// Fake types (GlobalMockProxy, GlobalMockService, GlobalMockProxyWithInterface,
    /// IGlobalMockProxyInterface) are defined in RegisterGlobalAttributeTests.cs.
    /// </remarks>
    [TestFixture]
    public class RegisterGlobalAttributeBehaviourTests
    {
        // ── Fields ────────────────────────────────────────────────────────────

        private System.Collections.Generic.List<GameObject> _createdGameObjects
            = new System.Collections.Generic.List<GameObject>();

        // ── Lifecycle ─────────────────────────────────────────────────────────

        [SetUp]
        public void SetUp()
        {
            var existing = MvcFacade.InstanceOrNull;
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);
        }

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _createdGameObjects)
            {
                if (go != null)
                    Object.DestroyImmediate(go);
            }
            _createdGameObjects.Clear();

            var facade = MvcFacade.InstanceOrNull;
            if (facade != null)
                Object.DestroyImmediate(facade.gameObject);

            // Clear scanner cache so attribute registrations do not leak between tests.
            AttributeScanner.Reset();
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        [Test]
        public void RegisterGlobal_PlainProxy_IsResolvableFromGlobalContainerAfterFacadeInit()
        {
            // Accessing FacadeInstance triggers Awake -> InitializeIfNeeded ->
            // DrainAttributeGlobalRegistrations, which registers all [RegisterGlobal] types.
            _ = MvcFacade.FacadeInstance;

            var found = MvcFacade.Global.TryResolve<GlobalMockProxy>(out var proxy);

            Assert.IsTrue(found,
                "TryResolve<GlobalMockProxy> must return true after facade initializes the global container.");
            Assert.IsNotNull(proxy,
                "Resolved GlobalMockProxy instance must not be null.");
        }

        [Test]
        public void RegisterGlobal_PlainService_IsResolvableFromGlobalContainer()
        {
            _ = MvcFacade.FacadeInstance;

            var found = MvcFacade.Global.TryResolve<GlobalMockService>(out var service);

            Assert.IsTrue(found,
                "TryResolve<GlobalMockService> must return true after facade initializes the global container.");
            Assert.IsNotNull(service,
                "Resolved GlobalMockService instance must not be null.");
        }

        [Test]
        public void RegisterGlobal_ProxyWithInterface_IsResolvableByInterface()
        {
            _ = MvcFacade.FacadeInstance;

            var found = MvcFacade.Global.TryResolve<IGlobalMockProxyInterface>(out var proxy);

            Assert.IsTrue(found,
                "TryResolve<IGlobalMockProxyInterface> must return true; [RegisterGlobal(LogicInterface=...)] should register by interface.");
            Assert.IsNotNull(proxy,
                "Resolved IGlobalMockProxyInterface instance must not be null.");
        }

        [Test]
        public void RegisterGlobal_DrainRunsAfterFacadeReinit()
        {
            // Initialize and destroy the first facade instance.
            var firstFacade = MvcFacade.FacadeInstance;
            Object.DestroyImmediate(firstFacade.gameObject);

            // Reset scanner to simulate a fresh application lifecycle (e.g., domain reload disabled).
            AttributeScanner.Reset();

            // Create a second facade - DrainAttributeGlobalRegistrations must run again.
            _ = MvcFacade.FacadeInstance;

            var found = MvcFacade.Global.TryResolve<GlobalMockProxy>(out var proxy);

            Assert.IsTrue(found,
                "GlobalMockProxy must be resolvable after a fresh facade is created - drain must run on every new init.");
            Assert.IsNotNull(proxy,
                "Resolved GlobalMockProxy instance must not be null after re-initialization.");
        }

        // ── MonoBehaviour auto-creation (find-or-create) ─────────────────────

        // Creates the MvcFacade GameObject inactive so Awake() (and therefore InitializeIfNeeded /
        // DrainAttributeGlobalRegistrations) does not fire yet. This gives a window to pre-place
        // child GameObjects before triggering initialization via MvcFacade.FacadeInstance, whose
        // lookup explicitly searches inactive objects too (FindObjectsInactive.Include). The facade's
        // own GameObject is cleaned up by the existing TearDown (MvcFacade.InstanceOrNull check) -
        // no need to track it in _createdGameObjects, and any children parented under it are
        // destroyed along with it.
        private MvcFacade CreateInactiveFacade()
        {
            var go = new GameObject(nameof(MvcFacade));
            go.SetActive(false);
            return go.AddComponent<MvcFacade>();
        }

        [Test]
        public void RegisterGlobal_ProxyBehaviourWithNoSceneInstance_IsAutoCreatedUnderGlobalProxies()
        {
            _ = MvcFacade.FacadeInstance;

            var found = MvcFacade.Global.TryResolve<GlobalMockProxyBehaviour>(out var proxy);

            Assert.IsTrue(found, "Auto-created GlobalMockProxyBehaviour should resolve from the global container.");
            Assert.IsNotNull(proxy);
            Assert.IsTrue(proxy.Initialized, "Auto-created global ProxyBehaviour should receive OnInitialized.");
            Assert.AreEqual("Global Proxies", proxy.transform.parent.name,
                "Auto-created global ProxyBehaviour should live under the 'Global Proxies' container.");
        }

        [Test]
        public void RegisterGlobal_MonoBehaviourServiceWithNoSceneInstance_IsAutoCreatedUnderGlobalServices()
        {
            _ = MvcFacade.FacadeInstance;

            var found = MvcFacade.Global.TryResolve<GlobalMockMonoBehaviourService>(out var service);

            Assert.IsTrue(found, "Auto-created GlobalMockMonoBehaviourService should resolve from the global container.");
            Assert.IsNotNull(service);
            Assert.IsTrue(service.Initialized, "Auto-created global MonoBehaviour service should receive OnInitialized.");
            Assert.AreEqual("Global Services", service.transform.parent.name,
                "Auto-created global MonoBehaviour service should live under the 'Global Services' container.");
        }

        [Test]
        public void RegisterGlobal_ProxyBehaviourHandPlacedInHierarchy_IsFoundAndReused()
        {
            var facade = CreateInactiveFacade();

            var strayGo = new GameObject("StrayProxyBehaviour");
            strayGo.transform.SetParent(facade.transform, false);
            var strayProxy = strayGo.AddComponent<GlobalMockProxyBehaviour>();

            _ = MvcFacade.FacadeInstance;

            var found = MvcFacade.Global.TryResolve<GlobalMockProxyBehaviour>(out var resolved);

            Assert.IsTrue(found);
            Assert.AreSame(strayProxy, resolved,
                "A hand-placed instance anywhere in the facade hierarchy should be reused, not duplicated.");
        }

        [Test]
        public void RegisterGlobal_DuplicateProxyBehaviourInstances_AreNotRegistered()
        {
            var facade = CreateInactiveFacade();

            var go1 = new GameObject("Dup1");
            go1.transform.SetParent(facade.transform, false);
            go1.AddComponent<GlobalMockProxyBehaviour>();

            var go2 = new GameObject("Dup2");
            go2.transform.SetParent(facade.transform, false);
            go2.AddComponent<GlobalMockProxyBehaviour>();

            // Duplicate instances are expected to log an error; declare it so the test
            // framework does not treat the expected log as a failure.
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("has 2 instances in the MvcFacade hierarchy"));

            _ = MvcFacade.FacadeInstance;

            var found = MvcFacade.Global.TryResolve<GlobalMockProxyBehaviour>(out _);

            Assert.IsFalse(found,
                "Ambiguous (2+ instance) MonoBehaviour registrations must be skipped, not arbitrarily resolved.");
        }

        [Test]
        public void RegisterGlobal_DuplicateInstances_DoNotAffectOtherGlobalRegistrations()
        {
            var facade = CreateInactiveFacade();

            var go1 = new GameObject("Dup1");
            go1.transform.SetParent(facade.transform, false);
            go1.AddComponent<GlobalMockProxyBehaviour>();

            var go2 = new GameObject("Dup2");
            go2.transform.SetParent(facade.transform, false);
            go2.AddComponent<GlobalMockProxyBehaviour>();

            // Duplicate instances are expected to log an error; declare it so the test
            // framework does not treat the expected log as a failure.
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("has 2 instances in the MvcFacade hierarchy"));

            _ = MvcFacade.FacadeInstance;

            var found = MvcFacade.Global.TryResolve<GlobalMockProxy>(out _);

            Assert.IsTrue(found,
                "A duplicate/ambiguous MonoBehaviour registration must not block unrelated [RegisterGlobal] types.");
        }
    }
}

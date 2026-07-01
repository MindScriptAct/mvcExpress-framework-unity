using NUnit.Framework;
using mvcExpress;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Initialization;
using UnityEngine;
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
    }
}

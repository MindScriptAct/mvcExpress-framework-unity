using NUnit.Framework;
using mvcExpress;
using mvcExpress.Internal.Initialization;
using System.Collections.Generic;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests
{
    /// <summary>
    /// Behavior tests for the code-override global registration feature added to
    /// <see cref="MvcFacade"/>: <see cref="MvcFacade.RegisterGlobalServices"/>,
    /// <see cref="MvcFacade.RegisterGlobalProxies"/>, and <see cref="FacadeGlobalContainerApi"/>
    /// (exposed via the protected <c>GlobalDependencies</c> property).
    /// </summary>
    [TestFixture]
    public class MvcFacadeGlobalOverrideTests
    {
        // ── Test doubles ─────────────────────────────────────────────────────

        // Plain service registered via code override; tracks lifecycle callbacks.
        public class FacadeOverrideMockService : IMvcLifecycle
        {
            public bool OnInitializedCalled { get; private set; }
            public void OnInitialized() => OnInitializedCalled = true;
            public void OnCleanup() { }
        }

        // Proxy registered via code override; tracks whether the framework initialized it.
        public class FacadeOverrideMockProxy : Proxy
        {
            public bool Initialized { get; private set; }
            protected override void OnInitialized() => Initialized = true;
        }

        // Facade subclass that registers a plain service from RegisterGlobalServices.
        private class ServiceOverrideFacade : MvcFacade
        {
            public FacadeOverrideMockService RegisteredService { get; private set; }

            protected override void RegisterGlobalServices()
            {
                RegisteredService = new FacadeOverrideMockService();
                GlobalDependencies.Register(RegisteredService).ToLogic().AsPermanent();
            }
        }

        // Facade subclass that registers a proxy from RegisterGlobalProxies.
        private class ProxyOverrideFacade : MvcFacade
        {
            public FacadeOverrideMockProxy RegisteredProxy { get; private set; }

            protected override void RegisterGlobalProxies()
            {
                RegisteredProxy = new FacadeOverrideMockProxy();
                GlobalDependencies.Register(RegisteredProxy).ToLogic().AsPermanent();
            }
        }

        // Facade subclass that records the relative call order of both override hooks.
        private class OrderTrackingFacade : MvcFacade
        {
            public readonly List<string> CallOrder = new List<string>();

            protected override void RegisterGlobalServices() => CallOrder.Add("Services");
            protected override void RegisterGlobalProxies() => CallOrder.Add("Proxies");
        }

        // Service marked with [RegisterGlobal] so the attribute scanner picks it up automatically.
        [RegisterGlobal]
        public class AttributeDrivenMockService { }

        // Facade subclass that checks, from inside RegisterGlobalServices, whether an
        // attribute-driven global registration is already resolvable - proving the attribute
        // drain runs before the code-override hook.
        private class PrecedenceCheckFacade : MvcFacade
        {
            public bool AttributeServiceResolvableDuringOverride { get; private set; }

            protected override void RegisterGlobalServices()
            {
                AttributeServiceResolvableDuringOverride =
                    GlobalDependencies.TryResolve<AttributeDrivenMockService>(out _);
            }
        }

        // A minimal module used to prove global overrides are visible before any module
        // registers - i.e. there is no race condition between global setup and module startup.
        private class ConsumingModule : MvcModule
        {
            public FacadeOverrideMockService ResolvedDuringRegisterServices { get; private set; }

            protected override void RegisterServices()
            {
                Global.TryResolve<FacadeOverrideMockService>(out var resolved);
                ResolvedDuringRegisterServices = resolved;
            }
        }

        // ── Lifecycle ────────────────────────────────────────────────────────

        private readonly List<GameObject> _createdGameObjects = new List<GameObject>();

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

            AttributeScanner.Reset();
        }

        // Creates a GameObject with the given MvcFacade subclass attached, triggering Awake
        // (and therefore InitializeIfNeeded) synchronously.
        private T CreateFacade<T>() where T : MvcFacade
        {
            var go = new GameObject(typeof(T).Name);
            _createdGameObjects.Add(go);
            return go.AddComponent<T>();
        }

        // ── Tests ────────────────────────────────────────────────────────────

        [Test]
        public void RegisterGlobalServices_Override_ServiceIsResolvableFromGlobalContainer()
        {
            var facade = CreateFacade<ServiceOverrideFacade>();

            var found = MvcFacade.Global.TryResolve<FacadeOverrideMockService>(out var resolved);

            Assert.IsTrue(found,
                "A service registered from RegisterGlobalServices override must be resolvable from the global container.");
            Assert.AreSame(facade.RegisteredService, resolved,
                "Resolved instance must be the exact instance registered by the override.");
            Assert.IsTrue(resolved.OnInitializedCalled,
                "IMvcLifecycle.OnInitialized must be called for services registered via GlobalDependencies.Register.");
        }

        [Test]
        public void RegisterGlobalProxies_Override_ProxyIsInitializedAndResolvable()
        {
            var facade = CreateFacade<ProxyOverrideFacade>();

            var found = MvcFacade.Global.TryResolve<FacadeOverrideMockProxy>(out var resolved);

            Assert.IsTrue(found,
                "A proxy registered from RegisterGlobalProxies override must be resolvable from the global container.");
            Assert.AreSame(facade.RegisteredProxy, resolved,
                "Resolved instance must be the exact instance registered by the override.");
            Assert.IsTrue(resolved.Initialized,
                "Proxy.OnInitialized must be called for proxies registered via GlobalDependencies.Register.");
        }

        [Test]
        public void RegisterGlobalServices_Override_RunsAfterAttributeDrain()
        {
            var facade = CreateFacade<PrecedenceCheckFacade>();

            Assert.IsTrue(facade.AttributeServiceResolvableDuringOverride,
                "[RegisterGlobal] attribute registrations must already be resolvable inside " +
                "RegisterGlobalServices, confirming the Inspector -> Attribute -> Code precedence.");
        }

        [Test]
        public void RegisterGlobalServices_Override_RunsBeforeRegisterGlobalProxies()
        {
            var facade = CreateFacade<OrderTrackingFacade>();

            CollectionAssert.AreEqual(new[] { "Services", "Proxies" }, facade.CallOrder,
                "RegisterGlobalServices must run before RegisterGlobalProxies, matching MvcModule's phase order.");
        }

        [Test]
        public void GlobalOverride_Dependency_IsResolvableBeforeAnyModuleRegisters()
        {
            // Creating the subclassed facade first runs the override synchronously via Awake.
            CreateFacade<ServiceOverrideFacade>();

            // Adding the module triggers its own Awake -> RegisterModule -> RegisterServices,
            // which must find the facade already initialized with no race condition.
            var moduleGo = new GameObject(nameof(ConsumingModule));
            _createdGameObjects.Add(moduleGo);
            var module = moduleGo.AddComponent<ConsumingModule>();

            Assert.IsNotNull(module.ResolvedDuringRegisterServices,
                "A module must be able to resolve a facade-level global dependency during its own " +
                "RegisterServices phase, since global overrides run before any module registers.");
        }
    }
}

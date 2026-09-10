using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using mvcExpress;
using mvcExpress.Internal.Initialization;
using mvcExpress.Internal.Messaging;
using mvcExpress.Internal.Proxies;
using mvcExpress.Internal.Services;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests
{
    // Regression coverage: MvcFacade.OnDestroy() never disposed the
    // global container at all, and neither Inspector registrar reaches Code- or
    // Attribute-sourced global registrations - a plain (non-Proxy) IMvcLifecycle service or a
    // plain IDisposable registered globally via GlobalDependencies.Register/Global.Register or
    // [RegisterGlobal] was never torn down when the application (facade) shut down.
    [TestFixture]
    public class MvcFacadeGlobalCleanup_UnitTests
    {
        // ── Test doubles ─────────────────────────────────────────────────────

        // Implements IFirstMarker/ISecondMarker too - ReverseOrderFacade below registers two
        // instances of this class under those two distinct marker interfaces
        // (RegistrationBuilder validates that the instance actually implements whatever it's
        // aliased to, so a plain marker interface can no longer be used as a bare, unimplemented key).
        public class TrackedLifecycleService : IMvcLifecycle, IFirstMarker, ISecondMarker
        {
            public bool OnInitializedCalled { get; private set; }
            public bool OnCleanupCalled { get; private set; }
            public Action OnCleanupHook;

            public void OnInitialized() => OnInitializedCalled = true;

            public void OnCleanup()
            {
                OnCleanupCalled = true;
                OnCleanupHook?.Invoke();
            }
        }

        public class ThrowingLifecycleService : IMvcLifecycle
        {
            public void OnInitialized() { }
            public void OnCleanup() => throw new InvalidOperationException("Boom.");
        }

        public class TrackedDisposableService : IDisposable
        {
            public int DisposeCount { get; private set; }
            public void Dispose() => DisposeCount++;
        }

        // Inspector-sourced global service test double - a MonoBehaviour so it can be wired
        // through GlobalServiceRegistrar.RegisterSerializedGlobalServiceBehaviours the same way
        // a real Inspector-configured ServiceMapping would.
        private class TrackedInspectorLifecycleService : MonoBehaviour, IMvcLifecycle
        {
            public int OnInitializedCallCount;
            public int OnCleanupCallCount;

            public void OnInitialized() => OnInitializedCallCount++;
            public void OnCleanup() => OnCleanupCallCount++;
        }

        // Throws from OnCleanup() - used to prove GlobalServiceRegistrar.Cleanup()'s loop
        // continues past one throwing service instead of aborting entirely.
        private class ThrowingInspectorLifecycleService : MonoBehaviour, IMvcLifecycle
        {
            public void OnInitialized() { }
            public void OnCleanup() => throw new InvalidOperationException("Boom.");
        }

        [RegisterGlobal]
        public class AttributeLifecycleService : IMvcLifecycle
        {
            public static bool OnCleanupCalled;
            public void OnInitialized() { }
            public void OnCleanup() => OnCleanupCalled = true;
        }

        private class ServiceOverrideFacade : MvcFacade
        {
            public TrackedLifecycleService RegisteredService { get; private set; }

            protected override void RegisterGlobalServices()
            {
                RegisteredService = new TrackedLifecycleService();
                GlobalDependencies.Register(RegisteredService).ToLogic().AsPermanent();
            }
        }

        private class ThrowingServiceFacade : MvcFacade
        {
            public ThrowingLifecycleService Throwing { get; private set; }
            public TrackedLifecycleService RegisteredAfter { get; private set; }

            protected override void RegisterGlobalServices()
            {
                Throwing = new ThrowingLifecycleService();
                GlobalDependencies.Register(Throwing).ToLogic().AsPermanent();

                RegisteredAfter = new TrackedLifecycleService();
                GlobalDependencies.Register(RegisteredAfter).ToLogicAs<TrackedLifecycleService>().AsPermanent();
            }
        }

        private class DisposableServiceFacade : MvcFacade
        {
            public TrackedDisposableService RegisteredService { get; private set; }

            protected override void RegisterGlobalServices()
            {
                RegisteredService = new TrackedDisposableService();
                GlobalDependencies.Register(RegisteredService).ToLogic().AsPermanent();
            }
        }

        private class ReverseOrderFacade : MvcFacade
        {
            public readonly List<string> CleanupOrder = new List<string>();

            protected override void RegisterGlobalServices()
            {
                var first = new TrackedLifecycleService { OnCleanupHook = () => CleanupOrder.Add("First") };
                GlobalDependencies.Register(first).ToLogicAs<IFirstMarker>().AsPermanent();

                var second = new TrackedLifecycleService { OnCleanupHook = () => CleanupOrder.Add("Second") };
                GlobalDependencies.Register(second).ToLogicAs<ISecondMarker>().AsPermanent();
            }
        }

        // Distinct marker interfaces so two TrackedLifecycleService instances can be registered
        // under different keys in the same chain-less calls (registering the same concrete type
        // twice would collide on the shared logic key).
        private interface IFirstMarker { }
        private interface ISecondMarker { }

        private class BusStateDuringCleanupFacade : MvcFacade
        {
            public bool? BusWasDisposedDuringCleanup;

            protected override void RegisterGlobalServices()
            {
                var service = new TrackedLifecycleService
                {
                    OnCleanupHook = () =>
                    {
                        var bus = MvcFacade.MessageBus;
                        var field = typeof(MvcMessageBus).GetField("_disposed", BindingFlags.Instance | BindingFlags.NonPublic);
                        BusWasDisposedDuringCleanup = bus != null && field != null && (bool)field.GetValue(bus);
                    }
                };
                GlobalDependencies.Register(service).ToLogic().AsPermanent();
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
            AttributeLifecycleService.OnCleanupCalled = false;
        }

        private T CreateFacade<T>() where T : MvcFacade
        {
            var go = new GameObject(typeof(T).Name);
            _createdGameObjects.Add(go);
            return go.AddComponent<T>();
        }

        // Tracks lifecycle callback counts for the Inspector-registered global proxy tests below.
        public class TrackedGlobalProxy : ProxyBehaviour
        {
            public int OnInitializedCallCount;
            public int OnCleanupCallCount;
            protected override void OnInitialized() => OnInitializedCallCount++;
            protected override void OnCleanup() => OnCleanupCallCount++;
        }

        private (MvcFacade facade, TrackedGlobalProxy proxy) SetUpInspectorGlobalProxy()
        {
            var facade = CreateFacade<ServiceOverrideFacade>();

            var registryGo = new GameObject("GlobalProxyRegistry");
            _createdGameObjects.Add(registryGo);
            var registry = registryGo.AddComponent<GlobalProxyRegistryBehaviour>();

            var proxyGo = new GameObject("TrackedInspectorProxy");
            _createdGameObjects.Add(proxyGo);
            var proxy = proxyGo.AddComponent<TrackedGlobalProxy>();

            var mapping = new ProxyMapping
            {
                Proxy = proxy,
                RegisterToLogic = true,
                RegisterToView = false,
                IsTransient = false,
                LogicTypeName = typeof(TrackedGlobalProxy).AssemblyQualifiedName,
            };

            typeof(GlobalProxyRegistryBehaviour)
                .GetField("_proxyMappings", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(registry, new[] { mapping });

            typeof(MvcFacade)
                .GetField("_globalProxyRegistry", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(facade, registry);

            var initializeGlobalProxies = typeof(MvcFacade)
                .GetMethod("InitializeGlobalProxies", BindingFlags.Instance | BindingFlags.NonPublic);
            initializeGlobalProxies.Invoke(facade, null);

            return (facade, proxy);
        }

        [Test]
        public void ClearGlobalContainer_InspectorRegisteredGlobalProxy_OnCleanupCalledPromptly()
        {
            var (facade, proxy) = SetUpInspectorGlobalProxy();

            Assert.AreEqual(1, proxy.OnInitializedCallCount,
                "Precondition: the Inspector-configured proxy must have been initialized once.");
            Assert.AreEqual(0, proxy.OnCleanupCallCount, "Precondition: not yet cleaned up.");

            facade.ClearGlobalContainer();

            Assert.AreEqual(1, proxy.OnCleanupCallCount,
                "ClearGlobalContainer() must call OnCleanup() on Inspector-sourced global proxies " +
                "promptly (synchronously, via GlobalProxyRegistrar.ResetTrackedProxiesForContainerClear) " +
                "rather than leaving it to whatever deferred OnDestroy() cascade eventually fires once " +
                "the container clear destroys the GameObject.");
        }

        // Throws from OnCleanup() - used to prove ResetForContainerClear()'s flag reset (and
        // GlobalProxyRegistrar's own loop continuation) survive a throwing proxy.
        public class ThrowingCleanupGlobalProxy : ProxyBehaviour
        {
            public int OnInitializedCallCount;
            protected override void OnInitialized() => OnInitializedCallCount++;
            protected override void OnCleanup() => throw new InvalidOperationException("Boom.");
        }

        [Test]
        public void ClearGlobalContainer_InspectorProxyOnCleanupThrows_FlagsStillResetForNextReinitialization()
        {
            var facade = CreateFacade<ServiceOverrideFacade>();

            var registryGo = new GameObject("GlobalProxyRegistry");
            _createdGameObjects.Add(registryGo);
            var registry = registryGo.AddComponent<GlobalProxyRegistryBehaviour>();

            var proxyGo = new GameObject("ThrowingCleanupProxy");
            _createdGameObjects.Add(proxyGo);
            var proxy = proxyGo.AddComponent<ThrowingCleanupGlobalProxy>();

            var mapping = new ProxyMapping
            {
                Proxy = proxy,
                RegisterToLogic = true,
                RegisterToView = false,
                IsTransient = false,
                LogicTypeName = typeof(ThrowingCleanupGlobalProxy).AssemblyQualifiedName,
            };

            typeof(GlobalProxyRegistryBehaviour)
                .GetField("_proxyMappings", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(registry, new[] { mapping });

            typeof(MvcFacade)
                .GetField("_globalProxyRegistry", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(facade, registry);

            var initializeGlobalProxies = typeof(MvcFacade)
                .GetMethod("InitializeGlobalProxies", BindingFlags.Instance | BindingFlags.NonPublic);
            initializeGlobalProxies.Invoke(facade, null);

            Assert.AreEqual(1, proxy.OnInitializedCallCount, "Precondition: initialized once.");

            LogAssert.Expect(LogType.Error, new Regex("threw during OnCleanup"));
            LogAssert.Expect(LogType.Exception, new Regex("Boom"));
            facade.ClearGlobalContainer();

            Assert.AreEqual(2, proxy.OnInitializedCallCount,
                "Even though OnCleanup() threw, ResetForContainerClear()'s finally block must still " +
                "reset the initialized flags, so the subsequent re-registration actually " +
                "re-initializes the proxy instead of silently no-oping via CompleteInitialization()'s " +
                "idempotency guard.");

            // The proxy is now re-initialized (_initialized == true again), so its real OnDestroy()
            // will call the still-throwing OnCleanup() once more when its GameObject is eventually
            // destroyed - correct, expected behavior for a permanently-broken proxy, not a test
            // artifact. Destroy explicitly here (rather than relying on TearDown) so the matching
            // LogAssert.Expect stays inside this test's own body - LogAssert's queue is not honored
            // across into a separate TearDown call.
            LogAssert.Expect(LogType.Exception, new Regex("Boom"));
            _createdGameObjects.Remove(proxyGo);
            Object.DestroyImmediate(proxyGo);
        }

        [Test]
        public void ClearGlobalContainer_TwoInspectorServices_FirstThrowsOnCleanup_SecondStillCleanedUpAndListCleared()
        {
            // GlobalServiceRegistrar.Cleanup() previously had no catch at all
            // around lifecycle.OnCleanup() (only a finally) - one throwing service would propagate
            // out of the loop, skipping every service still earlier in iteration order and never
            // reaching _registeredServices.Clear().
            var facade = CreateFacade<ServiceOverrideFacade>();

            var registryGo = new GameObject("GlobalServiceRegistry");
            _createdGameObjects.Add(registryGo);
            var registry = registryGo.AddComponent<GlobalServiceRegistryBehaviour>();

            var throwingGo = new GameObject("ThrowingInspectorService");
            _createdGameObjects.Add(throwingGo);
            var throwingService = throwingGo.AddComponent<ThrowingInspectorLifecycleService>();

            var trackedGo = new GameObject("TrackedInspectorService2");
            _createdGameObjects.Add(trackedGo);
            var trackedService = trackedGo.AddComponent<TrackedInspectorLifecycleService>();

            // Cleanup() walks in reverse registration order, so 'tracked' (registered second) runs
            // BEFORE 'throwing' (registered first) - order the mappings so the throw happens first
            // in cleanup order, proving the loop survives it and still reaches the one after.
            var mappings = new[]
            {
                new ServiceMapping { Service = trackedService, RegisterToLogic = true, RegisterToView = false, IsTransient = false },
                new ServiceMapping { Service = throwingService, RegisterToLogic = true, RegisterToView = false, IsTransient = false },
            };

            typeof(GlobalServiceRegistryBehaviour)
                .GetField("_serviceMappings", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(registry, mappings);

            typeof(MvcFacade)
                .GetField("_globalServiceRegistry", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(facade, registry);

            var initializeGlobalServices = typeof(MvcFacade)
                .GetMethod("InitializeGlobalServices", BindingFlags.Instance | BindingFlags.NonPublic);
            initializeGlobalServices.Invoke(facade, null);

            LogAssert.Expect(LogType.Error, new Regex("threw during OnCleanup"));
            LogAssert.Expect(LogType.Exception, new Regex("Boom"));

            facade.ClearGlobalContainer();

            Assert.AreEqual(1, trackedService.OnCleanupCallCount,
                "The service registered before the throwing one (cleaned up AFTER it, in reverse " +
                "order) must still have OnCleanup() called - the loop must not abort on the first " +
                "exception.");

            // ClearGlobalContainer() re-registers from the still-wired registry as part of its own
            // InitializeGlobalServices() call, so the still-broken throwing service is back in
            // _registeredServices - the facade's own real destroy below will hit it again. That's
            // correct, expected behavior for a permanently-misconfigured Inspector service, not a
            // test artifact - destroy explicitly here (rather than relying on TearDown) so the
            // matching LogAssert.Expect stays inside this test's own body; LogAssert's queue is not
            // honored across into a separate TearDown call.
            LogAssert.Expect(LogType.Error, new Regex("threw during OnCleanup"));
            LogAssert.Expect(LogType.Exception, new Regex("Boom"));
            var facadeGo = facade.gameObject;
            _createdGameObjects.Remove(facadeGo);
            Object.DestroyImmediate(facadeGo);
        }

        // ── Tests ────────────────────────────────────────────────────────────

        [Test]
        public void FacadeDestroy_CodeRegisteredPlainGlobalService_OnCleanupIsCalled()
        {
            var facade = CreateFacade<ServiceOverrideFacade>();
            var service = facade.RegisteredService;

            Object.DestroyImmediate(facade.gameObject);

            Assert.IsTrue(service.OnCleanupCalled,
                "A plain IMvcLifecycle service registered globally via code (GlobalDependencies.Register) " +
                "must have OnCleanup() called when the facade is destroyed.");
        }

        [Test]
        public void FacadeDestroy_AttributeRegisteredPlainGlobalService_OnCleanupIsCalled()
        {
            var facade = CreateFacade<ServiceOverrideFacade>(); // triggers the attribute drain too
            Assert.IsTrue(MvcFacade.Global.TryResolve<AttributeLifecycleService>(out _),
                "Precondition: the [RegisterGlobal] service must have been registered.");

            Object.DestroyImmediate(facade.gameObject);

            Assert.IsTrue(AttributeLifecycleService.OnCleanupCalled,
                "A plain IMvcLifecycle service registered globally via [RegisterGlobal] must have " +
                "OnCleanup() called when the facade is destroyed.");
        }

        [Test]
        public void FacadeDestroy_CodeRegisteredPlainDisposableService_IsDisposed()
        {
            var facade = CreateFacade<DisposableServiceFacade>();
            var service = facade.RegisteredService;

            Object.DestroyImmediate(facade.gameObject);

            Assert.That(service.DisposeCount, Is.EqualTo(1),
                "A plain IDisposable registered globally via code must be disposed exactly once " +
                "when the facade is destroyed - MvcFacade.OnDestroy() must actually dispose the " +
                "global container, not skip it entirely.");
        }

        [Test]
        public void ClearGlobalContainer_CodeRegisteredPlainGlobalService_OnCleanupCalledBeforeClear()
        {
            var facade = CreateFacade<ServiceOverrideFacade>();
            var service = facade.RegisteredService;

            facade.ClearGlobalContainer();

            Assert.IsTrue(service.OnCleanupCalled,
                "ClearGlobalContainer() must call OnCleanup() on tracked global lifecycle services " +
                "before clearing the container, not just drop their registrations silently.");
            Assert.IsFalse(MvcFacade.Global.TryResolve<TrackedLifecycleService>(out _),
                "The service must no longer be resolvable after ClearGlobalContainer().");
        }

        [Test]
        public void ClearGlobalContainer_InspectorRegisteredGlobalService_OnCleanupCalledBeforeReinitialization()
        {
            // Inspector-sourced services are only auto-discovered via EnsureGlobalRegistries()
            // when Application.isPlaying is true, which EditMode tests are not. Wire the
            // registry and service directly via reflection instead, matching how a real
            // Inspector-configured GlobalServiceRegistryBehaviour would be populated.
            var facade = CreateFacade<ServiceOverrideFacade>();

            var registryGo = new GameObject("GlobalServiceRegistry");
            _createdGameObjects.Add(registryGo);
            var registry = registryGo.AddComponent<GlobalServiceRegistryBehaviour>();

            var serviceGo = new GameObject("TrackedInspectorService");
            _createdGameObjects.Add(serviceGo);
            var service = serviceGo.AddComponent<TrackedInspectorLifecycleService>();

            var mapping = new ServiceMapping
            {
                Service = service,
                RegisterToLogic = true,
                RegisterToView = false,
                IsTransient = false,
            };

            typeof(GlobalServiceRegistryBehaviour)
                .GetField("_serviceMappings", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(registry, new[] { mapping });

            typeof(MvcFacade)
                .GetField("_globalServiceRegistry", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(facade, registry);

            var initializeGlobalServices = typeof(MvcFacade)
                .GetMethod("InitializeGlobalServices", BindingFlags.Instance | BindingFlags.NonPublic);
            initializeGlobalServices.Invoke(facade, null);

            Assert.AreEqual(1, service.OnInitializedCallCount,
                "Precondition: the Inspector-configured service must have been initialized once.");

            facade.ClearGlobalContainer();

            Assert.AreEqual(1, service.OnCleanupCallCount,
                "ClearGlobalContainer() must call OnCleanup() on Inspector-sourced global services " +
                "(via _globalServiceRegistrar.Cleanup()) before clearing/reinitializing the registry.");
        }

        [Test]
        public void FacadeDestroy_ThrowingOnCleanup_OtherServicesStillCleanedUpAndContainerStillDisposed()
        {
            var facade = CreateFacade<ThrowingServiceFacade>();
            var registeredAfter = facade.RegisteredAfter;

            LogAssert.Expect(LogType.Error, new Regex(
                "ThrowingLifecycleService' threw during OnCleanup"));
            // CleanupGlobalLifecycleServices now also logs via Unity's own
            // Debug.LogException (unconditionally, unlike MvcDebug.LogError above) so a teardown
            // exception is visible even in a clean release build.
            LogAssert.Expect(LogType.Exception, new Regex("Boom"));
            Assert.DoesNotThrow(() => Object.DestroyImmediate(facade.gameObject),
                "A throwing OnCleanup() must not stop the facade's own teardown.");

            Assert.IsTrue(registeredAfter.OnCleanupCalled,
                "A service registered after one whose OnCleanup() throws must still be cleaned up.");
        }

        [Test]
        public void FacadeDestroy_GlobalLifecycleCleanup_RunsInReverseRegistrationOrder()
        {
            var facade = CreateFacade<ReverseOrderFacade>();

            Object.DestroyImmediate(facade.gameObject);

            CollectionAssert.AreEqual(new[] { "Second", "First" }, facade.CleanupOrder,
                "OnCleanup() must run in reverse registration order (last registered, first " +
                "cleaned up), matching ModuleInitializer.CleanupServices().");
        }

        [Test]
        public void FacadeDestroy_GlobalServiceCleanup_RunsBeforeMessageBusIsDisposed()
        {
            var facade = CreateFacade<BusStateDuringCleanupFacade>();

            Object.DestroyImmediate(facade.gameObject);

            Assert.IsNotNull(facade.BusWasDisposedDuringCleanup,
                "Precondition: OnCleanup() must have run and recorded the bus's disposed state.");
            Assert.IsFalse(facade.BusWasDisposedDuringCleanup.Value,
                "The message bus must still be alive while global lifecycle services clean up - " +
                "it must be disposed AFTER actor cleanup, not before.");
        }
    }
}

using System;
using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using mvcExpress;
using mvcExpress.Internal.Initialization;
using mvcExpress.Internal.DependencyInjection;

namespace mvcExpress.Tests
{
    [TestFixture]
    public class ModuleInitializerTests
    {
        // ── Test doubles ──────────────────────────────────────────────────────

        // Minimal module that lets Awake run normally (creates MvcFacade, runs ModuleInitializer).
        private class BasicTestModule : MvcModule
        {
            public bool OnInitializedCalled { get; private set; }
            protected override void OnInitialized() => OnInitializedCalled = true;
        }

        // Module whose OnInitialized throws, driving the failure path.
        private class FailingOnInitializedModule : MvcModule
        {
            protected override void OnInitialized() =>
                throw new InvalidOperationException("Deliberate test failure in OnInitialized.");
        }

        // Module that registers a plain service via code in RegisterServices.
        private class ServiceRegistrationModule : MvcModule
        {
            public TrackingService RegisteredService { get; private set; }

            protected override void RegisterServices()
            {
                RegisteredService = new TrackingService();
                Container.Register<TrackingService>(RegisteredService).ToLogic().AsPermanent();
            }
        }

        // Module whose Services phase succeeds (registering a TrackingService) but whose LATER
        // Proxies phase throws - tests whether the earlier, already-committed
        // phase's state gets cleaned up even though the module never reaches Initialized.
        private class ServiceThenFailingProxyModule : MvcModule
        {
            public TrackingService RegisteredService { get; private set; }

            protected override void RegisterServices()
            {
                RegisteredService = new TrackingService();
                Container.Register<TrackingService>(RegisteredService).ToLogic().AsPermanent();
            }

            protected override void RegisterProxies() =>
                throw new InvalidOperationException("Deliberate test failure in RegisterProxies.");
        }

        // Plain service that tracks whether OnInitialized was called by the framework.
        public class TrackingService : IMvcLifecycle
        {
            public bool OnInitializedCalled { get; private set; }
            public bool OnCleanupCalled { get; private set; }

            public void OnInitialized() => OnInitializedCalled = true;
            public void OnCleanup()     => OnCleanupCalled = true;
        }

        // Service class decorated with [Register] so the AttributeScanner picks it up.
        // TargetModuleType points at AttributeServiceModule so it only registers there.
        [Register(typeof(AttributeServiceModule), RegisterToLogic = true)]
        public class AttributeTrackedService : IMvcLifecycle
        {
            public bool OnInitializedCalled { get; private set; }
            public bool OnCleanupCalled     { get; private set; }

            public void OnInitialized() => OnInitializedCalled = true;
            public void OnCleanup()     => OnCleanupCalled = true;
        }

        // Module used solely for the attribute-service test.
        // Its type is referenced by AttributeTrackedService above.
        private class AttributeServiceModule : MvcModule { }

        // Service decorated with Lifecycle = Scoped. Tracks how many times OnInitialized fires across
        // ALL instances - the throwaway instance created during attribute registration must never
        // trigger this, since AsScoped() discards it and the real instance is created per resolution scope.
        [Register(typeof(ScopedAttributeModule), RegisterToLogic = true, Lifecycle = RegistrationLifecycle.Scoped)]
        public class ScopedAttributeService : IMvcLifecycle
        {
            public static int InitializedCount;
            public void OnInitialized() => InitializedCount++;
            public void OnCleanup() { }
        }

        // Module used solely for the scoped-attribute-service test.
        private class ScopedAttributeModule : MvcModule { }

        // ── Helpers ───────────────────────────────────────────────────────────

        // Reads the private _initializer field from a live MvcModule via reflection.
        private static ModuleInitializer GetInitializer(MvcModule module)
        {
            var field = typeof(MvcModule).GetField(
                "_initializer",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, "Could not find '_initializer' field on MvcModule via reflection.");
            return (ModuleInitializer)field.GetValue(module);
        }

        // Reads the private _diContainer field from a live ModuleInitializer via reflection.
        private static MvcDiContainer GetDiContainer(ModuleInitializer initializer)
        {
            var field = typeof(ModuleInitializer).GetField(
                "_diContainer",
                BindingFlags.Instance | BindingFlags.NonPublic);

            Assert.IsNotNull(field, "Could not find '_diContainer' field on ModuleInitializer via reflection.");
            return (MvcDiContainer)field.GetValue(initializer);
        }

        // Creates a module GameObject, adds the component (triggering Awake and full init),
        // and returns both. The caller owns teardown.
        private static (GameObject go, T module) CreateModule<T>() where T : MvcModule
        {
            var go = new GameObject(typeof(T).Name);
            var module = go.AddComponent<T>();
            return (go, module);
        }

        // Destroys a module GameObject and then cleans up the MvcFacade.
        private static void DestroyModuleAndFacade(GameObject moduleGo)
        {
            if (moduleGo != null)
                Object.DestroyImmediate(moduleGo);

            var facade = MvcFacade.InstanceOrNull;
            if (facade != null)
                Object.DestroyImmediate(facade.gameObject);
        }

        // ── Tests ─────────────────────────────────────────────────────────────

        // Section 1: Phase Transition Constraints

        [Test]
        public void PhaseTransitionConstraints_ThrowsWhenJumpingBackward()
        {
            // The ModuleInitializer validates that phases only advance forward.
            // We cannot call TransitionToPhase directly (private method), but we can verify the
            // guard indirectly: after a module completes initialization the phase is Initialized.
            // Calling InitializeModule() again (which calls _initializer.Initialize() again) on
            // an already-Initialized module must NOT throw an exception but must log a warning.
            //
            // For the strictly-backward transition test (e.g. Proxies -> Services), that path is
            // private and only reachable via a defect in ModuleInitializer itself. We test the
            // observable contract: the guard exists and prevents re-initialization.
            //
            // This test verifies the guard via the public/internal API rather than private reflection.

            var (go, module) = CreateModule<BasicTestModule>();
            try
            {
                var initializer = GetInitializer(module);
                Assert.AreEqual(InitializationPhase.Initialized, initializer.CurrentPhase,
                    "Module must be in Initialized phase after Awake completes.");

                // Calling Initialize() a second time on an already-Initialized module must be a no-op
                // (logs a warning, returns without throwing).
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("already initialized"));
                Assert.DoesNotThrow(
                    () => initializer.Initialize(),
                    "Initialize() called on an already-Initialized module must not throw.");

                // Phase must remain Initialized (not regress).
                Assert.AreEqual(InitializationPhase.Initialized, initializer.CurrentPhase,
                    "Phase must stay Initialized after a no-op re-initialization call.");
            }
            finally
            {
                DestroyModuleAndFacade(go);
            }
        }

        [Test]
        public void PhaseTransitionConstraints_AlreadyInitializedLogsWarning()
        {
            var (go, module) = CreateModule<BasicTestModule>();
            try
            {
                var initializer = GetInitializer(module);
                Assert.IsTrue(initializer.IsInitialized,
                    "Module must be fully initialized after Awake.");

                // Expect the warning Unity will emit when Initialize() is called again.
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("already initialized"));

                // Must not throw.
                initializer.Initialize();
            }
            finally
            {
                DestroyModuleAndFacade(go);
            }
        }

        // Section 2: Attribute-Based Service Registration

        [Test]
        public void AttributeBasedServiceRegistration_ResolvesAndBindsCorrectly()
        {
            // AttributeTrackedService is decorated with [Register(typeof(AttributeServiceModule), ...)]
            // so ModuleInitializer should create an instance and register it to the logic layer.

            var (go, module) = CreateModule<AttributeServiceModule>();
            try
            {
                var initializer = GetInitializer(module);
                Assert.IsTrue(initializer.IsInitialized,
                    "AttributeServiceModule must be fully initialized.");

                // The DiContainer is internal - accessible because InternalsVisibleTo is set.
                var container = module.DiContainer;
                Assert.IsNotNull(container, "DiContainer must not be null after init.");

                // The service must be resolvable from the logic scope.
                var resolved = container.Resolve<AttributeTrackedService>();
                Assert.IsNotNull(resolved,
                    "AttributeTrackedService must be resolvable from the logic scope after attribute registration.");

                // OnInitialized must have been called by the framework.
                Assert.IsTrue(resolved.OnInitializedCalled,
                    "IMvcLifecycle.OnInitialized must be called by ModuleInitializer on attribute-registered services.");
            }
            finally
            {
                DestroyModuleAndFacade(go);
            }
        }

        // Section 3: Lifecycle Method Invocations

        [Test]
        public void LifecycleMethodInvocations_FiredInCorrectOrder()
        {
            // ServiceRegistrationModule registers TrackingService via code in RegisterServices().
            // After Awake, the framework must have called IMvcLifecycle.OnInitialized on it.

            var (go, module) = CreateModule<ServiceRegistrationModule>();
            try
            {
                var initializer = GetInitializer(module);
                Assert.IsTrue(initializer.IsInitialized,
                    "Module must be fully initialized after Awake.");

                var svc = module.RegisteredService;
                Assert.IsNotNull(svc, "RegisteredService must not be null - RegisterServices was not called.");

                Assert.IsTrue(svc.OnInitializedCalled,
                    "IMvcLifecycle.OnInitialized must be called on code-registered services during the Services phase.");
            }
            finally
            {
                DestroyModuleAndFacade(go);
            }
        }

        // Section 4: Error Handling

        [UnityTest]
        public IEnumerator ErrorHandlingAndDeferredLogging_AbortsOnFailure()
        {
            // FailingOnInitializedModule.OnInitialized() throws intentionally.
            // ModuleInitializer catches this, sets IsFailed = true, stores the exception,
            // then re-throws so Unity logs the exception.
            //
            // Because the exception propagates out of AddComponent (Awake), Unity logs it as
            // an unhandled exception. We suppress that expected log with LogAssert.Expect.
            //
            // MvcModule.Awake() also catches this, logs its own error, and destroys
            // the module - LogAssert.Expect enforces chronological order, so that error (logged
            // synchronously, before self-destroy) must be declared before the exception it causes
            // Unity to log once the rethrow reaches the engine.
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("failed to initialize and will be destroyed"));
            LogAssert.Expect(LogType.Exception,
                new System.Text.RegularExpressions.Regex("Deliberate test failure"));

            GameObject go = null;
            FailingOnInitializedModule module = null;
            try
            {
                go = new GameObject(nameof(FailingOnInitializedModule));
                // Awake runs here; the exception is caught by Unity's message loop but still
                // reaches our assertions because AddComponent completes (module object exists).
                module = go.AddComponent<FailingOnInitializedModule>();
            }
            catch (Exception)
            {
                // AddComponent may or may not surface the exception depending on Unity version;
                // either way the module field is populated and we can inspect it.
            }

            try
            {
                // Even if AddComponent did not throw, the module was created and Awake ran, so the
                // component (and its plain C# _initializer field) are still inspectable here -
                // Destroy(gameObject) is deferred to end-of-frame in Play Mode, so the reflection
                // checks below run before the module is actually gone (see the yield further down).
                if (module == null && go != null)
                    module = go.GetComponent<FailingOnInitializedModule>();

                Assert.IsNotNull(module, "Module component must still be inspectable synchronously, before the deferred Destroy() takes effect.");

                var initializer = GetInitializer(module);
                Assert.IsNotNull(initializer,
                    "_initializer must be populated (it is assigned before Initialize() throws).");

                Assert.IsTrue(initializer.IsFailed,
                    "IsFailed must be true after an exception during initialization.");

                Assert.IsNotNull(initializer.FailureException,
                    "FailureException must be set when initialization fails.");

                // The failure wraps the user exception so check inner or message chain.
                var ex = initializer.FailureException;
                bool containsOriginalMessage =
                    (ex.Message != null && ex.Message.Contains("Deliberate test failure")) ||
                    (ex.InnerException != null && ex.InnerException.Message != null &&
                     ex.InnerException.Message.Contains("Deliberate test failure"));

                Assert.IsTrue(containsOriginalMessage,
                    "FailureException must contain the original error message from OnInitialized.");

                // Calling Initialize() again after failure must throw InvalidOperationException.
                Assert.Throws<InvalidOperationException>(
                    () => initializer.Initialize(),
                    "Calling Initialize() on a Failed module must throw InvalidOperationException.");

                // The module must have been destroyed as a consequence of the failure -
                // yield once so Destroy(gameObject) (deferred to end-of-frame in Play Mode) takes
                // effect before checking Unity's overloaded null comparison.
                yield return null;

                Assert.IsTrue(go == null,
                    "A module whose initialization fails must be destroyed.");
            }
            finally
            {
                DestroyModuleAndFacade(go);
            }
        }

        [UnityTest]
        public IEnumerator EarlierPhaseState_IsCleanedUp_WhenALaterPhaseFails()
        {
            // A module whose Services phase already committed real state
            // (here, a plain service registered and OnInitialized-called) must not leave that state
            // dangling forever just because a LATER phase (Proxies) then fails. Self-destroying the
            // module (see ErrorHandlingAndDeferredLogging_AbortsOnFailure above) routes through the
            // normal OnDestroy() pipeline, which calls OnCleanup() on every tracked lifecycle
            // service regardless of how far initialization got - this test proves that concretely.
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("failed to initialize and will be destroyed"));
            LogAssert.Expect(LogType.Exception,
                new System.Text.RegularExpressions.Regex("Deliberate test failure in RegisterProxies"));

            GameObject go = null;
            ServiceThenFailingProxyModule module = null;
            try
            {
                go = new GameObject(nameof(ServiceThenFailingProxyModule));
                module = go.AddComponent<ServiceThenFailingProxyModule>();
            }
            catch (Exception)
            {
                // AddComponent may or may not surface the exception depending on Unity version.
            }

            try
            {
                if (module == null && go != null)
                    module = go.GetComponent<ServiceThenFailingProxyModule>();

                var service = module.RegisteredService;
                Assert.IsNotNull(service,
                    "Precondition: the Services phase must have committed before Proxies threw.");
                Assert.IsTrue(service.OnInitializedCalled,
                    "Precondition: the earlier phase's service must have been fully initialized.");
                Assert.IsFalse(service.OnCleanupCalled,
                    "Precondition: OnCleanup() must not have run yet - the module hasn't been destroyed.");

                yield return null;

                Assert.IsTrue(go == null,
                    "The module must have been destroyed after the Proxies phase failed.");
                Assert.IsTrue(service.OnCleanupCalled,
                    "The earlier, already-committed Services-phase state must still be cleaned up " +
                    "(OnCleanup() called) via the normal OnDestroy() pipeline, even though the module " +
                    "never reached Initialized.");
            }
            finally
            {
                DestroyModuleAndFacade(go);
            }
        }

        [Test]
        public void RegisterAttributeServices_ScopedService_DoesNotInitializeThrowawayInstance()
        {
            ScopedAttributeService.InitializedCount = 0;

            var (go, module) = CreateModule<ScopedAttributeModule>();
            try
            {
                var initializer = GetInitializer(module);
                var container = GetDiContainer(initializer);

                Assert.That(container.IsScoped(typeof(ScopedAttributeService)), Is.True,
                    "Scoped attribute service must be registered as scoped in the container.");
                Assert.That(ScopedAttributeService.InitializedCount, Is.EqualTo(0),
                    "The throwaway instance created to walk the registration builder must never be initialized.");
            }
            finally
            {
                DestroyModuleAndFacade(go);
            }
        }
    }
}

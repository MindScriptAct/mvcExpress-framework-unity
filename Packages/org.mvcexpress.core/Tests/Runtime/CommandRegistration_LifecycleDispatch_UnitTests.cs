using NUnit.Framework;
using mvcExpress;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests
{
    // Regression coverage: CommandContainerApi.Register (module-scoped) and
    // CommandGlobalContainerApi.Register (global-scoped) are the two entry points a command uses
    // to register a dependency dynamically at runtime, after the module's/facade's normal
    // initialization phases have already passed. Both wired only a Proxy/ProxyBehaviour branch
    // (CommandGlobalContainerApi wired NOTHING at all) - a plain (non-Proxy) service implementing
    // only IMvcLifecycle registered through either API was silently never injected or initialized,
    // unlike the equivalent Facade/Module-level global APIs (FacadeGlobalContainerApi,
    // ModuleGlobalContainerApi), which already had the full three-way dispatch.
    //
    // Also covers CommandContainerApi.RegisterBehaviour<T>() (release audit CMP-01): this method
    // used to delegate to ModuleRegistrationContainerApi.RegisterBehaviour<T>(), which is compiled
    // out under MVC_EXPRESS_NO_CODE - breaking command-time MonoBehaviour registration under that
    // define even though it is a runtime feature, not Code-style setup. Fixed by inlining the same
    // logic directly. This test cannot reproduce the compile-time half of that regression (a normal
    // test run doesn't set MVC_EXPRESS_NO_CODE), but it does guard the runtime behavior: that the
    // inlined implementation still creates, parents, and registers the MonoBehaviour correctly.
    [TestFixture]
    public class CommandRegistration_LifecycleDispatch_UnitTests
    {
        public class TrackedLifecycleService : IMvcLifecycle
        {
            public bool OnInitializedCalled { get; private set; }
            public bool OnCleanupCalled { get; private set; }
            public void OnInitialized() => OnInitializedCalled = true;
            public void OnCleanup() => OnCleanupCalled = true;
        }

        // Holds the instance a Command should register - Commands are constructed internally by
        // the framework, so the test can't pass the instance to them directly.
        private static class PendingRegistration
        {
            public static TrackedLifecycleService Service;
            public static Proxy Proxy;
        }

        private sealed class TrackedProxy : Proxy
        {
            public bool Initialized { get; private set; }
            protected override void OnInitialized() => Initialized = true;
        }

        // ── CommandContainerApi (module-scoped) ─────────────────────────────

        private sealed class RegisterModuleLifecycleServiceCommand : Command
        {
            public override void Execute()
            {
                Container.Register(PendingRegistration.Service).ToLogic().AsPermanent();
            }
        }

        private sealed class ModuleCommandRegistrationModule : MvcModule
        {
            public void RunRegisterLifecycleServiceCommand() => Commander.Run<RegisterModuleLifecycleServiceCommand>();
        }

        [Test]
        public void CommandContainerApi_Register_PlainLifecycleService_OnInitializedIsCalled()
        {
            var service = new TrackedLifecycleService();
            PendingRegistration.Service = service;

            _moduleGo = new GameObject(nameof(ModuleCommandRegistrationModule));
            var module = _moduleGo.AddComponent<ModuleCommandRegistrationModule>();
            module.RunRegisterLifecycleServiceCommand();

            Assert.IsTrue(service.OnInitializedCalled,
                "A plain IMvcLifecycle service registered dynamically via CommandContainerApi.Register " +
                "must be injected and have OnInitialized() called, matching Proxy/ProxyBehaviour handling.");
        }

        [Test]
        public void CommandContainerApi_Register_PlainLifecycleService_OnCleanupCalledOnModuleDestroy()
        {
            var service = new TrackedLifecycleService();
            PendingRegistration.Service = service;

            _moduleGo = new GameObject(nameof(ModuleCommandRegistrationModule));
            var module = _moduleGo.AddComponent<ModuleCommandRegistrationModule>();
            module.RunRegisterLifecycleServiceCommand();

            Object.DestroyImmediate(_moduleGo);
            _moduleGo = null;

            Assert.IsTrue(service.OnCleanupCalled,
                "A dynamically command-registered plain service must be tracked the same way as a " +
                "setup-time one, so OnCleanup() still runs at module teardown.");
        }

        // ── CommandGlobalContainerApi (global-scoped) ───────────────────────

        private sealed class RegisterGlobalLifecycleServiceCommand : Command
        {
            public override void Execute()
            {
                Global.Register(PendingRegistration.Service).ToLogic().AsPermanent();
            }
        }

        private sealed class RegisterGlobalProxyCommand : Command
        {
            public override void Execute()
            {
                Global.Register(PendingRegistration.Proxy).ToLogic().AsPermanent();
            }
        }

        private sealed class GlobalCommandRegistrationModule : MvcModule
        {
            public void RunRegisterGlobalLifecycleServiceCommand() => Commander.Run<RegisterGlobalLifecycleServiceCommand>();
            public void RunRegisterGlobalProxyCommand() => Commander.Run<RegisterGlobalProxyCommand>();
        }

        [Test]
        public void CommandGlobalContainerApi_Register_PlainLifecycleService_OnInitializedIsCalled()
        {
            var service = new TrackedLifecycleService();
            PendingRegistration.Service = service;

            _moduleGo = new GameObject(nameof(GlobalCommandRegistrationModule));
            var module = _moduleGo.AddComponent<GlobalCommandRegistrationModule>();
            module.RunRegisterGlobalLifecycleServiceCommand();

            Assert.IsTrue(service.OnInitializedCalled,
                "A plain IMvcLifecycle service registered globally via CommandGlobalContainerApi.Register " +
                "must be injected and have OnInitialized() called - this API wired no callback at all before.");
        }

        [Test]
        public void CommandGlobalContainerApi_Register_PlainLifecycleService_OnCleanupCalledOnFacadeDestroy()
        {
            var service = new TrackedLifecycleService();
            PendingRegistration.Service = service;

            _moduleGo = new GameObject(nameof(GlobalCommandRegistrationModule));
            var module = _moduleGo.AddComponent<GlobalCommandRegistrationModule>();
            module.RunRegisterGlobalLifecycleServiceCommand();

            var facade = MvcFacade.InstanceOrNull;
            Assert.IsNotNull(facade, "Precondition: registering globally must have created the facade.");
            Object.DestroyImmediate(facade.gameObject);
            _facadeDestroyedInTest = true;

            Assert.IsTrue(service.OnCleanupCalled,
                "A plain service registered globally via a command must be tracked via " +
                "MvcFacade.TrackGlobalLifecycleService so OnCleanup() runs at " +
                "facade destroy, exactly like one registered via GlobalDependencies.Register.");
        }

        [Test]
        public void CommandGlobalContainerApi_Register_Proxy_IsInitialized()
        {
            var proxy = new TrackedProxy();
            PendingRegistration.Proxy = proxy;

            _moduleGo = new GameObject(nameof(GlobalCommandRegistrationModule));
            var module = _moduleGo.AddComponent<GlobalCommandRegistrationModule>();
            module.RunRegisterGlobalProxyCommand();

            Assert.IsTrue(proxy.Initialized,
                "A Proxy registered globally via CommandGlobalContainerApi.Register must be initialized - " +
                "before this fix NOTHING registered through this API was ever initialized, not just " +
                "plain IMvcLifecycle services.");
        }

        // ── CommandContainerApi.RegisterBehaviour (module-scoped, MonoBehaviour) ────

        private sealed class TrackedRegisterBehaviour : MonoBehaviour { }

        private sealed class RegisterBehaviourCommand : Command
        {
            public override void Execute()
            {
                Container.RegisterBehaviour<TrackedRegisterBehaviour>().ToLogic().AsPermanent();
            }
        }

        private sealed class RegisterBehaviourCommandModule : MvcModule
        {
            public void RunRegisterBehaviourCommand() => Commander.Run<RegisterBehaviourCommand>();
        }

        [Test]
        public void CommandContainerApi_RegisterBehaviour_CreatesAndRegistersUnderModelContainer()
        {
            _moduleGo = new GameObject(nameof(RegisterBehaviourCommandModule));
            var module = _moduleGo.AddComponent<RegisterBehaviourCommandModule>();
            module.RunRegisterBehaviourCommand();

            var resolved = module.DiContainer.Resolve<TrackedRegisterBehaviour>();

            Assert.IsNotNull(resolved,
                "A MonoBehaviour registered dynamically via CommandContainerApi.RegisterBehaviour<T>() " +
                "must be resolvable from the module container afterwards.");
            Assert.AreEqual(module.ModelContainer, resolved.transform.parent,
                "The new GameObject must be parented under the module's Model container, matching " +
                "the same-shape method on ModuleRegistrationContainerApi (the Code-style setup-time " +
                "equivalent this method was refactored away from delegating to).");
        }

        // ── Lifecycle ────────────────────────────────────────────────────────

        private GameObject _moduleGo;
        private bool _facadeDestroyedInTest;

        [TearDown]
        public void TearDown()
        {
            PendingRegistration.Service = null;
            PendingRegistration.Proxy = null;

            if (_moduleGo != null)
            {
                Object.DestroyImmediate(_moduleGo);
                _moduleGo = null;
            }

            if (!_facadeDestroyedInTest)
            {
                var facade = MvcFacade.InstanceOrNull;
                if (facade != null)
                    Object.DestroyImmediate(facade.gameObject);
            }
            _facadeDestroyedInTest = false;
        }
    }
}

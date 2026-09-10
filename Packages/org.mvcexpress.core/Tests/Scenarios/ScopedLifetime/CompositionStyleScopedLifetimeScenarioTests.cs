using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests.Scenarios
{
    /// <summary>
    /// Regression coverage for the five Code/Attribute-style Scoped registration entry points
    /// described in InternalSpecs/Architecture/SCOPED_LIFETIME_REDESIGN.md. Every path must leave
    /// the throwaway seed <see cref="Proxy"/> uninitialized.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public class CompositionStyleScopedLifetimeScenarioTests
    {
        // Shared seed-proxy test double: OnInitialized() increments InitCount so tests can
        // detect whether the throwaway instance handed to .AsScoped() was, incorrectly,
        // fully initialized before being discarded.
        private sealed class ScopedSeedProxy : Proxy
        {
            public int InitCount;
            protected override void OnInitialized() => InitCount++;
        }

        // Holds the pending seed instance across the boundary into a module/facade lifecycle
        // callback (RegisterProxies/RegisterGlobalProxies), which the framework invokes
        // internally - the test can't pass the instance as a parameter.
        private static class PendingSeed
        {
            public static ScopedSeedProxy Proxy;
        }

        private GameObject _moduleGo;
        private GameObject _facadeGo;

        [TearDown]
        public void TearDown()
        {
            PendingSeed.Proxy = null;

            if (_moduleGo != null)
            {
                Object.DestroyImmediate(_moduleGo);
                _moduleGo = null;
            }

            if (_facadeGo != null)
            {
                Object.DestroyImmediate(_facadeGo);
                _facadeGo = null;
            }

            var facade = MvcFacade.InstanceOrNull;
            if (facade != null)
            {
                Object.DestroyImmediate(facade.gameObject);
            }
        }

        // --- 1. ModuleRegistrationContainerApi.Register (module RegisterProxies) ----------
        // The registration pipeline sees the seed before the builder chooses a lifetime. It must
        // not initialize that seed after the chain commits as Scoped.

        private sealed class ModuleApiRegistrationModule : MvcModule
        {
            protected override void RegisterProxies()
            {
                Container.Register(PendingSeed.Proxy).ToLogic().AsScoped();
            }
        }

        [Test]
        public void ModuleRegistrationContainerApi_ScopedProxySeed_IsNotPrematurelyInitialized()
        {
            var proxy = new ScopedSeedProxy();
            PendingSeed.Proxy = proxy;

            _moduleGo = new GameObject(nameof(ModuleApiRegistrationModule));
            _moduleGo.AddComponent<ModuleApiRegistrationModule>(); // runs the full init pipeline synchronously

            Assert.That(proxy.InitCount, Is.EqualTo(0),
                "A scoped registration's seed instance is a throwaway discarded by .AsScoped() - the " +
                "real per-injectee instance is constructed later by the container. OnInitialized() must " +
                "never fire on the seed. ModuleRegistrationContainerApi.Register tracks the proxy via " +
                "OnContainerRegistering before .AsScoped() runs, so CompleteProxyInitialization() " +
                "initializes it anyway.");
        }

        // --- 2. CommandContainerApi.Register (dynamic runtime registration from a Command) --
        // Dynamic registration must defer initialization until the builder commits so Scoped can
        // discard the seed without running its lifecycle.

        private sealed class CommandApiRegistrationModule : MvcModule
        {
            public void RunRegisterScopedProxyCommand() => Commander.Run<RegisterScopedProxyViaCommandCommand>();
        }

        private sealed class RegisterScopedProxyViaCommandCommand : Command
        {
            public override void Execute()
            {
                Container.Register(PendingSeed.Proxy).ToLogic().AsScoped();
            }
        }

        [Test]
        public void CommandContainerApi_ScopedProxySeed_IsNotPrematurelyInitialized()
        {
            var proxy = new ScopedSeedProxy();
            PendingSeed.Proxy = proxy;

            _moduleGo = new GameObject(nameof(CommandApiRegistrationModule));
            var module = _moduleGo.AddComponent<CommandApiRegistrationModule>();

            module.RunRegisterScopedProxyCommand();

            Assert.That(proxy.InitCount, Is.EqualTo(0),
                "CommandContainerApi.Register must not call InitializeDynamicProxy " +
                "synchronously before the command's .AsScoped() call even runs, so the discarded seed " +
                "instance is fully initialized for real.");
        }

        // --- 3. ModuleGlobalContainerApi.Register (module-to-global path) ------------------
        // Module-to-global registration follows the same deferred-commit rule.

        private sealed class ModuleGlobalApiRegistrationModule : MvcModule
        {
            protected override void RegisterProxies()
            {
                Global.Register(PendingSeed.Proxy).ToLogic().AsScoped();
            }
        }

        [Test]
        public void ModuleGlobalContainerApi_ScopedProxySeed_IsNotPrematurelyInitialized()
        {
            var proxy = new ScopedSeedProxy();
            PendingSeed.Proxy = proxy;

            _moduleGo = new GameObject(nameof(ModuleGlobalApiRegistrationModule));
            _moduleGo.AddComponent<ModuleGlobalApiRegistrationModule>();

            Assert.That(proxy.InitCount, Is.EqualTo(0),
                "ModuleGlobalContainerApi.Register must not call InitializeRegisteredInstance " +
                "before the caller's .AsScoped() call runs, so the discarded seed instance is fully " +
                "initialized for real.");
        }

        // --- 4. FacadeGlobalContainerApi.Register (facade-level global path) ---------------
        // Facade-level global registration follows the same deferred-commit rule.

        private sealed class ScopedFacadeSubclass : MvcFacade
        {
            protected override void RegisterGlobalProxies()
            {
                GlobalDependencies.Register(PendingSeed.Proxy).ToLogic().AsScoped();
            }
        }

        [Test]
        public void FacadeGlobalContainerApi_ScopedProxySeed_IsNotPrematurelyInitialized()
        {
            var proxy = new ScopedSeedProxy();
            PendingSeed.Proxy = proxy;

            _facadeGo = new GameObject(nameof(ScopedFacadeSubclass));
            _facadeGo.AddComponent<ScopedFacadeSubclass>(); // Awake -> InitializeIfNeeded -> RegisterGlobalProxies

            Assert.That(proxy.InitCount, Is.EqualTo(0),
                "FacadeGlobalContainerApi.Register must not call InitializeRegisteredInstance " +
                "before the caller's .AsScoped() call runs, so the discarded seed instance is fully " +
                "initialized for real.");
        }

        // --- 5. Attribute style ([Register(Lifecycle = Scoped)]) ---------------------------
        // ModuleInitializer.cs's attribute-driven Scoped branch explicitly calls .AsScoped()
        // and then does not track or initialize the seed instance.

        [Register(typeof(AttributeStyleScopedModule), RegisterToLogic = true, Lifecycle = RegistrationLifecycle.Scoped)]
        public sealed class AttributeScopedSeedProxy : Proxy
        {
            public static int InitCount;
            protected override void OnInitialized() => InitCount++;
        }

        public sealed class AttributeStyleScopedModule : MvcModule { }

        [Test]
        public void AttributeStyleScopedRegistration_SeedProxy_IsNotInitialized_ConfirmsCorrectBehavior()
        {
            AttributeScopedSeedProxy.InitCount = 0;

            _moduleGo = new GameObject(nameof(AttributeStyleScopedModule));
            _moduleGo.AddComponent<AttributeStyleScopedModule>();

            Assert.That(AttributeScopedSeedProxy.InitCount, Is.EqualTo(0),
                "The attribute-driven Scoped path must not track or initialize the seed instance.");
        }
    }
}

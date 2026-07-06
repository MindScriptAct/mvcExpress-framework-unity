using System;
using mvcExpress.Internal.Commands;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests.Scenarios
{
    /// <summary>
    /// Multi-module scenarios built from the same runtime primitives each MvcModule owns:
    /// one local DI container, one message bus, one command processor, and one module context.
    /// The tests keep global dependencies as an explicit shared container so the distinction
    /// between module-local and application-wide scope stays visible.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public class MultiModuleScenarioTests
    {
        private ModuleHarness _moduleA;
        private ModuleHarness _moduleB;
        private MvcDiContainer _globalContainer;

        private sealed class NoInitModule : MvcModule
        {
            protected override void Awake() { }
            protected override void OnDestroy() { }
        }

        private sealed class ModuleHarness : IDisposable
        {
            public readonly GameObject GameObject;
            public readonly NoInitModule Module;
            public readonly MvcDiContainer Container;
            public readonly MvcMessageBus MessageBus;
            public readonly MvcCommandProcessor Processor;

            public ModuleHarness(string name)
            {
                GameObject = new GameObject(name);
                Module = GameObject.AddComponent<NoInitModule>();
                Container = new MvcDiContainer();
                MessageBus = new MvcMessageBus();
                Processor = new MvcCommandProcessor(typeof(NoInitModule), Container, MessageBus, Module);
            }

            public void Dispose()
            {
                Processor.Dispose();
                MessageBus.Dispose();
                Container.Dispose();

                if (GameObject != null)
                {
                    Object.DestroyImmediate(GameObject);
                }
            }
        }

        private sealed class ModuleOnlyService
        {
            public readonly string Owner;

            public ModuleOnlyService(string owner)
            {
                Owner = owner;
            }
        }

        private readonly struct LocalBusMessage : IMessage<int> { }

        private sealed class DisposableService : IDisposable
        {
            public bool Disposed { get; private set; }
            public void Dispose()
            {
                Disposed = true;
            }
        }

        private sealed class LifecycleProxy : Proxy
        {
            public bool Initialized { get; private set; }
            public bool CleanedUp { get; private set; }

            protected override void OnInitialized()
            {
                Initialized = true;
            }

            protected override void OnCleanup()
            {
                CleanedUp = true;
            }
        }

        private sealed class LocalDependencyProxy : Proxy
        {
            public readonly string Owner;

            public LocalDependencyProxy(string owner)
            {
                Owner = owner;
            }
        }

        private sealed class GlobalSettingsProxy : Proxy
        {
            public readonly string Value;

            public GlobalSettingsProxy(string value)
            {
                Value = value;
            }
        }

        [SetUp]
        public void SetUp()
        {
            _moduleA = new ModuleHarness("ScenarioModuleA");
            _moduleB = new ModuleHarness("ScenarioModuleB");
            _globalContainer = new MvcDiContainer();
        }

        [TearDown]
        public void TearDown()
        {
            _moduleA?.Dispose();
            _moduleB?.Dispose();
            _globalContainer?.Dispose();
        }

        [Test]
        public void TwoModules_IndependentScopes_ServicesNotShared()
        {
            var serviceA = new ModuleOnlyService("A");
            var serviceB = new ModuleOnlyService("B");
            _moduleA.Container.Register(serviceA).ToLogic().AsPermanent();
            _moduleB.Container.Register(serviceB).ToLogic().AsPermanent();

            var resolvedA = _moduleA.Container.Resolve<ModuleOnlyService>();
            var resolvedB = _moduleB.Container.Resolve<ModuleOnlyService>();

            Assert.That(resolvedA, Is.SameAs(serviceA),
                "Module A should resolve the service instance registered in Module A's local container.");
            Assert.That(resolvedB, Is.SameAs(serviceB),
                "Module B should resolve its own local service instance, not Module A's instance.");
            Assert.That(resolvedA, Is.Not.SameAs(resolvedB),
                "Equal service types in different modules must remain isolated by container instance.");
        }

        [Test]
        public void TwoModules_IndependentMessageBuses_MessageDoesNotCrossModules()
        {
            var moduleACount = 0;
            var moduleBCount = 0;
            _moduleA.MessageBus.Subscribe<LocalBusMessage, int>(_ => moduleACount++);
            _moduleB.MessageBus.Subscribe<LocalBusMessage, int>(_ => moduleBCount++);

            _moduleA.MessageBus.Publish<LocalBusMessage, int>(1);

            Assert.That(moduleACount, Is.EqualTo(1),
                "Publishing on Module A's bus should notify Module A subscribers.");
            Assert.That(moduleBCount, Is.EqualTo(0),
                "A separate message bus instance should not receive Module A publishes.");
        }

        [Test]
        public void ModuleLifecycle_CreateInitializeDestroy_CleanupComplete()
        {
            var service = new DisposableService();
            _moduleA.Container.Register(service).ToLogic().AsPermanent();
            _moduleA.Dispose();
            _moduleA = null;

            Assert.That(service.Disposed, Is.True,
                "Disposing a module harness should dispose permanent disposable services registered in its container.");
        }

        [Test]
        public void ModuleLifecycle_AllActorsDisposedOnModuleDestroy()
        {
            var proxy = new LifecycleProxy();
            _moduleA.Container.Register(proxy).ToLogic().AsPermanent();
            proxy.Initialize(typeof(NoInitModule), _moduleA.MessageBus, _moduleA.Container);

            _moduleA.Dispose();
            _moduleA = null;

            Assert.That(proxy.Initialized, Is.True,
                "Precondition: the proxy should receive OnInitialized before module cleanup.");
            Assert.That(proxy.CleanedUp, Is.True,
                "Disposing the owning module container should dispose initialized proxies and call OnCleanup.");
        }

        [Test]
        public void InterModuleDependency_ViaGlobalProxy_ResolvedCorrectly()
        {
            var globalProxy = new GlobalSettingsProxy("shared-settings");
            _globalContainer.Register(globalProxy).ToLogic().AsPermanent();

            var resolvedByModuleA = _globalContainer.Resolve<GlobalSettingsProxy>();
            var resolvedByModuleB = _globalContainer.Resolve<GlobalSettingsProxy>();

            Assert.That(resolvedByModuleA, Is.SameAs(globalProxy),
                "A dependency registered in the shared global container should be resolvable from Module A's global lookup path.");
            Assert.That(resolvedByModuleB, Is.SameAs(globalProxy),
                "Module B should see the same global proxy instance rather than a module-local copy.");
        }

        [Test]
        public void InterModuleDependency_ViaLocalProxy_NotResolvable()
        {
            var localProxy = new LocalDependencyProxy("A-only");
            _moduleA.Container.Register(localProxy).ToLogic().AsPermanent();

            Assert.Throws<InvalidOperationException>(() => _moduleB.Container.Resolve<LocalDependencyProxy>(),
                "Module B should not resolve a proxy registered only in Module A's local container. Shared dependencies belong in global scope.");
        }
    }
}

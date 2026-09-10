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
    /// Multi-module scenarios for the fundamental application topology: each module owns its
    /// local DI container, command processor, and module context, while all modules communicate
    /// through exactly one application-wide message bus. Global dependencies use an explicit
    /// shared container so dependency scope remains distinct from message delivery scope.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public class MultiModuleScenarioTests
    {
        private ModuleHarness _moduleA;
        private ModuleHarness _moduleB;
        private MvcDiContainer _globalContainer;
        private MvcMessageBus _messageBus;

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

            public ModuleHarness(string name, MvcMessageBus messageBus)
            {
                GameObject = new GameObject(name);
                Module = GameObject.AddComponent<NoInitModule>();
                Container = new MvcDiContainer();
                MessageBus = messageBus;
                Processor = new MvcCommandProcessor(typeof(NoInitModule), Container, MessageBus, Module);
            }

            public void Dispose()
            {
                Processor.Dispose();
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
            _messageBus = new MvcMessageBus();
            _moduleA = new ModuleHarness("ScenarioModuleA", _messageBus);
            _moduleB = new ModuleHarness("ScenarioModuleB", _messageBus);
            _globalContainer = new MvcDiContainer();
        }

        [TearDown]
        public void TearDown()
        {
            _moduleA?.Dispose();
            _moduleB?.Dispose();
            _globalContainer?.Dispose();
            _messageBus?.Dispose();
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
        public void TwoModules_SharedApplicationBus_MessageReachesBothModules()
        {
            var moduleACount = 0;
            var moduleBCount = 0;

            Assert.That(_moduleA.MessageBus, Is.SameAs(_moduleB.MessageBus),
                "Every module in one application must use the same MessageBus instance.");

            _moduleA.MessageBus.Subscribe<LocalBusMessage, int>(_ => moduleACount++);
            _moduleB.MessageBus.Subscribe<LocalBusMessage, int>(_ => moduleBCount++);

            _moduleA.MessageBus.Publish<LocalBusMessage, int>(1);

            Assert.That(moduleACount, Is.EqualTo(1),
                "Publishing from Module A must notify Module A subscribers.");
            Assert.That(moduleBCount, Is.EqualTo(1),
                "Publishing from Module A must notify matching subscribers in Module B.");
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

        private readonly struct CrossModuleMessage : IMessage<int> { }

        private sealed class CrossModuleCommand : Command<int>
        {
            public static int ExecuteCount;
            public static int LastValue;

            public static void Reset()
            {
                ExecuteCount = 0;
                LastValue = 0;
            }

            public override void Execute(int value)
            {
                ExecuteCount++;
                LastValue = value;
            }
        }

        private sealed class CrossModuleMediator : MediatorBehaviour
        {
            public static int ReceivedCount;

            public static void Reset() => ReceivedCount = 0;

            protected override void OnInitialized()
            {
                Messenger.Subscribe<CrossModuleMessage, int>(_ => ReceivedCount++);
            }
        }

        [Test]
        public void CrossModuleRoundTrip_ModuleAPublishesOnSharedBus_ModuleBRespondsThenNoOpsAfterDestroy()
        {
            CrossModuleCommand.Reset();
            CrossModuleMediator.Reset();

            var moduleBProcessor = new MvcCommandProcessor(typeof(NoInitModule), _moduleB.Container, _messageBus, _moduleB.Module);
            moduleBProcessor.BindCommand<CrossModuleCommand, CrossModuleMessage, int>();

            var mediatorGo = new GameObject("CrossModuleMediator");
            mediatorGo.transform.SetParent(_moduleB.GameObject.transform);
            var mediator = mediatorGo.AddComponent<CrossModuleMediator>();
            mediator.Initialize(_moduleB.Module, _moduleB.Container, _messageBus);

            _moduleA.MessageBus.Publish<CrossModuleMessage, int>(5);

            Assert.That(CrossModuleCommand.ExecuteCount, Is.EqualTo(1),
                "The first publish on the shared bus must execute Module B's bound command.");
            Assert.That(CrossModuleCommand.LastValue, Is.EqualTo(5),
                "Module B's command must receive the payload from Module A's publish.");
            Assert.That(CrossModuleMediator.ReceivedCount, Is.EqualTo(1),
                "The first publish must also reach Module B's mediator subscription.");

            moduleBProcessor.Dispose();
            Object.DestroyImmediate(mediatorGo);

            Assert.DoesNotThrow(() => _moduleA.MessageBus.Publish<CrossModuleMessage, int>(9),
                "A second publish after Module B's command processor and mediator are torn down " +
                "must be a clean no-op - no exceptions from dangling subscriptions or bindings.");
            Assert.That(CrossModuleCommand.ExecuteCount, Is.EqualTo(1),
                "The second publish must not execute the now-disposed command processor's binding again.");
            Assert.That(CrossModuleMediator.ReceivedCount, Is.EqualTo(1),
                "The second publish must not reach the destroyed mediator - its subscription must " +
                "have been cleaned up automatically.");
        }
    }
}

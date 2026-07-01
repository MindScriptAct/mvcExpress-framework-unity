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
    /// Edge-case scenarios for registration failures, rollback behavior, unbound messages,
    /// and cleanup idempotency. These tests document the framework contract where the public
    /// API intentionally differs from older TODO wording: registering a service to view scope
    /// is valid, but resolving a view-only dependency from logic scope is not.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public class EdgeCaseScenarioTests
    {
        private MvcDiContainer _container;
        private MvcMessageBus _messageBus;
        private MvcCommandProcessor _processor;
        private GameObject _moduleGo;
        private NoInitModule _module;

        private sealed class NoInitModule : MvcModule
        {
            protected override void Awake() { }
            protected override void OnDestroy() { }
        }

        private sealed class ScenarioService : IDisposable
        {
            public bool Disposed { get; private set; }
            public void Dispose()
            {
                Disposed = true;
            }
        }

        private sealed class AnotherScenarioService { }

        private readonly struct UnboundMessage : IMessage { }
        private readonly struct BoundMessage : IMessage { }

        private sealed class BoundCommand : Command
        {
            public static int ExecuteCount;

            public static void Reset()
            {
                ExecuteCount = 0;
            }

            public override void Execute()
            {
                ExecuteCount++;
            }
        }

        [SetUp]
        public void SetUp()
        {
            BoundCommand.Reset();

            _moduleGo = new GameObject("EdgeCaseScenarioModule");
            _module = _moduleGo.AddComponent<NoInitModule>();
            _container = new MvcDiContainer();
            _messageBus = new MvcMessageBus();
            _processor = new MvcCommandProcessor(typeof(NoInitModule), _container, _messageBus, _module);
        }

        [TearDown]
        public void TearDown()
        {
            _processor?.Dispose();
            _messageBus?.Dispose();
            _container?.Dispose();

            if (_moduleGo != null)
            {
                Object.DestroyImmediate(_moduleGo);
                _moduleGo = null;
                _module = null;
            }
        }

        [Test]
        public void DuplicateRegistration_SameTypeRegisteredTwice_ThrowsInvalidOperation()
        {
            _container.Register(new ScenarioService()).ToLogic().AsPersistent();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _container.Register(new ScenarioService()).ToLogic().AsPersistent());

            Assert.That(ex.Message, Does.Contain("already registered"),
                "Registering the same concrete type twice in the same scope should fail loudly instead of replacing the original dependency.");
        }

        [Test]
        public void DuplicateRegistration_RollbackOnFailure_ContainerStateUnchanged()
        {
            var original = new ScenarioService();
            _container.Register(original).ToView().AsPersistent();

            var replacement = new ScenarioService();
            Assert.Throws<InvalidOperationException>(() =>
                _container.Register(replacement).ToLogic().ToView().AsPersistent(),
                "The dual registration should fail because view scope already contains ScenarioService.");

            Assert.Throws<InvalidOperationException>(() => _container.Resolve<ScenarioService>(),
                "The failed dual registration must roll back the logic-scope half so no partial dependency remains.");
            Assert.DoesNotThrow(() => _container.Register(new AnotherScenarioService()).ToLogic().AsPersistent(),
                "After rollback, unrelated registrations should still complete, proving the container remains usable.");
        }

        [Test]
        public void ContainerException_FailedRegistration_NoPartialState()
        {
            var service = new ScenarioService();
            var builder = _container.Register(service);

            Assert.Throws<InvalidOperationException>(() => builder.AsPersistent(),
                "A registration without ToLogic or ToView is invalid and should throw before modifying container state.");
            Assert.Throws<InvalidOperationException>(() => _container.Resolve<ScenarioService>(),
                "A failed registration builder should not leave a resolvable dependency behind.");
        }

        [Test]
        public void WrongLayer_ServiceRegisteredToView_LogicResolveThrows()
        {
            var service = new ScenarioService();
            _container.Register(service).ToView().AsPersistent();

            Assert.Throws<InvalidOperationException>(() => _container.Resolve<ScenarioService>(),
                "Services may be registered to view scope, but logic actors cannot resolve view-only dependencies.");
            Assert.DoesNotThrow(() => _container.Register(new AnotherScenarioService()).ToLogic().AsPersistent(),
                "A view-only registration should not poison the logic container; unrelated logic registrations should still work.");
        }

        [Test]
        public void CommandBinding_UnregisteredMessage_NoExecution()
        {
            _processor.BindCommand<BoundCommand, BoundMessage>();

            _messageBus.Publish<UnboundMessage>();

            Assert.That(BoundCommand.ExecuteCount, Is.EqualTo(0),
                "Publishing an unbound message type should be a no-op for commands bound to other message types.");
        }

        [Test]
        public void GlobalContainerLifecycle_FacadeDestroyed_GlobalActorsCleanedUp()
        {
            var globalContainer = new MvcDiContainer();
            var service = new ScenarioService();
            globalContainer.Register(service).ToLogic().AsPersistent();

            globalContainer.Dispose();

            Assert.That(service.Disposed, Is.True,
                "Disposing the facade-owned global container should dispose persistent global service instances.");
        }

        [Test]
        public void GlobalContainerLifecycle_ModuleDestroyedBeforeFacade_HandledGracefully()
        {
            var globalContainer = new MvcDiContainer();
            globalContainer.Register(new AnotherScenarioService()).ToLogic().AsPersistent();
            _container.Register(new ScenarioService()).ToLogic().AsPersistent();

            Assert.DoesNotThrow(() =>
            {
                _container.Dispose();
                _container = null;
                globalContainer.Dispose();
            }, "Destroying a module before the facade/global container should be a valid shutdown order.");
        }

        [Test]
        public void DoubleDispose_BusDisposedTwice_NoException()
        {
            _messageBus.Subscribe<UnboundMessage>(() => { });
            _messageBus.Dispose();

            Assert.DoesNotThrow(() => _messageBus.Dispose(),
                "Message bus disposal should be idempotent so repeated teardown paths do not fail.");
            _messageBus = null;
        }
    }
}


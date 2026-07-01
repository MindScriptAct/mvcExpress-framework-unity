using mvcExpress.Internal.Commands;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests.Scenarios
{
    /// <summary>
    /// Transient dependency lifecycle scenarios for pooled commands.
    /// The important integration behavior is not just that a transient dependency can be
    /// injected. It is that a command already returned to its pool is invalidated when one
    /// of its transient dependencies is unregistered, so the next execution starts from a
    /// freshly created command and a freshly resolved dependency.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public class TransientLifecycleScenarioTests
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

        private sealed class ScenarioTransientProxy : Proxy
        {
            private static int _nextId;

            public ScenarioTransientProxy()
            {
                Id = ++_nextId;
            }

            public int Id { get; }

            public static void ResetIds()
            {
                _nextId = 0;
            }
        }

        private sealed class UsesTransientProxyCommand : Command
        {
            public static int CreatedCount;
            public static int ExecuteCount;
            public static int LastProxyId;

            [Inject] private ScenarioTransientProxy _proxy;

            public UsesTransientProxyCommand()
            {
                CreatedCount++;
            }

            public static void Reset()
            {
                CreatedCount = 0;
                ExecuteCount = 0;
                LastProxyId = 0;
            }

            public override void Execute()
            {
                ExecuteCount++;
                Assert.That(_proxy, Is.Not.Null,
                    "A command that declares a transient proxy dependency should receive it before Execute runs.");
                LastProxyId = _proxy.Id;
            }
        }

        private sealed class FirstUsesTransientProxyCommand : Command
        {
            public static int CreatedCount;
            public static int LastProxyId;

            [Inject] private ScenarioTransientProxy _proxy;

            public FirstUsesTransientProxyCommand()
            {
                CreatedCount++;
            }

            public static void Reset()
            {
                CreatedCount = 0;
                LastProxyId = 0;
            }

            public override void Execute()
            {
                Assert.That(_proxy, Is.Not.Null,
                    "The first command pool should resolve the transient proxy during execution.");
                LastProxyId = _proxy.Id;
            }
        }

        private sealed class SecondUsesTransientProxyCommand : Command
        {
            public static int CreatedCount;
            public static int LastProxyId;

            [Inject] private ScenarioTransientProxy _proxy;

            public SecondUsesTransientProxyCommand()
            {
                CreatedCount++;
            }

            public static void Reset()
            {
                CreatedCount = 0;
                LastProxyId = 0;
            }

            public override void Execute()
            {
                Assert.That(_proxy, Is.Not.Null,
                    "The second command pool should independently resolve the same transient proxy dependency.");
                LastProxyId = _proxy.Id;
            }
        }

        [SetUp]
        public void SetUp()
        {
            ScenarioTransientProxy.ResetIds();
            UsesTransientProxyCommand.Reset();
            FirstUsesTransientProxyCommand.Reset();
            SecondUsesTransientProxyCommand.Reset();

            _moduleGo = new GameObject("TransientLifecycleScenarioModule");
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
        public void TransientProxy_RegisterAndResolveFromCommand_InjectedCorrectly()
        {
            _container.Register(new ScenarioTransientProxy()).ToLogic().AsTransient();

            _processor.Run<UsesTransientProxyCommand>();

            Assert.That(UsesTransientProxyCommand.ExecuteCount, Is.EqualTo(1),
                "The command should execute after its transient proxy dependency is resolved.");
            Assert.That(UsesTransientProxyCommand.LastProxyId, Is.EqualTo(1),
                "The command should observe the exact transient proxy instance that was registered in the container.");
        }

        [Test]
        public void TransientProxy_UnregisteredAfterCommandHoldsRef_PoolClears()
        {
            _container.Register(new ScenarioTransientProxy()).ToLogic().AsTransient();
            _processor.CreatePool<UsesTransientProxyCommand>(poolSize: 1);
            _processor.Run<UsesTransientProxyCommand>();

            _container.Unregister<ScenarioTransientProxy>();
            _container.Register(new ScenarioTransientProxy()).ToLogic().AsTransient();
            _processor.Run<UsesTransientProxyCommand>();

            Assert.That(UsesTransientProxyCommand.CreatedCount, Is.EqualTo(2),
                "Unregistering a transient dependency should clear pooled commands that previously resolved it, forcing a fresh command on next execution.");
            Assert.That(UsesTransientProxyCommand.LastProxyId, Is.EqualTo(2),
                "After re-registering the transient proxy, the next command execution should resolve the new proxy instance.");
        }

        [Test]
        public void TransientProxy_ReRegistered_NewInstanceInjectedOnNextExecute()
        {
            _container.Register(new ScenarioTransientProxy()).ToLogic().AsTransient();
            _processor.Run<UsesTransientProxyCommand>();
            var firstProxyId = UsesTransientProxyCommand.LastProxyId;

            _container.Unregister<ScenarioTransientProxy>();
            _container.Register(new ScenarioTransientProxy()).ToLogic().AsTransient();
            _processor.Run<UsesTransientProxyCommand>();

            Assert.That(firstProxyId, Is.EqualTo(1),
                "Precondition: the first execution should use the original transient proxy instance.");
            Assert.That(UsesTransientProxyCommand.LastProxyId, Is.EqualTo(2),
                "Re-registering a transient proxy should make the new instance visible to subsequent command injections.");
        }

        [Test]
        public void TransientProxy_MultipleCommands_EachPoolClearedOnUnregister()
        {
            _container.Register(new ScenarioTransientProxy()).ToLogic().AsTransient();
            _processor.CreatePool<FirstUsesTransientProxyCommand>(poolSize: 1);
            _processor.CreatePool<SecondUsesTransientProxyCommand>(poolSize: 1);

            _processor.Run<FirstUsesTransientProxyCommand>();
            _processor.Run<SecondUsesTransientProxyCommand>();

            _container.Unregister<ScenarioTransientProxy>();
            _container.Register(new ScenarioTransientProxy()).ToLogic().AsTransient();

            _processor.Run<FirstUsesTransientProxyCommand>();
            _processor.Run<SecondUsesTransientProxyCommand>();

            Assert.That(FirstUsesTransientProxyCommand.CreatedCount, Is.EqualTo(2),
                "The first command pool should be cleared when the shared transient proxy dependency is unregistered.");
            Assert.That(SecondUsesTransientProxyCommand.CreatedCount, Is.EqualTo(2),
                "The second command pool should also be cleared, proving invalidation is dependency-wide rather than command-specific.");
            Assert.That(FirstUsesTransientProxyCommand.LastProxyId, Is.EqualTo(2),
                "The first command should resolve the newly registered transient proxy after invalidation.");
            Assert.That(SecondUsesTransientProxyCommand.LastProxyId, Is.EqualTo(2),
                "The second command should resolve the same newly registered transient proxy after invalidation.");
        }
    }
}

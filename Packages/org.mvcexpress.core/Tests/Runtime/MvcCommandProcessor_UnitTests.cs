// IMPORTANT: MvcCommandProcessor uses static CommandPool<TCommand> arrays.
// Always Dispose() the processor in TearDown to prevent pool state leaking between tests.
using System;
using NUnit.Framework;
using mvcExpress;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Commands;
using mvcExpress.Internal.Messaging;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests
{
    public class MvcCommandProcessor_UnitTests
    {
        private MvcDiContainer _diContainer;
        private MvcMessageBus _messageBus;
        private MvcCommandProcessor _processor;
        private GameObject _moduleGo;
        private NoInitModule _module;

        private class DummyModule : MvcModule {}
        private class NoInitModule : MvcModule
        {
            protected override void Awake() {}
            protected override void OnDestroy() {}
        }

        // Private nested types give unique generic slots in static storage
        private struct TestMessage : IMessage {}
        private class TestCommand : Command { public override void Execute() {} }
        private class TransientDependency {}
        private class ScopedDependencyProxy : Proxy {}

        private class CountingCommand : Command
        {
            public static int CreatedCount;
            public static int ExecuteCount;

            public CountingCommand() => CreatedCount++;
            public override void Execute() => ExecuteCount++;

            public static void Reset()
            {
                CreatedCount = 0;
                ExecuteCount = 0;
            }
        }

        private class TransientDependencyCommand : Command
        {
            [Inject] private TransientDependency _dependency;
            public override void Execute()
            {
                Assert.That(_dependency, Is.Not.Null);
            }
        }

        private class ScopedDependencyCommand : Command
        {
            public static int CreatedCount;

            [Inject] private ScopedDependencyProxy _proxy;

            public ScopedDependencyCommand() => CreatedCount++;
            public override void Execute()
            {
                Assert.That(_proxy, Is.Not.Null);
            }

            public static void Reset() => CreatedCount = 0;
        }

        // Explicit IDisposable implementation, as recommended in MvcCommandBase's remarks -
        // only reachable through an IDisposable reference, same as the pool uses internally.
        private class DisposableScopedCommand : Command, IDisposable
        {
            public static int DisposeCount;

            [Inject] private ScopedDependencyProxy _proxy;

            public override void Execute() => Assert.That(_proxy, Is.Not.Null);

            void IDisposable.Dispose() => DisposeCount++;

            public static void Reset() => DisposeCount = 0;
        }

        // Invalidates itself mid-execution (as an external console tool could via the
        // internal Invalidate() surface) to prove invalidated instances are still disposed.
        private class InvalidatingDisposableCommand : Command, IDisposable
        {
            public static int DisposeCount;

            public override void Execute() => Invalidate();

            void IDisposable.Dispose() => DisposeCount++;

            public static void Reset() => DisposeCount = 0;
        }

        [SetUp]
        public void Setup()
        {
            CountingCommand.Reset();
            ScopedDependencyCommand.Reset();

            _moduleGo = new GameObject("NoInitModule");
            _module = _moduleGo.AddComponent<NoInitModule>();
            _diContainer = new MvcDiContainer();
            _messageBus = new MvcMessageBus();
            _processor = new MvcCommandProcessor(typeof(DummyModule), _diContainer, _messageBus, _module);
        }

        [TearDown]
        public void Teardown()
        {
            _processor?.Dispose();
            _messageBus.Dispose();
            _diContainer.Dispose();
            if (_moduleGo != null)
            {
                Object.DestroyImmediate(_moduleGo);
                _moduleGo = null;
                _module = null;
            }
        }

        [Test]
        public void BindCommand_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => _processor.BindCommand<TestCommand, TestMessage>(poolSize: 0),
                "BindCommand must wire successfully on a valid processor.");
        }

        [Test]
        public void BindCommand_ThenUnbind_DoesNotThrow()
        {
            _processor.BindCommand<TestCommand, TestMessage>(poolSize: 0);

            Assert.DoesNotThrow(() => _processor.UnbindCommand<TestCommand, TestMessage>(),
                "UnbindCommand must succeed after a successful BindCommand.");
        }

        [Test]
        public void BindCommand_ThenUnbind_UpdatesBindingDiagnostics()
        {
            _processor.BindCommand<TestCommand, TestMessage>(poolSize: 0);

            Assert.That(_processor.IsBound<TestCommand, TestMessage>(), Is.True);
            Assert.That(_processor.HasMessageBindings<TestMessage>(), Is.True);
            Assert.That(_processor.GetCommandBindingCount<TestCommand>(), Is.EqualTo(1));
            Assert.That(_processor.GetBoundMessageCount(), Is.EqualTo(1));

            _processor.UnbindCommand<TestCommand, TestMessage>();

            Assert.That(_processor.IsBound<TestCommand, TestMessage>(), Is.False);
            Assert.That(_processor.HasMessageBindings<TestMessage>(), Is.False);
            Assert.That(_processor.GetCommandBindingCount<TestCommand>(), Is.EqualTo(0));
            Assert.That(_processor.GetBoundMessageCount(), Is.EqualTo(0));
        }

        [Test]
        public void Run_WithReusableCommand_ReturnsCommandToPool()
        {
            _processor.CreatePool<CountingCommand>(1);

            _processor.Run<CountingCommand>();
            _processor.Run<CountingCommand>();

            Assert.That(CountingCommand.ExecuteCount, Is.EqualTo(2));
            Assert.That(CountingCommand.CreatedCount, Is.EqualTo(1),
                "A valid command with no scoped dependency must be returned and reused.");
        }

        [Test]
        public void Run_WithScopedDependency_DoesNotReturnCommandToPool()
        {
            _diContainer.Register(new ScopedDependencyProxy()).ToLogic().AsScoped();
            _processor.CreatePool<ScopedDependencyCommand>(1);

            _processor.Run<ScopedDependencyCommand>();
            _processor.Run<ScopedDependencyCommand>();

            Assert.That(ScopedDependencyCommand.CreatedCount, Is.EqualTo(2),
                "Commands that resolve scoped dependencies must be discarded instead of pooled.");
        }

        [Test]
        public void Run_WithScopedDependency_DisposesDiscardedDisposableCommand()
        {
            DisposableScopedCommand.Reset();
            _diContainer.Register(new ScopedDependencyProxy()).ToLogic().AsScoped();
            _processor.CreatePool<DisposableScopedCommand>(1);

            _processor.Run<DisposableScopedCommand>();

            Assert.That(DisposableScopedCommand.DisposeCount, Is.EqualTo(1),
                "A command discarded due to a scoped dependency must still be disposed, even though it bypasses BoundedObjectPool.Return().");
        }

        [Test]
        public void Run_WithInvalidatedCommand_IsDisposedAndNotPooled()
        {
            InvalidatingDisposableCommand.Reset();
            var pool = _processor.GetOrCreatePool<InvalidatingDisposableCommand>(1);

            _processor.Run<InvalidatingDisposableCommand>();

            Assert.That(pool.GetStatistics().CurrentSize, Is.EqualTo(0),
                "A command that invalidates itself must not be returned to the pool.");
            Assert.That(InvalidatingDisposableCommand.DisposeCount, Is.EqualTo(1),
                "A command that invalidates itself must still be disposed, even though it bypasses BoundedObjectPool.Return().");
        }

        [Test]
        public void Unregister_TransientDependency_ClearsDependentCommandPool()
        {
            _diContainer.Register(new TransientDependency()).ToLogic().AsTransient();
            var pool = _processor.GetOrCreatePool<TransientDependencyCommand>(1);

            _processor.Run<TransientDependencyCommand>();
            Assert.That(pool.GetStatistics().CurrentSize, Is.EqualTo(1),
                "Precondition: command must be returned to the pool before dependency removal.");

            _diContainer.Unregister<TransientDependency>();

            Assert.That(pool.GetStatistics().CurrentSize, Is.EqualTo(0),
                "Removing a transient dependency must invalidate and clear dependent command pools.");
        }

#if UNITY_EDITOR || MVC_LOGGING
        [Test]
        public void BindCommand_WithPoolSize_PoolStatisticsAccessible()
        {
            _processor.BindCommand<TestCommand, TestMessage>(poolSize: 4);

            var stats = _processor.GetPoolStatistics();
            Assert.That(stats, Is.Not.Null,
                "GetPoolStatistics must return data after a pooled BindCommand.");
        }

        [Test]
        public void GetCommandBindingSnapshot_WithSyncBinding_ReturnsMessageCommandAndPool()
        {
            _processor.BindCommand<TestCommand, TestMessage>(poolSize: 4);

            var snapshot = new System.Collections.Generic.List<MvcCommandProcessor.CommandBindingSnapshot>();
            _processor.GetCommandBindingSnapshot(snapshot);

            Assert.That(snapshot.Count, Is.EqualTo(1));
            Assert.That(snapshot[0].MessageType, Is.EqualTo(typeof(TestMessage)));
            Assert.That(snapshot[0].CommandType, Is.EqualTo(typeof(TestCommand)));
            Assert.That(snapshot[0].Mode, Is.EqualTo(MvcCommandProcessor.CommandBindingMode.Sync));
            Assert.That(snapshot[0].PoolMax, Is.EqualTo(4));
        }
#endif

        [Test]
        public void Dispose_IsIdempotent()
        {
            _processor.BindCommand<TestCommand, TestMessage>(poolSize: 0);
            _processor.Dispose();

            Assert.DoesNotThrow(() => _processor.Dispose(),
                "Dispose must be safe to call twice.");

            _processor = null; // prevent TearDown from disposing again
        }
    }
}

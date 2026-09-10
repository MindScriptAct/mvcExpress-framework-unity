// IMPORTANT: MvcCommandProcessor uses static CommandPool<TCommand> arrays.
// Always Dispose() the processor in TearDown to prevent pool state leaking between tests.
using System;
using System.Threading.Tasks;
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

        // Used only to have a distinct generic slot in static pool storage for the
        // background-thread pool-creation test below - never actually executed.
        private class BackgroundPoolTestCommand : Command
        {
            public override void Execute() { }
        }

        private class TestAsyncCommand : CommandAsync
        {
            public override Task ExecuteAsync() => Task.CompletedTask;
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

        // Updated under the Scoped lifetime redesign (see
        // InternalSpecs/Architecture/SCOPED_LIFETIME_REDESIGN.md): a scoped dependency is now a
        // dedicated instance tied to the owning command INSTANCE's own lifetime, not a signal to
        // force-discard that instance after one use. These two tests used to assert the opposite
        // (v1) behavior - see Tests/Scenarios/ScopedLifetime/CommandScopedLifetimeScenarioTests.cs
        // for the full v2 behavioral suite this pair now matches.

        [Test]
        public void Run_WithScopedDependency_ReusesCommandFromPool()
        {
            _diContainer.Register(new ScopedDependencyProxy()).ToLogic().AsScoped();
            _processor.CreatePool<ScopedDependencyCommand>(1);

            _processor.Run<ScopedDependencyCommand>();
            _processor.Run<ScopedDependencyCommand>();

            Assert.That(ScopedDependencyCommand.CreatedCount, Is.EqualTo(1),
                "A command with a scoped dependency must now be reused across executions, exactly " +
                "like any other pooled command - the scoped dependency is dedicated to this command " +
                "instance's own lifetime, not a signal to discard it after one use.");
        }

        [Test]
        public void Run_WithScopedDependency_NotDisposedWhileStillPooled()
        {
            DisposableScopedCommand.Reset();
            _diContainer.Register(new ScopedDependencyProxy()).ToLogic().AsScoped();
            _processor.CreatePool<DisposableScopedCommand>(1);

            _processor.Run<DisposableScopedCommand>();

            Assert.That(DisposableScopedCommand.DisposeCount, Is.EqualTo(0),
                "A command with a scoped dependency must not be disposed after an ordinary execution " +
                "that leaves it sitting in the pool for reuse - only an actual discard (capacity " +
                "overflow, Invalidate(), or processor/module teardown) disposes it.");
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

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        /// <summary>
        /// PERF-07 follow-up (thread-guard fix, part 1 - the shared authoritative main thread).
        /// A command pool's owner-thread check used to compare against whichever thread happened to
        /// construct the pool, not the framework's actual main thread. GetOrCreatePool itself never
        /// validated a thread at construction time (nothing did, before this fix) - a pool could be
        /// silently born off the main thread, and only later, when the REAL main thread tried to use
        /// it, would the (backwards) rejection surface. Constructing the pool directly here from a
        /// background thread reproduces that exact condition; the main thread's own Get() must still
        /// succeed, because the check now compares against the shared
        /// MvcMessageBus.MainThreadId - the same answer no matter which thread constructed this
        /// particular pool.
        /// </summary>
        [Test]
        public void GetOrCreatePool_PoolFirstConstructedOnBackgroundThread_MainThreadCanStillUseIt()
        {
            var pool = Task.Run(() => _processor.GetOrCreatePool<BackgroundPoolTestCommand>(1)).Result;

            Assert.DoesNotThrow(() => pool.Get(),
                "The pool's thread check must compare against the framework's shared main thread, " +
                "not the thread that happened to construct the pool - otherwise a pool first " +
                "touched off-thread would reject the real main thread forever after.");
        }

        /// <summary>
        /// PERF-07 follow-up (thread-guard fix, part 2 - validate at the actual entry point).
        /// Unlike message-triggered execution (already main-thread-only behind
        /// MvcMessageBus.Publish's own validation), Run() had no main-thread guarantee at all before
        /// this fix - it went straight to GetOrCreatePool with nothing checking the calling thread.
        /// </summary>
        [Test]
        public void Run_CalledFromBackgroundThread_ThrowsInvalidOperationException()
        {
            var backgroundTask = Task.Run(() => _processor.Run<TestCommand>());

            var aggregate = Assert.Throws<AggregateException>(() => backgroundTask.Wait());
            Assert.IsInstanceOf<InvalidOperationException>(aggregate.InnerException,
                "Direct command execution (Run) must validate main-thread use itself, before ever " +
                "touching a command pool - not rely on the pool's own check as the only guard.");
            StringAssert.Contains("Commander.Run", aggregate.InnerException.Message,
                "The error should name the actual entry point that was misused (Run), not an " +
                "internal pooling class the caller has never heard of.");
        }

        /// <summary>Same as the Run() test above, for the async direct-execution entry point.</summary>
        [Test]
        public void RunAsync_CalledFromBackgroundThread_ThrowsInvalidOperationException()
        {
            var backgroundTask = Task.Run(async () => await _processor.RunAsync<TestAsyncCommand>());

            var aggregate = Assert.Throws<AggregateException>(() => backgroundTask.Wait());
            Assert.IsInstanceOf<InvalidOperationException>(aggregate.InnerException,
                "Direct async command execution (RunAsync) must validate main-thread use itself - " +
                "the check runs synchronously before the first await, so it still faults the " +
                "returned Task even though a throw inside an async method can't propagate to the " +
                "caller directly.");
        }

        /// <summary>Same guard, for the third public pool-creation entry point (not reached via Run/RunAsync).</summary>
        [Test]
        public void CreatePool_CalledFromBackgroundThread_ThrowsInvalidOperationException()
        {
            var backgroundTask = Task.Run(() => _processor.CreatePool<TestCommand>(4));

            var aggregate = Assert.Throws<AggregateException>(() => backgroundTask.Wait());
            Assert.IsInstanceOf<InvalidOperationException>(aggregate.InnerException,
                "CreatePool must validate main-thread use before configuring a command pool.");
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

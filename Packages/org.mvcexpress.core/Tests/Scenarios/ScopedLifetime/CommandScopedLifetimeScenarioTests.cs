using System;
using System.Threading.Tasks;
using mvcExpress.Internal.Commands;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests.Scenarios
{
    /// <summary>
    /// Scoped lifetime redesign (design contract: InternalSpecs/Architecture/SCOPED_LIFETIME_REDESIGN.md)
    /// - Command lifecycle coverage.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public class CommandScopedLifetimeScenarioTests
    {
        // Bare module: Awake()/OnDestroy() run for real (not stubbed to no-ops) so the one
        // genuine module-teardown test below can observe real CancelToken cancellation. The
        // module's own auto-created internal container/bus/processor are simply left unused -
        // every test drives command execution through the separately constructed
        // _container/_bus/_processor below instead, matching the convention established in
        // AsyncCommandOverlapScenarioTests (see that file's NoInitModule comment for why).
        private sealed class NoInitModule : MvcModule
        {
        }

        private MvcDiContainer _container;
        private MvcMessageBus _bus;
        private MvcCommandProcessor _processor;
        private GameObject _moduleGo;
        private NoInitModule _module;

        // ==================== Shared test doubles ====================

        // Plain, non-scoped dependency used to prove a scoped instance's own [Inject] field is
        // resolved recursively (design doc: "the scoped instance's own [Inject] fields must be
        // resolved too").
        private class SimpleService
        {
        }

        // The scoped dependency used by most tests below. Implements both IDisposable and
        // IMvcLifecycle so one type can stand in for every "actor dies" assertion in the design
        // doc's disposal-trigger map, plus the OnInitialized()/OnCleanup() lifecycle hooks it
        // requires for scoped instances.
        private class ScopedDependency : IDisposable, IMvcLifecycle
        {
            [Inject] public SimpleService InjectedService;

            public static int CreatedCount;
            public static int DisposeCount;
            public static int OnInitializedCount;
            public static int OnCleanupCount;

            public ScopedDependency() => CreatedCount++;

            public void OnInitialized() => OnInitializedCount++;
            public void OnCleanup() => OnCleanupCount++;
            public void Dispose() => DisposeCount++;

            public static void Reset()
            {
                CreatedCount = 0;
                DisposeCount = 0;
                OnInitializedCount = 0;
                OnCleanupCount = 0;
            }
        }

        // ---- (a) unpooled sync command ----

        private class UnpooledScopedCommand : Command
        {
            [Inject] private ScopedDependency _dep;

            public static int DisposeCountDuringExecute = -1;

            public override void Execute()
            {
                DisposeCountDuringExecute = ScopedDependency.DisposeCount;
            }

            public static void Reset() => DisposeCountDuringExecute = -1;
        }

        // ---- (b) pooled sync command ----

        private class PooledScopedCommand : Command
        {
            [Inject] private ScopedDependency _dep;

            public static int CreatedCount;
            public static int DisposeCountDuringLastExecute = -1;
            public static ScopedDependency LastInjectedDep;

            public PooledScopedCommand() => CreatedCount++;

            public override void Execute()
            {
                DisposeCountDuringLastExecute = ScopedDependency.DisposeCount;
                LastInjectedDep = _dep;
            }

            public static void Reset()
            {
                CreatedCount = 0;
                DisposeCountDuringLastExecute = -1;
                LastInjectedDep = null;
            }
        }

        // Deliberately NOT IDisposable - proves scoped-dependency release does not depend on the
        // concrete command implementing IDisposable. Today, MvcCommandProcessor.DisposeDiscardedCommand
        // and BoundedObjectPool<T>.DisposeIfNeeded both gate ANY teardown call on `command is
        // IDisposable`, so a non-IDisposable command gets no teardown call at discard time at all.
        private class NonDisposableScopedCommand : Command
        {
            [Inject] private ScopedDependency _dep;

            public override void Execute()
            {
            }
        }

        private class UnpooledScopedDependencyOwnFieldCommand : Command
        {
            [Inject] private ScopedDependency _dep;

            public static bool InjectedServiceWasResolved;

            public override void Execute()
            {
                InjectedServiceWasResolved = _dep != null && _dep.InjectedService != null;
            }

            public static void Reset() => InjectedServiceWasResolved = false;
        }

        // ---- interface-alias registration ----

        private interface IPingable
        {
            string Ping();
        }

        private class Pingable : IPingable
        {
            public string Ping() => "pong";
        }

        private class InterfaceAliasScopedCommand : Command
        {
            [Inject] private IPingable _pingable;

            public static string LastPingResult;

            public override void Execute()
            {
                LastPingResult = _pingable?.Ping();
            }

            public static void Reset() => LastPingResult = null;
        }

        // ---- (d) nested / reentrant execution ----

        private class OuterNestedScopedCommand : Command
        {
            [Inject] private ScopedDependency _dep;

            public static ScopedDependency Captured;
            public static int DisposeCountAtUse = -1;

            public override void Execute()
            {
                DisposeCountAtUse = ScopedDependency.DisposeCount;
                Captured = _dep;
                Commander.Run<InnerNestedScopedCommand>();
            }

            public static void Reset()
            {
                Captured = null;
                DisposeCountAtUse = -1;
            }
        }

        private class InnerNestedScopedCommand : Command
        {
            [Inject] private ScopedDependency _dep;

            public static ScopedDependency Captured;
            public static int DisposeCountAtUse = -1;

            public override void Execute()
            {
                DisposeCountAtUse = ScopedDependency.DisposeCount;
                Captured = _dep;
            }

            public static void Reset()
            {
                Captured = null;
                DisposeCountAtUse = -1;
            }
        }

        private class SimplePooledInnerCommand : Command
        {
            public static int ExecuteCount;

            public override void Execute() => ExecuteCount++;

            public static void Reset() => ExecuteCount = 0;
        }

        private class PooledOuterWithNestedInnerCommand : Command
        {
            [Inject] private ScopedDependency _dep;

            public static int CreatedCount;
            public static int DisposeCountAtLastUse = -1;

            public PooledOuterWithNestedInnerCommand() => CreatedCount++;

            public override void Execute()
            {
                DisposeCountAtLastUse = ScopedDependency.DisposeCount;
                Commander.Run<SimplePooledInnerCommand>();
            }

            public static void Reset()
            {
                CreatedCount = 0;
                DisposeCountAtLastUse = -1;
            }
        }

        // ---- (c) pooled async command ----

        private class GatedAsyncScopedCommand : CommandAsync
        {
            [Inject] private ScopedDependency _dep;

            public static TaskCompletionSource<bool> Gate;
            public static int DisposeCountBeforeAwait = -1;
            public static int DisposeCountAfterAwait = -1;

            public override async Task ExecuteAsync()
            {
                DisposeCountBeforeAwait = ScopedDependency.DisposeCount;
                await Gate.Task;
                DisposeCountAfterAwait = ScopedDependency.DisposeCount;
            }

            public static void Reset()
            {
                DisposeCountBeforeAwait = -1;
                DisposeCountAfterAwait = -1;
            }
        }

        private class SimpleAsyncScopedCommand : CommandAsync
        {
            [Inject] private ScopedDependency _dep;

            public static int CreatedCount;
            public static int DisposeCountDuringLastExecute = -1;

            public SimpleAsyncScopedCommand() => CreatedCount++;

            public override async Task ExecuteAsync()
            {
                await Task.Yield();
                DisposeCountDuringLastExecute = ScopedDependency.DisposeCount;
            }

            public static void Reset()
            {
                CreatedCount = 0;
                DisposeCountDuringLastExecute = -1;
            }
        }

        private class PausableScopedAsyncCancelCommand : CommandAsync
        {
            [Inject] private ScopedDependency _dep;

            public static TaskCompletionSource<bool> Gate;
            public static int DisposeCountObservedAtResume = -1;

            public override async Task ExecuteAsync()
            {
                await Gate.Task;
                DisposeCountObservedAtResume = ScopedDependency.DisposeCount;
                CancelToken.ThrowIfCancellationRequested();
            }

            public static void Reset() => DisposeCountObservedAtResume = -1;
        }

        [SetUp]
        public void SetUp()
        {
            ScopedDependency.Reset();
            UnpooledScopedCommand.Reset();
            PooledScopedCommand.Reset();
            UnpooledScopedDependencyOwnFieldCommand.Reset();
            InterfaceAliasScopedCommand.Reset();
            OuterNestedScopedCommand.Reset();
            InnerNestedScopedCommand.Reset();
            SimplePooledInnerCommand.Reset();
            PooledOuterWithNestedInnerCommand.Reset();
            GatedAsyncScopedCommand.Reset();
            SimpleAsyncScopedCommand.Reset();
            PausableScopedAsyncCancelCommand.Reset();

            _moduleGo = new GameObject(nameof(CommandScopedLifetimeScenarioTests));
            _module = _moduleGo.AddComponent<NoInitModule>();
            _container = new MvcDiContainer();
            _bus = new MvcMessageBus();
            _processor = new MvcCommandProcessor(typeof(NoInitModule), _container, _bus, _module);

            // ScopedDependency (the shared scoped fixture used by almost every test below) has a
            // required [Inject] SimpleService field, proving construction resolves a scoped
            // instance's own fields recursively - register it globally here rather than per-test,
            // since the redesign's real recursive injection now actually attempts to resolve it
            // (previously, the v1 bare-Activator.CreateInstance path never injected the scoped
            // instance's own fields at all, so a missing registration silently never surfaced).
            _container.Register(new SimpleService()).ToLogic().AsPermanent();
        }

        [TearDown]
        public void TearDown()
        {
            _processor?.Dispose();
            _bus?.Dispose();
            _container?.Dispose();

            if (_moduleGo != null)
            {
                Object.DestroyImmediate(_moduleGo);
                _moduleGo = null;
                _module = null;
            }

            var facade = MvcFacade.InstanceOrNull;
            if (facade != null)
            {
                Object.DestroyImmediate(facade.gameObject);
            }
        }

        // ==================== (a) Unpooled sync command ====================

        [Test]
        public void UnpooledCommand_ScopedDependency_NotDisposedDuringExecute()
        {
            _container.Register(new ScopedDependency()).ToLogic().AsScoped();

            _processor.Run<UnpooledScopedCommand>();

            Assert.That(UnpooledScopedCommand.DisposeCountDuringExecute, Is.EqualTo(0),
                "A command's scoped dependency must still be alive while Execute() runs - today " +
                "the v1 resolution scope (CommandBase.cs ~L174-179) closes and disposes every " +
                "scoped IDisposable right after field injection, before Execute() ever gets a " +
                "chance to use it.");
        }

        [Test]
        public void UnpooledCommand_ScopedDependency_DisposedOnceAfterExecution()
        {
            _container.Register(new ScopedDependency()).ToLogic().AsScoped();

            _processor.Run<UnpooledScopedCommand>();

            Assert.That(ScopedDependency.DisposeCount, Is.EqualTo(1),
                "With poolSize 0, the command instance itself 'dies' immediately after this one " +
                "execution, so its dedicated scoped dependency must be released exactly once by " +
                "the time Run() returns.");
        }

        [Test]
        public void UnpooledCommand_ScopedDependencyLifecycle_OnCleanupCalledOnceAfterDiscard()
        {
            _container.Register(new ScopedDependency()).ToLogic().AsScoped();

            _processor.Run<UnpooledScopedCommand>();

            Assert.That(ScopedDependency.OnCleanupCount, Is.EqualTo(1),
                "OnCleanup() must be called exactly once when the owning command instance is " +
                "discarded (poolSize 0 means that happens immediately after this one execution) " +
                "- today it is never called at all (bare Activator.CreateInstance skips " +
                "IMvcLifecycle entirely).");
        }

        [Test]
        public void PooledCommand_ScopedDependencyOwnInjectField_ResolvedRecursively()
        {
            // SimpleService is now registered globally in [SetUp].
            _container.Register(new ScopedDependency()).ToLogic().AsScoped();

            _processor.Run<UnpooledScopedDependencyOwnFieldCommand>();

            Assert.That(UnpooledScopedDependencyOwnFieldCommand.InjectedServiceWasResolved, Is.True,
                "The scoped instance's own [Inject] field must be resolved recursively when the " +
                "scoped instance is constructed - today's bare Activator.CreateInstance(type) " +
                "never runs member injection on the instance it creates.");
        }

        // ==================== (b) Pooled sync command ====================

        [Test]
        public void PooledCommand_ScopedDependency_PersistsAcrossReuse()
        {
            _container.Register(new ScopedDependency()).ToLogic().AsScoped();
            // .Register(new ScopedDependency()) above itself constructs one throwaway seed
            // instance (needed to read its concrete type for the .AsScoped() chain - it is never
            // stored or resolved). Reset the counter here so the assertion below measures only
            // constructions performed by the container's own scoped resolution, not that seed.
            ScopedDependency.Reset();
            var pool = _processor.GetOrCreatePool<PooledScopedCommand>(1);

            _processor.Run<PooledScopedCommand>();
            _processor.Run<PooledScopedCommand>();

            Assert.That(PooledScopedCommand.DisposeCountDuringLastExecute, Is.EqualTo(0),
                "The scoped dependency must still be alive on the SECOND pooled execution too - " +
                "today any command that resolves a scoped dependency is discarded after one run " +
                "(see MvcCommandProcessor_UnitTests.Run_WithScopedDependency_DoesNotReturnCommandToPool), " +
                "so this second execution actually runs on a freshly re-created, already-disposed " +
                "instance instead of a persisted one.");
            Assert.That(PooledScopedCommand.CreatedCount, Is.EqualTo(1),
                "A pooled command with a scoped dependency must now be reused normally (the v1 " +
                "'_scopedDependencyResolved forces discard' mechanism is removed entirely under " +
                "v2) - not re-created on every execution.");
            Assert.That(ScopedDependency.CreatedCount, Is.EqualTo(1),
                "The scoped dependency itself must be constructed once and persist across every " +
                "execution of the same pooled command instance, not reconstructed per execution.");
            Assert.That(pool.GetStatistics().CurrentSize, Is.EqualTo(1),
                "The command instance must actually be sitting back in the pool after both " +
                "executions, not silently discarded.");
        }

        [Test]
        public void PooledCommand_ScopedDependency_SameInstanceInjectedOnEachExecute()
        {
            _container.Register(new ScopedDependency()).ToLogic().AsScoped();
            _processor.CreatePool<PooledScopedCommand>(1);

            _processor.Run<PooledScopedCommand>();
            var firstDep = PooledScopedCommand.LastInjectedDep;

            _processor.Run<PooledScopedCommand>();
            var secondDep = PooledScopedCommand.LastInjectedDep;

            Assert.That(firstDep, Is.Not.Null, "Precondition: the first execution must resolve a dependency.");
            Assert.That(secondDep, Is.Not.Null, "Precondition: the second execution must resolve a dependency.");
            Assert.That(secondDep, Is.SameAs(firstDep),
                "Per-injectee ownership means the SAME dedicated scoped instance must be injected " +
                "into this command on every execution of the same command instance, never a new " +
                "one each time.");
        }

        [Test]
        public void PooledCommand_ScopedDependencyLifecycle_OnInitializedCalledOnce()
        {
            _container.Register(new ScopedDependency()).ToLogic().AsScoped();
            _processor.CreatePool<PooledScopedCommand>(1);

            _processor.Run<PooledScopedCommand>();
            _processor.Run<PooledScopedCommand>();

            Assert.That(ScopedDependency.OnInitializedCount, Is.EqualTo(1),
                "OnInitialized() must be called exactly once for the scoped instance's whole " +
                "lifetime, no matter how many times the owning pooled command is reused - today " +
                "it is never called at all (bare Activator.CreateInstance skips IMvcLifecycle " +
                "entirely).");
        }

        [Test]
        public void PooledCommand_NonDisposableScopedDependency_ReleasedOnPoolOverflowDiscard()
        {
            _container.Register(new ScopedDependency()).ToLogic().AsScoped();
            var pool = _processor.GetOrCreatePool<NonDisposableScopedCommand>(1);

            // NOTE: today's early-dispose bug (see UnpooledCommand_ScopedDependency_NotDisposedDuringExecute)
            // means both instances' scoped dependencies are already disposed well before this
            // test ever reaches the pool-overflow logic below - so this test is expected to go
            // red for that reason first, not only for the overflow-discard reason it targets.
            var first = (NonDisposableScopedCommand)pool.Get();
            first.Initialize(_module, _container, _bus, _processor);
            first.Execute();

            var second = (NonDisposableScopedCommand)pool.Get();
            second.Initialize(_module, _container, _bus, _processor);
            second.Execute();

            // Pool capacity is 1: returning `first` fills it, returning `second` overflows it and
            // routes it through BoundedObjectPool<T>.DisposeIfNeeded - the exact discard path the
            // design doc calls out as needing a release hook independent of IDisposable.
            pool.Return(first);
            pool.Return(second);

            Assert.That(ScopedDependency.DisposeCount, Is.EqualTo(1),
                "The overflow-discarded instance's dedicated scoped dependency must be released " +
                "even though NonDisposableScopedCommand itself does not implement IDisposable - " +
                "today BoundedObjectPool<T>.DisposeIfNeeded only calls Dispose() when the " +
                "discarded object itself is IDisposable, so a non-IDisposable command gets no " +
                "teardown call at all.");
        }

        [Test]
        public void PooledCommand_NonDisposableScopedDependency_ReleasedOnProcessorDispose()
        {
            _container.Register(new ScopedDependency()).ToLogic().AsScoped();
            _processor.CreatePool<NonDisposableScopedCommand>(1);

            _processor.Run<NonDisposableScopedCommand>();

            Assert.That(ScopedDependency.DisposeCount, Is.EqualTo(0),
                "Precondition: the command instance is still pooled and reusable after one " +
                "execution, so its dedicated scoped dependency must not be released yet.");

            _processor.Dispose();

            Assert.That(ScopedDependency.DisposeCount, Is.EqualTo(1),
                "Module teardown (MvcCommandProcessor.Dispose() clearing its pools) must release " +
                "every pooled command's dedicated scoped instances, even though the command " +
                "itself does not implement IDisposable.");

            _processor = null; // TearDown must not double-dispose.
        }

        // ==================== Interface-alias registration ====================

        [Test]
        public void InterfaceAliasScopedRegistration_CommandInjectsWorkingConcreteInstance()
        {
            _container.Register(new Pingable()).ToLogicAs<IPingable>().AsScoped();

            // Today, resolving an interface-aliased scoped registration crashes: construction
            // does Activator.CreateInstance(typeof(IPingable)) (an interface can't be
            // constructed). The command dispatch's own catch swallows and logs that as a normal
            // command failure - suppress the log-failure expectation itself (rather than pinning
            // its exact text) so this test's pass/fail outcome tracks only the assertion below,
            // in both today's broken state (crash, logged) and the fixed v2 state (no crash, no
            // log at all).
            var previousIgnore = LogAssert.ignoreFailingMessages;
            LogAssert.ignoreFailingMessages = true;
            try
            {
                _processor.Run<InterfaceAliasScopedCommand>();

                Assert.That(InterfaceAliasScopedCommand.LastPingResult, Is.EqualTo("pong"),
                    "Construction must use the registration's concrete ImplementationType " +
                    "(Pingable), never the requested interface type, so the command resolves a " +
                    "working instance instead of crashing.");
            }
            finally
            {
                LogAssert.ignoreFailingMessages = previousIgnore;
            }
        }

        // ==================== (d) Nested / reentrant execution ====================

        [Test]
        public void NestedCommandExecution_OuterInnerScopedDependency_ResolveToDistinctInstances()
        {
            _container.Register(new ScopedDependency()).ToLogic().AsScoped();

            _processor.Run<OuterNestedScopedCommand>();

            Assert.That(OuterNestedScopedCommand.DisposeCountAtUse, Is.EqualTo(0),
                "The outer command's own dedicated scoped instance must still be alive when its " +
                "Execute() runs, even though a nested command runs synchronously in the middle " +
                "of it.");
            Assert.That(InnerNestedScopedCommand.DisposeCountAtUse, Is.EqualTo(0),
                "The inner (nested) command's own dedicated scoped instance must likewise still " +
                "be alive when its Execute() runs.");
            Assert.That(OuterNestedScopedCommand.Captured, Is.Not.Null, "Precondition: outer must resolve a dependency.");
            Assert.That(InnerNestedScopedCommand.Captured, Is.Not.Null, "Precondition: inner must resolve a dependency.");
            Assert.That(InnerNestedScopedCommand.Captured, Is.Not.SameAs(OuterNestedScopedCommand.Captured),
                "Ownership is per-injectee: the outer and inner command each resolving the same " +
                "registered scoped type must get their own dedicated instance, never share one.");
        }

        [Test]
        public void NestedCommandExecution_PooledOuterCommandScopedDependency_PersistsDespiteInnerRun()
        {
            _container.Register(new ScopedDependency()).ToLogic().AsScoped();
            _processor.CreatePool<PooledOuterWithNestedInnerCommand>(1);
            _processor.CreatePool<SimplePooledInnerCommand>(1);

            _processor.Run<PooledOuterWithNestedInnerCommand>();
            _processor.Run<PooledOuterWithNestedInnerCommand>();

            Assert.That(PooledOuterWithNestedInnerCommand.DisposeCountAtLastUse, Is.EqualTo(0),
                "The outer command's scoped dependency must still be alive on its second pooled " +
                "execution, even with a nested inner command run interleaved on the same thread " +
                "- a reentrant nested Run() must not corrupt whether the outer's own scoped " +
                "dependency is still considered alive.");
            Assert.That(PooledOuterWithNestedInnerCommand.CreatedCount, Is.EqualTo(1),
                "The outer command must be pooled/reused across both executions despite the " +
                "nested inner run in between, not silently re-created.");
            Assert.That(SimplePooledInnerCommand.ExecuteCount, Is.EqualTo(2),
                "Sanity check: the nested inner command must actually run once per outer execution.");
        }

        // ==================== (c) Pooled async command ====================

        [Test]
        public async Task AsyncCommand_ScopedDependency_NotDisposedAcrossAwait()
        {
            _container.Register(new ScopedDependency()).ToLogic().AsScoped();
            GatedAsyncScopedCommand.Gate = new TaskCompletionSource<bool>();

            var runTask = _processor.RunAsync<GatedAsyncScopedCommand>();

            Assert.That(GatedAsyncScopedCommand.DisposeCountBeforeAwait, Is.EqualTo(0),
                "The scoped dependency must be alive before the command's first await point.");

            GatedAsyncScopedCommand.Gate.SetResult(true);
            await runTask;

            Assert.That(GatedAsyncScopedCommand.DisposeCountAfterAwait, Is.EqualTo(0),
                "The scoped dependency must still be alive after resuming from an await inside " +
                "ExecuteAsync() - it must not be disposed mid-flight, only once the command is " +
                "actually discarded afterward.");
        }

        [Test]
        public async Task PooledAsyncCommand_ScopedDependency_PersistsAcrossReuse()
        {
            _container.Register(new ScopedDependency()).ToLogic().AsScoped();
            // See the matching comment in PooledCommand_ScopedDependency_PersistsAcrossReuse:
            // the .Register() call above already constructed one throwaway seed instance.
            ScopedDependency.Reset();
            _processor.CreatePool<SimpleAsyncScopedCommand>(1);

            await _processor.RunAsync<SimpleAsyncScopedCommand>();
            await _processor.RunAsync<SimpleAsyncScopedCommand>();

            Assert.That(SimpleAsyncScopedCommand.DisposeCountDuringLastExecute, Is.EqualTo(0),
                "The scoped dependency must still be alive during the second pooled async " +
                "execution.");
            Assert.That(SimpleAsyncScopedCommand.CreatedCount, Is.EqualTo(1),
                "A pooled async command with a scoped dependency must be reused normally across " +
                "executions, matching the sync-command behavior change under v2.");
            Assert.That(ScopedDependency.CreatedCount, Is.EqualTo(1),
                "The scoped dependency must be constructed once and persist across pooled reuse " +
                "of the same async command instance.");
        }

        [Test]
        public void AsyncCommand_ModuleTornDownMidAwait_ScopedDependencyReleasedExactlyOnce()
        {
            _container.Register(new ScopedDependency()).ToLogic().AsScoped();
            PausableScopedAsyncCancelCommand.Gate = new TaskCompletionSource<bool>();

            var runTask = _processor.RunAsync<PausableScopedAsyncCancelCommand>();

            // Destroy the module while the command is suspended mid-await. NoInitModule here
            // does not stub out Awake()/OnDestroy(), so this genuinely cancels the module's real
            // _lifecycleCts - matching CommandAsyncCancellationDispatchTests' pattern for the
            // same reason.
            Object.DestroyImmediate(_moduleGo);
            _moduleGo = null; // TearDown must not double-destroy.

            PausableScopedAsyncCancelCommand.Gate.SetResult(true);

            Assert.DoesNotThrow(() => runTask.GetAwaiter().GetResult(),
                "Module-teardown cancellation must be swallowed quietly by the dispatch, the " +
                "same as any other command whose own token was genuinely cancelled by its " +
                "module dying - no exception should escape to the caller.");

            Assert.That(PausableScopedAsyncCancelCommand.DisposeCountObservedAtResume, Is.EqualTo(0),
                "The scoped dependency must still be alive when the command resumes from its " +
                "await, even though the module was destroyed while it was suspended.");
            Assert.That(ScopedDependency.DisposeCount, Is.EqualTo(1),
                "By the time the command's enclosing finally has run (module torn down, command " +
                "discarded), its dedicated scoped dependency must be released exactly once - " +
                "deterministically, with no leak and no double-dispose.");
        }
    }
}

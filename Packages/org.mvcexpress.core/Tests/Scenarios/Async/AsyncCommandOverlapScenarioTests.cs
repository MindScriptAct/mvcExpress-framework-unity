using System.Collections;
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
    /// Async command overlap scenario. Existing teardown tests (see
    /// CommandAsyncModuleTeardownTests / CommandAsyncCancellationDispatchTests) only cover a
    /// single in-flight async command instance. Real usage can easily have several overlapping
    /// in-flight executions against a pool sized well below the concurrency actually observed;
    /// this scenario proves pool overflow creates an extra instance (rather than blocking or
    /// reusing an instance that is still in flight), and that the third in-flight command's
    /// continuation genuinely observes the module's cancellation token (via <c>_lifecycleCts</c>)
    /// once the owning module has been torn down, rather than merely resuming on an unrelated
    /// gate.
    /// </summary>
    [TestFixture]
    [Category("Scenario")]
    public class AsyncCommandOverlapScenarioTests
    {
        private static readonly TaskCompletionSource<bool>[] Gates =
        {
            new TaskCompletionSource<bool>(),
            new TaskCompletionSource<bool>(),
            new TaskCompletionSource<bool>(),
        };

        private static int _nextGateIndex;
        private static int _distinctInstanceCount;

        // Set (from within ExecuteAsync, right after resuming from the gate) to whether this
        // instance's own CancelToken was already cancelled at that point - mirrors the
        // established idiom in CommandAsyncModuleTeardownTests.PausableCommand, which likewise
        // observes CancelToken.IsCancellationRequested immediately after awaiting a gate instead
        // of racing a synthetic delay against it.
        private static bool?[] _observedCancellationAtResume;

        private sealed class OverlappingAsyncCommand : CommandAsync
        {
            public override async Task ExecuteAsync()
            {
                var gateIndex = System.Threading.Interlocked.Increment(ref _nextGateIndex) - 1;
                System.Threading.Interlocked.Increment(ref _distinctInstanceCount);
                await Gates[gateIndex].Task;
                _observedCancellationAtResume[gateIndex] = CancelToken.IsCancellationRequested;
            }
        }

        private readonly struct OverlapMessage : IMessage { }

        private MvcDiContainer _container;
        private MvcMessageBus _bus;
        private MvcCommandProcessor _processor;
        private GameObject _moduleGo;

        // Awake() is left to run for real here (unlike a fully-stubbed module) specifically so
        // that EnsureCoreServicesInitialized() actually constructs the module's real
        // _lifecycleCts - without it, module.CancelToken would be permanently
        // CancellationToken.None and this test's teardown-cancellation assertion could never
        // fail even if the module's cancellation wiring were completely broken. The module's own
        // _commandProcessor/_diContainer/_messageBus that Awake() creates are simply left unused;
        // this test still drives command binding/dispatch through its own separately-constructed
        // _container/_bus/_processor below (matching the OverlappingAsyncCommand's captured
        // CancelToken, since that processor's _moduleContext is this same module instance).
        private sealed class NoInitModule : MvcModule { }

        [SetUp]
        public void SetUp()
        {
            _nextGateIndex = 0;
            _distinctInstanceCount = 0;
            _observedCancellationAtResume = new bool?[] { null, null, null };
            for (int i = 0; i < Gates.Length; i++)
            {
                Gates[i] = new TaskCompletionSource<bool>();
            }

            _moduleGo = new GameObject(nameof(AsyncCommandOverlapScenarioTests));
            var module = _moduleGo.AddComponent<NoInitModule>();
            _container = new MvcDiContainer();
            _bus = new MvcMessageBus();
            _processor = new MvcCommandProcessor(typeof(NoInitModule), _container, _bus, module);
        }

        [TearDown]
        public void TearDown()
        {
            _processor?.Dispose();
            _bus?.Dispose();
            _container?.Dispose();
            if (_moduleGo != null) Object.DestroyImmediate(_moduleGo);

            var facade = MvcFacade.InstanceOrNull;
            if (facade != null) Object.DestroyImmediate(facade.gameObject);
        }

        [UnityTest]
        public IEnumerator ThreeOverlappingPublishes_PoolSizeTwo_ThirdOverflowsToNewInstance_ThenTeardownCancelsRemainder()
        {
            // CreatePool runs before BindCommandAsync and eagerly constructs the pool with
            // maxSize=2. BindCommandAsync is then called with its default poolSize=0, which
            // (per its own `if (poolSize != 0)` guard) does NOT touch the already-created pool -
            // so the pool this test relies on for its "overflow" assertion really is capped at 2,
            // not silently left at the pool's zero-arg default of 0 (which would disable pooling
            // outright, per BoundedObjectPool's own doc comment on MaxSize == 0).
            _processor.CreatePool<OverlappingAsyncCommand>(poolSize: 2);
            _processor.BindCommandAsync<OverlappingAsyncCommand, OverlapMessage>();

#if UNITY_EDITOR || MVC_LOGGING
            var statsBeforeDispatch = _processor.GetPoolStatistics();
            Assert.That(statsBeforeDispatch.Count, Is.EqualTo(1),
                "Exactly one pool should be tracked for the single bound async command type.");
            Assert.That(statsBeforeDispatch[0].PoolStats.MaxSize, Is.EqualTo(2u),
                "CreatePool(poolSize: 2) must actually configure the pool's maxSize to 2 - " +
                "confirming this here rules out the pool silently staying at the disabled " +
                "default (maxSize=0) that a missing/ignored pool size would otherwise leave it at.");
#endif

            _bus.Publish<OverlapMessage>();
            _bus.Publish<OverlapMessage>();
            _bus.Publish<OverlapMessage>();

            yield return null;

            Assert.That(_distinctInstanceCount, Is.EqualTo(3),
                "Three overlapping in-flight publishes against a pool sized for 2 must create a third " +
                "instance on overflow rather than reusing one still in flight.");

            Gates[0].SetResult(true);
            Gates[1].SetResult(true);
            yield return null;
            yield return null;

#if UNITY_EDITOR || MVC_LOGGING
            var statsAfterTwoComplete = _processor.GetPoolStatistics();
            Assert.That(statsAfterTwoComplete[0].PoolStats.MaxSize, Is.EqualTo(2u),
                "Pool's maxSize must remain 2 after the first two overlapping executions return " +
                "their instances to the pool.");
            Assert.That(statsAfterTwoComplete[0].PoolStats.TotalCreated, Is.EqualTo(3),
                "Exactly three instances should have been created by the factory: two filling the " +
                "pool's capacity and one created on overflow for the third concurrent execution.");
#endif

            // Third completion left pending on purpose - destroy the module before it completes.
            // Module teardown (MvcModule.OnDestroy) cancels the module's real _lifecycleCts,
            // which is the same CancellationToken this instance captured as CancelToken at
            // Initialize() time (via the test's _processor, whose _moduleContext is this module).
            Object.DestroyImmediate(_moduleGo);
            _moduleGo = null;

            // Resume the third in-flight command now that its captured token has genuinely been
            // cancelled - same idiom as CommandAsyncModuleTeardownTests: complete the gate, then
            // check what CancelToken.IsCancellationRequested reads immediately on resume.
            Assert.DoesNotThrow(() => Gates[2].SetResult(true),
                "Completing the third in-flight command's awaited task after module teardown must not throw.");
            yield return null;
            yield return null;

            Assert.That(_observedCancellationAtResume[2], Is.True,
                "The third in-flight command's CancelToken (captured from the module's _lifecycleCts at " +
                "Initialize() time) must already read IsCancellationRequested == true by the time it " +
                "resumes after module teardown - if the module's cancellation-token wiring were broken " +
                "(token never cancelled, or a different token than the one this instance captured), this " +
                "would read false and the assertion would fail.");
        }
    }
}

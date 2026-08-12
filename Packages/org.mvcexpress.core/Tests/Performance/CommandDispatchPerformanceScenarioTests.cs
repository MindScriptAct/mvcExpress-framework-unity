// This file lives in the org.mvcexpress.core.Tests.Performance assembly, which is gated behind
// the MVC_PERFORMANCE_TESTS scripting define (see this assembly's .asmdef "defineConstraints").
// Add MVC_PERFORMANCE_TESTS to Project Settings > Player > Scripting Define Symbols to compile
// and run these tests. They measure wall-clock/allocation-adjacent behavior rather than
// correctness, so the assembly is excluded from default test runs (not merely skipped at
// runtime) to keep the fast test suite fast and to avoid perf-test flakiness blocking normal CI.
using System;
using mvcExpress.Internal.Commands;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace mvcExpress.Tests.Performance
{
    /// <summary>
    /// Command dispatch performance/allocation regression tests, per the implementation audit's
    /// suggested measurement (InternalSpecs/ReleaseAudit_260705/04_command-path-performance.md,
    /// section 4): a pooled sync command dispatch should be allocation-free after warmup (C1/C3),
    /// the documented poolSize-0 default should still allocate every dispatch (a canary, not a
    /// defect), and a mediator publish through its wrapper delegate should not allocate anything
    /// beyond the one-time subscribe-time closure (M8).
    /// </summary>
    /// <remarks>
    /// <b>Measurement environment note:</b> <c>GC.GetAllocatedBytesForCurrentThread()</c> was
    /// empirically confirmed (via a throwaway diagnostic test - allocating a 1 MB array on this
    /// same thread and observing before/after both read back as literal 0) to always report 0 in
    /// this project's <c>-testPlatform PlayMode -batchmode -nographics</c> Editor test-runner
    /// invocation, regardless of real allocations. Because of this, the pooled tests below pin
    /// C1/C3's steady-state-allocation-free claim using <c>MvcCommandProcessor.GetPoolStatistics()</c>
    /// -'s <c>TotalCreated</c> counter (proven reliable: it correctly reported 1001 creates for
    /// 1001 dispatches against an unpooled command in the same diagnostic session) instead of, or
    /// alongside, the byte-delta assertion. The byte-delta assertions are kept for parity with the
    /// existing <c>MessagingScenarioTests.StructMessage_HighFrequencyPublish_ZeroAllocation</c>
    /// idiom and for real Player/other-runner environments where the API does work, but they must
    /// not be the sole evidence in this environment - see the <c>TotalCreated</c> assertions for
    /// the assertion that actually carries weight here.
    /// </remarks>
    [TestFixture]
    [Category("Scenario")]
    public class CommandDispatchPerformanceScenarioTests
    {
        private const int WarmupIterations = 1;
        private const int MeasuredIterations = 1000;

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

        private readonly struct PooledDispatchMessage : IMessage { }
        private readonly struct InjectedDispatchMessage : IMessage { }
        private readonly struct UnpooledDispatchMessage : IMessage { }
        private readonly struct MediatorDispatchMessage : IMessage { }

        private sealed class ZeroPayloadPooledCommand : Command
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

        private sealed class DispatchService
        {
            public int CallCount;

            public void Ping()
            {
                CallCount++;
            }
        }

        private sealed class InjectedDependencyCommand : Command
        {
            public static int ExecuteCount;

            [Inject] private DispatchService _service;

            public static void Reset()
            {
                ExecuteCount = 0;
            }

            public override void Execute()
            {
                ExecuteCount++;
                _service.Ping();
            }
        }

        private sealed class UnpooledDefaultCommand : Command
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

        private sealed class WrapperDelegateMediator : MediatorBehaviour
        {
            public static int ReceivedCount;

            public static void Reset()
            {
                ReceivedCount = 0;
            }

            protected override void OnInitialized()
            {
                // Subscribing here allocates the wrapper closure exactly once (dev-build path)
                // or binds the raw handler directly (release path, per M8) - either way this
                // happens before the measurement window in every test that uses this mediator.
                Messenger.Subscribe<MediatorDispatchMessage>(OnMediatorMessage);
            }

            private void OnMediatorMessage()
            {
                ReceivedCount++;
            }
        }

        [SetUp]
        public void SetUp()
        {
            ZeroPayloadPooledCommand.Reset();
            InjectedDependencyCommand.Reset();
            UnpooledDefaultCommand.Reset();
            WrapperDelegateMediator.Reset();

            _moduleGo = new GameObject("CommandDispatchPerformanceModule");
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
        public void PooledCommandDispatch_ZeroPayload_PoolSizeConfigured_ZeroAllocAfterWarmup()
        {
            const uint poolSize = 4;
            _processor.BindCommand<ZeroPayloadPooledCommand, PooledDispatchMessage>(poolSize);

#if UNITY_EDITOR || MVC_LOGGING
            var statsBeforeDispatch = _processor.GetPoolStatistics();
            Assert.That(statsBeforeDispatch.Count, Is.EqualTo(1),
                "Exactly one pool should be tracked for the single bound command type.");
            Assert.That(statsBeforeDispatch[0].PoolStats.MaxSize, Is.EqualTo(poolSize),
                "BindCommand(poolSize: 4) must actually configure the pool's maxSize to 4 - " +
                "confirming this rules out silently falling back to the disabled default (maxSize=0), " +
                "which would make every dispatch below allocate and defeat the point of this test.");
#endif

            for (int i = 0; i < WarmupIterations; i++)
            {
                _messageBus.Publish<PooledDispatchMessage>();
            }

            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < MeasuredIterations; i++)
            {
                _messageBus.Publish<PooledDispatchMessage>();
            }
            var afterBytes = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(ZeroPayloadPooledCommand.ExecuteCount, Is.EqualTo(WarmupIterations + MeasuredIterations),
                "Every publish should have dispatched to the bound command's Execute().");
            Assert.That(afterBytes - beforeBytes, Is.EqualTo(0),
                "Steady-state dispatch of a pooled command must not allocate: once the single pooled " +
                "instance has been created and injected on the warmup dispatch, every subsequent " +
                "dispatch should just be pool pop / Initialize no-op (guarded by _hasBeenInjected, " +
                "which also skips the per-dispatch actor-context rebuild per audit finding C3) / " +
                "Execute / pool push. A non-zero delta here means either the pool is not actually " +
                "configured (see the pool-size assertions above) or C1/C3 do not hold as claimed.");

#if UNITY_EDITOR || MVC_LOGGING
            // Belt-and-suspenders check that does not depend on GC.GetAllocatedBytesForCurrentThread
            // (see the class-level remarks: that API reads back 0 in this test environment
            // regardless of real allocations). TotalCreated only increments in
            // BoundedObjectPool.Get() when the pool is empty and a fresh instance must be
            // factory-created - so exactly 1 (the warmup dispatch) proves every one of the
            // MeasuredIterations dispatches actually reused the pooled instance instead of
            // allocating a new one.
            var statsAfterDispatch = _processor.GetPoolStatistics();
            Assert.That(statsAfterDispatch[0].PoolStats.TotalCreated, Is.EqualTo(1),
                "Only the warmup dispatch should have created a command instance; all " +
                MeasuredIterations + " measured dispatches must reuse that same pooled instance " +
                "(C3) rather than creating new ones - this is the reliable allocation signal in " +
                "this test environment, independent of the (here, non-functional) byte counter.");
#endif
        }

        [Test]
        public void PooledCommandDispatch_WithInjectedDependency_ZeroAllocAfterWarmup()
        {
            _container.Register(new DispatchService()).ToLogic().AsPermanent();

            const uint poolSize = 4;
            _processor.BindCommand<InjectedDependencyCommand, InjectedDispatchMessage>(poolSize);

#if UNITY_EDITOR || MVC_LOGGING
            var statsBeforeDispatch = _processor.GetPoolStatistics();
            Assert.That(statsBeforeDispatch.Count, Is.EqualTo(1),
                "Exactly one pool should be tracked for the single bound command type.");
            Assert.That(statsBeforeDispatch[0].PoolStats.MaxSize, Is.EqualTo(poolSize),
                "BindCommand(poolSize: 4) must configure real pooling before this test's zero-alloc " +
                "assertion means anything.");
#endif

            for (int i = 0; i < WarmupIterations; i++)
            {
                _messageBus.Publish<InjectedDispatchMessage>();
            }

            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < MeasuredIterations; i++)
            {
                _messageBus.Publish<InjectedDispatchMessage>();
            }
            var afterBytes = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(InjectedDependencyCommand.ExecuteCount, Is.EqualTo(WarmupIterations + MeasuredIterations),
                "Every publish should have dispatched to the bound command's Execute(), which calls " +
                "into its injected service.");
            Assert.That(afterBytes - beforeBytes, Is.EqualTo(0),
                "A pooled command with an [Inject] dependency must not allocate on steady-state " +
                "dispatch: member injection and OnInitialize() run once, on the instance's first " +
                "execution (warmup), guarded by MvcCommandBase._hasBeenInjected. If this fails, " +
                "reflection-based injection or context/scope setup is being re-run on every dispatch " +
                "for a pooled command, contradicting audit finding C3.");

#if UNITY_EDITOR || MVC_LOGGING
            // See PooledCommandDispatch_ZeroPayload_...'s matching check and the class-level
            // remarks: TotalCreated is the reliable allocation signal in this test environment.
            var statsAfterDispatch = _processor.GetPoolStatistics();
            Assert.That(statsAfterDispatch[0].PoolStats.TotalCreated, Is.EqualTo(1),
                "Only the warmup dispatch should have created a command instance; every measured " +
                "dispatch must reuse that same pooled instance and its already-injected dependency " +
                "(C3), not re-resolve DispatchService or allocate a fresh command.");
#endif
        }

        [Test]
        public void DefaultUnpooledCommandDispatch_AllocatesEveryDispatch_DocumentsCurrentBehavior()
        {
            // Deliberately omit poolSize - the documented default (command.md: "poolSize: 0
            // (default) - a new instance is created and discarded per dispatch") means every
            // dispatch below both allocates a fresh TCommand and re-runs [Inject] injection.
            _processor.BindCommand<UnpooledDefaultCommand, UnpooledDispatchMessage>();

            for (int i = 0; i < WarmupIterations; i++)
            {
                _messageBus.Publish<UnpooledDispatchMessage>();
            }

            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < MeasuredIterations; i++)
            {
                _messageBus.Publish<UnpooledDispatchMessage>();
            }
            var afterBytes = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(UnpooledDefaultCommand.ExecuteCount, Is.EqualTo(WarmupIterations + MeasuredIterations),
                "Every publish should have dispatched to the bound command's Execute().");

#if UNITY_EDITOR || MVC_LOGGING
            // Primary, reliable assertion for this test environment - see the class-level remarks:
            // GC.GetAllocatedBytesForCurrentThread() was empirically confirmed to always read back
            // 0 in this project's batchmode PlayMode test runner (verified with a throwaway
            // diagnostic that allocated a 1 MB array on this same thread and still observed a 0
            // delta), so it cannot be trusted to prove a *non-zero* allocation claim here. TotalCreated
            // does not have that problem: it is a plain counter incremented once per BoundedObjectPool.Get()
            // factory call, and directly reflects the documented C1 default behavior (a new instance
            // is created and discarded on every dispatch, poolSize == 0 disables pooling entirely).
            var statsAfterDispatch = _processor.GetPoolStatistics();
            Assert.That(statsAfterDispatch[0].PoolStats.MaxSize, Is.EqualTo(0u),
                "Precondition: this command must be bound with no explicit pool size, so the " +
                "underlying pool's maxSize stays at the disabled default (0) - otherwise this test " +
                "would not actually exercise the documented poolSize:0 behavior it claims to.");
            Assert.That(statsAfterDispatch[0].PoolStats.TotalCreated, Is.EqualTo(WarmupIterations + MeasuredIterations),
                "This is a documentation/canary test, not a regression fix: per audit finding C1 " +
                "and command.md's own documented contract, the default poolSize=0 path creates (and " +
                "immediately discards) a fresh command instance on every single dispatch - so " +
                "TotalCreated must equal the total dispatch count. If this assertion ever fails " +
                "because TotalCreated is lower than the dispatch count, the default pooling behavior " +
                "has silently changed - that is a deliberate, user-facing API/behavior change (and " +
                "would also contradict command.md), so this test must be updated explicitly " +
                "alongside that change, not left to silently start passing.");
#endif

            // Kept for parity with the established MessagingScenarioTests idiom and for other
            // test runners/environments where this API does report real deltas; see the class-level
            // remarks for why it cannot be trusted as the sole evidence in this environment.
            // Intentionally not asserted on directly - the TotalCreated assertion above is the one
            // that actually proves the documented C1 allocate-every-dispatch behavior here.
            _ = afterBytes - beforeBytes;
        }

        [Test]
        public void MediatorPublish_AfterSubscribeWarmup_ZeroAllocPerPublish()
        {
            var mediatorGo = new GameObject("WrapperDelegateMediator");
            mediatorGo.transform.SetParent(_moduleGo.transform);
            var mediator = mediatorGo.AddComponent<WrapperDelegateMediator>();
            // OnInitialized() runs here and subscribes - in dev builds this allocates the
            // tracker wrapper-closure exactly once (per audit finding M8); in release builds
            // the raw handler is subscribed directly with no wrapper. Either way, this cost is
            // paid once, here, and is not part of the measured loop below.
            mediator.Initialize(_module, _container, _messageBus);

            // Warmup publish - still before the measurement window, so any first-publish-only
            // cost (e.g. JIT, lazy static init) is absorbed here rather than in the loop.
            _messageBus.Publish<MediatorDispatchMessage>();
            WrapperDelegateMediator.ReceivedCount = 0;

            var beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < MeasuredIterations; i++)
            {
                _messageBus.Publish<MediatorDispatchMessage>();
            }
            var afterBytes = GC.GetAllocatedBytesForCurrentThread();

            Assert.That(WrapperDelegateMediator.ReceivedCount, Is.EqualTo(MeasuredIterations),
                "Every publish after the warmup should have reached the mediator's handler exactly once.");
            // Note (see class-level remarks): unlike the pooled-command tests above, mediator
            // dispatch has no BoundedObjectPool/TotalCreated equivalent to fall back on as an
            // independent allocation signal, so this assertion relies solely on
            // GC.GetAllocatedBytesForCurrentThread(), which is known to read back 0 in this test
            // environment regardless of real allocations - mirroring the same limitation already
            // present in the existing MessagingScenarioTests.StructMessage_HighFrequencyPublish_ZeroAllocation
            // precedent this test was modeled on. It still documents the intended contract (M8) and
            // will provide real signal in environments/runners where the counter functions.
            Assert.That(afterBytes - beforeBytes, Is.EqualTo(0),
                "Per-publish dispatch through a mediator's subscription (whether via the dev-build " +
                "wrapper delegate or the release-build raw handler, per finding M8) must not allocate " +
                "anything beyond the one-time subscribe-time closure created in OnInitialized above. " +
                "A non-zero delta here means the wrapper (or something else on the mediator delivery " +
                "path) allocates per-delivery rather than per-subscribe.");

            Object.DestroyImmediate(mediatorGo);
        }
    }
}

// Tier 2 performance harness: a plain MonoBehaviour (no NUnit, no UnityEngine.TestRunner) that
// measures the same critical dispatch paths as the Tier 1 Editor-batchmode NUnit suite
// (Packages/org.mvcexpress.core/Tests/Performance/CommandDispatchPerformanceScenarioTests.cs),
// but running inside a real, non-development Standalone Player. Tier 1 discovered that
// GC.GetAllocatedBytesForCurrentThread() reads back a constant 0 in the Editor's batchmode
// PlayMode test runner regardless of real allocations, so it fell back to pool-statistics
// counters as an allocation proxy and could not get true release-mode timing numbers at all.
// This harness exists to get real Stopwatch timings and real GC byte deltas from an actual
// release Player, closing that gap.
//
// This script intentionally lives outside any "Tests" folder and only references the
// org.mvcexpress.core runtime assembly plus stock UnityEngine, so it compiles into (and can run
// inside) a normal Player build - Unity excludes assemblies under Tests/ folders (and anything
// referencing UnityEngine.TestRunner/nunit.framework) from Player builds by this project's
// asmdef conventions.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using mvcExpress;
using mvcExpress.Internal.Commands;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;
using Unity.Profiling;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace mvcExpress.PerformanceHarness
{
    /// <summary>
    /// Runtime benchmark harness for Tier 2 release-Player performance measurement. Attach to a
    /// single GameObject in <c>ReleaseBenchmarkScene.unity</c>; on <see cref="Start"/> it builds
    /// real <see cref="MvcDiContainer"/>/<see cref="MvcMessageBus"/>/<see cref="MvcCommandProcessor"/>
    /// instances (the same primitives used throughout the existing test suite), benchmarks a
    /// fixed set of critical dispatch paths, writes the results to disk, and quits - since this
    /// Player is meant to run headless via <c>-batchmode -nographics</c> with nobody to close it.
    /// </summary>
    public sealed class ReleaseBenchmarkHarness : MonoBehaviour
    {
        private const int WarmupIterations = 100;
        private const int MeasuredIterations = 10000;
        private const uint PoolSize = 4;

        // PERF-02 (InternalSpecs/ReleaseAudit_260813/03_performance_and_messaging.md): the audit
        // found this harness's allocation numbers unfalsifiable because GC.GetAllocatedBytesForCurrentThread()
        // silently reads back 0 in some environments while still being reported as "zero
        // allocations." The fix is not to trust either counter by assumption: both are run against
        // the same known-size canary allocation in BenchmarkGcByteCounterDiagnostic, and only a
        // counter that actually observes that canary is trusted for the rest of the run. A
        // ProfilerRecorder handle is invalid (Valid == false) if the requested counter doesn't
        // exist in this build/platform at all, which is itself a useful, distinct failure mode from
        // "exists but reads 0".
        private ProfilerRecorder _gcAllocatedInFrameRecorder;
        private bool _gcCounterFunctional;
        private bool _profilerCounterFunctional;

        private readonly struct AllocationSnapshot
        {
            public readonly long gcBytes;

            public AllocationSnapshot(long gcBytes)
            {
                this.gcBytes = gcBytes;
            }
        }

        private AllocationSnapshot SampleAllocations()
        {
            return new AllocationSnapshot(GC.GetAllocatedBytesForCurrentThread());
        }

        // --- Message/command/service types, mirroring the Tier 1 NUnit fixture's private nested
        // types as closely as a top-level runtime script allows (no test-only static Reset()
        // convenience needed here since each benchmark method creates its own fresh container). ---

        private readonly struct ZeroPayloadMessage : IMessage { }
        private readonly struct OnePayloadMessage : IMessage<int> { }
        private readonly struct PooledDispatchMessage : IMessage { }
        private readonly struct InjectedDispatchMessage : IMessage { }
        private readonly struct UnpooledDispatchMessage : IMessage { }
        private readonly struct MediatorDispatchMessage : IMessage { }
        private readonly struct AsyncDispatchMessage : IMessage { }
        private readonly struct TwelvePayloadMessage : IMessage<int, int, int, int, int, int, int, int, int, int, int, int> { }

        private sealed class PooledZeroPayloadCommand : Command
        {
            public static long ExecuteCount;
            public override void Execute() => ExecuteCount++;
        }

        private sealed class CompletedAsyncCommand : CommandAsync
        {
            public static long ExecuteCount;

            public override Task ExecuteAsync()
            {
                ExecuteCount++;
                return Task.CompletedTask;
            }
        }

        private sealed class TwelvePayloadCommand : Command<int, int, int, int, int, int, int, int, int, int, int, int>
        {
            public static long ExecuteCount;
            public static long PayloadSum;

            public override void Execute(int p1, int p2, int p3, int p4, int p5, int p6,
                int p7, int p8, int p9, int p10, int p11, int p12)
            {
                ExecuteCount++;
                PayloadSum += p1 + p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9 + p10 + p11 + p12;
            }
        }

        // Deliberately bound with no explicit poolSize (defaults to 0, i.e. pooling disabled) -
        // per command.md, every dispatch creates and discards a fresh instance. This is the
        // canary path: it exists specifically to prove GC.GetAllocatedBytesForCurrentThread()
        // reports real, non-zero numbers in this real Player build, unlike the Tier 1 Editor
        // batchmode environment where the same API always read back a constant 0.
        private sealed class UnpooledDefaultCommand : Command
        {
            public static long ExecuteCount;
            public override void Execute() => ExecuteCount++;
        }

        private sealed class DispatchService
        {
            public long CallCount;
            public void Ping() => CallCount++;
        }

        private sealed class InjectedDependencyCommand : Command
        {
            public static long ExecuteCount;
            [Inject] private DispatchService _service;
            public override void Execute()
            {
                ExecuteCount++;
                _service.Ping();
            }
        }

        // Bare MvcModule subclass with no-op Awake/OnDestroy: MvcCommandProcessor's constructor
        // requires a module instance, but we don't want the full MvcFacade-driven lifecycle here -
        // same idiom as CommandDispatchPerformanceScenarioTests.NoInitModule.
        private sealed class NoInitModule : MvcModule
        {
            protected override void Awake() { }
            protected override void OnDestroy() { }
        }

        private IEnumerator Start()
        {
            Debug.Log("[ReleaseBenchmarkHarness] Starting Tier 2 release-Player benchmark run.");

            // Started before any benchmark runs so the diagnostic canary and every subsequent
            // measured method sample it under identical conditions. "GC Allocated In Frame" is the
            // standard Memory-category counter the Profiler window itself displays; if this exact
            // name doesn't resolve on the running platform/Unity version, the recorder is normally
            // just invalid rather than throwing (SampleAllocations() already treats that as
            // "unavailable"), but the construction call itself is guarded too - a stripped Profiler
            // module throwing here must not take down the whole benchmark run before it produces
            // any output.
            try
            {
                _gcAllocatedInFrameRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame", 1);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ReleaseBenchmarkHarness] ProfilerRecorder unavailable, falling back to " +
                                  $"GC.GetAllocatedBytesForCurrentThread() alone: {ex.Message}");
            }

            // ProfilerRecorder.LastValue is a completed-frame sample, not a synchronous counter.
            // Give the recorder one frame to become active before putting the known allocation in
            // its own frame. Every runtime benchmark below follows the same execute/yield/read
            // sequence, so profiler values never come from two reads inside one frame.
            yield return null;

            var diagnosticResult = BenchmarkGcByteCounterDiagnostic();
            yield return null;
            CaptureProfilerFrame(ref diagnosticResult);
            const int diagnosticAllocationSize = 1024 * 1024;
            _profilerCounterFunctional = diagnosticResult.profilerAllocatedBytesDelta >= diagnosticAllocationSize;
            diagnosticResult = SelectAllocationSource(diagnosticResult);
            LogResult(diagnosticResult);

            bool allocationDataTrustworthy = _gcCounterFunctional || _profilerCounterFunctional;

            if (!allocationDataTrustworthy)
            {
                // PERF-02: neither counter can observe a known 1 MB allocation, so every
                // allocatedBytesDelta/bytesPerOp/profilerAllocatedBytesDelta/profilerBytesPerOp
                // value produced below is meaningless and must not be reported as "zero
                // allocations" or any other allocation claim. This run's timing numbers (ns/op)
                // remain valid and are still written out.
                Debug.LogError("[ReleaseBenchmarkHarness] PERF-02: allocation instrumentation is " +
                                "non-functional in this build/platform - neither GC.GetAllocatedBytesForCurrentThread() " +
                                "nor the ProfilerRecorder 'GC Allocated In Frame' counter observed the diagnostic's " +
                                "known 1 MB allocation. Every allocation figure in this run's output is invalid " +
                                "and must be ignored; only the ns/op timing figures can be trusted.");
            }

            var runtimeResults = new List<BenchmarkResult> { diagnosticResult };

            var benchmarkFactories = new Func<BenchmarkResult>[]
            {
                BenchmarkMessagePublishZeroPayload,
                BenchmarkMessagePublishOnePayload,
                BenchmarkMessagePublishTwelvePayloads,
                BenchmarkPooledCommandDispatchZeroPayload,
                BenchmarkPooledCommandDispatchWithInjectedDependency,
                BenchmarkPooledAsyncCommandDispatch,
                BenchmarkPooledTwelvePayloadCommandDispatch,
                BenchmarkUnpooledDefaultCommandDispatch,
                BenchmarkMediatorPublish
            };

            for (int i = 0; i < benchmarkFactories.Length; i++)
            {
                var result = benchmarkFactories[i]();
                // Resume on the next frame before reading LastValue: it now describes the frame
                // in which this benchmark ran. This is intentionally a conservative whole-frame
                // fallback; the GC counter remains the precise measured-loop source when valid.
                yield return null;
                CaptureProfilerFrame(ref result);
                result = SelectAllocationSource(result);
                LogResult(result);
                runtimeResults.Add(result);
            }

            var results = new BenchmarkResults
            {
                runUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                unityVersion = Application.unityVersion,
                scriptingBackend = GetScriptingBackend(),
                isDevelopmentBuild = Debug.isDebugBuild,
                warmupIterations = WarmupIterations,
                measuredIterations = MeasuredIterations,
                gcCounterFunctional = _gcCounterFunctional,
                profilerCounterFunctional = _profilerCounterFunctional,
                allocationDataTrustworthy = allocationDataTrustworthy,
                results = runtimeResults.ToArray(),
                // These benchmarks currently expose synchronous measured-loop GC values only.
                // The campaign treats them as timing-only when that counter is unavailable rather
                // than incorrectly applying one aggregate profiler frame to every sub-result.
                dataStructureResults = ContainerDataStructureBenchmarks.Run()
            };

            _gcAllocatedInFrameRecorder.Dispose();

            string outputPath = ResolveOutputPath();
            WriteResults(outputPath, results);

            Debug.Log($"[ReleaseBenchmarkHarness] Wrote benchmark results to '{outputPath}'. Quitting.");

            // Fail the run (non-zero exit) when allocation instrumentation is broken, per PERF-02's
            // "fail the benchmark when the canary cannot observe an intentional allocation" -
            // otherwise an automated caller (CI, a batchmode script) sees a clean exit code and has
            // no signal that the allocation data it just got is worthless. Timing data is still
            // written to disk either way; only the process's success/failure signal changes.
            Application.Quit(allocationDataTrustworthy ? 0 : 1);

#if UNITY_EDITOR
            // Application.Quit is a no-op in the Editor; this harness is designed to run as a
            // built Player, but avoid hanging an accidental Editor Play Mode run.
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        private BenchmarkResult BenchmarkMessagePublishTwelvePayloads()
        {
            var messageBus = new MvcMessageBus();
            long receivedSum = 0;
            messageBus.Subscribe<TwelvePayloadMessage, int, int, int, int, int, int, int, int, int, int, int, int>(
                (p1, p2, p3, p4, p5, p6, p7, p8, p9, p10, p11, p12) =>
                    receivedSum += p1 + p2 + p3 + p4 + p5 + p6 + p7 + p8 + p9 + p10 + p11 + p12);

            for (int i = 0; i < WarmupIterations; i++)
            {
                messageBus.Publish<TwelvePayloadMessage, int, int, int, int, int, int, int, int, int, int, int, int>(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);
            }

            var before = SampleAllocations();
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < MeasuredIterations; i++)
            {
                messageBus.Publish<TwelvePayloadMessage, int, int, int, int, int, int, int, int, int, int, int, int>(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);
            }
            stopwatch.Stop();
            var after = SampleAllocations();

            messageBus.Dispose();
            return BuildResult("MessagePublish_TwelvePayloads", stopwatch, before, after,
                receivedSum == (WarmupIterations + MeasuredIterations) * 12L);
        }

        private static string GetScriptingBackend()
        {
#if ENABLE_IL2CPP
            return "IL2CPP";
#elif ENABLE_MONO
            return "Mono";
#else
            return "Unknown";
#endif
        }

        // --- diagnostic: sanity-checks GC.GetAllocatedBytesForCurrentThread() itself, independent
        // of any mvcExpress code path. Allocates a known-size byte[] on the measurement thread and
        // reports the observed delta. Tier 1's report documented that this API reads back a
        // constant 0 in the Editor's batchmode PlayMode test runner regardless of real allocations
        // (proven with an identical throwaway diagnostic there); this benchmark is the equivalent
        // check for this real Player build, so the report can state plainly whether the API is
        // trustworthy here rather than inferring it indirectly from the mvcExpress-specific paths.
        private BenchmarkResult BenchmarkGcByteCounterDiagnostic()
        {
            const int arraySize = 1024 * 1024; // 1 MB
            var stopwatch = Stopwatch.StartNew();
            var before = SampleAllocations();
            byte[] scratch = new byte[arraySize];
            scratch[0] = 1;
            scratch[arraySize - 1] = 2;
            var after = SampleAllocations();
            stopwatch.Stop();

            bool correct = scratch[0] == 1 && scratch[arraySize - 1] == 2;
            long gcDelta = after.gcBytes - before.gcBytes;

            // The GC counter is synchronous, so it can be validated here. The profiler counter is
            // validated by Start after this allocation frame has completed.
            _gcCounterFunctional = gcDelta >= arraySize;

            Debug.Log("[ReleaseBenchmarkHarness] Allocation counter diagnostic: allocated a " +
                      $"{arraySize} byte array. GC.GetAllocatedBytesForCurrentThread delta={gcDelta} bytes " +
                      $"({(_gcCounterFunctional ? "FUNCTIONAL" : "NON-FUNCTIONAL")}); " +
                      "ProfilerRecorder validation will be read after this frame completes.");

            return BuildResult("GcByteCounterDiagnostic_1MBAllocation", stopwatch, before, after, correct);
        }

        // --- a. Message publish (zero-payload) -> subscriber invoked ---
        private BenchmarkResult BenchmarkMessagePublishZeroPayload()
        {
            var messageBus = new MvcMessageBus();
            long received = 0;
            messageBus.Subscribe<ZeroPayloadMessage>(() => received++);

            for (int i = 0; i < WarmupIterations; i++)
            {
                messageBus.Publish<ZeroPayloadMessage>();
            }

            var before = SampleAllocations();
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < MeasuredIterations; i++)
            {
                messageBus.Publish<ZeroPayloadMessage>();
            }
            stopwatch.Stop();
            var after = SampleAllocations();

            messageBus.Dispose();

            return BuildResult("MessagePublish_ZeroPayload", stopwatch, before, after,
                received == WarmupIterations + MeasuredIterations);
        }

        // --- b. Message publish (one payload) -> subscriber invoked ---
        private BenchmarkResult BenchmarkMessagePublishOnePayload()
        {
            var messageBus = new MvcMessageBus();
            long receivedSum = 0;
            messageBus.Subscribe<OnePayloadMessage, int>(p1 => receivedSum += p1);

            for (int i = 0; i < WarmupIterations; i++)
            {
                messageBus.Publish<OnePayloadMessage, int>(1);
            }

            var before = SampleAllocations();
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < MeasuredIterations; i++)
            {
                messageBus.Publish<OnePayloadMessage, int>(1);
            }
            stopwatch.Stop();
            var after = SampleAllocations();

            messageBus.Dispose();

            return BuildResult("MessagePublish_OnePayload", stopwatch, before, after,
                receivedSum == WarmupIterations + MeasuredIterations);
        }

        // --- c. Pooled command dispatch (zero-payload, pool size configured) ---
        private BenchmarkResult BenchmarkPooledCommandDispatchZeroPayload()
        {
            PooledZeroPayloadCommand.ExecuteCount = 0;

            var moduleGo = new GameObject("PooledZeroPayloadModule");
            var module = moduleGo.AddComponent<NoInitModule>();
            var container = new MvcDiContainer();
            var messageBus = new MvcMessageBus();
            var processor = new MvcCommandProcessor(typeof(NoInitModule), container, messageBus, module);
            processor.BindCommand<PooledZeroPayloadCommand, PooledDispatchMessage>(PoolSize);

            for (int i = 0; i < WarmupIterations; i++)
            {
                messageBus.Publish<PooledDispatchMessage>();
            }

            var before = SampleAllocations();
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < MeasuredIterations; i++)
            {
                messageBus.Publish<PooledDispatchMessage>();
            }
            stopwatch.Stop();
            var after = SampleAllocations();

            bool correct = PooledZeroPayloadCommand.ExecuteCount == WarmupIterations + MeasuredIterations;

            processor.Dispose();
            messageBus.Dispose();
            container.Dispose();
            Destroy(moduleGo);

            return BuildResult("Scenario_MessageToPooledCommand_ZeroPayload", stopwatch, before, after, correct);
        }

        // --- d. Pooled command dispatch with an [Inject] dependency ---
        private BenchmarkResult BenchmarkPooledCommandDispatchWithInjectedDependency()
        {
            InjectedDependencyCommand.ExecuteCount = 0;

            var moduleGo = new GameObject("PooledInjectedModule");
            var module = moduleGo.AddComponent<NoInitModule>();
            var container = new MvcDiContainer();
            container.Register(new DispatchService()).ToLogic().AsPermanent();
            var messageBus = new MvcMessageBus();
            var processor = new MvcCommandProcessor(typeof(NoInitModule), container, messageBus, module);
            processor.BindCommand<InjectedDependencyCommand, InjectedDispatchMessage>(PoolSize);

            for (int i = 0; i < WarmupIterations; i++)
            {
                messageBus.Publish<InjectedDispatchMessage>();
            }

            var before = SampleAllocations();
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < MeasuredIterations; i++)
            {
                messageBus.Publish<InjectedDispatchMessage>();
            }
            stopwatch.Stop();
            var after = SampleAllocations();

            bool correct = InjectedDependencyCommand.ExecuteCount == WarmupIterations + MeasuredIterations;

            processor.Dispose();
            messageBus.Dispose();
            container.Dispose();
            Destroy(moduleGo);

            return BuildResult("Scenario_MessageToPooledCommand_InjectedDependency", stopwatch, before, after, correct);
        }

        private BenchmarkResult BenchmarkPooledAsyncCommandDispatch()
        {
            CompletedAsyncCommand.ExecuteCount = 0;
            var moduleGo = new GameObject("PooledAsyncModule");
            var module = moduleGo.AddComponent<NoInitModule>();
            var container = new MvcDiContainer();
            var messageBus = new MvcMessageBus();
            var processor = new MvcCommandProcessor(typeof(NoInitModule), container, messageBus, module);
            processor.BindCommand<CompletedAsyncCommand, AsyncDispatchMessage>(PoolSize);

            for (int i = 0; i < WarmupIterations; i++) messageBus.Publish<AsyncDispatchMessage>();
            var before = SampleAllocations();
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < MeasuredIterations; i++) messageBus.Publish<AsyncDispatchMessage>();
            stopwatch.Stop();
            var after = SampleAllocations();
            bool correct = CompletedAsyncCommand.ExecuteCount == WarmupIterations + MeasuredIterations;

            processor.Dispose();
            messageBus.Dispose();
            container.Dispose();
            Destroy(moduleGo);
            return BuildResult("Scenario_MessageToPooledAsyncCommand_CompletedTask", stopwatch, before, after, correct);
        }

        private BenchmarkResult BenchmarkPooledTwelvePayloadCommandDispatch()
        {
            TwelvePayloadCommand.ExecuteCount = 0;
            TwelvePayloadCommand.PayloadSum = 0;
            var moduleGo = new GameObject("PooledTwelvePayloadModule");
            var module = moduleGo.AddComponent<NoInitModule>();
            var container = new MvcDiContainer();
            var messageBus = new MvcMessageBus();
            var processor = new MvcCommandProcessor(typeof(NoInitModule), container, messageBus, module);
            processor.BindCommand<TwelvePayloadCommand, TwelvePayloadMessage, int, int, int, int, int, int, int, int, int, int, int, int>(PoolSize);

            for (int i = 0; i < WarmupIterations; i++)
                messageBus.Publish<TwelvePayloadMessage, int, int, int, int, int, int, int, int, int, int, int, int>(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);
            var before = SampleAllocations();
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < MeasuredIterations; i++)
                messageBus.Publish<TwelvePayloadMessage, int, int, int, int, int, int, int, int, int, int, int, int>(1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1);
            stopwatch.Stop();
            var after = SampleAllocations();
            long expectedCount = WarmupIterations + MeasuredIterations;
            bool correct = TwelvePayloadCommand.ExecuteCount == expectedCount && TwelvePayloadCommand.PayloadSum == expectedCount * 12L;

            processor.Dispose();
            messageBus.Dispose();
            container.Dispose();
            Destroy(moduleGo);
            return BuildResult("Scenario_MessageToPooledCommand_TwelvePayloads", stopwatch, before, after, correct);
        }

        // --- canary: default (unpooled, poolSize == 0) command dispatch, allocates every
        // dispatch by documented design (command.md). Included specifically to prove the GC byte
        // counter is truthful in this real Player build: a non-zero delta here, alongside zero
        // deltas on the pooled paths above, is the evidence Tier 1 could not obtain in the Editor
        // batchmode test runner. ---
        private BenchmarkResult BenchmarkUnpooledDefaultCommandDispatch()
        {
            UnpooledDefaultCommand.ExecuteCount = 0;

            var moduleGo = new GameObject("UnpooledDefaultModule");
            var module = moduleGo.AddComponent<NoInitModule>();
            var container = new MvcDiContainer();
            var messageBus = new MvcMessageBus();
            var processor = new MvcCommandProcessor(typeof(NoInitModule), container, messageBus, module);
            // No poolSize argument - documented default (poolSize: 0) creates and discards a
            // fresh command instance on every single dispatch.
            processor.BindCommand<UnpooledDefaultCommand, UnpooledDispatchMessage>();

            for (int i = 0; i < WarmupIterations; i++)
            {
                messageBus.Publish<UnpooledDispatchMessage>();
            }

            var before = SampleAllocations();
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < MeasuredIterations; i++)
            {
                messageBus.Publish<UnpooledDispatchMessage>();
            }
            stopwatch.Stop();
            var after = SampleAllocations();

            bool correct = UnpooledDefaultCommand.ExecuteCount == WarmupIterations + MeasuredIterations;

            processor.Dispose();
            messageBus.Dispose();
            container.Dispose();
            Destroy(moduleGo);

            return BuildResult("UnpooledDefaultCommandDispatch_Canary", stopwatch, before, after, correct);
        }

        // --- e. Mediator publish (subscribe once, then publish repeatedly) ---
        private sealed class BenchmarkMediator : MediatorBehaviour
        {
            public long ReceivedCount;
            protected override void OnInitialized()
            {
                Messenger.Subscribe<MediatorDispatchMessage>(() => ReceivedCount++);
            }
        }

        private BenchmarkResult BenchmarkMediatorPublish()
        {
            var moduleGo = new GameObject("MediatorPublishModule");
            var module = moduleGo.AddComponent<NoInitModule>();
            var container = new MvcDiContainer();
            var messageBus = new MvcMessageBus();

            var mediatorGo = new GameObject("BenchmarkMediator");
            mediatorGo.transform.SetParent(moduleGo.transform);
            var mediator = mediatorGo.AddComponent<BenchmarkMediator>();
            // Subscribe happens here, once, outside the measured loop below.
            mediator.Initialize(module, container, messageBus);

            // Warmup publish absorbs any first-publish-only cost (JIT, lazy static init).
            messageBus.Publish<MediatorDispatchMessage>();
            mediator.ReceivedCount = 0;

            for (int i = 1; i < WarmupIterations; i++)
            {
                messageBus.Publish<MediatorDispatchMessage>();
            }
            mediator.ReceivedCount = 0;

            var before = SampleAllocations();
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < MeasuredIterations; i++)
            {
                messageBus.Publish<MediatorDispatchMessage>();
            }
            stopwatch.Stop();
            var after = SampleAllocations();

            bool correct = mediator.ReceivedCount == MeasuredIterations;

            Destroy(mediatorGo);
            messageBus.Dispose();
            container.Dispose();
            Destroy(moduleGo);

            return BuildResult("Scenario_MessageToMediator_ZeroPayload", stopwatch, before, after, correct);
        }

        private BenchmarkResult BuildResult(string name, Stopwatch stopwatch, AllocationSnapshot before, AllocationSnapshot after, bool correctnessCheckPassed)
        {
            long gcDeltaBytes = after.gcBytes - before.gcBytes;
            double totalMs = stopwatch.Elapsed.TotalMilliseconds;
            double nsPerOp = (stopwatch.ElapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency)) / MeasuredIterations;
            double gcBytesPerOp = (double)gcDeltaBytes / MeasuredIterations;

            return new BenchmarkResult
            {
                name = name,
                totalMilliseconds = totalMs,
                nanosecondsPerOp = nsPerOp,
                allocatedBytesDelta = gcDeltaBytes,
                bytesPerOp = gcBytesPerOp,
                profilerAllocatedBytesDelta = -1L,
                profilerBytesPerOp = -1.0,
                selectedAllocatedBytesDelta = -1L,
                selectedBytesPerOp = -1.0,
                correctnessCheckPassed = correctnessCheckPassed
            };
        }

        private void CaptureProfilerFrame(ref BenchmarkResult result)
        {
            long frameBytes = _gcAllocatedInFrameRecorder.Valid
                ? _gcAllocatedInFrameRecorder.LastValue
                : -1L;
            result.profilerAllocatedBytesDelta = frameBytes;
            result.profilerBytesPerOp = frameBytes >= 0
                ? (double)frameBytes / MeasuredIterations
                : -1.0;
        }

        private BenchmarkResult SelectAllocationSource(BenchmarkResult result)
        {
            if (_gcCounterFunctional)
            {
                result.allocationDataTrustworthy = true;
                result.allocationSource = "GC.GetAllocatedBytesForCurrentThread";
                result.allocationMeasurementScope = "MeasuredLoop";
                result.selectedAllocatedBytesDelta = result.allocatedBytesDelta;
                result.selectedBytesPerOp = result.bytesPerOp;
            }
            else if (_profilerCounterFunctional && result.profilerAllocatedBytesDelta >= 0)
            {
                result.allocationDataTrustworthy = true;
                result.allocationSource = "ProfilerRecorder.GCAllocatedInFrame";
                result.allocationMeasurementScope = "BenchmarkFrame";
                result.selectedAllocatedBytesDelta = result.profilerAllocatedBytesDelta;
                result.selectedBytesPerOp = result.profilerBytesPerOp;
            }
            else
            {
                result.allocationDataTrustworthy = false;
                result.allocationSource = "Unavailable";
                result.allocationMeasurementScope = "Unavailable";
            }

            return result;
        }

        private static void LogResult(BenchmarkResult result)
        {
            Debug.Log($"[ReleaseBenchmarkHarness] {result.name}: {result.totalMilliseconds:F3} ms total, " +
                      $"{result.nanosecondsPerOp:F1} ns/op, selected={result.selectedAllocatedBytesDelta} bytes " +
                      $"({result.selectedBytesPerOp:F2} bytes/op, {result.allocationSource}, " +
                      $"scope={result.allocationMeasurementScope}), correctnessCheckPassed={result.correctnessCheckPassed}");
        }

        // Looks for "-benchmarkOutput <path>" among the process command-line arguments; falls
        // back to a fixed path next to the project (Application.dataPath/../benchmark-results.json)
        // if the flag isn't present. The fixed fallback is what the harness actually relies on for
        // the headless run documented in the Tier 2 report - it's simpler and more reliable than
        // threading a custom argument through the batchmode invocation, but the flag is supported
        // for callers who do want to control the output location explicitly.
        private static string ResolveOutputPath()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], "-benchmarkOutput", StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }

            return Path.Combine(Application.dataPath, "..", "benchmark-results.json");
        }

        private static void WriteResults(string path, BenchmarkResults results)
        {
            try
            {
                string json = JsonUtility.ToJson(results, prettyPrint: true);
                File.WriteAllText(path, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[ReleaseBenchmarkHarness] Failed to write results to '{path}': {ex}");
            }
        }

        [Serializable]
        private struct BenchmarkResults
        {
            public string runUtc;
            public string unityVersion;
            public string scriptingBackend;
            public bool isDevelopmentBuild;
            public int warmupIterations;
            public int measuredIterations;
            // PERF-02: both flags are set only from the known 1 MB canary. Consumers should use
            // each result's selected allocation fields; this top-level flag indicates whether at
            // least one validated source was available for runtime-scenario results.
            public bool gcCounterFunctional;
            public bool profilerCounterFunctional;
            public bool allocationDataTrustworthy;
            public BenchmarkResult[] results;
            public DataStructureBenchmarkResult[] dataStructureResults;
        }

        [Serializable]
        private struct BenchmarkResult
        {
            public string name;
            public double totalMilliseconds;
            public double nanosecondsPerOp;
            // From GC.GetAllocatedBytesForCurrentThread(); only meaningful when the enclosing
            // BenchmarkResults.gcCounterFunctional is true.
            public long allocatedBytesDelta;
            public double bytesPerOp;
            // Whole-frame sample from ProfilerRecorder "GC Allocated In Frame"; -1 when that
            // counter was unavailable. Unlike the synchronous GC delta above, this includes setup,
            // cleanup, and engine allocations in the frame containing the benchmark.
            public long profilerAllocatedBytesDelta;
            public double profilerBytesPerOp;
            // Canonical allocation value selected from the validated counter. Consumers should
            // read these fields instead of reimplementing source-selection logic.
            public bool allocationDataTrustworthy;
            public string allocationSource;
            public string allocationMeasurementScope;
            public long selectedAllocatedBytesDelta;
            public double selectedBytesPerOp;
            public bool correctnessCheckPassed;
        }
    }
}

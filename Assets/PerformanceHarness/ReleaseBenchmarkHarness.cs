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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using mvcExpress;
using mvcExpress.Internal.Commands;
using mvcExpress.Internal.DependencyInjection;
using mvcExpress.Internal.Messaging;
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

        // --- Message/command/service types, mirroring the Tier 1 NUnit fixture's private nested
        // types as closely as a top-level runtime script allows (no test-only static Reset()
        // convenience needed here since each benchmark method creates its own fresh container). ---

        private readonly struct ZeroPayloadMessage : IMessage { }
        private readonly struct OnePayloadMessage : IMessage<int> { }
        private readonly struct PooledDispatchMessage : IMessage { }
        private readonly struct InjectedDispatchMessage : IMessage { }
        private readonly struct UnpooledDispatchMessage : IMessage { }
        private readonly struct MediatorDispatchMessage : IMessage { }

        private sealed class PooledZeroPayloadCommand : Command
        {
            public static long ExecuteCount;
            public override void Execute() => ExecuteCount++;
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

        private void Start()
        {
            Debug.Log("[ReleaseBenchmarkHarness] Starting Tier 2 release-Player benchmark run.");

            var results = new BenchmarkResults
            {
                unityVersion = Application.unityVersion,
                isDevelopmentBuild = Debug.isDebugBuild,
                warmupIterations = WarmupIterations,
                measuredIterations = MeasuredIterations,
                results = new[]
                {
                    BenchmarkGcByteCounterDiagnostic(),
                    BenchmarkMessagePublishZeroPayload(),
                    BenchmarkMessagePublishOnePayload(),
                    BenchmarkPooledCommandDispatchZeroPayload(),
                    BenchmarkPooledCommandDispatchWithInjectedDependency(),
                    BenchmarkUnpooledDefaultCommandDispatch(),
                    BenchmarkMediatorPublish(),
                }
            };

            string outputPath = ResolveOutputPath();
            WriteResults(outputPath, results);

            Debug.Log($"[ReleaseBenchmarkHarness] Wrote benchmark results to '{outputPath}'. Quitting.");

            Application.Quit(0);

#if UNITY_EDITOR
            // Application.Quit is a no-op in the Editor; this harness is designed to run as a
            // built Player, but avoid hanging an accidental Editor Play Mode run.
            UnityEditor.EditorApplication.isPlaying = false;
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
            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            byte[] scratch = new byte[arraySize];
            scratch[0] = 1;
            scratch[arraySize - 1] = 2;
            long afterBytes = GC.GetAllocatedBytesForCurrentThread();
            stopwatch.Stop();

            bool correct = scratch[0] == 1 && scratch[arraySize - 1] == 2;
            long delta = afterBytes - beforeBytes;

            Debug.Log($"[ReleaseBenchmarkHarness] GC byte counter diagnostic: allocated a {arraySize} byte " +
                      $"array, observed delta={delta} bytes. {(delta >= arraySize ? "Counter appears FUNCTIONAL." : "Counter appears NON-FUNCTIONAL (reads back 0 or implausibly low, same as the Tier 1 Editor batchmode finding).")}");

            return BuildResult("GcByteCounterDiagnostic_1MBAllocation", stopwatch, beforeBytes, afterBytes, correct);
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

            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < MeasuredIterations; i++)
            {
                messageBus.Publish<ZeroPayloadMessage>();
            }
            stopwatch.Stop();
            long afterBytes = GC.GetAllocatedBytesForCurrentThread();

            messageBus.Dispose();

            return BuildResult("MessagePublish_ZeroPayload", stopwatch, beforeBytes, afterBytes,
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

            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < MeasuredIterations; i++)
            {
                messageBus.Publish<OnePayloadMessage, int>(1);
            }
            stopwatch.Stop();
            long afterBytes = GC.GetAllocatedBytesForCurrentThread();

            messageBus.Dispose();

            return BuildResult("MessagePublish_OnePayload", stopwatch, beforeBytes, afterBytes,
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

            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < MeasuredIterations; i++)
            {
                messageBus.Publish<PooledDispatchMessage>();
            }
            stopwatch.Stop();
            long afterBytes = GC.GetAllocatedBytesForCurrentThread();

            bool correct = PooledZeroPayloadCommand.ExecuteCount == WarmupIterations + MeasuredIterations;

            processor.Dispose();
            messageBus.Dispose();
            container.Dispose();
            Destroy(moduleGo);

            return BuildResult("PooledCommandDispatch_ZeroPayload", stopwatch, beforeBytes, afterBytes, correct);
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

            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < MeasuredIterations; i++)
            {
                messageBus.Publish<InjectedDispatchMessage>();
            }
            stopwatch.Stop();
            long afterBytes = GC.GetAllocatedBytesForCurrentThread();

            bool correct = InjectedDependencyCommand.ExecuteCount == WarmupIterations + MeasuredIterations;

            processor.Dispose();
            messageBus.Dispose();
            container.Dispose();
            Destroy(moduleGo);

            return BuildResult("PooledCommandDispatch_WithInjectedDependency", stopwatch, beforeBytes, afterBytes, correct);
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

            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < MeasuredIterations; i++)
            {
                messageBus.Publish<UnpooledDispatchMessage>();
            }
            stopwatch.Stop();
            long afterBytes = GC.GetAllocatedBytesForCurrentThread();

            bool correct = UnpooledDefaultCommand.ExecuteCount == WarmupIterations + MeasuredIterations;

            processor.Dispose();
            messageBus.Dispose();
            container.Dispose();
            Destroy(moduleGo);

            return BuildResult("UnpooledDefaultCommandDispatch_Canary", stopwatch, beforeBytes, afterBytes, correct);
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

            long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            for (int i = 0; i < MeasuredIterations; i++)
            {
                messageBus.Publish<MediatorDispatchMessage>();
            }
            stopwatch.Stop();
            long afterBytes = GC.GetAllocatedBytesForCurrentThread();

            bool correct = mediator.ReceivedCount == MeasuredIterations;

            Destroy(mediatorGo);
            messageBus.Dispose();
            container.Dispose();
            Destroy(moduleGo);

            return BuildResult("MediatorPublish_AfterSubscribeWarmup", stopwatch, beforeBytes, afterBytes, correct);
        }

        private BenchmarkResult BuildResult(string name, Stopwatch stopwatch, long beforeBytes, long afterBytes, bool correctnessCheckPassed)
        {
            long deltaBytes = afterBytes - beforeBytes;
            double totalMs = stopwatch.Elapsed.TotalMilliseconds;
            double nsPerOp = (stopwatch.ElapsedTicks * (1_000_000_000.0 / Stopwatch.Frequency)) / MeasuredIterations;
            double bytesPerOp = (double)deltaBytes / MeasuredIterations;

            Debug.Log($"[ReleaseBenchmarkHarness] {name}: {totalMs:F3} ms total, {nsPerOp:F1} ns/op, " +
                      $"{deltaBytes} bytes delta ({bytesPerOp:F2} bytes/op), correctnessCheckPassed={correctnessCheckPassed}");

            return new BenchmarkResult
            {
                name = name,
                totalMilliseconds = totalMs,
                nanosecondsPerOp = nsPerOp,
                allocatedBytesDelta = deltaBytes,
                bytesPerOp = bytesPerOp,
                correctnessCheckPassed = correctnessCheckPassed
            };
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
            public string unityVersion;
            public bool isDevelopmentBuild;
            public int warmupIterations;
            public int measuredIterations;
            public BenchmarkResult[] results;
        }

        [Serializable]
        private struct BenchmarkResult
        {
            public string name;
            public double totalMilliseconds;
            public double nanosecondsPerOp;
            public long allocatedBytesDelta;
            public double bytesPerOp;
            public bool correctnessCheckPassed;
        }
    }
}

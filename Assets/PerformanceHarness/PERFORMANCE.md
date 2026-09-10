# mvcExpress performance benchmarks

The performance suite runs inside non-development Unity Players so measurements cover the actual Mono and IL2CPP runtime paths rather than Editor-only behavior.

## Coverage

The release harness measures:

- Zero-, one-, and twelve-payload message publishing.
- Pooled synchronous command dispatch.
- Pooled command dispatch with dependency injection.
- Pooled asynchronous command dispatch with an already-completed task.
- Pooled twelve-payload command dispatch.
- Unpooled command dispatch as an allocation canary.
- Message-to-mediator callback dispatch.
- DI index lookup, registration, snapshot construction, and dynamic side-table lookup.
- Direct `Type.IsInterface` versus cached metadata lookup.
- Tuple-key command delegate metadata caches.

Each benchmark performs a warmup, records elapsed time and available allocation counters, and validates its observable result.

Allocation counters are validated independently against a known 1 MB allocation. When
`GC.GetAllocatedBytesForCurrentThread()` is functional, `selectedBytesPerOp` is its precise delta
around the measured loop. Otherwise, a functional `ProfilerRecorder` supplies the completed
`GC Allocated In Frame` sample from the frame containing that benchmark. The profiler fallback is
deliberately labeled with `allocationMeasurementScope: BenchmarkFrame` because it also includes
setup, cleanup, and Unity engine allocations in that frame; it is a conservative fallback, not an
equivalent precision measurement. Data-structure benchmark allocations remain GC-counter-only and
are reported as unavailable when that counter fails.

## Collect a dated report

For the interactive console dashboard:

```powershell
.\Tools\Performance\Open-PerformanceDashboard.ps1
```

The dashboard can run campaigns, list saved revisions, generate/open readable HTML reports, and
select two dated reports for a visual before/after comparison.

For direct script use, run from the repository root:

```powershell
.\Tools\Performance\Run-PerformanceBenchmarks.ps1
```

The default campaign builds Mono and IL2CPP Standalone Windows Players and runs each backend 15 times. Every campaign gets a dated directory under `BenchmarkResults/Performance`, containing raw JSON, Player logs, and a CSV summary.

For a quick Mono smoke run:

```powershell
.\Tools\Performance\Run-PerformanceBenchmarks.ps1 -Backends Mono -Runs 1 -SkipBuild -AllowTimingOnly
```

`-AllowTimingOnly` accepts platforms where both allocation-counter canaries fail, but leaves
allocation columns empty rather than reporting invalid zeroes. The campaign consumes each runtime
result's validated `selectedBytesPerOp` and records its `AllocationSource` and
`AllocationMeasurementScope` in the CSV; it never substitutes a failed GC counter for a working
profiler counter. If runs in one timing group use different allocation scopes, timing is still
aggregated but allocation is marked `Mixed` and withheld rather than averaging incompatible values. The dashboard enables this option
by default. Backend mismatches, missing output, and correctness failures still fail the campaign.

## Compare before and after

Keep the dated report directory from before a refactoring, collect a new report afterward, then run:

```powershell
.\Tools\Performance\Compare-PerformanceReports.ps1 `
    -Before .\BenchmarkResults\Performance\2026-09-07_10-00-00\performance-summary-2026-09-07_10-00-00.csv `
    -After .\BenchmarkResults\Performance\2026-09-14_10-00-00\performance-summary-2026-09-14_10-00-00.csv
```

Negative `TimingDeltaPercent` and `AllocationDeltaPercent` values are improvements. Compare Mono and IL2CPP separately and investigate changes smaller than normal run-to-run variation before treating them as regressions.

## Environment requirements

- Unity `6000.5.10f1` or the repository's current pinned editor.
- The Windows Build Support module for Mono.
- The Windows IL2CPP Build Support module for IL2CPP campaigns.
- Release Players only; development builds and deep profiling materially change timings.

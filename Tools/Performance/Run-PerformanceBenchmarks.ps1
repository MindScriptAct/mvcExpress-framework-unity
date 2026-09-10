param(
	[ValidateSet("Mono", "IL2CPP")]
	[string[]] $Backends = @("Mono", "IL2CPP"),
	[ValidateRange(1, 100)]
	[int] $Runs = 15,
	[switch] $SkipBuild,
	[switch] $AllowTimingOnly,
	[string] $Label = ""
)

$ErrorActionPreference = "Stop"
$projectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
$runTimestamp = Get-Date -Format "yyyy-MM-dd_HH-mm-ss"
$outputRoot = Join-Path $projectRoot "BenchmarkResults\Performance\$runTimestamp"
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$metadata = [ordered]@{
	label = $Label
	runId = $runTimestamp
	createdUtc = (Get-Date).ToUniversalTime().ToString("O")
	gitBranch = (& git -C $projectRoot branch --show-current 2>$null)
	gitCommit = (& git -C $projectRoot rev-parse HEAD 2>$null)
	gitDirty = [bool](& git -C $projectRoot status --porcelain 2>$null)
	backends = @($Backends)
	runsPerBackend = $Runs
}
$metadata | ConvertTo-Json | Set-Content (Join-Path $outputRoot "performance-metadata.json") -Encoding UTF8

$configuration = @{
	Mono = @{
		BuildMethod = "mvcExpress.Editor.PerformanceHarnessBuilder.BuildMonoBenchmarkPlayer"
		Executable = "Builds\PerformanceHarness\Mono\ReleaseBenchmarkHarness.exe"
	}
	IL2CPP = @{
		BuildMethod = "mvcExpress.Editor.PerformanceHarnessBuilder.BuildIl2CppBenchmarkPlayer"
		Executable = "Builds\PerformanceHarness\IL2CPP\ReleaseBenchmarkHarness.exe"
	}
}

Push-Location $projectRoot
try {
	foreach ($backend in $Backends) {
		$settings = $configuration[$backend]
		if (-not $SkipBuild) {
			& unity build --target StandaloneWindows64 --execute-method $settings.BuildMethod
			if ($LASTEXITCODE -ne 0) { throw "$backend benchmark Player build failed with exit code $LASTEXITCODE." }
		}

		$executable = Join-Path $projectRoot $settings.Executable
		if (-not (Test-Path $executable)) { throw "Benchmark executable not found: $executable" }

		for ($run = 1; $run -le $Runs; $run++) {
			$output = Join-Path $outputRoot ("performance-{0}-run{1:D2}.json" -f $backend.ToLowerInvariant(), $run)
			$process = Start-Process -FilePath $executable -ArgumentList @(
				"-batchmode",
				"-nographics",
				"-logFile",
				"`"$(Join-Path $outputRoot ("performance-{0}-run{1:D2}.log" -f $backend.ToLowerInvariant(), $run))`"",
				"-benchmarkOutput",
				"`"$output`""
			) -Wait -PassThru
			if (-not (Test-Path $output)) { throw "$backend benchmark run $run produced no JSON output." }

			$document = Get-Content -Raw $output | ConvertFrom-Json
			if ($process.ExitCode -ne 0 -and -not ($AllowTimingOnly -and -not $document.allocationDataTrustworthy)) {
				if (-not $document.allocationDataTrustworthy) {
					throw "$backend benchmark run $run produced valid timing data, but allocation counters are unavailable. Rerun with -AllowTimingOnly to accept timing-only results. Player log: $(Join-Path $outputRoot ("performance-{0}-run{1:D2}.log" -f $backend.ToLowerInvariant(), $run))"
				}
				throw "$backend benchmark run $run failed with exit code $($process.ExitCode)."
			}
			if ($document.scriptingBackend -ne $backend) {
				throw "Expected $backend output, but run $run reported '$($document.scriptingBackend)'."
			}
			if (-not $document.allocationDataTrustworthy -and -not $AllowTimingOnly) {
				throw "$backend run $run failed its allocation-counter canary."
			}
			$failed = @($document.results + $document.dataStructureResults | Where-Object { -not $_.correctnessCheckPassed })
			if ($failed.Count -gt 0) {
				throw "$backend run $run has $($failed.Count) failed benchmark correctness check(s)."
			}
		}
	}
}
finally {
	Pop-Location
}

$rows = foreach ($file in Get-ChildItem $outputRoot -Filter "performance-*-run*.json") {
	$document = Get-Content -Raw $file.FullName | ConvertFrom-Json
	foreach ($result in $document.results) {
		$hasSelectedAllocation = $null -ne $result.PSObject.Properties["selectedBytesPerOp"]
		$allocationTrustworthy = if ($hasSelectedAllocation) {
			[bool]$result.allocationDataTrustworthy
		} else {
			[bool]$document.gcCounterFunctional
		}
		$allocationSource = if ($hasSelectedAllocation) {
			[string]$result.allocationSource
		} elseif ($document.gcCounterFunctional) {
			"GC.GetAllocatedBytesForCurrentThread"
		} else {
			"Unavailable"
		}
		$allocationScope = if ($hasSelectedAllocation) {
			[string]$result.allocationMeasurementScope
		} elseif ($document.gcCounterFunctional) {
			"MeasuredLoop"
		} else {
			"Unavailable"
		}
		[pscustomobject]@{
			Backend = $document.scriptingBackend
			UnityVersion = $document.unityVersion
			Category = "Runtime scenario"
			Variant = "Current implementation"
			Operation = $result.name
			TableSize = 0
			NanosecondsPerOperation = [double]$result.nanosecondsPerOp
			AllocationDataTrustworthy = $allocationTrustworthy
			AllocationSource = $allocationSource
			AllocationMeasurementScope = $allocationScope
			BytesPerOperation = if ($allocationTrustworthy) {
				if ($hasSelectedAllocation) { [double]$result.selectedBytesPerOp } else { [double]$result.bytesPerOp }
			} else { $null }
		}
	}
	foreach ($result in $document.dataStructureResults) {
		$allocationTrustworthy = [bool]$document.gcCounterFunctional
		[pscustomobject]@{
			Backend = $document.scriptingBackend
			UnityVersion = $document.unityVersion
			Category = $result.category
			Variant = $result.variant
			Operation = $result.operation
			TableSize = [int]$result.tableSize
			NanosecondsPerOperation = [double]$result.nanosecondsPerOperation
			AllocationDataTrustworthy = $allocationTrustworthy
			AllocationSource = if ($allocationTrustworthy) { "GC.GetAllocatedBytesForCurrentThread" } else { "Unavailable" }
			AllocationMeasurementScope = if ($allocationTrustworthy) { "MeasuredLoop" } else { "Unavailable" }
			BytesPerOperation = if ($allocationTrustworthy) { [double]$result.bytesPerOperation } else { $null }
		}
	}
}

$summary = foreach ($group in $rows | Group-Object Backend, Category, Variant, Operation, TableSize) {
	$ordered = @($group.Group.NanosecondsPerOperation | Sort-Object)
	$count = $ordered.Count
	$median = if (($count % 2) -eq 0) {
		($ordered[$count / 2 - 1] + $ordered[$count / 2]) / 2
	} else {
		$ordered[[math]::Floor($count / 2)]
	}
	$p95Index = [math]::Min($count - 1, [math]::Ceiling($count * 0.95) - 1)
	$first = $group.Group[0]
	$trustedAllocationRows = @($group.Group | Where-Object AllocationDataTrustworthy)
	$allocationIdentities = @($trustedAllocationRows | ForEach-Object { "$($_.AllocationSource)|$($_.AllocationMeasurementScope)" } | Select-Object -Unique)
	$allocationSource = if ($trustedAllocationRows.Count -eq 0) { "Unavailable" } elseif ($allocationIdentities.Count -eq 1) { $trustedAllocationRows[0].AllocationSource } else { "Mixed" }
	$allocationScope = if ($trustedAllocationRows.Count -eq 0) { "Unavailable" } elseif ($allocationIdentities.Count -eq 1) { $trustedAllocationRows[0].AllocationMeasurementScope } else { "Mixed" }
	# Never average precise measured-loop GC values with conservative whole-frame profiler values.
	# Timing samples remain comparable and stay in one row; only the allocation aggregate is withheld.
	$byteSamples = if ($allocationIdentities.Count -le 1) { @($trustedAllocationRows | ForEach-Object BytesPerOperation) } else { @() }
	[pscustomobject]@{
		Backend = $first.Backend
		Category = $first.Category
		Variant = $first.Variant
		Operation = $first.Operation
		TableSize = $first.TableSize
		AllocationSource = $allocationSource
		AllocationMeasurementScope = $allocationScope
		Samples = $count
		MedianNanoseconds = [math]::Round($median, 3)
		P95Nanoseconds = [math]::Round($ordered[$p95Index], 3)
		MeanNanoseconds = [math]::Round(($ordered | Measure-Object -Average).Average, 3)
		AllocationSamples = $byteSamples.Count
		MeanBytesPerOperation = if ($byteSamples.Count -gt 0) {
			[math]::Round(($byteSamples | Measure-Object -Average).Average, 3)
		} else {
			$null
		}
	}
}

$summaryPath = Join-Path $outputRoot "performance-summary-$runTimestamp.csv"
$summary | Sort-Object Backend, Category, Operation, TableSize, Variant | Export-Csv -NoTypeInformation $summaryPath
$summary | Sort-Object Backend, Category, Operation, TableSize, Variant | Format-Table -AutoSize
Write-Host "Performance summary written to $summaryPath"

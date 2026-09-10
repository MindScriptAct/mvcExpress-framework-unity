param(
	[Parameter(Mandatory = $true)]
	[string] $Before,
	[Parameter(Mandatory = $true)]
	[string] $After,
	[string] $Output
)

$ErrorActionPreference = "Stop"
$beforeRows = Import-Csv (Resolve-Path $Before)
$afterRows = Import-Csv (Resolve-Path $After)

function Get-Key($row) {
	return "$($row.Backend)|$($row.Category)|$($row.Variant)|$($row.Operation)|$($row.TableSize)"
}

$beforeByKey = @{}
foreach ($row in $beforeRows) { $beforeByKey[(Get-Key $row)] = $row }

$comparison = foreach ($afterRow in $afterRows) {
	$key = Get-Key $afterRow
	if (-not $beforeByKey.ContainsKey($key)) { continue }

	$beforeRow = $beforeByKey[$key]
	$beforeTiming = [double]$beforeRow.MedianNanoseconds
	$afterTiming = [double]$afterRow.MedianNanoseconds
	$timingDelta = if ($beforeTiming -ne 0) { (($afterTiming - $beforeTiming) / $beforeTiming) * 100 } else { $null }

	$allocationDelta = $null
	if ($beforeRow.AllocationSamples -ne "0" -and $afterRow.AllocationSamples -ne "0" -and
		$beforeRow.MeanBytesPerOperation -ne "" -and $afterRow.MeanBytesPerOperation -ne "") {
		$beforeAllocation = [double]$beforeRow.MeanBytesPerOperation
		$afterAllocation = [double]$afterRow.MeanBytesPerOperation
		if ($beforeAllocation -ne 0) {
			$allocationDelta = (($afterAllocation - $beforeAllocation) / $beforeAllocation) * 100
		}
	}

	[pscustomobject]@{
		Backend = $afterRow.Backend
		Category = $afterRow.Category
		Variant = $afterRow.Variant
		Operation = $afterRow.Operation
		TableSize = $afterRow.TableSize
		BeforeMedianNanoseconds = $beforeTiming
		AfterMedianNanoseconds = $afterTiming
		TimingDeltaPercent = if ($null -eq $timingDelta) { $null } else { [math]::Round($timingDelta, 2) }
		BeforeMeanBytesPerOperation = $beforeRow.MeanBytesPerOperation
		AfterMeanBytesPerOperation = $afterRow.MeanBytesPerOperation
		AllocationDeltaPercent = if ($null -eq $allocationDelta) { $null } else { [math]::Round($allocationDelta, 2) }
	}
}

if (-not $Output) {
	$afterDirectory = Split-Path (Resolve-Path $After) -Parent
	$Output = Join-Path $afterDirectory "performance-comparison.csv"
}

$comparison | Sort-Object Backend, Category, Operation, TableSize, Variant | Export-Csv -NoTypeInformation $Output
$comparison | Sort-Object TimingDeltaPercent -Descending | Format-Table -AutoSize
Write-Host "Performance comparison written to $Output"

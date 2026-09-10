param(
    [Parameter(Mandatory = $true)][string]$LogPath,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$records = [System.Collections.Generic.List[object]]::new()
$pending = $null
$diagnostic = $false
$launch = 0
$phaseNames = @('Activation', 'Admission', 'Search', 'Publication', 'Completed')

function Get-FrameStatistics([long[]]$Ticks, [long]$Frequency) {
    if ($Ticks.Length -eq 0) { return $null }
    $sortedTicks = [long[]]$Ticks.Clone()
    [Array]::Sort($sortedTicks)
    $position = ($sortedTicks.Length - 1) * 0.99
    $lower = [int][Math]::Floor($position)
    $upper = [int][Math]::Ceiling($position)
    $p99 = $sortedTicks[$lower] + ($sortedTicks[$upper] - $sortedTicks[$lower]) * ($position - $lower)
    $overAllowance = 0
    $overFrame = 0
    foreach ($tick in $Ticks) {
        if ($tick * 1000.0 / $Frequency -gt 2) { $overAllowance++ }
        if ($tick * 1000.0 / $Frequency -gt 31.25) { $overFrame++ }
    }
    [pscustomobject]@{
        Count = $Ticks.Length
        ObservedP99Milliseconds = $p99 * 1000.0 / $Frequency
        MaximumMilliseconds = $sortedTicks[-1] * 1000.0 / $Frequency
        Over2Milliseconds = $overAllowance
        Over31_25Milliseconds = $overFrame
    }
}

function Convert-Sample($Sample, [string]$Stage, [int]$Index, [double]$OperationNanoseconds) {
    foreach ($arrayName in @('ticks', 'allocatedBytes', 'completionMasks', 'phases', 'activeRequests')) {
        if ($Sample.$arrayName.Count -ne $Sample.frames) { throw "Wrong $arrayName length in process $($Sample.process)." }
    }
    if ($null -ne $Sample.collectionMasks) {
        if ($Sample.collectionMasks.Count -ne $Sample.frames) { throw 'Wrong collectionMasks length.' }
        foreach ($collectionMask in $Sample.collectionMasks) {
            if ($collectionMask -isnot [long] -and $collectionMask -isnot [int]) { throw 'Non-integer collection mask.' }
            if ($collectionMask -lt 0 -or $collectionMask -gt 7) { throw 'Invalid collection generation mask.' }
        }
    }
    if ($Sample.frames -le 0 -or $Sample.stopwatchFrequency -le 0) { throw 'Invalid frame/frequency metadata.' }
    $completionFrames = [int[]]::new($Sample.requests)
    $phaseTicks = @{}
    $phaseMaximumFrames = @{}
    $previousMask = 0
    $allocated = 0L
    for ($frame = 0; $frame -lt $Sample.frames; $frame++) {
        $mask = [int]$Sample.completionMasks[$frame]
        if (($mask -band $previousMask) -ne $previousMask) { throw 'A completed request became pending again.' }
        for ($request = 0; $request -lt $Sample.requests; $request++) {
            if (($mask -band (1 -shl $request)) -ne 0 -and $completionFrames[$request] -eq 0) {
                $completionFrames[$request] = $frame + 1
            }
        }
        $previousMask = $mask
        $allocated += [long]$Sample.allocatedBytes[$frame]
        $phase = if ($Sample.activeRequests[$frame] -lt 0) { 'Idle' } else { $phaseNames[[int]$Sample.phases[$frame]] }
        if (!$phaseTicks.ContainsKey($phase)) {
            $phaseTicks[$phase] = [System.Collections.Generic.List[long]]::new()
            $phaseMaximumFrames[$phase] = $frame
        }
        $phaseTicks[$phase].Add([long]$Sample.ticks[$frame])
        if ($Sample.ticks[$frame] -gt $Sample.ticks[$phaseMaximumFrames[$phase]]) { $phaseMaximumFrames[$phase] = $frame }
    }
    if ($previousMask -ne (1 -shl $Sample.requests) - 1) { throw 'Incomplete request mask at operation end.' }
    $phases = foreach ($phase in @('Idle', 'Activation', 'Admission', 'Search', 'Publication', 'Completed')) {
        if ($phaseTicks.ContainsKey($phase)) {
            [pscustomobject]@{
                Phase = $phase
                Statistics = Get-FrameStatistics $phaseTicks[$phase].ToArray() $Sample.stopwatchFrequency
                MaximumFrameIndex = $phaseMaximumFrames[$phase]
                MaximumFrameAllocatedBytes = $Sample.allocatedBytes[$phaseMaximumFrames[$phase]]
            }
        }
    }
    [pscustomobject]@{
        Process = $Sample.process; Launch = $launch; Stage = $Stage; StageIndex = $Index
        ExportIteration = $Sample.iteration; Nodes = $Sample.nodes; Algorithm = $Sample.algorithm
        Requests = $Sample.requests; Settings = $Sample.settings; Frames = $Sample.frames
        CompletionFrames = $completionFrames
        CollectionOverlapFrames = @($(if ($null -ne $Sample.collectionMasks) {
            for ($frame = 0; $frame -lt $Sample.frames; $frame++) {
                if ($Sample.collectionMasks[$frame] -ne 0) {
                    [pscustomobject]@{ Frame = $frame; GenerationMask = $Sample.collectionMasks[$frame]
                        Milliseconds = $Sample.ticks[$frame] * 1000.0 / $Sample.stopwatchFrequency }
                }
            }
        }))
        SimulatedWaitingSeconds = @($completionFrames | ForEach-Object { $_ / [double]$Sample.frameRate })
        WholeOperationNanoseconds = $OperationNanoseconds
        NestedAllocatedBytes = $allocated
        FrameStatistics = Get-FrameStatistics ([long[]]$Sample.ticks) $Sample.stopwatchFrequency
        Phases = @($phases)
    }
}

foreach ($line in [System.IO.File]::ReadLines((Resolve-Path -LiteralPath $LogPath))) {
    if ($line.StartsWith('// Benchmark:')) {
        if ($null -ne $pending) { throw 'Unmatched sample at benchmark boundary.' }
        $diagnostic = $false
    }
    if ($line -match '^// Launch: (\d+) /') { $launch = [int]$Matches[1]; $diagnostic = $false }
    if ($line -eq '// BeforeActualRun') { $diagnostic = $false }
    if ($line -eq '// AfterActualRun') { $diagnostic = $true }
    if ($line.StartsWith('NAVIGATION_SCHEDULE_SAMPLES ')) {
        if ($null -ne $pending) { throw 'Two nested records without an intervening BDN measurement.' }
        $sample = $line.Substring(28) | ConvertFrom-Json
        if ($diagnostic) {
            $records.Add((Convert-Sample $sample 'Diagnostic' 0 0))
        } else { $pending = $sample }
    }
    if ($line -match '^Workload(Warmup|Actual)\s+(\d+):\s+1 op,\s+([0-9.]+) ns,') {
        if ($null -eq $pending) { throw 'BDN measurement has no nested frame record.' }
        $stage = $Matches[1]
        $index = [int]$Matches[2]
        $nanoseconds = [double]::Parse($Matches[3], [Globalization.CultureInfo]::InvariantCulture)
        $records.Add((Convert-Sample $pending $stage $index $nanoseconds))
        $pending = $null
    }
}
if ($null -ne $pending) { throw 'Unmatched nested sample at end of log.' }
if (!($records | Where-Object Stage -eq 'Actual')) { throw 'No actual frame records were found.' }
[pscustomobject]@{ SourceLog = (Resolve-Path -LiteralPath $LogPath).Path; Records = $records.ToArray() } |
    ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath
$records | Where-Object Stage -eq 'Actual' | Select-Object Nodes, Algorithm, Requests, Launch, StageIndex, Frames,
    @{Name='P99ms';Expression={$_.FrameStatistics.ObservedP99Milliseconds}},
    @{Name='MaxMs';Expression={$_.FrameStatistics.MaximumMilliseconds}},
    @{Name='Over2ms';Expression={$_.FrameStatistics.Over2Milliseconds}} | Format-Table

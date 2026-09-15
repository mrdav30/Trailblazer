# Behavior checks for the log parser; generated fixtures remain under artifacts.
$ErrorActionPreference = 'Stop'
$directory = Join-Path $PSScriptRoot "../../artifacts/los-phase/phase2/parser-tests/$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$log = Join-Path $directory 'fixture.log'
$output = Join-Path $directory 'summary.json'
$parser = Join-Path $PSScriptRoot 'AnalyzeGuidedFrames.ps1'

function New-Sample([string]$Algorithm = 'AStar', [int]$Agents = 100) {
    [ordered]@{
        process = 1; iteration = 1; algorithm = $Algorithm; agents = $Agents
        frameRate = 32; preparationFrames = 32; frames = 64; stopwatchFrequency = 1000
        ticks = @(1L) * 64; allocatedBytes = @(0L) * 64
        collectionMasks = @(0) * 64; checksums = @([long]::MaxValue) * 64
    }
}

function Add-Instrumentation($Sample, [string]$Mode, [long]$ClockOrigin = 64) {
    $phases = switch ($Mode) {
        'Unphased' { @($null) * $Sample.agents }
        'BalancedPhases' { @(for ($i = 0; $i -lt $Sample.agents; $i++) { $i % 16 }) }
        'UnbalancedPhase' { @(0) * $Sample.agents }
    }
    $preparation = @(0) * 32
    $periodic = @(0) * 64
    $preparation[0] = $Sample.agents
    if ($Mode -eq 'Unphased') {
        $preparation[16] = $Sample.agents
        foreach ($frame in @(0, 16, 32, 48)) { $periodic[$frame] = $Sample.agents }
    }
    else {
        for ($frame = 1; $frame -lt 32; $frame++) {
            $remainder = (($ClockOrigin - 32 + $frame) % 16 + 16) % 16
            $preparation[$frame] = @($phases | Where-Object { $_ -eq $remainder }).Count
        }
        for ($frame = 0; $frame -lt 64; $frame++) {
            $remainder = (($ClockOrigin + $frame) % 16 + 16) % 16
            $periodic[$frame] = @($phases | Where-Object { $_ -eq $remainder }).Count
        }
    }
    $Sample.mode = $Mode
    $Sample.intervalFrames = 16
    $Sample.phaseAssignments = $phases
    $Sample.clockOrigin = $ClockOrigin
    $Sample.mandatoryRayCount = $Sample.agents
    $Sample.preparationRayCounts = $preparation
    $Sample.periodicRayCounts = $periodic
    $Sample.firstAdmissionTicks = 7L
    $Sample.firstAdmissionAllocatedBytes = 0L
    $Sample.firstAdmissionCollectionMask = 0
}

function Get-Header([string]$Method, [string]$Algorithm = 'AStar', [int]$Agents = 100,
    [bool]$WithCounts = $true) {
    $job = if ($WithCounts) {
        'Job-X(InvocationCount=1, IterationCount=1, LaunchCount=1, WarmupCount=1)'
    } else { 'Job' }
    "// Benchmark: NavigationGuidedFrameBenchmarks.${Method}: $job [AgentCount=$Agents, Algorithm=$Algorithm]"
}

function Get-Record($Sample) {
    'NAVIGATION_GUIDED_SAMPLES ' + ($Sample | ConvertTo-Json -Depth 8 -Compress)
}

function Get-CompleteLaunch($Sample, [int]$Launch = 1, [int]$Launches = 1) {
    $warm = Get-Record $Sample
    $Sample.iteration++
    $actual = Get-Record $Sample
    $Sample.iteration++
    $diagnostic = Get-Record $Sample
    @(
        "// Launch: $Launch / $Launches"
        '// BeforeActualRun'
        $warm
        'WorkloadWarmup 1: 1 op, 64000000.00 ns, 64 ms/op'
        $actual
        'WorkloadActual 1: 1 op, 128000000.00 ns, 128 ms/op'
        '// AfterActualRun'
        $diagnostic
        'WorkloadResult 1: 1 op, 128000000.00 ns, 128 ms/op'
        "// Benchmark Process $($Sample.process) has exited with code 0."
    )
}

function Invoke-Parser([string[]]$Lines) {
    $Lines | Set-Content -LiteralPath $log
    & $parser -LogPath $log -OutputPath $output | Out-Null
    Get-Content -Raw -LiteralPath $output | ConvertFrom-Json
}

function Invoke-Rejected([string[]]$Lines, [string]$ExpectedMessage) {
    $Lines | Set-Content -LiteralPath $log
    $rejected = $false
    try { & $parser -LogPath $log -OutputPath $output | Out-Null }
    catch {
        if ($_.Exception.Message -notlike "*$ExpectedMessage*") { throw }
        $rejected = $true
    }
    if (!$rejected) { throw "Parser accepted malformed input: $ExpectedMessage" }
}

# Historical payload shape remains accepted, exact Int64 replay survives JSON parsing,
# and warmup/diagnostic observations do not pollute actual nested statistics.
$sample = New-Sample
$header = Get-Header 'Simulate64GuidedFrames'
$launch = Get-CompleteLaunch $sample
$launch[2] = ($launch[2] -replace '"ticks":\[1,', '"ticks":[1000,')
$launch[4] = ($launch[4] -replace '"ticks":\[1,', '"ticks":[65,' -replace '"allocatedBytes":\[0,', '"allocatedBytes":[16,' -replace '"collectionMasks":\[0,', '"collectionMasks":[1,')
$launch[7] = ($launch[7] -replace '"ticks":\[1,', '"ticks":[2000,')
$result = Invoke-Parser (@($header) + $launch)
$stats = $result.Summaries[0].Statistics
$block = $result.Summaries[0].BlockStatistics
if ($result.Records.Count -ne 3 -or $result.Summaries[0].Case -ne 'AStar/100' -or
    $result.Summaries[0].Blocks -ne 1 -or $stats.Frames -ne 64 -or
    $stats.MedianMs -ne 1 -or $stats.MaximumMs -ne 65 -or
    [Math]::Abs($stats.ObservedP99Ms - 24.68) -gt 0.000001 -or
    $stats.Over2Ms -ne 1 -or $stats.Over31_25Ms -ne 1 -or
    $stats.TotalAllocatedBytes -ne 16 -or $stats.MaximumFrameAllocatedBytes -ne 16 -or
    $stats.CollectionOverlapFrames -ne 1 -or $block.Blocks -ne 1 -or
    $block.MedianMs -ne 128 -or $result.StageValidation -ne 'BenchmarkMetadata') {
    throw 'Incorrect historical-shape, stage separation, Int64, or block statistics.'
}

# Distinct instrumented method identities map to distinct modes and may replay differently.
$lines = [System.Collections.Generic.List[string]]::new()
foreach ($entry in @(
    @('Simulate64GuidedFramesInstrumentedUnphased', 'Unphased', 11L),
    @('Simulate64GuidedFramesInstrumentedBalancedPhases', 'BalancedPhases', 22L),
    @('Simulate64GuidedFramesInstrumentedUnbalancedPhase', 'UnbalancedPhase', 33L))) {
    $instrumented = New-Sample
    Add-Instrumentation $instrumented $entry[1]
    $instrumented.checksums = @([long]$entry[2]) * 64
    $lines.Add((Get-Header $entry[0]))
    foreach ($line in (Get-CompleteLaunch $instrumented)) { $lines.Add($line) }
}
$result = Invoke-Parser $lines.ToArray()
if (($result.Summaries.Case -join ',') -ne
    'BalancedPhases/AStar/100,UnbalancedPhase/AStar/100,Unphased/AStar/100') {
    throw 'Instrumented modes were pooled or mapped incorrectly.'
}
foreach ($summary in $result.Summaries) {
    $expectedPeriodicFrames = if ($summary.Mode -eq 'BalancedPhases') { 64 } else { 4 }
    if ($summary.Periodic.Statistics.Frames -ne $expectedPeriodicFrames -or
        $summary.Ordinary.Statistics.Frames -ne (64 - $expectedPeriodicFrames) -or
        $summary.Launches.Count -ne 1 -or $summary.Launches[0].Records.Count -ne 1) {
        throw 'Recorded periodic counts or per-launch records were not retained.'
    }
}

# If the original and instrumented unphased controls coexist, their replay must match.
$original = New-Sample
$instrumented = New-Sample
Add-Instrumentation $instrumented 'Unphased'
$instrumented.checksums[63] = [long]::MinValue
$controlLines = @((Get-Header 'Simulate64GuidedFrames')) + (Get-CompleteLaunch $original) +
    @((Get-Header 'Simulate64GuidedFramesInstrumentedUnphased')) + (Get-CompleteLaunch $instrumented)
Invoke-Rejected $controlLines 'Unphased instrumentation replay differs from original'

# Metadata, schedule, replay, launch completeness, and child outcome rejection.
$bad = New-Sample
Add-Instrumentation $bad 'BalancedPhases'
$bad.mode = 'Unphased'
Invoke-Rejected (@((Get-Header 'Simulate64GuidedFramesInstrumentedBalancedPhases')) +
    (Get-CompleteLaunch $bad)) 'method-to-mode'

$bad = New-Sample
Add-Instrumentation $bad 'BalancedPhases'
$bad.periodicRayCounts[0]++
Invoke-Rejected (@((Get-Header 'Simulate64GuidedFramesInstrumentedBalancedPhases')) +
    (Get-CompleteLaunch $bad)) 'periodic ray schedule'

$bad = New-Sample
Add-Instrumentation $bad 'BalancedPhases'
$bad.phaseAssignments[17] = 3
Invoke-Rejected (@((Get-Header 'Simulate64GuidedFramesInstrumentedBalancedPhases')) +
    (Get-CompleteLaunch $bad)) 'phase assignment'

$bad = New-Sample
Add-Instrumentation $bad 'BalancedPhases'
$bad.mandatoryRayCount = 99
Invoke-Rejected (@((Get-Header 'Simulate64GuidedFramesInstrumentedBalancedPhases')) +
    (Get-CompleteLaunch $bad)) 'mandatory ray count'

$bad = New-Sample
$bad.ticks = @(1L) * 63
Invoke-Rejected (@((Get-Header 'Simulate64GuidedFrames')) + (Get-CompleteLaunch $bad)) 'Wrong ticks length'

$first = New-Sample
$firstLaunch = Get-CompleteLaunch $first 1 2
Invoke-Rejected (@((Get-Header 'Simulate64GuidedFrames' -WithCounts $false)) + $firstLaunch) 'Missing launch 2 of 2'

$badExit = Get-CompleteLaunch (New-Sample)
$badExit[-1] = '// Benchmark Process 1 has exited with code 7.'
Invoke-Rejected (@((Get-Header 'Simulate64GuidedFrames')) + $badExit) 'exited with code 7'

$missingExit = Get-CompleteLaunch (New-Sample)
$missingExit = $missingExit[0..($missingExit.Count - 2)]
Invoke-Rejected (@((Get-Header 'Simulate64GuidedFrames')) + $missingExit) 'missing a successful child exit'

$sample = New-Sample
$warm = Get-Record $sample
$sample.iteration++
$actual = Get-Record $sample
Invoke-Rejected @((Get-Header 'Simulate64GuidedFrames'), '// Launch: 1 / 1', $warm,
    'WorkloadWarmup 1: 1 op, 1.00 ns, 1 ns/op', '// BeforeActualRun', $actual,
    'WorkloadActual 1: 1 op, 1.00 ns, 1 ns/op', '// AfterActualRun',
    '// Benchmark Process 1 has exited with code 0.') 'diagnostic record'

# Exact replay is required only within a mode across invocations/launches.
$sample = New-Sample
$firstLaunch = Get-CompleteLaunch $sample 1 2
$sample.process = 2; $sample.iteration = 1; $sample.checksums[63]--
$secondLaunch = Get-CompleteLaunch $sample 2 2
Invoke-Rejected (@((Get-Header 'Simulate64GuidedFrames' -WithCounts $false)) + $firstLaunch + $secondLaunch) 'Replay differs'

# Without BDN count metadata, the parser accepts complete launches but labels count validation as controller-owned.
$sample = New-Sample
$result = Invoke-Parser (@((Get-Header 'Simulate64GuidedFrames' -WithCounts $false)) + (Get-CompleteLaunch $sample))
if ($result.StageValidation -ne 'ControllerRequired') { throw 'Missing BDN counts were not reported explicitly.' }

# Pooled frame statistics normalize each launch with its own stopwatch frequency.
$sample = New-Sample
$frequencyOne = Get-CompleteLaunch $sample 1 2
$sample.process = 2; $sample.iteration = 1; $sample.stopwatchFrequency = 2000
$sample.ticks = @(2L) * 64
$frequencyTwo = Get-CompleteLaunch $sample 2 2
$result = Invoke-Parser (@((Get-Header 'Simulate64GuidedFrames' -WithCounts $false)) + $frequencyOne + $frequencyTwo)
if ($result.Summaries[0].Statistics.MedianMs -ne 1 -or
    $result.Summaries[0].Statistics.MaximumMs -ne 1) {
    throw 'Pooled statistics did not normalize each sample stopwatch frequency.'
}

# Every announced case must contain a launch, launch indices stay within 1..n,
# and a case header may occur only once in one capture.
$complete = @((Get-Header 'Simulate64GuidedFrames')) + (Get-CompleteLaunch (New-Sample))
Invoke-Rejected ($complete + @((Get-Header 'Simulate64GuidedFrames' 'FlowField'))) 'started benchmark case has no launch'

$flow = New-Sample 'FlowField'
$consecutiveHeaders = @((Get-Header 'Simulate64GuidedFrames'),
    (Get-Header 'Simulate64GuidedFrames' 'FlowField')) + (Get-CompleteLaunch $flow)
Invoke-Rejected $consecutiveHeaders 'started benchmark case has no launch'

$extra = New-Sample
$extra.process = 2
Invoke-Rejected ($complete + (Get-CompleteLaunch $extra 2 1)) 'outside declared range'

$outOfOrder = New-Sample
$second = Get-CompleteLaunch $outOfOrder 2 2
$outOfOrder.process = 2; $outOfOrder.iteration = 1
$first = Get-CompleteLaunch $outOfOrder 1 2
Invoke-Rejected (@((Get-Header 'Simulate64GuidedFrames' -WithCounts $false)) + $second + $first) 'exact 1..n order'

Invoke-Rejected ($complete + @((Get-Header 'Simulate64GuidedFrames'))) 'Duplicate benchmark header'

# Control scenes must remain disjoint from each other and from historical identities.
$controlLines = [System.Collections.Generic.List[string]]::new()
foreach ($control in @('Clear100', 'Short100', 'Late100', 'Ineligible100', 'Sparse500')) {
    foreach ($mode in @('Unphased', 'BalancedPhases')) {
        $count = if ($control -eq 'Sparse500') { 500 } else { 100 }
        $sample = New-Sample 'FlowField' $count
        Add-Instrumentation $sample $mode
        $sample.controlCase = $control
        $sample.checksums = @([long]$controlLines.Count) * 64
        $header = "// Benchmark: NavigationDirectVisibilityFrameBenchmarks.Simulate64Frames${mode}: Job-X(IterationCount=1, LaunchCount=1, WarmupCount=1) [Case=$control]"
        $controlLines.Add($header)
        foreach ($line in (Get-CompleteLaunch $sample)) { $controlLines.Add($line) }
    }
}
$result = Invoke-Parser $controlLines.ToArray()
if ($result.Summaries.Count -ne 10 -or
    @($result.Summaries | Where-Object Case -notlike 'Control/*').Count -ne 0) {
    throw 'Direct visibility controls were pooled or misidentified.'
}
foreach ($mutation in @('unknown', 'missing', 'mismatch', 'algorithm', 'count')) {
    $sample = New-Sample 'FlowField'
    Add-Instrumentation $sample 'Unphased'
    $sample.controlCase = 'Clear100'
    switch ($mutation) {
        'unknown' { $sample.controlCase = 'Unknown' }
        'missing' { $sample.Remove('controlCase') }
        'mismatch' { $sample.controlCase = 'Short100' }
        'algorithm' { $sample.algorithm = 'AStar' }
        'count' { $sample.agents = 500 }
    }
    Invoke-Rejected (@($controlLines[0]) + (Get-CompleteLaunch $sample)) 'control metadata'
}
Invoke-Rejected @($controlLines[0].Replace('Clear100', 'Unknown')) 'Unsupported benchmark header'
Invoke-Rejected @($controlLines[0].Replace('Simulate64FramesUnphased', 'Simulate64FramesOther')) 'Unsupported benchmark header'
$sample = New-Sample
$sample.controlCase = 'Clear100'
Invoke-Rejected (@((Get-Header 'Simulate64GuidedFrames')) + (Get-CompleteLaunch $sample)) 'control metadata'

Write-Output "Guided parser checks passed; fixtures: $directory"

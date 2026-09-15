param(
    [Parameter(Mandatory = $true)][string]$LogPath,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$methodModes = @{
    Simulate64GuidedFrames = $null
    Simulate64GuidedFramesInstrumentedUnphased = 'Unphased'
    Simulate64GuidedFramesInstrumentedBalancedPhases = 'BalancedPhases'
    Simulate64GuidedFramesInstrumentedUnbalancedPhase = 'UnbalancedPhase'
}
$records = [System.Collections.Generic.List[object]]::new()
$pending = $null
$diagnostic = $false
$launch = 0
$currentCase = $null
$currentMode = $null
$currentControl = $null
$controlCounts = @{ Clear100 = 100; Short100 = 100; Late100 = 100; Ineligible100 = 100; Sparse500 = 500 }
$launchState = $null
$launchStates = [System.Collections.Generic.List[object]]::new()
$seenCases = [System.Collections.Generic.HashSet[string]]::new()
$seenLaunches = [System.Collections.Generic.HashSet[string]]::new()
$caseLaunchTotals = @{}
$replays = @{}
$expectedCounts = $null
$controllerValidationRequired = $false
$currentCaseHasLaunch = $false
$currentLaunchSequence = 0

function Test-Integer($Value) {
    return $Value -is [sbyte] -or $Value -is [byte] -or
        $Value -is [int16] -or $Value -is [uint16] -or
        $Value -is [int32] -or $Value -is [uint32] -or
        $Value -is [int64] -or $Value -is [uint64]
}

function Get-Percentile($Sorted, [double]$Fraction) {
    if ($Sorted.Length -eq 0) { return $null }
    $position = ($Sorted.Length - 1) * $Fraction
    $lower = [int][Math]::Floor($position)
    $upper = [int][Math]::Ceiling($position)
    return $Sorted[$lower] + ($Sorted[$upper] - $Sorted[$lower]) * ($position - $lower)
}

function Get-Statistics($Samples, [scriptblock]$IncludeFrame = { param($sample, $frame) $true }) {
    $milliseconds = [System.Collections.Generic.List[double]]::new()
    $bytes = 0L
    $maximumBytes = 0L
    $collections = 0
    foreach ($sample in $Samples) {
        for ($frame = 0; $frame -lt $sample.frames; $frame++) {
            if (!(& $IncludeFrame $sample $frame)) { continue }
            $milliseconds.Add([long]$sample.ticks[$frame] * 1000.0 / $sample.stopwatchFrequency)
            $bytes += [long]$sample.allocatedBytes[$frame]
            $maximumBytes = [Math]::Max($maximumBytes, [long]$sample.allocatedBytes[$frame])
            if ($sample.collectionMasks[$frame] -ne 0) { $collections++ }
        }
    }
    $sorted = $milliseconds.ToArray()
    [Array]::Sort($sorted)
    [pscustomobject]@{
        Frames = $sorted.Length
        MedianMs = if ($sorted.Length) { Get-Percentile $sorted 0.5 } else { $null }
        ObservedP95Ms = if ($sorted.Length) { Get-Percentile $sorted 0.95 } else { $null }
        ObservedP99Ms = if ($sorted.Length) { Get-Percentile $sorted 0.99 } else { $null }
        MaximumMs = if ($sorted.Length) { $sorted[-1] } else { $null }
        Over2Ms = @($sorted | Where-Object { $_ -gt 2 }).Count
        Over31_25Ms = @($sorted | Where-Object { $_ -gt 31.25 }).Count
        TotalAllocatedBytes = $bytes
        MaximumFrameAllocatedBytes = $maximumBytes
        CollectionOverlapFrames = $collections
    }
}

function Get-BlockStatistics($Samples) {
    $milliseconds = [System.Collections.Generic.List[double]]::new()
    $records = [System.Collections.Generic.List[object]]::new()
    foreach ($sample in $Samples) {
        $sum = 0L
        foreach ($tick in $sample.ticks) { $sum += [long]$tick }
        $value = $sum * 1000.0 / $sample.stopwatchFrequency
        $milliseconds.Add($value)
        $records.Add([pscustomobject]@{ Process = $sample.process; Iteration = $sample.iteration; SumMs = $value })
    }
    $sorted = $milliseconds.ToArray()
    [Array]::Sort($sorted)
    [pscustomobject]@{
        Blocks = $sorted.Length
        MedianMs = if ($sorted.Length) { Get-Percentile $sorted 0.5 } else { $null }
        ObservedP95Ms = if ($sorted.Length) { Get-Percentile $sorted 0.95 } else { $null }
        ObservedP99Ms = if ($sorted.Length) { Get-Percentile $sorted 0.99 } else { $null }
        MaximumMs = if ($sorted.Length) { $sorted[-1] } else { $null }
        Records = $records.ToArray()
    }
}

function Assert-IntegerArray($Sample, [string]$Name, [int]$Length, [long]$Minimum, [long]$Maximum) {
    if ($Sample.$Name.Count -ne $Length) { throw "Wrong $Name length." }
    foreach ($value in $Sample.$Name) {
        if (!(Test-Integer $value)) { throw "Non-integer $Name value." }
        if ([long]$value -lt $Minimum -or [long]$value -gt $Maximum) { throw "Invalid $Name value." }
    }
}

function Get-ExpectedPhaseCount($Assignments, [int]$Remainder) {
    $count = 0
    foreach ($phase in $Assignments) {
        if ($phase -eq $Remainder) { $count++ }
    }
    return $count
}

function Assert-InstrumentedMetadata($Sample, [string]$Mode) {
    foreach ($name in @('mode', 'intervalFrames', 'phaseAssignments', 'clockOrigin',
        'mandatoryRayCount', 'preparationRayCounts', 'periodicRayCounts',
        'firstAdmissionTicks', 'firstAdmissionAllocatedBytes', 'firstAdmissionCollectionMask')) {
        if ($Sample.PSObject.Properties.Name -notcontains $name) { throw "Missing instrumented metadata: $name." }
    }
    if ($Sample.mode -ne $Mode) { throw 'Instrumented method-to-mode mapping disagrees with payload.' }
    if (!(Test-Integer $Sample.intervalFrames) -or $Sample.intervalFrames -ne 16 -or
        !(Test-Integer $Sample.clockOrigin) -or $Sample.clockOrigin -lt 32 -or
        !(Test-Integer $Sample.mandatoryRayCount) -or $Sample.mandatoryRayCount -ne $Sample.agents -or
        !(Test-Integer $Sample.firstAdmissionTicks) -or $Sample.firstAdmissionTicks -lt 0 -or
        !(Test-Integer $Sample.firstAdmissionAllocatedBytes) -or $Sample.firstAdmissionAllocatedBytes -lt 0 -or
        !(Test-Integer $Sample.firstAdmissionCollectionMask) -or $Sample.firstAdmissionCollectionMask -lt 0 -or
        $Sample.firstAdmissionCollectionMask -gt 7) {
        throw 'Invalid instrumented scalar metadata or mandatory ray count.'
    }
    if ($Sample.phaseAssignments.Count -ne $Sample.agents) { throw 'Wrong phase assignment length.' }
    for ($agent = 0; $agent -lt $Sample.agents; $agent++) {
        $phase = $Sample.phaseAssignments[$agent]
        $valid = switch ($Mode) {
            'Unphased' { $null -eq $phase }
            'BalancedPhases' { (Test-Integer $phase) -and $phase -eq ($agent % 16) }
            'UnbalancedPhase' { (Test-Integer $phase) -and $phase -eq 0 }
        }
        if (!$valid) { throw "Invalid phase assignment at agent $agent." }
    }
    Assert-IntegerArray $Sample 'preparationRayCounts' 32 0 $Sample.agents
    Assert-IntegerArray $Sample 'periodicRayCounts' 64 0 $Sample.agents
    if ($Sample.preparationRayCounts[0] -ne $Sample.mandatoryRayCount) {
        throw 'Invalid mandatory ray count in preparation frame zero.'
    }
    for ($frame = 0; $frame -lt 32; $frame++) {
        $expected = 0
        if ($frame -eq 0) { $expected = $Sample.agents }
        elseif ($Mode -eq 'Unphased') {
            if ($frame -eq 16) { $expected = $Sample.agents }
        }
        else {
            $remainder = [int](($Sample.clockOrigin - 32 + $frame) % 16)
            $expected = Get-ExpectedPhaseCount $Sample.phaseAssignments $remainder
        }
        if ($Sample.preparationRayCounts[$frame] -ne $expected) {
            throw "Invalid preparation ray schedule at frame $frame."
        }
    }
    $periodicTotal = 0
    for ($frame = 0; $frame -lt 64; $frame++) {
        if ($Mode -eq 'Unphased') {
            $expected = if ($frame % 16 -eq 0) { $Sample.agents } else { 0 }
        }
        else {
            $remainder = [int](($Sample.clockOrigin + $frame) % 16)
            $expected = Get-ExpectedPhaseCount $Sample.phaseAssignments $remainder
        }
        if ($Sample.periodicRayCounts[$frame] -ne $expected) {
            throw "Invalid periodic ray schedule at frame $frame."
        }
        $periodicTotal += [int]$Sample.periodicRayCounts[$frame]
    }
    if ($periodicTotal -ne 4 * $Sample.agents) {
        throw 'Instrumented block does not contain exactly four periodic rays per agent.'
    }
}

function Add-Sample($Sample, [string]$Stage, [int]$Index, [double]$OperationNanoseconds) {
    if ($null -ne $currentControl) {
        if ($Sample.controlCase -ne $currentControl -or $Sample.algorithm -ne 'FlowField' -or
            $Sample.agents -ne $controlCounts[$currentControl]) { throw 'Invalid control metadata.' }
    }
    elseif ($Sample.PSObject.Properties.Name -contains 'controlCase') { throw 'Unexpected control metadata.' }
    if ($Sample.frames -ne 64 -or $Sample.preparationFrames -ne 32 -or $Sample.frameRate -ne 32 -or
        !(Test-Integer $Sample.stopwatchFrequency) -or $Sample.stopwatchFrequency -le 0 -or
        !(Test-Integer $Sample.agents) -or $Sample.agents -notin @(100, 500) -or
        $Sample.algorithm -notin @('AStar', 'FlowField') -or $launch -le 0) {
        throw 'Invalid guided-frame scenario metadata.'
    }
    Assert-IntegerArray $Sample 'ticks' $Sample.frames 0 ([long]::MaxValue)
    Assert-IntegerArray $Sample 'allocatedBytes' $Sample.frames 0 ([long]::MaxValue)
    Assert-IntegerArray $Sample 'collectionMasks' $Sample.frames 0 7
    Assert-IntegerArray $Sample 'checksums' $Sample.frames ([long]::MinValue) ([long]::MaxValue)
    if ($null -eq $currentMode) {
        if ($Sample.PSObject.Properties.Name -contains 'mode') {
            throw 'Original payload unexpectedly contains instrumented metadata.'
        }
    }
    else { Assert-InstrumentedMetadata $Sample $currentMode }
    $case = if ($null -eq $currentMode) {
        "$($Sample.algorithm)/$($Sample.agents)"
    } else { "$currentMode/$($Sample.algorithm)/$($Sample.agents)" }
    if ($null -ne $currentControl) { $case = "Control/$currentControl/$case" }
    if ($case -ne $currentCase) { throw 'Payload case metadata disagrees with the benchmark header.' }
    if (!(Test-Integer $Sample.process) -or $Sample.process -le 0 -or
        !(Test-Integer $Sample.iteration) -or $Sample.iteration -ne $launchState.Iteration + 1) {
        throw 'Invalid or duplicate invocation identity.'
    }
    if ($launchState.Process -ne 0 -and $Sample.process -ne $launchState.Process) { throw 'Launch process changed.' }
    if ($Stage -eq 'Warmup' -and ($launchState.Actual -ne 0 -or $launchState.Diagnostic -ne 0 -or
        $Index -ne $launchState.Warmup + 1) -or
        $Stage -eq 'Actual' -and ($launchState.Diagnostic -ne 0 -or $Index -ne $launchState.Actual + 1) -or
        $Stage -eq 'Diagnostic' -and $launchState.Actual -eq 0) {
        throw 'Invalid or duplicate BDN stage ordering.'
    }
    $launchState.Process = $Sample.process
    $launchState.Iteration = $Sample.iteration
    if ($Stage -eq 'Diagnostic') { $launchState.Diagnostic++ } else { $launchState.$Stage = $Index }
    $checksumSequence = $Sample.checksums -join ','
    if ($replays.ContainsKey($case) -and $replays[$case] -ne $checksumSequence) {
        throw "Replay differs across invocations or launches: $case."
    }
    $replays[$case] = $checksumSequence
    $records.Add([pscustomobject]@{
        Case = $case; Mode = $currentMode; Launch = $launch; Stage = $Stage; StageIndex = $Index
        WholeOperationNanoseconds = $OperationNanoseconds; Sample = $Sample
    })
}

foreach ($line in [System.IO.File]::ReadAllLines((Resolve-Path -LiteralPath $LogPath))) {
    if ($line.StartsWith('// Benchmark:')) {
        if ($null -ne $pending) { throw 'Unmatched sample at benchmark boundary.' }
        if ($null -ne $launchState -and !$launchState.ExitSucceeded) {
            throw 'Previous launch is missing a successful child exit.'
        }
        if ($null -ne $currentCase -and !$currentCaseHasLaunch) {
            throw 'Started benchmark case has no launch.'
        }
        $currentControl = $null
        if ($line -match '^// Benchmark: NavigationDirectVisibilityFrameBenchmarks\.Simulate64Frames(Unphased|BalancedPhases): .+ \[Case=(Clear100|Short100|Late100|Ineligible100|Sparse500)\]$') {
            $currentMode = $Matches[1]
            $currentControl = $Matches[2]
            $currentCase = "Control/$currentControl/$currentMode/FlowField/$($controlCounts[$currentControl])"
        }
        elseif ($line -match '^// Benchmark: NavigationGuidedFrameBenchmarks\.([A-Za-z0-9]+): .+ \[AgentCount=(100|500), Algorithm=(AStar|FlowField)\]$') {
            $method = $Matches[1]
            if (!$methodModes.ContainsKey($method)) { throw 'Unsupported guided-frame benchmark method.' }
            $currentMode = $methodModes[$method]
            $currentCase = if ($null -eq $currentMode) { "$($Matches[3])/$($Matches[2])" }
                else { "$currentMode/$($Matches[3])/$($Matches[2])" }
        }
        else {
            throw 'Unsupported benchmark header.'
        }
        if (!$seenCases.Add($currentCase)) { throw 'Duplicate benchmark header.' }
        $currentCaseHasLaunch = $false
        $currentLaunchSequence = 0
        $launch = 0
        $launchState = $null
        $diagnostic = $false
        if ($line -match 'LaunchCount=(\d+)' -and $line -match 'WarmupCount=(\d+)' -and
            $line -match 'IterationCount=(\d+)') {
            $launchCount = [regex]::Match($line, 'LaunchCount=(\d+)').Groups[1].Value
            $warmupCount = [regex]::Match($line, 'WarmupCount=(\d+)').Groups[1].Value
            $iterationCount = [regex]::Match($line, 'IterationCount=(\d+)').Groups[1].Value
            $expectedCounts = [pscustomobject]@{
                Launch = [int]$launchCount; Warmup = [int]$warmupCount; Actual = [int]$iterationCount
            }
        }
        else {
            $expectedCounts = $null
            $controllerValidationRequired = $true
        }
        continue
    }
    if ($line -match '^// Launch: (\d+) / (\d+)$') {
        if ($null -ne $pending) { throw 'Unmatched sample at benchmark/launch boundary.' }
        if ($null -ne $launchState -and !$launchState.ExitSucceeded) {
            throw 'Previous launch is missing a successful child exit.'
        }
        $launch = [int]$Matches[1]
        $launchTotal = [int]$Matches[2]
        if ($null -eq $currentCase) { throw 'Missing benchmark or duplicate launch.' }
        if ($launch -lt 1 -or $launch -gt $launchTotal) {
            throw 'Launch index is outside declared range.'
        }
        if ($launch -ne $currentLaunchSequence + 1) {
            throw 'Launch sequence is not in exact 1..n order.'
        }
        if (!$seenLaunches.Add("$currentCase/$launch")) {
            throw 'Missing benchmark or duplicate launch.'
        }
        if (($null -ne $expectedCounts -and $launchTotal -ne $expectedCounts.Launch) -or
            ($caseLaunchTotals.ContainsKey($currentCase) -and $caseLaunchTotals[$currentCase] -ne $launchTotal)) {
            throw 'Launch total disagrees with benchmark metadata.'
        }
        $caseLaunchTotals[$currentCase] = $launchTotal
        $currentCaseHasLaunch = $true
        $currentLaunchSequence = $launch
        $launchState = [pscustomobject]@{
            Case = $currentCase; Launch = $launch; ExpectedLaunches = $launchTotal
            ExpectedCounts = $expectedCounts; Process = 0; Iteration = 0
            Warmup = 0; Actual = 0; Diagnostic = 0; ExitSucceeded = $false
        }
        $launchStates.Add($launchState)
        $diagnostic = $false
        continue
    }
    if ($line -eq '// BeforeActualRun') { $diagnostic = $false; continue }
    if ($line -eq '// AfterActualRun') { $diagnostic = $true; continue }
    if ($line.StartsWith('NAVIGATION_GUIDED_SAMPLES ')) {
        if ($null -eq $launchState) { throw 'Nested record has no launch.' }
        if ($null -ne $pending) { throw 'Two nested records without a BDN measurement.' }
        $sample = $line.Substring('NAVIGATION_GUIDED_SAMPLES '.Length) | ConvertFrom-Json
        if ($diagnostic) { Add-Sample $sample 'Diagnostic' 0 0 } else { $pending = $sample }
        continue
    }
    if ($line -match '^Workload(Warmup|Actual)\s+(\d+):\s+1 op,\s+([0-9.]+) ns,') {
        if ($null -eq $pending) { throw 'BDN measurement has no nested record.' }
        Add-Sample $pending $Matches[1] ([int]$Matches[2]) ([double]::Parse($Matches[3], [Globalization.CultureInfo]::InvariantCulture))
        $pending = $null
        continue
    }
    if ($line -match '^// Benchmark Process (\d+) has exited with code (-?\d+)\.$') {
        if ($null -eq $launchState -or $launchState.ExitSucceeded) { throw 'Child exit has no open launch.' }
        $exitCode = [int]$Matches[2]
        if ($exitCode -ne 0) { throw "Benchmark child exited with code $exitCode." }
        if ($launchState.Process -ne 0 -and [int]$Matches[1] -ne $launchState.Process) {
            throw 'Child exit process disagrees with nested records.'
        }
        $launchState.ExitSucceeded = $true
    }
}
if ($null -ne $pending) { throw 'Unmatched nested record at end of log.' }
if ($null -ne $currentCase -and !$currentCaseHasLaunch) { throw 'Started benchmark case has no launch.' }
foreach ($state in $launchStates) {
    if (!$state.ExitSucceeded) { throw "Launch $($state.Launch) is missing a successful child exit." }
    if ($state.Actual -eq 0) { throw "Launch $($state.Launch) has no actual record." }
    if ($state.Diagnostic -eq 0) { throw "Launch $($state.Launch) has no diagnostic record." }
    if ($null -ne $state.ExpectedCounts -and
        ($state.Warmup -ne $state.ExpectedCounts.Warmup -or $state.Actual -ne $state.ExpectedCounts.Actual)) {
        throw "Launch $($state.Launch) stage counts disagree with BDN metadata."
    }
}
foreach ($case in $caseLaunchTotals.Keys) {
    $total = $caseLaunchTotals[$case]
    for ($index = 1; $index -le $total; $index++) {
        if (!$seenLaunches.Contains("$case/$index")) { throw "Missing launch $index of $total for $case." }
    }
}
$unphasedMismatch =
    ($replays.ContainsKey('Unphased/AStar/100') -and $replays.ContainsKey('AStar/100') -and
        $replays['Unphased/AStar/100'] -ne $replays['AStar/100']) -or
    ($replays.ContainsKey('Unphased/FlowField/100') -and $replays.ContainsKey('FlowField/100') -and
        $replays['Unphased/FlowField/100'] -ne $replays['FlowField/100']) -or
    ($replays.ContainsKey('Unphased/FlowField/500') -and $replays.ContainsKey('FlowField/500') -and
        $replays['Unphased/FlowField/500'] -ne $replays['FlowField/500'])
if ($unphasedMismatch) {
    throw 'Unphased instrumentation replay differs from original.'
}
$actual = @($records | Where-Object Stage -eq 'Actual')
if ($actual.Count -eq 0) { throw 'No actual frame records found.' }
$summaries = foreach ($case in ($actual | Group-Object Case | Sort-Object Name)) {
    $samples = @($case.Group.Sample)
    $instrumented = $null -ne $case.Group[0].Mode
    [pscustomobject]@{
        Case = $case.Name
        Mode = $case.Group[0].Mode
        Blocks = $case.Count
        Statistics = Get-Statistics $samples
        BlockStatistics = Get-BlockStatistics $samples
        Periodic = if ($instrumented) {
            [pscustomobject]@{ Statistics = Get-Statistics $samples { param($sample, $frame) $sample.periodicRayCounts[$frame] -gt 0 } }
        } else { $null }
        Ordinary = if ($instrumented) {
            [pscustomobject]@{ Statistics = Get-Statistics $samples { param($sample, $frame) $sample.periodicRayCounts[$frame] -eq 0 } }
        } else { $null }
        Launches = @(foreach ($run in ($case.Group | Group-Object Launch | Sort-Object { [int]$_.Name })) {
            $runSamples = @($run.Group.Sample)
            [pscustomobject]@{
                Launch = [int]$run.Name
                Blocks = $run.Count
                Statistics = Get-Statistics $runSamples
                BlockStatistics = Get-BlockStatistics $runSamples
                Records = @($run.Group | Select-Object StageIndex, WholeOperationNanoseconds,
                    @{Name='NestedFrameSumMs';Expression={(($_.Sample.ticks | Measure-Object -Sum).Sum * 1000.0 / $_.Sample.stopwatchFrequency)}})
            }
        })
    }
}
[pscustomobject]@{
    SourceLog = (Resolve-Path -LiteralPath $LogPath).Path
    StageValidation = if ($controllerValidationRequired) { 'ControllerRequired' } else { 'BenchmarkMetadata' }
    StageCounts = @($records | Group-Object Stage | Select-Object Name, Count)
    Summaries = @($summaries)
    Records = $records.ToArray()
} | ConvertTo-Json -Depth 16 | Set-Content -LiteralPath $OutputPath
$summaries | Select-Object Case, Blocks, @{Name='Frames';Expression={$_.Statistics.Frames}},
    @{Name='MedianMs';Expression={$_.Statistics.MedianMs}},
    @{Name='P99Ms';Expression={$_.Statistics.ObservedP99Ms}},
    @{Name='MaxMs';Expression={$_.Statistics.MaximumMs}},
    @{Name='Over2Ms';Expression={$_.Statistics.Over2Ms}},
    @{Name='Bytes';Expression={$_.Statistics.TotalAllocatedBytes}} | Format-Table

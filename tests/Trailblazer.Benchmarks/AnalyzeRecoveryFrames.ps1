param(
    [Parameter(Mandatory = $true)][string]$LogPath,
    [Parameter(Mandatory = $true)][string]$OutputPath,
    [switch]$Smoke,
    [ValidateRange(1, 3)][int]$ExpectedDirectLaunches = 3,
    [ValidateRange(1, 3)][int]$FirstDirectLaunch = 1,
    [ValidateSet('All', 'Flow', 'Flow100', 'Flow500', 'AStar100')][string]$ExpectedPopulations = 'All',
    [ValidateSet('Immediate', 'Scheduled')][string]$ExpectedMode = 'Immediate',
    [string]$ExpectedWorkLimits = 'Default',
    [switch]$DiagnosticControls
)

$ErrorActionPreference = 'Stop'
$records = [System.Collections.Generic.List[object]]::new()
$pending = $null
$diagnostic = $false
$launch = 0
$exits = 0
$directLaunches = [System.Collections.Generic.HashSet[int]]::new()
$replays = @{}
$settingsByArm = @{}
$expectedCases = if ($DiagnosticControls) { 6 } else {
    switch ($ExpectedPopulations) { 'All' { 15 } 'Flow' { 10 } default { 5 } }
}
$expectedWorkLabel = if ($ExpectedWorkLimits -eq 'Default') {
    'default-work-limits-with-workload-sized-retention'
} else { 'explicit:' + $ExpectedWorkLimits }
$expectedBudget = [ordered]@{
    MaxLookupProbes = 10240000; MaxEndpointCandidates = 16; MaxExpandedNodes = 10000
    MaxEvaluatedEdges = 320000; MaxConnectionLegs = 0; MaxTransitionCandidates = 0
    MaxTransitionPairs = 0; MaxStagedLegAttempts = 0; MaxTraceIntervals = 160000
    MaxCoveredVoxelIntervals = 320000; MaxSimplificationRays = 1024
}
if ($FirstDirectLaunch + $ExpectedDirectLaunches - 1 -gt 3) {
    throw 'Direct recovery launch range must stay within 1..3.'
}

function Read-Sample($sample, [string]$stage, [int]$index, [double]$operationNanoseconds) {
    $diagnosticName = $sample.diagnostic
    if ($DiagnosticControls) {
        if ($diagnosticName -notin @('MixedFlowKeys', 'UncoveredFlowOrigin', 'RetentionPressure8', 'RetentionPressure100') -or
            $sample.algorithm -ne 'FlowField' -or $sample.agents -ne 100 -or $sample.episode -ne 'ColdInitial' -or
            ($diagnosticName -like 'RetentionPressure*' -and $sample.acquisitionMode -ne 'Scheduled')) {
            throw 'Unrecognized recovery diagnostic identity.'
        }
    } elseif ($diagnosticName) { throw 'Diagnostic recovery record cannot pool with ordinary cases.' }
    if ($sample.algorithm -notin @('AStar', 'FlowField') -or $sample.agents -notin @(100, 500) -or
        ($sample.algorithm -eq 'AStar' -and $sample.agents -eq 500) -or
        $sample.episode -notin @('ColdInitial', 'WarmCompatible', 'SameMapRebake', 'ProfileReplacement', 'Restore')) {
        throw 'Unrecognized recovery population/episode.'
    }
    if ($sample.schema -ne 1 -or $sample.frames -le 0 -or $sample.frames -gt 10000 -or $sample.stopwatchFrequency -le 0) {
        throw 'Invalid recovery metadata.'
    }
    $population = $(if ($sample.algorithm -eq 'AStar') { 'AStar' } else { 'Flow' }) + $sample.agents
    if ($ExpectedPopulations -ne 'All' -and $ExpectedPopulations -ne $population -and
        !($ExpectedPopulations -eq 'Flow' -and $sample.algorithm -eq 'FlowField')) {
        throw 'Recovery population differs from the explicitly selected diagnostic scope.'
    }
    foreach ($name in @('ticks', 'allocatedBytes', 'collectionMasks', 'checksums', 'readyCounts')) {
        if (@($sample.$name).Count -ne $sample.frames) { throw "Invalid $name length." }
    }
    if (@($sample.readyFrames).Count -ne $sample.agents -or @($sample.failedAgents).Count -ne $sample.agents) {
        throw 'Invalid per-original-agent record length.'
    }
    if (!$sample.replayMatches -or $sample.ready -ne $sample.agents -or $sample.failures -ne 0 -or $sample.cancellations -ne 0 -or $sample.censored -ne 0) {
        throw 'Failed, cancelled, censored or divergent recovery episode is not an accepted capture.'
    }
    if ($sample.episode -eq 'ColdInitial' -and (!$sample.freshContextPerEpisode -or $sample.cacheCountAtTrigger -ne 0)) {
        throw 'Cold episode was not cache cold.'
    }
    if ($sample.episode -eq 'WarmCompatible' -and $sample.warmOriginsVerified -ne $sample.agents) {
        throw 'Warm episode did not prove compatible coverage for every origin.'
    }
    $followerReuses = if ($sample.acquisitionCounters.PSObject.Properties.Name -contains 'sharedFollowerCheckouts') {
        [long]$sample.acquisitionCounters.sharedFollowerCheckouts
    } else { 0L }
    if ($followerReuses -lt 0 -or ($sample.acquisitionMode -eq 'Immediate' -and $followerReuses -ne 0)) {
        throw 'Invalid follower reuse accounting.'
    }
    if ($sample.episode -eq 'WarmCompatible' -and ($sample.acquisitionCounters.buildStarts -ne 0 -or
        $sample.acquisitionCounters.cacheCheckoutHits + $followerReuses -ne $sample.agents)) {
        throw 'Warm episode rebuilt instead of reusing exact compatible coverage.'
    }
    foreach ($ready in $sample.readyFrames) {
        if ($ready -lt 1 -or $ready -gt $sample.frames) { throw 'Invalid readiness frame.' }
    }
    foreach ($mask in $sample.collectionMasks) {
        if ($mask -lt 0 -or $mask -gt 7) { throw 'Invalid GC overlap mask.' }
    }
    $workLimits = if ($sample.workLimits) { $sample.workLimits } else { 'default-work-limits-with-workload-sized-retention' }
    $key = "$($sample.acquisitionMode)|$workLimits|$($sample.algorithm)|$($sample.agents)|$($sample.episode)"
    if ($DiagnosticControls) {
        if ($workLimits -ne 'explicit:65536,128,4096,8192,64,256') {
            throw 'Diagnostic recovery work limits differ from selected B.'
        }
        $key = "$diagnosticName|$key"
        if (!$sample.originalDestinationsIntact -or @($sample.originalStartCells).Count -ne 100 -or
            @($sample.originalDestinationCells).Count -ne 100) {
            throw "Diagnostic original-agent mapping is missing or changed: $key"
        }
        for ($i = 0; $i -lt 100; $i++) {
            $expectedStart = if ($diagnosticName -eq 'UncoveredFlowOrigin' -and $i -eq 0) { '48,0,94' }
                else { "$(2 + $i % 25),0,$(2 + [Math]::Floor($i / 25) * 2)" }
            $expectedEnd = if ($diagnosticName -eq 'MixedFlowKeys' -and ([Math]::Floor($i / 25) % 2) -eq 1) {
                '98,0,60'
            } else { '98,0,50' }
            if ($sample.originalStartCells[$i] -ne $expectedStart -or $sample.originalDestinationCells[$i] -ne $expectedEnd) {
                throw "Diagnostic original-agent mapping differs at index $i : $key"
            }
        }
        if ($diagnosticName -eq 'MixedFlowKeys' -and ($sample.acquisitionCounters.buildStarts -ne 2 -or
            $sample.acquisitionCounters.cacheCheckoutHits + $followerReuses -ne 98 -or
            $sample.acquisitionMode -eq 'Scheduled' -and $sample.acquisitionCounters.cacheCheckoutHits -lt 2)) {
            throw 'Mixed Flow diagnostic did not build/reuse two independent exact keys.'
        }
        if ($diagnosticName -eq 'UncoveredFlowOrigin' -and $sample.acquisitionMode -eq 'Scheduled' -and
            ($sample.acquisitionCounters.sharedCoverageMisses -le 0 -or $sample.acquisitionCounters.buildStarts -le 1)) {
            throw 'Scheduled uncovered-origin diagnostic missed its coverage/build precondition.'
        }
        if ($diagnosticName -like 'RetentionPressure*') {
            if (@($sample.retainedRequestCounts).Count -ne $sample.frames -or
                @($sample.capacityStatusCounts).Count -ne $sample.frames -or
                $sample.distinctCapacityStatusAgents -lt $(if ($diagnosticName -eq 'RetentionPressure8') { 1 } else { 0 })) {
                throw 'Retention diagnostic lacks complete per-frame pressure observations.'
            }
            $limit = if ($diagnosticName -eq 'RetentionPressure8') { 8 } else { 100 }
            if (@($sample.retainedRequestCounts | Where-Object { $_ -lt 0 -or $_ -gt $limit }).Count -gt 0) {
                throw 'Retention diagnostic exceeded its request ceiling.'
            }
        }
    }
    foreach ($setting in @('queryBudget', 'schedulerSettings', 'queryLimits')) {
        if ($null -eq $sample.$setting -or @($sample.$setting.PSObject.Properties).Count -eq 0) {
            throw "Missing concrete recovery $setting."
        }
    }
    foreach ($entry in $expectedBudget.GetEnumerator()) {
        if ($sample.queryBudget.($entry.Key) -ne $entry.Value) {
            throw "Concrete recovery query budget differs from original control: $key"
        }
    }
    $expectedDescriptorBytes = [Math]::Max(65536, $sample.agents * 512)
    $expectedRetained = if ($diagnosticName -eq 'RetentionPressure8') { 8 } else { $sample.agents }
    if ($sample.schedulerSettings.MaxRetainedRequests -ne $expectedRetained -or
        $sample.schedulerSettings.MaxRetainedDescriptorBytes -ne $expectedDescriptorBytes -or
        $sample.queryLimits.MaxConcurrentNavigationQueries -ne 1 -or
        $sample.queryLimits.MaxFlowActivePayloadLeases -ne $sample.agents -or
        $sample.queryLimits.MaxAStarActivePayloadLeases -ne $sample.agents) {
        throw "Recovery retention or active-lease settings differ from the original population control: $key"
    }
    if ($workLimits -like 'explicit:*') {
        $parts = @($workLimits.Substring('explicit:'.Length).Split(','))
        if ($parts.Count -ne 6) { throw "Invalid explicit recovery work limits: $key" }
        $names = @('LookupStepLimit', 'EndpointCandidateStepLimit', 'NodeStepLimit',
            'EdgeStepLimit', 'ConnectionStepLimit', 'OperationStepLimit')
        for ($i = 0; $i -lt 6; $i++) {
            $expected = 0
            if (![int]::TryParse($parts[$i], [ref]$expected) -or $sample.schedulerSettings.($names[$i]) -ne $expected) {
                throw "Concrete scheduler work limits differ from label: $key"
            }
        }
    } elseif ($workLimits -eq 'default-work-limits-with-workload-sized-retention') {
        if ($sample.schedulerSettings.LookupStepLimit -ne 8192 -or
            $sample.schedulerSettings.EndpointCandidateStepLimit -ne 1 -or
            $sample.schedulerSettings.NodeStepLimit -ne 256 -or
            $sample.schedulerSettings.EdgeStepLimit -ne 256 -or
            $sample.schedulerSettings.ConnectionStepLimit -ne 64 -or
            ($null -ne $sample.schedulerSettings.OperationStepLimit -and
                $sample.schedulerSettings.OperationStepLimit -ne 64)) {
            throw "Concrete scheduler default work limits differ from label: $key"
        }
    } else { throw "Unrecognized recovery work-limit label: $key" }
    $arm = "$diagnosticName|$($sample.acquisitionMode)|$workLimits|$($sample.algorithm)|$($sample.agents)"
    $settings = @($sample.queryBudget, $sample.schedulerSettings, $sample.queryLimits |
        ForEach-Object { $_ | ConvertTo-Json -Compress -Depth 20 }) -join '|'
    if ($settingsByArm.ContainsKey($arm) -and $settingsByArm[$arm] -ne $settings) {
        throw "Concrete recovery settings drift within arm: $arm"
    }
    $settingsByArm[$arm] = $settings
    $replay = @($sample.checksums) -join ','
    $replay += '|' + (@($sample.readyFrames) -join ',')
    if ($replays.ContainsKey($key) -and $replays[$key] -ne $replay) { throw "Cross-process replay diverged: $key" }
    $replays[$key] = $replay
    $milliseconds = @($sample.ticks | ForEach-Object { $_ * 1000.0 / $sample.stopwatchFrequency })
    $sorted = @($milliseconds | Sort-Object)
    $percentiles = @{}
    foreach ($percentile in @(50, 95, 99)) {
        $position = ($sorted.Count - 1) * $percentile / 100.0
        $lo = [int][Math]::Floor($position)
        $hi = [int][Math]::Ceiling($position)
        $percentiles[$percentile] = $sorted[$lo] + ($sorted[$hi] - $sorted[$lo]) * ($position - $lo)
    }
    [pscustomobject]@{
        Key = $key; Launch = $(if ($sample.captureStage -in @('Warmup', 'Actual')) { $sample.launch } else { $launch }); Stage = $stage; StageIndex = $index
        WholeOperationNanoseconds = $operationNanoseconds
        CompletionFrames = $sample.frames; WaitingSecondsAt32Hz = $sample.frames / 32.0
        MedianMs = $percentiles[50]; P95Ms = $percentiles[95]; P99Ms = $percentiles[99]; MaximumMs = $sorted[-1]
        Over2Ms = @($milliseconds | Where-Object { $_ -gt 2 }).Count
        Over31_25Ms = @($milliseconds | Where-Object { $_ -gt 31.25 }).Count
        FrameTotalMs = ($milliseconds | Measure-Object -Sum).Sum
        FrameAllocatedBytes = ($sample.allocatedBytes | Measure-Object -Sum).Sum
        CollectionOverlapFrames = @($sample.collectionMasks | Where-Object { $_ -ne 0 }).Count
        ProcessWideCpuMs = $sample.processWideCpuTicks * 1000.0 / $sample.processCpuFrequency
        Raw = $sample
    }
}

foreach ($line in Get-Content -LiteralPath $LogPath) {
    if ($line -match '^NAVIGATION_RECOVERY_DIAGNOSTIC_COMPLETE launch=(\d+) cases=(\d+) warmup=(\d+) actual=(\d+)$') {
        if (!$DiagnosticControls -or [int]$Matches[2] -ne 6 -or [int]$Matches[3] -ne 6 -or [int]$Matches[4] -ne 18) {
            throw 'Diagnostic completion marker does not match six fixed controls.'
        }
        if (!$directLaunches.Add([int]$Matches[1])) { throw 'Duplicate diagnostic launch completion.' }
    }
    if ($line -match '^NAVIGATION_RECOVERY_CAPTURE_COMPLETE launch=(\d+) cases=(\d+) warmup=(\d+) actual=(\d+)$') {
        if ($DiagnosticControls) { throw 'Ordinary completion marker cannot satisfy diagnostic controls.' }
        if ([int]$Matches[2] -ne $expectedCases -or [int]$Matches[3] -ne $expectedCases -or [int]$Matches[4] -ne 3 * $expectedCases) {
            throw 'Direct completion marker does not match the explicitly selected case scope.'
        }
        if (!$directLaunches.Add([int]$Matches[1])) { throw 'Duplicate direct capture launch completion.' }
    }
    if ($line.StartsWith('// Benchmark:')) {
        if ($null -ne $pending) { throw 'Unmatched recovery record at benchmark boundary.' }
        $diagnostic = $false
    }
    if ($line -match '^// Launch: (\d+) /') { $launch = [int]$Matches[1]; $diagnostic = $false }
    if ($line -eq '// BeforeActualRun') { $diagnostic = $false }
    if ($line -eq '// AfterActualRun') { $diagnostic = $true }
    if ($line -match '^// Benchmark Process .* exited with code (.+)\.$') {
        if ($Matches[1] -ne '0') { throw "Failed child exit: $line" }
        $exits++
    }
    if ($line.StartsWith('NAVIGATION_RECOVERY_SAMPLES ')) {
        if ($null -ne $pending) { throw 'Two records without an intervening BDN measurement.' }
        $sample = $line.Substring('NAVIGATION_RECOVERY_SAMPLES '.Length) | ConvertFrom-Json
        if ($Smoke) { $records.Add((Read-Sample $sample 'Smoke' $sample.iteration 0)) }
        elseif ($sample.captureStage -in @('Warmup', 'Actual')) {
            $records.Add((Read-Sample $sample $sample.captureStage ($sample.iteration - 1) 0))
        }
        elseif ($diagnostic) { $records.Add((Read-Sample $sample 'Diagnostic' 0 0)) }
        else { $pending = $sample }
    }
    if ($line -match '^Workload(Jitting|Pilot|Warmup|Actual)\s+(\d+):\s+1 op,\s+([0-9.]+) ns,') {
        if ($null -eq $pending) {
            if ($Matches[1] -eq 'Jitting') { continue }
            throw 'BDN measurement has no recovery record.'
        }
        $stage = $Matches[1]
        $index = [int]$Matches[2]
        $nanoseconds = [double]::Parse($Matches[3], [Globalization.CultureInfo]::InvariantCulture)
        $records.Add((Read-Sample $pending $stage $index $nanoseconds))
        $pending = $null
    }
}
if ($null -ne $pending) { throw 'Unmatched recovery record at end of log.' }
if ($records.Count -eq 0) { throw 'No recovery records.' }
if (!$Smoke -and (($exits -eq 0 -and $directLaunches.Count -eq 0) -or !($records | Where-Object Stage -eq 'Actual'))) {
    throw 'Missing actual observations or successful child exits.'
}
if ($directLaunches.Count -gt 0) {
    if ($directLaunches.Count -ne $ExpectedDirectLaunches -or $records.Count -ne 4 * $expectedCases * $ExpectedDirectLaunches) {
        throw "Expected $ExpectedDirectLaunches complete direct launches / $(4 * $expectedCases * $ExpectedDirectLaunches) records."
    }
    foreach ($expectedLaunch in $FirstDirectLaunch..($FirstDirectLaunch + $ExpectedDirectLaunches - 1)) {
        if (!$directLaunches.Contains($expectedLaunch)) { throw "Missing direct launch $expectedLaunch." }
    }
    foreach ($directLaunch in $directLaunches) {
        $groups = @($records | Where-Object Launch -eq $directLaunch | Group-Object Key)
        $populations = switch ($ExpectedPopulations) {
            'All' { @('AStar100', 'Flow100', 'Flow500') }
            'Flow' { @('Flow100', 'Flow500') }
            default { @($ExpectedPopulations) }
        }
        $expectedKeys = if ($DiagnosticControls) {
            @(
                'MixedFlowKeys|Immediate', 'MixedFlowKeys|Scheduled',
                'UncoveredFlowOrigin|Immediate', 'UncoveredFlowOrigin|Scheduled',
                'RetentionPressure8|Scheduled', 'RetentionPressure100|Scheduled'
            ) | ForEach-Object { "$_|explicit:65536,128,4096,8192,64,256|FlowField|100|ColdInitial" }
        } else { foreach ($population in $populations) {
            $algorithm = if ($population -eq 'AStar100') { 'AStar' } else { 'FlowField' }
            $count = if ($population -eq 'Flow500') { 500 } else { 100 }
            foreach ($episode in @('ColdInitial', 'WarmCompatible', 'SameMapRebake', 'ProfileReplacement', 'Restore')) {
                "$ExpectedMode|$expectedWorkLabel|$algorithm|$count|$episode"
            }
        } }
        $actualCaseSet = @($groups.Name | Sort-Object) -join "`n"
        $expectedCaseSet = @($expectedKeys | Sort-Object) -join "`n"
        if ($actualCaseSet -ne $expectedCaseSet) {
            throw "Direct launch $directLaunch has the wrong mode/work-limit/population/episode case set."
        }
        foreach ($group in $groups) {
            if (@($group.Group | Where-Object Stage -eq 'Warmup').Count -ne 1 -or @($group.Group | Where-Object Stage -eq 'Actual').Count -ne 3) {
                throw "Direct launch $directLaunch has an incomplete episode protocol."
            }
        }
    }
    foreach ($record in $records) {
        if (!$directLaunches.Contains([int]$record.Launch)) { throw 'Record belongs to unfinished direct launch.' }
    }
}
[pscustomobject]@{
    SourceLog = (Resolve-Path -LiteralPath $LogPath).Path
    SuccessfulChildExits = $exits
    CompletedDirectLaunches = @($directLaunches | Sort-Object)
    DirectProtocolScope = $(if ($Smoke) { 'Smoke' } elseif ($directLaunches.Count -eq 0) { 'BenchmarkDotNet' }
        elseif (!$DiagnosticControls -and $FirstDirectLaunch -eq 1 -and $ExpectedDirectLaunches -eq 3 -and $ExpectedPopulations -eq 'All') { 'FormalThreeLaunch' } else { 'BoundedDiagnostic' })
    StageCounts = @($records | Group-Object Key, Launch, Stage | Select-Object Name, Count)
    Records = $records.ToArray()
    AcceptanceNote = 'Check all intended cases, 3 launches, 1 warmup and 3 actual episodes per launch against BDN JSON; nested frame percentiles are descriptive and correlated.'
} | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath $OutputPath
$records | Where-Object { $_.Stage -eq 'Actual' -or $_.Stage -eq 'Smoke' } |
    Select-Object Key, Launch, StageIndex, CompletionFrames, MaximumMs, FrameTotalMs, ProcessWideCpuMs | Format-Table

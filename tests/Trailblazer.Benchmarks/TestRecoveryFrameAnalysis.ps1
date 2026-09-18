$ErrorActionPreference = 'Stop'
$temporary = Join-Path ([IO.Path]::GetTempPath()) ('trailblazer-recovery-parser-' + [Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temporary) | Out-Null
$log = Join-Path $temporary 'input.log'
$output = Join-Path $temporary 'summary.json'
$analyzer = Join-Path $PSScriptRoot 'AnalyzeRecoveryFrames.ps1'

function New-Sample([string]$algorithm, [int]$agents, [string]$episode, [int]$launch, [int]$iteration) {
    [ordered]@{
        schema = 1; frames = 1; stopwatchFrequency = 10000000; processCpuFrequency = 10000000
        algorithm = $algorithm; agents = $agents; episode = $episode; acquisitionMode = 'Immediate'
        captureStage = $(if ($iteration -eq 1) { 'Warmup' } else { 'Actual' }); launch = $launch; iteration = $iteration
        ticks = @(20000); allocatedBytes = @(0); collectionMasks = @(0); checksums = @(1234)
        readyCounts = @($agents); readyFrames = @(1) * $agents; failedAgents = @($false) * $agents
        ready = $agents; failures = 0; cancellations = 0; censored = 0; replayMatches = $true
        freshContextPerEpisode = $true; cacheCountAtTrigger = 0; warmOriginsVerified = $agents
        acquisitionCounters = @{ buildStarts = 0; cacheCheckoutHits = $agents }; processWideCpuTicks = 0
        queryBudget = [ordered]@{ MaxLookupProbes = 10240000; MaxEndpointCandidates = 16
            MaxExpandedNodes = 10000; MaxEvaluatedEdges = 320000; MaxConnectionLegs = 0
            MaxTransitionCandidates = 0; MaxTransitionPairs = 0; MaxStagedLegAttempts = 0
            MaxTraceIntervals = 160000; MaxCoveredVoxelIntervals = 320000; MaxSimplificationRays = 1024 }
        schedulerSettings = [ordered]@{ MaxRetainedRequests = $agents
            MaxRetainedDescriptorBytes = [Math]::Max(65536, $agents * 512)
            LookupStepLimit = 8192; EndpointCandidateStepLimit = 1; NodeStepLimit = 256
            EdgeStepLimit = 256; ConnectionStepLimit = 64; OperationStepLimit = 64 }
        queryLimits = [ordered]@{ MaxBatchItems = 1; MaxBatchDescriptorBytes = 65536
            MaxConcurrentNavigationQueries = 1; AStarWorkspaceMapCapacity = 1
            AStarWorkspaceEndpointPageCapacity = 159; AStarWorkspaceComponentCapacity = 1
            AStarWorkspaceNodeCapacity = 10000; MaxAStarCacheEntries = $agents
            MaxAStarReusablePayloadBytes = 272925600; MaxAStarSinglePayloadBytes = 2729256
            MaxAStarActivePayloadBytes = 2729256 * $agents; MaxAStarActivePayloadLeases = $agents
            FlowWorkspaceMapCapacity = 1; FlowWorkspaceEndpointPageCapacity = 159
            FlowWorkspaceComponentCapacity = 1; FlowWorkspaceNodeCapacity = 10000
            RayWorkspaceCoveredAddressCapacity = 10000; RayWorkspaceTraceIntervalCapacity = 10000
            AStarWorkspaceGuidePointCapacity = 19999; MaxFlowCacheEntries = $agents
            MaxFlowReusablePayloadBytes = 236925600; MaxFlowSinglePayloadBytes = 2369256
            MaxFlowActivePayloadBytes = 2369256 * $agents; MaxFlowActivePayloadLeases = $agents }
    }
}

function Invoke-Parser([string[]]$lines, [bool]$reject = $false, [int]$expectedLaunches = 3,
    [string]$populations = 'All', [string]$mode = 'Immediate', [string]$workLimits = 'Default',
    [bool]$diagnosticControls = $false, [int]$firstLaunch = 1, [string]$expectedError = '') {
    [IO.File]::WriteAllLines($log, $lines)
    $rejected = $false
    try { & $analyzer -LogPath $log -OutputPath $output -ExpectedDirectLaunches $expectedLaunches -FirstDirectLaunch $firstLaunch -ExpectedPopulations $populations -ExpectedMode $mode -ExpectedWorkLimits $workLimits -DiagnosticControls:$diagnosticControls | Out-Null }
    catch {
        $rejected = $true
        if (!$reject) { throw }
        if ($expectedError -and $_.Exception.Message -ne $expectedError) {
            throw "Expected parser error '$expectedError', got '$($_.Exception.Message)'."
        }
    }
    if ($rejected -ne $reject) { throw "Parser rejection expected=$reject, actual=$rejected" }
}

function New-DiagnosticSample([string]$diagnostic, [string]$mode, [int]$iteration) {
    $sample = New-Sample 'FlowField' 100 'ColdInitial' 1 $iteration
    $sample.diagnostic = $diagnostic
    $sample.acquisitionMode = $mode
    $sample.workLimits = 'explicit:65536,128,4096,8192,64,256'
    $sample.schedulerSettings.LookupStepLimit = 65536
    $sample.schedulerSettings.EndpointCandidateStepLimit = 128
    $sample.schedulerSettings.NodeStepLimit = 4096
    $sample.schedulerSettings.EdgeStepLimit = 8192
    $sample.schedulerSettings.OperationStepLimit = 256
    $sample.originalDestinationsIntact = $true
    $sample.originalStartCells = @(for ($i = 0; $i -lt 100; $i++) {
        if ($diagnostic -eq 'UncoveredFlowOrigin' -and $i -eq 0) { '48,0,94' }
        else { "$(2 + $i % 25),0,$(2 + [Math]::Floor($i / 25) * 2)" }
    })
    $sample.originalDestinationCells = @(for ($i = 0; $i -lt 100; $i++) {
        if ($diagnostic -eq 'MixedFlowKeys' -and ([Math]::Floor($i / 25) % 2) -eq 1) { '98,0,60' }
        else { '98,0,50' }
    })
    $sample.acquisitionCounters.buildStarts = if ($diagnostic -eq 'MixedFlowKeys' -or
        ($diagnostic -eq 'UncoveredFlowOrigin' -and $mode -eq 'Scheduled')) { 2 } else { 1 }
    $sample.acquisitionCounters.cacheCheckoutHits = if ($mode -eq 'Immediate') { 100 - $sample.acquisitionCounters.buildStarts } else { 0 }
    $sample.acquisitionCounters.sharedFollowerCheckouts = if ($mode -eq 'Scheduled') { 100 - $sample.acquisitionCounters.buildStarts } else { 0 }
    if ($diagnostic -eq 'MixedFlowKeys' -and $mode -eq 'Scheduled') {
        $sample.acquisitionCounters.cacheCheckoutHits = 2
        $sample.acquisitionCounters.sharedFollowerCheckouts = 96
    }
    $sample.acquisitionCounters.sharedCoverageMisses = if ($diagnostic -eq 'UncoveredFlowOrigin' -and $mode -eq 'Scheduled') { 1 } else { 0 }
    if ($diagnostic -like 'RetentionPressure*') {
        $sample.schedulerSettings.MaxRetainedRequests = if ($diagnostic -eq 'RetentionPressure8') { 8 } else { 100 }
        $sample.retainedRequestCounts = @($sample.schedulerSettings.MaxRetainedRequests)
        $sample.capacityStatusCounts = @(if ($diagnostic -eq 'RetentionPressure8') { 92 } else { 0 })
        $sample.distinctCapacityStatusAgents = $sample.capacityStatusCounts[0]
    }
    return $sample
}

try {
    $lines = [Collections.Generic.List[string]]::new()
    foreach ($launch in 1..3) {
        foreach ($population in @(@('AStar', 100), @('FlowField', 100), @('FlowField', 500))) {
            foreach ($episode in @('ColdInitial', 'WarmCompatible', 'SameMapRebake', 'ProfileReplacement', 'Restore')) {
                foreach ($iteration in 1..4) {
                    $sample = New-Sample $population[0] $population[1] $episode $launch $iteration
                    $lines.Add('NAVIGATION_RECOVERY_SAMPLES ' + ($sample | ConvertTo-Json -Compress -Depth 8))
                }
            }
        }
        $lines.Add("NAVIGATION_RECOVERY_CAPTURE_COMPLETE launch=$launch cases=15 warmup=15 actual=45")
    }
    Invoke-Parser $lines.ToArray()
    $summary = Get-Content -Raw $output | ConvertFrom-Json
    if ($summary.Records.Count -ne 180 -or @($summary.Records | Where-Object Stage -eq 'Actual').Count -ne 135) {
        throw 'Direct actual/warmup stage counts are incorrect.'
    }
    $scheduled = foreach ($line in $lines) {
        if (!$line.StartsWith('NAVIGATION_RECOVERY_SAMPLES ')) { $line; continue }
        $sample = $line.Substring('NAVIGATION_RECOVERY_SAMPLES '.Length) | ConvertFrom-Json -AsHashtable
        $sample.acquisitionMode = 'Scheduled'
        $sample.workLimits = 'default-work-limits-with-workload-sized-retention'
        $sample.acquisitionCounters.cacheCheckoutHits = 1
        $sample.acquisitionCounters.sharedFollowerCheckouts = $sample.agents - 1
        'NAVIGATION_RECOVERY_SAMPLES ' + ($sample | ConvertTo-Json -Compress -Depth 8)
    }
    Invoke-Parser $scheduled -mode Scheduled
    $flowOnly = foreach ($line in $scheduled) {
        if ($line.StartsWith('NAVIGATION_RECOVERY_CAPTURE_COMPLETE ')) {
            $line.Replace('cases=15 warmup=15 actual=45', 'cases=10 warmup=10 actual=30')
        } elseif (!$line.Contains('"algorithm":"AStar"')) { $line }
    }
    Invoke-Parser $flowOnly -reject $true -mode Scheduled
    Invoke-Parser $flowOnly -populations Flow -mode Scheduled
    $flowSummary = Get-Content -Raw $output | ConvertFrom-Json
    if ($flowSummary.Records.Count -ne 120 -or $flowSummary.DirectProtocolScope -ne 'BoundedDiagnostic') {
        throw 'Explicit Flow subset must retain all records and remain diagnostic.'
    }
    $bad = [string[]]$scheduled.Clone()
    $bad[4] = $bad[4].Replace('"sharedFollowerCheckouts":99', '"sharedFollowerCheckouts":100')
    if ($bad[4] -eq $scheduled[4]) { throw 'Scheduled follower fixture did not change its counter.' }
    Invoke-Parser $bad -reject $true -mode Scheduled -expectedError 'Warm episode rebuilt instead of reusing exact compatible coverage.'
    Invoke-Parser ($lines.ToArray() | Select-Object -SkipLast 1) -reject $true
    Invoke-Parser ($lines.ToArray() | Select-Object -First 61) -reject $true
    Invoke-Parser ($lines.ToArray() | Select-Object -First 61) -expectedLaunches 1
    if ((Get-Content -Raw $output | ConvertFrom-Json).DirectProtocolScope -ne 'BoundedDiagnostic') {
        throw 'A deliberately partial direct capture must not be labeled formal.'
    }
    $secondLaunch = @($lines.ToArray() | Select-Object -First 61 | ForEach-Object { $_.Replace('"launch":1', '"launch":2').Replace('launch=1 cases=', 'launch=2 cases=') })
    Invoke-Parser $secondLaunch -expectedLaunches 1 -firstLaunch 2
    $bad = $lines.ToArray()
    $bad[0] = $bad[0].Replace('"cacheCountAtTrigger":0', '"cacheCountAtTrigger":1')
    Invoke-Parser $bad -reject $true
    $bad = $lines.ToArray()
    $bad[61] = $bad[61].Replace('"checksums":[1234]', '"checksums":[9999]')
    Invoke-Parser $bad -reject $true

    # A substituted case retains 15 groups and 180 records, but is not the requested Immediate set.
    $bad = [string[]]$lines.ToArray().Clone()
    foreach ($index in 0..3) {
        $sample = $bad[$index].Substring('NAVIGATION_RECOVERY_SAMPLES '.Length) | ConvertFrom-Json -AsHashtable
        $sample.acquisitionMode = 'Scheduled'
        $bad[$index] = 'NAVIGATION_RECOVERY_SAMPLES ' + ($sample | ConvertTo-Json -Compress -Depth 8)
    }
    Invoke-Parser $bad -reject $true

    # Concrete drift cannot hide beneath the same textual case key.
    foreach ($field in @('queryBudget', 'schedulerSettings', 'queryLimits')) {
        $bad = [string[]]$lines.ToArray().Clone()
        $sample = $bad[1].Substring('NAVIGATION_RECOVERY_SAMPLES '.Length) | ConvertFrom-Json -AsHashtable
        switch ($field) {
            'queryBudget' { $sample.queryBudget.MaxExpandedNodes = 9999 }
            'schedulerSettings' { $sample.schedulerSettings.MaxRetainedRequests = 8 }
            'queryLimits' { $sample.queryLimits.MaxFlowReusablePayloadBytes++ }
        }
        $bad[1] = 'NAVIGATION_RECOVERY_SAMPLES ' + ($sample | ConvertTo-Json -Compress -Depth 8)
        Invoke-Parser $bad -reject $true
    }
    # Even uniform drift must not masquerade as the ordinary default setting.
    $bad = foreach ($line in $lines) {
        if (!$line.StartsWith('NAVIGATION_RECOVERY_SAMPLES ')) { $line; continue }
        $sample = $line.Substring('NAVIGATION_RECOVERY_SAMPLES '.Length) | ConvertFrom-Json -AsHashtable
        $sample.schedulerSettings.LookupStepLimit = 9999
        'NAVIGATION_RECOVERY_SAMPLES ' + ($sample | ConvertTo-Json -Compress -Depth 8)
    }
    Invoke-Parser $bad -reject $true
    $bad = foreach ($line in $lines) {
        if (!$line.StartsWith('NAVIGATION_RECOVERY_SAMPLES ')) { $line; continue }
        $sample = $line.Substring('NAVIGATION_RECOVERY_SAMPLES '.Length) | ConvertFrom-Json -AsHashtable
        $sample.queryBudget.MaxExpandedNodes = 9999
        'NAVIGATION_RECOVERY_SAMPLES ' + ($sample | ConvertTo-Json -Compress -Depth 8)
    }
    Invoke-Parser $bad -reject $true

    $sample = New-Sample 'FlowField' 100 'ColdInitial' 0 1
    $sample.captureStage = 'BenchmarkDotNet'
    $record = 'NAVIGATION_RECOVERY_SAMPLES ' + ($sample | ConvertTo-Json -Compress -Depth 8)
    Invoke-Parser @('// Benchmark: recovery', '// Launch: 1 / 3',
        'OverheadJitting 1: 1 op, 20.00 ns,', 'WorkloadJitting 1: 1 op, 30.00 ns,',
        $record, 'WorkloadPilot 1: 1 op, 20000.00 ns,',
        $record, 'WorkloadWarmup 1: 1 op, 20000.00 ns,',
        $record, 'WorkloadActual 1: 1 op, 20000.00 ns,',
        'WorkloadResult 1: 1 op, 20000.00 ns,', '// AfterActualRun', $record,
        '// Benchmark Process 123 has exited with code 0.')
    $summary = Get-Content -Raw $output | ConvertFrom-Json
    if (($summary.Records.Stage -join ',') -ne 'Pilot,Warmup,Actual,Diagnostic') {
        throw 'BDN stage pairing incorrectly included overhead/JIT/result duplicates.'
    }

    $diagnostics = [Collections.Generic.List[string]]::new()
    foreach ($case in @(@('MixedFlowKeys', 'Immediate'), @('MixedFlowKeys', 'Scheduled'),
        @('UncoveredFlowOrigin', 'Immediate'), @('UncoveredFlowOrigin', 'Scheduled'),
        @('RetentionPressure8', 'Scheduled'), @('RetentionPressure100', 'Scheduled'))) {
        foreach ($iteration in 1..4) {
            $diagnostics.Add('NAVIGATION_RECOVERY_SAMPLES ' +
                ((New-DiagnosticSample $case[0] $case[1] $iteration) | ConvertTo-Json -Compress -Depth 8))
        }
    }
    $diagnostics.Add('NAVIGATION_RECOVERY_DIAGNOSTIC_COMPLETE launch=1 cases=6 warmup=6 actual=18')
    Invoke-Parser $diagnostics.ToArray() -expectedLaunches 1 -diagnosticControls $true
    $summary = Get-Content -Raw $output | ConvertFrom-Json
    if ($summary.Records.Count -ne 24 -or $summary.DirectProtocolScope -ne 'BoundedDiagnostic' -or
        @($summary.Records | Group-Object Key).Count -ne 6) {
        throw 'Six diagnostic identities were not retained separately.'
    }
    Invoke-Parser $diagnostics.ToArray() -reject $true -expectedLaunches 1
    $bad = [string[]]$diagnostics.ToArray().Clone()
    $sample = $bad[16].Substring('NAVIGATION_RECOVERY_SAMPLES '.Length) | ConvertFrom-Json -AsHashtable
    $sample.retainedRequestCounts = @(9)
    $bad[16] = 'NAVIGATION_RECOVERY_SAMPLES ' + ($sample | ConvertTo-Json -Compress -Depth 8)
    Invoke-Parser $bad -reject $true -expectedLaunches 1 -diagnosticControls $true

    Write-Output 'Recovery parser checks passed: exact direct cases/settings, baseline compatibility, six separate diagnostic controls, incomplete/cold/replay rejection, BDN stage separation.'
}
finally {
    # The sole deletion target is the exact GUID-named directory created above.
    if ((Split-Path -Leaf $temporary) -like 'trailblazer-recovery-parser-*') {
        Remove-Item -LiteralPath $temporary -Recurse -Force
    }
}

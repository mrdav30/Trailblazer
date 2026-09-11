param(
    [Parameter(Mandatory = $true)][string]$LogPath,
    [Parameter(Mandatory = $true)][string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$records = [System.Collections.Generic.List[object]]::new()
$pending = $null
$diagnostic = $false
$launch = 0
$currentCase = $null
$launchState = $null
$seenLaunches = [System.Collections.Generic.HashSet[string]]::new()
$replays = @{}

function Get-Statistics($Samples) {
    $milliseconds = [System.Collections.Generic.List[double]]::new()
    $bytes = 0L
    $maximumBytes = 0L
    $collections = 0
    foreach ($sample in $Samples) {
        for ($frame = 0; $frame -lt $sample.frames; $frame++) {
            $milliseconds.Add($sample.ticks[$frame] * 1000.0 / $sample.stopwatchFrequency)
            $bytes += $sample.allocatedBytes[$frame]
            $maximumBytes = [Math]::Max($maximumBytes, $sample.allocatedBytes[$frame])
            if ($sample.collectionMasks[$frame] -ne 0) { $collections++ }
        }
    }
    $sorted = $milliseconds.ToArray()
    [Array]::Sort($sorted)
    $percentiles = foreach ($fraction in @(0.5, 0.95, 0.99)) {
        $position = ($sorted.Length - 1) * $fraction
        $lower = [int][Math]::Floor($position)
        $upper = [int][Math]::Ceiling($position)
        $sorted[$lower] + ($sorted[$upper] - $sorted[$lower]) * ($position - $lower)
    }
    [pscustomobject]@{
        Frames = $sorted.Length
        MedianMs = $percentiles[0]; ObservedP95Ms = $percentiles[1]; ObservedP99Ms = $percentiles[2]
        MaximumMs = $sorted[-1]
        Over2Ms = @($sorted | Where-Object { $_ -gt 2 }).Count
        Over31_25Ms = @($sorted | Where-Object { $_ -gt 31.25 }).Count
        TotalAllocatedBytes = $bytes; MaximumFrameAllocatedBytes = $maximumBytes
        CollectionOverlapFrames = $collections
    }
}

function Add-Sample($Sample, [string]$Stage, [int]$Index, [double]$OperationNanoseconds) {
    if ($Sample.frames -ne 64 -or $Sample.preparationFrames -ne 32 -or $Sample.frameRate -ne 32 -or
        $Sample.stopwatchFrequency -le 0 -or $Sample.agents -notin @(100, 500) -or
        $Sample.algorithm -notin @('AStar', 'FlowField') -or $launch -le 0) {
        throw 'Invalid guided-frame scenario metadata.'
    }
    foreach ($name in @('ticks', 'allocatedBytes', 'collectionMasks', 'checksums')) {
        if ($Sample.$name.Count -ne $Sample.frames) { throw "Wrong $name length." }
        foreach ($value in $Sample.$name) {
            if ($value -isnot [long] -and $value -isnot [int]) { throw "Non-integer $name value." }
            if ($name -ne 'checksums' -and $value -lt 0) { throw "Negative $name value." }
            if ($name -eq 'collectionMasks' -and $value -gt 7) { throw 'Invalid collection mask.' }
        }
    }
    $case = "$($Sample.algorithm)/$($Sample.agents)"
    if ($case -ne $currentCase) { throw 'Payload case metadata disagrees with the benchmark header.' }
    if ($Sample.process -le 0 -or $Sample.iteration -ne $launchState.Iteration + 1) {
        throw 'Invalid or duplicate invocation identity.'
    }
    if ($launchState.Process -ne 0 -and $Sample.process -ne $launchState.Process) { throw 'Launch process changed.' }
    if ($Stage -eq 'Warmup' -and ($launchState.Actual -ne 0 -or $Index -ne $launchState.Warmup + 1) -or
        $Stage -eq 'Actual' -and ($launchState.Diagnostic -or $Index -ne $launchState.Actual + 1) -or
        $Stage -eq 'Diagnostic' -and ($launchState.Diagnostic -or $launchState.Actual -eq 0)) {
        throw 'Invalid or duplicate BDN stage ordering.'
    }
    $launchState.Process = $Sample.process
    $launchState.Iteration = $Sample.iteration
    if ($Stage -eq 'Diagnostic') { $launchState.Diagnostic = $true } else { $launchState.$Stage = $Index }
    $checksumSequence = $Sample.checksums -join ','
    if ($replays.ContainsKey($case) -and $replays[$case] -ne $checksumSequence) {
        throw "Replay differs across invocations or launches: $case."
    }
    $replays[$case] = $checksumSequence
    $records.Add([pscustomobject]@{
        Case = $case; Launch = $launch; Stage = $Stage; StageIndex = $Index
        WholeOperationNanoseconds = $OperationNanoseconds; Sample = $Sample
    })
}

foreach ($line in [System.IO.File]::ReadAllLines((Resolve-Path -LiteralPath $LogPath))) {
    if ($line.StartsWith('// Benchmark:')) {
        if ($null -ne $pending) { throw 'Unmatched sample at benchmark boundary.' }
        if ($line -notmatch '\[AgentCount=(100|500), Algorithm=(AStar|FlowField)\]$') { throw 'Unsupported benchmark header.' }
        $currentCase = "$($Matches[2])/$($Matches[1])"
        $launch = 0
        $launchState = $null
        $diagnostic = $false
    }
    if ($line -match '^// Launch: (\d+) /') {
        if ($null -ne $pending) { throw 'Unmatched sample at benchmark/launch boundary.' }
        $launch = [int]$Matches[1]
        if ($null -eq $currentCase -or !$seenLaunches.Add("$currentCase/$launch")) { throw 'Missing benchmark or duplicate launch.' }
        $launchState = [pscustomobject]@{ Process = 0; Iteration = 0; Warmup = 0; Actual = 0; Diagnostic = $false }
        $diagnostic = $false
    }
    if ($line -eq '// BeforeActualRun') { $diagnostic = $false }
    if ($line -eq '// AfterActualRun') { $diagnostic = $true }
    if ($line.StartsWith('NAVIGATION_GUIDED_SAMPLES ')) {
        if ($null -ne $pending) { throw 'Two nested records without a BDN measurement.' }
        $sample = $line.Substring('NAVIGATION_GUIDED_SAMPLES '.Length) | ConvertFrom-Json
        if ($diagnostic) { Add-Sample $sample 'Diagnostic' 0 0 } else { $pending = $sample }
    }
    if ($line -match '^Workload(Warmup|Actual)\s+(\d+):\s+1 op,\s+([0-9.]+) ns,') {
        if ($null -eq $pending) { throw 'BDN measurement has no nested record.' }
        Add-Sample $pending $Matches[1] ([int]$Matches[2]) ([double]::Parse($Matches[3], [Globalization.CultureInfo]::InvariantCulture))
        $pending = $null
    }
}
if ($null -ne $pending) { throw 'Unmatched nested record at end of log.' }
$actual = @($records | Where-Object Stage -eq 'Actual')
if ($actual.Count -eq 0) { throw 'No actual frame records found.' }
$summaries = foreach ($case in ($actual | Group-Object Case)) {
    [pscustomobject]@{
        Case = $case.Name; Blocks = $case.Count
        Statistics = Get-Statistics @($case.Group.Sample)
        Launches = @(foreach ($run in ($case.Group | Group-Object Launch)) {
            [pscustomobject]@{ Launch = [int]$run.Name; Blocks = $run.Count
                Statistics = Get-Statistics @($run.Group.Sample) }
        })
    }
}
[pscustomobject]@{
    SourceLog = (Resolve-Path -LiteralPath $LogPath).Path
    StageCounts = @($records | Group-Object Stage | Select-Object Name, Count)
    Summaries = @($summaries)
    Records = $records.ToArray()
} | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath
$summaries | Select-Object Case, Blocks, @{Name='Frames';Expression={$_.Statistics.Frames}},
    @{Name='MedianMs';Expression={$_.Statistics.MedianMs}},
    @{Name='P99Ms';Expression={$_.Statistics.ObservedP99Ms}},
    @{Name='MaxMs';Expression={$_.Statistics.MaximumMs}},
    @{Name='Over2Ms';Expression={$_.Statistics.Over2Ms}},
    @{Name='Bytes';Expression={$_.Statistics.TotalAllocatedBytes}} | Format-Table

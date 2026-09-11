# Behavior checks for the log parser; generated fixtures remain under artifacts.
$ErrorActionPreference = 'Stop'
$directory = Join-Path $PSScriptRoot "../../artifacts/guided-parser-tests/$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $directory -Force | Out-Null
$log = Join-Path $directory 'fixture.log'
$output = Join-Path $directory 'summary.json'
$parser = Join-Path $PSScriptRoot 'AnalyzeGuidedFrames.ps1'
$header = '// Benchmark: NavigationGuidedFrameBenchmarks.Simulate64GuidedFrames: Job [AgentCount=100, Algorithm=AStar]'
$sample = [ordered]@{
    process = 1; iteration = 1; algorithm = 'AStar'; agents = 100
    frameRate = 32; preparationFrames = 32; frames = 64; stopwatchFrequency = 1000
    ticks = @(1L) * 64; allocatedBytes = @(0L) * 64
    collectionMasks = @(0) * 64; checksums = @(42L) * 64
}

function Get-Record { 'NAVIGATION_GUIDED_SAMPLES ' + ($sample | ConvertTo-Json -Compress) }
function Invoke-Rejected([string[]]$Lines, [string]$ExpectedMessage) {
    @($header) + $Lines | Set-Content -LiteralPath $log
    $rejected = $false
    try { & $parser -LogPath $log -OutputPath $output | Out-Null }
    catch {
        if ($_.Exception.Message -notlike "*$ExpectedMessage*") { throw }
        $rejected = $true
    }
    if (!$rejected) { throw "Parser accepted malformed input: $ExpectedMessage" }
}

# Large warmup/diagnostic frames must not pollute actual frame statistics.
$sample.ticks = @(1000L) * 64
$warm = Get-Record
$sample.iteration = 2
$sample.ticks = @(1L) * 63 + @(65L)
$sample.allocatedBytes[63] = 16L
$sample.collectionMasks[63] = 1
$actual = Get-Record
$sample.iteration = 3
$sample.ticks = @(2000L) * 64
$diagnostic = Get-Record
@($header, '// Launch: 1 / 1', '// BeforeActualRun', $warm,
    'WorkloadWarmup 1: 1 op, 64000000000.00 ns, 64 s/op', $actual,
    'WorkloadActual 1: 1 op, 128000000.00 ns, 128 ms/op', '// AfterActualRun', $diagnostic,
    'WorkloadResult 1: 1 op, 128000000.00 ns, 128 ms/op') | Set-Content -LiteralPath $log
& $parser -LogPath $log -OutputPath $output | Out-Null
$result = Get-Content -Raw -LiteralPath $output | ConvertFrom-Json
$stats = $result.Summaries[0].Statistics
if ($result.Records.Count -ne 3 -or $result.Summaries[0].Blocks -ne 1 -or
    $stats.Frames -ne 64 -or $stats.MedianMs -ne 1 -or $stats.MaximumMs -ne 65 -or
    [Math]::Abs($stats.ObservedP99Ms - 24.68) -gt 0.000001 -or
    $stats.Over2Ms -ne 1 -or $stats.Over31_25Ms -ne 1 -or
    $stats.TotalAllocatedBytes -ne 16 -or $stats.MaximumFrameAllocatedBytes -ne 16 -or
    $stats.CollectionOverlapFrames -ne 1) { throw 'Incorrect stage separation or frame statistics.' }

Invoke-Rejected @('// Launch: 1 / 1', $actual) 'Unmatched nested record at end'
Invoke-Rejected @('// Launch: 1 / 1', $actual, $actual) 'Two nested records'
Invoke-Rejected @('// Launch: 1 / 1', 'WorkloadActual 1: 1 op, 1.00 ns, 1 ns/op') 'no nested record'
$sample.iteration = 1
$first = Get-Record
Invoke-Rejected @('// Launch: 1 / 1', $first, 'WorkloadActual 1: 1 op, 1.00 ns, 1 ns/op',
    $first, 'WorkloadActual 1: 1 op, 1.00 ns, 1 ns/op') 'invocation identity'
$sample.process = 2
$sample.iteration = 2
Invoke-Rejected @('// Launch: 1 / 1', $first, 'WorkloadActual 1: 1 op, 1.00 ns, 1 ns/op',
    (Get-Record), 'WorkloadActual 2: 1 op, 1.00 ns, 1 ns/op') 'process changed'
$sample.process = 1
$sample.iteration = 1
$sample.agents = 500
Invoke-Rejected @('// Launch: 1 / 1', (Get-Record), 'WorkloadActual 1: 1 op, 1.00 ns, 1 ns/op') 'case metadata'
$sample.agents = 100
Invoke-Rejected @('// Launch: 1 / 1', $first, 'WorkloadActual 1: 1 op, 1.00 ns, 1 ns/op',
    $header, $first, 'WorkloadActual 1: 1 op, 1.00 ns, 1 ns/op') 'metadata'
$sample.ticks = @(1L) * 63
Invoke-Rejected @('// Launch: 1 / 1', (Get-Record), 'WorkloadActual 1: 1 op, 1.00 ns, 1 ns/op') 'Wrong ticks length'
$sample.ticks = @(1L) * 64
$sample.checksums[63] = 43L
Invoke-Rejected @('// Launch: 1 / 2', $first, 'WorkloadActual 1: 1 op, 1.00 ns, 1 ns/op',
    '// Launch: 2 / 2', (Get-Record), 'WorkloadActual 1: 1 op, 1.00 ns, 1 ns/op') 'Replay differs'
Write-Output 'Guided parser checks passed: actual-only statistics, duplicated BDN results, malformed/truncated records and cross-launch replay.'

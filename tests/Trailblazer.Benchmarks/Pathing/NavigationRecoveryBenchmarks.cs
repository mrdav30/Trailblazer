using System;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Chronicler.Diagnostics;
using Trailblazer.Navigation.Steering;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Complete Navigator acquisition/recovery episodes, not isolated lease acquisition.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(PerformanceGateConfig))]
[BenchmarkCategory("Graph", "Guidance", "Recovery")]
public class NavigationRecoveryBenchmarks
{
    private NavigationRecoveryScenario _scenario;
    private long[] _expectedHashes;
    private int[] _expectedReadyFrames;
    private int _iteration;
    internal string CaptureStage { get; set; } = "BenchmarkDotNet";
    internal int Launch { get; set; }
    internal string Diagnostic { get; set; }

    [Params("AStar100", "Flow100", "Flow500")]
    public string Population { get; set; }

    [Params("ColdInitial", "WarmCompatible", "SameMapRebake", "ProfileReplacement", "Restore")]
    public string Episode { get; set; }

    [Params(NavigationAcquisitionMode.Immediate, NavigationAcquisitionMode.Scheduled)]
    public NavigationAcquisitionMode AcquisitionMode { get; set; }

    [Params("Default")]
    public string WorkLimits { get; set; } = "Default";

    [IterationSetup]
    public void Prepare()
    {
        // A new context on EVERY invocation also excludes setup/smoke/warmup payload leakage.
        // All topology, publication, JSON preparation and initial lifecycle work is untimed.
        _scenario?.Dispose();
        _scenario = new NavigationRecoveryScenario(Population, Episode, AcquisitionMode, WorkLimits, Diagnostic);
    }

    [Benchmark]
    public long RecoverUntilUsable() => _scenario.Run();

    [IterationCleanup]
    public void ValidateAndExport()
    {
        long[] hashes = _scenario.FrameHashes;
        int[] ready = _scenario.ReadyFrames;
        bool replayMatches = _expectedHashes == null
            || hashes.AsSpan().SequenceEqual(_expectedHashes)
                && ready.AsSpan().SequenceEqual(_expectedReadyFrames);
        Console.WriteLine("NAVIGATION_RECOVERY_SAMPLES " + JsonSerializer.Serialize(
            _scenario.CreateRecord(++_iteration, replayMatches, CaptureStage, Launch)));
        _expectedHashes ??= hashes;
        _expectedReadyFrames ??= (int[])ready.Clone();
        ChronicleGuard.ThrowIfTrue(!replayMatches, "Recovery episode deterministic replay diverged.");
        ChronicleGuard.ThrowIfTrue(!_scenario.Successful, "Recovery episode has failures or censored agents.");
        ChronicleGuard.ThrowIfTrue(_scenario.DiagnosticValidationError != null, _scenario.DiagnosticValidationError);
        ChronicleGuard.ThrowIfTrue(!_scenario.WarmReuseValid, "Warm compatible acquisition unexpectedly built or failed to reuse a payload.");
        _scenario.Dispose();
        _scenario = null;
    }

    [GlobalCleanup]
    public void Cleanup() => _scenario?.Dispose();

    internal static int RunSmoke(string population, NavigationAcquisitionMode mode = NavigationAcquisitionMode.Immediate,
        string workLimits = "Default")
    {
        foreach (string episode in new[] { "ColdInitial", "WarmCompatible", "SameMapRebake", "ProfileReplacement", "Restore" })
        {
            var benchmark = new NavigationRecoveryBenchmarks
                { Population = population, Episode = episode, CaptureStage = "Smoke", AcquisitionMode = mode, WorkLimits = workLimits };
            try
            {
                // First is a disposable warmup; second must independently prove cold/warm state and replay.
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    benchmark.Prepare();
                    benchmark.RecoverUntilUsable();
                    benchmark.ValidateAndExport();
                }
            }
            finally { benchmark.Cleanup(); }
        }
        return 0;
    }

    internal static int RunCapture(int launch, NavigationAcquisitionMode mode = NavigationAcquisitionMode.Immediate,
        string workLimits = "Default", string populationFilter = "All")
    {
        ChronicleGuard.ThrowIfTrue(launch < 1 || launch > 3, "Capture launch must be 1, 2, or 3.");
        string[] populations = populationFilter == "All" ? new[] { "AStar100", "Flow100", "Flow500" }
            : populationFilter == "Flow" ? new[] { "Flow100", "Flow500" } : new[] { populationFilter };
        foreach (string population in populations)
        foreach (string episode in new[] { "ColdInitial", "WarmCompatible", "SameMapRebake", "ProfileReplacement", "Restore" })
        {
            var benchmark = new NavigationRecoveryBenchmarks
                { Population = population, Episode = episode, Launch = launch, AcquisitionMode = mode, WorkLimits = workLimits };
            try
            {
                for (int invocation = 0; invocation < 4; invocation++)
                {
                    benchmark.CaptureStage = invocation == 0 ? "Warmup" : "Actual";
                    benchmark.Prepare();
                    benchmark.RecoverUntilUsable();
                    benchmark.ValidateAndExport();
                }
            }
            finally { benchmark.Cleanup(); }
        }
        Console.WriteLine($"NAVIGATION_RECOVERY_CAPTURE_COMPLETE launch={launch} cases={populations.Length * 5} warmup={populations.Length * 5} actual={populations.Length * 15}");
        return 0;
    }

    internal static int RunDiagnosticCapture(int launch)
    {
        ChronicleGuard.ThrowIfTrue(launch < 1 || launch > 3, "Diagnostic launch must be 1, 2, or 3.");
        const string limits = "65536,128,4096,8192,64,256";
        (string name, NavigationAcquisitionMode mode)[] cases =
        {
            ("MixedFlowKeys", NavigationAcquisitionMode.Immediate),
            ("MixedFlowKeys", NavigationAcquisitionMode.Scheduled),
            ("UncoveredFlowOrigin", NavigationAcquisitionMode.Immediate),
            ("UncoveredFlowOrigin", NavigationAcquisitionMode.Scheduled),
            ("RetentionPressure8", NavigationAcquisitionMode.Scheduled),
            ("RetentionPressure100", NavigationAcquisitionMode.Scheduled)
        };
        foreach ((string name, NavigationAcquisitionMode mode) in cases)
        {
            var benchmark = new NavigationRecoveryBenchmarks
            {
                Population = "Flow100", Episode = "ColdInitial", Launch = launch,
                AcquisitionMode = mode, WorkLimits = limits, Diagnostic = name
            };
            try
            {
                for (int invocation = 0; invocation < 4; invocation++)
                {
                    benchmark.CaptureStage = invocation == 0 ? "Warmup" : "Actual";
                    benchmark.Prepare();
                    benchmark.RecoverUntilUsable();
                    benchmark.ValidateAndExport();
                }
            }
            finally { benchmark.Cleanup(); }
        }
        Console.WriteLine($"NAVIGATION_RECOVERY_DIAGNOSTIC_COMPLETE launch={launch} cases=6 warmup=6 actual=18");
        return 0;
    }
}

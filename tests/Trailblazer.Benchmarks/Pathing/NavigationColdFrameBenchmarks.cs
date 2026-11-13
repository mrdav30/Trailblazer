using System;
using BenchmarkDotNet.Attributes;
using Chronicler.Diagnostics;
using FixedMathSharp;
using Trailblazer.Navigation;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Measures a headless fixed frame containing 100 moving Navigators and guide acquisitions.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(PerformanceGateConfig))]
[BenchmarkCategory("Graph", "Flow", "Frame")]
public class NavigationColdFrameBenchmarks
{
    private const int AgentCount = 100;
    private const int PreparationFrames = 32;
    private ColdGuideBenchmarkScenario _scenario;
    private BenchmarkNavigator[] _agents;
    private PathQuery[] _queries;
    private int _burst;
    private bool _warm;
    private bool _warmPayloadsPrepared;

    [Params(10_000, 100_000)]
    public int SettledNodeCount { get; set; }

    /// <summary>One frame with no acquisitions, reusable acquisitions, or distinct cold acquisitions.</summary>
    [Params("Baseline", "Warm1", "Warm4", "Cold1", "Cold4")]
    public string Workload { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _burst = Workload == "Baseline" ? 0 : Workload.EndsWith("4", StringComparison.Ordinal) ? 4 : 1;
        _warm = Workload.StartsWith("Warm", StringComparison.Ordinal);
        _warmPayloadsPrepared = false;
        _scenario = new ColdGuideBenchmarkScenario("Plane", SettledNodeCount);
        _scenario.Fixture.Context.SetFrameRate(32);
        PathQuery query = _scenario.FlowQuery;
        _queries = new PathQuery[4];
        for (int i = 0; i < _queries.Length; i++)
        {
            // Arrival tolerance participates in the cache key but does not change
            // the reverse-integration graph. Each burst therefore builds a field.
            var profile = new NavigationAgentProfile(
                query.Agent.Shape, query.Agent.MaxStepUp, query.Agent.MaxDropDown,
                query.Agent.AllowedMedia, query.Agent.Capabilities);
            _queries[i] = query.WithAgentAndStartState(
                profile, query.Start.Position, query.Traversal.StartMedium, query.Start.MapId);
        }

        _agents = new BenchmarkNavigator[AgentCount];
        for (int i = 0; i < _agents.Length; i++)
        {
            Vector3d foot = query.Start.Position + new Vector3d(2 + (i % 10) * 2, 0, 2 + (i / 10) * 2);
            _agents[i] = new BenchmarkNavigator(_scenario.Fixture.Context, foot, query.Agent, i);
        }
    }

    [IterationSetup]
    public void PrepareFrame()
    {
        if (!_warm)
            _scenario.ClearCaches();
        for (int i = 0; i < _agents.Length; i++)
            _agents[i].PrepareFrame();
        // Existing agents have already been moving for one simulated second.
        // This avoids measuring the first motor tick after initialization.
        for (int frame = 0; frame < PreparationFrames; frame++)
        {
            _scenario.Fixture.Context.Simulate();
            for (int i = 0; i < _agents.Length; i++)
            {
                _agents[i].ApplyInputTrekRequest(Vector3d.Right, TrekRate.Slow);
                _agents[i].Simulate();
                _agents[i].CommitFrameMotion();
            }
        }
        for (int i = 0; i < _agents.Length; i++)
            _agents[i].ApplyInputTrekRequest(Vector3d.Right, TrekRate.Slow);
        if (_warm)
        {
            for (int i = 0; i < _burst; i++)
            {
                if (!_warmPayloadsPrepared)
                    _scenario.RequestFlow(_queries[i]);
                // Exact-key checkout fails on a miss instead of quietly rebuilding
                // a field during preparation for a warm measurement.
                _scenario.ValidateFlowPayload(_queries[i]);
            }
            _warmPayloadsPrepared = true;
        }
    }

    [Benchmark]
    public long SimulateFixedFrame()
    {
        _scenario.Fixture.Context.Simulate();
        long checksum = 0;
        for (int i = 0; i < _agents.Length; i++)
        {
            BenchmarkNavigator agent = _agents[i];
            agent.Simulate();
            agent.CommitFrameMotion();
            checksum = unchecked(checksum + agent.Position.X.m_rawValue);
        }
        for (int i = 0; i < _burst; i++)
            checksum = unchecked(checksum + _scenario.RequestFlow(_queries[i]));
        return checksum;
    }

    [IterationCleanup]
    public void ValidateFrame()
    {
        for (int i = 0; i < _agents.Length; i++)
        {
            BenchmarkNavigator agent = _agents[i];
            ChronicleGuard.ThrowIfTrue(
                agent.Position.X <= agent.LastPosition.X || !agent.LastCommittedCell.HasValue,
                message: "Every frame must move all 100 Navigators forward on the published map.");
        }
        _scenario.ValidateDrained();
        for (int i = 0; i < _burst; i++)
            _scenario.ValidateFlowPayload(_queries[i]);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        Console.WriteLine($"NAVIGATION_COLD_FRAME workload={Workload} nodes={SettledNodeCount} "
            + $"agents={AgentCount} frame_rate=32 navigation_budget_ms=2 requests={_burst} "
            + $"untimed_preparation_frames={PreparationFrames} "
            + "host=headless_input_driven no_physics_or_existing_guide_sampling=true");
        _scenario?.WriteCounters();
        if (_agents != null)
        {
            for (int i = 0; i < _agents.Length; i++)
                _agents[i]?.Reset();
        }
        _scenario?.Dispose();
    }
}

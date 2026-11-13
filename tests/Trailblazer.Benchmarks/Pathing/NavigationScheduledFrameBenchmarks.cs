using System;
using System.Diagnostics;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using Chronicler.Diagnostics;
using FixedMathSharp;
using Trailblazer.Navigation;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Measures complete scheduled cold acquisitions and retains every containing-frame observation.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(PerformanceGateConfig))]
[BenchmarkCategory("Graph", "Scheduling", "Frame")]
public class NavigationScheduledFrameBenchmarks
{
    private const int AgentCount = 100;
    private const int MaximumFrames = 1_000_000;
    private ColdGuideBenchmarkScenario _scenario;
    private BenchmarkNavigator[] _agents;
    private PathQuery[] _queries;
    private NavigationGuideRequest[] _requests;
    private long[] _elapsedTicks;
    private long[] _allocatedBytes;
    private int[] _completionMasks;
    private int[] _phases;
    private int[] _activeRequests;
    private int[] _collections;
    private long[] _movement;
    private Fixed64[] _minimumX;
    private bool[] _movingRight;
    private int _frames;
    private int _iteration;

    [Params(10_000, 100_000)]
    public int SettledNodeCount { get; set; }

    [Params("AStar", "FlowField")]
    public string Algorithm { get; set; }

    [Params(0, 1, 4)]
    public int RequestCount { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _scenario = new ColdGuideBenchmarkScenario("Plane", SettledNodeCount);
        _scenario.Fixture.Context.SetFrameRate(32);
        PathQuery query = Algorithm == "AStar" ? _scenario.AStarQuery : _scenario.FlowQuery;
        _queries = new PathQuery[RequestCount];
        _requests = new NavigationGuideRequest[RequestCount];
        for (int i = 0; i < _queries.Length; i++)
        {
            // Distinct profile keys preserve geometry/cost, preventing burst cache hits.
            var profile = new NavigationAgentProfile(query.Agent.Shape,
                query.Agent.MaxStepUp, query.Agent.MaxDropDown,
                query.Agent.AllowedMedia, query.Agent.Capabilities);
            _queries[i] = query.WithAgentAndStartState(profile, query.Start.Position,
                query.Traversal.StartMedium, query.Start.MapId);
        }
        _agents = new BenchmarkNavigator[AgentCount];
        _minimumX = new Fixed64[AgentCount];
        _movingRight = new bool[AgentCount];
        for (int i = 0; i < _agents.Length; i++)
        {
            Vector3d foot = query.Start.Position + new Vector3d(2 + i % 10 * 2, 0, 2 + i / 10 * 2);
            _agents[i] = new BenchmarkNavigator(_scenario.Fixture.Context, foot, query.Agent, i);
            _minimumX[i] = foot.X;
        }
        _elapsedTicks = new long[MaximumFrames];
        _allocatedBytes = new long[MaximumFrames];
        _completionMasks = new int[MaximumFrames];
        _phases = new int[MaximumFrames];
        _activeRequests = new int[MaximumFrames];
        _collections = new int[MaximumFrames];
        _movement = new long[AgentCount];
    }

    [IterationSetup]
    public void Prepare()
    {
        _scenario.ClearCaches();
        _frames = 0;
        Array.Clear(_requests);
        Array.Clear(_movement);
        for (int i = 0; i < _agents.Length; i++)
        {
            _agents[i].PrepareFrame();
            _movingRight[i] = true;
        }
        for (int frame = 0; frame < 32; frame++)
        {
            SetInputs();
            SimulateAgents();
        }
    }

    [Benchmark]
    public long CompleteColdRequests()
    {
        long checksum = 0;
        int completed = 0;
        int target = (1 << RequestCount) - 1;
        for (int frame = 0; frame < MaximumFrames; frame++)
        {
            // Input changes are host preparation, matching the existing containing-frame control.
            // Position-bounded reversals prevent gradual turn/inertia drift off-map.
            SetInputs();
            NavigationGuideRequestPhase phase = NavigationGuideRequestPhase.Queued;
            int activeRequest = -1;
            for (int i = 0; i < _requests.Length; i++)
            {
                if (_requests[i] != null && _requests[i].Status == NavigationGuideStatus.Pending)
                {
                    phase = _requests[i].Phase;
                    activeRequest = i;
                    break;
                }
            }
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);
            long started = Stopwatch.GetTimestamp();
            if (frame == 0)
            {
                activeRequest = RequestCount == 0 ? -1 : 0;
                for (int i = 0; i < _queries.Length; i++)
                {
                    NavigationGuideStatus status = _scenario.Fixture.Context.Guides.Schedule(_queries[i], out _requests[i]);
                    ChronicleGuard.ThrowIfTrue(status != NavigationGuideStatus.Pending,
                        "Scheduled cold request was not admitted.");
                }
            }
            SimulateAgents();
            for (int i = 0; i < _requests.Length; i++)
            {
                if ((completed & (1 << i)) != 0 || _requests[i].Status == NavigationGuideStatus.Pending)
                    continue;
                ChronicleGuard.ThrowIfTrue(_requests[i].Status != NavigationGuideStatus.Success,
                    "Scheduled cold request failed.");
                long cost;
                if (Algorithm == "AStar")
                {
                    ChronicleGuard.ThrowIfTrue(!_requests[i].TryTakeGuide(out NavigationGuideLease? result),
                        "Completed A* guide could not be transferred.");
                    using NavigationGuideLease guide = result.Value;
                    cost = guide.TotalCost.m_rawValue;
                    ChronicleGuard.ThrowIfTrue(guide.StepCount != _scenario.AStarStepCount,
                        "Scheduled A* step count changed.");
                }
                else
                {
                    ChronicleGuard.ThrowIfTrue(!_requests[i].TryTakeFlowField(out NavigationFlowFieldLease? result),
                        "Completed Flow guide could not be transferred.");
                    using NavigationFlowFieldLease guide = result.Value;
                    cost = guide.OriginIntegrationCost.m_rawValue;
                }
                ChronicleGuard.ThrowIfTrue(cost != _scenario.ExpectedCost, "Scheduled route cost changed.");
                checksum = unchecked(checksum + cost);
                _requests[i].Dispose();
                completed |= 1 << i;
            }
            _elapsedTicks[frame] = Stopwatch.GetTimestamp() - started;
            _allocatedBytes[frame] = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            _completionMasks[frame] = completed;
            _phases[frame] = (int)phase;
            _activeRequests[frame] = activeRequest;
            _collections[frame] = (GC.CollectionCount(0) != gen0 ? 1 : 0)
                | (GC.CollectionCount(1) != gen1 ? 2 : 0)
                | (GC.CollectionCount(2) != gen2 ? 4 : 0);
            _frames = frame + 1;
            ValidateAgentFrame(frame);
            // The no-acquisition control uses the identical continuous host and instrumentation.
            if (completed == target && (RequestCount != 0 || _frames == 256))
                return checksum;
        }
        ChronicleGuard.ThrowIfTrue(completed != target, "Scheduled benchmark exceeded its finite frame ceiling.");
        return checksum;
    }

    private void SetInputs()
    {
        for (int i = 0; i < _agents.Length; i++)
        {
            Fixed64 x = _agents[i].Position.X;
            if (x <= _minimumX[i])
                _movingRight[i] = true;
            else if (x >= _minimumX[i] + (Fixed64)4)
                _movingRight[i] = false;
            _agents[i].ApplyInputTrekRequest(_movingRight[i] ? Vector3d.Right : Vector3d.Left, TrekRate.Slow);
        }
    }

    private void SimulateAgents()
    {
        _scenario.Fixture.Context.Simulate();
        for (int i = 0; i < _agents.Length; i++)
        {
            _agents[i].Simulate();
            _agents[i].CommitFrameMotion();
        }
    }

    private void ValidateAgentFrame(int frame)
    {
        for (int i = 0; i < _agents.Length; i++)
        {
            BenchmarkNavigator agent = _agents[i];
            NavigationCommittedCellState? cell = agent.LastCommittedCell;
            if (!cell.HasValue
                || cell.Value.SimulationFrame != _scenario.Fixture.Context.FrameCount
                || cell.Value.Medium != TraversalMedium.Solid)
                ChronicleGuard.ThrowIfTrue(true,
                    $"Scheduled agent commit invalid: frame={frame}, agent={i}, position={agent.Position}, "
                    + $"cellFrame={cell?.SimulationFrame}, expectedFrame={_scenario.Fixture.Context.FrameCount}, medium={cell?.Medium}.");
            _movement[i] += Math.Abs(agent.Position.X.m_rawValue - agent.LastPosition.X.m_rawValue)
                + Math.Abs(agent.Position.Z.m_rawValue - agent.LastPosition.Z.m_rawValue);
            if ((frame + 1) % 32 == 0)
            {
                ChronicleGuard.ThrowIfTrue(_movement[i] == 0,
                    "Every Navigator must move during each direction block.");
                _movement[i] = 0;
            }
        }
    }

    [IterationCleanup]
    public void ValidateAndExport()
    {
        for (int i = 0; i < _requests.Length; i++)
        {
            ChronicleGuard.ThrowIfTrue(_requests[i].Status != NavigationGuideStatus.Success,
                "Scheduled completion status changed after ownership transfer.");
            if (Algorithm == "FlowField")
                _scenario.ValidateFlowPayload(_queries[i]);
            else
                _scenario.RemoveAStarPayload(_queries[i]);
        }
        for (int i = 0; i < _agents.Length; i++)
        {
            ChronicleGuard.ThrowIfTrue(!_agents[i].LastCommittedCell.HasValue,
                "Scheduled frame lost a Navigator's committed map cell.");
        }
        _scenario.ValidateDrained();
        ChronicleGuard.ThrowIfTrue(_scenario.Fixture.Context.Guides.Scheduler.RetainedRequestCount != 0
            || _scenario.Fixture.Context.Guides.Scheduler.RetainedDescriptorBytes != 0,
            "Scheduled acquisition retained request capacity after all results were consumed.");
        // Output is untimed. Keep all raw frame samples, including outliers, GC and completion frames.
        Console.WriteLine("NAVIGATION_SCHEDULE_SAMPLES " + JsonSerializer.Serialize(new
        {
            process = Environment.ProcessId,
            iteration = ++_iteration,
            nodes = SettledNodeCount,
            algorithm = Algorithm,
            requests = RequestCount,
            agents = AgentCount,
            frameRate = 32,
            settings = _scenario.Fixture.Context.Settings.Scheduling,
            stopwatchFrequency = Stopwatch.Frequency,
            frames = _frames,
            ticks = _elapsedTicks.AsSpan(0, _frames).ToArray(),
            allocatedBytes = _allocatedBytes.AsSpan(0, _frames).ToArray(),
            completionMasks = _completionMasks.AsSpan(0, _frames).ToArray(),
            phases = _phases.AsSpan(0, _frames).ToArray(),
            activeRequests = _activeRequests.AsSpan(0, _frames).ToArray(),
            collectionMasks = _collections.AsSpan(0, _frames).ToArray()
        }));
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_requests != null)
            foreach (NavigationGuideRequest request in _requests)
                request?.Dispose();
        if (_agents != null)
            foreach (BenchmarkNavigator agent in _agents)
                agent?.Reset();
        _scenario?.WriteCounters();
        _scenario?.Dispose();
    }
}

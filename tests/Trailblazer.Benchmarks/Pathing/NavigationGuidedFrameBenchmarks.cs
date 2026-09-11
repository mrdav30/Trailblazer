//=======================================================================
// NavigationGuidedFrameBenchmarks.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Diagnostics;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Spatial;
using SwiftCollections.Diagnostics;
using Trailblazer.Navigation;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Measures sustained headless guide following, retaining each complete host-frame observation.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(PerformanceGateConfig))]
[BenchmarkCategory("Graph", "Guidance", "Frame")]
public class NavigationGuidedFrameBenchmarks
{
    private const int Width = 100;
    private const int Length = 100;
    private const int PreparationFrames = 32;
    private const int MeasuredFrames = 64;
    private const string MapId = "guided-frame";
    private readonly long[] _elapsedTicks = new long[MeasuredFrames];
    private readonly long[] _allocatedBytes = new long[MeasuredFrames];
    private readonly int[] _collections = new int[MeasuredFrames];
    private readonly long[] _checksums = new long[MeasuredFrames];
    private readonly long[] _expectedChecksums = new long[MeasuredFrames];
    private BenchmarkPathFixture _fixture;
    private BenchmarkNavigator[] _agents;
    private PathQuery[] _queries;
    private PathAlgorithm _algorithm;
    private long[] _movement;
    private bool _hasExpectedReplay;
    private int _iteration;
    private int _frames;

    [Params(100, 500)]
    public int AgentCount { get; set; }

    [Params("AStar", "FlowField")]
    public string Algorithm { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _algorithm = Enum.Parse<PathAlgorithm>(Algorithm);
        GridConfiguration configuration = NavigationGraphBenchmarkScenario.CreateConfiguration(Width, Length);
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(configuration, NavigationGraphBenchmarkScenario.CreateSettings(
            Width * Length, concurrentQueries: 1, cacheEntries: AgentCount, activeLeaseCapacity: AgentCount));
        _fixture.Context.SetFrameRate(32);
        SwiftThrowHelper.ThrowIfTrue(!configuration.TryNormalize(out NormalizedGridConfiguration binding),
            "Guided frame grid configuration is invalid.");
        NavigationAreaPolicyKey policy = PublishMap(binding);
        Vector3d destination = NavigationGraphBenchmarkScenario.GetFoot(binding, new VoxelIndex(98, 0, 50));
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Quarter, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero, Fixed64.Zero, Fixed64.Quarter, TraversalMedia.Solid, TraversalCapability.None);
        // Real ray capacity is essential: the wall, not budget exhaustion, must prevent direct travel.
        var budget = new NavigationWorkBudget(
            maxLookupProbes: 10_240_000, maxEndpointCandidates: 16,
            maxExpandedNodes: Width * Length, maxEvaluatedEdges: 320_000,
            maxConnectionLegs: 0, maxTransitionCandidates: 0, maxTransitionPairs: 0,
            maxStagedLegAttempts: 0, maxTraceIntervals: 160_000,
            maxCoveredVoxelIntervals: 320_000, maxSimplificationRays: 1_024);
        _agents = new BenchmarkNavigator[AgentCount];
        _queries = new PathQuery[AgentCount];
        _movement = new long[AgentCount];
        for (int i = 0; i < AgentCount; i++)
        {
            Vector3d foot = NavigationGraphBenchmarkScenario.GetFoot(binding,
                new VoxelIndex(2 + i % 25, 0, 2 + i / 25 * 2));
            _agents[i] = new BenchmarkNavigator(_fixture.Context, foot, profile, i);
            _queries[i] = new PathQuery(
                new NavigationEndpoint(foot, MapId), new NavigationEndpoint(destination, MapId),
                profile, policy, new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
                _algorithm, budget, allowTransitions: false);
            NavigationRayStatus direct = _fixture.Context.Guides.TryGetDirectHeading(_queries[i], foot, out _);
            if (direct != NavigationRayStatus.Blocked)
                SwiftThrowHelper.ThrowIfTrue(true, $"Guided frame wall did not block direct travel for agent {i}: {direct}.");
        }

        // Exercise the real lifecycle before timing. Later invocations must replay every frame exactly.
        Prepare();
        Simulate64GuidedFrames();
        Array.Copy(_checksums, _expectedChecksums, MeasuredFrames);
        _hasExpectedReplay = true;
        ReleaseAgentsAndValidateDrained();
    }

    private NavigationAreaPolicyKey PublishMap(NormalizedGridConfiguration binding)
    {
        var cell = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None,
            default, Fixed64.Zero, (Fixed64)4, (Fixed64)4);
        var builder = new NavigationMapBuilder(MapId, binding);
        for (int x = 0; x < Width; x++)
        {
            for (int z = 0; z < Length; z++)
            {
                // A five-cell opening joins the two sides; agents begin far from that opening.
                if (x != 50 || z >= 95)
                    builder.AddCell(new VoxelIndex(x, 0, z), cell);
            }
        }
        var map = new NavigationMapCommitOperation(
            new PreparedNavigationMap(builder.Build(), bakeVersion: 1), OverlayReplacementPolicy.Clear,
            operationSequence: 1, effectiveFrame: _fixture.Context.FrameCount + 1);
        var key = new NavigationAreaPolicyKey(MapId, revision: 1);
        var policy = new NavigationAreaPolicyCommitOperation(new NavigationAreaPolicy(key,
            new[] { new NavigationAreaRule(true, Fixed64.Zero) }),
            publicationSequence: 2, effectiveFrame: _fixture.Context.FrameCount + 1);
        SwiftThrowHelper.ThrowIfTrue(!_fixture.Context.Pathing.Admit(map) || !_fixture.Context.Pathing.Admit(policy),
            "Guided frame map or policy was not admitted.");
        for (int frame = 0; frame < 4_096
            && (map.Receipt.Status == NavigationOperationStatus.Pending
                || policy.Receipt.Status == NavigationOperationStatus.Pending); frame++)
        {
            _fixture.Context.Simulate();
        }
        SwiftThrowHelper.ThrowIfTrue(map.Receipt.Status != NavigationOperationStatus.Applied
            || policy.Receipt.Status != NavigationOperationStatus.Applied, "Guided frame publication failed.");
        return key;
    }

    [IterationSetup]
    public void Prepare()
    {
        // Release all old occupancy before registering the identical new starting formation.
        ReleaseAgentsAndValidateDrained();
        _frames = 0;
        Array.Clear(_movement);
        for (int i = 0; i < AgentCount; i++)
        {
            _agents[i].PrepareFrame();
            _agents[i].ApplyGuidedTrekRequest(_queries[i], TrekRate.Slow);
        }
        for (int frame = 0; frame < PreparationFrames; frame++)
        {
            SimulateAgents();
            ValidateAgentFrame(frame);
        }
        ValidateActiveGuides();
    }

    /// <summary>The BenchmarkDotNet operation is a complete 64-frame block, not a single frame.</summary>
    [Benchmark]
    public long Simulate64GuidedFrames()
    {
        long checksum = 1_469_598_103_934_665_603L;
        for (int frame = 0; frame < MeasuredFrames; frame++)
        {
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);
            long started = Stopwatch.GetTimestamp();
            SimulateAgents();
            _elapsedTicks[frame] = Stopwatch.GetTimestamp() - started;
            _allocatedBytes[frame] = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            _collections[frame] = (GC.CollectionCount(0) != gen0 ? 1 : 0)
                | (GC.CollectionCount(1) != gen1 ? 2 : 0)
                | (GC.CollectionCount(2) != gen2 ? 4 : 0);
            _frames = frame + 1;

            // Semantic checks and replay hashing are outside the inner host-frame timer/allocation bracket.
            ValidateAgentFrame(frame);
            checksum = Fold(checksum, frame);
            for (int i = 0; i < AgentCount; i++)
            {
                BenchmarkNavigator agent = _agents[i];
                checksum = Fold(checksum, agent.Position.X.m_rawValue);
                checksum = Fold(checksum, agent.Position.Y.m_rawValue);
                checksum = Fold(checksum, agent.Position.Z.m_rawValue);
                checksum = Fold(checksum, agent.Velocity.X.m_rawValue);
                checksum = Fold(checksum, agent.Velocity.Y.m_rawValue);
                checksum = Fold(checksum, agent.Velocity.Z.m_rawValue);
                checksum = Fold(checksum, agent.LastCommittedCell.Value.Address.Index.x);
                checksum = Fold(checksum, agent.LastCommittedCell.Value.Address.Index.z);
            }
            _checksums[frame] = checksum;
            if (_hasExpectedReplay && checksum != _expectedChecksums[frame])
                SwiftThrowHelper.ThrowIfTrue(true, $"Guided frame replay diverged at relative frame {frame}.");
        }
        return checksum;
    }

    private static long Fold(long checksum, long value) => unchecked((checksum ^ value) * 1_099_511_628_211L);

    private void SimulateAgents()
    {
        _fixture.Context.Simulate();
        for (int i = 0; i < AgentCount; i++)
        {
            _agents[i].Simulate();
            _agents[i].CommitFrameMotion();
        }
    }

    private void ValidateAgentFrame(int frame)
    {
        for (int i = 0; i < AgentCount; i++)
        {
            BenchmarkNavigator agent = _agents[i];
            NavigationCommittedCellState? committed = agent.LastCommittedCell;
            if (!agent.IsGuided || !agent.Steering.HasNavigationGuidance
                || !committed.HasValue || committed.Value.SimulationFrame != _fixture.Context.FrameCount
                || committed.Value.Medium != TraversalMedium.Solid || committed.Value.Address.MapId != MapId
                || committed.Value.Address.Index.x < 0 || committed.Value.Address.Index.x >= 50
                || committed.Value.Address.Index.y != 0
                || committed.Value.Address.Index.z < 0 || committed.Value.Address.Index.z >= Length
                || agent.FootPosition.X - agent.Radius <= -Fixed64.Half
                || agent.FootPosition.X + agent.Radius >= (Fixed64)49 + Fixed64.Half
                || agent.FootPosition.Z - agent.Radius <= -Fixed64.Half
                || agent.FootPosition.Z + agent.Radius >= (Fixed64)99 + Fixed64.Half)
            {
                SwiftThrowHelper.ThrowIfTrue(true,
                    $"Guided agent invalid: frame={frame}, agent={i}, position={agent.Position}, "
                    + $"guided={agent.Steering.HasNavigationGuidance}, cellFrame={committed?.SimulationFrame}, "
                    + $"expectedFrame={_fixture.Context.FrameCount}, cell={committed?.Address}.");
            }
            _movement[i] += Math.Abs(agent.Position.X.m_rawValue - agent.LastPosition.X.m_rawValue)
                + Math.Abs(agent.Position.Z.m_rawValue - agent.LastPosition.Z.m_rawValue);
            if ((frame + 1) % 32 == 0)
            {
                if (_movement[i] == 0)
                    SwiftThrowHelper.ThrowIfTrue(true, $"Guided agent {i} did not move during a 32-frame block.");
                _movement[i] = 0;
            }
        }
    }

    private void ValidateActiveGuides()
    {
        NavigationAStarPayloadCache astar = _fixture.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        NavigationFlowFieldPayloadCache flow = _fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache;
        SwiftThrowHelper.ThrowIfTrue(astar.ActiveLeaseCount != (_algorithm == PathAlgorithm.AStar ? AgentCount : 0)
            || flow.ActiveLeaseCount != (_algorithm == PathAlgorithm.FlowField ? AgentCount : 0),
            "Guided frame did not retain one real guide per Navigator.");
    }

    [IterationCleanup]
    public void ValidateAndExport()
    {
        SwiftThrowHelper.ThrowIfTrue(_frames != MeasuredFrames, "Guided frame block ended early.");
        ValidateActiveGuides();
        ReleaseAgentsAndValidateDrained();
        Console.WriteLine("NAVIGATION_GUIDED_SAMPLES " + JsonSerializer.Serialize(new
        {
            process = Environment.ProcessId,
            iteration = ++_iteration,
            algorithm = Algorithm,
            agents = AgentCount,
            frameRate = 32,
            preparationFrames = PreparationFrames,
            frames = _frames,
            stopwatchFrequency = Stopwatch.Frequency,
            ticks = _elapsedTicks,
            allocatedBytes = _allocatedBytes,
            collectionMasks = _collections,
            checksums = _checksums
        }));
    }

    private void ReleaseAgentsAndValidateDrained()
    {
        if (_agents != null)
            foreach (BenchmarkNavigator agent in _agents)
                agent?.Reset();
        NavigationAStarPayloadCache astar = _fixture.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        NavigationFlowFieldPayloadCache flow = _fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache;
        SwiftThrowHelper.ThrowIfTrue(astar.ActiveLeaseCount != 0 || astar.ReservedLeaseCount != 0
            || astar.ReservedPayloadBytes != 0 || astar.LeasedBytes != 0 || astar.DetachedBytes != 0
            || flow.ActiveLeaseCount != 0 || flow.ReservedLeaseCount != 0
            || flow.ReservedPayloadBytes != 0 || flow.LeasedBytes != 0 || flow.DetachedBytes != 0
            || _fixture.Context.Guides.Scheduler.RetainedRequestCount != 0
            || _fixture.Context.Guides.Scheduler.RetainedDescriptorBytes != 0,
            "Guided frame retained active, reserved, detached, or scheduled ownership after reset.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        if (_fixture == null)
            return;
        try
        {
            ReleaseAgentsAndValidateDrained();
        }
        finally
        {
            _fixture.Teardown();
        }
    }
}

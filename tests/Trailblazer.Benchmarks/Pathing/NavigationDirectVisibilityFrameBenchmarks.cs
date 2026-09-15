using System;
using System.Diagnostics;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using System.Collections.Generic;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Spatial;
using SwiftCollections.Diagnostics;
using Trailblazer.Navigation;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>Measures five direct-visibility controls, retaining each complete moving host-frame observation.</summary>
[MemoryDiagnoser]
[AllStatisticsColumn]
[Config(typeof(PerformanceGateConfig))]
[BenchmarkCategory("Graph", "Guidance", "Frame")]
public class NavigationDirectVisibilityFrameBenchmarks
{
    private int Width;
    private int Length;
    private const int MaximumAgentCount = 500;
    private const int PreparationFrames = 32;
    private const int MeasuredFrames = 64;
    private string MapId => "direct-visibility-" + Case;
    private int AgentCount => Case == "Sparse500" ? 500 : 100;
    private const string Algorithm = "FlowField";
    private bool Direct => Case is "Clear100" or "Short100";
    private readonly long[] _elapsedTicks = new long[MeasuredFrames];
    private readonly long[] _allocatedBytes = new long[MeasuredFrames];
    private readonly int[] _collections = new int[MeasuredFrames];
    private readonly long[] _checksums = new long[MeasuredFrames];
    private readonly long[] _expectedChecksums = new long[MeasuredFrames];
    private readonly int?[] _phaseAssignments = new int?[MaximumAgentCount];
    private readonly int[] _preparationRayCounts = new int[PreparationFrames];
    private readonly int[] _periodicRayCounts = new int[MeasuredFrames];
    private readonly byte[] _preparationRayObservations =
        new byte[PreparationFrames * MaximumAgentCount];
    private readonly byte[] _periodicRayObservations =
        new byte[MeasuredFrames * MaximumAgentCount];
    private BenchmarkPathFixture _fixture;
    private BenchmarkNavigator[] _agents;
    private PathQuery[] _queries;
    private long[] _movement;
    private bool _hasExpectedReplay;
    private int _iteration;
    private int _frames;
    private string _mode;
    private long _clockOrigin;
    private int _mandatoryRayCount;
    private long _firstAdmissionTicks;
    private long _firstAdmissionAllocatedBytes;
    private int _firstAdmissionCollectionMask;


    [Params("Clear100", "Short100", "Late100", "Ineligible100", "Sparse500")]
    public string Case { get; set; }

    [GlobalSetup(Target = nameof(Simulate64FramesUnphased))]
    public void SetupUnphased() => Setup("Unphased");

    [GlobalSetup(Target = nameof(Simulate64FramesBalancedPhases))]
    public void SetupBalancedPhases() => Setup("BalancedPhases");

    internal static int RunSmoke()
    {
        int failures = 0;
        foreach (string control in new[] { "Clear100", "Short100", "Late100", "Ineligible100", "Sparse500" })
        foreach (string mode in new[] { "Unphased", "BalancedPhases" })
        {
            var benchmark = new NavigationDirectVisibilityFrameBenchmarks { Case = control };
            try
            {
                benchmark.Setup(mode);
                for (int replay = 0; replay < 2; replay++)
                {
                    benchmark.Prepare();
                    benchmark.Simulate64Frames();
                    benchmark.ValidateAndExport();
                }
                Console.WriteLine($"DIRECT_VISIBILITY_SMOKE PASS case={control}, mode={mode}");
            }
            catch (Exception error)
            {
                failures++;
                Console.WriteLine($"DIRECT_VISIBILITY_SMOKE FAIL case={control}, mode={mode}: {error}");
            }
            finally { benchmark.Cleanup(); }
        }
        return failures == 0 ? 0 : 1;
    }

    private void Setup(string mode)
    {
        if (Case is not ("Clear100" or "Short100" or "Late100" or "Ineligible100" or "Sparse500"))
            throw new InvalidOperationException($"Unknown control: {Case}.");
        _mode = mode;
        Width = Case == "Short100" ? 32 : 256;
        Length = Case == "Short100" ? 32 : 192;
        bool sparse = Case == "Sparse500";
        var configuration = sparse
            ? new GridConfiguration(Vector3d.Zero, new Vector3d(Width - 1, 0, Length - 1),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
                storageKind: GridStorageKind.Sparse)
            : NavigationGraphBenchmarkScenario.CreateConfiguration(Width, Length);
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(settings: NavigationGraphBenchmarkScenario.CreateSettings(
            Width * Length, concurrentQueries: 1, cacheEntries: AgentCount, activeLeaseCapacity: AgentCount,
            dynamicCellSlotsPerMap: sparse ? 8192 : null));
        _fixture.Context.SetFrameRate(32);
        var physical = new List<VoxelIndex>();
        if (sparse)
            for (int x = 0; x < Width; x++)
            for (int z = 0; z < Length; z++)
                if ((x >= 2 && x <= 30 && z >= 2 && z <= 44
                    || Math.Abs((long)(z - 2) * 251 - (long)(x - 2) * 183) <= 4 * 251
                    || x >= 2 && x <= 4 || x >= 251 && x <= 253 || z >= 189)
                    && (x != 48 || z >= 189))
                    physical.Add(new VoxelIndex(x, 0, z));
        Check(sparse ? _fixture.World.TryAddGrid(configuration, physical, out _)
            : _fixture.World.TryAddGrid(configuration, out _), "physical grid registration");
        if (Case == "Ineligible100")
            Check(_fixture.World.TryAddGrid(NavigationGraphBenchmarkScenario.CreateConfiguration(
                1, 1, new Vector3d(10000, 0, 10000)), out _), "second unbound grid");
        if (sparse)
            for (int frame = 0; frame < 256; frame++) _fixture.Context.Simulate();
        Check(configuration.TryNormalize(out NormalizedGridConfiguration binding), "grid normalization");
        NavigationAreaPolicyKey policy = PublishMap(binding);
        ValidateScene(sparse ? physical.Count : Width * Length);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.FromFraction(1, 8), Fixed64.One, Fixed64.Zero),
            Fixed64.Zero, Fixed64.Zero, Fixed64.Quarter, TraversalMedia.Solid, TraversalCapability.None);
        var budget = new NavigationWorkBudget(
            maxLookupProbes: 10_240_000, maxEndpointCandidates: 16,
            maxExpandedNodes: Width * Length, maxEvaluatedEdges: Math.Max(320_000, checked(32 * Width * Length)),
            maxConnectionLegs: 0, maxTransitionCandidates: 0, maxTransitionPairs: 0,
            maxStagedLegAttempts: 0, maxTraceIntervals: 160_000,
            maxCoveredVoxelIntervals: 320_000, maxSimplificationRays: 1_024);
        _agents = new BenchmarkNavigator[AgentCount];
        _queries = new PathQuery[AgentCount];
        _movement = new long[AgentCount];
        for (int i = 0; i < AgentCount; i++)
        {
            VoxelIndex start = Case == "Short100" ? new(2 + 2 * (i % 10), 0, 2 + 2 * (i / 10))
                : sparse ? new(4 + i % 25, 0, 4 + 2 * (i / 25)) : new(2 + 2 * i, 0, 2 + i);
            VoxelIndex end = Case == "Short100" ? new(start.x + 4, 0, start.z + 2)
                : sparse ? new(253, 0, 185) : new(254, 0, 128);
            Vector3d foot = NavigationGraphBenchmarkScenario.GetFoot(binding, start);
            Vector3d destination = NavigationGraphBenchmarkScenario.GetFoot(binding, end);
            _agents[i] = new BenchmarkNavigator(_fixture.Context, foot, profile, i);
            _queries[i] = new PathQuery(new NavigationEndpoint(foot, MapId),
                new NavigationEndpoint(destination, MapId), profile, policy,
                new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
                PathAlgorithm.FlowField, budget, allowTransitions: false);
            NavigationWorkMeter meter = _fixture.Context.Pathing.ImmediateRayWorkspace.WorkMeter;
            meter.Reset(default);
            NavigationRayStatus status = _fixture.Context.Guides.TryGetDirectHeading(_queries[i], foot, out _);
            Check(status == (Direct ? NavigationRayStatus.Success : NavigationRayStatus.Blocked)
                && meter.TraceIntervals > 0 && !meter.IsGuideSampleBridge,
                $"initial ray agent={i}, position={foot}, status={status}, trace={meter.TraceIntervals}, "
                + $"covered={meter.CoveredVoxelIntervals}, lookup={meter.LookupProbes}");
        }
        Prepare();
        Simulate64Frames();
        Array.Copy(_checksums, _expectedChecksums, MeasuredFrames);
        _hasExpectedReplay = true;
        ReleaseAgentsAndValidateDrained();
    }

    private NavigationAreaPolicyKey PublishMap(NormalizedGridConfiguration binding)
    {
        var cell = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None,
            default, Fixed64.Zero, (Fixed64)4, (Fixed64)4);
        var builder = new NavigationMapBuilder(MapId, binding);
        if (Case == "Sparse500") builder.SetDefaultCell(cell);
        else
            for (int x = 0; x < Width; x++)
            for (int z = 0; z < Length; z++)
                if (Direct || x != 250 || z >= 189)
                    builder.AddCell(new VoxelIndex(x, 0, z), cell);
        var map = new NavigationMapCommitOperation(new PreparedNavigationMap(builder.Build(), 1),
            OverlayReplacementPolicy.Clear, 1, _fixture.Context.FrameCount + 1);
        var key = new NavigationAreaPolicyKey(MapId, 1);
        var policy = new NavigationAreaPolicyCommitOperation(new NavigationAreaPolicy(key,
            new[] { new NavigationAreaRule(true, Fixed64.Zero) }), 2, _fixture.Context.FrameCount + 1);
        Check(_fixture.Context.Pathing.Admit(map) && _fixture.Context.Pathing.Admit(policy), "publication admission");
        for (int frame = 0; frame < 4096 && (map.Receipt.Status == NavigationOperationStatus.Pending
            || policy.Receipt.Status == NavigationOperationStatus.Pending); frame++) _fixture.Context.Simulate();
        Check(map.Receipt.Status == NavigationOperationStatus.Applied
            && policy.Receipt.Status == NavigationOperationStatus.Applied,
            $"publication map={map.Receipt.Status}/{map.Receipt.Rejection}, policy={policy.Receipt.Status}");
        // Applied receipts may precede sparse committed-change/component catch-up.
        int catchup = 0;
        while (catchup < 4096 && (_fixture.Context.Pathing.RetainedBaselineCaptureCount != 0
            || _fixture.Context.Pathing.RetainedCompositionWorkCount != 0
            || _fixture.Context.Pathing.RetainedOperationWorkCount != 0
            || _fixture.Context.Pathing.GetNavigationGraphDiagnostics().BaselineRebuildCount != 0))
        {
            _fixture.Context.Simulate();
            catchup++;
        }
        Console.WriteLine($"DIRECT_VISIBILITY_PUBLICATION case={Case}, catchupFrames={catchup}");
        return key;
    }

    private void ValidateScene(int seededPhysicalCount)
    {
        var diagnostics = _fixture.Context.Pathing.GetNavigationGraphDiagnostics();
        Check(diagnostics.Maps.Count == 1 && diagnostics.BaselineRebuildCount == 0
            && _fixture.Context.Pathing.RetainedBaselineCaptureCount == 0
            && _fixture.Context.Pathing.RetainedCompositionWorkCount == 0
            && _fixture.Context.Pathing.RetainedOperationWorkCount == 0,
            $"publication not drained: maps={diagnostics.Maps.Count}, baselines={diagnostics.BaselineRebuildCount}, "
            + $"captures={_fixture.Context.Pathing.RetainedBaselineCaptureCount}, "
            + $"composition={_fixture.Context.Pathing.RetainedCompositionWorkCount}, "
            + $"operations={_fixture.Context.Pathing.RetainedOperationWorkCount}, "
            + $"capacityBlocked={diagnostics.BaselineCapacityBlockedCount}");
        using var lease = _fixture.Context.Pathing.TryAcquireNavigationGraph();
        Check(lease != null && lease.Graph.MapCount == 1, "one graph map");
        var graph = lease.Graph;
        var instance = graph.GetInstance(0);
        Check(instance.IsMaterialized && graph.AutomaticSeams.PairCount == 0
            && graph.ExplicitConnections.GetSourceOwnerCount(MapId) == 0, "materialization and edge shape");
        Check(_fixture.World.TryGetGrid(instance.GridIdentity.GridIndex, out var physicalGrid)
            && physicalGrid.ConfiguredVoxelCount == seededPhysicalCount,
            $"actual physical count differs from seed={seededPhysicalCount}");
        Check(_fixture.World.TryCaptureNavigationBaseline(instance.GridIdentity.ConfigurationKey,
                ReadOnlySpan<VoxelIndex>.Empty, out var currentBaseline)
            && currentBaseline.WorldSpawnToken == instance.GridIdentity.WorldSpawnToken
            && currentBaseline.GridSpawnToken == instance.GridIdentity.GridSpawnToken
            && currentBaseline.CapturedChangeSequence == _fixture.World.ChangeSequence
            && currentBaseline.CapturedChangeSequence == instance.BaselineCapturedChangeSequence
            && currentBaseline.GridLastChangeSequence == instance.GridLastChangeSequence,
            "current world/grid identity or captured sequences disagree");
        int present = 0;
        var physicalPages = new HashSet<int>();
        NavigationSurfaceComponent component = null;
        for (int x = 0; x < Width; x++)
        for (int z = 0; z < Length; z++)
        {
            var index = new VoxelIndex(x, 0, z);
            bool physical = instance.IsPhysicallyPresent(index);
            if (Case == "Sparse500" && x == 48 && z < 189) Check(!physical, "sparse wall must be physically absent");
            if (!physical) continue;
            present++;
            Check(instance.TryGetSlot(index, out int slot), $"physical slot {index}");
            physicalPages.Add(slot / NavigationPhysicalPage.SlotCount);
            Check(graph.SurfaceComponents.TryGet(new NavigationCellAddress(MapId, index),
                TraversalMedium.Solid, out var current), $"Solid membership {index}");
            component ??= current;
            Check(component.Key.Equals(current.Key), $"multiple Solid components at {index}");
        }
        int expected = seededPhysicalCount - (Case is "Late100" or "Ineligible100" ? 189 : 0);
        Check(present == expected && (Case != "Sparse500" || present < Width * Length / 4),
            $"captured physical-semantic count={present}, expected={expected}");
        // At initial publication these are the captured physical baseline pages, with no later updates.
        int pages = instance.LastCopiedPhysicalPages;
        var limits = _fixture.Context.Settings.QueryLimits;
        Check(pages == physicalPages.Count && pages > 0 && pages <= limits.AStarWorkspaceEndpointPageCapacity
            && limits.AStarWorkspaceComponentCapacity >= 1
            && 1L + limits.RayWorkspaceTraceIntervalCapacity <= 10_240_000
            && (long)Width * Length <= limits.RayWorkspaceCoveredAddressCapacity, "Phase 0 headroom");
        int grids = 0;
        foreach (var grid in _fixture.World.ActiveGrids) if (grid != null && grid.IsActive) grids++;
        Check(grids == (Case == "Ineligible100" ? 2 : 1), "physical universe");
        Console.WriteLine($"DIRECT_VISIBILITY_SCENE case={Case}, physicalSeed={seededPhysicalCount}, "
            + $"capturedNavigablePhysical={present}, capturedPages={pages}, "
            + $"pageCapacity={limits.AStarWorkspaceEndpointPageCapacity}, components=1, grids={grids}, "
            + $"traceCapacity={limits.RayWorkspaceTraceIntervalCapacity}, coveredCapacity={limits.RayWorkspaceCoveredAddressCapacity}");
    }

    private void Check(bool valid, string context)
    {
        if (!valid) throw new InvalidOperationException($"Direct visibility {Case}/{_mode}: {context}.");
    }

    [IterationSetup]
    public void Prepare()
    {
        ReleaseAgentsAndValidateDrained();
        _frames = 0;
        Array.Clear(_movement);
        Array.Clear(_preparationRayCounts);
        Array.Clear(_periodicRayCounts);
        Array.Clear(_preparationRayObservations);
        Array.Clear(_periodicRayObservations);
        for (int i = 0; i < AgentCount; i++)
        {
            _agents[i].PrepareFrame();
            _phaseAssignments[i] = _mode == "BalancedPhases" ? i % 16 : null;
            _agents[i].Steering.ConfigurePathRechecks(16, _phaseAssignments[i]);
            _agents[i].ApplyGuidedTrekRequest(_queries[i], TrekRate.Slow);
        }
        for (int frame = 0; frame < PreparationFrames; frame++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
            long started = Stopwatch.GetTimestamp();
            _preparationRayCounts[frame] = SimulateAgentsInstrumented(_preparationRayObservations, frame);
            if (frame == 0)
            {
                _firstAdmissionTicks = Stopwatch.GetTimestamp() - started;
                _firstAdmissionAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
                _firstAdmissionCollectionMask = (GC.CollectionCount(0) != gen0 ? 1 : 0)
                    | (GC.CollectionCount(1) != gen1 ? 2 : 0) | (GC.CollectionCount(2) != gen2 ? 4 : 0);
            }
            ValidateAgentFrame(frame);
        }
        _mandatoryRayCount = _preparationRayCounts[0];
        _clockOrigin = checked((long)_fixture.Context.LogicalFrameIndex + 1);
        ValidateRaySchedule(_preparationRayCounts, _preparationRayObservations, preparation: true);
    }

    [Benchmark]
    public long Simulate64FramesUnphased() => Simulate64Frames();

    [Benchmark]
    public long Simulate64FramesBalancedPhases() => Simulate64Frames();

    private long Simulate64Frames()
    {
        long checksum = 1_469_598_103_934_665_603L;
        for (int frame = 0; frame < MeasuredFrames; frame++)
        {
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            int gen0 = GC.CollectionCount(0);
            int gen1 = GC.CollectionCount(1);
            int gen2 = GC.CollectionCount(2);
            long started = Stopwatch.GetTimestamp();
            _periodicRayCounts[frame] = SimulateAgentsInstrumented(
                _periodicRayObservations,
                frame);
            _elapsedTicks[frame] = Stopwatch.GetTimestamp() - started;
            _allocatedBytes[frame] = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            _collections[frame] = (GC.CollectionCount(0) != gen0 ? 1 : 0)
                | (GC.CollectionCount(1) != gen1 ? 2 : 0)
                | (GC.CollectionCount(2) != gen2 ? 4 : 0);
            _frames = frame + 1;

            checksum = ValidateAndHashFrame(frame, checksum);
        }
        ValidateRaySchedule(_periodicRayCounts, _periodicRayObservations, preparation: false);
        return checksum;
    }

    private long ValidateAndHashFrame(int frame, long checksum)
    {
        // Semantic checks and replay hashing stay outside each containing-frame timing bracket.
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
            Check(false, $"replay diverged at relative frame {frame}");
        return checksum;
    }

    private static long Fold(long checksum, long value) => unchecked((checksum ^ value) * 1_099_511_628_211L);

    private int SimulateAgentsInstrumented(byte[] observations, int frame)
    {
        _fixture.Context.Simulate();
        NavigationWorkMeter meter = _fixture.Context.Pathing.ImmediateRayWorkspace.WorkMeter;
        int rayCount = 0;
        for (int i = 0; i < AgentCount; i++)
        {
            meter.Reset(default);
            _agents[i].Simulate();
            SwiftThrowHelper.ThrowIfTrue(
                meter.IsGuideSampleBridge,
                $"Direct visibility {Case}/{_mode}: Flow recovery ray contaminated direct LOS attribution for agent {i}.");
            bool observed = meter.TraceIntervals > 0;
            observations[frame * MaximumAgentCount + i] = observed ? (byte)1 : (byte)0;
            if (observed)
                rayCount++;
            _agents[i].CommitFrameMotion();
        }
        return rayCount;
    }

    private void ValidateRaySchedule(int[] counts, byte[] observations, bool preparation)
    {
        int length = preparation ? PreparationFrames : MeasuredFrames;
        int total = 0;
        for (int frame = 0; frame < length; frame++)
        {
            long logicalFrame = preparation
                ? _clockOrigin - PreparationFrames + frame
                : _clockOrigin + frame;
            int remainder = (int)(logicalFrame % 16);
            int expected = 0;
            int observed = 0;
            for (int agent = 0; agent < AgentCount; agent++)
            {
                bool expectedAgent = preparation && frame == 0
                    || _mode == "Unphased" && frame % 16 == 0
                    || _mode != "Unphased" && _phaseAssignments[agent] == remainder;
                bool observedAgent = observations[frame * MaximumAgentCount + agent] != 0;
                if (expectedAgent)
                    expected++;
                if (observedAgent)
                    observed++;
                SwiftThrowHelper.ThrowIfTrue(
                    observedAgent != expectedAgent,
                    $"Guided frame {_mode} {(preparation ? "preparation" : "measured")} per-agent LOS schedule "
                    + $"mismatch at frame {frame}, agent {agent}: expected={expectedAgent}, actual={observedAgent}.");
            }
            SwiftThrowHelper.ThrowIfTrue(
                counts[frame] != observed || observed != expected,
                $"Guided frame {_mode} {(preparation ? "preparation" : "measured")} LOS schedule "
                + $"mismatch at frame {frame}: expected={expected}, observed={observed}, actual={counts[frame]}.");
            if (!preparation)
                total += observed;
        }
        if (!preparation)
        {
            SwiftThrowHelper.ThrowIfTrue(
                total != AgentCount * 4,
                $"Guided frame {_mode} measured {total} periodic rays instead of {AgentCount * 4}.");
            for (int agent = 0; agent < AgentCount; agent++)
            {
                int agentTotal = 0;
                for (int frame = 0; frame < MeasuredFrames; frame++)
                    agentTotal += observations[frame * MaximumAgentCount + agent];
                SwiftThrowHelper.ThrowIfTrue(
                    agentTotal != 4,
                    $"Guided frame {_mode} agent {agent} measured {agentTotal} periodic rays instead of 4.");
            }
        }
    }


    private void ValidateAgentFrame(int frame)
    {
        for (int i = 0; i < AgentCount; i++)
        {
            BenchmarkNavigator agent = _agents[i];
            var committed = agent.LastCommittedCell;
            PathQuery? query = agent.Steering.CurrentQuery;
            bool valid = agent.IsGuided && agent.Steering.ShouldMove
                && agent.Steering.HasLineOfSightPath == Direct
                && agent.Steering.HasNavigationGuidance != Direct
                && query.HasValue && query.Value.End == _queries[i].End
                && query.Value.AreaPolicy == _queries[i].AreaPolicy
                && query.Value.Algorithm == PathAlgorithm.FlowField
                && committed.HasValue && committed.Value.SimulationFrame == _fixture.Context.FrameCount
                && committed.Value.Medium == TraversalMedium.Solid && committed.Value.Address.MapId == MapId
                && committed.Value.Address.Index.y == 0 && agent.FootPosition.Y == -Fixed64.Half
                && Math.Abs(agent.FootPosition.X.m_rawValue - ((Fixed64)committed.Value.Address.Index.x).m_rawValue) <= Fixed64.Half.m_rawValue
                && Math.Abs(agent.FootPosition.Z.m_rawValue - ((Fixed64)committed.Value.Address.Index.z).m_rawValue) <= Fixed64.Half.m_rawValue
                && agent.FootPosition.X - agent.Radius > -Fixed64.Half
                && agent.FootPosition.X + agent.Radius < (Fixed64)(Width - 1) + Fixed64.Half
                && agent.FootPosition.Z - agent.Radius > -Fixed64.Half
                && agent.FootPosition.Z + agent.Radius < (Fixed64)(Length - 1) + Fixed64.Half;
            if (valid)
                valid = _fixture.Context.Pathing.TryGetNavigationGraphCellState(MapId,
                    committed.Value.Address.Index, out var cell) && cell.IsMaterialized
                    && cell.IsPresent && cell.HasCell && !cell.IsBlocked
                    && cell.Cell.Media == TraversalMedia.Solid;
            if (!valid)
            {
                if (!Direct) DiagnoseFailedFlow(_queries[i]);
                // Failure-only diagnostic is outside the containing-frame timer.
                NavigationRayStatus status = query.HasValue
                    ? _fixture.Context.Guides.TryGetDirectHeading(query.Value, agent.FootPosition, out _)
                    : NavigationRayStatus.Blocked;
                Check(false, $"frame={frame}, logical={_fixture.Context.LogicalFrameIndex}, agent={i}, "
                    + $"position={agent.Position}, direct={agent.Steering.HasLineOfSightPath}, "
                    + $"guide={agent.Steering.HasNavigationGuidance}, moving={agent.Steering.ShouldMove}, "
                    + $"cell={committed?.Address}, cellFrame={committed?.SimulationFrame}, directStatus={status}");
            }
            _movement[i] += Math.Abs(agent.Position.X.m_rawValue - agent.LastPosition.X.m_rawValue)
                + Math.Abs(agent.Position.Z.m_rawValue - agent.LastPosition.Z.m_rawValue);
            if ((frame + 1) % 32 == 0)
            {
                Check(_movement[i] > 0, $"agent={i} stationary in 32-frame block ending {frame}");
                _movement[i] = 0;
            }
        }
        ValidateActiveGuides();
    }

    private void ValidateActiveGuides()
    {
        var astar = _fixture.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        var flow = _fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache;
        Check(astar.ActiveLeaseCount == 0 && flow.ActiveLeaseCount == (Direct ? 0 : AgentCount),
            $"leases AStar={astar.ActiveLeaseCount}, Flow={flow.ActiveLeaseCount}");
    }

    private void DiagnoseFailedFlow(PathQuery query)
    {
        // Failure-only untimed reproduction with the same budgets, using existing work diagnostics.
        var limits = _fixture.Context.Settings.QueryLimits;
        var workspace = new NavigationFlowFieldWorkspace(1, limits.FlowWorkspaceEndpointPageCapacity,
            1, Width * Length, limits.RayWorkspaceCoveredAddressCapacity, limits.RayWorkspaceTraceIntervalCapacity);
        using var admission = new NavigationQueryAdmissionWork(_fixture.World,
            _fixture.Context.Pathing.NavigationGraphStore, workspace.EndpointWorkspace,
            workspace.RayWorkspace, PathAlgorithm.FlowField);
        admission.Begin(_fixture.Context.Pathing.TryAcquireNavigationGraph(), query,
            TraversalMedium.Solid, TraversalMedia.Solid);
        while (admission.Status == NavigationQueryAdmissionStatus.Pending)
            admission.Advance(query.Budget.MaxLookupProbes, query.Budget.MaxEndpointCandidates);
        string status = admission.Status.ToString();
        if (admission.Status == NavigationQueryAdmissionStatus.Success)
        {
            using var work = new NavigationFlowFieldWork(_fixture.World, admission.Result, workspace,
                _fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.MaximumSinglePayloadBytes);
            while (work.Status == NavigationFlowFieldStatus.Pending)
                work.Advance(query.Budget.MaxLookupProbes, Width * Length, query.Budget.MaxEvaluatedEdges, 0);
            status = work.Status.ToString();
        }
        var meter = admission.Meter;
        Console.WriteLine($"DIRECT_VISIBILITY_FAILURE case={Case}, mode={_mode}, flow={status}, "
            + $"lookup={meter.LookupProbes}/{query.Budget.MaxLookupProbes}, "
            + $"endpoints={meter.EndpointCandidates}/{query.Budget.MaxEndpointCandidates}, "
            + $"nodes={meter.ExpandedNodes}/{query.Budget.MaxExpandedNodes}, "
            + $"edges={meter.EvaluatedEdges}/{query.Budget.MaxEvaluatedEdges}, "
            + $"trace={meter.TraceIntervals}/{query.Budget.MaxTraceIntervals}, "
            + $"covered={meter.CoveredVoxelIntervals}/{query.Budget.MaxCoveredVoxelIntervals}");
    }

    [IterationCleanup]
    public void ValidateAndExport()
    {
        Check(_frames == MeasuredFrames, "frame block ended early");
        ValidateActiveGuides();
        ReleaseAgentsAndValidateDrained();
        Console.WriteLine("NAVIGATION_GUIDED_SAMPLES " + JsonSerializer.Serialize(new
        {
            process = Environment.ProcessId,
            iteration = ++_iteration,
            controlCase = Case,
            algorithm = Algorithm,
            agents = AgentCount,
            frameRate = 32,
            preparationFrames = PreparationFrames,
            frames = _frames,
            stopwatchFrequency = Stopwatch.Frequency,
            ticks = _elapsedTicks,
            allocatedBytes = _allocatedBytes,
            collectionMasks = _collections,
            checksums = _checksums,
            mode = _mode,
            intervalFrames = 16,
            phaseAssignments = _phaseAssignments.AsSpan(0, AgentCount).ToArray(),
            clockOrigin = _clockOrigin,
            mandatoryRayCount = _mandatoryRayCount,
            preparationRayCounts = _preparationRayCounts,
            periodicRayCounts = _periodicRayCounts,
            firstAdmissionTicks = _firstAdmissionTicks,
            firstAdmissionAllocatedBytes = _firstAdmissionAllocatedBytes,
            firstAdmissionCollectionMask = _firstAdmissionCollectionMask
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
        Check(_fixture.Context.Pathing.NavigationGraphStore.ActiveLeaseCount == 0
            && _fixture.Context.Pathing.RetainedBaselineCaptureCount == 0
            && _fixture.Context.Pathing.RetainedCompositionWorkCount == 0
            && _fixture.Context.Pathing.RetainedOperationWorkCount == 0
            && _fixture.Context.Pathing.ImmediateRayWorkspace.RayWork.Status == NavigationRayStatus.Pending,
            "graph/workspace/publication ownership did not drain");
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Chronicler;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections.Diagnostics;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks;

/// <summary>The existing guided wall-gap workload, observed from trigger through usable guidance.</summary>
internal sealed class NavigationRecoveryScenario : IDisposable
{
    private const string MapId = "guided-frame";
    private const int MaximumFrames = 10_000;
    private readonly BenchmarkPathFixture _fixture = new();
    private readonly BenchmarkNavigator[] _agents;
    private readonly PathQuery[] _queries;
    private readonly bool[] _failed;
    private readonly long[] _movement;
    private readonly long[] _ticks = new long[MaximumFrames];
    private readonly long[] _allocations = new long[MaximumFrames];
    private readonly int[] _collections = new int[MaximumFrames];
    private readonly long[] _hashes = new long[MaximumFrames];
    private readonly int[] _readyCounts = new int[MaximumFrames];
    private readonly int[] _waitingCounts = new int[MaximumFrames];
    private readonly int[] _operationCounts = new int[MaximumFrames];
    private readonly int[] _retainedRequestCounts;
    private readonly int[] _capacityStatusCounts;
    private readonly bool[] _everCapacityStatus;
    private readonly string[] _originalStartCells;
    private readonly string[] _originalDestinationCells;
    private readonly NavigationAcquisitionMode _acquisitionMode;
    private readonly string _workLimits;
    private readonly string _episode;
    private readonly string _diagnostic;
    private readonly PathAlgorithm _algorithm;
    private readonly Process _process = Process.GetCurrentProcess();
    private NavigationOperationReceipt _rebakeReceipt;
    private int _frames;
    private int _ready;
    private int _failureCount;
    private int _distinctCapacityStatusAgents;
    private bool _originalDestinationsIntact = true;
    private long _processCpuTicks;
    private long _episodeTicks;
    private long _jsonTicks;
    private long _jsonAllocatedBytes;
    private int _jsonCollections;
    private int _cacheCountAtTrigger;
    private int _warmCoverage;
    private long _clockOrigin;
    private bool _disposed;
    private Dictionary<string, long> _countersBefore;
    private Dictionary<string, long> _counters;

    internal int[] ReadyFrames { get; }
    internal long[] FrameHashes => _hashes.AsSpan(0, _frames).ToArray();
    internal bool Successful => _ready == _agents.Length && _failureCount == 0;
    internal bool WarmReuseValid => _episode != "WarmCompatible" || _counters["buildStarts"] == 0
        && _counters["cacheCheckoutHits"] + _counters["sharedFollowerCheckouts"] == _agents.Length;

    internal string DiagnosticValidationError
    {
        get
        {
            if (_diagnostic == null) return null;
            if (!_originalDestinationsIntact) return "An original agent lost its own destination.";
            if (_diagnostic == "MixedFlowKeys" && (_counters["buildStarts"] != 2
                || _counters["cacheCheckoutHits"] + _counters["sharedFollowerCheckouts"] != 98
                || _acquisitionMode == NavigationAcquisitionMode.Scheduled && _counters["cacheCheckoutHits"] < 2))
                return "Mixed Flow keys did not independently build and reuse both exact destinations.";
            if (_diagnostic == "UncoveredFlowOrigin" && _acquisitionMode == NavigationAcquisitionMode.Scheduled
                && (_counters["sharedCoverageMisses"] == 0 || _counters["buildStarts"] <= 1))
                return "Scheduled uncovered origin did not produce a coverage miss and multiple builds.";
            if (_diagnostic == "RetentionPressure8" && (_retainedRequestCounts.AsSpan(0, _frames).IndexOfAnyInRange(9, int.MaxValue) >= 0
                || _distinctCapacityStatusAgents == 0))
                return "Retention-8 did not stay bounded or show real capacity status pressure.";
            return null;
        }
    }

    internal static int RunReadinessRegression()
    {
        using var scenario = new NavigationRecoveryScenario("Flow100", "ColdInitial");
        scenario._agents[98].Steering.CanMove = false;
        scenario._agents[99].Steering.CanMove = false;
        Advance(0);
        Require(98, 0, "two original requests are still waiting");
        SwiftThrowHelper.ThrowIfTrue(scenario.ReadyFrames[0] != 1, "Agent0 did not acquire its original guide.");

        Request(0, scenario._agents[0].FootPosition + Vector3d.Right * 2);
        Advance(1);
        SwiftThrowHelper.ThrowIfTrue(!scenario._agents[0].Steering.HasLineOfSightPath
            || scenario._agents[0].Steering.HasNavigationGuidance,
            "Regression must actually switch the early-ready agent from a lease to direct LOS.");
        Require(98, 0, "switching to LOS must not revoke an original success");

        scenario._agents[98].Steering.CanMove = true;
        Request(98, scenario._agents[98].FootPosition + Vector3d.Right * 2);
        Advance(2);
        SwiftThrowHelper.ThrowIfTrue(!scenario._agents[98].Steering.HasLineOfSightPath
            || scenario.ReadyFrames[98] != 3, "A first usable direct heading must qualify for readiness.");
        Require(99, 0, "the direct-first request completed while the final request waits");

        Request(0, new Vector3d(200, 0, 200));
        Advance(3);
        SwiftThrowHelper.ThrowIfTrue(!scenario._agents[0].Steering.IsAtDestination,
            "Regression must exercise the real invalid-path/arrival callback after first readiness.");
        Require(99, 0, "a later failed trek must not reclassify the original successful request");

        scenario._agents[99].Steering.CanMove = true;
        Request(99, new Vector3d(200, 0, 200));
        Advance(4);
        Require(99, 1, "the still-pending original request failed, leaving no censored remainder");
        Request(99, scenario._queries[99].End.Position);
        Advance(5);
        Require(99, 1, "a failed original request stays failed after a later successful trek");
        SwiftThrowHelper.ThrowIfTrue(scenario.ReadyFrames[0] != 1 || scenario.ReadyFrames[99] != -1
            || scenario._failed[0] || !scenario._failed[99], "Original outcomes are not disjoint and latched.");
        Console.WriteLine("RECOVERY_READINESS_REGRESSION passed: leased-to-direct, direct-first, sticky success/failure, zero final censoring");
        return 0;

        void Advance(int frame)
        {
            scenario.SimulateFrame();
            scenario.ValidateFrame(frame, recordReadiness: true);
        }

        void Request(int index, Vector3d destination)
        {
            PathQuery query = scenario._queries[index];
            scenario._agents[index].ApplyGuidedTrekRequest(new PathQuery(
                new NavigationEndpoint(scenario._agents[index].FootPosition, MapId),
                new NavigationEndpoint(destination, MapId), query.Agent, query.AreaPolicy,
                query.Traversal, query.Algorithm, query.Budget, query.AllowTransitions, query.FlowField), TrekRate.Slow);
        }

        void Require(int ready, int failures, string reason) => SwiftThrowHelper.ThrowIfTrue(
            scenario._ready != ready || scenario._failureCount != failures,
            $"Readiness regression: {reason}; ready={scenario._ready}/{ready}, failures={scenario._failureCount}/{failures}.");
    }

    internal NavigationRecoveryScenario(string population, string episode,
        NavigationAcquisitionMode acquisitionMode = NavigationAcquisitionMode.Immediate, string workLimits = "Default",
        string diagnostic = null)
    {
        SwiftThrowHelper.ThrowIfTrue(population != "AStar100" && population != "Flow100" && population != "Flow500",
            "Recovery population must be AStar100, Flow100, or Flow500.");
        SwiftThrowHelper.ThrowIfTrue(diagnostic != null && (population != "Flow100" || episode != "ColdInitial"
            || workLimits != "65536,128,4096,8192,64,256"
            || diagnostic is not ("MixedFlowKeys" or "UncoveredFlowOrigin" or "RetentionPressure8" or "RetentionPressure100")
            || diagnostic.StartsWith("RetentionPressure", StringComparison.Ordinal) && acquisitionMode != NavigationAcquisitionMode.Scheduled),
            "Recovery diagnostic must be its fixed Flow100 cold arm with the selected B work limits/mode.");
        _episode = episode;
        _diagnostic = diagnostic;
        _acquisitionMode = acquisitionMode;
        _workLimits = workLimits;
        _algorithm = population == "AStar100" ? PathAlgorithm.AStar : PathAlgorithm.FlowField;
        int count = population == "Flow500" ? 500 : 100;
        _agents = new BenchmarkNavigator[count];
        _queries = new PathQuery[count];
        _failed = new bool[count];
        _movement = new long[count];
        if (diagnostic != null)
        {
            _originalStartCells = new string[count];
            _originalDestinationCells = new string[count];
        }
        if (diagnostic is "RetentionPressure8" or "RetentionPressure100")
        {
            _retainedRequestCounts = new int[MaximumFrames];
            _capacityStatusCounts = new int[MaximumFrames];
            _everCapacityStatus = new bool[count];
        }
        ReadyFrames = new int[count];
        Array.Fill(ReadyFrames, -1);
        GridConfiguration configuration = NavigationGraphBenchmarkScenario.CreateConfiguration(100, 100);
        _fixture.Setup(configuration, NavigationGraphBenchmarkScenario.CreateSettings(
            10_000, concurrentQueries: 1, cacheEntries: count, activeLeaseCapacity: count,
            scheduling: CreateScheduling(count, workLimits, diagnostic == "RetentionPressure8" ? 8 : count)));
        _fixture.Context.SetFrameRate(32);
        SwiftThrowHelper.ThrowIfTrue(!configuration.TryNormalize(out NormalizedGridConfiguration binding),
            "Recovery grid configuration is invalid.");
        var cell = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None,
            default, Fixed64.Zero, (Fixed64)4, (Fixed64)4);
        var builder = new NavigationMapBuilder(MapId, binding);
        for (int x = 0; x < 100; x++)
            for (int z = 0; z < 100; z++)
                if (x != 50 || z >= 95)
                    builder.AddCell(new VoxelIndex(x, 0, z), cell);
        NavigationMap map = builder.Build();
        var publication = new NavigationMapCommitOperation(new PreparedNavigationMap(map, 1),
            OverlayReplacementPolicy.Clear, 1, _fixture.Context.FrameCount + 1);
        var policy = new NavigationAreaPolicyKey(MapId, 1);
        var policyPublication = new NavigationAreaPolicyCommitOperation(new NavigationAreaPolicy(policy,
            new[] { new NavigationAreaRule(true, Fixed64.Zero) }), 2, _fixture.Context.FrameCount + 1);
        SwiftThrowHelper.ThrowIfTrue(!_fixture.Context.Pathing.Admit(publication)
            || !_fixture.Context.Pathing.Admit(policyPublication), "Recovery map/policy admission failed.");
        for (int i = 0; i < 4_096 && (publication.Receipt.Status == NavigationOperationStatus.Pending
            || policyPublication.Receipt.Status == NavigationOperationStatus.Pending); i++)
            _fixture.Context.Simulate();
        SwiftThrowHelper.ThrowIfTrue(publication.Receipt.Status != NavigationOperationStatus.Applied
            || policyPublication.Receipt.Status != NavigationOperationStatus.Applied, "Recovery publication failed.");
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Quarter, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero, Fixed64.Zero, Fixed64.Quarter, TraversalMedia.Solid, TraversalCapability.None);
        var budget = new NavigationWorkBudget(10_240_000, 16, 10_000, 320_000,
            0, 0, 0, 0, 160_000, 320_000, 1_024);
        for (int i = 0; i < count; i++)
        {
            int startX = diagnostic == "UncoveredFlowOrigin" && i == 0 ? 48 : 2 + i % 25;
            int startZ = diagnostic == "UncoveredFlowOrigin" && i == 0 ? 94 : 2 + i / 25 * 2;
            int destinationZ = diagnostic == "MixedFlowKeys" && (i / 25) % 2 == 1 ? 60 : 50;
            Vector3d foot = NavigationGraphBenchmarkScenario.GetFoot(binding, new VoxelIndex(startX, 0, startZ));
            Vector3d destination = NavigationGraphBenchmarkScenario.GetFoot(binding, new VoxelIndex(98, 0, destinationZ));
            if (diagnostic != null)
            {
                _originalStartCells[i] = $"{startX},0,{startZ}";
                _originalDestinationCells[i] = $"98,0,{destinationZ}";
            }
            _queries[i] = new PathQuery(new NavigationEndpoint(foot, MapId),
                new NavigationEndpoint(destination, MapId), profile, policy,
                new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid), _algorithm, budget,
                allowTransitions: false);
            _agents[i] = new BenchmarkNavigator(_fixture.Context, foot, profile, i);
            _agents[i].PrepareFrame();
            if (episode is "ColdInitial" or "WarmCompatible")
                ConfigureAcquisitionMode(_agents[i]);
            _agents[i].Steering.ConfigurePathRechecks(16, i % 16);
            NavigationWorkMeter meter = _fixture.Context.Pathing.ImmediateRayWorkspace.WorkMeter;
            meter.Reset(default);
            NavigationRayStatus direct = _fixture.Context.Guides.TryGetDirectHeading(_queries[i], foot, out _);
            SwiftThrowHelper.ThrowIfTrue(direct != NavigationRayStatus.Blocked || meter.TraceIntervals <= 0,
                "Recovery wall must block direct travel without budget exhaustion.");
        }
        AssertDrained();
        AssertEmptyCaches();
        if (episode == "WarmCompatible")
        {
            for (int i = 0; i < count; i++)
                WarmQuery(_queries[i]);
            for (int i = 0; i < count; i++)
                ValidateWarmCoverage(_queries[i], new NavigationCellAddress(MapId,
                    new VoxelIndex(2 + i % 25, 0, 2 + i / 25 * 2)));
        }
        for (int i = 0; i < count; i++)
            _agents[i].ApplyGuidedTrekRequest(_queries[i], TrekRate.Slow);
        if (episode is "SameMapRebake" or "ProfileReplacement" or "Restore")
        {
            for (int frame = 0; frame < 32; frame++)
            {
                SimulateFrame();
                ValidateFrame(frame, recordReadiness: false);
            }
            for (int i = 0; i < count; i++)
                SwiftThrowHelper.ThrowIfTrue(!_agents[i].Steering.HasNavigationGuidance || _movement[i] == 0,
                    "Recovery preparation must retain moving guided agents.");
            foreach (BenchmarkNavigator agent in _agents)
                ConfigureAcquisitionMode(agent);
        }
        if (episode == "SameMapRebake")
        {
            var rebake = new NavigationMapCommitOperation(new PreparedNavigationMap(map, 2),
                OverlayReplacementPolicy.Clear, 3, _fixture.Context.FrameCount + 1);
            _rebakeReceipt = rebake.Receipt;
            SwiftThrowHelper.ThrowIfTrue(!_fixture.Context.Pathing.Admit(rebake), "Recovery rebake rejected.");
        }
        else if (episode == "ProfileReplacement")
        {
            var replacement = new NavigationAgentProfile(
                new KinematicBodyShape(Fixed64.FromFraction(1, 5), Fixed64.One, Fixed64.Zero),
                Fixed64.Zero, Fixed64.Zero, Fixed64.Quarter, TraversalMedia.Solid, TraversalCapability.None);
            foreach (BenchmarkNavigator agent in _agents)
                SwiftThrowHelper.ThrowIfTrue(agent.TryReplaceNavigationProfile(replacement)
                    != NavigationProfileReplacementStatus.Applied, "Recovery profile replacement rejected.");
        }
        else if (episode == "Restore")
            PrepareRestore(profile);
        else
            SwiftThrowHelper.ThrowIfTrue(episode != "ColdInitial" && episode != "WarmCompatible",
                "Unknown recovery episode.");
        if (episode == "ColdInitial")
            AssertEmptyCaches();
        _cacheCountAtTrigger = RelevantCacheCount();
        _clockOrigin = checked(_fixture.Context.FrameCount + 1);
        Array.Clear(_movement);
        for (int i = 0; i < count; i++)
        {
            int index = i;
            _agents[i].Steering.Events.OnInvalidPath = () => RecordOriginalFailure(index);
        }
    }

    private void ConfigureAcquisitionMode(BenchmarkNavigator agent) =>
        agent.Steering.ConfigureAcquisitionMode(_acquisitionMode);

    private static NavigationSchedulingSettings CreateScheduling(int count, string workLimits, int maxRetainedRequests)
    {
        long bytes = Math.Max(65_536L, count * 512L);
        if (workLimits == "Default")
            return new NavigationSchedulingSettings(maxRetainedRequests: maxRetainedRequests, maxRetainedDescriptorBytes: bytes);
        string[] values = workLimits.Split(',');
        SwiftThrowHelper.ThrowIfTrue(values.Length != 6,
            "Explicit work limits must be lookup,candidate,node,edge,connection,operation.");
        return new NavigationSchedulingSettings(maxRetainedRequests, bytes, int.Parse(values[0]), int.Parse(values[1]),
            int.Parse(values[2]), int.Parse(values[3]), int.Parse(values[4]), int.Parse(values[5]));
    }

    private void PrepareRestore(NavigationAgentProfile profile)
    {
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
        long started = Stopwatch.GetTimestamp();
        for (int i = 0; i < _agents.Length; i++)
        {
            BenchmarkNavigator source = _agents[i];
            string json = JsonRecordSerializer.Serialize(source);
            Vector3d foot = source.FootPosition;
            source.Reset();
            var restored = new BenchmarkNavigator(_fixture.Context, foot, profile, i);
            restored.PrepareFrame();
            ConfigureAcquisitionMode(restored);
            JsonRecordSerializer.Populate(restored, json);
            _agents[i] = restored;
            SwiftThrowHelper.ThrowIfTrue(restored.Steering.HasNavigationGuidance
                || restored.Steering.PathRecheckPhase != i % 16,
                "Recovery restore retained transient guidance or lost balanced phases.");
        }
        _jsonTicks = Stopwatch.GetTimestamp() - started;
        _jsonAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocated;
        _jsonCollections = CollectionMask(gen0, gen1, gen2);
        AssertDrained();
    }

    internal long Run()
    {
        _countersBefore = ReadCounters();
        _process.Refresh();
        long cpuBefore = _process.TotalProcessorTime.Ticks;
        long episodeStarted = Stopwatch.GetTimestamp();
        long hash = 1_469_598_103_934_665_603L;
        for (int frame = 0; frame < MaximumFrames; frame++)
        {
            long allocated = GC.GetAllocatedBytesForCurrentThread();
            int gen0 = GC.CollectionCount(0), gen1 = GC.CollectionCount(1), gen2 = GC.CollectionCount(2);
            long started = Stopwatch.GetTimestamp();
            SimulateFrame();
            _ticks[frame] = Stopwatch.GetTimestamp() - started;
            _allocations[frame] = GC.GetAllocatedBytesForCurrentThread() - allocated;
            _collections[frame] = CollectionMask(gen0, gen1, gen2);
            _frames = frame + 1;
            ValidateFrame(frame, recordReadiness: true);
            for (int i = 0; i < _agents.Length; i++)
            {
                BenchmarkNavigator agent = _agents[i];
                hash = Fold(hash, agent.Position.X.m_rawValue);
                hash = Fold(hash, agent.Position.Y.m_rawValue);
                hash = Fold(hash, agent.Position.Z.m_rawValue);
                hash = Fold(hash, agent.Velocity.X.m_rawValue);
                hash = Fold(hash, agent.Velocity.Y.m_rawValue);
                hash = Fold(hash, agent.Velocity.Z.m_rawValue);
                hash = Fold(hash, agent.LastCommittedCell.Value.Address.Index.x);
                hash = Fold(hash, agent.LastCommittedCell.Value.Address.Index.z);
                hash = Fold(hash, ReadyFrames[i]);
            }
            _hashes[frame] = hash;
            _readyCounts[frame] = _ready;
            _operationCounts[frame] = _fixture.Context.Guides.Scheduler.LastFrameOperationCount;
            if (_retainedRequestCounts != null)
                _retainedRequestCounts[frame] = _fixture.Context.Guides.Scheduler.RetainedRequestCount;
            for (int i = 0; i < _agents.Length; i++)
                if (_agents[i].Steering.IsAwaitingGuidance) _waitingCounts[frame]++;
            if (_capacityStatusCounts != null)
                for (int i = 0; i < _agents.Length; i++)
                {
                    if (_agents[i].Steering.LastAcquisitionStatus == NavigationGuideStatus.CapacityExceeded)
                    {
                        _capacityStatusCounts[frame]++;
                        if (!_everCapacityStatus[i])
                        {
                            _everCapacityStatus[i] = true;
                            _distinctCapacityStatusAgents++;
                        }
                    }
                }
            if (_ready + _failureCount == _agents.Length)
                break;
        }
        _episodeTicks = Stopwatch.GetTimestamp() - episodeStarted;
        _process.Refresh();
        _processCpuTicks = _process.TotalProcessorTime.Ticks - cpuBefore;
        _counters = ReadCounters();
        foreach (string key in _countersBefore.Keys)
            _counters[key] -= _countersBefore[key];
        return hash;
    }

    private void SimulateFrame()
    {
        _fixture.Context.Simulate();
        foreach (BenchmarkNavigator agent in _agents)
        {
            agent.Simulate();
            agent.CommitFrameMotion();
        }
    }

    private void ValidateFrame(int frame, bool recordReadiness)
    {
        _ready = 0;
        _failureCount = 0;
        for (int i = 0; i < _agents.Length; i++)
        {
            BenchmarkNavigator agent = _agents[i];
            if (_diagnostic != null && agent.Steering.CurrentQuery is PathQuery current
                && current.End.Position != _queries[i].End.Position)
                _originalDestinationsIntact = false;
            NavigationCommittedCellState? committed = agent.LastCommittedCell;
            SwiftThrowHelper.ThrowIfTrue(!committed.HasValue
                || committed.Value.SimulationFrame != _fixture.Context.FrameCount
                || committed.Value.GraphVersion != _fixture.Context.Pathing.NavigationGraphStore.Current.GraphVersion
                || committed.Value.Address.MapId != MapId || committed.Value.Medium != TraversalMedium.Solid
                || committed.Value.Address.Index.y != 0
                || agent.FootPosition.X - agent.Radius <= -Fixed64.Half
                || agent.FootPosition.X + agent.Radius >= (Fixed64)99 + Fixed64.Half
                || agent.FootPosition.Z - agent.Radius <= -Fixed64.Half
                || agent.FootPosition.Z + agent.Radius >= (Fixed64)99 + Fixed64.Half
                || agent.FootPosition.Z - agent.Radius < (Fixed64)94 + Fixed64.Half
                    && agent.FootPosition.X + agent.Radius > (Fixed64)49 + Fixed64.Half
                    && agent.FootPosition.X - agent.Radius < (Fixed64)50 + Fixed64.Half,
                $"Recovery committed cell/body invalid at frame {frame}, agent {i}.");
            int occupancy = 0;
            foreach (WorldVoxelIndex index in GridOccupantManager.GetOccupiedIndices(_fixture.World, agent))
            {
                occupancy++;
                SwiftThrowHelper.ThrowIfTrue(index.VoxelIndex != committed.Value.Address.Index
                    || !GridOccupantManager.TryGetOccupancyTicket(_fixture.World, agent, index, out _),
                    $"Recovery agent {i} occupancy does not match its committed physical cell at frame {frame}: occupied={index.VoxelIndex}, committed={committed.Value.Address.Index}, foot={agent.FootPosition}, waiting={agent.Steering.IsAwaitingGuidance}, guide={agent.Steering.HasNavigationGuidance}.");
            }
            SwiftThrowHelper.ThrowIfTrue(occupancy != 1, $"Recovery agent {i} occupancy count is {occupancy}.");
            _movement[i] += Math.Abs(agent.Position.X.m_rawValue - agent.LastPosition.X.m_rawValue)
                + Math.Abs(agent.Position.Z.m_rawValue - agent.LastPosition.Z.m_rawValue);
            SwiftThrowHelper.ThrowIfTrue(agent.PendingTransition.HasValue,
                "This wall-gap episode contains no authored action; an action cannot stand in for readiness.");
            bool arrival = agent.Steering.IsAtDestination
                && (agent.FootPosition - _queries[i].End.Position).MagnitudeSquared
                    <= _queries[i].Agent.ArrivalRadius * _queries[i].Agent.ArrivalRadius;
            if (agent.Steering.IsAtDestination && !arrival)
                RecordOriginalFailure(i);
            // A direct-first result is accepted only after current-foot proof, not cached LOS trust.
            // This validation ray is outside the frame timer and is never repeated for a latched outcome.
            bool provenDirect = recordReadiness && ReadyFrames[i] < 0 && !_failed[i]
                && agent.Steering.HasLineOfSightPath && agent.Steering.TargetDirection != Vector3d.Zero
                && agent.Steering.CurrentQuery.HasValue
                && _fixture.Context.Guides.TryGetDirectHeading(agent.Steering.CurrentQuery.Value,
                    agent.FootPosition, out Vector3d directHeading) == NavigationRayStatus.Success
                && directHeading != Vector3d.Zero;
            bool usable = !_failed[i] && (arrival || (agent.Steering.HasNavigationGuidance || provenDirect)
                && agent.Steering.TargetDirection != Vector3d.Zero);
            if (recordReadiness && usable && ReadyFrames[i] < 0
                && (_rebakeReceipt == null || _rebakeReceipt.Status == NavigationOperationStatus.Applied))
            {
                SwiftThrowHelper.ThrowIfTrue(!arrival && _movement[i] == 0,
                    $"Recovery agent {i} reported heading but did not move.");
                ReadyFrames[i] = frame + 1;
            }
            // Original requests have disjoint sticky outcomes. Later live navigation is still
            // validated above, but cannot revoke success or turn it into failure/censoring.
            if (ReadyFrames[i] >= 0) _ready++;
            else if (_failed[i]) _failureCount++;
        }
    }

    private void RecordOriginalFailure(int index)
    {
        if (ReadyFrames[index] < 0)
            _failed[index] = true;
    }

    private void WarmQuery(PathQuery query)
    {
        if (_algorithm == PathAlgorithm.AStar)
        {
            NavigationGuideStatus status = _fixture.Context.Guides.RequestGuide(query, out var lease);
            SwiftThrowHelper.ThrowIfTrue(status != NavigationGuideStatus.Success || !lease.HasValue,
                "Warm recovery A* preparation failed.");
            lease.Value.Dispose();
        }
        else
        {
            NavigationGuideStatus status = _fixture.Context.Guides.RequestFlowField(query, out var lease);
            SwiftThrowHelper.ThrowIfTrue(status != NavigationGuideStatus.Success || !lease.HasValue,
                "Warm recovery Flow preparation failed.");
            lease.Value.Dispose();
        }
    }

    private void ValidateWarmCoverage(PathQuery query, NavigationCellAddress start)
    {
        var end = new NavigationCellAddress(MapId, new VoxelIndex(98, 0, 50));
        NavigationWorldGraphStore store = _fixture.Context.Pathing.NavigationGraphStore;
        if (_algorithm == PathAlgorithm.AStar)
        {
            NavigationAStarPayloadCache cache = _fixture.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
            SwiftThrowHelper.ThrowIfTrue(!cache.TryReservePayload(
                _fixture.Context.Settings.QueryLimits.MaxAStarSinglePayloadBytes, out var reservation),
                "Warm recovery A* diagnostic reservation failed.");
            try
            {
                var key = new NavigationAStarPayloadKey(query, start, end, TraversalMedium.Solid, TraversalMedia.Solid);
                SwiftThrowHelper.ThrowIfTrue(!cache.TryCheckoutReserved(key, store.Current, ref reservation, out var lease),
                    "Warm recovery A* exact-key coverage is missing.");
                lease.Dispose();
            }
            finally { cache.ReleasePayloadReservation(ref reservation); }
        }
        else
        {
            var key = new NavigationFlowFieldPayloadKey(query, end, TraversalMedium.Solid, TraversalMedia.Solid);
            NavigationFlowFieldStatus status = _fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache
                .TryCheckout(store, store.Current, key, start, out var lease, out _);
            SwiftThrowHelper.ThrowIfTrue(status != NavigationFlowFieldStatus.Success,
                "Warm recovery Flow compatible-origin coverage is missing.");
            lease.Dispose();
        }
        _warmCoverage++;
    }

    internal object CreateRecord(int iteration, bool replayMatches, string captureStage, int launch) => new
    {
        schema = 1, process = Environment.ProcessId, iteration, episode = _episode, diagnostic = _diagnostic, captureStage, launch,
        algorithm = _algorithm.ToString(), agents = _agents.Length, acquisitionMode = _acquisitionMode.ToString(),
        workLimits = _workLimits == "Default" ? "default-work-limits-with-workload-sized-retention" : "explicit:" + _workLimits,
        frameRate = 32, maximumFrames = MaximumFrames, frames = _frames, clockOrigin = _clockOrigin,
        intervalFrames = 16, phaseAssignment = "original-agent-index-modulo-16",
        stopwatchFrequency = Stopwatch.Frequency, ticks = _ticks.AsSpan(0, _frames).ToArray(),
        allocatedBytes = _allocations.AsSpan(0, _frames).ToArray(),
        collectionMasks = _collections.AsSpan(0, _frames).ToArray(), checksums = FrameHashes,
        readyCounts = _readyCounts.AsSpan(0, _frames).ToArray(), readyFrames = ReadyFrames,
        waitingCounts = _waitingCounts.AsSpan(0, _frames).ToArray(),
        frameOperationCounts = _operationCounts.AsSpan(0, _frames).ToArray(),
        retainedRequestCounts = _retainedRequestCounts?.AsSpan(0, _frames).ToArray(),
        capacityStatusCounts = _capacityStatusCounts?.AsSpan(0, _frames).ToArray(),
        distinctCapacityStatusAgents = _retainedRequestCounts == null ? (int?)null : _distinctCapacityStatusAgents,
        originalDestinationsIntact = _diagnostic == null ? (bool?)null : _originalDestinationsIntact,
        originalStartCells = _originalStartCells, originalDestinationCells = _originalDestinationCells,
        latestAcquisitionOutcomes = Array.ConvertAll(_agents, agent => agent.Steering.LastAcquisitionStatus?.ToString()),
        warmReuseValid = WarmReuseValid,
        failedAgents = _failed, failures = _failureCount, cancellations = 0,
        censored = _agents.Length - _ready - _failureCount, ready = _ready, replayMatches,
        episodeTicks = _episodeTicks, processWideCpuTicks = _processCpuTicks,
        processCpuFrequency = TimeSpan.TicksPerSecond,
        processCpuScope = "entire-episode-including-untimed-validation-not-preparation-or-export",
        cacheCountAtTrigger = _cacheCountAtTrigger, warmOriginsVerified = _warmCoverage,
        freshContextPerEpisode = true, publicationStatus = _rebakeReceipt?.Status.ToString(),
        jsonPreparationTicks = _jsonTicks, jsonPreparationAllocatedBytes = _jsonAllocatedBytes,
        jsonPreparationCollectionMask = _jsonCollections,
        jsonPreparationScope = "serialize-reset-construct-prepare-populate-assert",
        acquisitionCounters = _counters,
        coalescedRequests = _counters["sharedFollowerCheckouts"],
        workCounterScope = "finalized-admitted-query-meters-exclude-active-unfinalized-work-clear-copy-LOS-and-sampling",
        queryBudget = _queries[0].Budget,
        schedulerSettings = _fixture.Context.Settings.Scheduling,
        queryLimits = _fixture.Context.Settings.QueryLimits
    };

    private Dictionary<string, long> ReadCounters()
    {
        NavigationAcquisitionDiagnostics counters = _algorithm == PathAlgorithm.AStar
            ? _fixture.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache.AcquisitionDiagnostics
            : _fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.AcquisitionDiagnostics;
        return new Dictionary<string, long>
        {
            ["buildStarts"] = counters.BuildStarts,
            ["cacheCheckoutHits"] = counters.CacheCheckoutHits,
            ["sharedFollowerCheckouts"] = counters.SharedFollowerCheckouts,
            ["sharedCoverageMisses"] = counters.SharedCoverageMisses,
            ["promotions"] = counters.Promotions,
            ["completedQueries"] = counters.CompletedQueries,
            ["lookupProbes"] = counters.LookupProbes,
            ["endpointCandidates"] = counters.EndpointCandidates,
            ["expandedNodes"] = counters.ExpandedNodes,
            ["evaluatedEdges"] = counters.EvaluatedEdges,
            ["connectionLegs"] = counters.ConnectionLegs,
            ["transitionCandidates"] = counters.TransitionCandidates,
            ["transitionPairs"] = counters.TransitionPairs,
            ["primaryVolumeCandidates"] = counters.PrimaryVolumeCandidates,
            ["shortcutVolumeCandidates"] = counters.ShortcutVolumeCandidates,
            ["volumeUnionChecks"] = counters.VolumeUnionChecks,
            ["successfulDependencyMerges"] = counters.SuccessfulDependencyMerges,
            ["traceIntervals"] = counters.TraceIntervals,
            ["coveredVoxelIntervals"] = counters.CoveredVoxelIntervals,
            ["simplificationRays"] = counters.SimplificationRays,
        };
    }

    private int RelevantCacheCount() => _algorithm == PathAlgorithm.AStar
        ? _fixture.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache.Count
        : _fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.Count;

    private void AssertEmptyCaches() => SwiftThrowHelper.ThrowIfTrue(
        _fixture.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache.Count != 0
        || _fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.Count != 0,
        "A cold episode must begin with genuinely empty payload caches.");

    private void AssertDrained()
    {
        var astar = _fixture.Context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        var flow = _fixture.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache;
        SwiftThrowHelper.ThrowIfTrue(astar.ActiveLeaseCount != 0 || astar.ReservedLeaseCount != 0
            || astar.ReservedPayloadBytes != 0 || astar.LeasedBytes != 0 || astar.DetachedBytes != 0
            || flow.ActiveLeaseCount != 0 || flow.ReservedLeaseCount != 0
            || flow.ReservedPayloadBytes != 0 || flow.LeasedBytes != 0 || flow.DetachedBytes != 0
            || _fixture.Context.Guides.Scheduler.RetainedRequestCount != 0
            || _fixture.Context.Guides.Scheduler.RetainedDescriptorBytes != 0,
            "Recovery episode leaked leases, reservations, detached payloads, or scheduled ownership.");
    }

    private static int CollectionMask(int gen0, int gen1, int gen2) =>
        (GC.CollectionCount(0) != gen0 ? 1 : 0) | (GC.CollectionCount(1) != gen1 ? 2 : 0)
        | (GC.CollectionCount(2) != gen2 ? 4 : 0);
    private static long Fold(long hash, long value) => unchecked((hash ^ value) * 1_099_511_628_211L);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            foreach (BenchmarkNavigator agent in _agents) agent?.Reset();
            AssertDrained();
        }
        finally { _fixture.Teardown(); _process.Dispose(); }
    }
}

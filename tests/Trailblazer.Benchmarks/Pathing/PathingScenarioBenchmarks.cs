using BenchmarkDotNet.Attributes;
using FixedMathSharp;
using System;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks.Pathing;

/// <summary>
/// Route-shape counters returned by pathing scenario preflight helpers.
/// </summary>
public readonly struct PathingScenarioSummary
{
    internal PathingScenarioSummary(
        int requestsAttempted = 0,
        int requestsCreated = 0,
        int cacheKeysRead = 0,
        int guidesResolved = 0,
        int failedRoutes = 0,
        int chartUpdates = 0,
        int fieldsVisited = 0,
        int maxPathSearchRange = 0,
        int extraFloodRange = 0,
        long reachabilitySnapshotBuilds = 0,
        int reachabilitySnapshotsRetained = 0,
        int reachabilityScratchCapacity = 0)
    {
        RequestsAttempted = requestsAttempted;
        RequestsCreated = requestsCreated;
        CacheKeysRead = cacheKeysRead;
        GuidesResolved = guidesResolved;
        FailedRoutes = failedRoutes;
        ChartUpdates = chartUpdates;
        FieldsVisited = fieldsVisited;
        MaxPathSearchRange = maxPathSearchRange;
        ExtraFloodRange = extraFloodRange;
        ReachabilitySnapshotBuilds = reachabilitySnapshotBuilds;
        ReachabilitySnapshotsRetained = reachabilitySnapshotsRetained;
        ReachabilityScratchCapacity = reachabilityScratchCapacity;
    }

    public int RequestsAttempted { get; }

    public int RequestsCreated { get; }

    public int CacheKeysRead { get; }

    public int GuidesResolved { get; }

    public int FailedRoutes { get; }

    public int ChartUpdates { get; }

    public int FieldsVisited { get; }

    /// <summary>
    /// Gets the effective search range carried by the measured flow-field flood request.
    /// </summary>
    public int MaxPathSearchRange { get; }

    /// <summary>
    /// Gets the extra flood range carried by the measured flow-field flood request.
    /// </summary>
    public int ExtraFloodRange { get; }

    /// <summary>
    /// Gets the reachability snapshots built by the measured operation.
    /// </summary>
    public long ReachabilitySnapshotBuilds { get; }

    /// <summary>
    /// Gets the reachability snapshot keys retained after the measured operation.
    /// </summary>
    public int ReachabilitySnapshotsRetained { get; }

    /// <summary>
    /// Gets the retained reachability scratch container capacity after the measured operation.
    /// </summary>
    public int ReachabilityScratchCapacity { get; }
}

/// <summary>
/// Scenario-level pathing benchmarks for cache invalidation, shared flow fields,
/// reachability first hits, transition request churn, and flood-range scaling.
/// </summary>
[MemoryDiagnoser]
[BenchmarkCategory("Pathing", "Scenario")]
public class PathingScenarioBenchmarks
{
    public const int DynamicRepathWaveCount = 64;
    public const int FlowSharingCount100 = 100;
    public const int FlowSharingCount500 = 500;
    public const int ReachabilityComboCount = 4;
    public const int ReachabilityWorkloadComboCount = 32;
    public const int ReachabilitySteadyHitOperations = 1024;
    public const int TransitionChurnRequestCount = 64;

    private const int DynamicPlaneSize = 64;
    private const int FlowSharingPlaneSize = 64;
    private const int FlowSharing100BenchmarkBatches = 8192;
    private const int FlowSharing500BenchmarkBatches = 2048;
    private const int FloodOpen32Operations = 4;
    private const int FloodOpen64Operations = 2;
    private const int FloodBlocker64Operations = 2;
    private const string DynamicChartName = "ScenarioDynamic64";
    private const string ReachabilityChartName = "ScenarioReachabilitySplit";
    private const int DynamicToggleX = 32;
    private const int DynamicToggleY = 0;
    private const int DynamicToggleZ = 32;
    private const int ReachabilityInvalidateX = 0;
    private const int ReachabilityInvalidateY = 0;
    private const int ReachabilityInvalidateZ = 0;

    private static readonly Vector3d DynamicOffset = Vector3d.Zero;
    private static readonly Vector3d FlowSharingOffset = new(80, 0, 0);
    private static readonly Vector3d ReachabilityOffset = new(160, 0, 0);
    private static readonly Vector3d TransitionOffset = new(208, 0, 0);
    private static readonly Vector3d Flood32Offset = new(0, 0, 96);
    private static readonly Vector3d Flood64Offset = new(48, 0, 96);
    private static readonly Vector3d Flood128Offset = new(128, 0, 96);
    private static readonly Vector3d FloodBlocker64Offset = new(272, 0, 96);

    private BenchmarkPathFixture _fixture;

    private AStarPathRequest[] _dynamicRepathRequests;
    private AStarGuide[] _dynamicGuideBuffer;

    private FlowFieldPathRequest[] _flowSharingRequests;
    private FlowFieldGuide[] _flowSharingGuideBuffer;

    private AStarPathRequest[] _reachabilityRequests;
    private AStarPathRequest[] _reachabilityWorkloadRequests;

    private Vector3d _transitionOrigin;
    private Vector3d _transitionDestination;
    private AStarPathRequest _transitionAStarRequest;
    private FlowFieldPathRequest _transitionFlowFieldRequest;
    private int _requestKeySink;

    private FlowFieldPathRequest _floodOpen32Request;
    private FlowFieldPathRequest _floodOpen64Request;
    private FlowFieldPathRequest _floodOpen128Request;
    private FlowFieldPathRequest _floodBlocker64DefaultRequest;
    private FlowFieldPathRequest _floodBlocker64LargeRequest;

    [GlobalSetup]
    public void GlobalSetup()
    {
        _fixture = new BenchmarkPathFixture();
        _fixture.Setup(BenchmarkChartFactory.GridConfigForArea(maxXExclusive: 352, maxZExclusive: 240));

        SetupDynamicObstacleWave();
        SetupFlowFieldSharing();
        SetupReachabilityFirstHit();
        SetupTransitionRequests();
        SetupFlowFieldFloodSweep();
        ValidateConfiguredScenarios();
        _fixture.FlushGuideCache();
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        _fixture?.Teardown();
    }

    [IterationSetup(Targets = new[]
    {
        nameof(DynamicObstacleUpdate_RepathWave64)
    })]
    public void PrepareDynamicObstacleRepathWave()
    {
        PathManager.TryUpdateChartCell(
            DynamicChartName,
            DynamicToggleX,
            DynamicToggleY,
            DynamicToggleZ,
            NavigationChartCell.Empty);

        SeedAStarGuides(_dynamicRepathRequests);
    }

    [IterationSetup(Targets = new[]
    {
        nameof(FlowFieldSharing_100Starts),
        nameof(FlowFieldSharing_500Starts)
    })]
    public void PrepareFlowFieldSharing()
    {
        _fixture.FlushGuideCache();
        if (!PathGuideFactory.RequestGuide(_flowSharingRequests[0], out FlowFieldGuide guide))
            throw new InvalidOperationException("Preflight: flow-field sharing seed request failed.");

        PathGuideFactory.ReturnGuide(guide);
    }

    [IterationSetup(Targets = new[]
    {
        nameof(ReachabilityFirstHit_ClearanceCombos),
        nameof(ReachabilityFirstHit_WorkloadCombos)
    })]
    public void PrepareReachabilityFirstHit()
    {
        _fixture.FlushGuideCache();
        PathManager.TryUpdateChartCell(
            ReachabilityChartName,
            ReachabilityInvalidateX,
            ReachabilityInvalidateY,
            ReachabilityInvalidateZ,
            NavigationChartCell.Empty);
        PathManager.TryUpdateChartCell(
            ReachabilityChartName,
            ReachabilityInvalidateX,
            ReachabilityInvalidateY,
            ReachabilityInvalidateZ,
            NavigationChartCell.Solid);
    }

    [IterationSetup(Targets = new[]
    {
        nameof(ReachabilitySteadyHit_ActiveCombo)
    })]
    public void PrepareReachabilitySteadyHit()
    {
        PrepareReachabilityFirstHit();
        RequestExpectedUnreachable(_reachabilityWorkloadRequests[0], "reachability steady seed");
    }

    [IterationSetup(Targets = new[]
    {
        nameof(ReachabilityInvalidate_ActiveSnapshot)
    })]
    public void PrepareReachabilityInvalidation()
    {
        PrepareReachabilityFirstHit();
        MeasureReachabilityFirstHitWorkloadCombos();
    }

    /// <summary>
    /// Applies one chart update, invalidates guide cache entries for that chart, and resolves a
    /// 64-request A* repath wave.
    /// </summary>
    [Benchmark(OperationsPerInvoke = DynamicRepathWaveCount)]
    [BenchmarkCategory("Pathing", "Scenario", "Dynamic", "AStar")]
    public int DynamicObstacleUpdate_RepathWave64()
    {
        return MeasureDynamicObstacleUpdateRepathWave().GuidesResolved;
    }

    /// <summary>
    /// Requests guides for 100 starts sharing one cached flow-field destination.
    /// </summary>
    [Benchmark(OperationsPerInvoke = FlowSharingCount100 * FlowSharing100BenchmarkBatches)]
    [InvocationCount(1)]
    [BenchmarkCategory("Pathing", "Scenario", "FlowField", "Sharing")]
    public int FlowFieldSharing_100Starts()
    {
        return MeasureFlowFieldSharingBatched(FlowSharingCount100, FlowSharing100BenchmarkBatches).GuidesResolved;
    }

    /// <summary>
    /// Requests guides for 500 starts sharing one cached flow-field destination.
    /// </summary>
    [Benchmark(OperationsPerInvoke = FlowSharingCount500 * FlowSharing500BenchmarkBatches)]
    [InvocationCount(1)]
    [BenchmarkCategory("Pathing", "Scenario", "FlowField", "Sharing")]
    public int FlowFieldSharing_500Starts()
    {
        return MeasureFlowFieldSharingBatched(FlowSharingCount500, FlowSharing500BenchmarkBatches).GuidesResolved;
    }

    /// <summary>
    /// First unreachable-route checks for distinct unit-size and climb-height snapshot keys.
    /// </summary>
    [Benchmark(OperationsPerInvoke = ReachabilityComboCount)]
    [BenchmarkCategory("Pathing", "Scenario", "Reachability")]
    public int ReachabilityFirstHit_ClearanceCombos()
    {
        return MeasureReachabilityFirstHitClearanceCombos().FailedRoutes;
    }

    /// <summary>
    /// First unreachable-route checks for a wider host-shaped set of snapshot keys.
    /// </summary>
    [Benchmark(OperationsPerInvoke = ReachabilityWorkloadComboCount)]
    [BenchmarkCategory("Pathing", "Scenario", "Reachability")]
    public int ReachabilityFirstHit_WorkloadCombos()
    {
        return MeasureReachabilityFirstHitWorkloadCombos().FailedRoutes;
    }

    /// <summary>
    /// Repeats the active unreachable-route snapshot key to measure steady hit cost.
    /// </summary>
    [Benchmark(OperationsPerInvoke = ReachabilitySteadyHitOperations)]
    [InvocationCount(1)]
    [BenchmarkCategory("Pathing", "Scenario", "Reachability")]
    public int ReachabilitySteadyHit_ActiveCombo()
    {
        return MeasureReachabilitySteadyHitActiveCombo().FailedRoutes;
    }

    /// <summary>
    /// Invalidates an active reachability snapshot through deterministic chart updates.
    /// </summary>
    [Benchmark(OperationsPerInvoke = 2)]
    [InvocationCount(1)]
    [BenchmarkCategory("Pathing", "Scenario", "Reachability", "Invalidation")]
    public int ReachabilityInvalidate_ActiveSnapshot()
    {
        return MeasureReachabilityInvalidation().ChartUpdates;
    }

    /// <summary>
    /// Constructs an A* transition-aware jump-link request.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Scenario", "Transition", "Request")]
    public AStarPathRequest TransitionRequestConstruction_AStarJumpLink()
    {
        return AStarPathRequest.Create(_fixture.Context,
            _transitionOrigin,
            _transitionDestination,
            Fixed64.One,
            allowTraversalTransitions: true);
    }

    /// <summary>
    /// Reads the cache key for a pre-created A* transition-aware jump-link request.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Scenario", "Transition", "Request", "Key")]
    public int TransitionRequestCacheKey_AStarJumpLink()
    {
        return _transitionAStarRequest.RequestCacheKey;
    }

    /// <summary>
    /// Constructs a flow-field transition-aware jump-link request.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Scenario", "Transition", "Request")]
    public FlowFieldPathRequest TransitionRequestConstruction_FlowFieldJumpLink()
    {
        return FlowFieldPathRequest.Create(_fixture.Context,
            _transitionOrigin,
            _transitionDestination,
            Fixed64.One,
            allowTraversalTransitions: true);
    }

    /// <summary>
    /// Reads the cache key for a pre-created flow-field transition-aware jump-link request.
    /// </summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Scenario", "Transition", "Request", "Key")]
    public int TransitionRequestCacheKey_FlowFieldJumpLink()
    {
        return _transitionFlowFieldRequest.RequestCacheKey;
    }

    /// <summary>
    /// Simulates a host creating transition-aware requests every fixed step.
    /// </summary>
    [Benchmark(OperationsPerInvoke = TransitionChurnRequestCount)]
    [BenchmarkCategory("Pathing", "Scenario", "Transition", "Request", "Churn")]
    public int TransitionRequestChurn_64Requests()
    {
        return MeasureTransitionRequestChurn().RequestsCreated;
    }

    /// <summary>Raw flow-field flood on a 32x32 open plane, batched four times.</summary>
    [Benchmark(OperationsPerInvoke = FloodOpen32Operations)]
    [BenchmarkCategory("Pathing", "Scenario", "FlowField", "FloodRange")]
    public int FlowFieldFloodRange_OpenPlane32()
    {
        return MeasureFlowFieldFloodOpen32().FieldsVisited;
    }

    /// <summary>Raw flow-field flood on a 64x64 open plane, batched twice.</summary>
    [Benchmark(OperationsPerInvoke = FloodOpen64Operations)]
    [BenchmarkCategory("Pathing", "Scenario", "FlowField", "FloodRange")]
    public int FlowFieldFloodRange_OpenPlane64()
    {
        return MeasureFlowFieldFloodOpen64().FieldsVisited;
    }

    /// <summary>Raw flow-field flood on a 128x128 open plane.</summary>
    [Benchmark]
    [BenchmarkCategory("Pathing", "Scenario", "FlowField", "FloodRange")]
    public int FlowFieldFloodRange_OpenPlane128()
    {
        return MeasureFlowFieldFloodOpen128().FieldsVisited;
    }

    /// <summary>Raw flow-field flood on a 64x64 blocker field with default range.</summary>
    [Benchmark(OperationsPerInvoke = FloodBlocker64Operations)]
    [BenchmarkCategory("Pathing", "Scenario", "FlowField", "FloodRange")]
    public int FlowFieldFloodRange_Blocker64Default()
    {
        return MeasureFlowFieldFloodBlocker64Default().FieldsVisited;
    }

    /// <summary>Raw flow-field flood on a 64x64 blocker field with enlarged range.</summary>
    [Benchmark(OperationsPerInvoke = FloodBlocker64Operations)]
    [BenchmarkCategory("Pathing", "Scenario", "FlowField", "FloodRange")]
    public int FlowFieldFloodRange_Blocker64Large()
    {
        return MeasureFlowFieldFloodBlocker64Large().FieldsVisited;
    }

    public PathingScenarioSummary MeasureDynamicObstacleUpdateRepathWave()
    {
        int changed = PathManager.TryUpdateChartCell(
            DynamicChartName,
            DynamicToggleX,
            DynamicToggleY,
            DynamicToggleZ,
            NavigationChartCell.Solid)
            ? 1
            : 0;

        int resolved = RequestAStarGuides(_dynamicRepathRequests, _dynamicGuideBuffer, _dynamicRepathRequests.Length);
        return new PathingScenarioSummary(
            requestsAttempted: _dynamicRepathRequests.Length,
            guidesResolved: resolved,
            chartUpdates: changed);
    }

    public PathingScenarioSummary MeasureFlowFieldSharing100() =>
        MeasureFlowFieldSharing(FlowSharingCount100);

    public PathingScenarioSummary MeasureFlowFieldSharing500() =>
        MeasureFlowFieldSharing(FlowSharingCount500);

    private PathingScenarioSummary MeasureFlowFieldSharingBatched(int count, int batchCount)
    {
        int resolved = 0;
        for (int i = 0; i < batchCount; i++)
            resolved += MeasureFlowFieldSharing(count).GuidesResolved;

        return new PathingScenarioSummary(
            requestsAttempted: count * batchCount,
            guidesResolved: resolved);
    }

    public PathingScenarioSummary MeasureReachabilityFirstHitClearanceCombos()
    {
        return MeasureReachabilityRequests(_reachabilityRequests, _reachabilityRequests.Length);
    }

    public PathingScenarioSummary MeasureReachabilityFirstHitWorkloadCombos()
    {
        return MeasureReachabilityRequests(_reachabilityWorkloadRequests, _reachabilityWorkloadRequests.Length);
    }

    public PathingScenarioSummary MeasureReachabilitySteadyHitActiveCombo()
    {
        return MeasureReachabilityRequestRepeated(
            _reachabilityWorkloadRequests[0],
            ReachabilitySteadyHitOperations);
    }

    public PathingScenarioSummary MeasureReachabilityInvalidation()
    {
        int changed = PathManager.TryUpdateChartCell(
            ReachabilityChartName,
            ReachabilityInvalidateX,
            ReachabilityInvalidateY,
            ReachabilityInvalidateZ,
            NavigationChartCell.Empty)
            ? 1
            : 0;

        changed += PathManager.TryUpdateChartCell(
            ReachabilityChartName,
            ReachabilityInvalidateX,
            ReachabilityInvalidateY,
            ReachabilityInvalidateZ,
            NavigationChartCell.Solid)
            ? 1
            : 0;

        SolidPartitionReachability.SolidPartitionReachabilityStats stats =
            SolidPartitionReachability.CaptureStats();

        return new PathingScenarioSummary(
            chartUpdates: changed,
            reachabilitySnapshotsRetained: stats.ActiveSnapshotCount,
            reachabilityScratchCapacity: GetReachabilityScratchCapacity(stats));
    }

    private static PathingScenarioSummary MeasureReachabilityRequests(
        AStarPathRequest[] requests,
        int count)
    {
        SolidPartitionReachability.SolidPartitionReachabilityStats before =
            SolidPartitionReachability.CaptureStats();

        int failed = 0;
        for (int i = 0; i < count; i++)
        {
            bool ok = PathGuideFactory.RequestGuide(requests[i], out AStarGuide guide);
            if (ok)
            {
                PathGuideFactory.ReturnGuide(guide);
                continue;
            }

            failed++;
        }

        SolidPartitionReachability.SolidPartitionReachabilityStats after =
            SolidPartitionReachability.CaptureStats();

        return new PathingScenarioSummary(
            requestsAttempted: count,
            failedRoutes: failed,
            reachabilitySnapshotBuilds: after.SnapshotBuildCount - before.SnapshotBuildCount,
            reachabilitySnapshotsRetained: after.ActiveSnapshotCount,
            reachabilityScratchCapacity: GetReachabilityScratchCapacity(after));
    }

    private static PathingScenarioSummary MeasureReachabilityRequestRepeated(
        AStarPathRequest request,
        int count)
    {
        SolidPartitionReachability.SolidPartitionReachabilityStats before =
            SolidPartitionReachability.CaptureStats();

        int failed = 0;
        for (int i = 0; i < count; i++)
        {
            bool ok = PathGuideFactory.RequestGuide(request, out AStarGuide guide);
            if (ok)
            {
                PathGuideFactory.ReturnGuide(guide);
                continue;
            }

            failed++;
        }

        SolidPartitionReachability.SolidPartitionReachabilityStats after =
            SolidPartitionReachability.CaptureStats();

        return new PathingScenarioSummary(
            requestsAttempted: count,
            failedRoutes: failed,
            reachabilitySnapshotBuilds: after.SnapshotBuildCount - before.SnapshotBuildCount,
            reachabilitySnapshotsRetained: after.ActiveSnapshotCount,
            reachabilityScratchCapacity: GetReachabilityScratchCapacity(after));
    }

    private static int GetReachabilityScratchCapacity(
        SolidPartitionReachability.SolidPartitionReachabilityStats stats)
    {
        return stats.PassablePartitionCapacity
            + stats.ComponentRootCapacity
            + stats.ComponentQueueCapacity;
    }

    public PathingScenarioSummary MeasureTransitionRequestChurn()
    {
        int created = 0;
        int keysRead = 0;
        int checksum = 0;

        for (int i = 0; i < TransitionChurnRequestCount; i++)
        {
            if ((i & 1) == 0)
            {
                AStarPathRequest request = AStarPathRequest.Create(_fixture.Context,
                    _transitionOrigin,
                    _transitionDestination,
                    Fixed64.One,
                    allowTraversalTransitions: true);
                if (request == null)
                    continue;

                created++;
                keysRead++;
                checksum ^= request.RequestCacheKey;
            }
            else
            {
                FlowFieldPathRequest request = FlowFieldPathRequest.Create(_fixture.Context,
                    _transitionOrigin,
                    _transitionDestination,
                    Fixed64.One,
                    allowTraversalTransitions: true);
                if (request == null)
                    continue;

                created++;
                keysRead++;
                checksum ^= request.RequestCacheKey;
            }
        }

        _requestKeySink = checksum;
        return new PathingScenarioSummary(
            requestsCreated: created,
            cacheKeysRead: keysRead);
    }

    public PathingScenarioSummary MeasureFlowFieldFloodOpen32() =>
        MeasureFlowFieldFlood(_floodOpen32Request, FloodOpen32Operations);

    public PathingScenarioSummary MeasureFlowFieldFloodOpen64() =>
        MeasureFlowFieldFlood(_floodOpen64Request, FloodOpen64Operations);

    public PathingScenarioSummary MeasureFlowFieldFloodOpen128() =>
        MeasureFlowFieldFlood(_floodOpen128Request, 1);

    public PathingScenarioSummary MeasureFlowFieldFloodBlocker64Default() =>
        MeasureFlowFieldFlood(_floodBlocker64DefaultRequest, FloodBlocker64Operations);

    public PathingScenarioSummary MeasureFlowFieldFloodBlocker64Large() =>
        MeasureFlowFieldFlood(_floodBlocker64LargeRequest, FloodBlocker64Operations);

    private void SetupDynamicObstacleWave()
    {
        BenchmarkChartFactory.RegisterOpenPlane(DynamicChartName, DynamicPlaneSize, DynamicOffset);
        Vector3d[] starts = BenchmarkChartFactory.GenerateUniqueStartPositions(
            DynamicPlaneSize,
            DynamicRepathWaveCount,
            out Vector3d destination,
            DynamicOffset);

        _dynamicRepathRequests = new AStarPathRequest[DynamicRepathWaveCount];
        _dynamicGuideBuffer = new AStarGuide[DynamicRepathWaveCount];
        for (int i = 0; i < DynamicRepathWaveCount; i++)
        {
            _dynamicRepathRequests[i] = AStarPathRequest.Create(_fixture.Context, starts[i], destination, Fixed64.One);
            if (_dynamicRepathRequests[i] == null)
                throw new InvalidOperationException($"Preflight: dynamic A* request {i} could not be created.");
        }
    }

    private void SetupFlowFieldSharing()
    {
        var (starts, destination) = BenchmarkChartFactory.RegisterDestinationCluster(
            "ScenarioFlowShare64",
            FlowSharingPlaneSize,
            FlowSharingCount500,
            FlowSharingOffset);

        _flowSharingRequests = new FlowFieldPathRequest[FlowSharingCount500];
        _flowSharingGuideBuffer = new FlowFieldGuide[FlowSharingCount500];
        for (int i = 0; i < FlowSharingCount500; i++)
        {
            _flowSharingRequests[i] = FlowFieldPathRequest.Create(_fixture.Context, starts[i], destination, Fixed64.One);
            if (_flowSharingRequests[i] == null)
                throw new InvalidOperationException($"Preflight: flow-sharing request {i} could not be created.");
        }
    }

    private void SetupReachabilityFirstHit()
    {
        var (start, end) = RegisterSplitSolidIslands(ReachabilityChartName, ReachabilityOffset);

        _reachabilityRequests = new AStarPathRequest[ReachabilityComboCount];
        _reachabilityRequests[0] = CreateReachabilityRequest(start, end, Fixed64.One, Fixed64.Zero);
        _reachabilityRequests[1] = CreateReachabilityRequest(start, end, Fixed64.One, Fixed64.One);
        _reachabilityRequests[2] = CreateReachabilityRequest(start, end, (Fixed64)2, Fixed64.Zero);
        _reachabilityRequests[3] = CreateReachabilityRequest(start, end, (Fixed64)2, Fixed64.One);

        _reachabilityWorkloadRequests = new AStarPathRequest[ReachabilityWorkloadComboCount];
        for (int i = 0; i < _reachabilityWorkloadRequests.Length; i++)
        {
            Fixed64 unitSize = (Fixed64)(1 + (i & 3));
            Fixed64 maxClimbHeight = (Fixed64)(i >> 2);
            _reachabilityWorkloadRequests[i] =
                CreateReachabilityRequest(start, end, unitSize, maxClimbHeight);
        }
    }

    private void SetupTransitionRequests()
    {
        RegisterTwoPointIsland("ScenarioTransitionJumpA", TransitionOffset);
        RegisterTwoPointIsland("ScenarioTransitionJumpB", TransitionOffset + new Vector3d(3, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "scenario-transition-jump",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(TransitionOffset + new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(TransitionOffset + new Vector3d(3, 0, 0)),
            pathCostModifier: 4));

        _transitionOrigin = TransitionOffset;
        _transitionDestination = TransitionOffset + new Vector3d(4, 0, 0);

        _transitionAStarRequest = AStarPathRequest.Create(_fixture.Context,
            _transitionOrigin,
            _transitionDestination,
            Fixed64.One,
            allowTraversalTransitions: true);
        _transitionFlowFieldRequest = FlowFieldPathRequest.Create(_fixture.Context,
            _transitionOrigin,
            _transitionDestination,
            Fixed64.One,
            allowTraversalTransitions: true);

        if (_transitionAStarRequest == null || _transitionFlowFieldRequest == null)
            throw new InvalidOperationException("Preflight: transition request setup failed.");
    }

    private void SetupFlowFieldFloodSweep()
    {
        var (open32Origin, open32Destination) =
            BenchmarkChartFactory.RegisterOpenPlane("ScenarioFloodOpen32", 32, Flood32Offset);
        var (open64Origin, open64Destination) =
            BenchmarkChartFactory.RegisterOpenPlane("ScenarioFloodOpen64", 64, Flood64Offset);
        var (open128Origin, open128Destination) =
            BenchmarkChartFactory.RegisterOpenPlane("ScenarioFloodOpen128", 128, Flood128Offset);
        var (blockerOrigin, blockerDestination) =
            BenchmarkChartFactory.RegisterSparseBlockerField("ScenarioFloodBlocker64", 64, FloodBlocker64Offset);

        _floodOpen32Request = CreateFlowRequest(open32Origin, open32Destination, "open32");
        _floodOpen64Request = CreateFlowRequest(open64Origin, open64Destination, "open64");
        _floodOpen128Request = CreateFlowRequest(open128Origin, open128Destination, "open128");
        _floodBlocker64DefaultRequest = CreateFlowRequest(blockerOrigin, blockerDestination, "blocker64-default");
        _floodBlocker64DefaultRequest.ExtraFloodRange = FlowFieldPathRequest.DefaultExtraFloodRange;
        _floodBlocker64LargeRequest = CreateFlowRequest(blockerOrigin, blockerDestination, "blocker64-large");
        _floodBlocker64LargeRequest.ExtraFloodRange = FlowFieldPathRequest.DefaultExtraFloodRange * 4;
    }

    private void ValidateConfiguredScenarios()
    {
        EnsureAStarGuideResolves(_dynamicRepathRequests[0], "dynamic wave first");
        EnsureAStarGuideResolves(_dynamicRepathRequests[_dynamicRepathRequests.Length - 1], "dynamic wave last");

        PrepareFlowFieldSharing();
        PathingScenarioSummary sharing = MeasureFlowFieldSharing(FlowSharingCount100);
        if (sharing.GuidesResolved != FlowSharingCount100)
            throw new InvalidOperationException("Preflight: flow-field sharing scenario did not resolve all sampled starts.");

        PrepareReachabilityFirstHit();
        PathingScenarioSummary reachability = MeasureReachabilityFirstHitClearanceCombos();
        if (reachability.FailedRoutes != ReachabilityComboCount)
            throw new InvalidOperationException("Preflight: reachability split-island routes unexpectedly resolved.");

        PrepareReachabilityFirstHit();
        PathingScenarioSummary reachabilityWorkload = MeasureReachabilityFirstHitWorkloadCombos();
        if (reachabilityWorkload.FailedRoutes != ReachabilityWorkloadComboCount
            || reachabilityWorkload.ReachabilitySnapshotsRetained != 1)
        {
            throw new InvalidOperationException("Preflight: reachability workload split-island routes unexpectedly resolved.");
        }

        PrepareReachabilitySteadyHit();
        PathingScenarioSummary reachabilitySteady = MeasureReachabilitySteadyHitActiveCombo();
        if (reachabilitySteady.FailedRoutes != ReachabilitySteadyHitOperations
            || reachabilitySteady.ReachabilitySnapshotBuilds != 0)
        {
            throw new InvalidOperationException("Preflight: reachability steady-hit scenario rebuilt unexpectedly.");
        }

        PrepareReachabilityInvalidation();
        if (MeasureReachabilityInvalidation().ReachabilitySnapshotsRetained != 0)
            throw new InvalidOperationException("Preflight: reachability invalidation retained an active snapshot.");

        EnsureAStarGuideResolves(_transitionAStarRequest, "transition A* jump");
        EnsureFlowFieldGuideResolves(_transitionFlowFieldRequest, "transition flow-field jump");

        EnsureFlowSurveyHasPath(_floodOpen32Request, "flood open32");
        EnsureFlowSurveyHasPath(_floodOpen64Request, "flood open64");
        EnsureFlowSurveyHasPath(_floodBlocker64DefaultRequest, "flood blocker64");
    }

    private PathingScenarioSummary MeasureFlowFieldSharing(int count)
    {
        FlowFieldGuide[] guides = _flowSharingGuideBuffer;
        int resolved = 0;

        for (int i = 0; i < count; i++)
        {
            if (PathGuideFactory.RequestGuide(_flowSharingRequests[i], out FlowFieldGuide guide))
                guides[resolved++] = guide;
        }

        for (int i = 0; i < resolved; i++)
        {
            PathGuideFactory.ReturnGuide(guides[i]);
            guides[i] = null;
        }

        return new PathingScenarioSummary(
            requestsAttempted: count,
            guidesResolved: resolved);
    }

    private static int RequestAStarGuides(
        AStarPathRequest[] requests,
        AStarGuide[] guides,
        int count)
    {
        int resolved = 0;
        for (int i = 0; i < count; i++)
        {
            if (PathGuideFactory.RequestGuide(requests[i], out AStarGuide guide))
                guides[resolved++] = guide;
        }

        for (int i = 0; i < resolved; i++)
        {
            PathGuideFactory.ReturnGuide(guides[i]);
            guides[i] = null;
        }

        return resolved;
    }

    private static void SeedAStarGuides(AStarPathRequest[] requests)
    {
        for (int i = 0; i < requests.Length; i++)
            EnsureAStarGuideResolves(requests[i], $"seed A* request {i}");
    }

    private static PathingScenarioSummary MeasureFlowFieldFlood(
        FlowFieldPathRequest request,
        int operations)
    {
        int fieldsVisited = 0;
        int paths = 0;
        for (int i = 0; i < operations; i++)
        {
            FlowFieldSurveyResult result = FlowFieldSurveyor.Shared.FindPath(request);
            if (!result.HasPath)
                continue;

            fieldsVisited += result.Fields?.Count ?? 0;
            paths++;
        }

        return new PathingScenarioSummary(
            requestsAttempted: operations,
            guidesResolved: paths,
            fieldsVisited: fieldsVisited,
            maxPathSearchRange: request.MaxPathSearchRange,
            extraFloodRange: request.ExtraFloodRange);
    }

    private AStarPathRequest CreateReachabilityRequest(
        Vector3d start,
        Vector3d end,
        Fixed64 unitSize,
        Fixed64 maxClimbHeight)
    {
        AStarPathRequest request = AStarPathRequest.Create(_fixture.Context, start, end, unitSize)
            ?? throw new InvalidOperationException(
                $"Preflight: reachability request could not be created for unit size {unitSize}.");

        request.MaxClimbHeight = maxClimbHeight;
        return request;
    }

    private FlowFieldPathRequest CreateFlowRequest(Vector3d start, Vector3d end, string name)
    {
        return FlowFieldPathRequest.Create(_fixture.Context, start, end, Fixed64.One)
            ?? throw new InvalidOperationException($"Preflight: flow-field request '{name}' could not be created.");
    }

    private static void EnsureAStarGuideResolves(AStarPathRequest request, string requestName)
    {
        if (!PathGuideFactory.RequestGuide(request, out AStarGuide guide))
            throw new InvalidOperationException($"Preflight: {requestName} A* guide failed.");

        PathGuideFactory.ReturnGuide(guide);
    }

    private static void RequestExpectedUnreachable(AStarPathRequest request, string requestName)
    {
        if (PathGuideFactory.RequestGuide(request, out AStarGuide guide))
        {
            PathGuideFactory.ReturnGuide(guide);
            throw new InvalidOperationException($"Preflight: {requestName} A* guide unexpectedly resolved.");
        }
    }

    private static void EnsureFlowFieldGuideResolves(FlowFieldPathRequest request, string requestName)
    {
        if (!PathGuideFactory.RequestGuide(request, out FlowFieldGuide guide))
            throw new InvalidOperationException($"Preflight: {requestName} flow-field guide failed.");

        PathGuideFactory.ReturnGuide(guide);
    }

    private static void EnsureFlowSurveyHasPath(FlowFieldPathRequest request, string requestName)
    {
        FlowFieldSurveyResult result = FlowFieldSurveyor.Shared.FindPath(request);
        if (!result.HasPath)
            throw new InvalidOperationException($"Preflight: {requestName} raw flow-field survey failed.");
    }

    private static (Vector3d Start, Vector3d End) RegisterSplitSolidIslands(
        string chartName,
        Vector3d origin)
    {
        const int width = 20;
        const int depth = 12;
        bool[,,] data = new bool[1, width, depth];
        for (int x = 0; x < width; x++)
        {
            for (int z = 0; z < depth; z++)
                data[0, x, z] = x <= 7 || x >= 12;
        }

        NavigationChart chart = NavigationChart.From3D(chartName, data, origin, Fixed64.One);
        PathManager.Register(chart);

        return (
            origin + new Vector3d(3, 0, depth / 2),
            origin + new Vector3d(16, 0, depth / 2));
    }

    private static void RegisterTwoPointIsland(string chartName, Vector3d origin)
    {
        bool[,,] data = new bool[1, 2, 1];
        data[0, 0, 0] = true;
        data[0, 1, 0] = true;

        NavigationChart chart = NavigationChart.From3D(chartName, data, origin, Fixed64.One);
        PathManager.Register(chart);
    }
}

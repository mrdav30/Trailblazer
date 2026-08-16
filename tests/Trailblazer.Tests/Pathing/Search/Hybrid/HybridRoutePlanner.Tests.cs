using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class HybridRoutePlannerTests : IDisposable
{
    private readonly GridConfiguration _configuration;

    public HybridRoutePlannerTests()
    {
        TestWorld.Setup();
        _configuration = new GridConfiguration(
            new Vector3d(-8, -8, -8),
            new Vector3d(16, 16, 16));
        TestWorld.World.TryAddGrid(_configuration, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TraversalTransitionRegistry.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void RouteTotal_ShouldPreserveFractionalFixedPointValues()
    {
        HybridRouteStep step = HybridRouteStep.Waypoint(
            TestWorld.Context,
            Vector3d.Right);
        var plan = new HybridRoutePlan(
            new[] { step },
            Array.Empty<TraversalTransition>(),
            Fixed64.Half);

        plan.TotalPathCost.Should().Be(Fixed64.Half);
    }

    [Fact]
    public void SurfaceStep_ShouldRetainTheExactGraphFlowQuery()
    {
        var query = new PathQuery(
            new NavigationEndpoint(
                Vector3d.Zero,
                "surface-start",
                EndpointResolutionPolicy.NearestNavigable,
                Fixed64.Half),
            new NavigationEndpoint(
                Vector3d.Right,
                "surface-end",
                EndpointResolutionPolicy.Strict,
                Fixed64.Zero),
            PathTestFactory.DefaultNavigationProfile,
            new NavigationAreaPolicyKey("hybrid-policy", 7),
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.FlowField,
            new NavigationWorkBudget(11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21),
            allowTransitions: false,
            new FlowFieldQueryOptions(Fixed64.Half));

        HybridRouteStep step = HybridRouteStep.Surface(TestWorld.Context, query);

        step.SurfaceQuery.Should().Be(query);
        step.VolumeRequest.Should().BeNull();
        step.Context.Should().BeSameAs(TestWorld.Context);
    }

    [Fact]
    public void Create_ShouldRejectAnExplicitQueryWhenNoGraphSurfaceRouteExists()
    {
        PathQuery query = CreateGraphFlowQuery(Vector3d.Zero, Vector3d.Right);

        HybridPathRequest.Create(TestWorld.Context, query).Should().BeNull();
    }

    [Fact]
    public void Create_ShouldBuildSurfaceVolumeSurfaceStagesWithoutRetainingPlanningLeases()
    {
        HybridRoutePlan plan = CreateGraphHybridPlan();

        plan.Steps.Should().HaveCount(5);
        plan.Steps[0].SurfaceQuery.Should().NotBeNull();
        plan.Steps[0].SurfaceQuery!.Value.AllowTransitions.Should().BeFalse();
        plan.Steps[2].VolumeRequest.Should().NotBeNull();
        plan.Steps[4].SurfaceQuery.Should().NotBeNull();
        plan.TotalPathCost.Should().BeGreaterThan(Fixed64.Zero);
        TestWorld.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void HybridRouteGuide_ShouldSequenceSurfaceVolumeSurfaceAndReleaseEachOwnedResource()
    {
        HybridRoutePlan plan = CreateGraphHybridPlan();
        var guide = new HybridRouteGuide(plan);
        var sampleBudget = new GuideSampleWorkBudget(0, 0, 0, 0, 0, 0, 0);

        NavigationGuideStatus firstStatus = guide.TrySample(
                GraphFootPosition("graph-hybrid", SurfacePosition(0)),
                sampleBudget,
                out Vector3d firstSurfaceHeading);
        firstStatus.Should().Be(NavigationGuideStatus.BudgetExceeded, "the first surface sample has a zero budget");
        firstSurfaceHeading.Should().Be(Vector3d.Zero);
        TestWorld.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(1);
        TestWorld.Context.Guides.InUseVolumeGuideCount.Should().Be(0);

        NavigationGuideStatus entryStatus = guide.TrySample(
                plan.Steps[0].SurfaceQuery!.Value.End.Position,
                sampleBudget,
                out Vector3d entryHeading);
        entryStatus.Should().Be(NavigationGuideStatus.Success, "the reached surface stage must release before the entry waypoint");
        entryHeading.X.Should().BeGreaterThan(Fixed64.Zero);
        TestWorld.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);

        NavigationGuideStatus volumeStatus = guide.TrySample(
                plan.Steps[1].WaypointPosition,
                sampleBudget,
                out Vector3d volumeHeading);
        volumeStatus.Should().Be(NavigationGuideStatus.Success, "the reached entry waypoint must advance to the volume stage");
        volumeHeading.X.Should().BeGreaterThan(Fixed64.Zero);
        TestWorld.Context.Guides.InUseVolumeGuideCount.Should().Be(1);

        NavigationGuideStatus exitStatus = guide.TrySample(
                plan.Steps[2].VolumeRequest!.TargetPosition,
                sampleBudget,
                out Vector3d exitHeading);
        exitStatus.Should().Be(NavigationGuideStatus.Success, "the reached volume stage must release before the exit waypoint");
        exitHeading.X.Should().BeGreaterThan(Fixed64.Zero);
        TestWorld.Context.Guides.InUseVolumeGuideCount.Should().Be(0);

        NavigationGuideStatus finalStatus = guide.TrySample(
                GraphFootPosition("graph-hybrid", plan.Steps[4].SurfaceQuery!.Value.Start.Position),
                sampleBudget,
                out Vector3d finalSurfaceHeading);
        finalStatus.Should().Be(NavigationGuideStatus.BudgetExceeded, "the final surface sample has a zero budget");
        finalSurfaceHeading.Should().Be(Vector3d.Zero);
        TestWorld.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(1);

        guide.Dispose();

        TestWorld.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);
        TestWorld.Context.Guides.InUseVolumeGuideCount.Should().Be(0);
    }

    private HybridRoutePlan CreateGraphHybridPlan()
    {
        PathTestFactory.RegisterLineChart(TestWorld.Context, "GraphHybridStart", SurfacePosition(0), 3);
        PathTestFactory.RegisterLineChart(TestWorld.Context, "GraphHybridEnd", SurfacePosition(5), 3);
        PathTestFactory.RegisterVolumeLine(
            TestWorld.Context,
            SurfacePosition(3),
            TraversalMedium.Liquid,
            2,
            "GraphHybridLiquid");

        const string mapId = "graph-hybrid";
        NavigationAreaPolicyKey policyKey = PublishSurfaceGraph(
            mapId,
            SurfacePosition(0),
            SurfacePosition(1),
            SurfacePosition(2),
            SurfacePosition(5),
            SurfacePosition(6),
            SurfacePosition(7));
        TestWorld.Context.Transitions.Register(new TraversalTransition(
            "graph-hybrid-entry",
            TraversalTransitionType.SwimEntry,
            TraversalTransitionAnchor.Solid(SurfacePosition(2)),
            TraversalTransitionAnchor.Liquid(SurfacePosition(3)),
            pathCostModifier: 1)).Should().BeTrue();
        TestWorld.Context.Transitions.Register(new TraversalTransition(
            "graph-hybrid-exit",
            TraversalTransitionType.SwimExit,
            TraversalTransitionAnchor.Liquid(SurfacePosition(4)),
            TraversalTransitionAnchor.Solid(SurfacePosition(5)),
            pathCostModifier: 1)).Should().BeTrue();
        PathQuery query = CreateGraphFlowQuery(SurfacePosition(0), SurfacePosition(7), mapId, policyKey);

        return TestRequire.NotNull(
            HybridPathRequest.Create(TestWorld.Context, query)).RoutePlan!;
    }

    /// <summary>
    /// Exercises the single-transition solid->solid route when direct chart travel is impossible.
    /// This covers the main TryPlanSingleTransition success path.
    /// </summary>
    /// <summary>
    /// Exercises TryCreateVolumeStep (Liquid medium) and the chart-step zero-displacement path.
    /// Origin→entry and exit→target are zero-displacement chart hops (same position) which hit the
    /// HybridRouteStep.Waypoint shortcut. The liquid volume segment between them hits TryCreateVolumeStep.
    /// </summary>
    /// <summary>
    /// Exercises TryCreateVolumeStep (Gas medium) and the gas-based TryPlanTransitionPairForMedium path.
    /// </summary>
    /// <summary>
    /// Exercises the branch that compares both local gas and liquid transition-pair plans and keeps
    /// the cheaper candidate through GetBetterPlan.
    /// </summary>
    /// <summary>
    /// Exercises the chart step with zero displacement: when origin == destination, the step
    /// becomes a waypoint rather than a path segment.
    /// </summary>
    [Fact]
    public void TryPlan_ShouldRejectNullRequests()
    {
        HybridRoutePlanner.TryPlan(null!, out HybridRoutePlan? plan).Should().BeFalse();
        plan.Should().BeNull();
    }

    private static PathQuery CreateGraphFlowQuery(
        Vector3d origin,
        Vector3d destination,
        string? mapId = null,
        NavigationAreaPolicyKey? policyKey = null) => new(
        new NavigationEndpoint(origin, mapId),
        new NavigationEndpoint(destination, mapId),
        new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Quarter, Fixed64.One, Fixed64.Quarter),
            Fixed64.One,
            Fixed64.One,
            Fixed64.Half,
            TraversalMedia.Solid | TraversalMedia.Gas | TraversalMedia.Liquid,
            TraversalCapability.Jump
                | TraversalCapability.Climb
                | TraversalCapability.Swim
                | TraversalCapability.Fly),
        policyKey ?? new NavigationAreaPolicyKey("hybrid-policy", 1),
        new TraversalIntent(
            TraversalDomain.Surface,
            TraversalMedium.Solid,
            TraversalDomain.Surface),
        PathAlgorithm.FlowField,
        new NavigationWorkBudget(64, 8, 64, 256, 8, 16, 16, 8, 0, 0, 0),
        allowTransitions: true,
        new FlowFieldQueryOptions(Fixed64.Half));

    private static Vector3d SurfacePosition(int x) =>
        new((Fixed64)x + Fixed64.Half, Fixed64.Zero, Fixed64.Half);

    private static Vector3d GraphFootPosition(string mapId, Vector3d position)
    {
        VoxelIndex index = PathTestFactory.RequireVoxel(TestWorld.Context, position).WorldIndex.VoxelIndex;
        TestWorld.Context.Pathing.NavigationGraphStore.Current.TryGetNodeRef(
                new NavigationCellAddress(mapId, index),
                out NavigationNodeRef nodeRef)
            .Should().BeTrue();
        TestWorld.Context.Pathing.NavigationGraphStore.Current.TryGetNodeState(
                nodeRef,
                out NavigationNodeState node)
            .Should().BeTrue();
        return node.FootAnchor;
    }

    private NavigationAreaPolicyKey PublishSurfaceGraph(string mapId, params Vector3d[] positions)
    {
        _configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var builder = new NavigationMapBuilder(mapId, binding);
        var cell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        for (int i = 0; i < positions.Length; i++)
        {
            VoxelIndex index = PathTestFactory.RequireVoxel(TestWorld.Context, positions[i]).WorldIndex.VoxelIndex;
            builder.AddCell(index, cell);
        }

        var mapOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(builder.Build(), bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 1);
        var policyKey = new NavigationAreaPolicyKey(mapId, 1);
        var policyOperation = new NavigationAreaPolicyCommitOperation(
            new NavigationAreaPolicy(
                policyKey,
                new[] { new NavigationAreaRule(true, Fixed64.Zero) }),
            publicationSequence: 2,
            effectiveFrame: 1);
        TestWorld.Context.Pathing.Admit(mapOperation).Should().BeTrue();
        TestWorld.Context.Pathing.Admit(policyOperation).Should().BeTrue();
        for (int frame = 0;
             frame < 64
             && (mapOperation.Receipt.Status == NavigationOperationStatus.Pending
                 || policyOperation.Receipt.Status == NavigationOperationStatus.Pending);
             frame++)
        {
            TestWorld.Context.Simulate();
        }

        mapOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        policyOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        return policyKey;
    }

}

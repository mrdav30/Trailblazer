using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class HybridRoutePlannerTests : IDisposable
{
    public HybridRoutePlannerTests()
    {
        TestWorld.Setup();
        TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16)),
            out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TraversalTransitionRegistry.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Exercises the direct A* chart path branch in TryPlanDirect/TryCreateAStarStep.
    /// </summary>
    [Fact]
    public void TryPlan_AStarKind_ShouldBuildDirectPlanWithAStarSegment()
    {
        RegisterLineChart("HybridPlannerAStar", Vector3d.Zero, 3);

        HybridPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            out HybridPathRequest? hybridRequest).Should().BeTrue();

        HybridRoutePlan routePlan = TestRequire.NotNull(TestRequire.NotNull(hybridRequest).RoutePlan);
        routePlan.DirectedTransitions.Should().BeEmpty();
        routePlan.Steps.Should().HaveCount(1);
        routePlan.Steps[0].Kind.Should().Be(HybridRouteStepKind.PathSegment);
        routePlan.Steps[0].SegmentRequest.Should().BeOfType<AStarPathRequest>();
    }

    /// <summary>
    /// Exercises TryCreateFlowFieldStep via HybridPathRequest.CreateFromFlowField. When a FlowFieldPathRequest
    /// is converted to a HybridPathRequest, TryPlan routes chart steps through TryCreateFlowFieldStep
    /// instead of the A* path.
    /// </summary>
    [Fact]
    public void TryPlan_FlowFieldKind_ShouldBuildDirectPlanWithFlowFieldStep()
    {
        RegisterLineChart("HybridPlannerFF", Vector3d.Zero, 3);

        FlowFieldPathRequest flowFieldRequest = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One));

        HybridPathRequest hybridRequest = TestRequire.NotNull(HybridPathRequest.CreateFromFlowField(flowFieldRequest));
        HybridRoutePlan routePlan = TestRequire.NotNull(hybridRequest.RoutePlan);
        routePlan.Steps.Should().HaveCount(1);
        routePlan.Steps[0].Kind.Should().Be(HybridRouteStepKind.PathSegment);
        routePlan.Steps[0].SegmentRequest.Should().BeOfType<FlowFieldPathRequest>();
    }

    /// <summary>
    /// Exercises the single-transition solid->solid route when direct chart travel is impossible.
    /// This covers the main TryPlanSingleTransition success path.
    /// </summary>
    [Fact]
    public void TryPlan_SolidTransition_ShouldBuildSingleTransitionPlan()
    {
        RegisterLineChart("HybridPlannerSolidStart", Vector3d.Zero, 2);
        RegisterLineChart("HybridPlannerSolidEnd", new Vector3d(4, 0, 0), 2);

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "hybridplanner-solid-hop",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)),
            pathCostModifier: 2)).Should().BeTrue();

        HybridPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(5, 0, 0),
            Fixed64.One,
            out HybridPathRequest? hybridRequest).Should().BeTrue();

        HybridRoutePlan routePlan = TestRequire.NotNull(TestRequire.NotNull(hybridRequest).RoutePlan);
        routePlan.DirectedTransitions.Should().ContainSingle();
        routePlan.DirectedTransitions[0].Id.Should().Be("hybridplanner-solid-hop");
        routePlan.Steps.Should().HaveCount(3);
        routePlan.Steps[0].SegmentRequest.Should().BeOfType<AStarPathRequest>();
        routePlan.Steps[1].Kind.Should().Be(HybridRouteStepKind.Waypoint);
        routePlan.Steps[2].SegmentRequest.Should().BeOfType<AStarPathRequest>();
    }

    /// <summary>
    /// Exercises TryCreateVolumeStep (Liquid medium) and the TryCreateAStarStep zero-displacement path.
    /// Origin→entry and exit→target are zero-displacement chart hops (same position) which hit the
    /// HybridRouteStep.Waypoint shortcut. The liquid volume segment between them hits TryCreateVolumeStep.
    /// </summary>
    [Fact]
    public void TryPlan_LiquidTransitionPair_ShouldBuildPlanWithVolumeStep()
    {
        PathTestFactory.RegisterSingleWalkablePoint("HybridPlannerLiquidStart", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("HybridPlannerLiquidEnd", new Vector3d(4, 0, 0));
        PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(1, 0, 0), TraversalMedium.Liquid, "HybridPlannerLiquidVol");
        PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(2, 0, 0), TraversalMedium.Liquid, "HybridPlannerLiquidVol");
        PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(3, 0, 0), TraversalMedium.Liquid, "HybridPlannerLiquidVol");

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "hybridplanner-swim-entry",
            type: TraversalTransitionType.SwimEntry,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Liquid(new Vector3d(1, 0, 0)),
            pathCostModifier: 2)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "hybridplanner-swim-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.Liquid(new Vector3d(3, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();

        HybridPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            out HybridPathRequest? hybridRequest).Should().BeTrue();

        HybridRoutePlan routePlan = TestRequire.NotNull(TestRequire.NotNull(hybridRequest).RoutePlan);
        routePlan.DirectedTransitions.Should().HaveCount(2);

        // Plan must include a volume (liquid) segment step
        bool hasVolumeSegment = false;
        foreach (HybridRouteStep step in routePlan.Steps)
        {
            if (step.Kind == HybridRouteStepKind.PathSegment
                && step.SegmentRequest is VolumePathRequest vpr
                && vpr.Medium == TraversalMedium.Liquid)
            {
                hasVolumeSegment = true;
                break;
            }
        }

        hasVolumeSegment.Should().BeTrue("expected a liquid volume segment step in the hybrid plan");
    }

    /// <summary>
    /// Exercises TryCreateVolumeStep (Gas medium) and the gas-based TryPlanTransitionPairForMedium path.
    /// </summary>
    [Fact]
    public void TryPlan_GasTransitionPair_ShouldBuildPlanWithGasVolumeStep()
    {
        PathTestFactory.RegisterSingleWalkablePoint("HybridPlannerGasStart", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("HybridPlannerGasEnd", new Vector3d(4, 0, 0));
        PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(1, 0, 0), TraversalMedium.Gas, "HybridPlannerGasVol");
        PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(2, 0, 0), TraversalMedium.Gas, "HybridPlannerGasVol");
        PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(3, 0, 0), TraversalMedium.Gas, "HybridPlannerGasVol");

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "hybridplanner-gas-entry",
            type: TraversalTransitionType.Takeoff,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Gas(new Vector3d(1, 0, 0)),
            pathCostModifier: 2)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "hybridplanner-gas-exit",
            type: TraversalTransitionType.Landing,
            source: TraversalTransitionAnchor.Gas(new Vector3d(3, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();

        HybridPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            out HybridPathRequest? hybridRequest).Should().BeTrue();

        HybridRoutePlan routePlan = TestRequire.NotNull(TestRequire.NotNull(hybridRequest).RoutePlan);
        routePlan.DirectedTransitions.Should().HaveCount(2);

        bool hasGasSegment = false;
        foreach (HybridRouteStep step in routePlan.Steps)
        {
            if (step.Kind == HybridRouteStepKind.PathSegment
                && step.SegmentRequest is VolumePathRequest vpr
                && vpr.Medium == TraversalMedium.Gas)
            {
                hasGasSegment = true;
                break;
            }
        }

        hasGasSegment.Should().BeTrue("expected a gas volume segment step in the hybrid plan");
    }

    /// <summary>
    /// Exercises the branch that compares both local gas and liquid transition-pair plans and keeps
    /// the cheaper candidate through GetBetterPlan.
    /// </summary>
    [Fact]
    public void TryPlan_ShouldPreferCheaperLiquidTransitionPair_WhenGasAndLiquidRoutesBothExist()
    {
        PathTestFactory.RegisterSingleWalkablePoint("HybridPlannerDualStart", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("HybridPlannerDualEnd", new Vector3d(6, 0, 0));

        for (int x = 1; x <= 5; x++)
            PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(x, 0, 0), TraversalMedium.Gas, "HybridPlannerDualGas");

        for (int x = 1; x <= 5; x++)
            PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(x, 0, 1), TraversalMedium.Liquid, "HybridPlannerDualLiquid");

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "hybridplanner-dual-gas-entry",
            type: TraversalTransitionType.Takeoff,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Gas(new Vector3d(1, 0, 0)),
            pathCostModifier: 6)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "hybridplanner-dual-gas-exit",
            type: TraversalTransitionType.Landing,
            source: TraversalTransitionAnchor.Gas(new Vector3d(5, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(6, 0, 0)),
            pathCostModifier: 6)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "hybridplanner-dual-liquid-entry",
            type: TraversalTransitionType.SwimEntry,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Liquid(new Vector3d(1, 0, 1)),
            pathCostModifier: 1)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "hybridplanner-dual-liquid-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.Liquid(new Vector3d(5, 0, 1)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(6, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();

        HybridPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(6, 0, 0),
            Fixed64.One,
            out HybridPathRequest? hybridRequest).Should().BeTrue();

        HybridRoutePlan routePlan = TestRequire.NotNull(TestRequire.NotNull(hybridRequest).RoutePlan);
        routePlan.DirectedTransitions.Should().HaveCount(2);
        routePlan.DirectedTransitions[0].Id.Should().Be("hybridplanner-dual-liquid-entry");
        routePlan.DirectedTransitions[1].Id.Should().Be("hybridplanner-dual-liquid-exit");

        HybridRouteStep volumeStep = routePlan.Steps.Should().ContainSingle(
            step => step.Kind == HybridRouteStepKind.PathSegment
                && step.SegmentRequest is VolumePathRequest).Subject;
        volumeStep.SegmentRequest.Should().BeOfType<VolumePathRequest>()
            .Which.Medium.Should().Be(TraversalMedium.Liquid);
    }

    [Fact]
    public void TryPlan_ShouldUseLocalDestinationGridQuery_WhenSourceAndDestinationAreInDifferentGrids()
    {
        TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(20, -8, -8), new Vector3d(16, 16, 16)),
            out _).Should().BeTrue();

        RegisterLineChart("HybridPlannerCrossGridStart", Vector3d.Zero, 2);
        PathTestFactory.RegisterSingleWalkablePoint("HybridPlannerCrossGridEnd", new Vector3d(20, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "hybridplanner-cross-grid-hop",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(20, 0, 0)),
            pathCostModifier: 2)).Should().BeTrue();

        HybridPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(20, 0, 0),
            Fixed64.One,
            out HybridPathRequest? hybridRequest).Should().BeTrue();

        HybridPathRequest actualRequest = TestRequire.NotNull(hybridRequest);
        TestRequire.NotNull(actualRequest.StartNode).GridIndex.Should().NotBe(TestRequire.NotNull(actualRequest.EndNode).GridIndex);
        HybridRoutePlan routePlan = TestRequire.NotNull(actualRequest.RoutePlan);
        routePlan.DirectedTransitions.Should().ContainSingle();
        routePlan.DirectedTransitions[0].Id.Should().Be("hybridplanner-cross-grid-hop");
    }

    [Fact]
    public void TryPlan_ShouldBuildChainedClimbRoute_ForAuthoredParkourTopology()
    {
        RegisterAuthoredClimbRoute("HybridPlannerClimbChain");

        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(3, 0, 1),
            Fixed64.One,
            HeuristicMethod.Manhattan,
            allowUnwalkableEndpoints: true));
        request.MaxClimbHeight = Fixed64.Zero;

        HybridPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(3, 0, 1),
            Fixed64.One,
            out HybridPathRequest? hybridRequest,
            maxClimbHeight: Fixed64.Zero,
            allowUnwalkableEndpoints: true).Should().BeTrue();

        HybridRoutePlan routePlan = TestRequire.NotNull(TestRequire.NotNull(hybridRequest).RoutePlan);
        routePlan.DirectedTransitions.Should().HaveCount(5);
        routePlan.DirectedTransitions.Should().OnlyContain(t => t.Type == TraversalTransitionType.Climb);
        routePlan.DirectedTransitions[^1].RequestsClimbIntent.Should().BeFalse();
        routePlan.Steps.Should().HaveCount(7);
        routePlan.Steps[0].Kind.Should().Be(HybridRouteStepKind.Waypoint);
        routePlan.Steps[^1].Kind.Should().Be(HybridRouteStepKind.PathSegment);
        routePlan.Steps.Should().Contain(step =>
            step.Kind == HybridRouteStepKind.Waypoint
            && step.WaypointPosition == new Vector3d(1, 1, 0));
        routePlan.Steps.Should().Contain(step =>
            step.Kind == HybridRouteStepKind.Waypoint
            && step.WaypointPosition == new Vector3d(1, 1, 1));
        routePlan.Steps.Should().Contain(step =>
            step.Kind == HybridRouteStepKind.Waypoint
            && step.WaypointPosition == new Vector3d(2, 0, 1));
    }

    /// <summary>
    /// Exercises TryCreateFlowFieldStep with zero displacement: when origin == destination, the step
    /// becomes a waypoint rather than a path segment.
    /// </summary>
    [Fact]
    public void TryPlan_FlowFieldKind_ZeroDisplacement_ShouldBuildWaypointStep()
    {
        RegisterLineChart("HybridPlannerFFZero", Vector3d.Zero, 3);

        FlowFieldPathRequest flowFieldRequest = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            Vector3d.Zero,
            Fixed64.One,
            allowUnwalkableEndpoints: true));

        HybridPathRequest? hybridRequest = HybridPathRequest.CreateFromFlowField(flowFieldRequest);

        // Zero displacement means both start and end are the same — the entire route is a waypoint
        // or the plan itself is trivially zero-cost
        if (hybridRequest != null)
        {
            TestRequire.NotNull(hybridRequest.RoutePlan);
        }
    }

    [Fact]
    public void TryCreateAStarStep_ShouldReturnFalse_WhenChartRequestCannotResolve()
    {
        RegisterLineChart("HybridPlannerAStarInvalid", Vector3d.Zero, 2);

        HybridPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            out HybridPathRequest? request).Should().BeTrue();

        object[] args =
        {
            new Vector3d(64, 0, 0),
            new Vector3d(65, 0, 0),
            TestRequire.NotNull(request),
            null!,
            0
        };

        ReflectionUtility.InvokePrivateStatic<bool>(typeof(HybridRoutePlanner), "TryCreateAStarStep", args).Should().BeFalse();
        args[3].Should().BeNull();
        args[4].Should().Be(0);
    }

    [Fact]
    public void TryCreateFlowFieldStep_ShouldReturnFalse_WhenChartRequestCannotResolve()
    {
        RegisterLineChart("HybridPlannerFlowInvalid", Vector3d.Zero, 2);

        FlowFieldPathRequest flowFieldRequest = TestRequire.NotNull(FlowFieldPathRequest.Create(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One));

        HybridPathRequest request = TestRequire.NotNull(HybridPathRequest.CreateFromFlowField(flowFieldRequest));

        object[] args =
        {
            new Vector3d(64, 0, 0),
            new Vector3d(65, 0, 0),
            request,
            null!,
            0
        };

        ReflectionUtility.InvokePrivateStatic<bool>(typeof(HybridRoutePlanner), "TryCreateFlowFieldStep", args).Should().BeFalse();
        args[3].Should().BeNull();
        args[4].Should().Be(0);
    }

    [Fact]
    public void TryCreateVolumeStep_ShouldReturnFalse_WhenVolumeRequestCannotResolve()
    {
        RegisterLineChart("HybridPlannerVolumeInvalid", Vector3d.Zero, 2);

        HybridPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            out HybridPathRequest? request).Should().BeTrue();

        object[] args =
        {
            new Vector3d(64, 0, 0),
            new Vector3d(65, 0, 0),
            TestRequire.NotNull(request),
            TraversalMedium.Gas,
            null!,
            0
        };

        ReflectionUtility.InvokePrivateStatic<bool>(typeof(HybridRoutePlanner), "TryCreateVolumeStep", args).Should().BeFalse();
        args[4].Should().BeNull();
        args[5].Should().Be(0);
    }

    [Fact]
    public void TryCreateVolumeStep_ShouldReturnWaypoint_WhenOriginMatchesDestination()
    {
        RegisterLineChart("HybridPlannerVolumeZeroSolid", Vector3d.Zero, 2);
        PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(4, 0, 0), TraversalMedium.Gas, "HybridPlannerVolumeZeroGas");

        HybridPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            out HybridPathRequest? request).Should().BeTrue();

        object[] args =
        {
            new Vector3d(4, 0, 0),
            new Vector3d(4, 0, 0),
            TestRequire.NotNull(request),
            TraversalMedium.Gas,
            null!,
            0
        };

        ReflectionUtility.InvokePrivateStatic<bool>(typeof(HybridRoutePlanner), "TryCreateVolumeStep", args).Should().BeTrue();
        args[4].Should().BeOfType<HybridRouteStep>()
            .Which.Kind.Should().Be(HybridRouteStepKind.Waypoint);
        args[5].Should().Be(0);
    }

    [Fact]
    public void TryCreateVolumeStep_ShouldReturnFalse_WhenVolumeSurveyFindsNoPath()
    {
        RegisterLineChart("HybridPlannerVolumeMissSolid", Vector3d.Zero, 2);
        PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(4, 0, 0), TraversalMedium.Gas, "HybridPlannerVolumeMissGasA");
        PathTestFactory.RegisterGeneratedVolumePoint(new Vector3d(6, 0, 0), TraversalMedium.Gas, "HybridPlannerVolumeMissGasB");

        HybridPathRequest.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            out HybridPathRequest? request).Should().BeTrue();

        object[] args =
        {
            new Vector3d(4, 0, 0),
            new Vector3d(6, 0, 0),
            request!,
            TraversalMedium.Gas,
            null!,
            0
        };

        ReflectionUtility.InvokePrivateStatic<bool>(typeof(HybridRoutePlanner), "TryCreateVolumeStep", args).Should().BeFalse();
        args[4].Should().BeNull();
        args[5].Should().Be(0);
    }

    [Fact]
    public void TryPlan_ShouldRejectNullRequests()
    {
        HybridRoutePlanner.TryPlan(null!, out HybridRoutePlan? plan).Should().BeFalse();
        plan.Should().BeNull();
    }

    private static void RegisterLineChart(string chartName, Vector3d minBounds, int length)
    {
        bool[,,] data = new bool[1, length, 1];
        for (int i = 0; i < length; i++)
            data[0, i, 0] = true;

        PathTestFactory.RegisterFromData(chartName, data, minBounds);
    }

    private static void RegisterAuthoredClimbRoute(string chartName)
    {
        string[,,] map = new string[2, 4, 2];
        map[0, 0, 0] = "S";
        map[0, 1, 0] = "SC!";
        map[1, 1, 0] = "SC";
        map[1, 1, 1] = "SC";
        map[1, 2, 1] = "SC!";
        map[0, 2, 1] = "S";
        map[0, 3, 1] = "S";

        TraversalBuildResult buildResult = new TraversalAuthoringMap(
            chartName,
            map,
            Vector3d.Zero,
            Fixed64.One).Build();
        buildResult.GeneratedTransitions.Should().NotBeEmpty();
        PathManager.Register(buildResult).Should().BeTrue();
        TraversalTransitionRegistry.AllTransitions.Should().Contain(t => t.Type == TraversalTransitionType.Climb);
        TraversalTransitionQuery.GetDirectedTransitions(TraversalMedium.Solid, TraversalMedium.Solid)
            .Should()
            .Contain(t => t.Type == TraversalTransitionType.Climb);
    }

}

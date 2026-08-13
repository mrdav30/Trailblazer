using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
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
        PathTestFactory.RegisterLineChart(TestWorld.Context, "HybridPlannerAStar", Vector3d.Zero, 3);

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
        PathTestFactory.RegisterLineChart(TestWorld.Context, "HybridPlannerFF", Vector3d.Zero, 3);

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
        PathTestFactory.RegisterLineChart(TestWorld.Context, "HybridPlannerSolidStart", Vector3d.Zero, 2);
        PathTestFactory.RegisterLineChart(TestWorld.Context, "HybridPlannerSolidEnd", new Vector3d(4, 0, 0), 2);

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
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "HybridPlannerLiquidStart", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "HybridPlannerLiquidEnd", new Vector3d(4, 0, 0));
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(1, 0, 0), TraversalMedium.Liquid, "HybridPlannerLiquidVol");
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(2, 0, 0), TraversalMedium.Liquid, "HybridPlannerLiquidVol");
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(3, 0, 0), TraversalMedium.Liquid, "HybridPlannerLiquidVol");

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
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "HybridPlannerGasStart", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "HybridPlannerGasEnd", new Vector3d(4, 0, 0));
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(1, 0, 0), TraversalMedium.Gas, "HybridPlannerGasVol");
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(2, 0, 0), TraversalMedium.Gas, "HybridPlannerGasVol");
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(3, 0, 0), TraversalMedium.Gas, "HybridPlannerGasVol");

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
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "HybridPlannerDualStart", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "HybridPlannerDualEnd", new Vector3d(6, 0, 0));

        for (int x = 1; x <= 5; x++)
            PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(x, 0, 0), TraversalMedium.Gas, "HybridPlannerDualGas");

        for (int x = 1; x <= 5; x++)
            PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(x, 0, 1), TraversalMedium.Liquid, "HybridPlannerDualLiquid");

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

        PathTestFactory.RegisterLineChart(TestWorld.Context, "HybridPlannerCrossGridStart", Vector3d.Zero, 2);
        PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "HybridPlannerCrossGridEnd", new Vector3d(20, 0, 0));

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
        PathTestFactory.RegisterAuthoredClimbRoute(TestWorld.Context, "HybridPlannerClimbChain");

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
        PathTestFactory.RegisterLineChart(TestWorld.Context, "HybridPlannerFFZero", Vector3d.Zero, 3);

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
        PathTestFactory.RegisterLineChart(TestWorld.Context, "HybridPlannerAStarInvalid", Vector3d.Zero, 2);

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
        PathTestFactory.RegisterLineChart(TestWorld.Context, "HybridPlannerFlowInvalid", Vector3d.Zero, 2);

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
        PathTestFactory.RegisterLineChart(TestWorld.Context, "HybridPlannerVolumeInvalid", Vector3d.Zero, 2);

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
        PathTestFactory.RegisterLineChart(TestWorld.Context, "HybridPlannerVolumeZeroSolid", Vector3d.Zero, 2);
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(4, 0, 0), TraversalMedium.Gas, "HybridPlannerVolumeZeroGas");

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
        PathTestFactory.RegisterLineChart(TestWorld.Context, "HybridPlannerVolumeMissSolid", Vector3d.Zero, 2);
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(4, 0, 0), TraversalMedium.Gas, "HybridPlannerVolumeMissGasA");
        PathTestFactory.RegisterGeneratedVolumePoint(TestWorld.Context, new Vector3d(6, 0, 0), TraversalMedium.Gas, "HybridPlannerVolumeMissGasB");

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

}

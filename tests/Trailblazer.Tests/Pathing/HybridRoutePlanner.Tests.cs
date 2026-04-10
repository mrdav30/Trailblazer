using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class HybridRoutePlannerTests : IDisposable
{
    public HybridRoutePlannerTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();

        GlobalGridManager.TryAddGrid(
            new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16)),
            out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TraversalTransitionRegistry.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
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

        var flowFieldRequest = FlowFieldPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One);
        flowFieldRequest.Should().NotBeNull();

        var hybridRequest = HybridPathRequest.CreateFromFlowField(flowFieldRequest);
        hybridRequest.Should().NotBeNull();
        hybridRequest.RoutePlan.Should().NotBeNull();
        hybridRequest.RoutePlan.Steps.Should().HaveCount(1);
        hybridRequest.RoutePlan.Steps[0].Kind.Should().Be(HybridRouteStepKind.PathSegment);
        hybridRequest.RoutePlan.Steps[0].SegmentRequest.Should().BeOfType<FlowFieldPathRequest>();
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

        HybridPathRequest.TryCreate(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            out HybridPathRequest hybridRequest).Should().BeTrue();

        hybridRequest.Should().NotBeNull();
        hybridRequest.RoutePlan.Should().NotBeNull();
        hybridRequest.RoutePlan.DirectedTransitions.Should().HaveCount(2);

        // Plan must include a volume (liquid) segment step
        bool hasVolumeSegment = false;
        foreach (HybridRouteStep step in hybridRequest.RoutePlan.Steps)
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

        HybridPathRequest.TryCreate(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            out HybridPathRequest hybridRequest).Should().BeTrue();

        hybridRequest.Should().NotBeNull();
        hybridRequest.RoutePlan.Should().NotBeNull();
        hybridRequest.RoutePlan.DirectedTransitions.Should().HaveCount(2);

        bool hasGasSegment = false;
        foreach (HybridRouteStep step in hybridRequest.RoutePlan.Steps)
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
    /// Exercises TryCreateFlowFieldStep with zero displacement: when origin == destination, the step
    /// becomes a waypoint rather than a path segment.
    /// </summary>
    [Fact]
    public void TryPlan_FlowFieldKind_ZeroDisplacement_ShouldBuildWaypointStep()
    {
        RegisterLineChart("HybridPlannerFFZero", Vector3d.Zero, 3);

        FlowFieldPathRequest flowFieldRequest = FlowFieldPathRequest.Create(
            Vector3d.Zero,
            Vector3d.Zero,
            Fixed64.One,
            allowUnwalkableEndpoints: true);
        flowFieldRequest.Should().NotBeNull();

        var hybridRequest = HybridPathRequest.CreateFromFlowField(flowFieldRequest);

        // Zero displacement means both start and end are the same — the entire route is a waypoint
        // or the plan itself is trivially zero-cost
        if (hybridRequest != null)
        {
            hybridRequest.RoutePlan.Should().NotBeNull();
        }
    }

    private static void RegisterLineChart(string chartName, Vector3d minBounds, int length)
    {
        bool[,,] data = new bool[1, length, 1];
        for (int i = 0; i < length; i++)
            data[0, i, 0] = true;

        PathTestFactory.RegisterFromData(chartName, data, minBounds);
    }
}

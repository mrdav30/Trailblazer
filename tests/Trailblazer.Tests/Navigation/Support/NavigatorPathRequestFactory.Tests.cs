using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using System;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

[Collection("PathingCollection")]
public sealed class NavigatorPathRequestFactoryTests : IDisposable
{
    public NavigatorPathRequestFactoryTests()
    {
        if (TrailblazerWorldManager.IsActive)
            TrailblazerWorldManager.Reset();
        else
            TrailblazerWorldManager.Setup();

        TrailblazerWorldManager.TryAddGrid(new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16)), out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TrailblazerWorldManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TryCreate_ShouldBuildDirectRequests_ForSupportedModes()
    {
        RegisterLineChart("NavigatorFactorySolid", Vector3d.Zero, 3);
        RegisterVolumeLine(new Vector3d(0, 0, 2), TraversalMedium.Gas, 3, "NavigatorFactoryGas");
        RegisterVolumeLine(new Vector3d(0, 0, 4), TraversalMedium.Liquid, 3, "NavigatorFactoryLiquid");

        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: true,
            allowTraversalTransitions: true,
            maxClimbHeight: (Fixed64)2,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 11,
            traversalMedium: TraversalMedium.Solid,
            out IPathRequest? aStarRequest).Should().BeTrue();

        aStarRequest.Should().BeOfType<AStarPathRequest>()
            .Which.MaxClimbHeight.Should().Be((Fixed64)2);

        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            GuidedPathMode.FlowField,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: (Fixed64)3,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 17,
            traversalMedium: TraversalMedium.Solid,
            out IPathRequest? flowFieldRequest).Should().BeTrue();

        flowFieldRequest.Should().BeOfType<FlowFieldPathRequest>().Which.ExtraFloodRange.Should().Be(17);

        NavigatorPathRequestFactory.TryCreate(
            new Vector3d(0, 0, 2),
            new Vector3d(2, 0, 2),
            Fixed64.One,
            GuidedPathMode.Aerial,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Gas,
            out IPathRequest? aerialRequest).Should().BeTrue();

        aerialRequest.Should().BeOfType<VolumePathRequest>().Which.Medium.Should().Be(TraversalMedium.Gas);

        NavigatorPathRequestFactory.TryCreate(
            new Vector3d(0, 0, 4),
            new Vector3d(2, 0, 4),
            Fixed64.One,
            GuidedPathMode.Swim,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Liquid,
            out IPathRequest? swimRequest).Should().BeTrue();

        swimRequest.Should().BeOfType<VolumePathRequest>().Which.Medium.Should().Be(TraversalMedium.Liquid);
    }

    [Fact]
    public void TryCreate_ShouldRejectInvalidModesAndSwimMediums()
    {
        RegisterVolumeLine(Vector3d.Zero, TraversalMedium.Gas, 2, "NavigatorFactoryRejectGas");

        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            GuidedPathMode.Swim,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Gas,
            out IPathRequest? swimRequest).Should().BeFalse();

        swimRequest.Should().BeNull();

        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            (GuidedPathMode)99,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Solid,
            out IPathRequest? invalidRequest).Should().BeFalse();

        invalidRequest.Should().BeNull();
    }

    [Fact]
    public void TryCreate_WithAerialMode_ShouldBuildLandingHandoff_WhenDirectFlightCannotReachTarget()
    {
        const string sceneKey = "NavigatorFactoryAerialHandoff";
        GuidedPathTestScene.RegisterAerialLandingHandoffScene(sceneKey);

        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            GuidedPathMode.Aerial,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: (Fixed64)2,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 9,
            traversalMedium: TraversalMedium.Gas,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        request.Should().BeOfType<VolumePathRequest>().Which.TargetPosition.Should().Be(new Vector3d(1, 0, 0));
        GuidedVolumeExitHandoff aerialHandoff = TestRequire.NotNull(handoff);
        aerialHandoff.TransitionId.Should().Be($"{sceneKey}-landing");
        aerialHandoff.ChartOriginPosition.Should().Be(new Vector3d(1, 0, 0));
        aerialHandoff.TargetPosition.Should().Be(new Vector3d(4, 0, 0));
        aerialHandoff.ChartPathMode.Should().Be(GuidedPathMode.AStar);
    }

    [Fact]
    public void TryCreate_WithAerialMode_ShouldNormalizeUnsupportedFallbackModesToAStar()
    {
        const string sceneKey = "NavigatorFactoryAerialNormalize";
        GuidedPathTestScene.RegisterAerialLandingHandoffScene(sceneKey);

        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            GuidedPathMode.Aerial,
            GuidedPathMode.Swim,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: (Fixed64)2,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Gas,
            out _,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        GuidedVolumeExitHandoff normalizedHandoff = TestRequire.NotNull(handoff);
        normalizedHandoff.ChartPathMode.Should().Be(GuidedPathMode.AStar);
    }

    [Fact]
    public void TryCreate_WithAerialMode_ShouldKeepDirectRequest_WhenTargetIsNotChartBacked()
    {
        RegisterVolumeLine(Vector3d.Zero, TraversalMedium.Gas, 3, "NavigatorFactoryDirectGas");

        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            GuidedPathMode.Aerial,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Gas,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        request.Should().BeOfType<VolumePathRequest>().Which.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        handoff.Should().BeNull();
    }

    [Fact]
    public void TryCreate_WithAerialMode_ShouldKeepDirectRequest_WhenDirectFlightIsCheaperThanLanding()
    {
        const string sceneKey = "NavigatorFactoryAerialDirectPreferred";
        GuidedPathTestScene.RegisterAerialLandingChoiceScene(sceneKey);

        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            GuidedPathMode.Aerial,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Gas,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        request.Should().BeOfType<VolumePathRequest>().Which.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        handoff.Should().BeNull();
    }

    [Fact]
    public void TryCreate_WithSwimMode_ShouldBuildExitHandoff_WhenSolidTargetRequiresOne()
    {
        const string sceneKey = "NavigatorFactorySwimHandoff";
        GuidedPathTestScene.RegisterVolumeExitHandoffScene(sceneKey);

        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            GuidedPathMode.Swim,
            GuidedPathMode.FlowField,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: (Fixed64)2,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 12,
            traversalMedium: TraversalMedium.Liquid,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        request.Should().BeOfType<VolumePathRequest>().Which.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        GuidedVolumeExitHandoff swimHandoff = TestRequire.NotNull(handoff);
        swimHandoff.TransitionId.Should().Be($"{sceneKey}-exit");
        swimHandoff.ChartOriginPosition.Should().Be(new Vector3d(2, 0, 0));
        swimHandoff.ChartPathMode.Should().Be(GuidedPathMode.FlowField);
        swimHandoff.FlowFieldExtraFloodRange.Should().Be(12);
    }

    [Fact]
    public void TryCreate_WithSwimMode_ShouldKeepDirectRequest_WhenTargetSupportsLiquid()
    {
        const string sceneKey = "NavigatorFactorySwimDirect";
        GuidedPathTestScene.RegisterChartBackedSwimTargetScene(sceneKey);

        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            GuidedPathMode.Swim,
            GuidedPathMode.FlowField,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 7,
            traversalMedium: TraversalMedium.Liquid,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        request.Should().BeOfType<VolumePathRequest>().Which.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        handoff.Should().BeNull();
    }

    [Fact]
    public void TryCreate_WithConstrainedVolumeExit_ShouldFail_WhenTraversalTransitionsAreDisabled()
    {
        const string sceneKey = "NavigatorFactorySwimDisabled";
        GuidedPathTestScene.RegisterVolumeExitHandoffScene(sceneKey);

        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            GuidedPathMode.Swim,
            GuidedPathMode.FlowField,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Liquid,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeFalse();

        request.Should().BeNull();
        handoff.Should().BeNull();
    }

    [Fact]
    public void TryCreate_Internal_Aerial_ShouldKeepDirectRequest_WhenTargetIsOutsideGridButEndpointSnaps()
    {
        RegisterVolumeLine(Vector3d.Zero, TraversalMedium.Gas, 3, "NavigatorFactoryOutsideGrid");

        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            new Vector3d(12, 0, 0),
            Fixed64.One,
            GuidedPathMode.Aerial,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: true,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Gas,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        VolumePathRequest volumeRequest = Assert.IsType<VolumePathRequest>(request);
        volumeRequest.TargetPosition.Should().Be(new Vector3d(12, 0, 0));
        TestRequire.NotNull(volumeRequest.EndNode).WorldPosition.Should().Be(new Vector3d(2, 0, 0));
        handoff.Should().BeNull();
    }

    [Fact]
    public void TryCreate_Internal_Aerial_ShouldKeepDirectRequest_WhenNoLandingHandoffCanBePlanned()
    {
        RegisterVolumeLine(Vector3d.Zero, TraversalMedium.Gas, 3, "NavigatorFactoryNoLandingGas");
        PathTestFactory.RegisterSingleTraversalPoint(
            "NavigatorFactoryNoLandingTarget",
            new Vector3d(2, 0, 0),
            TraversalMedia.Solid | TraversalMedia.Gas);

        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            GuidedPathMode.Aerial,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Gas,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        request.Should().BeOfType<VolumePathRequest>().Which.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        handoff.Should().BeNull();
    }

    [Fact]
    public void TryCreate_Internal_Aerial_ShouldBuildLandingHandoff_WhenDirectRequestSnapsFromSolidTarget()
    {
        const string sceneKey = "NavigatorFactorySnappedAerialHandoff";
        GuidedPathTestScene.RegisterAerialLandingHandoffScene(sceneKey);

        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            GuidedPathMode.Aerial,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: true,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Gas,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        request.Should().BeOfType<VolumePathRequest>().Which.TargetPosition.Should().Be(new Vector3d(1, 0, 0));
        GuidedVolumeExitHandoff snappedAerialHandoff = TestRequire.NotNull(handoff);
        snappedAerialHandoff.TransitionId.Should().Be($"{sceneKey}-landing");
        snappedAerialHandoff.ChartOriginPosition.Should().Be(new Vector3d(1, 0, 0));
    }

    [Fact]
    public void TryCreate_Internal_Swim_ShouldBuildExitHandoff_WhenDirectRequestSnapsFromSolidTarget()
    {
        const string sceneKey = "NavigatorFactorySnappedSwimHandoff";
        GuidedPathTestScene.RegisterVolumeExitHandoffScene(sceneKey);

        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            GuidedPathMode.Swim,
            GuidedPathMode.FlowField,
            allowUnwalkableEndpoints: true,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 4,
            traversalMedium: TraversalMedium.Liquid,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        request.Should().BeOfType<VolumePathRequest>().Which.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        GuidedVolumeExitHandoff snappedSwimHandoff = TestRequire.NotNull(handoff);
        snappedSwimHandoff.TransitionId.Should().Be($"{sceneKey}-exit");
        snappedSwimHandoff.ChartOriginPosition.Should().Be(new Vector3d(2, 0, 0));
    }

    /// <summary>
    /// Covers the null-request early-exit in both overloads of TryCreate for AStar and FlowField modes.
    /// When origin and destination are inside the grid but on no registered chart, request creation returns
    /// null and both methods return false.
    /// </summary>
    [Fact]
    public void TryCreate_ShouldReturnFalse_WhenNoChartCoversOriginOrDestination()
    {
        // No chart registered — positions are inside the grid but unreachable
        NavigatorPathRequestFactory.TryCreate(
            new Vector3d(-6, -6, -6),
            new Vector3d(-5, -6, -6),
            Fixed64.One,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Solid,
            out IPathRequest? aStarRequest).Should().BeFalse();

        aStarRequest.Should().BeNull();

        NavigatorPathRequestFactory.TryCreate(
            new Vector3d(-6, -6, -6),
            new Vector3d(-5, -6, -6),
            Fixed64.One,
            GuidedPathMode.FlowField,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Solid,
            out IPathRequest? flowFieldRequest).Should().BeFalse();

        flowFieldRequest.Should().BeNull();
    }

    /// <summary>
    /// Covers the null FlowField path in the internal TryCreate overload (with handoff parameter).
    /// When neither endpoint is on a chart and no transition plan can be built, the method returns false.
    /// </summary>
    [Fact]
    public void TryCreate_Internal_FlowField_ShouldReturnFalse_WhenNoChartCoversEndpoints()
    {
        NavigatorPathRequestFactory.TryCreate(
            new Vector3d(-6, -6, -6),
            new Vector3d(-5, -6, -6),
            Fixed64.One,
            GuidedPathMode.FlowField,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Solid,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeFalse();

        request.Should().BeNull();
        handoff.Should().BeNull();
    }

    /// <summary>
    /// Covers the Aerial null-volume path in the internal TryCreate overload: when the origin is not in
    /// a gas volume and no transition plan can be built, the method returns false.
    /// </summary>
    [Fact]
    public void TryCreate_Internal_Aerial_ShouldReturnFalse_WhenNotInGasVolumeAndNoHandoff()
    {
        // No gas volume registered — VolumePathRequest.Create returns null.
        // allowTraversalTransitions=false prevents the fallback handoff succeeding.
        NavigatorPathRequestFactory.TryCreate(
            new Vector3d(-6, -6, -6),
            new Vector3d(-5, -6, -6),
            Fixed64.One,
            GuidedPathMode.Aerial,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Gas,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeFalse();

        request.Should().BeNull();
        handoff.Should().BeNull();
    }

    /// <summary>
    /// Covers the Swim null-volume path in the internal TryCreate overload: when the origin is not in
    /// a liquid volume and no transition plan can be built, the method returns false.
    /// </summary>
    [Fact]
    public void TryCreate_Internal_Swim_ShouldReturnFalse_WhenNotInLiquidVolumeAndNoHandoff()
    {
        NavigatorPathRequestFactory.TryCreate(
            new Vector3d(-6, -6, -6),
            new Vector3d(-5, -6, -6),
            Fixed64.One,
            GuidedPathMode.Swim,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Liquid,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeFalse();

        request.Should().BeNull();
        handoff.Should().BeNull();
    }

    /// <summary>
    /// Covers TryCreateGasLandingHandoff returning false when medium is Liquid (not Gas).
    /// When the swim target has a solid partition that supports liquid, TryCreateGasLandingHandoff is
    /// invoked with TraversalMedium.Liquid and returns false immediately — causing the direct swim
    /// request to be kept with no handoff.
    /// </summary>
    [Fact]
    public void TryCreate_Internal_Swim_ShouldKeepDirectRequest_WhenTargetSupportsMedium_GasLandingHandoffSkipped()
    {
        const string sceneKey = "NavigatorFactorySwimGasLanding";
        GuidedPathTestScene.RegisterChartBackedSwimTargetScene(sceneKey);

        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            GuidedPathMode.Swim,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Liquid,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        // TryCreateGasLandingHandoff returns false for Liquid medium → direct request kept
        request.Should().BeOfType<VolumePathRequest>().Which.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        handoff.Should().BeNull();
    }

    [Fact]
    public void TryCreate_Internal_ShouldRejectInvalidModes()
    {
        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            (GuidedPathMode)123,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Solid,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeFalse();

        request.Should().BeNull();
        handoff.Should().BeNull();
    }

    [Fact]
    public void TryCreate_Internal_Aerial_ShouldKeepDirectRequest_WhenTargetVoxelDoesNotExist()
    {
        RegisterVolumeLine(Vector3d.Zero, TraversalMedium.Gas, 3, "NavigatorFactoryFarOutsideGrid");

        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            new Vector3d(64, 0, 0),
            Fixed64.One,
            GuidedPathMode.Aerial,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: true,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Gas,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        VolumePathRequest volumeRequest = Assert.IsType<VolumePathRequest>(request);
        TestRequire.NotNull(volumeRequest.EndNode).WorldPosition.Should().Be(new Vector3d(2, 0, 0));
        handoff.Should().BeNull();
    }

    [Fact]
    public void TryCreate_Internal_Aerial_ShouldPreferLandingHandoff_WhenTargetSharesGasVoxelButPositionDiffers()
    {
        const string sceneKey = "NavigatorFactoryPreciseLanding";
        RegisterGasLandingChoiceTargetScene(sceneKey, new Vector3d(2, 0, 0), authoredGasLength: 2);

        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            new Vector3d(2.25f, 0, 0),
            Fixed64.One,
            GuidedPathMode.Aerial,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Gas,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        Assert.IsType<VolumePathRequest>(request).TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        GuidedVolumeExitHandoff preciseLandingHandoff = TestRequire.NotNull(handoff);
        preciseLandingHandoff.TransitionId.Should().Be($"{sceneKey}-landing");
        preciseLandingHandoff.ChartOriginPosition.Should().Be(new Vector3d(2, 0, 0));
    }

    [Fact]
    public void TryCreate_Internal_Aerial_ShouldPreferLandingHandoff_WhenLandingIsCheaperThanDirectFlight()
    {
        const string sceneKey = "NavigatorFactoryCheaperLanding";
        GuidedPathTestScene.RegisterAerialLandingHandoffScene(sceneKey);
        VolumeMediumRules.SetGasVoxelRule(static voxel =>
            voxel != null && voxel.WorldPosition == new Vector3d(4, 0, 0));

        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            GuidedPathMode.Aerial,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Gas,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        request.Should().BeOfType<VolumePathRequest>().Which.TargetPosition.Should().Be(new Vector3d(1, 0, 0));
        GuidedVolumeExitHandoff cheaperLandingHandoff = TestRequire.NotNull(handoff);
        cheaperLandingHandoff.TransitionId.Should().Be($"{sceneKey}-landing");
    }

    [Fact]
    public void TryCreate_Internal_Aerial_ShouldKeepDirectRequest_WhenZeroDisplacementIsCheaperThanLanding()
    {
        const string sceneKey = "NavigatorFactoryZeroDisplacementLanding";
        RegisterGasLandingChoiceTargetScene(sceneKey, Vector3d.Zero, authoredGasLength: 0);

        NavigatorPathRequestFactory.TryCreate(
            Vector3d.Zero,
            Vector3d.Zero,
            Fixed64.One,
            GuidedPathMode.Aerial,
            GuidedPathMode.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            traversalMedium: TraversalMedium.Gas,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        request.Should().BeOfType<VolumePathRequest>().Which.TargetPosition.Should().Be(Vector3d.Zero);
        handoff.Should().BeNull();
    }

    private static void RegisterLineChart(string chartName, Vector3d minBounds, int length)
    {
        var data = new bool[1, length, 1];
        for (int i = 0; i < length; i++)
            data[0, i, 0] = true;

        PathTestFactory.RegisterFromData(chartName, data, minBounds);
    }

    private static void RegisterVolumeLine(Vector3d start, TraversalMedium medium, int length, string chartNamePrefix)
    {
        for (int i = 0; i < length; i++)
        {
            PathTestFactory.RegisterGeneratedVolumePoint(
                new Vector3d(start.x + i, start.y, start.z),
                medium,
                chartNamePrefix);
        }
    }

    private static void RegisterGasLandingChoiceTargetScene(string sceneKey, Vector3d targetPosition, int authoredGasLength)
    {
        if (authoredGasLength > 0)
            RegisterVolumeLine(Vector3d.Zero, TraversalMedium.Gas, authoredGasLength, $"{sceneKey}-Gas");

        PathTestFactory.RegisterSingleTraversalPoint(
            $"{sceneKey}-Target",
            targetPosition,
            TraversalMedia.Solid | TraversalMedia.Gas);

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: $"{sceneKey}-landing",
            type: TraversalTransitionType.Landing,
            source: TraversalTransitionAnchor.Gas(targetPosition),
            destination: TraversalTransitionAnchor.Solid(targetPosition),
            pathCostModifier: 1)).Should().BeTrue();
    }
}

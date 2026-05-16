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
        TestWorld.Setup();
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16)), out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TryCreate_ShouldBuildDirectRequests_ForSupportedModes()
    {
        PathTestFactory.RegisterLineChart(TestWorld.Context, "NavigatorFactorySolid", Vector3d.Zero, 3);
        PathTestFactory.RegisterVolumeLine(TestWorld.Context, new Vector3d(0, 0, 2), TraversalMedium.Gas, 3, "NavigatorFactoryGas");
        PathTestFactory.RegisterVolumeLine(TestWorld.Context, new Vector3d(0, 0, 4), TraversalMedium.Liquid, 3, "NavigatorFactoryLiquid");

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: true,
            allowTraversalTransitions: true,
            maxClimbHeight: (Fixed64)2,
            traversalMedium: TraversalMedium.Solid,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 11,
            out IPathRequest? aStarRequest,
            out _).Should().BeTrue();

        aStarRequest.Should().BeOfType<AStarPathRequest>()
            .Which.MaxClimbHeight.Should().Be((Fixed64)2);

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            SolidPathAlgorithm.FlowField,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: (Fixed64)3,
            traversalMedium: TraversalMedium.Solid,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 17,
            out IPathRequest? flowFieldRequest,
            out _).Should().BeTrue();

        flowFieldRequest.Should().BeOfType<FlowFieldPathRequest>().Which.ExtraFloodRange.Should().Be(17);

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, new Vector3d(0, 0, 2),
            new Vector3d(2, 0, 2),
            Fixed64.One,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Gas,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            out IPathRequest? aerialRequest,
            out _).Should().BeTrue();

        aerialRequest.Should().BeOfType<VolumePathRequest>().Which.Medium.Should().Be(TraversalMedium.Gas);

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, new Vector3d(0, 0, 4),
            new Vector3d(2, 0, 4),
            Fixed64.One,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Liquid,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            out IPathRequest? swimRequest,
            out _).Should().BeTrue();

        swimRequest.Should().BeOfType<VolumePathRequest>().Which.Medium.Should().Be(TraversalMedium.Liquid);
    }

    [Fact]
    public void TryCreate_WithAerialMode_ShouldBuildLandingHandoff_WhenDirectFlightCannotReachTarget()
    {
        const string sceneKey = "NavigatorFactoryAerialHandoff";
        GuidedPathTestScene.RegisterAerialLandingHandoffScene(TestWorld.Context, sceneKey);

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: (Fixed64)2,
            traversalMedium: TraversalMedium.Gas,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 9,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        request.Should().BeOfType<VolumePathRequest>().Which.TargetPosition.Should().Be(new Vector3d(1, 0, 0));
        GuidedVolumeExitHandoff aerialHandoff = TestRequire.NotNull(handoff);
        aerialHandoff.TransitionId.Should().Be($"{sceneKey}-landing");
        aerialHandoff.ChartOriginPosition.Should().Be(new Vector3d(1, 0, 0));
        aerialHandoff.TargetPosition.Should().Be(new Vector3d(4, 0, 0));
        aerialHandoff.ChartPathMode.Should().Be(SolidPathAlgorithm.AStar);
    }

    [Fact]
    public void TryCreate_WithAerialMode_ShouldNormalizeUnsupportedFallbackModesToAStar()
    {
        const string sceneKey = "NavigatorFactoryAerialNormalize";
        GuidedPathTestScene.RegisterAerialLandingHandoffScene(TestWorld.Context, sceneKey);

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: (Fixed64)2,
            traversalMedium: TraversalMedium.Gas,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            out _,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        GuidedVolumeExitHandoff normalizedHandoff = TestRequire.NotNull(handoff);
        normalizedHandoff.ChartPathMode.Should().Be(SolidPathAlgorithm.AStar);
    }

    [Fact]
    public void TryCreate_WithAerialMode_ShouldKeepDirectRequest_WhenTargetIsNotChartBacked()
    {
        PathTestFactory.RegisterVolumeLine(TestWorld.Context, Vector3d.Zero, TraversalMedium.Gas, 3, "NavigatorFactoryDirectGas");

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Gas,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        request.Should().BeOfType<VolumePathRequest>().Which.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        handoff.Should().BeNull();
    }

    [Fact]
    public void TryCreate_WithAerialMode_ShouldKeepDirectRequest_WhenDirectFlightIsCheaperThanLanding()
    {
        const string sceneKey = "NavigatorFactoryAerialDirectPreferred";
        GuidedPathTestScene.RegisterAerialLandingChoiceScene(TestWorld.Context, sceneKey);

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Gas,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        request.Should().BeOfType<VolumePathRequest>().Which.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        handoff.Should().BeNull();
    }

    [Fact]
    public void TryCreate_WithSwimMode_ShouldBuildExitHandoff_WhenSolidTargetRequiresOne()
    {
        const string sceneKey = "NavigatorFactorySwimHandoff";
        GuidedPathTestScene.RegisterVolumeExitHandoffScene(TestWorld.Context, sceneKey);

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            SolidPathAlgorithm.FlowField,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: (Fixed64)2,
            traversalMedium: TraversalMedium.Liquid,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 12,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        request.Should().BeOfType<VolumePathRequest>().Which.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        GuidedVolumeExitHandoff swimHandoff = TestRequire.NotNull(handoff);
        swimHandoff.TransitionId.Should().Be($"{sceneKey}-exit");
        swimHandoff.ChartOriginPosition.Should().Be(new Vector3d(2, 0, 0));
        swimHandoff.ChartPathMode.Should().Be(SolidPathAlgorithm.FlowField);
        swimHandoff.FlowFieldExtraFloodRange.Should().Be(12);
    }

    [Fact]
    public void TryCreate_WithSwimMode_ShouldKeepDirectRequest_WhenTargetSupportsLiquid()
    {
        const string sceneKey = "NavigatorFactorySwimDirect";
        GuidedPathTestScene.RegisterChartBackedSwimTargetScene(TestWorld.Context, sceneKey);

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            SolidPathAlgorithm.FlowField,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Liquid,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 7,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        request.Should().BeOfType<VolumePathRequest>().Which.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        handoff.Should().BeNull();
    }

    [Fact]
    public void TryCreate_WithConstrainedVolumeExit_ShouldFail_WhenTraversalTransitionsAreDisabled()
    {
        const string sceneKey = "NavigatorFactorySwimDisabled";
        GuidedPathTestScene.RegisterVolumeExitHandoffScene(TestWorld.Context, sceneKey);

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            SolidPathAlgorithm.FlowField,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Liquid,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 0,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeFalse();

        request.Should().BeNull();
        handoff.Should().BeNull();
    }

    [Fact]
    public void TryCreate_Internal_Aerial_ShouldKeepDirectRequest_WhenTargetIsOutsideGridButEndpointSnaps()
    {
        PathTestFactory.RegisterVolumeLine(TestWorld.Context, Vector3d.Zero, TraversalMedium.Gas, 3, "NavigatorFactoryOutsideGrid");

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(12, 0, 0),
            Fixed64.One,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: true,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Gas,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
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
        PathTestFactory.RegisterVolumeLine(TestWorld.Context, Vector3d.Zero, TraversalMedium.Gas, 3, "NavigatorFactoryNoLandingGas");
        PathTestFactory.RegisterSingleTraversalPoint(
            TestWorld.Context, "NavigatorFactoryNoLandingTarget",
            new Vector3d(2, 0, 0),
            TraversalMedia.Solid | TraversalMedia.Gas);

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Gas,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        request.Should().BeOfType<VolumePathRequest>().Which.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        handoff.Should().BeNull();
    }

    [Fact]
    public void TryCreate_Internal_Aerial_ShouldBuildLandingHandoff_WhenDirectRequestSnapsFromSolidTarget()
    {
        const string sceneKey = "NavigatorFactorySnappedAerialHandoff";
        GuidedPathTestScene.RegisterAerialLandingHandoffScene(TestWorld.Context, sceneKey);

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: true,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Gas,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
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
        GuidedPathTestScene.RegisterVolumeExitHandoffScene(TestWorld.Context, sceneKey);

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            SolidPathAlgorithm.FlowField,
            allowUnwalkableEndpoints: true,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Liquid,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 4,
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
        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, new Vector3d(-6, -6, -6),
            new Vector3d(-5, -6, -6),
            Fixed64.One,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Solid,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 0,
            out IPathRequest? aStarRequest,
            out _).Should().BeFalse();

        aStarRequest.Should().BeNull();

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, new Vector3d(-6, -6, -6),
            new Vector3d(-5, -6, -6),
            Fixed64.One,
            SolidPathAlgorithm.FlowField,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Solid,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 0,
            out IPathRequest? flowFieldRequest,
            out _).Should().BeFalse();

        flowFieldRequest.Should().BeNull();
    }

    /// <summary>
    /// Covers the null FlowField path in the internal TryCreate overload (with handoff parameter).
    /// When neither endpoint is on a chart and no transition plan can be built, the method returns false.
    /// </summary>
    [Fact]
    public void TryCreate_Internal_FlowField_ShouldReturnFalse_WhenNoChartCoversEndpoints()
    {
        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, new Vector3d(-6, -6, -6),
            new Vector3d(-5, -6, -6),
            Fixed64.One,
            SolidPathAlgorithm.FlowField,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Solid,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 0,
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
        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, new Vector3d(-6, -6, -6),
            new Vector3d(-5, -6, -6),
            Fixed64.One,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Gas,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
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
        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, new Vector3d(-6, -6, -6),
            new Vector3d(-5, -6, -6),
            Fixed64.One,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Liquid,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
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
        GuidedPathTestScene.RegisterChartBackedSwimTargetScene(TestWorld.Context, sceneKey);

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Liquid,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        // TryCreateGasLandingHandoff returns false for Liquid medium → direct request kept
        request.Should().BeOfType<VolumePathRequest>().Which.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        handoff.Should().BeNull();
    }

    [Fact]
    public void TryCreate_Internal_ShouldRejectInvalidModes()
    {
        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(1, 0, 0),
            Fixed64.One,
            (SolidPathAlgorithm)123,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Solid,
            aStarHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 0,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeFalse();

        request.Should().BeNull();
        handoff.Should().BeNull();
    }

    [Fact]
    public void TryCreate_Internal_Aerial_ShouldKeepDirectRequest_WhenTargetVoxelDoesNotExist()
    {
        PathTestFactory.RegisterVolumeLine(TestWorld.Context, Vector3d.Zero, TraversalMedium.Gas, 3, "NavigatorFactoryFarOutsideGrid");

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(64, 0, 0),
            Fixed64.One,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: true,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Gas,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
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

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2.25f, 0, 0),
            Fixed64.One,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Gas,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
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
        GuidedPathTestScene.RegisterAerialLandingHandoffScene(TestWorld.Context, sceneKey);
        VolumeMediumRules.SetGasVoxelRule(static voxel =>
            voxel != null && voxel.WorldPosition == new Vector3d(4, 0, 0));

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Gas,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
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

        NavigatorPathRequestFactory.TryCreate(TestWorld.Context, Vector3d.Zero,
            Vector3d.Zero,
            Fixed64.One,
            SolidPathAlgorithm.AStar,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            traversalMedium: TraversalMedium.Gas,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 0,
            out IPathRequest? request,
            out GuidedVolumeExitHandoff? handoff).Should().BeTrue();

        request.Should().BeOfType<VolumePathRequest>().Which.TargetPosition.Should().Be(Vector3d.Zero);
        handoff.Should().BeNull();
    }

    private static void RegisterGasLandingChoiceTargetScene(string sceneKey, Vector3d targetPosition, int authoredGasLength)
    {
        if (authoredGasLength > 0)
            PathTestFactory.RegisterVolumeLine(TestWorld.Context, Vector3d.Zero, TraversalMedium.Gas, authoredGasLength, $"{sceneKey}-Gas");

        PathTestFactory.RegisterSingleTraversalPoint(
            TestWorld.Context, $"{sceneKey}-Target",
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

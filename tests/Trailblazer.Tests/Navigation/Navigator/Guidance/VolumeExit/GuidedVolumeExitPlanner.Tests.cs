using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

[Collection("PathingCollection")]
public sealed class GuidedVolumeExitPlannerTests : IDisposable
{
    public GuidedVolumeExitPlannerTests()
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
    public void TryPlan_ShouldCreateFlowFieldExitPlan_ForLocalSwimExit()
    {
        const string sceneKey = "GuidedPlannerFlow";
        GuidedPathTestScene.RegisterVolumeExitHandoffScene(TestWorld.Context, sceneKey);

        GuidedVolumeExitPlanner.TryPlan(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            TraversalMedium.Liquid,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: (Fixed64)2,
            volumeHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 8,
            out VolumePathRequest? request,
            out GuidedVolumeExitHandoff? handoff,
            out int totalCost).Should().BeTrue();

        VolumePathRequest plannedRequest = TestRequire.NotNull(request);
        GuidedVolumeExitHandoff plannedHandoff = TestRequire.NotNull(handoff);
        plannedRequest.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        plannedHandoff.FlowFieldExtraFloodRange.Should().Be(8);
        totalCost.Should().BeGreaterThan(0);

        plannedHandoff.TryCreateFollowupRequest(TestWorld.Context, new Vector3d(2, 0, 0), PathTestFactory.DefaultNavigationProfile, out IPathRequest? followup).Should().BeTrue();
        followup.Should().BeOfType<FlowFieldPathRequest>();
    }

    [Fact]
    public void TryPlan_ShouldUseTransitionAwareFlowFieldChartLeg_ForAerialLanding()
    {
        const string sceneKey = "GuidedPlannerAerialFlow";
        GuidedPathTestScene.RegisterAerialLandingHandoffScene(TestWorld.Context, sceneKey);

        GuidedVolumeExitPlanner.TryPlan(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            TraversalMedium.Gas,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            volumeHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 6,
            out VolumePathRequest? request,
            out GuidedVolumeExitHandoff? handoff,
            out int totalCost).Should().BeTrue();

        VolumePathRequest plannedRequest = TestRequire.NotNull(request);
        GuidedVolumeExitHandoff plannedHandoff = TestRequire.NotNull(handoff);
        plannedRequest.TargetPosition.Should().Be(new Vector3d(1, 0, 0));
        plannedHandoff.TransitionId.Should().Be($"{sceneKey}-landing");
        plannedHandoff.FlowFieldExtraFloodRange.Should().Be(6);
        totalCost.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TryPlan_ShouldFail_WhenFlowFieldChartLegNeedsTransitionsButFallbackIsDisabled()
    {
        const string sceneKey = "GuidedPlannerAerialFlowDisabled";
        GuidedPathTestScene.RegisterAerialLandingHandoffScene(TestWorld.Context, sceneKey);

        GuidedVolumeExitPlanner.TryPlan(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            TraversalMedium.Gas,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: false,
            maxClimbHeight: Fixed64.One,
            volumeHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 4,
            out VolumePathRequest? request,
            out GuidedVolumeExitHandoff? handoff,
            out int totalCost).Should().BeFalse();

        request.Should().BeNull();
        handoff.Should().BeNull();
        totalCost.Should().Be(0);
    }

    [Fact]
    public void TryPlan_ShouldAllowZeroDisplacementChartLeg()
    {
        const string sceneKey = "GuidedPlannerZeroChartLeg";
        GuidedPathTestScene.RegisterVolumeExitHandoffScene(TestWorld.Context, sceneKey);

        GuidedVolumeExitPlanner.TryPlan(TestWorld.Context, Vector3d.Zero,
            new Vector3d(2, 0, 0),
            Fixed64.One,
            TraversalMedium.Liquid,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            volumeHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 3,
            out VolumePathRequest? request,
            out GuidedVolumeExitHandoff? handoff,
            out int totalCost).Should().BeTrue();

        VolumePathRequest plannedRequest = TestRequire.NotNull(request);
        GuidedVolumeExitHandoff plannedHandoff = TestRequire.NotNull(handoff);
        plannedRequest.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        plannedHandoff.ChartOriginPosition.Should().Be(new Vector3d(2, 0, 0));
        plannedHandoff.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        totalCost.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TryPlan_ShouldFail_WhenTargetIsOutsideEveryActiveGrid()
    {
        const string sceneKey = "GuidedPlannerOutsideGrid";
        GuidedPathTestScene.RegisterVolumeExitHandoffScene(TestWorld.Context, sceneKey);

        GuidedVolumeExitPlanner.TryPlan(TestWorld.Context, Vector3d.Zero,
            new Vector3d(40, 0, 0),
            Fixed64.One,
            TraversalMedium.Liquid,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            volumeHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 0,
            out VolumePathRequest? request,
            out GuidedVolumeExitHandoff? handoff,
            out int totalCost).Should().BeFalse();

        request.Should().BeNull();
        handoff.Should().BeNull();
        totalCost.Should().Be(0);
    }

    [Fact]
    public void TryPlan_ShouldFail_WhenNoTransitionsCanExitVolume()
    {
        RegisterSolidTargetLine("GuidedPlannerNoTransition", new Vector3d(2, 0, 0), 3);
        GuidedPathTestScene.AddWater(TestWorld.Context, Vector3d.Zero);
        GuidedPathTestScene.AddWater(TestWorld.Context, new Vector3d(1, 0, 0));

        GuidedVolumeExitPlanner.TryPlan(TestWorld.Context, Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            TraversalMedium.Liquid,
            allowUnwalkableEndpoints: false,
            allowTraversalTransitions: true,
            maxClimbHeight: Fixed64.One,
            volumeHeuristic: HeuristicMethod.Manhattan,
            flowFieldExtraFloodRange: 0,
            out VolumePathRequest? request,
            out GuidedVolumeExitHandoff? handoff,
            out int totalCost).Should().BeFalse();

        request.Should().BeNull();
        handoff.Should().BeNull();
        totalCost.Should().Be(0);
    }

    private static void RegisterSolidTargetLine(string chartKey, Vector3d minBounds, int length)
    {
        var data = new bool[1, length, 1];
        for (int i = 0; i < length; i++)
            data[0, i, 0] = true;

        PathTestFactory.RegisterFromData(TestWorld.Context, chartKey, data, minBounds);
    }
}

/// <summary>
/// Standalone tests for <see cref="GuidedVolumeExitHandoff"/> failure paths that do not require
/// a live chart or transition infrastructure.
/// </summary>
[Collection("PathingCollection")]
public sealed class GuidedVolumeExitHandoffTests : IDisposable
{
    public GuidedVolumeExitHandoffTests()
    {
        if (TestWorld.IsActive)
            TestWorld.Reset();
        else
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
    public void TryCreateFollowupRequest_ShouldFail_WhenHandoffIsInvalid()
    {
        // Default TransitionId is null, so IsValid == false; the early-return branch is exercised.
        var handoff = new GuidedVolumeExitHandoff();
        handoff.TryCreateFollowupRequest(TestWorld.Context, Vector3d.Zero, PathTestFactory.DefaultNavigationProfile, out _).Should().BeFalse();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true, false)]
    [InlineData(true, true)]
#endif
    public void RoundTrip_ShouldRejectLegacyOrMissingHandoffModeInsteadOfFallingBackToFlowField(
        bool useMemoryPack,
        bool omitMode)
    {
        PathTestFactory.RegisterFromData(
            TestWorld.Context,
            "LegacySerializedAStarHandoff",
            new bool[1, 2, 1] { { { true }, { true } } },
            Vector3d.Zero);
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        var source = new GuidedVolumeExitHandoff
        {
            TransitionId = "test-transition",
            ChartOriginPosition = Vector3d.Zero,
            TargetPosition = Vector3d.Right
        };
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        payload = omitMode
            ? SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "ChartPathMode")
            : SerializationUtility.SetPayloadValue(payload, useMemoryPack, 0, "ChartPathMode");
        var target = new GuidedVolumeExitHandoff();

        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.IsValid.Should().BeFalse();
        target.TryCreateFollowupRequest(TestWorld.Context, Vector3d.Zero, profile, out IPathRequest? request)
            .Should().BeFalse();
        request.Should().BeNull();
    }

    [Fact]
    public void TryCreateFollowupRequest_ShouldFail_WhenFlowFieldCreateReturnsNull()
    {
        // Same as above but for the FlowField case branch.
        var handoff = new GuidedVolumeExitHandoff
        {
            TransitionId = "test-transition",
            ChartOriginPosition = new Vector3d(1000, 0, 0),
            TargetPosition = new Vector3d(1001, 0, 0),
        };
        handoff.TryCreateFollowupRequest(TestWorld.Context, Vector3d.Zero, PathTestFactory.DefaultNavigationProfile, out _).Should().BeFalse();
    }
}

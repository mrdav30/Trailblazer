using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Navigation;

[Collection("PathingCollection")]
public sealed class GuidedVolumeExitPlannerTests : IDisposable
{
    private readonly GridConfiguration _configuration;

    public GuidedVolumeExitPlannerTests()
    {
        TestWorld.Setup();
        _configuration = new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16));
        TestWorld.World.TryAddGrid(_configuration, out _);
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
        NavigationAreaPolicyKey policyKey = PublishSurfaceGraph(
            "guided-volume-exit",
            new Vector3d(2, 0, 0),
            new Vector3d(3, 0, 0),
            new Vector3d(4, 0, 0));
        var query = new PathQuery(
            new NavigationEndpoint(Vector3d.Zero, "guided-volume-exit"),
            new NavigationEndpoint(new Vector3d(4, 0, 0), "guided-volume-exit"),
            PathTestFactory.DefaultNavigationProfile,
            policyKey,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.FlowField,
            new NavigationWorkBudget(4096, 4096, 4096, 4096, 4096, 64, 64, 64, 0, 0, 0),
            allowTransitions: true,
            new FlowFieldQueryOptions(Fixed64.FromFraction(1, 3)));

        GuidedVolumeExitPlanner.TryPlan(
            TestWorld.Context,
            query,
            TraversalMedium.Liquid,
            HeuristicMethod.Manhattan,
            out VolumePathRequest? request,
            out GuidedVolumeExitHandoff? handoff,
            out Fixed64 totalCost).Should().BeTrue();

        VolumePathRequest plannedRequest = TestRequire.NotNull(request);
        GuidedVolumeExitHandoff plannedHandoff = TestRequire.NotNull(handoff);
        plannedRequest.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        plannedHandoff.FollowupQuery.Should().Be(query);
        totalCost.Should().BeGreaterThan(Fixed64.Zero);
        totalCost.Should().NotBe(totalCost.Floor());
        TestWorld.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount.Should().Be(0);

        plannedHandoff.TryCreateFollowupQuery(
            new Vector3d(2, 0, 0),
            out PathQuery? followup).Should().BeTrue();
        followup.Should().Be(query.WithStartPosition(new Vector3d(2, 0, 0)));
    }

    [Fact]
    public void Navigator_ShouldActivateTheExactGraphQueryAfterTheVolumeExit()
    {
        const string sceneKey = "NavigatorExactVolumeExit";
        GuidedPathTestScene.RegisterVolumeExitHandoffScene(TestWorld.Context, sceneKey);
        NavigationAreaPolicyKey policyKey = PublishSurfaceGraph(
            "navigator-volume-exit",
            new Vector3d(2, 0, 0),
            new Vector3d(3, 0, 0),
            new Vector3d(4, 0, 0));
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        var query = new PathQuery(
            new NavigationEndpoint(Vector3d.Zero, "navigator-volume-exit"),
            new NavigationEndpoint(new Vector3d(4, 0, 0), "navigator-volume-exit"),
            profile,
            policyKey,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.FlowField,
            new NavigationWorkBudget(4096, 4096, 4096, 4096, 4096, 64, 64, 64, 0, 0, 0),
            allowTransitions: true,
            new FlowFieldQueryOptions(Fixed64.FromFraction(1, 3)));
        var navigator = new TestNavigator(TestWorld.Context);
        navigator.Setup(Vector3d.Up * profile.Shape.RootToFootOffsetY, profile);
        navigator.Initialize(new TrekCondition
        {
            Medium = TraversalMedium.Liquid,
            SurfaceLevel = Fixed64.Zero
        });
        NavSteering steering = TestRequire.NotNull(navigator.Steering);

        navigator.ApplyGuidedTrekRequest(
            query,
            rate: TrekRate.Fast,
            isRequestingSwim: true,
            groupId: 7);

        steering.CurrentRequest.Should().BeOfType<VolumePathRequest>()
            .Which.TargetPosition.Should().Be(new Vector3d(2, 0, 0));
        steering.CurrentQuery.Should().BeNull();

        navigator.SetTestPosition(
            new Vector3d(2, 0, 0) + Vector3d.Up * profile.Shape.RootToFootOffsetY);
        navigator.SetGroundContact(surfaceLevel: Fixed64.Zero, updateMotorState: true);
        steering.Arrive();
        navigator.Simulate();

        steering.CurrentRequest.Should().BeNull();
        steering.CurrentQuery.Should().Be(query.WithStartPosition(new Vector3d(2, 0, 0)));
        steering.MovementGroupID.Should().Be(7);
        steering.ShouldMove.Should().BeTrue();
        navigator.FrameRequest.IsRequestingSwim.Should().BeFalse();
        TestWorld.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount
            .Should().Be(1);

        steering.StopMove();
        TestWorld.Context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount
            .Should().Be(0);
    }

    [Fact]
    public void Navigator_ShouldKeepTheActiveVolumeHandoffWhenReplacementPlanningFails()
    {
        const string sceneKey = "NavigatorVolumeReplacement";
        GuidedPathTestScene.RegisterVolumeExitHandoffScene(TestWorld.Context, sceneKey);
        NavigationAreaPolicyKey policyKey = PublishSurfaceGraph(
            "navigator-volume-replacement",
            new Vector3d(2, 0, 0),
            new Vector3d(3, 0, 0),
            new Vector3d(4, 0, 0));
        NavigationAgentProfile profile = PathTestFactory.DefaultNavigationProfile;
        var valid = new PathQuery(
            new NavigationEndpoint(Vector3d.Zero, "navigator-volume-replacement"),
            new NavigationEndpoint(new Vector3d(4, 0, 0), "navigator-volume-replacement"),
            profile,
            policyKey,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.FlowField,
            new NavigationWorkBudget(4096, 4096, 4096, 4096, 4096, 64, 64, 64, 0, 0, 0),
            allowTransitions: true);
        var navigator = new TestNavigator(TestWorld.Context);
        navigator.Setup(Vector3d.Up * profile.Shape.RootToFootOffsetY, profile);
        navigator.Initialize(new TrekCondition
        {
            Medium = TraversalMedium.Liquid,
            SurfaceLevel = Fixed64.Zero
        });
        navigator.ApplyGuidedTrekRequest(valid, groupId: 17);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        IPathRequest activeRequest = TestRequire.NotNull(steering.CurrentRequest);
        GuidedVolumeExitHandoff activeHandoff = TestRequire.NotNull(
            ReflectionUtility.GetPrivateFieldFromBase<GuidedVolumeExitHandoff?>(
                navigator,
                "_pendingGuidedVolumeExitHandoff"));
        var invalid = new PathQuery(
            valid.Start,
            new NavigationEndpoint(new Vector3d(7, 0, 0), "missing-map"),
            valid.Agent,
            new NavigationAreaPolicyKey("missing-policy", 1),
            valid.Traversal,
            valid.Algorithm,
            valid.Budget,
            allowTransitions: true,
            valid.FlowField);

        Action replace = () => navigator.ApplyGuidedTrekRequest(invalid, groupId: 99);

        replace.Should().Throw<ArgumentException>().WithParameterName("query");
        steering.CurrentRequest.Should().BeSameAs(activeRequest);
        ReflectionUtility.GetPrivateFieldFromBase<GuidedVolumeExitHandoff?>(
                navigator,
                "_pendingGuidedVolumeExitHandoff")
            .Should().BeSameAs(activeHandoff);
        steering.MovementGroupID.Should().Be(17);
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
                new[] { new NavigationAreaRule(true, Fixed64.FromFraction(1, 3)) }),
            publicationSequence: 2,
            effectiveFrame: 1);
        TestWorld.Context.Pathing.Admit(mapOperation).Should().BeTrue();
        TestWorld.Context.Pathing.Admit(policyOperation).Should().BeTrue();
        while (mapOperation.Receipt.Status == NavigationOperationStatus.Pending
            || policyOperation.Receipt.Status == NavigationOperationStatus.Pending)
        {
            TestWorld.Context.Simulate();
        }

        mapOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        policyOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        return policyKey;
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

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldPreserveTheExactGraphFollowupQueryWithWireModeTwo(bool useMemoryPack)
    {
        PathQuery query = CreateGraphQuery();
        var source = new GuidedVolumeExitHandoff
        {
            TransitionId = "graph-transition",
            FollowupQuery = query,
            MovementGroupId = 7,
            IsRequestingClimb = true
        };

        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        var target = new GuidedVolumeExitHandoff();
        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.IsValid.Should().BeTrue();
        target.FollowupQuery.Should().Be(query);
        target.TryCreateFollowupQuery(Vector3d.Right, out PathQuery? rebased)
            .Should().BeTrue();
        rebased.Should().Be(query.WithStartPosition(Vector3d.Right));
        target.MovementGroupId.Should().Be(7);
        target.IsRequestingClimb.Should().BeTrue();
    }

    [Theory]
    [InlineData(false, -1)]
    [InlineData(false, 1)]
    [InlineData(false, 99)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true, -1)]
    [InlineData(true, 1)]
    [InlineData(true, 99)]
#endif
    public void RoundTrip_ShouldRejectInvalidHandoffModeBeforeMutatingExistingState(
        bool useMemoryPack,
        int serializedMode)
    {
        PathQuery sourceQuery = CreateGraphQuery();
        var source = new GuidedVolumeExitHandoff
        {
            TransitionId = "serialized-transition",
            FollowupQuery = sourceQuery,
            MovementGroupId = 7,
            IsRequestingClimb = true
        };
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        payload = serializedMode < 0
            ? SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "ChartPathMode")
            : SerializationUtility.SetPayloadValue(payload, useMemoryPack, serializedMode, "ChartPathMode");
        PathQuery sentinelQuery = sourceQuery.WithStartPosition(Vector3d.Left);
        var target = new GuidedVolumeExitHandoff
        {
            TransitionId = "sentinel-transition",
            FollowupQuery = sentinelQuery,
            MovementGroupId = 42,
            IsRequestingClimb = false
        };

        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.TransitionId.Should().Be("sentinel-transition");
        target.FollowupQuery.Should().Be(sentinelQuery);
        target.MovementGroupId.Should().Be(42);
        target.IsRequestingClimb.Should().BeFalse();
    }

    private static PathQuery CreateGraphQuery() => new(
        new NavigationEndpoint(Vector3d.Zero, "graph-map"),
        new NavigationEndpoint(new Vector3d(4, 0, 0), "graph-map"),
        PathTestFactory.DefaultNavigationProfile,
        new NavigationAreaPolicyKey("graph-policy", 3),
        new TraversalIntent(
            TraversalDomain.Surface,
            TraversalMedium.Solid,
            TraversalDomain.Surface),
        PathAlgorithm.FlowField,
        new NavigationWorkBudget(11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21),
        allowTransitions: true,
        new FlowFieldQueryOptions(Fixed64.Half));
}

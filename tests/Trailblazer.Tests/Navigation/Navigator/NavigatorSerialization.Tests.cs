using Chronicler;
using FixedMathSharp;
using FluentAssertions;
using GridForge;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Navigation.Turning;
using Trailblazer.Pathing;
using Trailblazer.Tests.Navigation.Motor;
using Xunit;

namespace Trailblazer.Tests.Navigation;

[Collection("PathingCollection")]
public class NavigatorSerializationTests : IDisposable
{
    public NavigatorSerializationTests()
    {
        TrailblazerWorldManager.Setup();
        var config = new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16));
        TrailblazerWorldManager.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        TrailblazerWorldManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void JsonRoundTrip_ShouldRestoreMotorStateIntoExistingMotor()
    {
        var source = CreateConfiguredMotorAgent();
        NavMotor sourceMotor = TestRequire.NotNull(source.Motor);

        string json = JsonRecordSerializer.Serialize(sourceMotor, writeIndented: true);

        var target = MockMotorAgentTestFactory.CreateMockAgent(startPosition: new Vector3d(-2, 0, -2), startingMedium: TraversalMedium.Solid);
        NavMotor targetMotor = TestRequire.NotNull(target.Motor);
        JsonRecordSerializer.Populate(targetMotor, json);

        AssertMotorStateMatches(sourceMotor, targetMotor);
    }

    [Fact]
    public void JsonRoundTrip_ShouldRestoreNavigatorAndMotorState()
    {
        var source = CreateConfiguredNavigator();

        string json = JsonRecordSerializer.Serialize(source, writeIndented: true);

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        JsonRecordSerializer.Populate(target, json);
        NavMotor targetMotor = TestRequire.NotNull(target.Motor);
        NavTurning targetTurning = TestRequire.NotNull(target.Turning);

        target.Position.Should().Be(source.Position);
        target.LastPosition.Should().Be(source.LastPosition);
        target.Rotation.Should().Be(source.Rotation);
        target.Forward.Should().Be(source.Forward);
        target.Velocity.Should().Be(source.Velocity);
        target.Speed.Should().Be(source.Speed);
        target.Acceleration.Should().Be(source.Acceleration);
        target.Size.Should().Be(source.Size);
        target.FootPositionAdjust.Should().Be(source.FootPositionAdjust);
        target.GuidedPathMode.Should().Be(source.GuidedPathMode);
        target.GuidedAllowUnwalkableEndpoints.Should().Be(source.GuidedAllowUnwalkableEndpoints);
        target.GuidedAllowTraversalTransitions.Should().Be(source.GuidedAllowTraversalTransitions);
        target.GuidedMaxClimbHeight.Should().Be(source.GuidedMaxClimbHeight);
        target.GuidedAStarHeuristic.Should().Be(source.GuidedAStarHeuristic);
        target.GuidedFlowFieldExtraFloodRange.Should().Be(source.GuidedFlowFieldExtraFloodRange);
        target.GlobalId.Should().Be(source.GlobalId);
        target.OccupantGroupId.Should().Be(source.OccupantGroupId);
        target.IsLockedOn.Should().Be(source.IsLockedOn);
        target.FrameRequest.FacingDirection.Should().Be(source.FrameRequest.FacingDirection);
        target.FrameRequest.IsRequestingSwim.Should().Be(source.FrameRequest.IsRequestingSwim);
        target.FrameRequest.IsRequestingClimb.Should().Be(source.FrameRequest.IsRequestingClimb);
        target.FrameRequest.CanAffordJump.Should().Be(source.FrameRequest.CanAffordJump);
        target.IsGuideded.Should().BeFalse();

        AssertMotorStateMatches(TestRequire.NotNull(source.Motor), targetMotor);
        AssertTurningStateMatches(TestRequire.NotNull(source.Turning), targetTurning);

        TrailblazerManager.Simulate();
        target.ApplyInputTrekRequest(Vector3d.Forward, TrekRate.Slow, isRequestingJump: false);
        target.Simulate();
        target.CommitFrameMotion();

        targetMotor.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public void MemoryPackRoundTrip_ShouldRestoreMotorStateIntoExistingMotor()
    {
        var source = CreateConfiguredMotorAgent();
        NavMotor sourceMotor = TestRequire.NotNull(source.Motor);

        byte[] data = MemoryPackRecordSerializer.Serialize(sourceMotor);

        var target = MockMotorAgentTestFactory.CreateMockAgent(startPosition: new Vector3d(-2, 0, -2), startingMedium: TraversalMedium.Solid);
        NavMotor targetMotor = TestRequire.NotNull(target.Motor);
        MemoryPackRecordSerializer.Populate(targetMotor, data);

        AssertMotorStateMatches(sourceMotor, targetMotor);
    }

    [Fact]
    public void MemoryPackRoundTrip_ShouldRestoreNavigatorAndMotorState()
    {
        var source = CreateConfiguredNavigator();

        byte[] data = MemoryPackRecordSerializer.Serialize(source);

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        MemoryPackRecordSerializer.Populate(target, data);
        NavMotor targetMotor = TestRequire.NotNull(target.Motor);
        NavTurning targetTurning = TestRequire.NotNull(target.Turning);

        target.Position.Should().Be(source.Position);
        target.LastPosition.Should().Be(source.LastPosition);
        target.Rotation.Should().Be(source.Rotation);
        target.Forward.Should().Be(source.Forward);
        target.Velocity.Should().Be(source.Velocity);
        target.Speed.Should().Be(source.Speed);
        target.Acceleration.Should().Be(source.Acceleration);
        target.Size.Should().Be(source.Size);
        target.FootPositionAdjust.Should().Be(source.FootPositionAdjust);
        target.GuidedPathMode.Should().Be(source.GuidedPathMode);
        target.GuidedAllowUnwalkableEndpoints.Should().Be(source.GuidedAllowUnwalkableEndpoints);
        target.GuidedAllowTraversalTransitions.Should().Be(source.GuidedAllowTraversalTransitions);
        target.GuidedMaxClimbHeight.Should().Be(source.GuidedMaxClimbHeight);
        target.GuidedAStarHeuristic.Should().Be(source.GuidedAStarHeuristic);
        target.GuidedFlowFieldExtraFloodRange.Should().Be(source.GuidedFlowFieldExtraFloodRange);
        target.GlobalId.Should().Be(source.GlobalId);
        target.OccupantGroupId.Should().Be(source.OccupantGroupId);
        target.IsLockedOn.Should().Be(source.IsLockedOn);
        target.FrameRequest.FacingDirection.Should().Be(source.FrameRequest.FacingDirection);
        target.FrameRequest.IsRequestingSwim.Should().Be(source.FrameRequest.IsRequestingSwim);
        target.FrameRequest.IsRequestingClimb.Should().Be(source.FrameRequest.IsRequestingClimb);
        target.FrameRequest.CanAffordJump.Should().Be(source.FrameRequest.CanAffordJump);
        target.IsGuideded.Should().BeFalse();

        AssertMotorStateMatches(TestRequire.NotNull(source.Motor), targetMotor);
        AssertTurningStateMatches(TestRequire.NotNull(source.Turning), targetTurning);

        TrailblazerManager.Simulate();
        target.ApplyInputTrekRequest(Vector3d.Forward, TrekRate.Slow, isRequestingJump: false);
        target.Simulate();
        target.CommitFrameMotion();

        targetMotor.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public void JsonRoundTrip_ShouldRestoreNavigatorAndSteeringState_ForGuidedAStarTraversal()
    {
        RegisterGuidedPathChart("NavigatorSerializationJsonAStar");

        var source = CreateConfiguredGuidedNavigator(SolidPathAlgorithm.AStar);
        NavSteering sourceSteering = TestRequire.NotNull(source.Steering);
        sourceSteering.TrailGuide.Should().BeOfType<AStarGuide>();

        string json = JsonRecordSerializer.Serialize(source, writeIndented: true);

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        JsonRecordSerializer.Populate(target, json);
        NavSteering targetSteering = TestRequire.NotNull(target.Steering);

        target.IsGuideded.Should().BeTrue();
        target.FrameRequest.Rate.Should().Be(source.FrameRequest.Rate);
        target.FrameRequest.IsRequestingJump.Should().Be(source.FrameRequest.IsRequestingJump);
        target.FrameRequest.CanAffordJump.Should().Be(source.FrameRequest.CanAffordJump);
        target.FrameRequest.IsRequestingFlight.Should().Be(source.FrameRequest.IsRequestingFlight);
        target.FrameRequest.IsRequestingSwim.Should().Be(source.FrameRequest.IsRequestingSwim);
        target.FrameRequest.IsRequestingClimb.Should().Be(source.FrameRequest.IsRequestingClimb);
        target.Size.Should().Be(source.Size);

        AssertSteeringStateMatches(sourceSteering, targetSteering);

        TrailblazerManager.Simulate();
        target.Simulate();

        target.FrameRequest.Direction.Should().NotBe(Vector3d.Zero);
        targetSteering.ShouldMove.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldRestoreExplicitGuidedClimbIntent(bool useMemoryPack)
    {
        RegisterGuidedPathChart("NavigatorSerializationGuidedClimb");

        var source = CreateNavigator(Vector3d.Zero, size: Fixed64.One);
        source.ConfigureForGuidedTraversal(pathAlgorithm: SolidPathAlgorithm.AStar);
        source.ApplyGuidedTrekRequest(
            new Vector3d(4, 0, 0),
            rate: TrekRate.Fast,
            isRequestingClimb: true);

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        SerializationUtility.PopulateRecord(target, SerializationUtility.SerializeRecord(source, useMemoryPack), useMemoryPack);
        NavSteering targetSteering = TestRequire.NotNull(target.Steering);

        target.IsGuideded.Should().BeTrue();
        target.FrameRequest.IsRequestingClimb.Should().BeTrue();

        TrailblazerManager.Simulate();
        target.Simulate();

        Assert.NotNull(targetSteering.CurrentRequest);
        target.FrameRequest.IsRequestingClimb.Should().BeTrue();

        PathManager.UnloadChart("NavigatorSerializationGuidedClimb");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldRestoreAutoGuidedClimbIntentTracking(bool useMemoryPack)
    {
        GuidedPathTestScene.RegisterTransitionFallbackClimbScene();

        var source = CreateNavigator(Vector3d.Zero, size: Fixed64.One);
        source.ConfigureForGuidedTraversal(pathAlgorithm: SolidPathAlgorithm.AStar, allowTraversalTransitions: true);
        source.ApplyGuidedTrekRequest(new Vector3d(4, 0, 0), rate: TrekRate.Fast);

        object sourceMode = ReflectionUtility.GetPrivateFieldFromBase<object>(source, "_guidedClimbIntentMode");
        int sourceLastSeenVersion = ReflectionUtility.GetPrivateFieldFromBase<int>(source, "_lastSeenGuidedRouteTopologyVersion");

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        SerializationUtility.PopulateRecord(target, SerializationUtility.SerializeRecord(source, useMemoryPack), useMemoryPack);
        NavSteering targetSteering = TestRequire.NotNull(target.Steering);

        ReflectionUtility.GetPrivateFieldFromBase<object>(target, "_guidedClimbIntentMode")
            .ToString().Should().Be(sourceMode.ToString());
        ReflectionUtility.GetPrivateFieldFromBase<int>(target, "_lastSeenGuidedRouteTopologyVersion")
            .Should().Be(sourceLastSeenVersion);

        TrailblazerManager.Simulate();
        target.Simulate();

        target.FrameRequest.IsRequestingClimb.Should().BeTrue();
        targetSteering.CurrentRouteRequestsClimbIntent.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldRebuildTurningRuntimeState_OnLoad(bool useMemoryPack)
    {
        var source = CreateNavigator(new Vector3d(2, 0, 2));
        NavTurning sourceTurning = TestRequire.NotNull(source.Turning);
        sourceTurning.TurnRate = (Fixed64)0.35f;
        sourceTurning.RequestTurnDirection(source.Forward, Vector3d.Right, interpolation: Fixed64.Half);
        sourceTurning.TrySimulateTurn(
            source.Position,
            source.LastPosition,
            source.Forward,
            source.Rotation,
            out _).Should().BeTrue();

        sourceTurning.TargetReached.Should().BeFalse();
        sourceTurning.TargetRotation.Should().NotBe(FixedQuaternion.Identity);

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        SerializationUtility.PopulateRecord(target, SerializationUtility.SerializeRecord(source, useMemoryPack), useMemoryPack);
        NavTurning targetTurning = TestRequire.NotNull(target.Turning);

        targetTurning.CanTurn.Should().Be(sourceTurning.CanTurn);
        targetTurning.TurnRate.Should().Be(sourceTurning.TurnRate);
        targetTurning.TargetReached.Should().BeTrue();
        targetTurning.TargetRotation.Should().Be(FixedQuaternion.Identity);
    }

    [Fact]
    public void MemoryPackRoundTrip_ShouldRestoreNavigatorAndSteeringState_ForGuidedFlowFieldTraversal()
    {
        RegisterGuidedPathChart("NavigatorSerializationMemoryPackFlowField");

        var source = CreateConfiguredGuidedNavigator(SolidPathAlgorithm.FlowField);
        NavSteering sourceSteering = TestRequire.NotNull(source.Steering);
        sourceSteering.TrailGuide.Should().BeOfType<FlowFieldGuide>();

        byte[] data = MemoryPackRecordSerializer.Serialize(source);

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        MemoryPackRecordSerializer.Populate(target, data);
        NavSteering targetSteering = TestRequire.NotNull(target.Steering);

        target.IsGuideded.Should().BeTrue();
        target.FrameRequest.Rate.Should().Be(source.FrameRequest.Rate);
        target.FrameRequest.IsRequestingJump.Should().Be(source.FrameRequest.IsRequestingJump);
        target.FrameRequest.CanAffordJump.Should().Be(source.FrameRequest.CanAffordJump);
        target.FrameRequest.IsRequestingFlight.Should().Be(source.FrameRequest.IsRequestingFlight);
        target.FrameRequest.IsRequestingSwim.Should().Be(source.FrameRequest.IsRequestingSwim);
        target.FrameRequest.IsRequestingClimb.Should().Be(source.FrameRequest.IsRequestingClimb);
        target.Size.Should().Be(source.Size);

        AssertSteeringStateMatches(sourceSteering, targetSteering);

        TrailblazerManager.Simulate();
        target.Simulate();

        target.FrameRequest.Direction.Should().NotBe(Vector3d.Zero);
        targetSteering.ShouldMove.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldRestoreNavigatorAndSteeringState_ForGuidedAerialTraversal(bool useMemoryPack)
    {
        var source = CreateConfiguredGuidedNavigator(medium: TraversalMedium.Gas, isFlying: true);
        NavSteering sourceSteering = TestRequire.NotNull(source.Steering);
        sourceSteering.CurrentRequest.Should().BeOfType<VolumePathRequest>();
        sourceSteering.TrailGuide.Should().BeOfType<VolumeGuide>();

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        SerializationUtility.PopulateRecord(target, SerializationUtility.SerializeRecord(source, useMemoryPack), useMemoryPack);
        NavSteering targetSteering = TestRequire.NotNull(target.Steering);

        target.IsGuideded.Should().BeTrue();
        target.FrameRequest.Rate.Should().Be(source.FrameRequest.Rate);
        target.FrameRequest.IsRequestingJump.Should().Be(source.FrameRequest.IsRequestingJump);
        target.FrameRequest.IsRequestingFlight.Should().Be(source.FrameRequest.IsRequestingFlight);
        target.FrameRequest.IsRequestingSwim.Should().Be(source.FrameRequest.IsRequestingSwim);
        target.FrameRequest.IsRequestingClimb.Should().Be(source.FrameRequest.IsRequestingClimb);
        target.Size.Should().Be(source.Size);

        AssertSteeringStateMatches(sourceSteering, targetSteering);

        TrailblazerManager.Simulate();
        target.Simulate();

        target.FrameRequest.Direction.Should().NotBe(Vector3d.Zero);
        target.FrameRequest.Direction.y.Should().BeGreaterThan(Fixed64.Zero);
        target.FrameRequest.IsRequestingFlight.Should().BeTrue();
        targetSteering.ShouldMove.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldRestoreNavigatorAndSteeringState_ForAStarTransitionFallback(bool useMemoryPack)
    {
        RegisterTransitionFallbackAStarScene();

        var source = CreateConfiguredTransitionFallbackAStarNavigator();
        NavSteering sourceSteering = TestRequire.NotNull(source.Steering);
        sourceSteering.CurrentRequest.Should().BeOfType<AStarPathRequest>();
        sourceSteering.TrailGuide.Should().BeOfType<AStarGuide>();

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        SerializationUtility.PopulateRecord(target, SerializationUtility.SerializeRecord(source, useMemoryPack), useMemoryPack);
        NavSteering targetSteering = TestRequire.NotNull(target.Steering);

        targetSteering.CurrentRequest.Should().BeOfType<AStarPathRequest>();
        targetSteering.TrailGuide.Should().BeOfType<AStarGuide>();
        AssertSteeringStateMatches(sourceSteering, targetSteering);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldRestoreBlockedAerialGuideProgress(bool useMemoryPack)
    {
        AddOpen(Vector3d.Zero);
        AddOpen(new Vector3d(1, 0, 0));
        AddOpen(new Vector3d(1, 1, 0));
        AddOpen(new Vector3d(2, 1, 0));
        AddOpen(new Vector3d(3, 1, 0));
        AddOpen(new Vector3d(4, 1, 0));
        AddOpen(new Vector3d(4, 0, 0));
        AddObstacle(new Vector3d(2, 0, 0));

        var source = CreateNavigator(Vector3d.Zero, size: Fixed64.One);
        source.SetTrekCondition(medium: TraversalMedium.Gas);
        source.ConfigureForGuidedTraversal(aStarHeuristic: HeuristicMethod.Euclidean);
        source.ApplyGuidedTrekRequest(
            new Vector3d(4, 0, 0),
            rate: TrekRate.Fast,
            isRequestingFlight: true,
            isRequestingJump: false,
            groupId: 3);

        TrailblazerManager.Simulate();
        NavSteering sourceSteering = TestRequire.NotNull(source.Steering);
        sourceSteering.GetHeading(source);

        VolumeGuide sourceGuide = sourceSteering.TrailGuide.Should().BeOfType<VolumeGuide>().Subject;
        if (sourceGuide.TryGetWaypointAt(sourceGuide.CurrentWaypointIndex + 1, out _))
            sourceGuide.AdvanceWaypoint();

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        SerializationUtility.PopulateRecord(target, SerializationUtility.SerializeRecord(source, useMemoryPack), useMemoryPack);
        NavSteering targetSteering = TestRequire.NotNull(target.Steering);

        targetSteering.TrailGuide.Should().BeOfType<VolumeGuide>();
        ((VolumeGuide)targetSteering.TrailGuide).CurrentWaypointIndex.Should().Be(sourceGuide.CurrentWaypointIndex);

        TrailblazerManager.Simulate();
        target.Simulate();

        target.FrameRequest.Direction.Should().NotBe(Vector3d.Zero);
        target.FrameRequest.IsRequestingFlight.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldRestoreNavigatorAndSteeringState_ForGuidedSwimTraversal(bool useMemoryPack)
    {
        AddWater(Vector3d.Zero);
        AddWater(new Vector3d(0, 0, 1));
        AddWater(new Vector3d(0, 0, 2));

        var source = CreateNavigator(Vector3d.Zero, size: Fixed64.One);
        source.SetWaterContact(surfaceLevel: (Fixed64)2, updateMotorState: true);
        source.ConfigureForGuidedTraversal(aStarHeuristic: HeuristicMethod.Euclidean);
        source.ApplyGuidedTrekRequest(
            new Vector3d(0, 0, 2),
            rate: TrekRate.Fast,
            isRequestingSwim: true,
            isRequestingJump: false,
            groupId: 3);

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        SerializationUtility.PopulateRecord(target, SerializationUtility.SerializeRecord(source, useMemoryPack), useMemoryPack);
        NavSteering targetSteering = TestRequire.NotNull(target.Steering);

        target.IsGuideded.Should().BeTrue();
        target.FrameRequest.IsRequestingFlight.Should().BeFalse();
        target.FrameRequest.IsRequestingSwim.Should().BeTrue();
        targetSteering.CurrentRequest.Should().BeOfType<VolumePathRequest>();
        ((VolumePathRequest)targetSteering.CurrentRequest).Medium.Should().Be(TraversalMedium.Liquid);

        TrailblazerManager.Simulate();
        target.Simulate();

        target.FrameRequest.Direction.Should().NotBe(Vector3d.Zero);
        target.FrameRequest.Direction.z.Should().BeGreaterThan(Fixed64.Zero);
        target.FrameRequest.IsRequestingFlight.Should().BeFalse();
        target.FrameRequest.IsRequestingSwim.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldRestorePendingSwimExitHandoff_AndActivateChartFollowup(bool useMemoryPack)
    {
        RegisterVolumeExitHandoffScene("NavigatorSerializationSwimExitHandoff");

        var source = CreateNavigator(Vector3d.Zero, size: Fixed64.One);
        source.SetWaterContact(surfaceLevel: Fixed64.Zero, updateMotorState: true);
        source.ConfigureForGuidedTraversal(
                pathAlgorithm: SolidPathAlgorithm.FlowField,
                allowTraversalTransitions: true,
                maxClimbHeight: (Fixed64)2,
                flowFieldExtraFloodRange: 12
        );
        source.ApplyGuidedTrekRequest(
            new Vector3d(4, 0, 0),
            rate: TrekRate.Fast,
            isRequestingSwim: true,
            isRequestingJump: false,
            groupId: 5);

        NavSteering sourceSteering = TestRequire.NotNull(source.Steering);
        VolumePathRequest sourceRequest = Assert.IsType<VolumePathRequest>(TestRequire.NotNull(sourceSteering.CurrentRequest));
        sourceRequest.TargetPosition.Should().Be(new Vector3d(2, 0, 0));

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        SerializationUtility.PopulateRecord(target, SerializationUtility.SerializeRecord(source, useMemoryPack), useMemoryPack);
        NavSteering targetSteering = TestRequire.NotNull(target.Steering);

        Assert.IsType<VolumePathRequest>(TestRequire.NotNull(targetSteering.CurrentRequest))
            .TargetPosition.Should().Be(new Vector3d(2, 0, 0));

        target.SetTestPosition(new Vector3d(2, 0, 0));
        target.SetGroundContact(surfaceLevel: Fixed64.Zero, updateMotorState: true);
        targetSteering.Arrive();

        TrailblazerManager.Simulate();
        target.Simulate();

        FlowFieldPathRequest followupRequest = Assert.IsType<FlowFieldPathRequest>(TestRequire.NotNull(targetSteering.CurrentRequest));
        followupRequest.TargetPosition.Should().Be(new Vector3d(4, 0, 0));
        followupRequest.MaxClimbHeight.Should().Be((Fixed64)2);
        targetSteering.MovementGroupID.Should().Be(5);
        target.FrameRequest.IsRequestingFlight.Should().BeFalse();
        target.FrameRequest.IsRequestingSwim.Should().BeFalse();
        target.FrameRequest.Direction.x.Should().BeGreaterThan(Fixed64.Zero);

        PathManager.UnloadChart("NavigatorSerializationSwimExitHandoff");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldRestorePendingAerialLandingHandoff_AndActivateChartFollowup(bool useMemoryPack)
    {
        const string sceneKey = "NavigatorSerializationAerialLandingHandoff";
        RegisterAerialLandingHandoffScene(sceneKey);

        var source = CreateNavigator(Vector3d.Zero, size: Fixed64.One);
        source.SetTrekCondition(medium: TraversalMedium.Gas);
        source.ConfigureForGuidedTraversal(
            pathAlgorithm: SolidPathAlgorithm.AStar,
            allowTraversalTransitions: true,
            aStarHeuristic: HeuristicMethod.Euclidean
        );
        source.ApplyGuidedTrekRequest(
            new Vector3d(4, 0, 0),
            rate: TrekRate.Fast,
            isRequestingFlight: true,
            isRequestingJump: false,
            groupId: 6);

        NavSteering sourceSteering = TestRequire.NotNull(source.Steering);
        VolumePathRequest sourceRequest = Assert.IsType<VolumePathRequest>(TestRequire.NotNull(sourceSteering.CurrentRequest));
        sourceRequest.TargetPosition.Should().Be(new Vector3d(1, 0, 0));

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        SerializationUtility.PopulateRecord(target, SerializationUtility.SerializeRecord(source, useMemoryPack), useMemoryPack);
        NavSteering targetSteering = TestRequire.NotNull(target.Steering);

        Assert.IsType<VolumePathRequest>(TestRequire.NotNull(targetSteering.CurrentRequest))
            .TargetPosition.Should().Be(new Vector3d(1, 0, 0));
        target.FrameRequest.IsRequestingFlight.Should().BeTrue();

        target.SetTestPosition(new Vector3d(1, 0, 0));
        target.SetGroundContact(surfaceLevel: Fixed64.Zero, updateMotorState: true);
        targetSteering.Arrive();

        TrailblazerManager.Simulate();
        target.Simulate();

        AStarPathRequest followupRequest = Assert.IsType<AStarPathRequest>(TestRequire.NotNull(targetSteering.CurrentRequest));
        followupRequest.TargetPosition.x.Should().Be((Fixed64)4);
        followupRequest.TargetPosition.y.Should().Be(Fixed64.Zero);
        followupRequest.TargetPosition.z.Should().Be(Fixed64.Zero);
        followupRequest.AllowTraversalTransitions.Should().BeTrue();
        targetSteering.MovementGroupID.Should().Be(6);
        targetSteering.Destination.x.Should().Be((Fixed64)4);
        target.FrameRequest.IsRequestingFlight.Should().BeFalse();

        UnloadAerialLandingHandoffScene(sceneKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldRestorePendingAerialClimbHandoff_AndPreserveClimbOnFollowup(bool useMemoryPack)
    {
        const string sceneKey = "NavigatorSerializationAerialClimbHandoff";
        GuidedPathTestScene.RegisterAerialClimbHandoffScene(sceneKey);

        var source = CreateNavigator(Vector3d.Zero, size: Fixed64.One);
        source.SetTrekCondition(medium: TraversalMedium.Gas);
        source.ConfigureForGuidedTraversal(
            pathAlgorithm: SolidPathAlgorithm.AStar,
            allowTraversalTransitions: true
        );
        source.ApplyGuidedTrekRequest(
            new Vector3d(4, 0, 0),
            rate: TrekRate.Fast,
            isRequestingFlight: true,
            isRequestingJump: false,
            groupId: 8);

        source.FrameRequest.IsRequestingClimb.Should().BeTrue();

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        SerializationUtility.PopulateRecord(target, SerializationUtility.SerializeRecord(source, useMemoryPack), useMemoryPack);
        NavSteering targetSteering = TestRequire.NotNull(target.Steering);

        target.FrameRequest.IsRequestingFlight.Should().BeTrue();
        target.FrameRequest.IsRequestingClimb.Should().BeTrue();

        target.SetTestPosition(new Vector3d(1, 0, 0));
        target.SetGroundContact(surfaceLevel: Fixed64.Zero, updateMotorState: true);
        targetSteering.Arrive();

        TrailblazerManager.Simulate();
        target.Simulate();

        targetSteering.CurrentRequest.Should().BeOfType<AStarPathRequest>();
        targetSteering.MovementGroupID.Should().Be(8);
        target.FrameRequest.IsRequestingFlight.Should().BeFalse();
        target.FrameRequest.IsRequestingClimb.Should().BeTrue();

        PathManager.UnloadChart($"{sceneKey}-Landing");
        PathManager.UnloadChart($"{sceneKey}-Target");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldSupportPartialNavigatorPayloads_AndPreserveOmittedBranches(bool useMemoryPack)
    {
        var source = CreateConfiguredNavigator();
        source.OccupantGroupId = 9;
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);

        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "OccupantGroupId");
        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "Steering");
        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "Turning");
        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "Motor");

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        target.OccupantGroupId = 9;
        NavSteering targetSteering = TestRequire.NotNull(target.Steering);
        NavTurning targetTurning = TestRequire.NotNull(target.Turning);
        NavMotor targetMotor = TestRequire.NotNull(target.Motor);
        targetSteering.StopMultiplier = (Fixed64)0.33f;
        targetTurning.TurnRate = (Fixed64)0.72f;
        targetMotor.Handler.Move.MaxFastSpeed = (Fixed64)8;

        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.Position.Should().Be(source.Position);
        // since we removed the occupantGroupId entry, it should fall back to the default value of 1
        // regardless of the source and target values before population
        target.OccupantGroupId.Should().Be(1);
        targetSteering.StopMultiplier.Should().Be((Fixed64)0.33f);
        targetTurning.TurnRate.Should().Be((Fixed64)0.72f);
        targetMotor.Handler.Move.MaxFastSpeed.Should().Be((Fixed64)8);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldLoadSetupOnlyNavigatorWithoutControllers(bool useMemoryPack)
    {
        var source = new TestNavigator();
        source.Setup(new Vector3d(1, 0, 1), size: Fixed64.One);

        var target = new TestNavigator(TrailblazerManager.DefaultContext);
        SerializationUtility.PopulateRecord(target, SerializationUtility.SerializeRecord(source, useMemoryPack), useMemoryPack);

        target.Position.Should().Be(new Vector3d(1, 0, 1));
        target.LastPosition.Should().Be(new Vector3d(1, 0, 1));
        target.Rotation.Should().Be(FixedQuaternion.Identity);
        target.Forward.Should().Be(Vector3d.Forward);
        target.Steering.Should().BeNull();
        target.Turning.Should().BeNull();
        target.Motor.Should().BeNull();
        target.IsActive.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldUseBackwardCompatibleDefaults_WhenPayloadOmitsNewerFields(bool useMemoryPack)
    {
        RegisterGuidedPathChart("NavigatorSerializationLegacyDefaults");

        var source = CreateConfiguredGuidedNavigator(SolidPathAlgorithm.FlowField);
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);

        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "GuidedAllowUnwalkableEndpoints");
        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "GuidedAllowTraversalTransitions");
        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "GuidedMaxClimbHeight");
        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "GuidedFlowFieldExtraFloodRange");
        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "Steering", "PathRecheckCooldownFrames");
        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "Steering", "StopMultiplier");
        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "Steering", "BrakingPower");
        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "Steering", "PathRequest", "MaxClimbHeight");
        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "Steering", "PathRequest", "FlowFieldExtraFloodRange");

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        NavSteering targetSteering = TestRequire.NotNull(target.Steering);
        NavSteering sourceSteering = TestRequire.NotNull(source.Steering);
        bool expectedAllowUnwalkable = target.GuidedAllowUnwalkableEndpoints;
        bool expectedAllowTraversalTransitions = target.GuidedAllowTraversalTransitions;
        Fixed64 expectedRootFlowFieldMaxClimbHeight = target.GuidedMaxClimbHeight;
        int expectedRootExtraFloodRange = target.GuidedFlowFieldExtraFloodRange;
        int expectedPathRecheckCooldown = targetSteering.PathRecheckCooldownFrames;
        Fixed64 expectedStopMultiplier = targetSteering.StopMultiplier;
        Fixed64 expectedBrakingPower = targetSteering.BrakingPower;

        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.IsGuideded.Should().BeTrue();
        target.GuidedAllowUnwalkableEndpoints.Should().Be(expectedAllowUnwalkable);
        target.GuidedAllowTraversalTransitions.Should().Be(expectedAllowTraversalTransitions);
        target.GuidedMaxClimbHeight.Should().Be(expectedRootFlowFieldMaxClimbHeight);
        target.GuidedFlowFieldExtraFloodRange.Should().Be(expectedRootExtraFloodRange);
        targetSteering.PathRecheckCooldownFrames.Should().Be(expectedPathRecheckCooldown);
        targetSteering.StopMultiplier.Should().Be(expectedStopMultiplier);
        targetSteering.BrakingPower.Should().Be(expectedBrakingPower);
        targetSteering.BehaviorWeights.Separation.Should().Be(sourceSteering.BehaviorWeights.Separation);

        var request = targetSteering.CurrentRequest.Should().BeOfType<FlowFieldPathRequest>().Subject;
        request.MaxClimbHeight.Should().Be(Fixed64.One);
        request.ExtraFloodRange.Should().Be(FlowFieldPathRequest.DefaultExtraFloodRange);

        PathManager.UnloadChart("NavigatorSerializationLegacyDefaults");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldUseBackwardCompatibleDefaults_WhenPayloadOmitsFacingDirection(bool useMemoryPack)
    {
        var source = CreateConfiguredNavigator();
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);

        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "FrameRequest", "FacingDirection");

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.FrameRequest.FacingDirection.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldUseBackwardCompatibleDefaults_WhenPayloadOmitsJumpAffordability(bool useMemoryPack)
    {
        var source = CreateConfiguredNavigator();
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);

        payload = SerializationUtility.RemovePayloadEntry(payload, useMemoryPack, "FrameRequest", "CanAffordJump");

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.FrameRequest.CanAffordJump.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldClearSteeringSession_WhenRequestRehydrationFails(bool useMemoryPack)
    {
        RegisterGuidedPathChart("NavigatorSerializationInvalidRequest");

        var source = CreateConfiguredGuidedNavigator(SolidPathAlgorithm.AStar);
        NavSteering sourceSteering = TestRequire.NotNull(source.Steering);
        source.ConfigureForGuidedTraversal(allowUnwalkableEndpoints: false);
        sourceSteering.CurrentRequest.Should().BeOfType<AStarPathRequest>().Subject.AllowUnwalkableEndpoints = false;
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        payload = SerializationUtility.SetPayloadValue(
            payload,
            useMemoryPack,
            new Vector3d(512, 0, 512),
            "Steering",
            "PathRequest",
            "TargetPosition");

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);
        NavSteering targetSteering = TestRequire.NotNull(target.Steering);

        targetSteering.CurrentRequest.Should().BeNull();
        targetSteering.TrailGuide.Should().BeNull();
        targetSteering.ShouldMove.Should().BeFalse();
        targetSteering.IsStuck.Should().BeFalse();
        targetSteering.HasLineOfSightPath.Should().BeFalse();
        targetSteering.Destination.Should().Be(Vector3d.Zero);
        targetSteering.TargetDirection.Should().Be(Vector3d.Zero);

        TrailblazerManager.Simulate();
        target.Simulate();

        target.FrameRequest.Direction.Should().Be(Vector3d.Zero);

        PathManager.UnloadChart("NavigatorSerializationInvalidRequest");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldClearTransientState_WhenLocomotionsLoadDisabled(bool useMemoryPack)
    {
        var source = CreateConfiguredMotorAgent();
        object payload = SerializationUtility.SerializeRecord(source.Motor, useMemoryPack);

        payload = SerializationUtility.SetPayloadValue(payload, useMemoryPack, false, "Handler", "Move", "IsEnabled");
        payload = SerializationUtility.SetPayloadValue(payload, useMemoryPack, false, "Handler", "Platform", "IsEnabled");
        payload = SerializationUtility.SetPayloadValue(payload, useMemoryPack, false, "Handler", "Jump", "IsEnabled");
        payload = SerializationUtility.SetPayloadValue(payload, useMemoryPack, false, "Handler", "Fall", "IsEnabled");
        payload = SerializationUtility.SetPayloadValue(payload, useMemoryPack, false, "Handler", "Slide", "IsEnabled");
        payload = SerializationUtility.SetPayloadValue(payload, useMemoryPack, false, "Handler", "Water", "IsEnabled");

        var target = MockMotorAgentTestFactory.CreatePlatformAgent(
            startPosition: new Vector3d(-2, 0, -2),
            platformMatrix: MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(-1, 0, -1)),
            motionTransfer: MotionTransfer.PermaLocked);
        NavMotor targetMotor = TestRequire.NotNull(target.Motor);
        SerializationUtility.PopulateRecord(targetMotor, payload, useMemoryPack);
        var targetPlatform = TestRequire.NotNull(targetMotor.Handler.Platform);
        var targetJump = TestRequire.NotNull(targetMotor.Handler.Jump);
        var targetFall = TestRequire.NotNull(targetMotor.Handler.Fall);
        var targetSlide = TestRequire.NotNull(targetMotor.Handler.Slide);
        var targetWater = TestRequire.NotNull(targetMotor.Handler.Water);

        targetMotor.Handler.Move.IsEnabled.Should().BeFalse();
        targetMotor.Handler.Move.FrameVelocity.Should().Be(Vector3d.Zero);

        targetPlatform.IsEnabled.Should().BeFalse();
        targetPlatform.IsNewPlatform.Should().BeFalse();
        targetPlatform.ActivePlatform.Should().BeNull();
        targetPlatform.PreviousPlatform.Should().BeNull();
        targetPlatform.HoldPlatform.Should().BeNull();
        targetPlatform.MovementTransfer.Should().Be(MotionTransfer.None);
        targetPlatform.ScoutLocalPoint.Should().Be(Vector3d.Zero);
        targetPlatform.ScoutLocalRotation.Should().Be(FixedQuaternion.Identity);
        targetPlatform.PlatformVelocity.Should().Be(Vector3d.Zero);
        targetPlatform.FramePlatformVelocity.Should().Be(Vector3d.Zero);
        targetPlatform.HoldPlatformFrames.Should().Be(0);

        targetJump.IsEnabled.Should().BeFalse();
        targetJump.IsJumping.Should().BeFalse();
        targetJump.IsHoldingJump.Should().BeFalse();
        targetJump.JumpStartTime.Should().Be(Fixed64.Zero);
        targetJump.FrameJumpDirection.Should().Be(Vector3d.Zero);

        targetFall.IsEnabled.Should().BeFalse();
        targetFall.IsFalling.Should().BeFalse();
        targetFall.FallStart.Should().Be(Fixed64.Zero);
        targetFall.FallEnd.Should().Be(Fixed64.Zero);

        targetSlide.IsEnabled.Should().BeFalse();
        targetSlide.IsSliding.Should().BeFalse();

        targetWater.IsEnabled.Should().BeFalse();
        targetWater.IsSwimming.Should().BeFalse();
        targetWater.IsDiving.Should().BeFalse();
        targetWater.UnderwaterTimer.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void JsonRoundTrip_ShouldAllowMovementGroupsToBePrewarmed_AfterLoad()
    {
        RegisterMovementGroupFormationChart("NavigatorSerializationMovementGroupPrewarm");

        Vector3d sharedDestination = new(4, 0, 0);

        var sourceFirst = CreateNavigator(new Vector3d(1, 0, 0), size: Fixed64.One);
        var sourceSecond = CreateNavigator(new Vector3d(2, 0, 0), size: Fixed64.One);

        sourceFirst.ApplyGuidedTrekRequest(sharedDestination, groupId: 5);
        sourceSecond.ApplyGuidedTrekRequest(sharedDestination, groupId: 5);

        string firstJson = JsonRecordSerializer.Serialize(sourceFirst, writeIndented: true);
        string secondJson = JsonRecordSerializer.Serialize(sourceSecond, writeIndented: true);

        TrailblazerManager.Reset();

        var lazyFirst = CreateNavigator(new Vector3d(-3, 0, 0), size: Fixed64.One);
        var lazySecond = CreateNavigator(new Vector3d(-2, 0, 0), size: Fixed64.One);
        JsonRecordSerializer.Populate(lazyFirst, firstJson);
        JsonRecordSerializer.Populate(lazySecond, secondJson);
        NavSteering lazyFirstSteering = TestRequire.NotNull(lazyFirst.Steering);
        NavSteering lazySecondSteering = TestRequire.NotNull(lazySecond.Steering);

        lazyFirstSteering.GetHeading(lazyFirst);
        lazySecondSteering.GetHeading(lazySecond);

        lazyFirstSteering.Destination.Should().Be(sharedDestination);
        lazySecondSteering.Destination.Should().Be(new Vector3d((Fixed64)4.5f, Fixed64.Zero, Fixed64.Zero));

        TrailblazerManager.Reset();

        var prewarmedFirst = CreateNavigator(new Vector3d(-3, 0, 0), size: Fixed64.One);
        var prewarmedSecond = CreateNavigator(new Vector3d(-2, 0, 0), size: Fixed64.One);
        JsonRecordSerializer.Populate(prewarmedFirst, firstJson);
        JsonRecordSerializer.Populate(prewarmedSecond, secondJson);
        NavSteering prewarmedFirstSteering = TestRequire.NotNull(prewarmedFirst.Steering);
        NavSteering prewarmedSecondSteering = TestRequire.NotNull(prewarmedSecond.Steering);

        prewarmedFirst.PrewarmMovementGroup();
        prewarmedSecond.PrewarmMovementGroup();

        prewarmedFirstSteering.GetHeading(prewarmedFirst);
        prewarmedSecondSteering.GetHeading(prewarmedSecond);

        prewarmedFirstSteering.Destination.Should().Be(new Vector3d((Fixed64)3.5f, Fixed64.Zero, Fixed64.Zero));
        prewarmedSecondSteering.Destination.Should().Be(new Vector3d((Fixed64)4.5f, Fixed64.Zero, Fixed64.Zero));

        PathManager.UnloadChart("NavigatorSerializationMovementGroupPrewarm");
    }

    private static TestNavigator CreateNavigator(Vector3d position, Fixed64? size = null)
    {
        var navigator = new TestNavigator();
        navigator.Setup(
            position,
            rotation: FixedQuaternion.FromAxisAngle(Vector3d.Up, (Fixed64)0.25f),
            velocity: new Vector3d(1, 0, 1),
            size: size ?? (Fixed64)2);
        navigator.Initialize(new TrekCondition()
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = Fixed64.Zero,
            GroundState = new GroundCondition()
        });
        return navigator;
    }

    private static MockMotorAgent CreateConfiguredMotorAgent()
    {
        var source = MockMotorAgentTestFactory.CreatePlatformAgent(
            startPosition: new Vector3d(2, 0, 3),
            platformMatrix: MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(4, 0, 5)),
            motionTransfer: MotionTransfer.PermaTransfer);
        NavMotor motor = TestRequire.NotNull(source.Motor);
        var jump = TestRequire.NotNull(motor.Handler.Jump);
        var fall = TestRequire.NotNull(motor.Handler.Fall);
        var slide = TestRequire.NotNull(motor.Handler.Slide);
        var water = TestRequire.NotNull(motor.Handler.Water);
        var fly = TestRequire.NotNull(motor.Handler.Fly);
        var platform = TestRequire.NotNull(motor.Handler.Platform);

        motor.Handler.IsInControl = false;
        motor.Handler.Move.MaxFastSpeed = (Fixed64)1.75f;
        motor.Handler.Move.FrameVelocity = new Vector3d(1, 2, 3);

        jump.MaxJumpCount = 2;
        jump.RegisterJump();
        jump.FrameJumpDirection = new Vector3d(0, 1, 1).Normal;
        jump.StartCooldown();

        fall.IsFalling = true;
        fall.FallStart = (Fixed64)9;
        fall.FallEnd = (Fixed64)3;

        slide.IsSliding = true;

        water.IsSwimming = true;
        water.IsDiving = true;
        water.UnderwaterTimer = (Fixed64)7;

        fly.MaxFlySpeed = (Fixed64)2.5f;
        fly.GravityCompensation = (Fixed64)0.75f;
        fly.IsFlying = true;

        var holdPlatform = new PlatformSnapshot(9, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(6, 0, 6)));
        platform.IsNewPlatform = true;
        platform.PreviousPlatform = new PlatformSnapshot(8, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(3, 0, 3)));
        platform.SetHoldPlatform(holdPlatform);
        platform.TickHoldOnPlatform();
        platform.ScoutLocalPoint = new Vector3d(1, 0, 1);
        platform.ScoutLocalRotation = FixedQuaternion.FromAxisAngle(Vector3d.Up, (Fixed64)0.25f);
        platform.PlatformVelocity = new Vector3d(5, 0, 0);
        platform.FramePlatformVelocity = new Vector3d(2, 0, 0);

        return source;
    }

    private static TestNavigator CreateConfiguredNavigator()
    {
        var source = CreateNavigator(new Vector3d(2, 0, 2));
        NavMotor motor = TestRequire.NotNull(source.Motor);
        NavTurning turning = TestRequire.NotNull(source.Turning);
        var platform = TestRequire.NotNull(motor.Handler.Platform);
        var jump = TestRequire.NotNull(motor.Handler.Jump);
        var fall = TestRequire.NotNull(motor.Handler.Fall);
        var fly = TestRequire.NotNull(motor.Handler.Fly);
        var climb = TestRequire.NotNull(motor.Handler.Climb);
        source.ApplyInputTrekRequest(
            Vector3d.Right,
            TrekRate.Moderate,
            isRequestingJump: true,
            facingDirection: Vector3d.Forward,
            canAffordJump: false);
        source.SetGroundContact(
            surfaceLevel: Fixed64.Zero,
            platform: new PlatformSnapshot(12, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(1, 0, 1))),
            surfaceFriction: (Fixed64)0.15f,
            motionTransfer: MotionTransfer.PermaLocked,
            updateMotorState: true);

        source.ConfigureForGuidedTraversal(
            pathAlgorithm: SolidPathAlgorithm.FlowField,
            allowUnwalkableEndpoints: true,
            allowTraversalTransitions: true,
            maxClimbHeight: (Fixed64)4,
            aStarHeuristic: HeuristicMethod.Euclidean,
            flowFieldExtraFloodRange: 32
        );

        source.FootPositionAdjust = (Fixed64)0.75f;
        source.IsLockedOn = true;
        motor.Handler.Move.FrameVelocity = new Vector3d(1, 0, 2);
        platform.ActivePlatform = new PlatformSnapshot(12, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(1, 0, 1)));
        platform.MovementTransfer = MotionTransfer.PermaLocked;
        platform.ScoutLocalPoint = new Vector3d(0, 0, 1);
        platform.ScoutLocalRotation = FixedQuaternion.FromAxisAngle(Vector3d.Up, (Fixed64)0.5f);
        jump.RegisterJump();
        jump.FrameJumpDirection = Vector3d.Up;
        fall.IsFalling = true;
        fall.FallStart = (Fixed64)10;
        fly.GravityCompensation = (Fixed64)0.8f;
        fly.IsFlying = true;
        climb.IsClimbing = true;
        climb.ActiveClimbKind = ClimbAffordanceKind.Surface;
        climb.AttachmentId = 21;
        climb.AttachmentPoint = new Vector3d(2, 1, 2);
        climb.AttachedSurfaceNormal = Vector3d.Left;
        climb.AttachedUpDirection = Vector3d.Up;
        turning.CanTurn = false;
        turning.TurnRate = (Fixed64)0.35f;

        return source;
    }

    private static TestNavigator CreateConfiguredGuidedNavigator(
        SolidPathAlgorithm pathMode = SolidPathAlgorithm.AStar,
        TraversalMedium medium = TraversalMedium.Solid,
        bool isFlying = false,
        bool isClimbing = false)
    {
        var source = CreateNavigator(Vector3d.Zero, size: Fixed64.One);
        source.SetTrekCondition(medium: medium);
        NavSteering steering = TestRequire.NotNull(source.Steering);
        source.ConfigureForGuidedTraversal(
                pathAlgorithm: pathMode,
                allowUnwalkableEndpoints: true,
                allowTraversalTransitions: true,
                maxClimbHeight: (Fixed64)2,
                aStarHeuristic: HeuristicMethod.Euclidean,
                flowFieldExtraFloodRange: 24
        );

        steering.PathRecheckCooldownFrames = 9;
        steering.StopMultiplier = (Fixed64)0.75f;
        steering.GroupFactor = (Fixed64)12;
        steering.AvoidFactor = (Fixed64)4;
        steering.BehaviorWeights = new GroupBehaviorWeights()
        {
            Separation = (Fixed64)3,
            Alignment = (Fixed64)0.75f,
            Cohesion = (Fixed64)0.4f,
            Avoidance = (Fixed64)1.25f
        };
        steering.BrakingPower = (Fixed64)0.2f;

        Vector3d targetPosition = medium == TraversalMedium.Gas
            ? new Vector3d(4, 4, 0)
            : new Vector3d(4, 0, 0);

        if (medium == TraversalMedium.Gas)
        {
            AddOpen(Vector3d.Zero);
            AddOpen(new Vector3d(0, 1, 0));
            AddOpen(new Vector3d(0, 2, 0));
            AddOpen(new Vector3d(0, 3, 0));
            AddOpen(new Vector3d(0, 4, 0));
            AddOpen(new Vector3d(1, 4, 0));
            AddOpen(new Vector3d(2, 4, 0));
            AddOpen(new Vector3d(3, 4, 0));
            AddOpen(new Vector3d(4, 4, 0));
        }

        source.ApplyGuidedTrekRequest(
            targetPosition,
            rate: TrekRate.Fast,
            isRequestingFlight: isFlying,
            isRequestingClimb: isClimbing,
            isRequestingJump: true,
            groupId: 7);

        TrailblazerManager.Simulate();
        steering.GetHeading(source);
        steering.PauseAutoStop();

        if (steering.TrailGuide is AStarGuide aStarGuide)
        {
            aStarGuide.AdvanceWaypoint();
            TrailblazerManager.Simulate();
            steering.GetHeading(source);
        }

        return source;
    }

    private static TestNavigator CreateConfiguredTransitionFallbackAStarNavigator()
    {
        var source = CreateNavigator(Vector3d.Zero, size: Fixed64.One);
        NavSteering steering = TestRequire.NotNull(source.Steering);
        source.ApplyInputTrekRequest(Vector3d.Right, TrekRate.Fast, isRequestingJump: true);

        steering.PathRecheckCooldownFrames = 11;
        steering.StopMultiplier = (Fixed64)0.8f;
        steering.GroupFactor = (Fixed64)10;
        steering.AvoidFactor = (Fixed64)3;
        steering.BehaviorWeights = new GroupBehaviorWeights()
        {
            Separation = (Fixed64)2.5f,
            Alignment = (Fixed64)0.5f,
            Cohesion = (Fixed64)0.35f,
            Avoidance = (Fixed64)1.4f
        };
        steering.BrakingPower = (Fixed64)0.3f;

        AStarPathRequest request = TestRequire.NotNull(AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean,
            allowUnwalkableEndpoints: true));
        request.MaxClimbHeight = (Fixed64)2;
        request.AllowTraversalTransitions = true;

        steering.ApplyPathRequest(request, groupId: 5);

        TrailblazerManager.Simulate();
        steering.GetHeading(source);
        steering.PauseAutoStop();

        AStarGuide guide = steering.TrailGuide.Should().BeOfType<AStarGuide>().Subject;
        if (guide.TryGetWaypointAt(guide.CurrentWaypointIndex + 1, out _))
        {
            guide.AdvanceWaypoint();
            TrailblazerManager.Simulate();
            steering.GetHeading(source);
        }

        return source;
    }

    private static void RegisterGuidedPathChart(string chartKey)
    {
        bool[,,] data = new bool[1, 5, 3]
        {
            {
                { true, true, true },
                { true, true, true },
                { false, true, false },
                { true, true, true },
                { true, true, true }
            }
        };

        PathTestFactory.RegisterFromData(chartKey, data, Vector3d.Zero);
    }

    private static void RegisterTransitionFallbackAStarScene()
    {
        PathTestFactory.RegisterSingleWalkablePoint("TransitionFallbackStart", Vector3d.Zero);
        PathTestFactory.RegisterSingleWalkablePoint("TransitionFallbackEnd", new Vector3d(4, 0, 0));

        AddWater(new Vector3d(1, 0, 0));
        AddWater(new Vector3d(2, 0, 0));
        AddWater(new Vector3d(3, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "transition-fallback-entry",
            type: TraversalTransitionType.SwimEntry,
            source: TraversalTransitionAnchor.Solid(Vector3d.Zero),
            destination: TraversalTransitionAnchor.Liquid(new Vector3d(1, 0, 0)),
            pathCostModifier: 2)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "transition-fallback-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.Liquid(new Vector3d(3, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();
    }

    private static void RegisterVolumeExitHandoffScene(string chartKey)
    {
        NavigationChartCell[,,] data = new NavigationChartCell[1, 3, 1]
        {
            {
                { NavigationChartCell.SolidLiquid },
                { NavigationChartCell.Solid },
                { NavigationChartCell.Solid }
            }
        };

        PathManager.Register(NavigationChart.From3D(chartKey, data, new Vector3d(2, 0, 0), Fixed64.One));

        AddWater(Vector3d.Zero);
        AddWater(new Vector3d(1, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: $"{chartKey}-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.Liquid(new Vector3d(2, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(2, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();
    }

    private static void RegisterAerialLandingHandoffScene(string sceneKey)
    {
        PathTestFactory.RegisterSingleTraversalPoint(
            $"{sceneKey}-Landing",
            new Vector3d(1, 0, 0),
            TraversalMedia.Solid | TraversalMedia.Gas);
        PathTestFactory.RegisterSingleWalkablePoint($"{sceneKey}-Target", new Vector3d(4, 0, 0));
        AddOpen(Vector3d.Zero);

        AddObstaclePlaneAtX(2);

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: $"{sceneKey}-landing",
            type: TraversalTransitionType.Landing,
            source: TraversalTransitionAnchor.Gas(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: $"{sceneKey}-chart-hop",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Solid(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Solid(new Vector3d(4, 0, 0)),
            pathCostModifier: 2)).Should().BeTrue();
    }

    private static void UnloadAerialLandingHandoffScene(string sceneKey)
    {
        PathManager.UnloadChart($"{sceneKey}-Landing");
        PathManager.UnloadChart($"{sceneKey}-Target");
    }

    private static void RegisterMovementGroupFormationChart(string chartKey)
    {
        bool[,,] data = new bool[1, 7, 1]
        {
            {
                { true },
                { true },
                { true },
                { true },
                { true },
                { true },
                { true }
            }
        };

        PathTestFactory.RegisterFromData(chartKey, data, Vector3d.Zero);
    }

    private static void AssertMotorStateMatches(NavMotor expected, NavMotor actual)
    {
        var expectedHandler = expected.Handler;
        var actualHandler = actual.Handler;
        var expectedJump = TestRequire.NotNull(expectedHandler.Jump);
        var actualJump = TestRequire.NotNull(actualHandler.Jump);
        var expectedFall = TestRequire.NotNull(expectedHandler.Fall);
        var actualFall = TestRequire.NotNull(actualHandler.Fall);
        var expectedSlide = TestRequire.NotNull(expectedHandler.Slide);
        var actualSlide = TestRequire.NotNull(actualHandler.Slide);
        var expectedWater = TestRequire.NotNull(expectedHandler.Water);
        var actualWater = TestRequire.NotNull(actualHandler.Water);
        var expectedFly = TestRequire.NotNull(expectedHandler.Fly);
        var actualFly = TestRequire.NotNull(actualHandler.Fly);
        var expectedClimb = TestRequire.NotNull(expectedHandler.Climb);
        var actualClimb = TestRequire.NotNull(actualHandler.Climb);
        var expectedPlatform = TestRequire.NotNull(expectedHandler.Platform);
        var actualPlatform = TestRequire.NotNull(actualHandler.Platform);

        actual.IsInitialized.Should().Be(expected.IsInitialized);
        actual.CurrentState.ToTrekCondition().Medium.Should().Be(expected.CurrentState.ToTrekCondition().Medium);
        actual.CurrentState.ToTrekCondition().SurfaceLevel.Should().Be(expected.CurrentState.ToTrekCondition().SurfaceLevel);
        actual.CurrentState.ToTrekCondition().CeilingLevel.Should().Be(expected.CurrentState.ToTrekCondition().CeilingLevel);
        actual.CurrentState.PreviousState.Should().Be(expected.CurrentState.PreviousState);

        actualHandler.IsInControl.Should().Be(expectedHandler.IsInControl);

        actualHandler.Move.IsEnabled.Should().Be(expectedHandler.Move.IsEnabled);
        actualHandler.Move.FrameVelocity.Should().Be(expectedHandler.Move.FrameVelocity);
        actualHandler.Move.MaxFastSpeed.Should().Be(expectedHandler.Move.MaxFastSpeed);

        actualJump.IsJumping.Should().Be(expectedJump.IsJumping);
        actualJump.IsHoldingJump.Should().Be(expectedJump.IsHoldingJump);
        actualJump.JumpStartTime.Should().Be(expectedJump.JumpStartTime);
        actualJump.FrameJumpDirection.Should().Be(expectedJump.FrameJumpDirection);
        actualJump.CanJump.Should().Be(expectedJump.CanJump);

        actualFall.IsFalling.Should().Be(expectedFall.IsFalling);
        actualFall.FallStart.Should().Be(expectedFall.FallStart);
        actualFall.FallEnd.Should().Be(expectedFall.FallEnd);

        actualSlide.IsSliding.Should().Be(expectedSlide.IsSliding);

        actualWater.IsSwimming.Should().Be(expectedWater.IsSwimming);
        actualWater.IsDiving.Should().Be(expectedWater.IsDiving);
        actualWater.UnderwaterTimer.Should().Be(expectedWater.UnderwaterTimer);

        actualFly.IsEnabled.Should().Be(expectedFly.IsEnabled);
        actualFly.MaxFlySpeed.Should().Be(expectedFly.MaxFlySpeed);
        actualFly.GravityCompensation.Should().Be(expectedFly.GravityCompensation);
        actualFly.IsFlying.Should().Be(expectedFly.IsFlying);

        actualClimb.IsEnabled.Should().Be(expectedClimb.IsEnabled);
        actualClimb.CanClimb.Should().Be(expectedClimb.CanClimb);
        actualClimb.IsClimbing.Should().Be(expectedClimb.IsClimbing);
        actualClimb.IsMantling.Should().Be(expectedClimb.IsMantling);
        actualClimb.ActiveClimbKind.Should().Be(expectedClimb.ActiveClimbKind);
        actualClimb.AttachmentId.Should().Be(expectedClimb.AttachmentId);
        actualClimb.AttachmentPoint.Should().Be(expectedClimb.AttachmentPoint);
        actualClimb.AttachedSurfaceNormal.Should().Be(expectedClimb.AttachedSurfaceNormal);
        actualClimb.AttachedUpDirection.Should().Be(expectedClimb.AttachedUpDirection);

        actualPlatform.IsNewPlatform.Should().Be(expectedPlatform.IsNewPlatform);
        actualPlatform.MovementTransfer.Should().Be(expectedPlatform.MovementTransfer);
        actualPlatform.ScoutLocalPoint.Should().Be(expectedPlatform.ScoutLocalPoint);
        actualPlatform.ScoutLocalRotation.Should().Be(expectedPlatform.ScoutLocalRotation);
        actualPlatform.PlatformVelocity.Should().Be(expectedPlatform.PlatformVelocity);
        actualPlatform.FramePlatformVelocity.Should().Be(expectedPlatform.FramePlatformVelocity);
        actualPlatform.HoldPlatformFrames.Should().Be(expectedPlatform.HoldPlatformFrames);

        var actualActivePlatform = TestRequire.NotNull(actualPlatform.ActivePlatform);
        var expectedActivePlatform = TestRequire.NotNull(expectedPlatform.ActivePlatform);
        actualActivePlatform.Id.Should().Be(expectedActivePlatform.Id);
        actualActivePlatform.Transform.Should().Be(expectedActivePlatform.Transform);

        actualPlatform.PreviousPlatform?.Id.Should().Be(expectedPlatform.PreviousPlatform?.Id);
        actualPlatform.PreviousPlatform?.Transform.Should().Be(expectedPlatform.PreviousPlatform?.Transform);
        actualPlatform.HoldPlatform?.Id.Should().Be(expectedPlatform.HoldPlatform?.Id);
        actualPlatform.HoldPlatform?.Transform.Should().Be(expectedPlatform.HoldPlatform?.Transform);
    }

    private static void AssertSteeringStateMatches(NavSteering expected, NavSteering actual)
    {
        actual.CanPathfind.Should().Be(expected.CanPathfind);
        actual.Destination.Should().Be(expected.Destination);
        actual.PathRecheckCooldownFrames.Should().Be(expected.PathRecheckCooldownFrames);
        actual.TargetDirection.Should().Be(expected.TargetDirection);
        actual.LastTargetDirection.Should().Be(expected.LastTargetDirection);
        actual.ShouldMove.Should().Be(expected.ShouldMove);
        actual.IsStuck.Should().Be(expected.IsStuck);
        actual.HasLineOfSightPath.Should().Be(expected.HasLineOfSightPath);
        actual.CurrentRouteRequestsClimbIntent.Should().Be(expected.CurrentRouteRequestsClimbIntent);
        actual.CurrentRouteTopologyVersion.Should().Be(expected.CurrentRouteTopologyVersion);
        actual.DistanceToTarget.Should().Be(expected.DistanceToTarget);
        actual.IsAtDestination.Should().Be(expected.IsAtDestination);
        actual.CanMove.Should().Be(expected.CanMove);
        actual.StoppedFrameCount.Should().Be(expected.StoppedFrameCount);
        actual.CanAutoStop.Should().Be(expected.CanAutoStop);
        actual.StopMultiplier.Should().Be(expected.StopMultiplier);
        actual.GroupFactor.Should().Be(expected.GroupFactor);
        actual.AvoidFactor.Should().Be(expected.AvoidFactor);
        actual.BehaviorWeights.Separation.Should().Be(expected.BehaviorWeights.Separation);
        actual.BehaviorWeights.Alignment.Should().Be(expected.BehaviorWeights.Alignment);
        actual.BehaviorWeights.Cohesion.Should().Be(expected.BehaviorWeights.Cohesion);
        actual.BehaviorWeights.Avoidance.Should().Be(expected.BehaviorWeights.Avoidance);
        actual.BrakingPower.Should().Be(expected.BrakingPower);
        actual.MovementGroupID.Should().Be(expected.MovementGroupID);

        if (expected.CurrentRequest == null)
        {
            actual.CurrentRequest.Should().BeNull();
        }
        else
        {
            IPathRequest actualRequest = TestRequire.NotNull(actual.CurrentRequest);
            actualRequest.GetType().Should().Be(expected.CurrentRequest.GetType());
            actualRequest.Origin.Should().Be(expected.CurrentRequest.Origin);
            actualRequest.TargetPosition.Should().Be(expected.CurrentRequest.TargetPosition);
            actualRequest.UnitSize.Should().Be(expected.CurrentRequest.UnitSize);
            actualRequest.AllowUnwalkableEndpoints.Should().Be(expected.CurrentRequest.AllowUnwalkableEndpoints);
            actualRequest.MaxPathSearchRange.Should().Be(expected.CurrentRequest.MaxPathSearchRange);

            if (expected.CurrentRequest is AStarPathRequest expectedAStar
                && actualRequest is AStarPathRequest actualAStar)
            {
                actualAStar.Heuristic.Should().Be(expectedAStar.Heuristic);
                actualAStar.MaxClimbHeight.Should().Be(expectedAStar.MaxClimbHeight);
                actualAStar.AllowTraversalTransitions.Should().Be(expectedAStar.AllowTraversalTransitions);
            }

            if (expected.CurrentRequest is FlowFieldPathRequest expectedFlowField
                && actualRequest is FlowFieldPathRequest actualFlowField)
            {
                actualFlowField.MaxClimbHeight.Should().Be(expectedFlowField.MaxClimbHeight);
                actualFlowField.ExtraFloodRange.Should().Be(expectedFlowField.ExtraFloodRange);
                actualFlowField.AllowTraversalTransitions.Should().Be(expectedFlowField.AllowTraversalTransitions);
            }

            if (expected.CurrentRequest is VolumePathRequest expectedVolume
                && actualRequest is VolumePathRequest actualVolume)
            {
                actualVolume.Heuristic.Should().Be(expectedVolume.Heuristic);
                actualVolume.Medium.Should().Be(expectedVolume.Medium);
            }

            if (expected.CurrentRequest is HybridPathRequest expectedHybrid
                && actualRequest is HybridPathRequest actualHybrid)
            {
                actualHybrid.Heuristic.Should().Be(expectedHybrid.Heuristic);
                actualHybrid.MaxClimbHeight.Should().Be(expectedHybrid.MaxClimbHeight);
            }
        }

        if (expected.TrailGuide == null)
        {
            actual.TrailGuide.Should().BeNull();
        }
        else
        {
            IGuide actualTrailGuide = TestRequire.NotNull(actual.TrailGuide);
            actualTrailGuide.GetType().Should().Be(expected.TrailGuide.GetType());

            if (expected.TrailGuide is AStarGuide expectedAStarGuide
                && actualTrailGuide is AStarGuide actualAStarGuide)
            {
                actualAStarGuide.CurrentWaypointIndex.Should().Be(expectedAStarGuide.CurrentWaypointIndex);
            }

            if (expected.TrailGuide is VolumeGuide expectedVolumeGuide
                && actualTrailGuide is VolumeGuide actualVolumeGuide)
            {
                actualVolumeGuide.CurrentWaypointIndex.Should().Be(expectedVolumeGuide.CurrentWaypointIndex);
            }

            if (expected.TrailGuide is HybridGuide expectedHybridGuide
                && actualTrailGuide is HybridGuide actualHybridGuide)
            {
                actualHybridGuide.CurrentWaypointIndex.Should().Be(expectedHybridGuide.CurrentWaypointIndex);
            }
        }
    }

    private static void AddObstacle(Vector3d position)
    {
        var (grid, voxel) = TestRequire.GridAndVoxelAt(position);
        grid.TryAddObstacle(
            voxel,
            new BoundsKey(position, position)).Should().BeTrue();
    }

    private static void AddObstaclePlaneAtX(int x)
    {
        for (int y = -4; y <= 4; y++)
        {
            for (int z = -4; z <= 4; z++)
                AddObstacle(new Vector3d(x, y, z));
        }
    }

    private static void AddWater(Vector3d position)
    {
        PathTestFactory.RegisterGeneratedVolumePoint(position, TraversalMedium.Liquid, "NavigatorSerializationWater");
    }

    private static void AddOpen(Vector3d position)
    {
        PathTestFactory.RegisterGeneratedVolumePoint(position, TraversalMedium.Gas, "NavigatorSerializationOpen");
    }

    private static void AssertTurningStateMatches(NavTurning expected, NavTurning actual)
    {
        actual.CanTurn.Should().Be(expected.CanTurn);
        actual.TurnRate.Should().Be(expected.TurnRate);
        actual.TargetReached.Should().BeTrue();
        actual.TargetRotation.Should().Be(FixedQuaternion.Identity);
    }
}

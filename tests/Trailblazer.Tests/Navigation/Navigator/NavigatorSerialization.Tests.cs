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
using Trailblazer.Serialization;
using Xunit;
using Trailblazer.Tests;
using Trailblazer.Tests.Navigation.Motor;

namespace Trailblazer.Tests.Navigation;

[Collection("PathingCollection")]
public class NavigatorSerializationTests : IDisposable
{
    public NavigatorSerializationTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();

        var config = new GridConfiguration(new Vector3d(-8, -8, -8), new Vector3d(16, 16, 16));
        GlobalGridManager.TryAddGrid(config, out _);
    }

    public void Dispose()
    {
        PathManager.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void JsonRoundTrip_ShouldRestoreMotorStateIntoExistingMotor()
    {
        var source = CreateConfiguredMotorAgent();

        string json = JsonRecordSerializer.Serialize(source.Motor, writeIndented: true);

        var target = MockMotorAgentTestFactory.CreateMockAgent(startPosition: new Vector3d(-2, 0, -2), startingMedium: TraversalMedium.Ground);
        JsonRecordSerializer.Populate(target.Motor, json);

        AssertMotorStateMatches(source.Motor, target.Motor);
    }

    [Fact]
    public void JsonRoundTrip_ShouldRestoreNavigatorAndMotorState()
    {
        var source = CreateConfiguredNavigator();

        string json = JsonRecordSerializer.Serialize(source, writeIndented: true);

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        JsonRecordSerializer.Populate(target, json);

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
        target.GuidedAllowUnwalkableEndNode.Should().Be(source.GuidedAllowUnwalkableEndNode);
        target.GuidedAllowTraversalTransitions.Should().Be(source.GuidedAllowTraversalTransitions);
        target.GuidedAStarHeuristic.Should().Be(source.GuidedAStarHeuristic);
        target.GuidedAStarMaxClimbHeight.Should().Be(source.GuidedAStarMaxClimbHeight);
        target.GuidedFlowFieldExtraFloodRange.Should().Be(source.GuidedFlowFieldExtraFloodRange);
        target.GlobalId.Should().Be(source.GlobalId);
        target.OccupantGroupId.Should().Be(source.OccupantGroupId);
        target.IsLockedOn.Should().Be(source.IsLockedOn);
        target.AnimDampTime.Should().Be(source.AnimDampTime);
        target.FrameRequest.FacingDirection.Should().Be(source.FrameRequest.FacingDirection);
        target.IsGuideded.Should().BeFalse();

        AssertMotorStateMatches(source.Motor, target.Motor);
        AssertTurningStateMatches(source.Turning, target.Turning);

        TrailblazerManager.Simulate();
        target.ApplyInputTrekRequest(Vector3d.Forward, TrekRate.Slow, isRequestingJump: false);
        target.Simulate();
        target.CommitFrameMotion();

        target.Motor.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public void MemoryPackRoundTrip_ShouldRestoreMotorStateIntoExistingMotor()
    {
        var source = CreateConfiguredMotorAgent();

        byte[] data = MemoryPackRecordSerializer.Serialize(source.Motor);

        var target = MockMotorAgentTestFactory.CreateMockAgent(startPosition: new Vector3d(-2, 0, -2), startingMedium: TraversalMedium.Ground);
        MemoryPackRecordSerializer.Populate(target.Motor, data);

        AssertMotorStateMatches(source.Motor, target.Motor);
    }

    [Fact]
    public void MemoryPackRoundTrip_ShouldRestoreNavigatorAndMotorState()
    {
        var source = CreateConfiguredNavigator();

        byte[] data = MemoryPackRecordSerializer.Serialize(source);

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        MemoryPackRecordSerializer.Populate(target, data);

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
        target.GuidedAllowUnwalkableEndNode.Should().Be(source.GuidedAllowUnwalkableEndNode);
        target.GuidedAllowTraversalTransitions.Should().Be(source.GuidedAllowTraversalTransitions);
        target.GuidedAStarHeuristic.Should().Be(source.GuidedAStarHeuristic);
        target.GuidedAStarMaxClimbHeight.Should().Be(source.GuidedAStarMaxClimbHeight);
        target.GuidedFlowFieldExtraFloodRange.Should().Be(source.GuidedFlowFieldExtraFloodRange);
        target.GlobalId.Should().Be(source.GlobalId);
        target.OccupantGroupId.Should().Be(source.OccupantGroupId);
        target.IsLockedOn.Should().Be(source.IsLockedOn);
        target.AnimDampTime.Should().Be(source.AnimDampTime);
        target.FrameRequest.FacingDirection.Should().Be(source.FrameRequest.FacingDirection);
        target.IsGuideded.Should().BeFalse();

        AssertMotorStateMatches(source.Motor, target.Motor);
        AssertTurningStateMatches(source.Turning, target.Turning);

        TrailblazerManager.Simulate();
        target.ApplyInputTrekRequest(Vector3d.Forward, TrekRate.Slow, isRequestingJump: false);
        target.Simulate();
        target.CommitFrameMotion();

        target.Motor.IsInitialized.Should().BeTrue();
    }

    [Fact]
    public void JsonRoundTrip_ShouldRestoreNavigatorAndSteeringState_ForGuidedAStarTraversal()
    {
        RegisterGuidedPathChart("NavigatorSerializationJsonAStar");

        var source = CreateConfiguredGuidedNavigator(GuidedPathMode.AStar);
        source.Steering.TrailGuide.Should().BeOfType<AStarGuide>();

        string json = JsonRecordSerializer.Serialize(source, writeIndented: true);

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        JsonRecordSerializer.Populate(target, json);

        target.IsGuideded.Should().BeTrue();
        target.FrameRequest.Rate.Should().Be(source.FrameRequest.Rate);
        target.FrameRequest.IsRequestingJump.Should().Be(source.FrameRequest.IsRequestingJump);
        target.FrameRequest.IsRequestingFlight.Should().Be(source.FrameRequest.IsRequestingFlight);
        target.Size.Should().Be(source.Size);

        AssertSteeringStateMatches(source.Steering, target.Steering);

        TrailblazerManager.Simulate();
        target.Simulate();

        target.FrameRequest.Direction.Should().NotBe(Vector3d.Zero);
        target.Steering.ShouldMove.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldRebuildTurningRuntimeState_OnLoad(bool useMemoryPack)
    {
        var source = CreateNavigator(new Vector3d(2, 0, 2));
        source.Turning.TurnRate = (Fixed64)0.35f;
        source.Turning.RequestTurnDirection(source.Forward, Vector3d.Right, interpolation: Fixed64.Half);
        source.Turning.TrySimulateTurn(
            source.Position,
            source.LastPosition,
            source.Forward,
            source.Rotation,
            out _).Should().BeTrue();

        source.Turning.TargetReached.Should().BeFalse();
        source.Turning.TargetRotation.Should().NotBe(FixedQuaternion.Identity);

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        PopulateRecord(target, SerializeRecord(source, useMemoryPack), useMemoryPack);

        target.Turning.CanTurn.Should().Be(source.Turning.CanTurn);
        target.Turning.TurnRate.Should().Be(source.Turning.TurnRate);
        target.Turning.TargetReached.Should().BeTrue();
        target.Turning.TargetRotation.Should().Be(FixedQuaternion.Identity);
    }

    [Fact]
    public void MemoryPackRoundTrip_ShouldRestoreNavigatorAndSteeringState_ForGuidedFlowFieldTraversal()
    {
        RegisterGuidedPathChart("NavigatorSerializationMemoryPackFlowField");

        var source = CreateConfiguredGuidedNavigator(GuidedPathMode.FlowField);
        source.Steering.TrailGuide.Should().BeOfType<FlowFieldGuide>();

        byte[] data = MemoryPackRecordSerializer.Serialize(source);

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        MemoryPackRecordSerializer.Populate(target, data);

        target.IsGuideded.Should().BeTrue();
        target.FrameRequest.Rate.Should().Be(source.FrameRequest.Rate);
        target.FrameRequest.IsRequestingJump.Should().Be(source.FrameRequest.IsRequestingJump);
        target.FrameRequest.IsRequestingFlight.Should().Be(source.FrameRequest.IsRequestingFlight);
        target.Size.Should().Be(source.Size);

        AssertSteeringStateMatches(source.Steering, target.Steering);

        TrailblazerManager.Simulate();
        target.Simulate();

        target.FrameRequest.Direction.Should().NotBe(Vector3d.Zero);
        target.Steering.ShouldMove.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldRestoreNavigatorAndSteeringState_ForGuidedAerialTraversal(bool useMemoryPack)
    {
        var source = CreateConfiguredGuidedNavigator(GuidedPathMode.Aerial);
        source.Steering.CurrentRequest.Should().BeOfType<VolumePathRequest>();
        source.Steering.TrailGuide.Should().BeOfType<VolumeGuide>();

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        PopulateRecord(target, SerializeRecord(source, useMemoryPack), useMemoryPack);

        target.IsGuideded.Should().BeTrue();
        target.FrameRequest.Rate.Should().Be(source.FrameRequest.Rate);
        target.FrameRequest.IsRequestingJump.Should().Be(source.FrameRequest.IsRequestingJump);
        target.FrameRequest.IsRequestingFlight.Should().Be(source.FrameRequest.IsRequestingFlight);
        target.Size.Should().Be(source.Size);

        AssertSteeringStateMatches(source.Steering, target.Steering);

        TrailblazerManager.Simulate();
        target.Simulate();

        target.FrameRequest.Direction.Should().NotBe(Vector3d.Zero);
        target.FrameRequest.Direction.y.Should().BeGreaterThan(Fixed64.Zero);
        target.FrameRequest.IsRequestingFlight.Should().BeTrue();
        target.Steering.ShouldMove.Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldRestoreNavigatorAndSteeringState_ForAStarTransitionFallback(bool useMemoryPack)
    {
        RegisterTransitionFallbackAStarScene();

        var source = CreateConfiguredTransitionFallbackAStarNavigator();
        source.Steering.CurrentRequest.Should().BeOfType<AStarPathRequest>();
        source.Steering.TrailGuide.Should().BeOfType<AStarGuide>();

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        PopulateRecord(target, SerializeRecord(source, useMemoryPack), useMemoryPack);

        target.Steering.CurrentRequest.Should().BeOfType<AStarPathRequest>();
        target.Steering.TrailGuide.Should().BeOfType<AStarGuide>();
        AssertSteeringStateMatches(source.Steering, target.Steering);
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
        source.GuidedPathMode = GuidedPathMode.Aerial;
        source.GuidedAStarHeuristic = HeuristicMethod.Euclidean;
        source.ApplyGuidedTrekRequest(
            new Vector3d(4, 0, 0),
            pathMode: GuidedPathMode.Aerial,
            rate: TrekRate.Fast,
            isRequestingJump: false,
            groupId: 3);

        TrailblazerManager.Simulate();
        source.Steering.GetHeading(source);

        VolumeGuide sourceGuide = source.Steering.TrailGuide.Should().BeOfType<VolumeGuide>().Subject;
        if (sourceGuide.TryGetWaypointAt(sourceGuide.CurrentWaypointIndex + 1, out _))
            sourceGuide.AdvanceWaypoint();

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        PopulateRecord(target, SerializeRecord(source, useMemoryPack), useMemoryPack);

        target.Steering.TrailGuide.Should().BeOfType<VolumeGuide>();
        ((VolumeGuide)target.Steering.TrailGuide).CurrentWaypointIndex.Should().Be(sourceGuide.CurrentWaypointIndex);

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
        source.GuidedPathMode = GuidedPathMode.Swim;
        source.GuidedAStarHeuristic = HeuristicMethod.Euclidean;
        source.ApplyGuidedTrekRequest(
            new Vector3d(0, 0, 2),
            pathMode: GuidedPathMode.Swim,
            rate: TrekRate.Fast,
            isRequestingJump: false,
            groupId: 3);

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        PopulateRecord(target, SerializeRecord(source, useMemoryPack), useMemoryPack);

        target.IsGuideded.Should().BeTrue();
        target.FrameRequest.IsRequestingFlight.Should().BeFalse();
        target.Steering.CurrentRequest.Should().BeOfType<VolumePathRequest>();
        ((VolumePathRequest)target.Steering.CurrentRequest).TraversalMode.Should().Be(VolumeTraversalMode.Water);

        TrailblazerManager.Simulate();
        target.Simulate();

        target.FrameRequest.Direction.Should().NotBe(Vector3d.Zero);
        target.FrameRequest.Direction.z.Should().BeGreaterThan(Fixed64.Zero);
        target.FrameRequest.IsRequestingFlight.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldRestorePendingSwimExitHandoff_AndActivateChartFollowup(bool useMemoryPack)
    {
        RegisterVolumeExitHandoffScene("NavigatorSerializationSwimExitHandoff");

        var source = CreateNavigator(Vector3d.Zero, size: Fixed64.One);
        source.SetWaterContact(surfaceLevel: Fixed64.Zero, updateMotorState: true);
        source.GuidedPathMode = GuidedPathMode.FlowField;
        source.GuidedAllowTraversalTransitions = true;
        source.GuidedFlowFieldExtraFloodRange = 12;
        source.ApplyGuidedTrekRequest(
            new Vector3d(4, 0, 0),
            pathMode: GuidedPathMode.Swim,
            rate: TrekRate.Fast,
            isRequestingJump: false,
            groupId: 5);

        VolumePathRequest sourceRequest = source.Steering.CurrentRequest.Should().BeOfType<VolumePathRequest>().Subject;
        sourceRequest.TargetPosition.Should().Be(new Vector3d(2, 0, 0));

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        PopulateRecord(target, SerializeRecord(source, useMemoryPack), useMemoryPack);

        target.Steering.CurrentRequest.Should().BeOfType<VolumePathRequest>()
            .Which.TargetPosition.Should().Be(new Vector3d(2, 0, 0));

        target.SetTestPosition(new Vector3d(2, 0, 0));
        target.SetGroundContact(surfaceLevel: Fixed64.Zero, updateMotorState: true);
        target.Steering.Arrive();

        TrailblazerManager.Simulate();
        target.Simulate();

        FlowFieldPathRequest followupRequest = target.Steering.CurrentRequest.Should().BeOfType<FlowFieldPathRequest>().Subject;
        followupRequest.TargetPosition.Should().Be(new Vector3d(4, 0, 0));
        target.Steering.MovementGroupID.Should().Be(5);
        target.FrameRequest.IsRequestingFlight.Should().BeFalse();
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
        source.GuidedPathMode = GuidedPathMode.AStar;
        source.GuidedAllowTraversalTransitions = true;
        source.GuidedAStarHeuristic = HeuristicMethod.Euclidean;
        source.ApplyGuidedTrekRequest(
            new Vector3d(4, 0, 0),
            pathMode: GuidedPathMode.Aerial,
            rate: TrekRate.Fast,
            isRequestingJump: false,
            groupId: 6);

        VolumePathRequest sourceRequest = source.Steering.CurrentRequest.Should().BeOfType<VolumePathRequest>().Subject;
        sourceRequest.TargetPosition.Should().Be(new Vector3d(1, 0, 0));

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        PopulateRecord(target, SerializeRecord(source, useMemoryPack), useMemoryPack);

        target.Steering.CurrentRequest.Should().BeOfType<VolumePathRequest>()
            .Which.TargetPosition.Should().Be(new Vector3d(1, 0, 0));
        target.FrameRequest.IsRequestingFlight.Should().BeTrue();

        target.SetTestPosition(new Vector3d(1, 0, 0));
        target.SetGroundContact(surfaceLevel: Fixed64.Zero, updateMotorState: true);
        target.Steering.Arrive();

        TrailblazerManager.Simulate();
        target.Simulate();

        AStarPathRequest followupRequest = target.Steering.CurrentRequest.Should().BeOfType<AStarPathRequest>().Subject;
        followupRequest.TargetPosition.x.Should().Be((Fixed64)4);
        followupRequest.TargetPosition.y.Should().Be(Fixed64.Zero);
        followupRequest.TargetPosition.z.Should().Be(Fixed64.Zero);
        followupRequest.AllowTraversalTransitions.Should().BeTrue();
        target.Steering.MovementGroupID.Should().Be(6);
        target.Steering.Destination.x.Should().Be((Fixed64)4);
        target.FrameRequest.IsRequestingFlight.Should().BeFalse();

        UnloadAerialLandingHandoffScene(sceneKey);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldSupportPartialNavigatorPayloads_AndPreserveOmittedBranches(bool useMemoryPack)
    {
        var source = CreateConfiguredNavigator();
        source.OccupantGroupId = 9;
        object payload = SerializeRecord(source, useMemoryPack);

        payload = RemovePayloadEntry(payload, useMemoryPack, "occupantGroupId");
        payload = RemovePayloadEntry(payload, useMemoryPack, "steering");
        payload = RemovePayloadEntry(payload, useMemoryPack, "turning");
        payload = RemovePayloadEntry(payload, useMemoryPack, "motor");

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        target.OccupantGroupId = 9;
        target.Steering.StopMultiplier = (Fixed64)0.33f;
        target.Turning.TurnRate = (Fixed64)0.72f;
        target.Motor.Handler.Move.MaxFastSpeed = (Fixed64)8;

        PopulateRecord(target, payload, useMemoryPack);

        target.Position.Should().Be(source.Position);
        // since we removed the occupantGroupId entry, it should fall back to the default value of 1
        // regardless of the source and target values before population
        target.OccupantGroupId.Should().Be(1);
        target.Steering.StopMultiplier.Should().Be((Fixed64)0.33f);
        target.Turning.TurnRate.Should().Be((Fixed64)0.72f);
        target.Motor.Handler.Move.MaxFastSpeed.Should().Be((Fixed64)8);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldUseBackwardCompatibleDefaults_WhenPayloadOmitsNewerFields(bool useMemoryPack)
    {
        RegisterGuidedPathChart("NavigatorSerializationLegacyDefaults");

        var source = CreateConfiguredGuidedNavigator(GuidedPathMode.FlowField);
        object payload = SerializeRecord(source, useMemoryPack);

        payload = RemovePayloadEntry(payload, useMemoryPack, "guidedAllowUnwalkableEndNode");
        payload = RemovePayloadEntry(payload, useMemoryPack, "guidedAllowTraversalTransitions");
        payload = RemovePayloadEntry(payload, useMemoryPack, "guidedFlowFieldExtraFloodRange");
        payload = RemovePayloadEntry(payload, useMemoryPack, "steering", "pathRecheckCooldownFrames");
        payload = RemovePayloadEntry(payload, useMemoryPack, "steering", "stopMultiplier");
        payload = RemovePayloadEntry(payload, useMemoryPack, "steering", "brakingPower");
        payload = RemovePayloadEntry(payload, useMemoryPack, "steering", "pathRequest", "flowFieldExtraFloodRange");

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        bool expectedAllowUnwalkable = target.GuidedAllowUnwalkableEndNode;
        bool expectedAllowTraversalTransitions = target.GuidedAllowTraversalTransitions;
        int expectedRootExtraFloodRange = target.GuidedFlowFieldExtraFloodRange;
        int expectedPathRecheckCooldown = target.Steering.PathRecheckCooldownFrames;
        Fixed64 expectedStopMultiplier = target.Steering.StopMultiplier;
        Fixed64 expectedBrakingPower = target.Steering.BrakingPower;

        PopulateRecord(target, payload, useMemoryPack);

        target.IsGuideded.Should().BeTrue();
        target.GuidedAllowUnwalkableEndNode.Should().Be(expectedAllowUnwalkable);
        target.GuidedAllowTraversalTransitions.Should().Be(expectedAllowTraversalTransitions);
        target.GuidedFlowFieldExtraFloodRange.Should().Be(expectedRootExtraFloodRange);
        target.Steering.PathRecheckCooldownFrames.Should().Be(expectedPathRecheckCooldown);
        target.Steering.StopMultiplier.Should().Be(expectedStopMultiplier);
        target.Steering.BrakingPower.Should().Be(expectedBrakingPower);
        target.Steering.BehaviorWeights.Separation.Should().Be(source.Steering.BehaviorWeights.Separation);

        var request = target.Steering.CurrentRequest.Should().BeOfType<FlowFieldPathRequest>().Subject;
        request.ExtraFloodRange.Should().Be(FlowFieldPathRequest.DefaultExtraFloodRange);

        PathManager.UnloadChart("NavigatorSerializationLegacyDefaults");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldUseBackwardCompatibleDefaults_WhenPayloadOmitsFacingDirection(bool useMemoryPack)
    {
        var source = CreateConfiguredNavigator();
        object payload = SerializeRecord(source, useMemoryPack);

        payload = RemovePayloadEntry(payload, useMemoryPack, "frameRequest", "FacingDirection");

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        PopulateRecord(target, payload, useMemoryPack);

        target.FrameRequest.FacingDirection.Should().BeNull();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RoundTrip_ShouldClearSteeringSession_WhenRequestRehydrationFails(bool useMemoryPack)
    {
        RegisterGuidedPathChart("NavigatorSerializationInvalidRequest");

        var source = CreateConfiguredGuidedNavigator(GuidedPathMode.AStar);
        source.GuidedAllowUnwalkableEndNode = false;
        source.Steering.CurrentRequest.Should().BeOfType<AStarPathRequest>().Subject.AllowUnwalkableEndNode = false;
        object payload = SerializeRecord(source, useMemoryPack);
        payload = SetPayloadValue(
            payload,
            useMemoryPack,
            new Vector3d(512, 0, 512),
            "steering",
            "pathRequest",
            "targetPosition");

        var target = CreateNavigator(new Vector3d(-4, 0, -4));
        PopulateRecord(target, payload, useMemoryPack);

        target.Steering.CurrentRequest.Should().BeNull();
        target.Steering.TrailGuide.Should().BeNull();
        target.Steering.ShouldMove.Should().BeFalse();
        target.Steering.IsStuck.Should().BeFalse();
        target.Steering.HasLineOfSightPath.Should().BeFalse();
        target.Steering.Destination.Should().Be(Vector3d.Zero);
        target.Steering.TargetDirection.Should().Be(Vector3d.Zero);

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
        object payload = SerializeRecord(source.Motor, useMemoryPack);

        payload = SetPayloadValue(payload, useMemoryPack, false, "handler", "move", "isEnabled");
        payload = SetPayloadValue(payload, useMemoryPack, false, "handler", "platform", "isEnabled");
        payload = SetPayloadValue(payload, useMemoryPack, false, "handler", "jump", "isEnabled");
        payload = SetPayloadValue(payload, useMemoryPack, false, "handler", "fall", "isEnabled");
        payload = SetPayloadValue(payload, useMemoryPack, false, "handler", "slide", "isEnabled");
        payload = SetPayloadValue(payload, useMemoryPack, false, "handler", "swim", "isEnabled");

        var target = MockMotorAgentTestFactory.CreatePlatformAgent(
            startPosition: new Vector3d(-2, 0, -2),
            platformMatrix: MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(-1, 0, -1)),
            motionTransfer: MotionTransfer.PermaLocked);
        PopulateRecord(target.Motor, payload, useMemoryPack);

        target.Motor.Handler.Move.IsEnabled.Should().BeFalse();
        target.Motor.Handler.Move.FrameVelocity.Should().Be(Vector3d.Zero);

        target.Motor.Handler.Platform.IsEnabled.Should().BeFalse();
        target.Motor.Handler.Platform.IsNewPlatform.Should().BeFalse();
        target.Motor.Handler.Platform.ActivePlatform.Should().BeNull();
        target.Motor.Handler.Platform.PreviousPlatform.Should().BeNull();
        target.Motor.Handler.Platform.HoldPlatform.Should().BeNull();
        target.Motor.Handler.Platform.MovementTransfer.Should().Be(MotionTransfer.None);
        target.Motor.Handler.Platform.ScoutLocalPoint.Should().Be(Vector3d.Zero);
        target.Motor.Handler.Platform.ScoutLocalRotation.Should().Be(FixedQuaternion.Identity);
        target.Motor.Handler.Platform.PlatformVelocity.Should().Be(Vector3d.Zero);
        target.Motor.Handler.Platform.FramePlatformVelocity.Should().Be(Vector3d.Zero);
        target.Motor.Handler.Platform.HoldPlatformFrames.Should().Be(0);

        target.Motor.Handler.Jump.IsEnabled.Should().BeFalse();
        target.Motor.Handler.Jump.IsJumping.Should().BeFalse();
        target.Motor.Handler.Jump.IsHoldingJump.Should().BeFalse();
        target.Motor.Handler.Jump.JumpStartTime.Should().Be(Fixed64.Zero);
        target.Motor.Handler.Jump.FrameJumpDirection.Should().Be(Vector3d.Zero);

        target.Motor.Handler.Fall.IsEnabled.Should().BeFalse();
        target.Motor.Handler.Fall.IsFalling.Should().BeFalse();
        target.Motor.Handler.Fall.FallStart.Should().Be(Fixed64.Zero);
        target.Motor.Handler.Fall.FallEnd.Should().Be(Fixed64.Zero);

        target.Motor.Handler.Slide.IsEnabled.Should().BeFalse();
        target.Motor.Handler.Slide.IsSliding.Should().BeFalse();

        target.Motor.Handler.Swim.IsEnabled.Should().BeFalse();
        target.Motor.Handler.Swim.IsSwimming.Should().BeFalse();
        target.Motor.Handler.Swim.IsDiving.Should().BeFalse();
        target.Motor.Handler.Swim.UnderwaterTimer.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void JsonRoundTrip_ShouldAllowMovementGroupsToBePrewarmed_AfterLoad()
    {
        RegisterMovementGroupFormationChart("NavigatorSerializationMovementGroupPrewarm");

        Vector3d sharedDestination = new(4, 0, 0);

        var sourceFirst = CreateNavigator(new Vector3d(1, 0, 0), size: Fixed64.One);
        var sourceSecond = CreateNavigator(new Vector3d(2, 0, 0), size: Fixed64.One);

        sourceFirst.ApplyGuidedTrekRequest(sharedDestination, pathMode: GuidedPathMode.AStar, groupId: 5);
        sourceSecond.ApplyGuidedTrekRequest(sharedDestination, pathMode: GuidedPathMode.AStar, groupId: 5);

        string firstJson = JsonRecordSerializer.Serialize(sourceFirst, writeIndented: true);
        string secondJson = JsonRecordSerializer.Serialize(sourceSecond, writeIndented: true);

        TrailblazerManager.Reset();

        var lazyFirst = CreateNavigator(new Vector3d(-3, 0, 0), size: Fixed64.One);
        var lazySecond = CreateNavigator(new Vector3d(-2, 0, 0), size: Fixed64.One);
        JsonRecordSerializer.Populate(lazyFirst, firstJson);
        JsonRecordSerializer.Populate(lazySecond, secondJson);

        lazyFirst.Steering.GetHeading(lazyFirst);
        lazySecond.Steering.GetHeading(lazySecond);

        lazyFirst.Steering.Destination.Should().Be(sharedDestination);
        lazySecond.Steering.Destination.Should().Be(new Vector3d((Fixed64)4.5f, Fixed64.Zero, Fixed64.Zero));

        TrailblazerManager.Reset();

        var prewarmedFirst = CreateNavigator(new Vector3d(-3, 0, 0), size: Fixed64.One);
        var prewarmedSecond = CreateNavigator(new Vector3d(-2, 0, 0), size: Fixed64.One);
        JsonRecordSerializer.Populate(prewarmedFirst, firstJson);
        JsonRecordSerializer.Populate(prewarmedSecond, secondJson);

        prewarmedFirst.PrewarmMovementGroup();
        prewarmedSecond.PrewarmMovementGroup();

        prewarmedFirst.Steering.GetHeading(prewarmedFirst);
        prewarmedSecond.Steering.GetHeading(prewarmedSecond);

        prewarmedFirst.Steering.Destination.Should().Be(new Vector3d((Fixed64)3.5f, Fixed64.Zero, Fixed64.Zero));
        prewarmedSecond.Steering.Destination.Should().Be(new Vector3d((Fixed64)4.5f, Fixed64.Zero, Fixed64.Zero));

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
            Medium = TraversalMedium.Ground,
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

        source.Motor.Handler.IsInControl = false;
        source.Motor.Handler.Move.MaxFastSpeed = (Fixed64)1.75f;
        source.Motor.Handler.Move.FrameVelocity = new Vector3d(1, 2, 3);

        source.Motor.Handler.Jump.MaxJumpCount = 2;
        source.Motor.Handler.Jump.RegisterJump();
        source.Motor.Handler.Jump.FrameJumpDirection = new Vector3d(0, 1, 1).Normal;
        source.Motor.Handler.Jump.StartCooldown();

        source.Motor.Handler.Fall.IsFalling = true;
        source.Motor.Handler.Fall.FallStart = (Fixed64)9;
        source.Motor.Handler.Fall.FallEnd = (Fixed64)3;

        source.Motor.Handler.Slide.IsSliding = true;

        source.Motor.Handler.Swim.IsSwimming = true;
        source.Motor.Handler.Swim.IsDiving = true;
        source.Motor.Handler.Swim.UnderwaterTimer = (Fixed64)7;

        source.Motor.Handler.Fly.MaxFlySpeed = (Fixed64)2.5f;
        source.Motor.Handler.Fly.GravityCompensation = (Fixed64)0.75f;
        source.Motor.Handler.Fly.IsFlying = true;

        var holdPlatform = new PlatformSnapshot(9, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(6, 0, 6)));
        source.Motor.Handler.Platform.IsNewPlatform = true;
        source.Motor.Handler.Platform.PreviousPlatform = new PlatformSnapshot(8, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(3, 0, 3)));
        source.Motor.Handler.Platform.SetHoldPlatform(holdPlatform);
        source.Motor.Handler.Platform.TickHoldOnPlatform();
        source.Motor.Handler.Platform.ScoutLocalPoint = new Vector3d(1, 0, 1);
        source.Motor.Handler.Platform.ScoutLocalRotation = FixedQuaternion.FromAxisAngle(Vector3d.Up, (Fixed64)0.25f);
        source.Motor.Handler.Platform.PlatformVelocity = new Vector3d(5, 0, 0);
        source.Motor.Handler.Platform.FramePlatformVelocity = new Vector3d(2, 0, 0);

        return source;
    }

    private static TestNavigator CreateConfiguredNavigator()
    {
        var source = CreateNavigator(new Vector3d(2, 0, 2));
        source.ApplyInputTrekRequest(
            Vector3d.Right,
            TrekRate.Moderate,
            isRequestingJump: true,
            isRequestingFlight: true,
            facingDirection: Vector3d.Forward);
        source.SetGroundContact(
            surfaceLevel: Fixed64.Zero,
            platform: new PlatformSnapshot(12, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(1, 0, 1))),
            surfaceFriction: (Fixed64)0.15f,
            motionTransfer: MotionTransfer.PermaLocked,
            updateMotorState: true);

        source.GuidedPathMode = GuidedPathMode.FlowField;
        source.GuidedAllowUnwalkableEndNode = true;
        source.GuidedAllowTraversalTransitions = true;
        source.GuidedAStarHeuristic = HeuristicMethod.Euclidean;
        source.GuidedAStarMaxClimbHeight = (Fixed64)4;
        source.GuidedFlowFieldExtraFloodRange = 32;
        source.FootPositionAdjust = (Fixed64)0.75f;
        source.IsLockedOn = true;
        source.AnimDampTime = (Fixed64)0.25f;
        source.Motor.Handler.Move.FrameVelocity = new Vector3d(1, 0, 2);
        source.Motor.Handler.Platform.ActivePlatform = new PlatformSnapshot(12, MockMotorAgentTestFactory.CreatePlatformTransform(new Vector3d(1, 0, 1)));
        source.Motor.Handler.Platform.MovementTransfer = MotionTransfer.PermaLocked;
        source.Motor.Handler.Platform.ScoutLocalPoint = new Vector3d(0, 0, 1);
        source.Motor.Handler.Platform.ScoutLocalRotation = FixedQuaternion.FromAxisAngle(Vector3d.Up, (Fixed64)0.5f);
        source.Motor.Handler.Jump.RegisterJump();
        source.Motor.Handler.Jump.FrameJumpDirection = Vector3d.Up;
        source.Motor.Handler.Fall.IsFalling = true;
        source.Motor.Handler.Fall.FallStart = (Fixed64)10;
        source.Motor.Handler.Fly.GravityCompensation = (Fixed64)0.8f;
        source.Motor.Handler.Fly.IsFlying = true;
        source.Turning.CanTurn = false;
        source.Turning.TurnRate = (Fixed64)0.35f;

        return source;
    }

    private static TestNavigator CreateConfiguredGuidedNavigator(GuidedPathMode pathMode)
    {
        var source = CreateNavigator(Vector3d.Zero, size: Fixed64.One);
        source.GuidedPathMode = pathMode;
        source.GuidedAllowUnwalkableEndNode = true;
        source.GuidedAllowTraversalTransitions = true;
        source.GuidedAStarHeuristic = HeuristicMethod.Euclidean;
        source.GuidedAStarMaxClimbHeight = (Fixed64)2;
        source.GuidedFlowFieldExtraFloodRange = 24;

        source.Steering.PathRecheckCooldownFrames = 9;
        source.Steering.StopMultiplier = (Fixed64)0.75f;
        source.Steering.GroupFactor = (Fixed64)12;
        source.Steering.AvoidFactor = (Fixed64)4;
        source.Steering.BehaviorWeights = new GroupBehaviorWeights()
        {
            Separation = (Fixed64)3,
            Alignment = (Fixed64)0.75f,
            Cohesion = (Fixed64)0.4f,
            Avoidance = (Fixed64)1.25f
        };
        source.Steering.BrakingPower = (Fixed64)0.2f;

        Vector3d targetPosition = pathMode == GuidedPathMode.Aerial
            ? new Vector3d(4, 4, 0)
            : new Vector3d(4, 0, 0);

        if (pathMode == GuidedPathMode.Aerial)
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
            pathMode: pathMode,
            rate: TrekRate.Fast,
            isRequestingJump: true,
            groupId: 7);

        TrailblazerManager.Simulate();
        source.Steering.GetHeading(source);
        source.Steering.PauseAutoStop();

        if (source.Steering.TrailGuide is AStarGuide aStarGuide)
        {
            aStarGuide.AdvanceWaypoint();
            TrailblazerManager.Simulate();
            source.Steering.GetHeading(source);
        }

        return source;
    }

    private static TestNavigator CreateConfiguredTransitionFallbackAStarNavigator()
    {
        var source = CreateNavigator(Vector3d.Zero, size: Fixed64.One);
        source.ApplyInputTrekRequest(Vector3d.Right, TrekRate.Fast, isRequestingJump: true);

        source.Steering.PathRecheckCooldownFrames = 11;
        source.Steering.StopMultiplier = (Fixed64)0.8f;
        source.Steering.GroupFactor = (Fixed64)10;
        source.Steering.AvoidFactor = (Fixed64)3;
        source.Steering.BehaviorWeights = new GroupBehaviorWeights()
        {
            Separation = (Fixed64)2.5f,
            Alignment = (Fixed64)0.5f,
            Cohesion = (Fixed64)0.35f,
            Avoidance = (Fixed64)1.4f
        };
        source.Steering.BrakingPower = (Fixed64)0.3f;

        AStarPathRequest request = AStarPathRequest.Create(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            Fixed64.One,
            HeuristicMethod.Euclidean,
            allowUnwalkableEndNode: true);

        request.Should().NotBeNull();
        request.MaxClimbHeight = (Fixed64)2;
        request.AllowTraversalTransitions = true;

        source.Steering.ApplyPathRequest(request, groupId: 5);

        TrailblazerManager.Simulate();
        source.Steering.GetHeading(source);
        source.Steering.PauseAutoStop();

        AStarGuide guide = source.Steering.TrailGuide.Should().BeOfType<AStarGuide>().Subject;
        if (guide.TryGetWaypointAt(guide.CurrentWaypointIndex + 1, out _))
        {
            guide.AdvanceWaypoint();
            TrailblazerManager.Simulate();
            source.Steering.GetHeading(source);
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
            source: TraversalTransitionAnchor.Chart(Vector3d.Zero),
            destination: TraversalTransitionAnchor.WaterVolume(new Vector3d(1, 0, 0)),
            pathCostModifier: 2)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: "transition-fallback-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.WaterVolume(new Vector3d(3, 0, 0)),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(4, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();
    }

    private static void RegisterVolumeExitHandoffScene(string chartKey)
    {
        bool[,,] data = new bool[1, 3, 1]
        {
            {
                { true },
                { true },
                { true }
            }
        };

        PathTestFactory.RegisterFromData(chartKey, data, new Vector3d(2, 0, 0));

        AddWater(Vector3d.Zero);
        AddWater(new Vector3d(1, 0, 0));
        AddWater(new Vector3d(2, 0, 0));

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: $"{chartKey}-exit",
            type: TraversalTransitionType.SwimExit,
            source: TraversalTransitionAnchor.WaterVolume(new Vector3d(2, 0, 0)),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(2, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();
    }

    private static void RegisterAerialLandingHandoffScene(string sceneKey)
    {
        PathTestFactory.RegisterSingleWalkablePoint($"{sceneKey}-Landing", new Vector3d(1, 0, 0));
        PathTestFactory.RegisterSingleWalkablePoint($"{sceneKey}-Target", new Vector3d(4, 0, 0));
        AddOpen(Vector3d.Zero);
        AddOpen(new Vector3d(1, 0, 0));

        AddObstaclePlaneAtX(2);

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: $"{sceneKey}-landing",
            type: TraversalTransitionType.Landing,
            source: TraversalTransitionAnchor.OpenVolume(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(1, 0, 0)),
            pathCostModifier: 1)).Should().BeTrue();

        TraversalTransitionRegistry.Register(new TraversalTransition(
            id: $"{sceneKey}-chart-hop",
            type: TraversalTransitionType.Jump,
            source: TraversalTransitionAnchor.Chart(new Vector3d(1, 0, 0)),
            destination: TraversalTransitionAnchor.Chart(new Vector3d(4, 0, 0)),
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

    private static object SerializeRecord(IRecordable record, bool useMemoryPack)
    {
        return useMemoryPack
            ? MemoryPackRecordSerializer.Serialize(record)
            : JsonRecordSerializer.Serialize(record, writeIndented: true);
    }

    private static void PopulateRecord(IRecordable target, object payload, bool useMemoryPack)
    {
        if (useMemoryPack)
        {
            MemoryPackRecordSerializer.Populate(target, (byte[])payload);
            return;
        }

        JsonRecordSerializer.Populate(target, (string)payload);
    }

    private static object RemovePayloadEntry(object payload, bool useMemoryPack, params string[] path)
    {
        return useMemoryPack
            ? SerializationPayloadEditor.RemoveMemoryPackEntry((byte[])payload, path)
            : SerializationPayloadEditor.RemoveJsonProperty((string)payload, path);
    }

    private static object SetPayloadValue<T>(object payload, bool useMemoryPack, T value, params string[] path)
    {
        return useMemoryPack
            ? SerializationPayloadEditor.SetMemoryPackValue((byte[])payload, value, path)
            : SerializationPayloadEditor.SetJsonValue((string)payload, value, path);
    }

    private static void AssertMotorStateMatches(NavMotor expected, NavMotor actual)
    {
        actual.IsInitialized.Should().Be(expected.IsInitialized);
        actual.CurrentState.ToTrekCondition().Medium.Should().Be(expected.CurrentState.ToTrekCondition().Medium);
        actual.CurrentState.ToTrekCondition().SurfaceLevel.Should().Be(expected.CurrentState.ToTrekCondition().SurfaceLevel);
        actual.CurrentState.ToTrekCondition().CeilingLevel.Should().Be(expected.CurrentState.ToTrekCondition().CeilingLevel);
        actual.CurrentState.PreviousState.Should().Be(expected.CurrentState.PreviousState);

        actual.Handler.IsInControl.Should().Be(expected.Handler.IsInControl);

        actual.Handler.Move.IsEnabled.Should().Be(expected.Handler.Move.IsEnabled);
        actual.Handler.Move.FrameVelocity.Should().Be(expected.Handler.Move.FrameVelocity);
        actual.Handler.Move.MaxFastSpeed.Should().Be(expected.Handler.Move.MaxFastSpeed);

        actual.Handler.Jump.IsJumping.Should().Be(expected.Handler.Jump.IsJumping);
        actual.Handler.Jump.IsHoldingJump.Should().Be(expected.Handler.Jump.IsHoldingJump);
        actual.Handler.Jump.JumpStartTime.Should().Be(expected.Handler.Jump.JumpStartTime);
        actual.Handler.Jump.FrameJumpDirection.Should().Be(expected.Handler.Jump.FrameJumpDirection);
        actual.Handler.Jump.CanJump.Should().Be(expected.Handler.Jump.CanJump);

        actual.Handler.Fall.IsFalling.Should().Be(expected.Handler.Fall.IsFalling);
        actual.Handler.Fall.FallStart.Should().Be(expected.Handler.Fall.FallStart);
        actual.Handler.Fall.FallEnd.Should().Be(expected.Handler.Fall.FallEnd);

        actual.Handler.Slide.IsSliding.Should().Be(expected.Handler.Slide.IsSliding);

        actual.Handler.Swim.IsSwimming.Should().Be(expected.Handler.Swim.IsSwimming);
        actual.Handler.Swim.IsDiving.Should().Be(expected.Handler.Swim.IsDiving);
        actual.Handler.Swim.UnderwaterTimer.Should().Be(expected.Handler.Swim.UnderwaterTimer);

        actual.Handler.Fly.IsEnabled.Should().Be(expected.Handler.Fly.IsEnabled);
        actual.Handler.Fly.MaxFlySpeed.Should().Be(expected.Handler.Fly.MaxFlySpeed);
        actual.Handler.Fly.GravityCompensation.Should().Be(expected.Handler.Fly.GravityCompensation);
        actual.Handler.Fly.IsFlying.Should().Be(expected.Handler.Fly.IsFlying);

        actual.Handler.Platform.IsNewPlatform.Should().Be(expected.Handler.Platform.IsNewPlatform);
        actual.Handler.Platform.MovementTransfer.Should().Be(expected.Handler.Platform.MovementTransfer);
        actual.Handler.Platform.ScoutLocalPoint.Should().Be(expected.Handler.Platform.ScoutLocalPoint);
        actual.Handler.Platform.ScoutLocalRotation.Should().Be(expected.Handler.Platform.ScoutLocalRotation);
        actual.Handler.Platform.PlatformVelocity.Should().Be(expected.Handler.Platform.PlatformVelocity);
        actual.Handler.Platform.FramePlatformVelocity.Should().Be(expected.Handler.Platform.FramePlatformVelocity);
        actual.Handler.Platform.HoldPlatformFrames.Should().Be(expected.Handler.Platform.HoldPlatformFrames);

        actual.Handler.Platform.ActivePlatform.Should().NotBeNull();
        expected.Handler.Platform.ActivePlatform.Should().NotBeNull();
        actual.Handler.Platform.ActivePlatform?.Id.Should().Be(expected.Handler.Platform.ActivePlatform?.Id);
        actual.Handler.Platform.ActivePlatform?.Transform.Should().Be(expected.Handler.Platform.ActivePlatform?.Transform);

        actual.Handler.Platform.PreviousPlatform?.Id.Should().Be(expected.Handler.Platform.PreviousPlatform?.Id);
        actual.Handler.Platform.PreviousPlatform?.Transform.Should().Be(expected.Handler.Platform.PreviousPlatform?.Transform);
        actual.Handler.Platform.HoldPlatform?.Id.Should().Be(expected.Handler.Platform.HoldPlatform?.Id);
        actual.Handler.Platform.HoldPlatform?.Transform.Should().Be(expected.Handler.Platform.HoldPlatform?.Transform);
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
            actual.CurrentRequest.Should().NotBeNull();
            actual.CurrentRequest.GetType().Should().Be(expected.CurrentRequest.GetType());
            actual.CurrentRequest.Origin.Should().Be(expected.CurrentRequest.Origin);
            actual.CurrentRequest.TargetPosition.Should().Be(expected.CurrentRequest.TargetPosition);
            actual.CurrentRequest.UnitSize.Should().Be(expected.CurrentRequest.UnitSize);
            actual.CurrentRequest.AllowUnwalkableEndNode.Should().Be(expected.CurrentRequest.AllowUnwalkableEndNode);
            actual.CurrentRequest.MaxPathSearchRange.Should().Be(expected.CurrentRequest.MaxPathSearchRange);

            if (expected.CurrentRequest is AStarPathRequest expectedAStar
                && actual.CurrentRequest is AStarPathRequest actualAStar)
            {
                actualAStar.Heuristic.Should().Be(expectedAStar.Heuristic);
                actualAStar.MaxClimbHeight.Should().Be(expectedAStar.MaxClimbHeight);
                actualAStar.AllowTraversalTransitions.Should().Be(expectedAStar.AllowTraversalTransitions);
            }

            if (expected.CurrentRequest is FlowFieldPathRequest expectedFlowField
                && actual.CurrentRequest is FlowFieldPathRequest actualFlowField)
            {
                actualFlowField.ExtraFloodRange.Should().Be(expectedFlowField.ExtraFloodRange);
                actualFlowField.AllowTraversalTransitions.Should().Be(expectedFlowField.AllowTraversalTransitions);
            }

            if (expected.CurrentRequest is VolumePathRequest expectedVolume
                && actual.CurrentRequest is VolumePathRequest actualVolume)
            {
                actualVolume.Heuristic.Should().Be(expectedVolume.Heuristic);
                actualVolume.TraversalMode.Should().Be(expectedVolume.TraversalMode);
            }

            if (expected.CurrentRequest is HybridPathRequest expectedHybrid
                && actual.CurrentRequest is HybridPathRequest actualHybrid)
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
            actual.TrailGuide.Should().NotBeNull();
            actual.TrailGuide.GetType().Should().Be(expected.TrailGuide.GetType());

            if (expected.TrailGuide is AStarGuide expectedAStarGuide
                && actual.TrailGuide is AStarGuide actualAStarGuide)
            {
                actualAStarGuide.CurrentWaypointIndex.Should().Be(expectedAStarGuide.CurrentWaypointIndex);
            }

            if (expected.TrailGuide is VolumeGuide expectedVolumeGuide
                && actual.TrailGuide is VolumeGuide actualVolumeGuide)
            {
                actualVolumeGuide.CurrentWaypointIndex.Should().Be(expectedVolumeGuide.CurrentWaypointIndex);
            }

            if (expected.TrailGuide is HybridGuide expectedHybridGuide
                && actual.TrailGuide is HybridGuide actualHybridGuide)
            {
                actualHybridGuide.CurrentWaypointIndex.Should().Be(expectedHybridGuide.CurrentWaypointIndex);
            }
        }
    }

    private static void AddObstacle(Vector3d position)
    {
        GlobalGridManager.TryGetVoxel(position, out Voxel voxel).Should().BeTrue();
        GridObstacleManager.TryAddObstacle(
            voxel.GlobalIndex,
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
        PathTestFactory.RegisterGeneratedVolumePoint(position, VolumeTraversalMode.Water, "NavigatorSerializationWater");
    }

    private static void AddOpen(Vector3d position)
    {
        PathTestFactory.RegisterGeneratedVolumePoint(position, VolumeTraversalMode.Open, "NavigatorSerializationOpen");
    }

    private static void AssertTurningStateMatches(NavTurning expected, NavTurning actual)
    {
        actual.CanTurn.Should().Be(expected.CanTurn);
        actual.TurnRate.Should().Be(expected.TurnRate);
        actual.TargetReached.Should().BeTrue();
        actual.TargetRotation.Should().Be(FixedQuaternion.Identity);
    }
}

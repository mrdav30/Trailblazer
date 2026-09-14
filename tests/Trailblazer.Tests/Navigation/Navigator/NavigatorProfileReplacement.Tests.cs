using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Navigation.Turning;
using Trailblazer.Pathing;
using SwiftCollections.Diagnostics;
using Xunit;

namespace Trailblazer.Tests.Navigation;

[Collection("PathingCollection")]
public sealed class NavigatorProfileReplacementTests : IDisposable
{
    private readonly TrailblazerWorldContext _context =
        PathTestFactory.CreateContextWithGrid();

    [Fact]
    public void TryReplaceNavigationProfile_ShouldPreserveFootIdentityAndLiveControllerState()
    {
        NavigationAgentProfile standing = CreateProfile(
            radius: Fixed64.Half,
            height: (Fixed64)2,
            rootToFootOffset: Fixed64.One,
            allowedMedia: TraversalMedia.Solid,
            capabilities: TraversalCapability.Jump | TraversalCapability.Fly);
        NavigationAgentProfile crouching = CreateProfile(
            radius: Fixed64.Quarter,
            height: Fixed64.One,
            rootToFootOffset: Fixed64.Quarter,
            allowedMedia: TraversalMedia.Solid,
            capabilities: TraversalCapability.Jump);
        var navigator = CreateNavigator(new Vector3d(0, 2, 0), standing);
        navigator.AddPositionDelta(Vector3d.Right);
        navigator.CommitFrameMotion();
        Vector3d foot = navigator.FootPosition;
        Vector3d velocity = navigator.Velocity;
        Guid identity = navigator.GlobalId;
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        NavMotor motor = TestRequire.NotNull(navigator.Motor);
        NavTurning turning = TestRequire.NotNull(navigator.Turning);
        Voxel oldVoxel = PathTestFactory.RequireVoxel(_context, navigator.Position);
        Voxel replacementVoxel = PathTestFactory.RequireVoxel(
            _context,
            new Vector3d(
                Fixed64.One,
                Fixed64.FromFraction(5, 4),
                Fixed64.Zero));
        oldVoxel.Should().NotBeSameAs(replacementVoxel);
        oldVoxel.OccupantCount.Should().Be(1);
        replacementVoxel.OccupantCount.Should().Be(0);
        NavigationAgentProfile observedProfile = default;
        Vector3d observedRoot = default;
        replacementVoxel.OnOccupantAdded += _ =>
        {
            observedProfile = navigator.NavigationProfile;
            observedRoot = navigator.Position;
        };

        NavigationProfileReplacementStatus status =
            navigator.TryReplaceNavigationProfile(crouching);

        status.Should().Be(NavigationProfileReplacementStatus.Applied);
        navigator.NavigationProfile.Should().Be(crouching);
        navigator.FootPosition.Should().Be(foot);
        navigator.Position.Should().Be(new Vector3d(
            Fixed64.One,
            Fixed64.FromFraction(5, 4),
            Fixed64.Zero));
        navigator.LastPosition.Should().Be(new Vector3d(
            Fixed64.Zero,
            Fixed64.FromFraction(5, 4),
            Fixed64.Zero));
        navigator.Velocity.Should().Be(velocity);
        navigator.GlobalId.Should().Be(identity);
        navigator.Steering.Should().BeSameAs(steering);
        navigator.Motor.Should().BeSameAs(motor);
        navigator.Turning.Should().BeSameAs(turning);
        navigator.PhysicalReplacementCallCount.Should().Be(1);
        navigator.LastPhysicalReplacementProfile.Should().Be(crouching);
        navigator.LastPhysicalReplacementRoot.Should().Be(navigator.Position);
        oldVoxel.OccupantCount.Should().Be(0);
        replacementVoxel.OccupantCount.Should().Be(1);
        observedProfile.Should().Be(crouching);
        observedRoot.Should().Be(navigator.Position);
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldNoOpWithoutTouchingTheHost()
    {
        NavigationAgentProfile profile = CreateProfile();
        var navigator = CreateNavigator(Vector3d.Up, profile);

        NavigationProfileReplacementStatus status =
            navigator.TryReplaceNavigationProfile(profile);

        status.Should().Be(NavigationProfileReplacementStatus.Unchanged);
        navigator.PhysicalReplacementCallCount.Should().Be(0);
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldRejectInvalidMediumOrPhysicalFitTransactionally()
    {
        NavigationAgentProfile standing = CreateProfile();
        NavigationAgentProfile gasOnly = CreateProfile(
            height: Fixed64.Half,
            rootToFootOffset: Fixed64.Zero,
            allowedMedia: TraversalMedia.Gas);
        NavigationAgentProfile crouching = CreateProfile(
            height: Fixed64.Half,
            rootToFootOffset: Fixed64.Zero);
        var navigator = CreateNavigator(Vector3d.Up, standing);
        Vector3d position = navigator.Position;

        navigator.TryReplaceNavigationProfile(default)
            .Should().Be(NavigationProfileReplacementStatus.InvalidProfile);
        navigator.TryReplaceNavigationProfile(gasOnly)
            .Should().Be(NavigationProfileReplacementStatus.CurrentMediumNotAllowed);
        navigator.SetTrekCondition(
            TraversalMedium.Unknown,
            surfaceCondition: null,
            updateMotorState: true);
        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.CurrentMediumNotAllowed);
        navigator.SetTrekCondition(
            TraversalMedium.Solid,
            surfaceCondition: new GroundCondition(),
            updateMotorState: true);
        navigator.AcceptPhysicalReplacement = false;
        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.PhysicalFitRejected);

        navigator.NavigationProfile.Should().Be(standing);
        navigator.Position.Should().Be(position);
        navigator.FootPosition.Should().Be(position - Vector3d.Up * standing.Shape.RootToFootOffsetY);
        navigator.PhysicalReplacementCallCount.Should().Be(1);
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldNotPublishOccupancyForARejectedPhysicalFit()
    {
        NavigationAgentProfile standing = CreateProfile(
            height: (Fixed64)2,
            rootToFootOffset: Fixed64.One);
        NavigationAgentProfile crouching = CreateProfile(
            height: Fixed64.Half,
            rootToFootOffset: Fixed64.Quarter);
        var navigator = CreateNavigator(new Vector3d(0, 2, 0), standing);
        Voxel oldVoxel = PathTestFactory.RequireVoxel(_context, navigator.Position);
        Vector3d replacementRoot = new(
            Fixed64.Zero,
            Fixed64.FromFraction(5, 4),
            Fixed64.Zero);
        Voxel replacementVoxel = PathTestFactory.RequireVoxel(
            _context,
            replacementRoot);
        oldVoxel.Should().NotBeSameAs(replacementVoxel);
        int additions = 0;
        int removals = 0;
        replacementVoxel.OnOccupantAdded += _ => additions++;
        replacementVoxel.OnOccupantRemoved += _ => removals++;
        navigator.AcceptPhysicalReplacement = false;

        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.PhysicalFitRejected);

        navigator.NavigationProfile.Should().Be(standing);
        navigator.Position.Should().Be(new Vector3d(0, 2, 0));
        oldVoxel.OccupantCount.Should().Be(1);
        replacementVoxel.OccupantCount.Should().Be(0);
        additions.Should().Be(0);
        removals.Should().Be(0);
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldRejectUnsafeQueuedAndOpenFrameMotion()
    {
        NavigationAgentProfile standing = CreateProfile();
        NavigationAgentProfile crouching = CreateProfile(
            height: Fixed64.Half,
            rootToFootOffset: Fixed64.Zero);
        var navigator = CreateNavigator(Vector3d.Up, standing);
        navigator.AddPositionDelta(Vector3d.Right);

        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.UnsafeLifecycle);
        navigator.RequestedFramePosition.Should().Be(Vector3d.Up + Vector3d.Right);

        navigator.CommitFrameMotion();
        navigator.AddLocomotionDisplacement(Vector3d.Forward);
        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.UnsafeLifecycle);
        navigator.CommitFrameMotion();
        FixedQuaternion turn = FixedQuaternion.FromAxisAngle(
            Vector3d.Up,
            Fixed64.Half);
        navigator.ApplyRotationDelta(turn);
        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.UnsafeLifecycle);
        navigator.CommitFrameMotion();
        _context.Simulate();
        navigator.Simulate();
        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.UnsafeLifecycle);

        navigator.NavigationProfile.Should().Be(standing);
        navigator.PhysicalReplacementCallCount.Should().Be(0);
        navigator.CommitFrameMotion();
        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.Applied);

        var inactive = new TestNavigator(_context);
        inactive.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.UnsafeLifecycle);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TryReplaceNavigationProfile_ShouldWorkWithoutNavigationDataOrAPhysicalHost(
        bool outsideWorld)
    {
        NavigationAgentProfile standing = CreateProfile();
        NavigationAgentProfile crouching = CreateProfile(
            height: Fixed64.Half,
            rootToFootOffset: Fixed64.Zero);
        Vector3d position = outsideWorld
            ? new Vector3d(100, 100, 100)
            : Vector3d.Up;
        var navigator = CreateNavigator(position, standing);
        navigator.UseBasePhysicalReplacement = true;
        Vector3d foot = navigator.FootPosition;

        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.Applied);

        navigator.FootPosition.Should().Be(foot);
        navigator.NavigationProfile.Should().Be(crouching);
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldRejectAnOccupiedRootProjection()
    {
        NavigationAgentProfile standing = CreateProfile(
            height: (Fixed64)2,
            rootToFootOffset: Fixed64.One);
        NavigationAgentProfile crouching = CreateProfile(
            height: Fixed64.Half,
            rootToFootOffset: Fixed64.Quarter);
        var navigator = CreateNavigator(new Vector3d(0, 2, 0), standing);
        Voxel oldVoxel = PathTestFactory.RequireVoxel(_context, navigator.Position);
        Vector3d replacementRoot = new(
            Fixed64.Zero,
            Fixed64.FromFraction(5, 4),
            Fixed64.Zero);
        _context.World.TryGetGrid(replacementRoot, out VoxelGrid? grid)
            .Should().BeTrue();
        Voxel targetVoxel = PathTestFactory.RequireVoxel(_context, replacementRoot);
        grid!.TryAddObstacle(
            targetVoxel,
            _context.World.AllocateObstacleToken()).Should().BeTrue();

        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.NavigationClearanceRejected);

        navigator.NavigationProfile.Should().Be(standing);
        navigator.Position.Should().Be(new Vector3d(0, 2, 0));
        navigator.PhysicalReplacementCallCount.Should().Be(0);
        oldVoxel.OccupantCount.Should().Be(1);
        targetVoxel.OccupantCount.Should().Be(0);
        targetVoxel.IsBlocked.Should().BeTrue();
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldClearEveryLostCapabilityState()
    {
        TraversalCapability allCapabilities = TraversalCapability.Jump
            | TraversalCapability.Fly
            | TraversalCapability.Swim
            | TraversalCapability.Climb;
        NavigationAgentProfile capable = CreateProfile(
            allowedMedia: TraversalMedia.Solid | TraversalMedia.Gas | TraversalMedia.Liquid,
            capabilities: allCapabilities);
        NavigationAgentProfile grounded = CreateProfile(
            height: Fixed64.Half,
            allowedMedia: capable.AllowedMedia,
            capabilities: TraversalCapability.None);
        var navigator = CreateNavigator(Vector3d.Up, capable);
        navigator.ApplyInputTrekRequest(
            isRequestingJump: true,
            isRequestingFlight: true,
            isRequestingSwim: true,
            isRequestingClimb: true);
        NavMotor motor = TestRequire.NotNull(navigator.Motor);
        WaterLocomotion water = TestRequire.NotNull(motor.Handler.Water);
        ClimbLocomotion climb = TestRequire.NotNull(motor.Handler.Climb);
        water.RequestedSwimThisTraversal = true;
        water.IsSwimming = true;
        climb.IsClimbing = true;

        navigator.TryReplaceNavigationProfile(grounded)
            .Should().Be(NavigationProfileReplacementStatus.Applied);

        navigator.FrameRequest.IsRequestingJump.Should().BeFalse();
        navigator.FrameRequest.IsRequestingFlight.Should().BeFalse();
        navigator.FrameRequest.IsRequestingSwim.Should().BeFalse();
        navigator.FrameRequest.IsRequestingClimb.Should().BeFalse();
        water.RequestedSwimThisTraversal.Should().BeFalse();
        water.IsSwimming.Should().BeFalse();
        climb.IsClimbing.Should().BeFalse();
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldClearLostRequestsWithoutOptionalLocomotions()
    {
        TraversalCapability optionalCapabilities = TraversalCapability.Fly
            | TraversalCapability.Swim
            | TraversalCapability.Climb;
        NavigationAgentProfile capable = CreateProfile(
            capabilities: optionalCapabilities);
        NavigationAgentProfile grounded = CreateProfile(
            height: Fixed64.Half,
            capabilities: TraversalCapability.None);
        var navigator = CreateNavigator(Vector3d.Up, capable);
        NavMotor motor = TestRequire.NotNull(navigator.Motor);
        motor.SetLocomotionProfile(LocomotionProfile.CreateCoreOnly());
        navigator.ApplyInputTrekRequest(
            isRequestingFlight: true,
            isRequestingSwim: true,
            isRequestingClimb: true);

        navigator.TryReplaceNavigationProfile(grounded)
            .Should().Be(NavigationProfileReplacementStatus.Applied);

        navigator.FrameRequest.IsRequestingFlight.Should().BeFalse();
        navigator.FrameRequest.IsRequestingSwim.Should().BeFalse();
        navigator.FrameRequest.IsRequestingClimb.Should().BeFalse();
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldNotReportStoppingAnInactiveClimb()
    {
        NavigationAgentProfile climbing = CreateProfile(
            capabilities: TraversalCapability.Climb);
        NavigationAgentProfile grounded = CreateProfile(
            height: Fixed64.Half,
            capabilities: TraversalCapability.None);
        var navigator = CreateNavigator(Vector3d.Up, climbing);
        navigator.ApplyInputTrekRequest(isRequestingClimb: true);
        NavMotor motor = TestRequire.NotNull(navigator.Motor);
        ClimbLocomotion climb = TestRequire.NotNull(motor.Handler.Climb);
        climb.IsClimbing.Should().BeFalse();
        int stopNotifications = 0;
        motor.Events.OnStopClimb = () => stopNotifications++;

        navigator.TryReplaceNavigationProfile(grounded)
            .Should().Be(NavigationProfileReplacementStatus.Applied);

        navigator.FrameRequest.IsRequestingClimb.Should().BeFalse();
        climb.IsClimbing.Should().BeFalse();
        stopNotifications.Should().Be(0);
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldRebuildDurableGuidanceWithoutChangingItsGoal()
    {
        NavigationAgentProfile standing = CreateProfile(
            allowedMedia: TraversalMedia.Solid | TraversalMedia.Liquid);
        NavigationAgentProfile crouching = CreateProfile(
            height: Fixed64.Half,
            rootToFootOffset: Fixed64.Zero,
            allowedMedia: TraversalMedia.Solid | TraversalMedia.Liquid);
        var navigator = CreateNavigator(Vector3d.Up, standing);
        var query = new PathQuery(
            new NavigationEndpoint(navigator.FootPosition),
            new NavigationEndpoint(Vector3d.Right * 3),
            standing,
            new NavigationAreaPolicyKey("profile-replacement", 1),
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Liquid),
            PathAlgorithm.FlowField,
            new NavigationWorkBudget(64, 64, 64, 64, 64, 0, 0, 0, 64, 64, 0),
            allowTransitions: true,
            new FlowFieldQueryOptions(Fixed64.One));
        navigator.ApplyGuidedTrekRequest(query, TrekRate.Moderate, groupId: 7);

        NavigationProfileReplacementStatus status =
            navigator.TryReplaceNavigationProfile(crouching);

        status.Should().Be(NavigationProfileReplacementStatus.Applied);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        PathQuery rebuilt = steering.CurrentQuery!.Value;
        rebuilt.Agent.Should().Be(crouching);
        rebuilt.Start.Position.Should().Be(navigator.FootPosition);
        rebuilt.Traversal.StartMedium.Should().Be(TraversalMedium.Solid);
        rebuilt.End.Should().Be(query.End);
        rebuilt.AreaPolicy.Should().Be(query.AreaPolicy);
        rebuilt.Traversal.TargetMedia.Should().Be(query.Traversal.TargetMedia);
        rebuilt.Algorithm.Should().Be(query.Algorithm);
        rebuilt.Budget.Should().Be(query.Budget);
        rebuilt.AllowTransitions.Should().BeTrue();
        rebuilt.FlowField.Should().Be(query.FlowField);
        steering.MovementGroupID.Should().Be(7);
        navigator.FrameRequest.Rate.Should().Be(TrekRate.Moderate);
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldRebaseGuidanceToTheCurrentMap()
    {
        (Vector3d start, Vector3d end) = PublishTwoMapCells();
        NavigationAgentProfile standing = CreateProfile();
        NavigationAgentProfile crouching = CreateProfile(height: Fixed64.Half);
        var navigator = CreateNavigator(start, standing);
        var query = new PathQuery(
            new NavigationEndpoint(start, "profile-map-a"),
            new NavigationEndpoint(end, "profile-map-b"),
            standing,
            new NavigationAreaPolicyKey("profile-replacement", 1),
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(64, 64, 64, 64, 64, 0, 0, 0, 0, 0, 0),
            allowTransitions: false);
        navigator.ApplyGuidedTrekRequest(query);
        navigator.AddPositionDelta(end - start);
        navigator.CommitFrameMotion();
        navigator.LastCommittedCell!.Value.Address.MapId
            .Should().Be("profile-map-b");

        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.Applied);

        PathQuery rebuilt = TestRequire.NotNull(navigator.Steering)
            .CurrentQuery!.Value;
        rebuilt.Start.Position.Should().Be(end);
        rebuilt.Start.MapId.Should().Be("profile-map-b");
        rebuilt.End.Should().Be(query.End);
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldRejectIncompatibleDurableIntent()
    {
        NavigationAgentProfile standing = CreateProfile(
            allowedMedia: TraversalMedia.Solid | TraversalMedia.Liquid);
        NavigationAgentProfile solidOnly = CreateProfile(
            height: Fixed64.Half,
            rootToFootOffset: Fixed64.Zero,
            allowedMedia: TraversalMedia.Solid);
        var navigator = CreateNavigator(Vector3d.Up, standing);
        var query = new PathQuery(
            new NavigationEndpoint(navigator.FootPosition),
            new NavigationEndpoint(Vector3d.Right),
            standing,
            new NavigationAreaPolicyKey("profile-replacement", 1),
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Liquid),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(64, 64, 64, 64, 64, 0, 0, 0, 0, 0, 0),
            allowTransitions: false);
        navigator.ApplyGuidedTrekRequest(query);

        navigator.TryReplaceNavigationProfile(solidOnly)
            .Should().Be(NavigationProfileReplacementStatus.GuidanceIncompatible);

        navigator.NavigationProfile.Should().Be(standing);
        TestRequire.NotNull(navigator.Steering).CurrentQuery.Should().Be(query);
        navigator.PhysicalReplacementCallCount.Should().Be(0);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void TryReplaceNavigationProfile_ShouldReleaseAndReacquireGuidanceForTheSameGoal(
        PathAlgorithm algorithm)
    {
        (Vector3d start, Vector3d end) = PublishGraphLine();
        NavigationAgentProfile standing = CreateProfile(
            rootToFootOffset: Fixed64.Quarter,
            capabilities: TraversalCapability.Jump);
        NavigationAgentProfile crouching = CreateProfile(
            radius: Fixed64.FromFraction(1, 5),
            height: Fixed64.Half,
            rootToFootOffset: Fixed64.Zero,
            capabilities: TraversalCapability.Jump);
        var navigator = CreateNavigator(
            start + Vector3d.Up * standing.Shape.RootToFootOffsetY,
            standing);
        var query = new PathQuery(
            new NavigationEndpoint(start, "profile-guidance"),
            new NavigationEndpoint(end, "profile-guidance"),
            standing,
            new NavigationAreaPolicyKey("profile-replacement", 1),
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
            algorithm,
            new NavigationWorkBudget(4096, 4096, 4096, 4096, 4096, 0, 0, 0, 0, 0, 0),
            allowTransitions: false,
            algorithm == PathAlgorithm.FlowField
                ? new FlowFieldQueryOptions(Fixed64.Quarter)
                : default);
        navigator.ApplyGuidedTrekRequest(query, groupId: 13);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.ConfigurePathRechecks(16, 15);
        steering.GetHeading(navigator, out _);
        ActiveLeaseCount().Should().Be(1);

        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.Applied);

        ActiveLeaseCount().Should().Be(0);
        steering.HasNavigationGuidance.Should().BeFalse();
        PathQuery rebuilt = steering.CurrentQuery!.Value;
        rebuilt.Agent.Should().Be(crouching);
        rebuilt.End.Should().Be(query.End);
        rebuilt.AreaPolicy.Should().Be(query.AreaPolicy);
        rebuilt.Algorithm.Should().Be(query.Algorithm);
        rebuilt.Budget.Should().Be(query.Budget);
        rebuilt.FlowField.Should().Be(query.FlowField);
        steering.MovementGroupID.Should().Be(13);
        steering.PathRecheckCooldownFrames.Should().Be(16);
        steering.PathRecheckPhase.Should().Be(15);

        steering.GetHeading(navigator, out _);

        steering.HasNavigationGuidance.Should().BeTrue();
        ActiveLeaseCount().Should().Be(1);
        navigator.Reset();
        ActiveLeaseCount().Should().Be(0);

        int ActiveLeaseCount() => algorithm == PathAlgorithm.AStar
            ? _context.Pathing.NavigationAStarAdmissionGate.PayloadCache.ActiveLeaseCount
            : _context.Pathing.NavigationFlowAdmissionGate.PayloadCache.ActiveLeaseCount;
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RoundTrip_ShouldRestoreTheAcceptedProfileAndAcquireFreshGuidance(
        bool useMemoryPack)
    {
        (Vector3d start, Vector3d end) = PublishGraphLine();
        NavigationAgentProfile standing = CreateProfile(
            rootToFootOffset: Fixed64.Quarter,
            capabilities: TraversalCapability.Jump);
        NavigationAgentProfile crouching = CreateProfile(
            radius: Fixed64.FromFraction(1, 5),
            height: Fixed64.Half,
            rootToFootOffset: Fixed64.Zero,
            capabilities: TraversalCapability.Jump);
        var source = CreateNavigator(
            start + Vector3d.Up * standing.Shape.RootToFootOffsetY,
            standing);
        PathQuery query = CreateQuery(start, end, standing, "profile-guidance");
        source.ApplyGuidedTrekRequest(query, TrekRate.Moderate, groupId: 21);
        source.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.Applied);
        NavSteering sourceSteering = TestRequire.NotNull(source.Steering);
        sourceSteering.GetHeading(source, out _).Should().NotBe(Vector3d.Zero);
        NavigationAStarPayloadCache cache =
            _context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        cache.ActiveLeaseCount.Should().Be(1);
        Vector3d acceptedRoot = source.Position;
        object payload = SerializationUtility.SerializeRecord(source, useMemoryPack);
        source.Reset();
        cache.ActiveLeaseCount.Should().Be(0);

        var mismatched = CreateNavigator(new Vector3d(-2, 0, -2), standing);
        Vector3d mismatchedRoot = mismatched.Position;
        Action populateMismatch = () =>
            SerializationUtility.PopulateRecord(mismatched, payload, useMemoryPack);
        populateMismatch.Should().Throw<InvalidOperationException>();
        mismatched.NavigationProfile.Should().Be(standing);
        mismatched.Position.Should().Be(mismatchedRoot);
        mismatched.Reset();

        var target = CreateNavigator(new Vector3d(-2, 0, -2), crouching);
        SerializationUtility.PopulateRecord(target, payload, useMemoryPack);

        target.NavigationProfile.Should().Be(crouching);
        target.Position.Should().Be(acceptedRoot);
        target.FootPosition.Should().Be(start);
        NavSteering targetSteering = TestRequire.NotNull(target.Steering);
        PathQuery restored = targetSteering.CurrentQuery!.Value;
        restored.Agent.Should().Be(crouching);
        restored.Start.Position.Should().Be(start);
        restored.End.Should().Be(query.End);
        targetSteering.MovementGroupID.Should().Be(21);
        target.FrameRequest.Rate.Should().Be(TrekRate.Moderate);
        targetSteering.HasNavigationGuidance.Should().BeFalse();
        cache.ActiveLeaseCount.Should().Be(0);

        targetSteering.GetHeading(target, out _).Should().NotBe(Vector3d.Zero);
        targetSteering.HasNavigationGuidance.Should().BeTrue();
        cache.ActiveLeaseCount.Should().Be(1);
        target.Reset();
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void TryReplaceNavigationProfile_ShouldRejectAHeldActionUntilTheHostCompletesIt(
        PathAlgorithm algorithm)
    {
        (Vector3d start, Vector3d end) = PublishTransitionGraph();
        NavigationAgentProfile standing = CreateProfile(
            rootToFootOffset: Fixed64.Quarter,
            allowedMedia: TraversalMedia.Solid | TraversalMedia.Gas,
            capabilities: TraversalCapability.Climb);
        NavigationAgentProfile crouching = CreateProfile(
            height: Fixed64.Half,
            rootToFootOffset: Fixed64.Zero,
            allowedMedia: TraversalMedia.Solid | TraversalMedia.Gas,
            capabilities: TraversalCapability.Climb);
        var navigator = CreateNavigator(
            start + Vector3d.Up * standing.Shape.RootToFootOffsetY,
            standing);
        var query = new PathQuery(
            new NavigationEndpoint(start, "profile-transition"),
            new NavigationEndpoint(end, "profile-transition"),
            standing,
            new NavigationAreaPolicyKey("profile-replacement", 1),
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Gas),
            algorithm,
            new NavigationWorkBudget(
                8192,
                32,
                128,
                1024,
                1024,
                1024,
                1024,
                0,
                0,
                1024,
                32),
            allowTransitions: true);
        navigator.ApplyGuidedTrekRequest(query);
        navigator.Simulate();
        NavigationTransitionInstruction pending = navigator.PendingTransition!.Value;
        navigator.CommitFrameMotion();

        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.ActionInProgress);
        navigator.NavigationProfile.Should().Be(standing);
        navigator.PendingTransition.Should().Be(pending);
        navigator.PhysicalReplacementCallCount.Should().Be(0);

        navigator.CompletePendingTransition(pending)
            .Should().Be(NavigationGuideStatus.Success);
        navigator.AddPositionDelta(
            end + Vector3d.Up * standing.Shape.RootToFootOffsetY
            - navigator.Position);
        navigator.CommitFrameMotion();
        navigator.SetTrekCondition(
            medium: TraversalMedium.Gas,
            surfaceCondition: null,
            updateMotorState: true);
        NavigationProfileReplacementStatus replacementStatus =
            navigator.TryReplaceNavigationProfile(crouching);
        navigator.PhysicalReplacementCallCount.Should().Be(1);
        replacementStatus.Should().Be(NavigationProfileReplacementStatus.Applied);
        navigator.NavigationProfile.Should().Be(crouching);
        navigator.PendingTransition.Should().BeNull();
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldFinishTheTransactionBeforeNotifyingClimbLoss()
    {
        (Vector3d start, Vector3d end) = PublishGraphLine();
        NavigationAgentProfile climbing = CreateProfile(
            capabilities: TraversalCapability.Climb);
        NavigationAgentProfile grounded = CreateProfile(
            height: Fixed64.Half,
            capabilities: TraversalCapability.None);
        var navigator = CreateNavigator(start, climbing);
        PathQuery query = CreateQuery(start, end, climbing, "profile-guidance");
        navigator.ApplyGuidedTrekRequest(query);
        NavSteering steering = TestRequire.NotNull(navigator.Steering);
        steering.GetHeading(navigator, out _).Should().NotBe(Vector3d.Zero);
        NavigationAStarPayloadCache cache =
            _context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        cache.ActiveLeaseCount.Should().Be(1);
        NavMotor motor = TestRequire.NotNull(navigator.Motor);
        ClimbLocomotion climb = TestRequire.NotNull(motor.Handler.Climb);
        climb.IsClimbing = true;
        motor.Events.OnStopClimb = () =>
            SwiftThrowHelper.ThrowIfTrue(true, message: "Host callback failure.");

        Action replace = () => navigator.TryReplaceNavigationProfile(grounded);

        replace.Should().Throw<InvalidOperationException>();
        navigator.NavigationProfile.Should().Be(grounded);
        navigator.PhysicalReplacementCallCount.Should().Be(1);
        climb.IsClimbing.Should().BeFalse();
        steering.HasNavigationGuidance.Should().BeFalse();
        steering.CurrentQuery!.Value.Agent.Should().Be(grounded);
        cache.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldAttemptEveryPostCommitNotification()
    {
        Vector3d foot = PublishSingleCell(new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One));
        NavigationAgentProfile climbing = CreateProfile(
            capabilities: TraversalCapability.Climb);
        NavigationAgentProfile grounded = CreateProfile(
            height: Fixed64.Half,
            capabilities: TraversalCapability.None);
        var navigator = CreateNavigator(
            foot + Vector3d.Up * climbing.Shape.RootToFootOffsetY,
            climbing);
        NavMotor motor = TestRequire.NotNull(navigator.Motor);
        ClimbLocomotion climb = TestRequire.NotNull(motor.Handler.Climb);
        climb.IsClimbing = true;
        int stopNotifications = 0;
        motor.Events.OnStopClimb = () => stopNotifications++;
        navigator.CommittedCellChanged += _ =>
            SwiftThrowHelper.ThrowIfTrue(
                true,
                message: "Host committed-cell callback failure.");

        Action replace = () => navigator.TryReplaceNavigationProfile(grounded);

        replace.Should().Throw<InvalidOperationException>();
        navigator.NavigationProfile.Should().Be(grounded);
        navigator.LastCommittedCell.Should().NotBeNull();
        climb.IsClimbing.Should().BeFalse();
        stopNotifications.Should().Be(1);
        navigator.PhysicalReplacementCallCount.Should().Be(1);
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldCommitBeforeRethrowingAPhysicalNotificationFailure()
    {
        NavigationAgentProfile standing = CreateProfile();
        NavigationAgentProfile crouching = CreateProfile(
            height: Fixed64.Half,
            rootToFootOffset: Fixed64.Zero);
        var navigator = CreateNavigator(Vector3d.Up, standing);
        Vector3d foot = navigator.FootPosition;
        var notificationFailure = new InvalidOperationException(
            "Physical post-commit notification failure.");
        navigator.PhysicalReplacementNotificationException = notificationFailure;

        Action replace = () =>
            navigator.TryReplaceNavigationProfile(crouching);

        replace.Should().Throw<InvalidOperationException>()
            .Which.Should().BeSameAs(notificationFailure);
        navigator.NavigationProfile.Should().Be(crouching);
        navigator.FootPosition.Should().Be(foot);
        navigator.PhysicalReplacementCallCount.Should().Be(1);
    }

    [Theory]
    [InlineData("radius")]
    [InlineData("height")]
    [InlineData("capability")]
    public void TryReplaceNavigationProfile_ShouldRejectCurrentCellClearanceViolations(
        string violation)
    {
        Fixed64 clearance = Fixed64.One;
        Vector3d foot = PublishSingleCell(new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.Climb,
            default,
            Fixed64.Zero,
            clearance,
            clearance));
        NavigationAgentProfile original = CreateProfile(
            radius: Fixed64.Half,
            height: Fixed64.Half,
            rootToFootOffset: Fixed64.Half,
            capabilities: TraversalCapability.Climb);
        Fixed64 beyond = Fixed64.One + Fixed64.FromRaw(1);
        NavigationAgentProfile replacement = CreateProfile(
            radius: violation == "radius" ? beyond : clearance,
            height: violation == "height" ? beyond : clearance,
            rootToFootOffset: Fixed64.Quarter,
            capabilities: violation == "capability"
                ? TraversalCapability.None
                : TraversalCapability.Climb);
        var navigator = CreateNavigator(
            foot + Vector3d.Up * original.Shape.RootToFootOffsetY,
            original);
        navigator.ApplyGuidedTrekRequest(CreateQuery(
            navigator.FootPosition,
            foot,
            original,
            "profile-clearance"));

        navigator.TryReplaceNavigationProfile(replacement)
            .Should().Be(NavigationProfileReplacementStatus.NavigationClearanceRejected);

        navigator.NavigationProfile.Should().Be(original);
        navigator.PhysicalReplacementCallCount.Should().Be(0);
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldUseNavigationPublishedAfterActivation()
    {
        NormalizedGridConfiguration binding = GetDefaultBinding();
        var index = new VoxelIndex(4, 4, 4);
        Vector3d foot = GuidedPathTestScene.Anchor(binding, index);
        NavigationAgentProfile standing = CreateProfile();
        NavigationAgentProfile crouching = CreateProfile(height: Fixed64.Half);
        var navigator = CreateNavigator(
            foot + Vector3d.Up * standing.Shape.RootToFootOffsetY,
            standing);
        navigator.LastCommittedCell.Should().BeNull();
        PublishSingleCell(new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One));
        navigator.LastCommittedCell.Should().BeNull();

        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.Applied);

        navigator.FootPosition.Should().Be(foot);
        navigator.LastCommittedCell!.Value.Address.Should().Be(
            new NavigationCellAddress("profile-clearance", index));
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldRejectAStaleGridBinding()
    {
        NormalizedGridConfiguration binding = GetDefaultBinding();
        var index = new VoxelIndex(4, 4, 4);
        Vector3d foot = GuidedPathTestScene.Anchor(binding, index);
        NavigationAgentProfile standing = CreateProfile();
        NavigationAgentProfile crouching = CreateProfile(height: Fixed64.Half);
        var navigator = CreateNavigator(
            foot + Vector3d.Up * standing.Shape.RootToFootOffsetY,
            standing);
        PublishSingleCell(new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One));
        VoxelGrid oldGrid = _context.World.ActiveGrids[0];
        GridConfiguration configuration = oldGrid.Configuration;
        _context.World.TryRemoveGrid(oldGrid.GridIndex).Should().BeTrue();
        _context.World.TryAddGrid(configuration, out _).Should().BeTrue();

        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.NavigationClearanceRejected);

        navigator.NavigationProfile.Should().Be(standing);
        navigator.FootPosition.Should().Be(foot);
        navigator.PhysicalReplacementCallCount.Should().Be(0);
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldRejectBlockedCurrentCell()
    {
        var index = new VoxelIndex(4, 4, 4);
        Vector3d foot = PublishSingleCell(new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One));
        NavigationAgentProfile standing = CreateProfile();
        NavigationAgentProfile crouching = CreateProfile(height: Fixed64.Half);
        _context.World.TryGetGrid(foot, out VoxelGrid? grid).Should().BeTrue();
        grid!.TryGetVoxel(index, out Voxel? voxel).Should().BeTrue();
        grid!.TryAddObstacle(
            voxel!,
            _context.World.AllocateObstacleToken()).Should().BeTrue();
        NavigationGraphCellState blockedState = default;
        for (int frame = 0; frame < 1_024; frame++)
        {
            _context.Simulate();
            if (_context.Pathing.TryGetNavigationGraphCellState(
                    "profile-clearance",
                    index,
                    out blockedState)
                && blockedState.IsBlocked)
            {
                break;
            }
        }
        blockedState.IsBlocked.Should().BeTrue();
        var navigator = CreateNavigator(
            foot + Vector3d.Up * standing.Shape.RootToFootOffsetY,
            standing);
        navigator.CommitFrameMotion();
        navigator.LastCommittedCell.Should().NotBeNull();

        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.NavigationClearanceRejected);

        navigator.NavigationProfile.Should().Be(standing);
        navigator.PhysicalReplacementCallCount.Should().Be(0);
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldAllowUnguidedReplacementAfterMapRemoval()
    {
        Vector3d foot = PublishSingleCell(new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One));
        NavigationAgentProfile standing = CreateProfile();
        NavigationAgentProfile crouching = CreateProfile(height: Fixed64.Half);
        var navigator = CreateNavigator(
            foot + Vector3d.Up * standing.Shape.RootToFootOffsetY,
            standing);
        navigator.CommitFrameMotion();
        navigator.LastCommittedCell.Should().NotBeNull();
        var removal = new NavigationMapRemoveOperation(
            "profile-clearance",
            operationSequence: 3,
            effectiveFrame: _context.FrameCount + 1);
        _context.Pathing.Admit(removal).Should().BeTrue();
        SimulateUntilTerminal(removal.Receipt);
        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.Applied);

        navigator.NavigationProfile.Should().Be(crouching);
        navigator.FootPosition.Should().Be(foot);
        navigator.LastCommittedCell.Should().BeNull();
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldRejectGuidanceWithoutACurrentCell()
    {
        NavigationCell cell = new(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One);
        PublishSingleCell(cell);
        NormalizedGridConfiguration binding = GetDefaultBinding();
        Vector3d missingFoot = GuidedPathTestScene.Anchor(
            binding,
            new VoxelIndex(5, 4, 4));
        NavigationAgentProfile standing = CreateProfile();
        NavigationAgentProfile crouching = CreateProfile(height: Fixed64.Half);
        var navigator = CreateNavigator(
            missingFoot + Vector3d.Up * standing.Shape.RootToFootOffsetY,
            standing);
        navigator.ApplyGuidedTrekRequest(CreateQuery(
            missingFoot,
            missingFoot,
            standing,
            "profile-clearance"));

        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.NavigationClearanceRejected);

        navigator.NavigationProfile.Should().Be(standing);
        navigator.PhysicalReplacementCallCount.Should().Be(0);
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldResolveTheCellAboveAVoxelBoundary()
    {
        var index = new VoxelIndex(4, 5, 4);
        Vector3d foot = PublishSingleCell(
            index,
            new NavigationCell(
                TraversalMedia.Solid,
                TraversalCapability.None,
                default,
                Fixed64.Zero,
                Fixed64.One,
                Fixed64.One));
        NavigationAgentProfile standing = CreateProfile();
        NavigationAgentProfile crouching = CreateProfile(height: Fixed64.Half);
        var navigator = CreateNavigator(
            foot + Vector3d.Up * standing.Shape.RootToFootOffsetY,
            standing);
        navigator.ApplyGuidedTrekRequest(CreateQuery(
            foot,
            foot,
            standing,
            "profile-clearance"));

        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.Applied);

        navigator.NavigationProfile.Should().Be(crouching);
        navigator.FootPosition.Should().Be(foot);
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldUseTheContainingCellForAirborneMedia()
    {
        var index = new VoxelIndex(4, 4, 4);
        PublishSingleCell(new NavigationCell(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One));
        NormalizedGridConfiguration binding = GetDefaultBinding();
        binding.TryGetCellPrism(index, out GridCellPrism prism).Should().BeTrue();
        Vector3d foot = prism.Center;
        NavigationAgentProfile standing = CreateProfile(
            allowedMedia: TraversalMedia.Solid | TraversalMedia.Gas);
        NavigationAgentProfile crouching = CreateProfile(
            height: Fixed64.Half,
            allowedMedia: standing.AllowedMedia);
        var navigator = CreateNavigator(
            foot + Vector3d.Up * standing.Shape.RootToFootOffsetY,
            standing);
        navigator.SetTrekCondition(
            TraversalMedium.Gas,
            surfaceCondition: null,
            updateMotorState: true);
        navigator.ApplyGuidedTrekRequest(new PathQuery(
            new NavigationEndpoint(foot, "profile-clearance"),
            new NavigationEndpoint(foot, "profile-clearance"),
            standing,
            new NavigationAreaPolicyKey("profile-replacement", 1),
            new TraversalIntent(TraversalMedium.Gas, TraversalMedia.Gas),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(64, 64, 64, 64, 64, 0, 0, 0, 0, 0, 0),
            allowTransitions: false));

        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.Applied);

        navigator.NavigationProfile.Should().Be(crouching);
        navigator.FootPosition.Should().Be(foot);
        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Gas);
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldRejectAPhysicallyAbsentCurrentCell()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var configuration = new GridConfiguration(
            new Vector3d(-1, -1, -1),
            new Vector3d(3, 3, 3),
            storageKind: GridStorageKind.Sparse);
        context.World.TryAddGrid(configuration, out ushort gridIndex)
            .Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        var index = new VoxelIndex(1, 1, 1);
        var cell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One);
        NavigationMap map = new NavigationMapBuilder("profile-absent", binding)
            .AddCell(index, cell)
            .Build();
        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("profile-absent", 1),
            new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        GuidedPathTestScene.PublishMapAndPolicy(
            context,
            map,
            bakeVersion: 1,
            OverlayReplacementPolicy.Clear,
            mapSequence: 1,
            policy,
            policySequence: 2);
        VoxelGrid grid = context.World.ActiveGrids[gridIndex];
        grid.TryAddVoxel(index, out _).Should().BeTrue();
        Vector3d foot = GuidedPathTestScene.Anchor(binding, index);
        NavigationAgentProfile standing = CreateProfile();
        NavigationAgentProfile crouching = CreateProfile(height: Fixed64.Half);
        var navigator = new TestNavigator(context);
        navigator.Setup(
            foot + Vector3d.Up * standing.Shape.RootToFootOffsetY,
            standing);
        navigator.Initialize(new TrekCondition
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = foot.Y,
            GroundState = new GroundCondition()
        });
        var query = new PathQuery(
            new NavigationEndpoint(foot, "profile-absent"),
            new NavigationEndpoint(foot, "profile-absent"),
            standing,
            policy.Key,
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(64, 64, 64, 64, 64, 0, 0, 0, 0, 0, 0),
            allowTransitions: false);
        navigator.ApplyGuidedTrekRequest(query);

        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.NavigationClearanceRejected);

        navigator.NavigationProfile.Should().Be(standing);
        navigator.Position.Should().Be(
            foot + Vector3d.Up * standing.Shape.RootToFootOffsetY);
        navigator.Steering!.CurrentQuery.Should().Be(query);
        navigator.PhysicalReplacementCallCount.Should().Be(0);
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldRejectCurrentCellMediumMismatch()
    {
        Vector3d foot = PublishSingleCell(new NavigationCell(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One));
        NavigationAgentProfile standing = CreateProfile(
            allowedMedia: TraversalMedia.Solid | TraversalMedia.Gas);
        NavigationAgentProfile crouching = CreateProfile(
            height: Fixed64.Half,
            allowedMedia: standing.AllowedMedia);
        var navigator = CreateNavigator(
            foot + Vector3d.Up * standing.Shape.RootToFootOffsetY,
            standing);
        PathQuery query = CreateQuery(
            foot,
            foot,
            standing,
            "profile-clearance");
        navigator.ApplyGuidedTrekRequest(query);

        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.NavigationClearanceRejected);

        navigator.NavigationProfile.Should().Be(standing);
        navigator.Steering!.CurrentQuery.Should().Be(query);
        navigator.PhysicalReplacementCallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("missing-policy")]
    [InlineData("denied")]
    public void TryReplaceNavigationProfile_ShouldPreserveGuidanceWhenAreaPolicyRejects(
        string policyCase)
    {
        Vector3d foot = PublishSingleCell(new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One));
        NavigationAreaPolicyKey key = policyCase == "missing-policy"
            ? new NavigationAreaPolicyKey("profile-missing", 1)
            : new NavigationAreaPolicyKey("profile-replacement", 1);
        if (policyCase == "denied")
        {
            key = new NavigationAreaPolicyKey("profile-denied", 1);
            var deniedPolicy = new NavigationAreaPolicy(
                key,
                new[] { new NavigationAreaRule(false, Fixed64.Zero) });
            var policyOperation = new NavigationAreaPolicyCommitOperation(
                deniedPolicy,
                publicationSequence: 3,
                effectiveFrame: _context.FrameCount + 1);
            _context.Pathing.Admit(policyOperation).Should().BeTrue();
            SimulateUntilTerminal(policyOperation.Receipt);
            policyOperation.Receipt.Status.Should().Be(
                NavigationOperationStatus.Applied);
        }
        NavigationAgentProfile standing = CreateProfile();
        NavigationAgentProfile crouching = CreateProfile(height: Fixed64.Half);
        var navigator = CreateNavigator(
            foot + Vector3d.Up * standing.Shape.RootToFootOffsetY,
            standing);
        var query = new PathQuery(
            new NavigationEndpoint(foot, "profile-clearance"),
            new NavigationEndpoint(foot, "profile-clearance"),
            standing,
            key,
            new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(64, 64, 64, 64, 64, 0, 0, 0, 0, 0, 0),
            allowTransitions: false);
        navigator.ApplyGuidedTrekRequest(query);

        navigator.TryReplaceNavigationProfile(crouching)
            .Should().Be(NavigationProfileReplacementStatus.NavigationClearanceRejected);

        navigator.NavigationProfile.Should().Be(standing);
        navigator.FootPosition.Should().Be(foot);
        navigator.Steering!.CurrentQuery.Should().Be(query);
        navigator.PhysicalReplacementCallCount.Should().Be(0);
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldAcceptInclusiveCurrentCellLimits()
    {
        Vector3d foot = PublishSingleCell(new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.Climb,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One));
        NavigationAgentProfile original = CreateProfile(
            radius: Fixed64.Half,
            height: Fixed64.Half,
            rootToFootOffset: Fixed64.Half,
            capabilities: TraversalCapability.Climb);
        NavigationAgentProfile replacement = CreateProfile(
            radius: Fixed64.One,
            height: Fixed64.One,
            rootToFootOffset: Fixed64.Quarter,
            capabilities: TraversalCapability.Climb);
        var navigator = CreateNavigator(
            foot + Vector3d.Up * original.Shape.RootToFootOffsetY,
            original);
        navigator.ApplyGuidedTrekRequest(CreateQuery(
            navigator.FootPosition,
            foot,
            original,
            "profile-clearance"));

        navigator.TryReplaceNavigationProfile(replacement)
            .Should().Be(NavigationProfileReplacementStatus.Applied);

        navigator.FootPosition.Should().Be(foot);
        navigator.NavigationProfile.Should().Be(replacement);
    }

    [Fact]
    public void TryReplaceNavigationProfile_ShouldReportAnUnrepresentableFootAnchor()
    {
        NavigationAgentProfile original = CreateProfile(
            rootToFootOffset: Fixed64.Zero);
        NavigationAgentProfile replacement = CreateProfile(
            height: (Fixed64)2,
            rootToFootOffset: Fixed64.MaxValue);
        var navigator = CreateNavigator(Vector3d.Up, original);

        navigator.TryReplaceNavigationProfile(replacement)
            .Should().Be(NavigationProfileReplacementStatus.FootAnchorUnrepresentable);

        navigator.Position.Should().Be(Vector3d.Up);
        navigator.NavigationProfile.Should().Be(original);
        navigator.PhysicalReplacementCallCount.Should().Be(0);

        NavigationAgentProfile extreme = CreateProfile(
            rootToFootOffset: Fixed64.MaxValue);
        var underflowing = CreateNavigator(
            new Vector3d(Fixed64.Zero, Fixed64.MinValue, Fixed64.Zero),
            extreme);

        underflowing.TryReplaceNavigationProfile(original)
            .Should().Be(NavigationProfileReplacementStatus.FootAnchorUnrepresentable);

        underflowing.Position.Y.Should().Be(Fixed64.MinValue);
        underflowing.NavigationProfile.Should().Be(extreme);
        underflowing.PhysicalReplacementCallCount.Should().Be(0);
    }

    [Fact]
    public void AirborneProfileReplacement_ShouldLoseAndRecoverFlightWithoutChangingMedium()
    {
        NavigationAgentProfile flying = CreateProfile(
            allowedMedia: TraversalMedia.Solid | TraversalMedia.Gas,
            capabilities: TraversalCapability.Fly);
        NavigationAgentProfile falling = CreateProfile(
            height: Fixed64.Half,
            allowedMedia: TraversalMedia.Solid | TraversalMedia.Gas,
            capabilities: TraversalCapability.None);
        var navigator = CreateNavigator(Vector3d.Up, flying);
        NavMotor motor = TestRequire.NotNull(navigator.Motor);
        navigator.SetAirborne(updateMotorState: true);
        navigator.ApplyInputTrekRequest(
            Vector3d.Forward,
            TrekRate.Fast,
            isRequestingFlight: true);
        _context.Simulate();
        navigator.Simulate();
        navigator.CommitFrameMotion();
        motor.IsFlying.Should().BeTrue();

        navigator.TryReplaceNavigationProfile(falling)
            .Should().Be(NavigationProfileReplacementStatus.Applied);

        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Gas);
        motor.IsFlying.Should().BeFalse();
        navigator.ToggleGuidedFlight(true);
        _context.Simulate();
        navigator.Simulate();
        motor.IsFlying.Should().BeFalse();
        navigator.CommitFrameMotion();

        navigator.TryReplaceNavigationProfile(flying)
            .Should().Be(NavigationProfileReplacementStatus.Applied);
        navigator.ToggleGuidedFlight(true);
        _context.Simulate();
        navigator.Simulate();

        navigator.FrameCondition.Medium.Should().Be(TraversalMedium.Gas);
        motor.IsFlying.Should().BeTrue();
    }

    public void Dispose()
    {
        _context.Dispose();
        GC.SuppressFinalize(this);
    }

    private TestNavigator CreateNavigator(
        Vector3d position,
        NavigationAgentProfile profile)
    {
        var navigator = new TestNavigator(_context);
        navigator.Setup(position, profile);
        navigator.Initialize(new TrekCondition
        {
            Medium = TraversalMedium.Solid,
            SurfaceLevel = Fixed64.Zero,
            GroundState = new GroundCondition()
        });
        return navigator;
    }

    private static NavigationAgentProfile CreateProfile(
        Fixed64 radius = default,
        Fixed64 height = default,
        Fixed64 rootToFootOffset = default,
        TraversalMedia allowedMedia = TraversalMedia.Solid,
        TraversalCapability capabilities = TraversalCapability.None)
    {
        radius = radius == default(Fixed64) ? Fixed64.Quarter : radius;
        height = height == default(Fixed64) ? Fixed64.One : height;
        return new NavigationAgentProfile(
            new KinematicBodyShape(radius, height, rootToFootOffset),
            maxStepUp: Fixed64.Half,
            maxDropDown: Fixed64.One,
            arrivalRadius: Fixed64.Quarter,
            allowedMedia,
            capabilities);
    }

    private Vector3d PublishSingleCell(NavigationCell cell) =>
        PublishSingleCell(new VoxelIndex(4, 4, 4), cell);

    private Vector3d PublishSingleCell(
        VoxelIndex index,
        NavigationCell cell)
    {
        var configuration = new GridConfiguration(
            new Vector3d(-4, -4, -4),
            new Vector3d(8, 8, 8));
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("profile-clearance", binding)
            .AddCell(index, cell)
            .Build();
        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("profile-replacement", 1),
            new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        GuidedPathTestScene.PublishMapAndPolicy(
            _context,
            map,
            bakeVersion: 1,
            OverlayReplacementPolicy.Clear,
            mapSequence: 1,
            policy,
            policySequence: 2);
        return GuidedPathTestScene.Anchor(binding, index);
    }

    private NormalizedGridConfiguration GetDefaultBinding()
    {
        VoxelGrid grid = _context.World.ActiveGrids[0];
        grid.Configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        return binding;
    }

    private void SimulateUntilTerminal(NavigationOperationReceipt receipt)
    {
        for (int frame = 0;
             frame < 1_024 && receipt.Status == NavigationOperationStatus.Pending;
             frame++)
        {
            _context.Simulate();
        }

        receipt.Status.Should().NotBe(
            NavigationOperationStatus.Pending,
            "the admitted profile-replacement fixture operation should finish within its bounded budget");
    }

    private (Vector3d Start, Vector3d End) PublishGraphLine()
    {
        var configuration = new GridConfiguration(
            new Vector3d(-4, -4, -4),
            new Vector3d(8, 8, 8));
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        var startIndex = new VoxelIndex(4, 4, 4);
        var middleIndex = new VoxelIndex(5, 4, 4);
        var endIndex = new VoxelIndex(6, 4, 4);
        var cell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One);
        NavigationMap map = new NavigationMapBuilder("profile-guidance", binding)
            .AddCell(startIndex, cell)
            .AddCell(middleIndex, cell)
            .AddCell(endIndex, cell)
            .Build();
        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("profile-replacement", 1),
            new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        GuidedPathTestScene.PublishMapAndPolicy(
            _context,
            map,
            bakeVersion: 1,
            OverlayReplacementPolicy.Clear,
            mapSequence: 1,
            policy,
            policySequence: 2);
        return (
            GuidedPathTestScene.Anchor(binding, startIndex),
            GuidedPathTestScene.Anchor(binding, endIndex));
    }

    private (Vector3d Start, Vector3d End) PublishTransitionGraph()
    {
        var configuration = new GridConfiguration(
            new Vector3d(-4, -4, -4),
            new Vector3d(8, 8, 8));
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        var sourceIndex = new VoxelIndex(4, 4, 4);
        var destinationIndex = new VoxelIndex(5, 4, 4);
        var sourceCell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        var destinationCell = new NavigationCell(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        var transition = new TraversalTransitionDefinition(
            "profile-climb",
            TraversalTransitionType.Climb,
            sourceIndex,
            TraversalMedium.Solid,
            new NavigationCellAddress("profile-transition", destinationIndex),
            TraversalMedium.Gas,
            actionCost: Fixed64.One,
            locomotionHints: TraversalTransitionLocomotionHints.RequestClimb);
        NavigationMap map = new NavigationMapBuilder("profile-transition", binding)
            .AddCell(sourceIndex, sourceCell)
            .AddCell(destinationIndex, destinationCell)
            .AddTransition(transition)
            .Build();
        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("profile-replacement", 1),
            new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        GuidedPathTestScene.PublishMapAndPolicy(
            _context,
            map,
            bakeVersion: 1,
            OverlayReplacementPolicy.Clear,
            mapSequence: 1,
            policy,
            policySequence: 2);
        return (
            GuidedPathTestScene.Anchor(binding, sourceIndex),
            GuidedPathTestScene.Anchor(binding, destinationIndex));
    }

    private (Vector3d Start, Vector3d End) PublishTwoMapCells()
    {
        var configuration = new GridConfiguration(
            new Vector3d(-4, -4, -4),
            new Vector3d(8, 8, 8));
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        var endConfiguration = new GridConfiguration(
            new Vector3d(8, -4, -4),
            new Vector3d(16, 4, 4));
        _context.World.TryAddGrid(endConfiguration, out _).Should().BeTrue();
        endConfiguration.TryNormalize(out NormalizedGridConfiguration endBinding)
            .Should().BeTrue();
        var startIndex = new VoxelIndex(4, 4, 4);
        var endIndex = new VoxelIndex(4, 4, 4);
        var cell = new NavigationCell(
            TraversalMedia.Solid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.One,
            Fixed64.One);
        NavigationMap startMap = new NavigationMapBuilder("profile-map-a", binding)
            .AddCell(startIndex, cell)
            .Build();
        NavigationMap endMap = new NavigationMapBuilder("profile-map-b", endBinding)
            .AddCell(endIndex, cell)
            .Build();
        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("profile-replacement", 1),
            new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        GuidedPathTestScene.PublishMapAndPolicy(
            _context,
            startMap,
            bakeVersion: 1,
            OverlayReplacementPolicy.Clear,
            mapSequence: 1,
            policy,
            policySequence: 2);
        var endOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(endMap, bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 3,
            effectiveFrame: _context.FrameCount + 1);
        _context.Pathing.Admit(endOperation).Should().BeTrue();
        for (int frame = 0;
             frame < 1_024 && endOperation.Receipt.Status == NavigationOperationStatus.Pending;
             frame++)
        {
            _context.Simulate();
        }
        endOperation.Receipt.Status.Should().Be(
            NavigationOperationStatus.Applied,
            "the second map should publish; rejection was {0}",
            endOperation.Receipt.Rejection);
        return (
            GuidedPathTestScene.Anchor(binding, startIndex),
            GuidedPathTestScene.Anchor(endBinding, endIndex));
    }

    private static PathQuery CreateQuery(
        Vector3d start,
        Vector3d end,
        NavigationAgentProfile profile,
        string mapId) => new(
        new NavigationEndpoint(start, mapId),
        new NavigationEndpoint(end, mapId),
        profile,
        new NavigationAreaPolicyKey("profile-replacement", 1),
        new TraversalIntent(TraversalMedium.Solid, TraversalMedia.Solid),
        PathAlgorithm.AStar,
        new NavigationWorkBudget(64, 64, 64, 64, 64, 0, 0, 0, 0, 0, 0),
        allowTransitions: false);
}

using System;
using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using Gravitas;
using Gravitas.Colliders;
using GridForge.Grids;
using GridForge.Spatial;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;
using static Trailblazer.Gravitas.Tests.Support.GravitasPlanarPhysicsFixture;

namespace Trailblazer.Gravitas.Tests;

public sealed partial class GravitasNavigator2DAcceptedMotionTests
{
    private static readonly Guid LifecycleActorId = new("5648a5c8-d83b-48d6-b4a4-288de8376bb3");

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void Population_ShouldPreserveRejectedMotionAndDiscardItAfterCheckpointRestore(bool memoryPack)
    {
        using var world = new GridWorld();
        using var navigation = TrailblazerWorldContext.Attach(world);
        using var physics = GravitasWorldContext.Attach(world);
        PublishPlanarMap(world, navigation, physics);
        SolidBody2D body = CreateCircle(physics, Vector2d.Zero);
        using var navigator = CreateNavigator(navigation, body, LifecycleActorId, (Fixed64)16);
        object bodyRecord = GravitasRecordSnapshot.Save(body, memoryPack);
        object navigatorRecord = GravitasRecordSnapshot.Save(navigator, memoryPack);
        using var incompatible = CreateNavigator(navigation, body,
            new Guid("5648a5c8-d83b-48d6-b4a4-288de8376bb4"), (Fixed64)16);
        object incompatibleRecord = GravitasRecordSnapshot.Save(incompatible, memoryPack);
        int cellEntries = 0;
        navigator.CommittedCellChanged += _ => cellEntries++;

        PrepareRight(navigation, physics, navigator);
        Action reject = () => GravitasRecordSnapshot.Load(navigator, incompatibleRecord);
        reject.Should().Throw<InvalidOperationException>();
        physics.LateSimulate();
        navigator.CommitFrameMotion();
        navigation.LateSimulate();
        navigator.Pose.Center.Should().Be(Vector2d.Right);
        navigator.Pose.Center.Should().Be(body.Position);
        cellEntries.Should().Be(1);

        PrepareRight(navigation, physics, navigator);
        physics.LateSimulate();
        body.Position.Should().Be(Vector2d.Right * (Fixed64)2);
        GravitasRecordSnapshot.Load(body, bodyRecord);
        GravitasRecordSnapshot.Load(navigator, navigatorRecord);
        Action staleCommit = navigator.CommitFrameMotion;
        staleCommit.Should().Throw<InvalidOperationException>();
        navigator.Pose.Center.Should().Be(Vector2d.Zero);
        body.Agent.Transform.WorldPosition.Should().Be(Vector2d.Zero.ToVector3d(HostY));
        cellEntries.Should().Be(1);
        navigation.LateSimulate();

        PrepareRight(navigation, physics, navigator);
        physics.LateSimulate();
        navigator.CommitFrameMotion();
        navigation.LateSimulate();
        navigator.Pose.Center.Should().Be(Vector2d.Right);
        navigator.Pose.Center.Should().Be(body.Position);
        navigator.Velocity.Should().Be(Vector2d.Right * (Fixed64)16);
        cellEntries.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void TopDownYaw_ShouldReplayIdleTranslationAndClippingAtTheExactAcceptedAngle(bool memoryPack)
    {
        using var world = new GridWorld();
        using var navigation = TrailblazerWorldContext.Attach(world);
        using var physics = GravitasWorldContext.Attach(world);
        PublishPlanarMap(world, navigation, physics);
        AddBox(physics, new Vector2d(2, 0), new Vector2d(1, 4), HostY);

        var facing = new Vector2d(2, 1);
        Fixed64 yaw = FixedMath.Atan2(facing.Y, facing.X);
        var transform = new FixedTransform(Vector2d.Zero.ToVector3d(HostY),
            FixedQuaternion.FromAxisAngle(Vector3d.Up, -yaw), Vector3d.One);
        var body = new SolidBody2D(new GravitasFixtureMatterAgent(physics, transform),
            new LSCircleCollider2D(CircleProfile.Radius))
        {
            Mass = Fixed64.One,
            ContinuousCollisionMode = ContinuousCollisionMode.Continuous,
        };
        body.Initialize(Vector2d.Zero, yaw, PhysicsMobilityType.Kinematic);
        using var navigator = CreateNavigator(navigation, body, LifecycleActorId, (Fixed64)16);

        var idle = new NavigationInput2D(null, false);
        Step(idle, Vector2d.Zero, Vector2d.Zero, yaw);
        object bodyRecord = GravitasRecordSnapshot.Save(body, memoryPack);
        object navigatorRecord = GravitasRecordSnapshot.Save(navigator, memoryPack);
        var entries = new List<NavigationCellAddress?>();
        navigator.CommittedCellChanged += state => entries.Add(state?.Address);

        Replay();
        entries.Should().Equal(Cell(1), Cell(0));
        entries.Clear();
        GravitasRecordSnapshot.Load(body, bodyRecord);
        GravitasRecordSnapshot.Load(navigator, navigatorRecord);
        entries.Should().BeEmpty();
        Replay();
        entries.Should().Equal(Cell(1), Cell(0));

        void Replay()
        {
            for (int i = 0; i < 4; i++)
                Step(idle, Vector2d.Zero, Vector2d.Zero, yaw);
            var right = new NavigationInput2D(Vector2d.Right, false, facing);
            Step(right, Vector2d.Right, Vector2d.Right * (Fixed64)16, yaw);
            Step(right, Vector2d.Right, Vector2d.Zero, yaw);
            Step(idle, Vector2d.Right, Vector2d.Zero, yaw);
            Step(new NavigationInput2D(-Vector2d.Right, false, facing), Vector2d.Zero, -Vector2d.Right * (Fixed64)16, yaw);
            Step(new NavigationInput2D(null, false, Vector2d.Right), Vector2d.Zero, Vector2d.Zero, Fixed64.Zero);
            Step(idle, Vector2d.Zero, Vector2d.Zero, Fixed64.Zero);
        }

        void Step(NavigationInput2D input, Vector2d center, Vector2d velocity, Fixed64 rotation)
        {
            navigation.Simulate();
            physics.Simulate();
            navigator.PrepareFrameMotion(input);
            physics.LateSimulate();
            navigator.CommitFrameMotion();
            navigation.LateSimulate();
            body.Position.Should().Be(center);
            body.Rotation.Should().Be(rotation);
            body.Collider.Rotation.Should().Be(rotation);
            body.Agent.Transform.WorldPosition.Should().Be(center.ToVector3d(HostY));
            navigator.Pose.Should().Be(new NavigationPose2D(center, rotation));
            navigator.Velocity.Should().Be(velocity);
            navigator.LastCommittedCell?.Address.Should().Be(Cell(center == Vector2d.Zero ? 0 : 1));
        }

        static NavigationCellAddress Cell(int x) => new(MapId, new VoxelIndex(x + 16, 0, 16));
    }

    [Fact]
    public void Reset_ShouldRetireThePreparedFrameAndReleaseItsOccupancy()
    {
        using var world = new GridWorld();
        using var navigation = TrailblazerWorldContext.Attach(world);
        using var physics = GravitasWorldContext.Attach(world);
        PublishPlanarMap(world, navigation, physics);
        SolidBody2D body = CreateCircle(physics, Vector2d.Zero);
        using var navigator = CreateNavigator(navigation, body, LifecycleActorId, (Fixed64)16);

        PrepareRight(navigation, physics, navigator);
        physics.LateSimulate();
        navigator.CommitFrameMotion();
        navigation.LateSimulate();
        GridOccupantManager.GetOccupiedIndices(world, navigator).Should().ContainSingle();
        PrepareRight(navigation, physics, navigator);

        navigation.Reset();
        Action staleCommit = navigator.CommitFrameMotion;
        staleCommit.Should().Throw<InvalidOperationException>();
        navigator.Pose.Center.Should().Be(Vector2d.Right);
        body.Position.Should().Be(Vector2d.Right);
        navigator.LastCommittedCell.Should().BeNull();
        GridOccupantManager.GetOccupiedIndices(world, navigator).Should().BeEmpty();
        body.Agent.Transform.WorldPositionXZ.Should().Be(Vector2d.Right * (Fixed64)2);

        // Reset discards Trailblazer's request. The host still owns the staged transform.
        body.Agent.Transform.TrySetWorldPosition(body.Position.ToVector3d(HostY)).Should().BeTrue();
        physics.LateSimulate();
        body.Position.Should().Be(Vector2d.Right);
    }

    [Fact]
    public void FailedPhysicsResolve_ShouldRecoverThePreparedFrameFromTheHostCheckpoint()
    {
        using var world = new GridWorld();
        using var navigation = TrailblazerWorldContext.Attach(world);
        using var physics = GravitasWorldContext.Attach(world);
        PublishPlanarMap(world, navigation, physics);
        SolidBody2D body = CreateCircle(physics, Vector2d.Zero);
        using var navigator = CreateNavigator(navigation, body, LifecycleActorId, (Fixed64)16);
        var triggerCenter = new Vector2d(3, 3);
        var dynamicBody = new SolidBody2D(CreateAgent(physics, triggerCenter, HostY),
            new LSCircleCollider2D(Fixed64.Half)) { Mass = Fixed64.One, GravityScale = Fixed64.Zero };
        dynamicBody.Initialize(triggerCenter);
        var trigger = new LSCircleCollider2D(Fixed64.Half) { IsTrigger = true };
        trigger.InitializeWithNoBody(CreateAgent(physics, triggerCenter, HostY));
        trigger.OnTriggerEnter += _ => throw new InvalidOperationException("Aborted physics resolve.");
        object bodyRecord = GravitasRecordSnapshot.Save(body, memoryPack: false);
        object navigatorRecord = GravitasRecordSnapshot.Save(navigator, memoryPack: false);
        int cellEntries = 0;
        navigator.CommittedCellChanged += _ => cellEntries++;

        PrepareRight(navigation, physics, navigator);
        Action resolve = physics.LateSimulate;
        resolve.Should().Throw<InvalidOperationException>().WithMessage("Aborted physics resolve.");
        body.Position.Should().Be(Vector2d.Right);
        navigator.Pose.Center.Should().Be(Vector2d.Zero);
        cellEntries.Should().Be(0);

        // A failed resolve invalidates the frame even if Gravitas began its distribution pass.
        trigger.Deactivate();
        dynamicBody.Deactivate();
        GravitasRecordSnapshot.Load(body, bodyRecord);
        GravitasRecordSnapshot.Load(navigator, navigatorRecord);
        Action staleCommit = navigator.CommitFrameMotion;
        staleCommit.Should().Throw<InvalidOperationException>();
        navigator.Pose.Center.Should().Be(body.Position);
        body.Position.Should().Be(Vector2d.Zero);
        cellEntries.Should().Be(0);

        PrepareRight(navigation, physics, navigator);
        physics.LateSimulate();
        navigator.CommitFrameMotion();
        navigation.LateSimulate();
        navigator.Pose.Center.Should().Be(Vector2d.Right);
        body.Position.Should().Be(Vector2d.Right);
        cellEntries.Should().Be(1);
    }

    private static void PrepareRight(TrailblazerWorldContext navigation, GravitasWorldContext physics,
        GravitasNavigator2D navigator)
    {
        navigation.Simulate();
        physics.Simulate();
        navigator.PrepareFrameMotion(Vector2d.Right);
    }
}

using System;
using FixedMathSharp;
using FluentAssertions;
using Gravitas;
using Gravitas.Colliders;
using GridForge.Grids;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Navigation;
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

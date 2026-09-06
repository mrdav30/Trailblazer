using System;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Navigation.Motor;
using Xunit;

namespace Trailblazer.Gravitas.Tests;

public sealed class GravitasNavigator3DContactTests
{
    [Fact]
    public void SupportWithoutCarrierIdentity_ShouldGroundWithoutEnablingPlatformCarry()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(
            addWall: false,
            addSupport: true);
        using GravitasNavigatorScenarioFixture.Actor actor = fixture.CreateActor();

        fixture.Step(actor);

        NavMotor motor = actor.Navigator.Motor!;
        motor.IsOnSolid.Should().BeTrue();
        GroundCondition ground = motor.CurrentState.GroundState.GetValueOrDefault();
        ground.SurfaceNormal.Should().Be(Vector3d.Up);
        ground.Platform.Id.Should().Be(0);
        ground.Platform.Inert.Should().BeTrue();
        motor.Handler.Platform.ActivePlatform.Should().BeNull();
    }

    [Fact]
    public void DownwardFacingSweepHit_ShouldRemainAirborneWithoutReplacingLaunchContact()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(
            addWall: false,
            addSupport: true,
            supportCenterY: Fixed64.Zero);
        using GravitasNavigatorScenarioFixture.Actor actor = fixture.CreateActor(
            carrierIdResolver: _ => 3);

        fixture.Step(actor);

        actor.Navigator.Motor!.IsInGas.Should().BeTrue();
        GroundCondition launchContact = actor.Navigator.Motor.CurrentState.GroundState.GetValueOrDefault();
        launchContact.SurfaceNormal.Should().Be(Vector3d.Up);
        launchContact.Platform.Inert.Should().BeTrue();
        actor.Navigator.Motor.Handler.Platform.ActivePlatform.Should().BeNull();
    }

    [Fact]
    public void NegativeCarrierIdentity_ShouldRejectTheResolvedContactBeforeCommit()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(
            addWall: false,
            addSupport: true);
        using GravitasNavigatorScenarioFixture.Actor actor = fixture.CreateActor(
            carrierIdResolver: _ => -1);
        fixture.Prepare(actor);
        fixture.Gravitas.LateSimulate();

        Action commit = actor.Navigator.CommitFrameMotion;

        commit.Should().Throw<ArgumentException>();
        actor.Navigator.Motor!.TraversalInProgress.Should().BeTrue();
        actor.Navigator.Reset();
        actor.Navigator.Motor.TraversalInProgress.Should().BeFalse();
    }

    [Fact]
    public void SupportSweep_ShouldPublishTheResolvedGroundContactAndCarrier()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(
            addWall: false,
            addSupport: true);
        using GravitasNavigatorScenarioFixture.Actor actor = fixture.CreateActor(
            carrierIdResolver: _ => 7);

        fixture.Step(actor);

        actor.Navigator.Motor!.IsOnSolid.Should().BeTrue();
        GroundCondition ground = actor.Navigator.Motor.CurrentState.GroundState.GetValueOrDefault();
        ground.SurfaceNormal.Should().Be(Vector3d.Up);
        ground.SurfaceFriction.Should().Be(Fixed64.Quarter);
        ground.Platform.Id.Should().Be(7);
        PlatformSnapshot platform = actor.Navigator.Motor.Handler.Platform.ActivePlatform.GetValueOrDefault();
        platform.Id.Should().Be(7);
        platform.SupportsKinematicMotion.Should().BeTrue();
    }

    [Fact]
    public void MovingSupport_ShouldApplyPlatformTranslationBeforeTheAcceptedRotation()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(
            addWall: false,
            addSupport: true);
        using GravitasNavigatorScenarioFixture.Actor actor = fixture.CreateActor(
            carrierIdResolver: _ => 11);
        fixture.Step(actor);

        Vector3d initialRoot = actor.Navigator.Position;
        Vector3d supportLocalRoot = fixture.SupportTransform!.InverseTransformPoint(initialRoot);
        FixedQuaternion supportLocalRotation =
            fixture.SupportTransform.WorldRotation.Inverse() * actor.Navigator.Rotation;

        FixedQuaternion platformRotation = FixedQuaternion.FromAxisAngle(
            Vector3d.Up,
            Fixed64.PiOver4);
        fixture.SupportTransform.LocalPosition += Vector3d.Right * Fixed64.Quarter;
        fixture.SupportTransform.LocalRotation = platformRotation;

        Vector3d expectedRoot = fixture.SupportTransform.TransformPoint(supportLocalRoot);
        FixedQuaternion expectedRotation = (
            fixture.SupportTransform.WorldRotation * supportLocalRotation).Normalized;
        fixture.SupportTransform.TryGetLocalToWorldMatrix(out Fixed4x4 expectedSupportTransform)
            .Should().BeTrue();

        fixture.Step(actor);

        PlatformSnapshot refreshed = actor.Navigator.Motor!.Handler.Platform.ActivePlatform.GetValueOrDefault();
        refreshed.Transform.Should().Be(expectedSupportTransform);
        actor.Navigator.Motor.CurrentState.SurfaceLevel.Should().Be(Fixed64.FromFraction(-1, 2));

        fixture.Step(actor);

        actor.Navigator.Position.Should().NotBe(initialRoot);
        actor.Navigator.Position.Should().Be(expectedRoot);
        actor.Body.Position3d.Should().Be(expectedRoot);
        FixedQuaternion.Angle(actor.Navigator.Rotation, expectedRotation)
            .Should().BeLessThan(Fixed64.FromFraction(1, 1000));
        actor.Navigator.Rotation.Should().Be(actor.Body.Rotation);

        FixedQuaternion acceptedRotation = actor.Navigator.Rotation;
        fixture.Step(actor);
        fixture.Step(actor);

        actor.Navigator.Turning!.TargetReached.Should().BeTrue();
        FixedQuaternion.Angle(actor.Navigator.Rotation, acceptedRotation)
            .Should().BeLessThan(Fixed64.FromFraction(1, 1000));
    }

    [Fact]
    public void SupportedInputTurn_ShouldCommitTheTurnedPoseAndRefreshContact()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(
            addWall: false,
            addSupport: true);
        using GravitasNavigatorScenarioFixture.Actor actor = fixture.CreateActor(
            rotation: FixedQuaternion.Identity,
            carrierIdResolver: _ => 13);
        fixture.Step(actor);

        actor.Navigator.ApplyInputTrekRequest(facingDirection: Vector3d.Right);
        fixture.Prepare(actor);
        FixedQuaternion requestedRotation = actor.Body.RotationTransform.WorldRotation;
        requestedRotation.Should().NotBe(FixedQuaternion.Identity);

        fixture.Resolve(actor);

        actor.Navigator.Rotation.Should().Be(requestedRotation);
        actor.Body.Rotation.Should().Be(requestedRotation);
        GroundCondition ground = actor.Navigator.Motor!.CurrentState.GroundState.GetValueOrDefault();
        ground.Platform.Id.Should().Be(13);
        ground.SurfaceNormal.Should().Be(Vector3d.Up);
    }
}

using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Gravitas.Tests;

public sealed class GravitasNavigator3DCollisionTurningTests
{
    [Fact]
    public void PartialWallAcceptance_ShouldDriveCollisionTurnOnTheFollowingIdleFrames()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(
            wallCenterX: Fixed64.FromFraction(515, 256));
        using GravitasNavigatorScenarioFixture.Actor actor = fixture.CreateActor(
            rotation: FixedQuaternion.Identity);
        actor.Navigator.Motor!.Handler.Move.MaxSidewaysSpeed = Fixed64.Half;

        int divergenceFrame = -1;
        Vector3d requested = Vector3d.Zero;
        for (int frame = 0; frame < 128; frame++)
        {
            actor.Navigator.ApplyInputTrekRequest(
                Vector3d.Right,
                TrekRate.Fast,
                facingDirection: Vector3d.Forward);
            fixture.Prepare(actor);
            requested = actor.Body.PositionTransform.LocalPosition;
            fixture.Resolve(actor);

            if (requested != actor.Body.Position3d)
            {
                divergenceFrame = frame;
                break;
            }
        }

        divergenceFrame.Should().Be(88, "the accepted root ended at {0}", actor.Navigator.Position);
        requested.Should().Be(new Vector3d(
            Fixed64.FromFraction(89, 64),
            Fixed64.Zero,
            Fixed64.Zero));
        actor.Navigator.Position.Should().Be(new Vector3d(
            Fixed64.FromFraction(355, 256),
            Fixed64.Zero,
            Fixed64.Zero));
        actor.Navigator.Velocity.Should().Be(
            Vector3d.Right * Fixed64.FromFraction(3, 8));
        actor.Navigator.Velocity.Should().Be(
            (actor.Navigator.Position - actor.Navigator.LastPosition)
            * fixture.Trailblazer.InvDeltaTime);
        actor.Navigator.Rotation.Should().Be(FixedQuaternion.Identity);
        actor.Navigator.Turning!.TargetReached.Should().BeTrue();
        actor.Navigator.Turning.TargetRotation.Should().Be(FixedQuaternion.Identity);

        actor.Navigator.ApplyInputTrekRequest();
        fixture.Step(actor);

        actor.Navigator.Turning.TargetReached.Should().BeTrue();
        actor.Navigator.Turning.TargetRotation.Should().Be(FixedQuaternion.Identity);
        actor.Navigator.Rotation.Should().Be(FixedQuaternion.Identity);

        actor.Navigator.ApplyInputTrekRequest();
        fixture.Step(actor);

        actor.Navigator.Turning.TargetReached.Should().BeFalse();
        actor.Navigator.Turning.TargetRotation.Should().Be(
            FixedQuaternion.FromDirection(Vector3d.Right));
        actor.Navigator.Rotation.Should().NotBe(FixedQuaternion.Identity);
        actor.Navigator.Rotation.Should().Be(actor.Body.Rotation);
    }
}

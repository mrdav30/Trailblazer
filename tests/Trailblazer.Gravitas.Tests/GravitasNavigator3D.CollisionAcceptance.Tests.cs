using FixedMathSharp;
using FluentAssertions;
using Gravitas.Colliders;
using Gravitas.Support;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Navigation;
using Xunit;

namespace Trailblazer.Gravitas.Tests;

public sealed class GravitasNavigator3DCollisionAcceptanceTests
{
    private readonly ITestOutputHelper _output;

    public GravitasNavigator3DCollisionAcceptanceTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(1)]
    [InlineData(-1)]
    public void SlopedMovement_ShouldFollowTheSurfaceWithRepeatableAcceptedMotion(int direction)
    {
        WalkSlope(direction).Should().Equal(WalkSlope(direction));
    }

    private Vector3d[] WalkSlope(int direction)
    {
        _output.WriteLine($"slope direction={direction}: navigator-request|host-request|accepted|velocity|normal");
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false);
        FixedQuaternion tilt = FixedQuaternion.FromAxisAngle(Vector3d.Forward, Fixed64.Pi / (Fixed64)8);
        Vector3d normal = tilt * Vector3d.Up;
        LSCuboidCollider slope = fixture.AddBox(Vector3d.Down * Fixed64.Half,
            new Vector3d(4, 1, 4), tilt, layer: 0);
        Vector3d surfacePoint = slope.Transform.TransformPoint(Vector3d.Up * Fixed64.Half);
        using GravitasNavigatorActor actor = fixture.CreateActor(
            position: surfacePoint + normal * (Fixed64.Half + Fixed64.FromRaw(32)));
        actor.Body.Collider.IgnoredCollisionLayers = PhysicsLayerMask.None;
        fixture.Step(actor);
        Vector3d start = actor.Navigator.Position;
        var trace = new Vector3d[64];
        for (int frame = 0; frame < 32; frame++)
        {
            actor.Navigator.ApplyInputTrekRequest(direction: Vector3d.Right * (Fixed64)direction, rate: TrekRate.Fast);
            fixture.Step(actor);
            actor.Navigator.Position.Should().Be(actor.Body.Position3d);
            actor.Navigator.Motor!.IsOnSolid.Should().BeTrue();
            trace[frame * 2] = actor.Navigator.Position;
            trace[frame * 2 + 1] = actor.Navigator.Velocity;
            Vector3d acceptedNormal = actor.Navigator.Motor.CurrentState.GroundState!.Value.SurfaceNormal;
            (acceptedNormal - normal).Magnitude.Should().BeLessThan(Fixed64.FromFraction(1, 1000));
            GravitasMotionTrace.WriteFrame(_output, frame,
                actor.NavigatorRequestedPosition, actor.HostRequestedPosition,
                actor.Navigator.Position, actor.Navigator.Velocity, acceptedNormal);
        }
        ((actor.Navigator.Position.X - start.X) * (Fixed64)direction).Should().BeGreaterThan(Fixed64.FromFraction(1, 8),
            "start {0}, requested {1}, accepted {2}, signed slope {3}, normal {4}", start,
            actor.NavigatorRequestedPosition, actor.Body.Position3d, actor.Navigator.Motor!.FrameSlopeAngle,
            actor.Navigator.Motor.CurrentState.SurfaceNormal);
        ((actor.Navigator.Position.Y - start.Y) * (Fixed64)direction).Should().BeGreaterThan(Fixed64.Zero);
        return trace;
    }

    [Theory]
    [InlineData(64)]
    [InlineData(1)]
    public void ThinWall_ShouldStopLowAndHighSpeedMotionAndAllowRetreat(int denominator)
    {
        _output.WriteLine($"thin wall denominator={denominator}: navigator-request|host-request|accepted|velocity");
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false);
        // Near face 9/16, radius 1/2: exactly 1/16 of admissible travel.
        fixture.AddBox(
            new Vector3d(Fixed64.FromFraction(37, 64), Fixed64.Zero, Fixed64.Zero),
            new Vector3d(Fixed64.FromFraction(1, 32), (Fixed64)4, (Fixed64)4));
        using GravitasNavigatorActor actor = fixture.CreateActor();
        Vector3d start = actor.Navigator.Position;
        Fixed64 step = Fixed64.One / (Fixed64)denominator;
        for (int frame = 0; frame < 12; frame++)
        {
            actor.Navigator.AddLocomotionDisplacement(Vector3d.Right * step);
            fixture.Step(actor);
            actor.Navigator.Position.Should().Be(actor.Body.Position3d);
            actor.Navigator.Position.X.Should().BeLessThanOrEqualTo(Fixed64.FromFraction(1, 16));
            GravitasMotionTrace.WriteFrame(_output, frame,
                actor.NavigatorRequestedPosition, actor.HostRequestedPosition,
                actor.Navigator.Position, actor.Navigator.Velocity);
        }

        actor.Navigator.Position.Should().Be(start + Vector3d.Right * Fixed64.FromFraction(1, 16));
        actor.Navigator.Velocity.Should().Be(Vector3d.Zero);
        actor.Navigator.AddLocomotionDisplacement(Vector3d.Left * Fixed64.Quarter);
        fixture.Step(actor);
        actor.Navigator.Position.Should().Be(start + Vector3d.Left * Fixed64.FromFraction(3, 16));
        actor.Navigator.Velocity.Should().Be(Vector3d.Left * (Fixed64)8);
    }

    [Fact]
    public void Corner_ShouldStopInwardMotionButAcceptAnExplicitTangentThenRetreat()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false, addSupport: true);
        fixture.AddBox(new Vector3d(1, 0, 0), new Vector3d(1, 4, 4));
        fixture.AddBox(new Vector3d(0, 0, 1), new Vector3d(4, 4, 1));
        using GravitasNavigatorActor actor = fixture.CreateActor();
        Vector3d start = actor.Navigator.Position;
        for (int frame = 0; frame < 8; frame++)
        {
            actor.Navigator.AddLocomotionDisplacement((Vector3d.Right + Vector3d.Forward) / 64);
            fixture.Step(actor);
            actor.Navigator.Position.Should().Be(start);
            actor.Navigator.Velocity.Should().Be(Vector3d.Zero);
        }

        // The bridge does not invent wall-slide motion: the host explicitly
        // requests a tangent away from the second wall, then leaves the corner.
        actor.Navigator.AddLocomotionDisplacement(Vector3d.Backward * Fixed64.Quarter);
        fixture.Step(actor);
        actor.Navigator.Position.Should().Be(start + Vector3d.Backward * Fixed64.Quarter);
        actor.Navigator.AddLocomotionDisplacement(Vector3d.Left * Fixed64.Quarter);
        fixture.Step(actor);
        // The previous 8 units/s tangent keeps 3/4 of its speed on this
        // surface (friction 1/4), contributing a further 3/16 this frame.
        actor.Navigator.Position.Should().Be(start + new Vector3d(
            -Fixed64.Quarter, Fixed64.Zero, Fixed64.FromFraction(-7, 16)));
    }

    [Fact]
    public void SlopedSupport_ShouldPublishThePhysicalNormalWithoutChangingTheAcceptedPose()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false);
        FixedQuaternion tilt = FixedQuaternion.FromAxisAngle(Vector3d.Forward, Fixed64.PiOver4);
        Vector3d normal = tilt * Vector3d.Up;
        LSCuboidCollider slope = fixture.AddBox(
            Vector3d.Down * Fixed64.Half,
            new Vector3d((Fixed64)4, Fixed64.Half, (Fixed64)4), tilt, layer: 0);
        Vector3d point = slope.Transform.TransformPoint(Vector3d.Up * Fixed64.Quarter);
        using GravitasNavigatorActor actor = fixture.CreateActor(
            position: point + normal * Fixed64.Half);
        Vector3d accepted = actor.Body.Position3d;

        fixture.Step(actor);

        actor.Navigator.Position.Should().Be(accepted);
        actor.Navigator.Position.Should().Be(actor.Body.Position3d);
        actor.Navigator.Motor!.IsOnSolid.Should().BeTrue();
        // Quaternion admission and query normalization each round in Q32.32.
        // Bound the analytic 45-degree normal to 16 raw units per component.
        Vector3d actualNormal = actor.Navigator.Motor.CurrentState.GroundState!.Value.SurfaceNormal;
        Fixed64 tolerance = Fixed64.FromRaw(16);
        FixedMath.Abs(actualNormal.X - normal.X).Should().BeLessThanOrEqualTo(tolerance);
        FixedMath.Abs(actualNormal.Y - normal.Y).Should().BeLessThanOrEqualTo(tolerance);
        actualNormal.Z.Should().Be(Fixed64.Zero);
    }
}

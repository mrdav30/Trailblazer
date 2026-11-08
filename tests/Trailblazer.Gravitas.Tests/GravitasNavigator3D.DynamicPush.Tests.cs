using FixedMathSharp;
using FluentAssertions;
using Gravitas;
using Gravitas.Colliders;
using Gravitas.Materials;
using Gravitas.Support;
using Trailblazer.Gravitas.Tests.Support;
using Xunit;

namespace Trailblazer.Gravitas.Tests;

public sealed class GravitasNavigator3DDynamicPushTests
{
    private readonly ITestOutputHelper _output;

    public GravitasNavigator3DDynamicPushTests(ITestOutputHelper output) => _output = output;

    [Theory]
    [InlineData(64)]
    [InlineData(1)]
    public void KinematicPush_ShouldMoveTheDynamicBodyAndReplayExactly(int denominator)
    {
        Vector3d[] first = Push(denominator);
        Vector3d[] second = Push(denominator);
        second.Should().Equal(first);
    }

    private Vector3d[] Push(int denominator)
    {
        _output.WriteLine($"push denominator={denominator}: navigator-request|host-request|accepted|velocity|dynamic-position|dynamic-velocity");
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false);
        using GravitasNavigatorActor actor = fixture.CreateActor();
        var start = new Vector3d(Fixed64.FromFraction(13, 16), Fixed64.Zero, Fixed64.Zero);
        var collider = new LSSphereCollider
        {
            Radius = Fixed64.Quarter,
            Layer = new PhysicsLayer(1),
            Material = PhysicsMaterial.Frictionless
        };
        var body = new SolidBody(new GravitasFixtureMatterAgent(fixture.Gravitas,
            new FixedTransform(start, FixedQuaternion.Identity, Vector3d.One)), collider)
        {
            GravityScale = Fixed64.Zero,
            Mass = Fixed64.One,
            LinearDragCoefficient = Fixed64.Zero,
            ContinuousCollisionMode = ContinuousCollisionMode.Continuous,
            FreezeAxes = BodyFreezeAxes3D.Rotation
        };
        body.UseManualGrounding();
        body.Initialize(start, FixedQuaternion.Identity, PhysicsMobilityType.Dynamic);
        try
        {
            var frames = new Vector3d[8 * 4];
            Fixed64 displacement = Fixed64.One / (Fixed64)denominator;
            for (int frame = 0; frame < 8; frame++)
            {
                // One explicit locomotion displacement; subsequent motor
                // inertia is part of the accepted-motion replay, not ignored.
                if (frame == 0)
                    actor.Navigator.AddLocomotionDisplacement(Vector3d.Right * displacement);
                fixture.Step(actor);
                actor.Navigator.Position.Should().Be(actor.Body.Position3d);
                actor.Navigator.Velocity.Should().Be(
                    (actor.Navigator.Position - actor.Navigator.LastPosition) * (Fixed64)32);
                frames[frame * 4] = actor.Navigator.Position;
                frames[frame * 4 + 1] = actor.Navigator.Velocity;
                frames[frame * 4 + 2] = body.Position3d;
                frames[frame * 4 + 3] = body.LinearVelocity;
                GravitasMotionTrace.WriteFrame(_output, frame,
                    actor.NavigatorRequestedPosition, actor.HostRequestedPosition,
                    actor.Navigator.Position, actor.Navigator.Velocity, body.Position3d, body.LinearVelocity);
            }
            body.Position3d.X.Should().BeGreaterThan(start.X);
            body.LinearVelocity.X.Should().BeGreaterThan(Fixed64.Zero);
            body.Position3d.Y.Should().Be(Fixed64.Zero);
            body.Position3d.Z.Should().Be(Fixed64.Zero);
            return frames;
        }
        finally
        {
            body.Deactivate();
        }
    }
}

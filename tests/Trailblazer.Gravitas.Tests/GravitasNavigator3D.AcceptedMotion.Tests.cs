using FixedMathSharp;
using FluentAssertions;
using GridForge.Spatial;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Gravitas.Tests;

public sealed class GravitasNavigator3DAcceptedMotionTests
{
    [Fact]
    public void UnobstructedMotion_ShouldCommitTheCompleteResolvedPath()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false);
        using GravitasNavigatorScenarioFixture.Actor actor = fixture.CreateActor();
        actor.Navigator.ApplyGuidedTrekRequest(fixture.CreateQuery(), TrekRate.Fast);

        for (int frame = 0; frame < 128; frame++)
        {
            fixture.Step(actor);
            actor.Navigator.Position.Should().Be(actor.Body.Position3d);
        }

        Vector3d endRoot = fixture.EndFoot + Vector3d.Up * Fixed64.Half;
        actor.Navigator.Position.Should().Be(endRoot);
        actor.Navigator.Velocity.Should().Be(new Vector3d(Fixed64.Half, Fixed64.Zero, Fixed64.Zero));
        actor.Navigator.LastCommittedCell!.Value.Address.Should().Be(
            new NavigationCellAddress("gravitas-wall", new VoxelIndex(6, 4, 4)));
    }

    [Fact]
    public void FullyBlockedMotion_ShouldCommitNoDisplacement()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(
            wallCenterX: Fixed64.FromFraction(5, 8));
        using GravitasNavigatorScenarioFixture.Actor actor = fixture.CreateActor();
        actor.Navigator.ApplyGuidedTrekRequest(fixture.CreateQuery(), TrekRate.Fast);

        fixture.Step(actor);

        actor.Body.Position3d.Should().Be(fixture.StartRoot);
        actor.Navigator.Position.Should().Be(fixture.StartRoot);
        actor.Navigator.LastPosition.Should().Be(fixture.StartRoot);
        actor.Navigator.Velocity.Should().Be(Vector3d.Zero);
        actor.Navigator.LastCommittedCell!.Value.Address.Should().Be(
            new NavigationCellAddress("gravitas-wall", new VoxelIndex(4, 4, 4)));
        actor.Navigator.Motor!.TraversalInProgress.Should().BeFalse();
    }

    [Fact]
    public void WallCollision_ShouldCommitOnlyThePoseAcceptedByGravitas()
    {
        using var fixture = new GravitasNavigatorScenarioFixture();
        using GravitasNavigatorScenarioFixture.Actor actor = fixture.CreateActor();
        actor.Navigator.ApplyGuidedTrekRequest(fixture.CreateQuery(), TrekRate.Fast);

        actor.Body.Position3d.Should().Be(fixture.StartRoot);
        actor.Navigator.Position.Should().Be(fixture.StartRoot);
        int firstDivergence = -1;
        Vector3d firstRequested = Vector3d.Zero;
        Vector3d firstAccepted = Vector3d.Zero;
        for (int frame = 0; frame < 120; frame++)
        {
            fixture.Prepare(actor);
            Vector3d requested = actor.Body.PositionTransform.LocalPosition;
            fixture.Resolve(actor);
            if (firstDivergence < 0 && requested != actor.Body.Position3d)
            {
                firstDivergence = frame;
                firstRequested = requested;
                firstAccepted = actor.Body.Position3d;
            }

            actor.Navigator.Position.Should().Be(actor.Body.Position3d);
            actor.Navigator.Rotation.Should().Be(actor.Body.Rotation);
        }

        firstDivergence.Should().Be(88, "requested {0} but Gravitas accepted {1}", firstRequested, firstAccepted);
        Vector3d accepted = new(
            Fixed64.FromFraction(11, 8),
            Fixed64.Zero,
            Fixed64.Zero);
        actor.Body.Position3d.Should().Be(accepted);
        actor.Navigator.Position.Should().Be(accepted);
        actor.Navigator.Rotation.Should().Be(actor.Body.Rotation);
        actor.Navigator.Velocity.Should().Be(
            (actor.Navigator.Position - actor.Navigator.LastPosition) * fixture.Trailblazer.InvDeltaTime);
        actor.Navigator.LastCommittedCell.Should().NotBeNull();
        actor.Navigator.LastCommittedCell!.Value.Address.Should().Be(
            new NavigationCellAddress("gravitas-wall", new VoxelIndex(5, 4, 4)));
        actor.Navigator.Motor!.IsInGas.Should().BeTrue();
        actor.Navigator.Motor.TraversalInProgress.Should().BeFalse();
    }
}

using System;
using FixedMathSharp;
using FluentAssertions;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Gravitas.Tests;

public sealed class GravitasNavigator3DRestoreLifecycleTests
{
    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void SuccessfulPopulation_ShouldDiscardPreparedMotionBeforeTheNextFrame(bool memoryPack)
    {
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false);
        using GravitasNavigatorActor actor = fixture.CreateActor();
        object body = GravitasRecordSnapshot.Save(actor.Body, memoryPack);
        object navigator = GravitasRecordSnapshot.Save(actor.Navigator, memoryPack);
        Vector3d checkpoint = actor.Navigator.Position;
        actor.Navigator.AddLocomotionDisplacement(Vector3d.Right * Fixed64.Quarter);
        fixture.Prepare(actor);
        fixture.Gravitas.LateSimulate();
        actor.Body.Position3d.Should().NotBe(checkpoint);

        GravitasRecordSnapshot.Load(actor.Body, body);
        GravitasRecordSnapshot.Load(actor.Navigator, navigator);

        Action discardedCommit = actor.Navigator.CommitFrameMotion;
        discardedCommit.Should().Throw<InvalidOperationException>();
        actor.Navigator.Position.Should().Be(checkpoint);
        actor.Navigator.Motor!.TraversalInProgress.Should().BeFalse();
        fixture.Step(actor);
        actor.Navigator.Position.Should().Be(checkpoint);
        actor.Navigator.Position.Should().Be(actor.Body.Position3d);
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void RejectedPopulation_ShouldPreserveThePreparedMotionAndAllowItsCommit(bool memoryPack)
    {
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false);
        using GravitasNavigatorActor actor = fixture.CreateActor();
        var different = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Quarter, Fixed64.Half, Fixed64.Quarter),
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, TraversalMedia.Solid, TraversalCapability.None);
        object incompatible;
        using (GravitasNavigatorActor other = fixture.CreateActor(profile: different))
            incompatible = GravitasRecordSnapshot.Save(other.Navigator, memoryPack);
        Vector3d checkpoint = actor.Navigator.Position;
        actor.Navigator.AddLocomotionDisplacement(Vector3d.Right * Fixed64.Quarter);
        fixture.Prepare(actor);

        Action load = () => GravitasRecordSnapshot.Load(actor.Navigator, incompatible);
        load.Should().Throw<InvalidOperationException>();
        actor.Navigator.Motor!.TraversalInProgress.Should().BeTrue();
        fixture.Resolve(actor);
        actor.Navigator.Position.Should().Be(checkpoint + Vector3d.Right * Fixed64.Quarter);
        actor.Navigator.Position.Should().Be(actor.Body.Position3d);
    }
}

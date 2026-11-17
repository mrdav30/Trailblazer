using System;
using FixedMathSharp;
using Gravitas.Queries;
using GridForge.Configuration;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Gravitas.Tests;

public sealed class GravitasNavigator3DSupportBudgetTests
{
    private static readonly PhysicsQueryBudget Ample = new(8192, 8192, 8192);

    [Fact]
    public void BoundedSupport_ShouldCommitTheBatchAndReleaseTheCarrierDuringRepeatedExhaustion()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false, addSupport: true);
        using GravitasNavigatorActor actor = fixture.CreateActor(carrierIdResolver: _ => 71);
        using GravitasNavigatorActor other = fixture.CreateActor(fixture.EndRoot);
        var navigator = actor.Navigator;
        var scratch = new PhysicsQueryScratch(128, 16);
        Assert.Equal(NavigationSupportStatus.NotApplicable, navigator.SupportStatus);
        Assert.Throws<ArgumentNullException>(() => navigator.ConfigureSupportQueryBudget(Ample, null!));
        navigator.ConfigureSupportQueryBudget(Ample, scratch);
        other.Navigator.ConfigureSupportQueryBudget(Ample, scratch);
        Assert.True(navigator.Motor!.TryTraversal(new TrekRequest(), out _, out _, out _));
        Assert.Throws<InvalidOperationException>(() => navigator.ConfigureSupportQueryBudget(default, scratch));
        navigator.Motor.AbortTraversalFrame();
        fixture.Prepare(actor, other);
        Assert.Throws<InvalidOperationException>(() => navigator.ConfigureSupportQueryBudget(default, scratch));
        Assert.Throws<InvalidOperationException>(navigator.ClearSupportQueryBudget);
        fixture.Resolve(actor, other);
        Assert.Equal(NavigationSupportStatus.Found, navigator.SupportStatus);
        Assert.Equal(NavigationSupportStatus.Found, other.Navigator.SupportStatus);
        Assert.Equal(PhysicsQueryExecutionStatus.Completed, navigator.LastSupportQueryExecutionStatus);
        PhysicsQueryWork work = Assert.IsType<PhysicsQueryWork>(navigator.LastSupportQueryWork);
        Assert.True(work.Geometry > 0);
        Assert.Equal(71, navigator.Motor!.Handler.Platform.ActivePlatform!.Value.Id);

        navigator.ConfigureSupportQueryBudget(new PhysicsQueryBudget(work.Discovery, work.Candidates, work.Geometry), scratch);
        fixture.Step(actor, other);
        Assert.Equal(NavigationSupportStatus.Found, navigator.SupportStatus);
        Assert.Equal(work, navigator.LastSupportQueryWork);

        fixture.SupportTransform!.LocalPosition += Vector3d.Right * Fixed64.Quarter;
        navigator.ConfigureSupportQueryBudget(new PhysicsQueryBudget(Ample.DiscoveryLimit, Ample.CandidateLimit, work.Geometry - 1), scratch);
        Assert.Null(navigator.LastSupportQueryExecutionStatus);
        Assert.Null(navigator.LastSupportQueryWork);
        Assert.Equal(71, navigator.Motor.Handler.Platform.ActivePlatform!.Value.Id);
        navigator.Motor.Handler.Forces.GravityForce = Fixed64.One;
        for (int frame = 0; frame < 3; frame++)
        {
            Vector3d previous = navigator.Position;
            fixture.Step(actor, other);
            Assert.Equal(actor.Body.Position3d, navigator.Position);
            Assert.Equal(previous.X, navigator.Position.X);
            if (frame > 0)
                Assert.True(navigator.Position.Y < previous.Y);

            Assert.Equal(NavigationSupportStatus.Unavailable, navigator.SupportStatus);
            Assert.Equal(PhysicsQueryExecutionStatus.WorkBudgetExceeded, navigator.LastSupportQueryExecutionStatus);
            Assert.InRange(navigator.LastSupportQueryWork!.Value.Geometry, 1UL, work.Geometry - 1);
            Assert.True(navigator.Motor.IsInGas);
            Assert.Null(navigator.Motor.CurrentState.GroundState);
            Assert.Null(navigator.Motor.Handler.Platform.ActivePlatform);
            Assert.False(navigator.Motor.TraversalInProgress);
            Assert.Equal(NavigationSupportStatus.Found, other.Navigator.SupportStatus);
            Assert.Equal(other.Body.Position3d, other.Navigator.Position);
        }

        navigator.ConfigureSupportQueryBudget(Ample, scratch);
        fixture.Step(actor, other);
        Assert.Equal(NavigationSupportStatus.Found, navigator.SupportStatus);
        Assert.Equal(PhysicsQueryExecutionStatus.Completed, navigator.LastSupportQueryExecutionStatus);
        Assert.Equal(71, navigator.Motor.Handler.Platform.ActivePlatform!.Value.Id);

        fixture.DeactivateSupport();
        fixture.Step(actor, other);
        Assert.Equal(NavigationSupportStatus.NoSupport, navigator.SupportStatus);
        Assert.Equal(PhysicsQueryExecutionStatus.Completed, navigator.LastSupportQueryExecutionStatus);
        Assert.True(navigator.Motor.IsInGas);

        navigator.ClearSupportQueryBudget();
        Assert.Null(navigator.LastSupportQueryExecutionStatus);
        Assert.Null(navigator.LastSupportQueryWork);
        fixture.Step(actor, other);
        Assert.Equal(NavigationSupportStatus.NoSupport, navigator.SupportStatus);
        Assert.Null(navigator.LastSupportQueryExecutionStatus);
        Assert.Null(navigator.LastSupportQueryWork);

        other.Navigator.Reset();
        Assert.Equal(NavigationSupportStatus.NotApplicable, other.Navigator.SupportStatus);
        Assert.Null(other.Navigator.LastSupportQueryExecutionStatus);
        Assert.Null(other.Navigator.LastSupportQueryWork);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundedSupport_ShouldRejectUnrepresentableProbeInputsAndCommitTheAcceptedPose(bool footUnderflows)
    {
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false);
        var position = new Vector3d(Fixed64.Half, footUnderflows ? -Fixed64.One : Fixed64.Two, Fixed64.Half);
        var profile = fixture.Profile;
        if (footUnderflows)
        {
            // The two offsets cancel, but the intermediate world foot lies below the scalar range.
            var shape = new KinematicBodyShape(profile.Shape.Radius, profile.Shape.Height, Fixed64.MaxValue);
            profile = new NavigationAgentProfile(shape, profile.MaxStepUp, profile.MaxDropDown,
                profile.AllowedMedia, profile.Capabilities);
        }
        using GravitasNavigatorActor actor = fixture.CreateActor(position, profile: profile, supportProbeOffset: Fixed64.MaxValue);
        actor.Navigator.ConfigureSupportQueryBudget(Ample, new PhysicsQueryScratch(128, 16));

        fixture.Step(actor);

        Assert.Equal(position, actor.Body.Position3d);
        Assert.Equal(actor.Body.Position3d, actor.Navigator.Position);
        Assert.Equal(NavigationSupportStatus.Unavailable, actor.Navigator.SupportStatus);
        Assert.Equal(PhysicsQueryExecutionStatus.Unrepresentable, actor.Navigator.LastSupportQueryExecutionStatus);
        Assert.Equal(default(PhysicsQueryWork), actor.Navigator.LastSupportQueryWork);
    }

    [Fact]
    public void SteeringCallback_ShouldNotChangeTheSupportPolicyDuringPrepare()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false, addSupport: true);
        using GravitasNavigatorActor actor = fixture.CreateActor();
        var navigator = actor.Navigator;
        navigator.ConfigureSupportQueryBudget(Ample, new PhysicsQueryScratch(128, 16));
        navigator.Steering!.CanPathfind = false;
        int invalidPaths = 0;
        navigator.Steering.Events.OnInvalidPath += () =>
        {
            Assert.False(navigator.Motor!.TraversalInProgress);
            Assert.Throws<InvalidOperationException>(navigator.Simulate);
            Assert.Throws<InvalidOperationException>(navigator.ClearSupportQueryBudget);
            invalidPaths++;
        };
        navigator.ApplyGuidedTrekRequest(fixture.CreateQuery());

        fixture.Step(actor);

        Assert.Equal(1, invalidPaths);
        Assert.Equal(PhysicsQueryExecutionStatus.Completed, navigator.LastSupportQueryExecutionStatus);
        Assert.Equal(NavigationSupportStatus.Found, navigator.SupportStatus);
        Assert.Equal(actor.Body.Position3d, navigator.Position);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnmaterializableNativeWitness_ShouldNeverGroundTheNavigator(bool bounded)
    {
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false, addNavigation: false);
        Fixed64 edge = (Fixed64)int.MaxValue;
        Fixed64 span = Fixed64.Two;
        Assert.True(fixture.Trailblazer.World.TryAddGrid(new GridConfiguration(
            new Vector3d(-span, edge - (Fixed64)4, -span), new Vector3d(span, edge, span)), out _));
        var box = fixture.AddBox(new Vector3d(Fixed64.Zero, Fixed64.MaxValue - Fixed64.Two, Fixed64.Zero),
            new Vector3d(8, 6, 8), layer: 0);
        var root = new Vector3d(Fixed64.Zero, Fixed64.MaxValue - Fixed64.FromFraction(3, 2), Fixed64.Zero);
        using GravitasNavigatorActor actor = fixture.CreateActor(root);
        var scratch = new PhysicsQueryScratch(128, 16);
        if (bounded)
            actor.Navigator.ConfigureSupportQueryBudget(Ample, scratch);

        fixture.Step(actor);

        Assert.Equal(root, actor.Navigator.Position);
        Assert.Equal(actor.Body.Position3d, actor.Navigator.Position);
        Assert.True(actor.Navigator.Motor!.IsInGas);
        Assert.Equal(bounded ? NavigationSupportStatus.Unavailable : NavigationSupportStatus.NoSupport,
            actor.Navigator.SupportStatus);
        Assert.Equal(!bounded, actor.Navigator.Motor.CurrentState.GroundState.HasValue);
        Assert.Equal(bounded ? PhysicsQueryExecutionStatus.Completed : (PhysicsQueryExecutionStatus?)null,
            actor.Navigator.LastSupportQueryExecutionStatus);

        // The sweep itself completed. Its native anchor remains authoritative even though
        // the adapter cannot materialize a world-space ground height from it.
        Vector3d probe = root + Vector3d.Down * Fixed64.FromFraction(3, 8);
        var status = fixture.Gravitas.Query3D.SweepSphere(probe, Fixed64.Quarter, Vector3d.Down,
            Fixed64.Quarter, actor.Navigator.SupportLayers, actor.Body.Collider, Ample, scratch, out var hit, out _);
        Assert.Equal(PhysicsQueryExecutionStatus.Completed, status);
        Physics3DHit witness = Assert.IsType<Physics3DHit>(hit);
        Assert.Same(box, witness.Collider);
        Assert.Equal(Vector3d.Up, witness.Normal);
        Assert.False(witness.TryGetPoint(out _));
    }
}

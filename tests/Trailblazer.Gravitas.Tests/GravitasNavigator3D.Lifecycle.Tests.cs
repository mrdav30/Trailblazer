using System;
using FixedMathSharp;
using FluentAssertions;
using Gravitas;
using Gravitas.Support;
using GridForge.Spatial;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Gravitas.Tests;

public sealed class GravitasNavigator3DLifecycleTests
{
    [Fact]
    public void FrameContract_ShouldRejectMissingOrDuplicatePhasesWithoutDoubleCommit()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false);
        using GravitasNavigatorScenarioFixture.Actor actor = fixture.CreateActor();
        int committedCellChanges = 0;
        actor.Navigator.CommittedCellChanged += _ => committedCellChanges++;

        Action commitWithoutPrepare = actor.Navigator.CommitFrameMotion;
        commitWithoutPrepare.Should().Throw<InvalidOperationException>();
        committedCellChanges.Should().Be(0);

        fixture.Prepare(actor);
        Action duplicatePrepare = actor.Navigator.Simulate;
        duplicatePrepare.Should().Throw<InvalidOperationException>();
        actor.Navigator.Motor!.TraversalInProgress.Should().BeTrue();

        fixture.Resolve(actor);
        committedCellChanges.Should().Be(1);
        actor.Navigator.Motor.TraversalInProgress.Should().BeFalse();

        Action duplicateCommit = actor.Navigator.CommitFrameMotion;
        duplicateCommit.Should().Throw<InvalidOperationException>();
        committedCellChanges.Should().Be(1);
    }

    [Fact]
    public void StalePreparedFrame_ShouldRejectFinalizationUntilResetAbortsTheMotorWork()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false);
        using GravitasNavigatorScenarioFixture.Actor actor = fixture.CreateActor();
        int committedCellChanges = 0;
        actor.Navigator.CommittedCellChanged += _ => committedCellChanges++;

        fixture.Prepare(actor);
        fixture.Gravitas.LateSimulate();
        fixture.Trailblazer.Simulate();

        Action staleCommit = actor.Navigator.CommitFrameMotion;
        staleCommit.Should().Throw<InvalidOperationException>();
        actor.Navigator.Motor!.TraversalInProgress.Should().BeTrue();
        committedCellChanges.Should().Be(0);

        actor.Navigator.Reset();

        actor.Navigator.IsActive.Should().BeFalse();
        actor.Navigator.Motor.TraversalInProgress.Should().BeFalse();
        committedCellChanges.Should().Be(0);
    }

    [Fact]
    public void DiscardedPreparedFrame_ShouldBeClosedByRepeatedResetWithoutPublishingACommit()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false);
        using GravitasNavigatorScenarioFixture.Actor actor = fixture.CreateActor();
        int committedCellChanges = 0;
        actor.Navigator.CommittedCellChanged += _ => committedCellChanges++;
        actor.Navigator.ApplyGuidedTrekRequest(fixture.CreateQuery(), TrekRate.Fast);

        fixture.Prepare(actor);
        actor.Navigator.Motor!.TraversalInProgress.Should().BeTrue();

        actor.Navigator.Reset();
        actor.Navigator.Reset();

        actor.Navigator.IsActive.Should().BeFalse();
        actor.Navigator.Motor.TraversalInProgress.Should().BeFalse();
        actor.Navigator.LastCommittedCell.Should().BeNull();
        committedCellChanges.Should().Be(0);
    }

    [Fact]
    public void ResetAfterCommit_ShouldNotReuseThePreviousGuideOrGroundContact()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(
            addWall: false,
            addSupport: true);
        using GravitasNavigatorScenarioFixture.Actor actor = fixture.CreateActor(
            carrierIdResolver: _ => 5);
        actor.Navigator.ApplyGuidedTrekRequest(fixture.CreateQuery(), TrekRate.Fast);
        fixture.Step(actor);
        actor.Navigator.Motor!.IsOnSolid.Should().BeTrue();

        actor.Navigator.Reset();
        fixture.DeactivateSupport();
        fixture.Reactivate(actor, new TrekCondition
        {
            Medium = TraversalMedium.Gas,
            SurfaceLevel = fixture.StartFoot.Y
        });

        fixture.Step(actor);

        actor.Navigator.Position.Should().Be(fixture.StartRoot);
        actor.Body.Position3d.Should().Be(fixture.StartRoot);
        actor.Navigator.IsGuided.Should().BeFalse();
        actor.Navigator.Motor!.IsInGas.Should().BeTrue();
        actor.Navigator.Motor.CurrentState.GroundState.Should().BeNull();
        actor.Navigator.Motor.Handler.Platform.ActivePlatform.Should().BeNull();
    }

    [Fact]
    public void StableActorBatch_ShouldExposeEveryAcceptedPoseAndCellAfterLateSimulation()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false);
        using GravitasNavigatorScenarioFixture.Actor first = fixture.CreateActor();
        using GravitasNavigatorScenarioFixture.Actor second = fixture.CreateActor(
            fixture.EndRoot,
            FixedQuaternion.FromDirection(Vector3d.Left));
        first.Navigator.ApplyGuidedTrekRequest(fixture.CreateQuery(), TrekRate.Fast);
        second.Navigator.ApplyGuidedTrekRequest(
            fixture.CreateQuery(fixture.EndFoot, fixture.StartFoot),
            TrekRate.Fast);
        int firstCellChanges = 0;
        int secondCellChanges = 0;
        first.Navigator.CommittedCellChanged += _ => firstCellChanges++;
        second.Navigator.CommittedCellChanged += _ => secondCellChanges++;

        fixture.Step(first, second);

        first.Navigator.Position.Should().Be(first.Body.Position3d);
        second.Navigator.Position.Should().Be(second.Body.Position3d);
        first.Navigator.Position.Should().Be(
            fixture.StartRoot + Vector3d.Right * Fixed64.FromFraction(1, 64));
        second.Navigator.Position.Should().Be(
            fixture.EndRoot + Vector3d.Left * Fixed64.FromFraction(1, 64));
        first.Navigator.LastCommittedCell!.Value.Address.Should().Be(
            new NavigationCellAddress("gravitas-wall", new VoxelIndex(4, 4, 4)));
        second.Navigator.LastCommittedCell!.Value.Address.Should().Be(
            new NavigationCellAddress("gravitas-wall", new VoxelIndex(6, 4, 4)));
        firstCellChanges.Should().Be(1);
        secondCellChanges.Should().Be(1);
    }

    [Fact]
    public void Adapter_ShouldRejectAnInactiveOrNonKinematicBodyBeforePreparingMotion()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false);
        using GravitasNavigatorScenarioFixture.Actor dynamicActor = fixture.CreateActor();
        dynamicActor.Body.SetMotionType(BodyMotionType.Dynamic);

        Action simulateDynamic = dynamicActor.Navigator.Simulate;
        simulateDynamic.Should().Throw<InvalidOperationException>();
        dynamicActor.Navigator.Motor!.TraversalInProgress.Should().BeFalse();

        using GravitasNavigatorScenarioFixture.Actor inactiveActor = fixture.CreateActor();
        inactiveActor.Body.Deactivate();

        Action simulateInactive = inactiveActor.Navigator.Simulate;
        simulateInactive.Should().Throw<InvalidOperationException>();
        inactiveActor.Navigator.Motor!.TraversalInProgress.Should().BeFalse();
    }

    [Fact]
    public void Prepare_ShouldRejectAWorldPoseThatTheHostTransformCannotRepresent()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false);
        using GravitasNavigatorScenarioFixture.Actor actor = fixture.CreateActor();
        var singularParent = new FixedTransform(
            Vector3d.Zero,
            FixedQuaternion.Identity,
            new Vector3d(Fixed64.Zero, Fixed64.One, Fixed64.One));
        actor.Body.PositionTransform.SetParentKeepingLocal(singularParent);

        Action prepare = actor.Navigator.Simulate;

        prepare.Should().Throw<InvalidOperationException>();
        actor.Navigator.Motor!.TraversalInProgress.Should().BeTrue();
        actor.Navigator.Reset();
        actor.Navigator.Motor.TraversalInProgress.Should().BeFalse();
    }

    [Fact]
    public void ConstructionContract_ShouldValidateBindingsProbeGeometryAndSafeReset()
    {
        using var first = new GravitasNavigatorScenarioFixture(addWall: false);
        using var second = new GravitasNavigatorScenarioFixture(addWall: false);
        using GravitasNavigatorScenarioFixture.Actor actor = first.CreateActor();

        Action nullBody = () => new GravitasNavigator3D(
            first.Trailblazer,
            null!,
            PhysicsLayerMask.None,
            Fixed64.Quarter,
            Fixed64.FromFraction(1, 8),
            Fixed64.Quarter);
        Action crossWorld = () => new GravitasNavigator3D(
            second.Trailblazer,
            actor.Body,
            PhysicsLayerMask.None,
            Fixed64.Quarter,
            Fixed64.FromFraction(1, 8),
            Fixed64.Quarter);
        Action nullContext = () => new GravitasNavigator3D(
            null!,
            actor.Body,
            PhysicsLayerMask.None,
            Fixed64.Quarter,
            Fixed64.FromFraction(1, 8),
            Fixed64.Quarter);
        Action zeroProbeRadius = () => new GravitasNavigator3D(
            first.Trailblazer,
            actor.Body,
            PhysicsLayerMask.None,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Quarter);
        Action negativeProbeOffset = () => new GravitasNavigator3D(
            first.Trailblazer,
            actor.Body,
            PhysicsLayerMask.None,
            Fixed64.Quarter,
            -Fixed64.Epsilon,
            Fixed64.Quarter);
        Action zeroProbeDistance = () => new GravitasNavigator3D(
            first.Trailblazer,
            actor.Body,
            PhysicsLayerMask.None,
            Fixed64.Quarter,
            Fixed64.Zero,
            Fixed64.Zero);

        nullBody.Should().Throw<ArgumentNullException>();
        crossWorld.Should().Throw<ArgumentException>();
        nullContext.Should().Throw<ArgumentNullException>();
        zeroProbeRadius.Should().Throw<ArgumentOutOfRangeException>();
        negativeProbeOffset.Should().Throw<ArgumentOutOfRangeException>();
        zeroProbeDistance.Should().Throw<ArgumentOutOfRangeException>();

        var uninitialized = new GravitasNavigator3D(
            first.Trailblazer,
            actor.Body,
            PhysicsLayerMask.FromLayer(3),
            Fixed64.Quarter,
            Fixed64.FromFraction(1, 8),
            Fixed64.Quarter);
        uninitialized.Body.Should().BeSameAs(actor.Body);
        uninitialized.SupportLayers.Should().Be(PhysicsLayerMask.FromLayer(3));

        Action resetBeforeSetup = uninitialized.Reset;
        resetBeforeSetup.Should().NotThrow();
        uninitialized.IsActive.Should().BeFalse();
    }

    [Fact]
    public void ResetAdapter_ShouldRejectRebindingAwayFromTheBodyWorld()
    {
        using var first = new GravitasNavigatorScenarioFixture(addWall: false);
        using var second = new GravitasNavigatorScenarioFixture(addWall: false);
        using GravitasNavigatorScenarioFixture.Actor actor = first.CreateActor();
        actor.Navigator.Reset();

        Action rebind = () => actor.Navigator.Setup(
            second.Trailblazer,
            first.StartRoot,
            first.Profile,
            FixedQuaternion.FromDirection(Vector3d.Right));

        rebind.Should().Throw<ArgumentException>();
        actor.Navigator.Context.Should().BeNull();
        actor.Navigator.IsActive.Should().BeFalse();
    }
}

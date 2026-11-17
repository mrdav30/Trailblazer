using System;
using FixedMathSharp;
using FluentAssertions;
using Gravitas.Colliders;
using Gravitas.Queries;
using Gravitas;
using GridForge.Configuration;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Gravitas.Tests;

public sealed partial class GravitasNavigator3DProfileReplacementTests
{
    private readonly ITestOutputHelper _output;

    public GravitasNavigator3DProfileReplacementTests(ITestOutputHelper output) => _output = output;

    private static readonly PhysicsQueryBudget PostureBudget = new(8192, 8192, 8192);

    [Fact]
    public void BoundedReplacement_ShouldPreserveStateWhenTheWorldViewIsNotReady()
    {
        using var fixture = new GravitasProfileReplacementScenarioFixture(addSupport: true);
        using var actor = fixture.CreateActor();
        fixture.Step(actor);
        var navigator = actor.Navigator;
        NavigationAgentProfile original = navigator.NavigationProfile;
        var compact = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Quarter, Fixed64.Half, Fixed64.Zero),
            original.MaxStepUp, original.MaxDropDown, original.AllowedMedia, original.Capabilities);
        fixture.Trailblazer.World.TryAddGrid(new GridConfiguration(new Vector3d(20, 0, 20),
            new Vector3d(21, 1, 21)), out _).Should().BeTrue();
        var before = fixture.Gravitas.ComputeReplayHash();
        object navigationBefore = GravitasRecordSnapshot.Save(navigator, memoryPack: false);
        var cell = navigator.LastCommittedCell;
        var collider = actor.Body.Collider;
        int publications = 0;
        int exits = 0;
        navigator.CommittedCellChanged += _ => publications++;
        collider.OnContactExit += _ => exits++;
        var scratch = new PhysicsQueryScratch(0, 0);

        navigator.TryReplaceNavigationProfile(compact, PostureBudget, scratch, out var status, out var work)
            .Should().Be(NavigationProfileReplacementStatus.PhysicalFitUnavailable);

        status.Should().Be(PhysicsQueryExecutionStatus.WorldNotReady);
        work.Should().Be(default(PhysicsQueryWork));
        fixture.Gravitas.ComputeReplayHash().Should().Be(before);
        GravitasRecordSnapshot.Save(navigator, memoryPack: false).Should().Be(navigationBefore);
        navigator.NavigationProfile.Should().Be(original);
        navigator.LastCommittedCell.Should().Be(cell);
        actor.Body.Collider.Should().BeSameAs(collider);
        publications.Should().Be(0);
        exits.Should().Be(0);

        fixture.Step(actor);
        navigator.TryReplaceNavigationProfile(compact, PostureBudget, scratch, out status, out _)
            .Should().Be(NavigationProfileReplacementStatus.Applied);
        status.Should().Be(PhysicsQueryExecutionStatus.Completed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BoundedReplacement_ShouldPreserveStateAtEveryOneBelowLimitAndCommitAtTheSameFoot(bool outsideGrid)
    {
        using var fixture = new GravitasProfileReplacementScenarioFixture(addSupport: true);
        using var actor = fixture.CreateActor(outsideGrid ? new Vector3d(32, 1, 0) : null);
        var navigator = actor.Navigator;
        NavigationAgentProfile standing = navigator.NavigationProfile;
        var compact = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Quarter, Fixed64.Half, Fixed64.Zero),
            standing.MaxStepUp, standing.MaxDropDown, standing.AllowedMedia, standing.Capabilities);
        navigator.TryReplaceNavigationProfile(compact).Should().Be(NavigationProfileReplacementStatus.Applied);
        Vector3d foot = navigator.FootPosition;
        LSCuboidCollider ceiling = fixture.AddCeiling(foot + Vector3d.Up * Fixed64.FromFraction(7, 8),
            new Vector3d(Fixed64.Two, Fixed64.Quarter, Fixed64.Two));
        Reconcile();
        if (outsideGrid)
            fixture.Trailblazer.World.TryGetVoxel(ceiling.Center, out _).Should().BeFalse();
        var scratch = new PhysicsQueryScratch(0, 0);
        var before = fixture.Gravitas.ComputeReplayHash();
        object navigationBefore = GravitasRecordSnapshot.Save(navigator, memoryPack: false);
        var cellBefore = navigator.LastCommittedCell;
        var collider = actor.Body.Collider;
        int publications = 0;
        int exits = 0;
        navigator.CommittedCellChanged += _ => publications++;
        collider.OnContactExit += _ => exits++;

        navigator.TryReplaceNavigationProfile(standing, PostureBudget, scratch, out var status, out var ample)
            .Should().Be(NavigationProfileReplacementStatus.PhysicalFitRejected);
        status.Should().Be(PhysicsQueryExecutionStatus.Completed);
        ample.Discovery.Should().BeGreaterThan(0);
        ample.Candidates.Should().BeGreaterThan(0);
        ample.Geometry.Should().BeGreaterThan(0);
        foreach (var budget in new[]
        {
            new PhysicsQueryBudget(ample.Discovery - 1, ample.Candidates, ample.Geometry),
            new PhysicsQueryBudget(ample.Discovery, ample.Candidates - 1, ample.Geometry),
            new PhysicsQueryBudget(ample.Discovery, ample.Candidates, ample.Geometry - 1)
        })
        {
            navigator.TryReplaceNavigationProfile(standing, budget, scratch, out status, out var work)
                .Should().Be(NavigationProfileReplacementStatus.PhysicalFitUnavailable);
            status.Should().Be(PhysicsQueryExecutionStatus.WorkBudgetExceeded);
            work.Discovery.Should().BeLessThanOrEqualTo(budget.DiscoveryLimit);
            work.Candidates.Should().BeLessThanOrEqualTo(budget.CandidateLimit);
            work.Geometry.Should().BeLessThanOrEqualTo(budget.GeometryLimit);
            AssertUnchanged();
        }
        navigator.TryReplaceNavigationProfile(standing,
            new PhysicsQueryBudget(ample.Discovery, ample.Candidates, ample.Geometry), scratch, out status, out var exact)
            .Should().Be(NavigationProfileReplacementStatus.PhysicalFitRejected);
        status.Should().Be(PhysicsQueryExecutionStatus.Completed);
        exact.Should().Be(ample);
        AssertUnchanged();

        ceiling.Deactivate();
        Reconcile();
        navigator.TryReplaceNavigationProfile(standing, PostureBudget, scratch, out status, out _)
            .Should().Be(NavigationProfileReplacementStatus.Applied);
        status.Should().Be(PhysicsQueryExecutionStatus.Completed);
        navigator.NavigationProfile.Should().Be(standing);
        navigator.FootPosition.Should().Be(foot);
        actor.Body.Position3d.Should().Be(navigator.Position);
        actor.Body.Collider.Should().BeSameAs(collider);
        ((LSCapsuleCollider)collider).Radius.Should().Be(standing.Shape.Radius);
        ((LSCapsuleCollider)collider).Size.Y.Should().Be(standing.Shape.Height);

        void Reconcile()
        {
            if (outsideGrid)
            {
                fixture.Gravitas.Simulate();
                fixture.Gravitas.LateSimulate();
            }
            else
                fixture.Step(actor);
        }

        void AssertUnchanged()
        {
            fixture.Gravitas.ComputeReplayHash().Should().Be(before);
            GravitasRecordSnapshot.Save(navigator, memoryPack: false).Should().Be(navigationBefore);
            navigator.NavigationProfile.Should().Be(compact);
            navigator.FootPosition.Should().Be(foot);
            navigator.LastCommittedCell.Should().Be(cellBefore);
            actor.Body.Collider.Should().BeSameAs(collider);
            actor.Body.Position3d.Should().Be(navigator.Position);
            publications.Should().Be(0);
            exits.Should().Be(0);
        }
    }

    [Fact]
    public void BoundedReplacement_ShouldRejectAnUnsupportedColliderBeforeStartingClearance()
    {
        using var fixture = new GravitasNavigatorScenarioFixture(addWall: false);
        using var actor = fixture.CreateActor();
        var navigator = actor.Navigator;
        NavigationAgentProfile current = navigator.NavigationProfile;
        var replacement = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Quarter, current.Shape.Height, current.Shape.RootToFootOffsetY),
            current.MaxStepUp, current.MaxDropDown, current.AllowedMedia, current.Capabilities);
        var before = fixture.Gravitas.ComputeReplayHash();
        object navigationBefore = GravitasRecordSnapshot.Save(navigator, memoryPack: false);

        navigator.TryReplaceNavigationProfile(replacement, PostureBudget, new PhysicsQueryScratch(0, 0),
            out var status, out var work).Should().Be(NavigationProfileReplacementStatus.PhysicalFitRejected);

        status.Should().BeNull();
        work.Should().Be(default(PhysicsQueryWork));
        navigator.NavigationProfile.Should().Be(current);
        fixture.Gravitas.ComputeReplayHash().Should().Be(before);
        GravitasRecordSnapshot.Save(navigator, memoryPack: false).Should().Be(navigationBefore);
    }

    [Fact]
    public void BoundedReplacement_ShouldLeaveClearanceEmptyWhenNavigationRejectsAndGuardPreparedMotion()
    {
        using var fixture = new GravitasProfileReplacementScenarioFixture();
        using var actor = fixture.CreateActor();
        var navigator = actor.Navigator;
        var scratch = new PhysicsQueryScratch(0, 0);
        navigator.TryReplaceNavigationProfile(default, PostureBudget, scratch, out var status, out var work)
            .Should().Be(NavigationProfileReplacementStatus.InvalidProfile);
        status.Should().BeNull();
        work.Should().Be(default(PhysicsQueryWork));
        navigator.TryReplaceNavigationProfile(navigator.NavigationProfile, PostureBudget, scratch, out status, out work)
            .Should().Be(NavigationProfileReplacementStatus.Unchanged);
        status.Should().BeNull();
        work.Should().Be(default(PhysicsQueryWork));
        Action missingScratch = () => navigator.TryReplaceNavigationProfile(default, PostureBudget, null!, out _, out _);
        missingScratch.Should().Throw<ArgumentNullException>().WithParameterName("scratch");
        fixture.Prepare(actor);
        navigator.TryReplaceNavigationProfile(default, PostureBudget, scratch, out status, out work)
            .Should().Be(NavigationProfileReplacementStatus.UnsafeLifecycle);
        status.Should().BeNull();
        work.Should().Be(default(PhysicsQueryWork));
        fixture.Resolve(actor);
    }

    [Fact]
    public void BoundedReplacement_ShouldReleaseScratchBeforeNotificationsAndPreserveDiagnosticsWhenTheyThrow()
    {
        using var fixture = new GravitasProfileReplacementScenarioFixture();
        using var actor = fixture.CreateActor();
        var navigator = actor.Navigator;
        NavigationAgentProfile standing = navigator.NavigationProfile;
        var compact = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Quarter, Fixed64.Half, Fixed64.Zero),
            standing.MaxStepUp, standing.MaxDropDown, standing.AllowedMedia, standing.Capabilities);
        var notified = new NavigationAgentProfile(compact.Shape, compact.MaxStepUp + Fixed64.MinIncrement,
            compact.MaxDropDown, compact.AllowedMedia, compact.Capabilities);
        using var contact = fixture.CreateActor(navigator.Position + Vector3d.Right * Fixed64.FromFraction(3, 4));
        contact.Body.SetMotionType(PhysicsMobilityType.Dynamic);
        fixture.Gravitas.Simulate();
        fixture.Gravitas.LateSimulate();
        var scratch = new PhysicsQueryScratch(0, 0);
        PhysicsQueryExecutionStatus? status = null;
        PhysicsQueryWork work = default;
        var failure = new InvalidOperationException("bounded posture notification");
        int exits = 0;
        actor.Body.Collider.OnContactExit += collision =>
        {
            exits++;
            navigator.NavigationProfile.Should().Be(compact);
            actor.Body.Position3d.Should().Be(navigator.Position);
            fixture.Trailblazer.World.TryAddGrid(new GridConfiguration(new Vector3d(20, 0, 20),
                new Vector3d(21, 1, 21)), out _).Should().BeTrue();
            fixture.Gravitas.Query3D.SweepSphere(Vector3d.Zero, Fixed64.Quarter, Vector3d.Down,
                Fixed64.One, navigator.SupportLayers, actor.Body.Collider, PostureBudget, scratch,
                out var hit, out _).Should().Be(PhysicsQueryExecutionStatus.WorldNotReady);
            hit.Should().BeNull();
            navigator.TryReplaceNavigationProfile(notified).Should().Be(NavigationProfileReplacementStatus.Applied);
            throw failure;
        };

        Action replace = () => navigator.TryReplaceNavigationProfile(compact, PostureBudget, scratch, out status, out work);
        replace.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);

        exits.Should().Be(1);
        status.Should().Be(PhysicsQueryExecutionStatus.Completed);
        work.Discovery.Should().BeGreaterThan(0);
        work.Geometry.Should().BeGreaterThan(0);
        navigator.NavigationProfile.Should().Be(notified);
        actor.Body.Position3d.Should().Be(navigator.Position);
    }
}

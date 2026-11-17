using System;
using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using Gravitas;
using Gravitas.Colliders;
using Gravitas.Queries;
using Gravitas.Support;
using GridForge.Configuration;
using GridForge.Grids;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;
using static Trailblazer.Gravitas.Tests.Support.GravitasPlanarPhysicsFixture;

namespace Trailblazer.Gravitas.Tests;

public sealed partial class GravitasNavigator2DSupportTests
{
    private readonly ITestOutputHelper _output;

    public GravitasNavigator2DSupportTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public void SideView_BoundedPostureShouldPreserveStateWhenTheWorldViewIsNotReady()
    {
        using var world = new GridWorld();
        using var navigation = TrailblazerWorldContext.Attach(world);
        using var physics = GravitasWorldContext.Attach(world);
        PublishFloorMap(world, navigation, physics);
        AddBox(physics, Vector2d.Zero, new Vector2d(9, 1));
        SolidBody2D body = CreateCapsule(physics, Vector2d.Forward, Compact);
        using var navigator = CreateNavigator(navigation, body, Compact);
        AdvanceRider(navigation, physics, navigator, Vector2d.Zero);
        world.TryAddGrid(new GridConfiguration(new Vector3d(20, 0, 20),
            new Vector3d(21, 0, 21)), out _).Should().BeTrue();
        var before = physics.ComputeReplayHash();
        object navigationBefore = GravitasRecordSnapshot.Save(navigator, memoryPack: false);
        var cell = navigator.LastCommittedCell;
        var support = navigator.Support;
        var collider = body.Collider;
        int publications = 0;
        int exits = 0;
        navigator.CommittedCellChanged += _ => publications++;
        collider.OnContactExit += _ => exits++;
        var scratch = new PhysicsQueryScratch(0, 0);

        navigator.TryReplaceNavigationProfile(Standing, SupportBudget, scratch, out var status, out var work)
            .Should().Be(NavigationProfileReplacementStatus.PhysicalFitUnavailable);

        status.Should().Be(PhysicsQueryExecutionStatus.WorldNotReady);
        work.Should().Be(default(PhysicsQueryWork));
        physics.ComputeReplayHash().Should().Be(before);
        GravitasRecordSnapshot.Save(navigator, memoryPack: false).Should().Be(navigationBefore);
        navigator.NavigationProfile.Should().Be(Compact);
        navigator.LastCommittedCell.Should().Be(cell);
        navigator.Support.Should().Be(support);
        body.Collider.Should().BeSameAs(collider);
        publications.Should().Be(0);
        exits.Should().Be(0);

        AdvanceRider(navigation, physics, navigator, Vector2d.Zero);
        navigator.TryReplaceNavigationProfile(Standing, SupportBudget, scratch, out status, out _)
            .Should().Be(NavigationProfileReplacementStatus.Applied);
        status.Should().Be(PhysicsQueryExecutionStatus.Completed);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SideView_BoundedPostureShouldKeepStateAtEveryOneBelowLimitAndRejectAnUnpartitionedCeiling(bool outsideGrid)
    {
        using var world = new GridWorld();
        using var navigation = TrailblazerWorldContext.Attach(world);
        using var physics = GravitasWorldContext.Attach(world);
        PublishFloorMap(world, navigation, physics);
        AddBox(physics, Vector2d.Zero, new Vector2d(9, 1));
        Vector2d start = new(outsideGrid ? (Fixed64)32 : Fixed64.Zero, Fixed64.One);
        LSAABBoxCollider2D ceiling = AddBox(physics, start + Vector2d.Forward,
            new Vector2d((Fixed64)4, Fixed64.Half));
        SolidBody2D body = CreateCapsule(physics, start, Compact);
        using var navigator = CreateNavigator(navigation, body, Compact);
        Reconcile();
        if (outsideGrid)
            world.TryGetVoxel(ceiling.Center.ToVector3d(Fixed64.Zero), out _).Should().BeFalse();
        var scratch = new PhysicsQueryScratch(0, 0);
        var before = physics.ComputeReplayHash();
        object navigationBefore = GravitasRecordSnapshot.Save(navigator, memoryPack: false);
        var originalCell = navigator.LastCommittedCell;
        var originalSupport = navigator.Support;
        var collider = body.Collider;
        int publications = 0;
        int exits = 0;
        navigator.CommittedCellChanged += _ => publications++;
        collider.OnContactExit += _ => exits++;

        navigator.TryReplaceNavigationProfile(Standing, SupportBudget, scratch, out var status, out var ample)
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
            navigator.TryReplaceNavigationProfile(Standing, budget, scratch, out status, out var work)
                .Should().Be(NavigationProfileReplacementStatus.PhysicalFitUnavailable);
            status.Should().Be(PhysicsQueryExecutionStatus.WorkBudgetExceeded);
            work.Discovery.Should().BeLessThanOrEqualTo(budget.DiscoveryLimit);
            work.Candidates.Should().BeLessThanOrEqualTo(budget.CandidateLimit);
            work.Geometry.Should().BeLessThanOrEqualTo(budget.GeometryLimit);
            AssertUnchanged();
        }
        navigator.TryReplaceNavigationProfile(Standing,
            new PhysicsQueryBudget(ample.Discovery, ample.Candidates, ample.Geometry), scratch, out status, out var exact)
            .Should().Be(NavigationProfileReplacementStatus.PhysicalFitRejected);
        status.Should().Be(PhysicsQueryExecutionStatus.Completed);
        exact.Should().Be(ample);
        AssertUnchanged();

        ceiling.Deactivate();
        Reconcile();
        navigator.TryReplaceNavigationProfile(Standing, SupportBudget, scratch, out status, out _)
            .Should().Be(NavigationProfileReplacementStatus.Applied);
        status.Should().Be(PhysicsQueryExecutionStatus.Completed);
        Vector2d accepted = start + Vector2d.Forward * Fixed64.Half;
        navigator.Pose.Center.Should().Be(accepted);
        body.Position.Should().Be(accepted);
        body.Collider.Should().BeSameAs(collider);
        ((LSCapsuleCollider2D)collider).Height.Should().Be((Fixed64)2);
        navigator.SupportStatus.Should().Be(NavigationSupportStatus.Unavailable);

        void Reconcile()
        {
            if (outsideGrid)
            {
                physics.Simulate();
                physics.LateSimulate();
            }
            else
                AdvanceRider(navigation, physics, navigator, Vector2d.Zero);
        }

        void AssertUnchanged()
        {
            physics.ComputeReplayHash().Should().Be(before);
            GravitasRecordSnapshot.Save(navigator, memoryPack: false).Should().Be(navigationBefore);
            navigator.NavigationProfile.Should().Be(Compact);
            navigator.Pose.Center.Should().Be(start);
            navigator.LastCommittedCell.Should().Be(originalCell);
            navigator.Support.Should().Be(originalSupport);
            body.Position.Should().Be(start);
            body.Collider.Should().BeSameAs(collider);
            publications.Should().Be(0);
            exits.Should().Be(0);
        }
    }

    [Fact]
    public void SideView_BoundedPostureShouldLeaveClearanceEmptyBeforePhysicsAndGuardPreparedMotion()
    {
        using var world = new GridWorld();
        using var navigation = TrailblazerWorldContext.Attach(world);
        using var physics = GravitasWorldContext.Attach(world);
        PublishFloorMap(world, navigation, physics);
        SolidBody2D body = CreateCapsule(physics, Vector2d.Forward, Compact);
        using var navigator = CreateNavigator(navigation, body, Compact);
        var scratch = new PhysicsQueryScratch(0, 0);
        navigator.TryReplaceNavigationProfile(default, SupportBudget, scratch, out var status, out var work)
            .Should().Be(NavigationProfileReplacementStatus.InvalidProfile);
        status.Should().BeNull();
        work.Should().Be(default(PhysicsQueryWork));
        navigator.TryReplaceNavigationProfile(Compact, SupportBudget, scratch, out status, out work)
            .Should().Be(NavigationProfileReplacementStatus.Unchanged);
        status.Should().BeNull();
        work.Should().Be(default(PhysicsQueryWork));
        Action missingScratch = () => navigator.TryReplaceNavigationProfile(Standing, SupportBudget, null!, out _, out _);
        missingScratch.Should().Throw<ArgumentNullException>().WithParameterName("scratch");
        navigation.Simulate();
        physics.Simulate();
        navigator.PrepareFrameMotion();
        navigator.TryReplaceNavigationProfile(Standing, SupportBudget, scratch, out status, out work)
            .Should().Be(NavigationProfileReplacementStatus.UnsafeLifecycle);
        status.Should().BeNull();
        work.Should().Be(default(PhysicsQueryWork));
        physics.LateSimulate();
        navigator.CommitFrameMotion();
        navigation.LateSimulate();
    }

    [Fact]
    public void TopDown_BoundedPostureShouldReplaceTheExactCircleAndKeepIdentity()
    {
        using var world = new GridWorld();
        using var navigation = TrailblazerWorldContext.Attach(world);
        using var physics = GravitasWorldContext.Attach(world);
        Configure(navigation, physics);
        var grid = new GridConfiguration(new Vector3d(-2, 0, -2), new Vector3d(2, 0, 2));
        world.TryAddGrid(grid, out _).Should().BeTrue();
        var cell = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.One, Fixed64.Two);
        GravitasPlanarMapFixture.Publish(navigation, physics, new NavigationMapBuilder(MapId, grid)
            .SetPlanarLayer(NavigationMode2D.TopDown, 0, Fixed64.Zero).SetDefaultCell(cell).Build());
        var profile = new NavigationAgentProfile2D(NavigationMode2D.TopDown, Fixed64.Half, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, TraversalMedia.Solid, TraversalCapability.None);
        SolidBody2D body = CreateKinematic(physics, Vector2d.Zero, HostY, new LSCircleCollider2D(Fixed64.Half));
        using var navigator = new GravitasNavigator2D(navigation, body, MapId,
            new Guid("3040c3be-0b7c-4600-91d2-f63c25bc189f"), profile,
            new NavigationMotorSettings2D(Fixed64.One, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero));
        AdvanceRider(navigation, physics, navigator, Vector2d.Zero);
        var replacement = new NavigationAgentProfile2D(profile.Mode, Fixed64.Quarter, Fixed64.Zero,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero, profile.AllowedMedia, profile.Capabilities);
        var collider = body.Collider;

        navigator.TryReplaceNavigationProfile(replacement, SupportBudget, new PhysicsQueryScratch(0, 0),
            out var status, out var work).Should().Be(NavigationProfileReplacementStatus.Applied);

        status.Should().Be(PhysicsQueryExecutionStatus.Completed);
        work.Geometry.Should().BeGreaterThan(0);
        navigator.Pose.Center.Should().Be(Vector2d.Zero);
        body.Collider.Should().BeSameAs(collider);
        ((LSCircleCollider2D)collider).Radius.Should().Be(Fixed64.Quarter);
        navigator.SupportStatus.Should().Be(NavigationSupportStatus.NotApplicable);
    }

    [Fact]
    public void SideView_BoundedPostureShouldReleaseScratchAndKeepCommittedStateWhenAnExitCallbackThrows()
    {
        using var world = new GridWorld();
        using var navigation = TrailblazerWorldContext.Attach(world);
        using var physics = GravitasWorldContext.Attach(world);
        PublishFloorMap(world, navigation, physics);
        AddBox(physics, Vector2d.Zero, new Vector2d(9, 1));
        var start = Vector2d.Forward * (Fixed64.One + Fixed64.Half);
        SolidBody2D body = CreateCapsule(physics, start, Standing);
        using var navigator = CreateNavigator(navigation, body, Standing);
        var compact = new NavigationAgentProfile2D(Standing.Mode, Fixed64.Quarter, Fixed64.Zero,
            Standing.MaxStepUp, Standing.MaxDropDown, Standing.MinimumSupportNormalDot,
            Standing.AllowedMedia, Standing.Capabilities);
        SolidBody2D contact = CreateKinematic(physics, start + Vector2d.Right * Fixed64.FromFraction(3, 4),
            HostY, new LSCircleCollider2D(Fixed64.Half));
        contact.GravityScale = Fixed64.Zero;
        contact.SetMotionType(PhysicsMobilityType.Dynamic);
        physics.Simulate();
        physics.LateSimulate();
        var scratch = new PhysicsQueryScratch(0, 0);
        var failure = new InvalidOperationException("bounded planar posture notification");
        PhysicsQueryExecutionStatus? status = null;
        PhysicsQueryWork work = default;
        int exits = 0;
        body.Collider.OnContactExit += collision =>
        {
            exits++;
            navigator.NavigationProfile.Should().Be(compact);
            body.Position.Should().Be(navigator.Pose.Center);
            world.TryAddGrid(new GridConfiguration(new Vector3d(20, 0, 20),
                new Vector3d(21, 0, 21)), out _).Should().BeTrue();
            var probe = new Physics2DSupportQuery(body.Collider, body.Position, compact.Radius,
                Vector2d.Forward, Fixed64.Quarter, compact.MinimumSupportNormalDot, PhysicsLayerMask.All);
            physics.Query2D.QuerySupport(probe, SupportBudget, scratch, out var support, out _)
                .Should().Be(PhysicsQueryExecutionStatus.WorldNotReady);
            support.Should().BeNull();
            navigator.TryReplaceNavigationProfile(Standing).Should().Be(NavigationProfileReplacementStatus.UnsafeLifecycle);
            throw failure;
        };

        Action replace = () => navigator.TryReplaceNavigationProfile(compact, SupportBudget, scratch, out status, out work);
        replace.Should().Throw<InvalidOperationException>().Which.Should().BeSameAs(failure);

        exits.Should().Be(1);
        status.Should().Be(PhysicsQueryExecutionStatus.Completed);
        work.Discovery.Should().BeGreaterThan(0);
        work.Geometry.Should().BeGreaterThan(0);
        navigator.NavigationProfile.Should().Be(compact);
        navigator.Pose.Center.Should().Be(Vector2d.Forward * Fixed64.FromFraction(3, 4));
        body.Position.Should().Be(navigator.Pose.Center);
        contact.Deactivate();
    }

    [Theory]
    [InlineData(false)]
#if !TRAILBLAZER_DISABLE_MEMORYPACK
    [InlineData(true)]
#endif
    public void SideView_BoundedPostureShouldRestoreTheBodyBeforeNavigatorAndReplayExactMotion(bool memoryPack)
    {
        using var world = new GridWorld();
        using var navigation = TrailblazerWorldContext.Attach(world);
        using var physics = GravitasWorldContext.Attach(world);
        PublishFloorMap(world, navigation, physics);
        AddBox(physics, Vector2d.Zero, new Vector2d(9, 1));
        SolidBody2D body = CreateCapsule(physics, Vector2d.Forward, Compact);
        using var navigator = CreateNavigator(navigation, body, Compact);
        AdvanceRider(navigation, physics, navigator, Vector2d.Zero);
        navigator.TryReplaceNavigationProfile(Standing, SupportBudget, new PhysicsQueryScratch(0, 0),
            out var status, out var work).Should().Be(NavigationProfileReplacementStatus.Applied);
        status.Should().Be(PhysicsQueryExecutionStatus.Completed);
        _output.WriteLine($"Posture2D|standing|{status}|{work.Discovery}/{work.Candidates}/{work.Geometry}");
        AdvanceRider(navigation, physics, navigator, Vector2d.Zero);
        object bodyRecord = GravitasRecordSnapshot.Save(body, memoryPack);
        object navigatorRecord = GravitasRecordSnapshot.Save(navigator, memoryPack);
        var first = Replay("source");

        GravitasRecordSnapshot.Load(body, bodyRecord);
        GravitasRecordSnapshot.Load(navigator, navigatorRecord);
        navigator.RefreshSupport();
        var second = Replay("restored");

        second.Should().Equal(first);
        navigator.NavigationProfile.Should().Be(Standing);
        body.Position.Should().Be(navigator.Pose.Center);

        List<(NavigationPose2D Pose, Vector2d Velocity, Vector2d Point, NavigationCellAddress? Cell)> Replay(string stage)
        {
            var frames = new List<(NavigationPose2D, Vector2d, Vector2d, NavigationCellAddress?)>();
            for (int frame = 0; frame < 16; frame++)
            {
                AdvanceRider(navigation, physics, navigator, Vector2d.Right * Fixed64.FromFraction(1, 16));
                navigator.IsGrounded.Should().BeTrue();
                var support = navigator.Support!.Value;
                frames.Add((navigator.Pose, navigator.Velocity, support.Point,
                    navigator.LastCommittedCell?.Address));
                _output.WriteLine($"Posture2D|{stage}|{frame}|{physics.ComputeReplayHash()}|{navigator.SupportStatus}"
                    + $"|{navigator.Pose.RotationRadians.ToRawString()}|{navigator.LastCommittedCell?.Address}"
                    + $"|{navigator.NavigationProfile.Radius.ToRawString()},{navigator.NavigationProfile.AxisLength.ToRawString()}"
                    + $"|{support.Friction.ToRawString()}");
                GravitasMotionTrace.WriteFrame(_output, frame, navigator.Pose.Center.ToVector3d(HostY),
                    body.Position.ToVector3d(HostY), navigator.Velocity.ToVector3d(Fixed64.Zero),
                    support.Point.ToVector3d(HostY), support.Normal.ToVector3d(Fixed64.Zero));
            }
            return frames;
        }
    }
}

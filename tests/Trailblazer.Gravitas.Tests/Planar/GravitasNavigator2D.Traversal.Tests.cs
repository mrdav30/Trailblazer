using System;
using System.Collections.Generic;
using FixedMathSharp;
using FluentAssertions;
using Gravitas;
using Gravitas.Colliders;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using Trailblazer.Gravitas.Tests.Support;
using Trailblazer.Navigation;
using Trailblazer.Pathing;
using Xunit;
using static Trailblazer.Gravitas.Tests.Support.GravitasPlanarPhysicsFixture;

namespace Trailblazer.Gravitas.Tests;

public sealed class GravitasNavigator2DTraversalTests
{
    private const string MapId = "side-view-ladder";
    private static readonly Fixed64 HostY = (Fixed64)7;
    private static readonly NavigationAreaPolicyKey Policy = new("ladder-policy", 1);
    private static readonly NavigationWorkBudget Budget = new(8192, 128, 128, 512, 512, 128, 128, 128, 1024, 4096, 0);
    private static readonly NavigationAgentProfile2D Profile = new(NavigationMode2D.SideView,
        Fixed64.Quarter, Fixed64.Half, Fixed64.Zero, Fixed64.Zero, Fixed64.Half,
        TraversalMedia.Solid, TraversalCapability.Climb);

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void SideViewLadder_ShouldHoldEntryAndCompleteOnlyAfterAcceptedLanding(PathAlgorithm algorithm)
    {
        var first = Climb(algorithm);
        var replay = Climb(algorithm);
        replay.Frames.Should().Equal(first.Frames);
        replay.Entries.Should().Equal(first.Entries);
    }

    private static (List<(NavigationPose2D Pose, Vector2d Velocity, NavigationSupportStatus2D Support,
        bool Grounded, bool ActionPending, NavigationCellAddress? Cell)> Frames,
        List<(int Frame, NavigationCellAddress? Address, TraversalMedium? Medium)> Entries) Climb(PathAlgorithm algorithm)
    {
        using var world = new GridWorld();
        using var navigation = TrailblazerWorldContext.Attach(world);
        using var physics = GravitasWorldContext.Attach(world);
        Configure(navigation, physics);

        var grid = new GridConfiguration(new Vector3d(0, 0, -1), new Vector3d(4, 1, 2));
        world.TryAddGrid(grid, out _).Should().BeTrue();
        var floor = new NavigationCell(TraversalMedia.Solid, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.One, Fixed64.Two);
        var air = new NavigationCell(TraversalMedia.Gas, TraversalCapability.None, default,
            Fixed64.Zero, Fixed64.Zero, Fixed64.Zero);
        var builder = new NavigationMapBuilder(MapId, grid).SetPlanarLayer(NavigationMode2D.SideView, 1, Fixed64.One)
            .SetDefaultCell(air);
        Fixed64 low = -Fixed64.Half;
        Fixed64 high = Fixed64.Half + Fixed64.Quarter;
        var start = AddSupport(0, 1, low);
        var departure = AddSupport(1, 1, low);
        var landing = AddSupport(2, 2, high);
        var goal = AddSupport(3, 2, high);
        builder.AddTransition(new TraversalTransitionDefinition("ladder-up", TraversalTransitionType.Climb,
            departure.Index, TraversalMedium.Solid, landing, TraversalMedium.Solid, TraversalCapability.Climb, (Fixed64)3));
        var policy = new NavigationAreaPolicy(Policy, new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        navigation.Pathing.Admit(new NavigationAreaPolicyCommitOperation(policy, 2, navigation.FrameCount + 1))
            .Should().BeTrue();
        GravitasPlanarMapFixture.Publish(navigation, physics, builder.Build());

        AddBox(physics, new Vector2d(Fixed64.Half, -Fixed64.One), new Vector2d(2, 1), HostY);
        AddBox(physics, new Vector2d(Fixed64.Two + Fixed64.Half, Fixed64.Quarter), new Vector2d(2, 1), HostY);
        var body = CreateKinematic(physics, Vector2d.Zero, HostY,
            new LSCapsuleCollider2D(Profile.Radius, Profile.AxisLength + Profile.Radius * (Fixed64)2));
        using var navigator = new GravitasNavigator2D(navigation, body, MapId,
            new Guid("253a97dd-7719-4112-9e43-c351a85b9499"), Profile,
            new NavigationMotorSettings2D((Fixed64)4, Fixed64.Zero, (Fixed64)16, Fixed64.Zero),
            supportProbeDistance: Fixed64.Quarter);

        var frames = new List<(NavigationPose2D, Vector2d, NavigationSupportStatus2D, bool, bool, NavigationCellAddress?)>();
        var entries = new List<(int Frame, NavigationCellAddress? Address, TraversalMedium? Medium)>();
        navigator.CommittedCellChanged += state => entries.Add((frames.Count, state?.Address, state?.Medium));
        navigator.RefreshSupport();
        navigator.Support!.Value.Point.Should().Be(new Vector2d(Fixed64.Zero, low));
        navigator.ApplySideViewGuidedTrekRequest(start, goal, Policy, algorithm, Budget, allowTransitions: true)
            .Should().Be(NavigationGuideStatus.Success);
        for (int i = 0; i < 12 && !navigator.PendingTransition.HasValue; i++)
            Step();

        navigator.PendingTransition.HasValue.Should().BeTrue();
        var action = navigator.PendingTransition!.Value;
        action.Id.Should().Be("ladder-up");
        action.Type.Should().Be(TraversalTransitionType.Climb);
        action.SourceAddress.Should().Be(departure);
        action.DestinationAddress.Should().Be(landing);
        var sourceCenter = Vector2d.Right;
        var landingCenter = new Vector2d(Fixed64.Two, high + Fixed64.Half);
        action.SourcePosition.Should().Be(sourceCenter);
        action.DestinationPosition.Should().Be(landingCenter);
        navigator.Pose.Center.Should().Be(sourceCenter);
        navigator.GuideCost.Should().Be((Fixed64)5);

        int entriesAtDeparture = entries.Count;
        for (int i = 0; i < 3; i++)
        {
            Step();
            navigator.Pose.Center.Should().Be(sourceCenter);
            navigator.Velocity.Should().Be(Vector2d.Zero);
            navigator.LastCommittedCell!.Value.Address.Should().Be(departure);
            AssertHeld();
        }
        entries.Count.Should().Be(entriesAtDeparture);

        // The blocker begins tangent to the upper cap and clips the first host climb request.
        var blocker = AddBox(physics, new Vector2d(Fixed64.One, Fixed64.Half + Fixed64.Quarter),
            new Vector2d(Fixed64.Half, Fixed64.Half), HostY);
        Step(action, sourceCenter + Vector2d.Forward * Fixed64.Quarter);
        navigator.Pose.Center.Should().Be(sourceCenter);
        navigator.Velocity.Should().Be(Vector2d.Zero);
        AssertHeld();
        blocker.Deactivate();

        for (int i = 1; i <= 5; i++)
        {
            var center = sourceCenter + Vector2d.Forward * (i * Fixed64.Quarter);
            Step(action, center);
            navigator.Pose.Center.Should().Be(center);
            AssertHeld();
        }
        navigator.SupportStatus.Should().Be(NavigationSupportStatus2D.NoSupport);
        for (int i = 1; i <= 4; i++)
        {
            var center = landingCenter - Vector2d.Right * ((4 - i) * Fixed64.Quarter);
            Step(action, center);
            navigator.Pose.Center.Should().Be(center);
            if (i == 3)
                navigator.IsGrounded.Should().BeTrue("the shelf supports the capsule before the exact landing center");
            if (i < 4)
                AssertHeld();
        }
        navigator.IsGrounded.Should().BeTrue();
        navigator.Support!.Value.Point.Should().Be(new Vector2d(Fixed64.Two, high));
        navigator.LastCommittedCell!.Value.Address.Should().Be(landing);
        navigator.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.Success);
        navigator.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.Stale);
        navigator.PendingTransition.Should().BeNull();

        for (int i = 0; i < 8 && !navigator.HasArrived; i++)
            Step();
        navigator.HasArrived.Should().BeTrue();
        navigator.Pose.Center.Should().Be(new Vector2d((Fixed64)3, landingCenter.Y));
        navigator.LastCommittedCell!.Value.Address.Should().Be(goal);
        entries.Should().SatisfyRespectively(
            entry => AssertEntry(entry, start),
            entry => AssertEntry(entry, departure),
            entry => AssertEntry(entry, new NavigationCellAddress(MapId, new VoxelIndex(1, 1, 2))),
            entry => AssertEntry(entry, landing),
            entry => AssertEntry(entry, goal));
        return (frames, entries);

        NavigationCellAddress AddSupport(int x, int row, Fixed64 height)
        {
            var index = new VoxelIndex(x, 1, row);
            var left = new Vector2d((Fixed64)x - Fixed64.Half, height);
            builder.AddCell(index, floor).AddPlanarSupport(index, left, left + Vector2d.Right);
            return new NavigationCellAddress(MapId, index);
        }

        void AssertHeld()
        {
            navigator.PendingTransition!.Value.Should().Be(action);
            navigator.CompletePendingTransition(action).Should().Be(NavigationGuideStatus.InvalidEnd);
        }

        static void AssertEntry((int Frame, NavigationCellAddress? Address, TraversalMedium? Medium) entry,
            NavigationCellAddress address)
        {
            entry.Address.Should().Be(address);
            entry.Medium.Should().Be(TraversalMedium.Solid);
        }

        void Step(NavigationTransitionInstruction2D? instruction = null, Vector2d target = default)
        {
            navigation.Simulate();
            physics.Simulate();
            if (instruction is NavigationTransitionInstruction2D climb)
            {
                navigator.PrepareActionFrameMotion(climb, new NavigationPose2D(target, Fixed64.Zero))
                    .Should().Be(NavigationGuideStatus.Success);
                body.Agent.Transform.WorldPosition.Should().Be(target.ToVector3d(HostY));
            }
            else
                navigator.PrepareFrameMotion();
            physics.LateSimulate();
            navigator.CommitFrameMotion();
            navigation.LateSimulate();
            navigator.Pose.Center.Should().Be(body.Position);
            navigator.Pose.RotationRadians.Should().Be(body.Rotation);
            body.Collider.Center.Should().Be(body.Position);
            body.Agent.Transform.WorldPosition.Should().Be(body.Position.ToVector3d(HostY));
            frames.Add((navigator.Pose, navigator.Velocity, navigator.SupportStatus, navigator.IsGrounded,
                navigator.PendingTransition.HasValue, navigator.LastCommittedCell?.Address));
        }
    }
}

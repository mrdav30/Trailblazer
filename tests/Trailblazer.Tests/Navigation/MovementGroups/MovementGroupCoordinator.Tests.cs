using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using System;
using Trailblazer.Navigation.MovementGroups;
using Xunit;

namespace Trailblazer.Tests.Navigation;

[Collection("PathingCollection")]
public sealed class MovementGroupCoordinatorTests : IDisposable
{
    public MovementGroupCoordinatorTests()
    {
        if (TestWorld.IsActive)
            TestWorld.Reset();
        else
            TestWorld.Setup();

        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        TestWorld.World.TryAddGrid(config, out _);
        TestWorld.Context.Navigation.MovementGroups.Reset();
        TestWorld.Context.Reset();
    }

    public void Dispose()
    {
        TestWorld.Context.Navigation.MovementGroups.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Prewarm_ShouldIgnoreUngroupedSessions()
    {
        var session = new MovementGroupSession();

        TestWorld.Context.Navigation.MovementGroups.Prewarm(
            session,
            Guid.NewGuid(),
            new Vector3d(4, 0, 0),
            Vector3d.Zero,
            Fixed64.One);

        session.HasOwnerId.Should().BeFalse();
        session.GroupIndex.Should().Be(-1);
    }

    [Fact]
    public void UpdateTarget_ShouldRefreshMembershipWhenOwnerChanges()
    {
        TestWorld.Context.Simulate();

        var session = new MovementGroupSession { GroupId = 7 };
        var probe = new MovementGroupSession { GroupId = 7 };
        Guid initialOwner = Guid.NewGuid();
        Guid replacementOwner = Guid.NewGuid();
        Vector3d destination = new(5, 0, 0);

        TestWorld.Context.Navigation.MovementGroups.Prewarm(session, initialOwner, destination, Vector3d.Zero, Fixed64.One);
        TestWorld.Context.Navigation.MovementGroups.IsNeighbor(probe, initialOwner, destination, TestWorld.Context.FrameCount).Should().BeTrue();

        TestWorld.Context.Navigation.MovementGroups.CacheOwner(session, replacementOwner);
        TestWorld.Context.Navigation.MovementGroups.UpdateTarget(session, destination, Vector3d.Right, Fixed64.One)
            .TravelMode.Should().Be(MovementGroupTravelMode.Individual);

        TestWorld.Context.Navigation.MovementGroups.IsNeighbor(probe, initialOwner, destination, TestWorld.Context.FrameCount).Should().BeFalse();
        TestWorld.Context.Navigation.MovementGroups.IsNeighbor(probe, replacementOwner, destination, TestWorld.Context.FrameCount).Should().BeTrue();
    }

    [Fact]
    public void UpdateTarget_ShouldReturnFormationForClusteredMembers()
    {
        TestWorld.Context.Simulate();

        var first = new MovementGroupSession { GroupId = 3 };
        var second = new MovementGroupSession { GroupId = 3 };
        Vector3d destination = new(10, 0, 0);

        TestWorld.Context.Navigation.MovementGroups.Prewarm(first, Guid.NewGuid(), destination, Vector3d.Zero, Fixed64.One);
        TestWorld.Context.Navigation.MovementGroups.Prewarm(second, Guid.NewGuid(), destination, new Vector3d(1, 0, 0), Fixed64.One);

        MovementGroupTarget target = TestWorld.Context.Navigation.MovementGroups.UpdateTarget(first, destination, Vector3d.Zero, Fixed64.One);

        target.TravelMode.Should().Be(MovementGroupTravelMode.Formation);
        target.Destination.Should().NotBe(destination);
    }

    [Fact]
    public void UpdateTarget_ShouldFallbackToGroupIndividualWhenFormationSpreadIsTooLarge()
    {
        TestWorld.Context.Simulate();

        var first = new MovementGroupSession { GroupId = 4 };
        var second = new MovementGroupSession { GroupId = 4 };
        Vector3d destination = new(25, 0, 0);

        TestWorld.Context.Navigation.MovementGroups.Prewarm(first, Guid.NewGuid(), destination, Vector3d.Zero, Fixed64.One);
        TestWorld.Context.Navigation.MovementGroups.Prewarm(second, Guid.NewGuid(), destination, new Vector3d(20, 0, 0), Fixed64.One);

        MovementGroupTarget target = TestWorld.Context.Navigation.MovementGroups.UpdateTarget(first, destination, Vector3d.Zero, Fixed64.One);

        target.TravelMode.Should().Be(MovementGroupTravelMode.GroupIndividual);
        target.Destination.Should().Be(destination);
    }

    [Fact]
    public void Remove_ShouldDropMembershipWhenSessionPointsAtMissingGroup()
    {
        TestWorld.Context.Simulate();

        var session = new MovementGroupSession { GroupId = 6 };
        var probe = new MovementGroupSession { GroupId = 6 };
        Guid ownerId = Guid.NewGuid();
        Vector3d destination = new(3, 0, 0);

        TestWorld.Context.Navigation.MovementGroups.Prewarm(session, ownerId, destination, Vector3d.Zero, Fixed64.One);
        TestWorld.Context.Navigation.MovementGroups.IsNeighbor(probe, ownerId, destination, TestWorld.Context.FrameCount).Should().BeTrue();

        session.GroupId = 99;
        TestWorld.Context.Navigation.MovementGroups.Remove(session);

        TestWorld.Context.Navigation.MovementGroups.IsNeighbor(probe, ownerId, destination, TestWorld.Context.FrameCount).Should().BeFalse();
        session.GroupIndex.Should().Be(-1);
    }
}

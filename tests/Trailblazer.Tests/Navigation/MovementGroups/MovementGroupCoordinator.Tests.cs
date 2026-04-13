using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using System;
using Trailblazer.Navigation.MovementGroups;
using Xunit;

namespace Trailblazer.Tests.Navigation;

[Collection("PathingCollection")]
public sealed class MovementGroupCoordinatorTests : IDisposable
{
    public MovementGroupCoordinatorTests()
    {
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();

        var config = new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8));
        GlobalGridManager.TryAddGrid(config, out _);
        MovementGroupCoordinator.Reset();
        TrailblazerManager.Reset();
    }

    public void Dispose()
    {
        MovementGroupCoordinator.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void Prewarm_ShouldIgnoreUngroupedSessions()
    {
        var session = new MovementGroupSession();

        MovementGroupCoordinator.Prewarm(
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
        TrailblazerManager.Simulate();

        var session = new MovementGroupSession { GroupId = 7 };
        var probe = new MovementGroupSession { GroupId = 7 };
        Guid initialOwner = Guid.NewGuid();
        Guid replacementOwner = Guid.NewGuid();
        Vector3d destination = new(5, 0, 0);

        MovementGroupCoordinator.Prewarm(session, initialOwner, destination, Vector3d.Zero, Fixed64.One);
        MovementGroupCoordinator.IsNeighbor(probe, initialOwner, destination, TrailblazerManager.FrameCount).Should().BeTrue();

        MovementGroupCoordinator.CacheOwner(session, replacementOwner);
        MovementGroupCoordinator.UpdateTarget(session, destination, Vector3d.Right, Fixed64.One)
            .TravelMode.Should().Be(MovementGroupTravelMode.Individual);

        MovementGroupCoordinator.IsNeighbor(probe, initialOwner, destination, TrailblazerManager.FrameCount).Should().BeFalse();
        MovementGroupCoordinator.IsNeighbor(probe, replacementOwner, destination, TrailblazerManager.FrameCount).Should().BeTrue();
    }

    [Fact]
    public void UpdateTarget_ShouldReturnFormationForClusteredMembers()
    {
        TrailblazerManager.Simulate();

        var first = new MovementGroupSession { GroupId = 3 };
        var second = new MovementGroupSession { GroupId = 3 };
        Vector3d destination = new(10, 0, 0);

        MovementGroupCoordinator.Prewarm(first, Guid.NewGuid(), destination, Vector3d.Zero, Fixed64.One);
        MovementGroupCoordinator.Prewarm(second, Guid.NewGuid(), destination, new Vector3d(1, 0, 0), Fixed64.One);

        MovementGroupTarget target = MovementGroupCoordinator.UpdateTarget(first, destination, Vector3d.Zero, Fixed64.One);

        target.TravelMode.Should().Be(MovementGroupTravelMode.Formation);
        target.Destination.Should().NotBe(destination);
    }

    [Fact]
    public void UpdateTarget_ShouldFallbackToGroupIndividualWhenFormationSpreadIsTooLarge()
    {
        TrailblazerManager.Simulate();

        var first = new MovementGroupSession { GroupId = 4 };
        var second = new MovementGroupSession { GroupId = 4 };
        Vector3d destination = new(25, 0, 0);

        MovementGroupCoordinator.Prewarm(first, Guid.NewGuid(), destination, Vector3d.Zero, Fixed64.One);
        MovementGroupCoordinator.Prewarm(second, Guid.NewGuid(), destination, new Vector3d(20, 0, 0), Fixed64.One);

        MovementGroupTarget target = MovementGroupCoordinator.UpdateTarget(first, destination, Vector3d.Zero, Fixed64.One);

        target.TravelMode.Should().Be(MovementGroupTravelMode.GroupIndividual);
        target.Destination.Should().Be(destination);
    }

    [Fact]
    public void Remove_ShouldDropMembershipWhenSessionPointsAtMissingGroup()
    {
        TrailblazerManager.Simulate();

        var session = new MovementGroupSession { GroupId = 6 };
        var probe = new MovementGroupSession { GroupId = 6 };
        Guid ownerId = Guid.NewGuid();
        Vector3d destination = new(3, 0, 0);

        MovementGroupCoordinator.Prewarm(session, ownerId, destination, Vector3d.Zero, Fixed64.One);
        MovementGroupCoordinator.IsNeighbor(probe, ownerId, destination, TrailblazerManager.FrameCount).Should().BeTrue();

        session.GroupId = 99;
        MovementGroupCoordinator.Remove(session);

        MovementGroupCoordinator.IsNeighbor(probe, ownerId, destination, TrailblazerManager.FrameCount).Should().BeFalse();
        session.GroupIndex.Should().Be(-1);
    }
}

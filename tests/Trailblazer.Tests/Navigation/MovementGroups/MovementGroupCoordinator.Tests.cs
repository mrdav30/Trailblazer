using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using Trailblazer.Navigation.MovementGroups;
using Xunit;

namespace Trailblazer.Tests.Navigation;

[Collection("PathingCollection")]
public sealed class MovementGroupCoordinatorTests : IDisposable
{
    private static readonly Guid FirstOwnerId = new("10000000-0000-0000-0000-000000000001");
    private static readonly Guid SecondOwnerId = new("10000000-0000-0000-0000-000000000002");

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
    public void UpdateTarget_ShouldLeaveUngroupedDestinationUnchanged()
    {
        var session = new MovementGroupSession();
        Vector3d destination = new(4, 0, 2);

        MovementGroupTarget target = TestWorld.Context.Navigation.MovementGroups.UpdateTarget(
            session,
            destination,
            Vector3d.Right,
            Fixed64.One);

        target.TravelMode.Should().Be(MovementGroupTravelMode.None);
        target.Destination.Should().Be(destination);
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
    public void UpdateTarget_ShouldUseExplicitWorldPaddingInsteadOfVoxelSize()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        TestWorld.Setup(new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            defaults.MaintenanceBudget,
            defaults.GuideSampleBudget,
            movementGroupPadding: Fixed64.Zero,
            defaults.MaxIngressEntries,
            defaults.MaxIngressBytes,
            defaults.MaxActiveSnapshots,
            defaults.MaxActiveSnapshotBytes,
            defaults.MaxRetiredSnapshots,
            defaults.MaxRetiredSnapshotBytes,
            defaults.MaxPersistentGraphPages,
            defaults.MaxDynamicCellSlotsPerMap,
            defaults.MaxDynamicCellSlots,
            defaults.NavigationAreaCount,
            defaults.MaxAreaPolicies,
            defaults.MaxAreaRulesPerPolicy,
            defaults.MaxAreaRules,
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits));

        var first = new MovementGroupSession { GroupId = 5 };
        var second = new MovementGroupSession { GroupId = 5 };
        Vector3d destination = new(25, 0, 0);

        TestWorld.Context.Navigation.MovementGroups.Prewarm(
            first,
            Guid.NewGuid(),
            destination,
            Vector3d.Zero,
            Fixed64.One);
        TestWorld.Context.Navigation.MovementGroups.Prewarm(
            second,
            Guid.NewGuid(),
            destination,
            new Vector3d(Fixed64.FromFraction(9, 2), Fixed64.Zero, Fixed64.Zero),
            Fixed64.One);

        MovementGroupTarget target = TestWorld.Context.Navigation.MovementGroups.UpdateTarget(
            first,
            destination,
            Vector3d.Zero,
            Fixed64.One);

        target.TravelMode.Should().Be(MovementGroupTravelMode.GroupIndividual);
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

    [Fact]
    public void IsNeighbor_ShouldRequireMatchingGroupDestinationAndFreshFrame()
    {
        TestWorld.Context.Simulate();

        var member = new MovementGroupSession { GroupId = 12 };
        Guid ownerId = FirstOwnerId;
        Vector3d destination = new(8, 0, 0);
        int frame = TestWorld.Context.FrameCount;
        TestWorld.Context.Navigation.MovementGroups.Prewarm(
            member,
            ownerId,
            destination,
            Vector3d.Zero,
            Fixed64.One);

        TestWorld.Context.Navigation.MovementGroups.IsNeighbor(
                new MovementGroupSession { GroupId = 13 },
                ownerId,
                destination,
                frame)
            .Should().BeFalse();
        TestWorld.Context.Navigation.MovementGroups.IsNeighbor(
                new MovementGroupSession { GroupId = 12 },
                ownerId,
                destination + Vector3d.Right,
                frame)
            .Should().BeFalse();
        TestWorld.Context.Navigation.MovementGroups.IsNeighbor(
                new MovementGroupSession { GroupId = 12 },
                ownerId,
                destination,
                frame + 2)
            .Should().BeFalse();
    }

    [Fact]
    public void Remove_ShouldRemainIdempotentWhileOtherGroupMembersRemain()
    {
        TestWorld.Context.Simulate();

        var first = new MovementGroupSession { GroupId = 14 };
        var second = new MovementGroupSession { GroupId = 14 };
        Guid firstOwner = FirstOwnerId;
        Guid secondOwner = SecondOwnerId;
        Vector3d destination = new(6, 0, 0);
        TestWorld.Context.Navigation.MovementGroups.Prewarm(
            first,
            firstOwner,
            destination,
            Vector3d.Zero,
            Fixed64.One);
        TestWorld.Context.Navigation.MovementGroups.Prewarm(
            second,
            secondOwner,
            destination,
            Vector3d.Right,
            Fixed64.One);

        TestWorld.Context.Navigation.MovementGroups.Remove(first);
        TestWorld.Context.Navigation.MovementGroups.Remove(first);

        var probe = new MovementGroupSession { GroupId = 14 };
        first.GroupIndex.Should().Be(-1);
        TestWorld.Context.Navigation.MovementGroups.IsNeighbor(
                probe,
                firstOwner,
                destination,
                TestWorld.Context.FrameCount)
            .Should().BeFalse();
        TestWorld.Context.Navigation.MovementGroups.IsNeighbor(
                probe,
                secondOwner,
                destination,
                TestWorld.Context.FrameCount)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UpdateTarget_ShouldExcludeMembersWithDifferentDestinationOrStaleFrame(bool makeStale)
    {
        TestWorld.Context.Simulate();

        var first = new MovementGroupSession { GroupId = 15 };
        var second = new MovementGroupSession { GroupId = 15 };
        Vector3d destination = new(10, 0, 0);
        TestWorld.Context.Navigation.MovementGroups.Prewarm(
            first,
            FirstOwnerId,
            destination,
            Vector3d.Zero,
            Fixed64.One);
        TestWorld.Context.Navigation.MovementGroups.Prewarm(
            second,
            SecondOwnerId,
            destination,
            Vector3d.Right,
            Fixed64.One);

        if (makeStale)
        {
            TestWorld.Context.Simulate();
            TestWorld.Context.Simulate();
        }
        else
        {
            TestWorld.Context.Navigation.MovementGroups.UpdateTarget(
                second,
                destination + Vector3d.Right,
                Vector3d.Right,
                Fixed64.One);
        }

        MovementGroupTarget target = TestWorld.Context.Navigation.MovementGroups.UpdateTarget(
            first,
            destination,
            Vector3d.Zero,
            Fixed64.One);

        target.TravelMode.Should().Be(MovementGroupTravelMode.Individual);
        target.Destination.Should().Be(destination);
    }
}

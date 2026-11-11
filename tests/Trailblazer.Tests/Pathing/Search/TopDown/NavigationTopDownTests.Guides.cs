using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed partial class NavigationTopDownTests
{
    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void Guide_RetainsTheExactDetourAndInvalidatesOnSemanticPublication(PathAlgorithm algorithm)
    {
        using var context = CreateContext();
        var grid = new GridConfiguration(new Vector3d(-1, 0, -1), new Vector3d(5, 1, 2));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        var builder = new NavigationMapBuilder("plane", grid).SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One);
        for (int x = 0; x <= 4; x++)
        {
            if (x != 2)
                builder.AddCell(RouteCell(x, 0).Index, Open);
            builder.AddCell(RouteCell(x, 1).Index, Open);
        }
        Publish(context, builder.Build(), 2);

        var start = RouteCell(0, 0);
        var end = RouteCell(4, 0);
        var expected = new NavigationCellAddress[7];
        FindRoute(context, start, end, Fixed64.Quarter, algorithm, expected, out int count, out Fixed64 cost)
            .Should().Be(NavigationGuideStatus.Success);
        RequestTopDownGuide(context, start, end, algorithm, out var result).Should().Be(NavigationGuideStatus.Success);
        using var guide = result;
        RequestTopDownGuide(context, start, end, algorithm, out result).Should().Be(NavigationGuideStatus.Success);
        using var independent = result;
        guide.Status.Should().Be(NavigationGuideStatus.Success);
        guide.StepCount.Should().Be(count);
        guide.TotalCost.Should().Be(cost);
        context.Pathing.NavigationGraphStore.ActiveLeaseCount.Should().Be(0);
        for (int i = 0; i < count; i++)
        {
            guide.CurrentStepIndex.Should().Be(i);
            guide.TryGetCurrentStep(out var address, out var center).Should().Be(NavigationGuideStatus.Success);
            address.Should().Be(expected[i]);
            center.Should().Be(new Vector2d((Fixed64)(address.Index.x - 1), (Fixed64)(address.Index.z - 1)));
            guide.TryAdvanceStep().Should().Be(NavigationGuideStatus.Success);
        }
        guide.CurrentStepIndex.Should().Be(count - 1);
        independent.CurrentStepIndex.Should().Be(0);

        ulong sequence = context.World.ChangeSequence;
        RouteOverlay(context, NavigationCellOverlayOperation.Set(RouteCell(2, 0).Index, Open), 3);
        context.World.ChangeSequence.Should().Be(sequence);
        guide.Status.Should().Be(NavigationGuideStatus.Stale);
        independent.TryGetCurrentStep(out var staleAddress, out var staleCenter).Should().Be(NavigationGuideStatus.Stale);
        staleAddress.Should().Be(default(NavigationCellAddress));
        staleCenter.Should().Be(Vector2d.Zero);
        independent.TryAdvanceStep().Should().Be(NavigationGuideStatus.Stale);
        independent.CurrentStepIndex.Should().Be(0);
        RequestTopDownGuide(context, start, end, algorithm, out result).Should().Be(NavigationGuideStatus.Success);
        using var replacement = result;
        replacement.TotalCost.Should().Be((Fixed64)4);
    }

    [Theory]
    [InlineData(PathAlgorithm.AStar)]
    [InlineData(PathAlgorithm.FlowField)]
    public void Guide_PortalRetainsExactCentersAndCapacityIsSharedWithAStar(PathAlgorithm algorithm)
    {
        using var context = CreateContext();
        var grid = new GridConfiguration(new Vector3d(-1, 0, -1), new Vector3d(5, 1, 2));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        var builder = new NavigationMapBuilder("plane", grid).SetPlanarLayer(NavigationMode2D.TopDown, 1, Fixed64.One);
        for (int x = 0; x <= 4; x++)
            builder.AddCell(RouteCell(x, 1).Index, Open);
        Publish(context, builder.Build(), 2);

        var start = RouteCell(0, 1);
        var end = RouteCell(4, 1);
        RequestTopDownGuide(context, start, end, algorithm, out var rejected, Fixed64.Half + Fixed64.MinIncrement)
            .Should().Be(NavigationGuideStatus.InvalidStart);
        rejected.Status.Should().Be(NavigationGuideStatus.Stale);
        RequestTopDownGuide(context, start, end, algorithm, out rejected, Fixed64.Half, RouteBudget(edges: 0))
            .Should().Be(NavigationGuideStatus.BudgetExceeded);
        rejected.StepCount.Should().Be(0);

        NavigationAStarPayloadCache cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        var leases = new NavigationTopDownGuideLease[context.Settings.QueryLimits.MaxAStarActivePayloadLeases];
        try
        {
            for (int i = 0; i < leases.Length; i++)
                RequestTopDownGuide(context, start, end, algorithm, out leases[i], Fixed64.Half)
                    .Should().Be(NavigationGuideStatus.Success);
            cache.ActiveLeaseCount.Should().Be(leases.Length);
            cache.ReservedLeaseCount.Should().Be(0);
            cache.Count.Should().Be(0);
            cache.LeasedBytes.Should().Be(NavigationPlanarGuidePayload.MeasureBytes(5) * leases.Length);
            cache.DetachedBytes.Should().Be(cache.LeasedBytes);
            cache.TryReservePayload(0, out _).Should().BeFalse();
            RequestTopDownGuide(context, start, end, algorithm, out rejected, Fixed64.Half)
                .Should().Be(NavigationGuideStatus.CapacityExceeded);
            rejected.CurrentStepIndex.Should().Be(-1);
            leases[0].StepCount.Should().Be(5);
            leases[0].TotalCost.Should().Be((Fixed64)4);
            leases[0].TryGetCurrentStep(out var address, out var center).Should().Be(NavigationGuideStatus.Success);
            address.Should().Be(start);
            center.Should().Be(new Vector2d(Fixed64.Zero, Fixed64.One));

            var alias = leases[0];
            leases[0].Dispose();
            RequestTopDownGuide(context, start, end, algorithm, out leases[0], Fixed64.Half)
                .Should().Be(NavigationGuideStatus.Success);
            alias.Dispose();
            alias.TryAdvanceStep().Should().Be(NavigationGuideStatus.Stale);
            leases[0].Status.Should().Be(NavigationGuideStatus.Success);
            leases[0].CurrentStepIndex.Should().Be(0);
        }
        finally
        {
            foreach (var lease in leases)
                lease.Dispose();
        }
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(0);
    }

    [Fact]
    public void Guide_RejectsRawChangesResetAndAttachedContextDisposal()
    {
        using var world = new GridWorld();
        using var context = TrailblazerWorldContext.Attach(world);
        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, 1)).Should().BeTrue();
        var grid = new GridConfiguration(new Vector3d(-1, 0, -1), new Vector3d(1, 1, 1));
        world.TryAddGrid(grid, out _).Should().BeTrue();
        var map = TopDown("plane", grid).SetDefaultCell(Open).Build();
        Publish(context, map, 2);
        var address = RouteCell(0, 0);
        var store = context.Pathing.NavigationGraphStore;

        RequestTopDownGuide(context, address, address, PathAlgorithm.FlowField, out var result)
            .Should().Be(NavigationGuideStatus.Success);
        using var original = result;
        store.MarkSafetyPending();
        original.TryAdvanceStep().Should().Be(NavigationGuideStatus.CapacityExceeded);
        store.ClearSafetyPending();
        original.Status.Should().Be(NavigationGuideStatus.Success);
        long publishedVersion = store.Current.GraphVersion;
        world.TryAddGrid(new GridConfiguration(new Vector3d(10, 0, 10), new Vector3d(11, 1, 11)), out _)
            .Should().BeTrue();
        store.Current.GraphVersion.Should().Be(publishedVersion);
        original.TryAdvanceStep().Should().Be(NavigationGuideStatus.Stale);
        context.Simulate();

        RequestTopDownGuide(context, address, address, PathAlgorithm.AStar, out result)
            .Should().Be(NavigationGuideStatus.Success);
        using var beforeReset = result;
        var rawSequence = world.ChangeSequence;
        context.Pathing.Reset();
        world.ChangeSequence.Should().Be(rawSequence);
        beforeReset.Status.Should().Be(NavigationGuideStatus.Stale);

        context.Pathing.Admit(new NavigationAreaPolicyCommitOperation(Policy, 1, context.FrameCount + 1)).Should().BeTrue();
        Publish(context, map, 2);
        RequestTopDownGuide(context, address, address, PathAlgorithm.FlowField, out result)
            .Should().Be(NavigationGuideStatus.Success);
        using var beforeDispose = result;
        context.Dispose();
        world.IsActive.Should().BeTrue();
        beforeDispose.TryGetCurrentStep(out _, out _).Should().Be(NavigationGuideStatus.Stale);
    }

    [Fact]
    public void Guide_UsesOneSnapshotLeaseAndRespectsTheExactByteLimit()
    {
        using var context = CreateContext();
        var grid = new GridConfiguration(new Vector3d(-1, 0, -1), new Vector3d(1, 1, 1));
        context.World.TryAddGrid(grid, out _).Should().BeTrue();
        Publish(context, TopDown("plane", grid).SetDefaultCell(Open).Build(), 2);
        var root = context.Pathing.NavigationGraphStore.Current;
        using var store = new NavigationWorldGraphStore(4, 2, long.MaxValue, long.MaxValue, int.MaxValue, 1);
        store.TryPublish(root).Should().Be(NavigationCandidatePublication.Published);
        var address = RouteCell(0, 0);
        var request = new NavigationPlanarRouteRequest(address, address, Fixed64.Quarter, Policy.Key,
            TraversalMedium.Solid, TraversalCapability.None, RouteBudget());
        var workspace = new NavigationAStarWorkspace(1, 1, 1, 2, 8, 0, 2);
        var ray = new NavigationRayWork(workspace.RayWorkspace);
        long exact = NavigationPlanarGuidePayload.MeasureBytes(1);
        var small = new NavigationAStarPayloadCache(context.World, 1, exact, exact - 1, exact, 2);
        Find(small, out var guide).Should().Be(NavigationGuideStatus.CapacityExceeded);
        guide.Status.Should().Be(NavigationGuideStatus.Stale);
        small.ReservedPayloadBytes.Should().Be(0);

        var cache = new NavigationAStarPayloadCache(context.World, 1, exact, exact, exact, 2);
        Find(cache, out guide).Should().Be(NavigationGuideStatus.Success);
        using var first = guide;
        first.Status.Should().Be(NavigationGuideStatus.Success);
        store.ActiveLeaseCount.Should().Be(0);
        Find(cache, out guide).Should().Be(NavigationGuideStatus.CapacityExceeded);
        first.Dispose();
        Find(cache, out guide).Should().Be(NavigationGuideStatus.Success);
        guide.Dispose();
        cache.LeasedBytes.Should().Be(0);

        NavigationGuideStatus Find(NavigationAStarPayloadCache retention, out NavigationTopDownGuideLease result)
        {
            var status = NavigationPlanarRouteWork.Find(context.World, store, request, workspace, ray, null, default,
                out _, out _, retention, out var lease);
            result = lease == null ? default : new NavigationTopDownGuideLease(lease);
            return status;
        }
    }

    [Fact]
    public void Guide_DetachedPublicationRefusesPendingOrChangedEvidence()
    {
        using var context = CreateContext();
        var store = context.Pathing.NavigationGraphStore;
        var world = context.World;
        var cache = context.Pathing.NavigationAStarAdmissionGate.PayloadCache;
        var payload = new NavigationPlanarGuidePayload(
            new[] { new NavigationAStarGuidePoint(RouteCell(0, 0), Vector3d.Zero, TraversalMedium.Solid) },
            Fixed64.Zero, Policy.Key, store.Current.GraphVersion, (world.SpawnToken, world.Version, world.ChangeSequence));
        cache.TryReservePayload(payload.RetainedBytes, out var reservation).Should().BeTrue();
        try
        {
            store.MarkSafetyPending();
            cache.PublishPlanarPayload(store, payload, ref reservation, out var guide)
                .Should().Be(NavigationGuideStatus.CapacityExceeded);
            guide.Should().BeNull();
            store.ClearSafetyPending();
            context.Pathing.Reset();
            cache.PublishPlanarPayload(store, payload, ref reservation, out guide).Should().Be(NavigationGuideStatus.Stale);
            guide.Should().BeNull();
            cache.ActiveLeaseCount.Should().Be(0);
        }
        finally
        {
            cache.ReleasePayloadReservation(ref reservation);
        }
        cache.ReservedLeaseCount.Should().Be(0);
    }

    private static NavigationGuideStatus RequestTopDownGuide(TrailblazerWorldContext context,
        NavigationCellAddress start, NavigationCellAddress end, PathAlgorithm algorithm, out NavigationTopDownGuideLease result,
        Fixed64? radius = null, NavigationWorkBudget? budget = null) =>
        context.Guides.RequestTopDownGuide(start, end, radius ?? Fixed64.Quarter, Policy.Key, TraversalMedium.Solid,
            TraversalCapability.None, algorithm, budget ?? RouteBudget(), out result);
}

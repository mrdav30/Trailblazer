//=======================================================================
// NavigationQueryAdmissionTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationQueryAdmissionTests
{
    [Theory]
    [InlineData(0, 1_000_000)]
    [InlineData(2, 0)]
    [InlineData(0, 0)]
    public void DisabledRetirement_ShouldAllowReadersAndBlockPublicationUntilReturn(
        int maxRetiredSnapshots,
        long maxRetiredBytes)
    {
        using var store = new NavigationWorldGraphStore(
            maxActiveSnapshots: 3,
            maxRetiredSnapshots,
            maxRetiredBytes,
            maxActiveBytes: 1_000_000,
            maxPersistentPages: 1_000,
            maxConcurrentLeases: 2,
            maxResultBytes: 1_000_000);

        NavigationWorldGraphLease lease = store.TryAcquire()!;

        lease.Should().NotBeNull();
        store.CanPublish.Should().BeFalse();
        store.TryPublish(NavigationWorldGraph.CreateEmpty(1))
            .Should().Be(NavigationCandidatePublication.Deferred);

        lease.Dispose();
        store.CanPublish.Should().BeTrue();
        store.TryPublish(NavigationWorldGraph.CreateEmpty(1))
            .Should().Be(NavigationCandidatePublication.Published);
    }

    [Fact]
    public void DisabledRetirement_ShouldStillAdmitQueryWhileNoWriterIsPending()
    {
        TrailblazerWorldContextSettings settings = QuerySettings(
            maxConcurrentQueries: 1,
            maxRetiredSnapshots: 0,
            maxRetiredSnapshotBytes: 0);
        using var store = CreateStore(settings);
        using var workspaces = new PathQueryWorkspacePool(settings);
        using var admission = new NavigationQueryAdmissionGate(store, workspaces, settings);

        admission.TryAdmit(
                new NavigationQueryAdmissionRequest(1, 4, 1),
                out NavigationQueryAdmissionLease? query)
            .Should().BeTrue();
        store.CanPublish.Should().BeFalse();

        query!.Dispose();
        store.CanPublish.Should().BeTrue();
    }

    [Fact]
    public void GraphStore_ShouldBoundConcurrentLeasesAndCloseAdmissionOnDispose()
    {
        var store = new NavigationWorldGraphStore(
            maxActiveSnapshots: 3,
            maxRetiredSnapshots: 2,
            maxRetiredBytes: 1_000_000,
            maxActiveBytes: 1_000_000,
            maxPersistentPages: 1_000,
            maxConcurrentLeases: 2,
            maxResultBytes: 1_000_000);

        NavigationWorldGraphLease first = store.TryAcquire()!;
        NavigationWorldGraphLease second = store.TryAcquire()!;

        first.Should().NotBeNull();
        second.Should().NotBeNull();
        store.ActiveLeaseCount.Should().Be(2);
        store.TryAcquire().Should().BeNull();

        first.Dispose();
        using NavigationWorldGraphLease replacement = store.TryAcquire()!;
        replacement.Should().NotBeNull();

        store.Dispose();
        store.TryAcquire().Should().BeNull();
        second.Dispose();
        store.ActiveLeaseCount.Should().Be(1);
    }

    [Fact]
    public void BatchAdmission_ShouldReserveDeterministicOrdinalPrefix()
    {
        TrailblazerWorldContextSettings settings = QuerySettings(
            maxConcurrentQueries: 3,
            maxActiveWorkspaceBytes: 10_000,
            maxActiveResultBytes: 100);
        var store = CreateStore(settings);
        var workspaces = new PathQueryWorkspacePool(settings);
        using var admission = new NavigationQueryAdmissionGate(store, workspaces, settings);
        var requests = new[]
        {
            new NavigationQueryAdmissionRequest(operationOrdinal: 20, minimumNodeCapacity: 4, maximumResultBytes: 40),
            new NavigationQueryAdmissionRequest(operationOrdinal: 10, minimumNodeCapacity: 4, maximumResultBytes: 70),
            new NavigationQueryAdmissionRequest(operationOrdinal: 30, minimumNodeCapacity: 4, maximumResultBytes: 10)
        };
        var leases = new NavigationQueryAdmissionLease?[requests.Length];

        int admitted = admission.AdmitBatch(requests, leases);

        admitted.Should().Be(1);
        leases[0].Should().BeNull();
        leases[1].Should().NotBeNull();
        leases[2].Should().BeNull();
        admission.ActiveResultBytes.Should().Be(70);
        store.ActiveLeaseCount.Should().Be(1);
        workspaces.ActiveCount.Should().Be(1);

        leases[1]!.Dispose();
        admission.ActiveResultBytes.Should().Be(0);
        store.ActiveLeaseCount.Should().Be(0);
        workspaces.ActiveCount.Should().Be(0);
    }

    [Fact]
    public void AdHocAdmission_ShouldUseOneBoundedSnapshotWorkspaceAndResultReservation()
    {
        TrailblazerWorldContextSettings settings = QuerySettings(
            maxConcurrentQueries: 1,
            maxActiveWorkspaceBytes: 1_000,
            maxActiveResultBytes: 64);
        var store = CreateStore(settings);
        var workspaces = new PathQueryWorkspacePool(settings);
        using var admission = new NavigationQueryAdmissionGate(store, workspaces, settings);

        admission.TryAdmit(
                new NavigationQueryAdmissionRequest(0, 4, 64),
                out NavigationQueryAdmissionLease? first)
            .Should().BeTrue();
        admission.TryAdmit(
                new NavigationQueryAdmissionRequest(1, 4, 1),
                out _)
            .Should().BeFalse();

        first!.Graph.Should().BeSameAs(store.Current);
        first.Workspace.NodeCapacity.Should().BeGreaterThanOrEqualTo(4);
        admission.ActiveCount.Should().Be(1);
        first.Dispose();
        admission.ActiveCount.Should().Be(0);

        admission.TryAdmit(
                new NavigationQueryAdmissionRequest(2, 4, 1),
                out NavigationQueryAdmissionLease? next)
            .Should().BeTrue();
        next!.Dispose();
    }

    [Fact]
    public void WorkspaceTrim_ShouldEvictLargestRetainedWorkspaceFirst()
    {
        TrailblazerWorldContextSettings settings = QuerySettings(
            maxConcurrentQueries: 2,
            maxActiveWorkspaceBytes: 10_000,
            maxRetainedWorkspaceBytes: 200);
        var pool = new PathQueryWorkspacePool(settings);
        pool.TryCheckout(8, out PathQueryWorkspaceLease? large).Should().BeTrue();
        pool.TryCheckout(4, out PathQueryWorkspaceLease? small).Should().BeTrue();

        large!.Dispose();
        small!.Dispose();

        pool.RetainedBytes.Should().Be(new PathQueryWorkspace(4).RetainedBytes);
        pool.TryCheckout(4, out PathQueryWorkspaceLease? reused).Should().BeTrue();
        reused!.Workspace.NodeCapacity.Should().Be(4);
        reused.Dispose();
    }

    [Fact]
    public void WorkspaceTrim_ShouldUseStableSlotOrderForEqualSizes()
    {
        TrailblazerWorldContextSettings settings = QuerySettings(
            maxConcurrentQueries: 2,
            maxActiveWorkspaceBytes: 10_000,
            maxRetainedWorkspaceBytes: new PathQueryWorkspace(4).RetainedBytes);
        var pool = new PathQueryWorkspacePool(settings);
        pool.TryCheckout(4, out PathQueryWorkspaceLease? first).Should().BeTrue();
        pool.TryCheckout(4, out PathQueryWorkspaceLease? second).Should().BeTrue();

        first!.Dispose();
        second!.Dispose();
        pool.TryCheckout(4, out PathQueryWorkspaceLease? retained).Should().BeTrue();

        retained.Should().BeSameAs(second);
        retained!.Dispose();
    }

    [Fact]
    public async Task ConcurrentAdmission_ShouldNeverExceedAggregateLimits()
    {
        TrailblazerWorldContextSettings settings = QuerySettings(maxConcurrentQueries: 2);
        var store = CreateStore(settings);
        var workspaces = new PathQueryWorkspacePool(settings);
        using var admission = new NavigationQueryAdmissionGate(store, workspaces, settings);
        var leases = new NavigationQueryAdmissionLease?[8];

        await Task.WhenAll(Enumerable.Range(0, leases.Length).Select(index => Task.Run(() =>
            admission.TryAdmit(
                new NavigationQueryAdmissionRequest(index, 4, 1),
                out leases[index]))));

        leases.Count(lease => lease != null).Should().Be(2);
        store.ActiveLeaseCount.Should().Be(2);
        workspaces.ActiveCount.Should().Be(2);
        admission.ActiveResultBytes.Should().Be(2);
        for (int i = 0; i < leases.Length; i++)
            leases[i]?.Dispose();
    }

    [Fact]
    public void ContextResetAndDispose_ShouldLeaveCheckedOutQuerySafeToReturn()
    {
        TrailblazerWorldContextSettings settings = QuerySettings(maxConcurrentQueries: 1);
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(settings: settings);
        context.Pathing.TryAdmitNavigationQuery(
                new NavigationQueryAdmissionRequest(0, 4, 1),
                out NavigationQueryAdmissionLease? lease)
            .Should().BeTrue();

        context.Reset();
        NavigationWorldGraph graph = lease!.Graph;
        context.Dispose();

        graph.Should().NotBeNull();
        Action release = () => lease.Dispose();
        release.Should().NotThrow();
    }

    [Fact]
    public void GraphAccounting_ShouldIncludeCatalogDirectoryMapIndexAndCompositionRoots()
    {
        NavigationAreaCatalog emptyCatalog = NavigationAreaCatalog.Empty;
        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("safe", 1),
            new[] { default(NavigationAreaRule), default(NavigationAreaRule) });
        emptyCatalog.TryPublish(policy, 8, 2, 8, 8, out NavigationAreaCatalog catalog)
            .Should().Be(NavigationOperationRejection.None);

        var graph = new NavigationWorldGraph(1, Array.Empty<NavigationMapInstance>(), catalog);

        graph.RetainedBytes.Should().Be(
            NavigationWorldGraph.BaseRetainedBytes
            + NavigationInstanceDirectory.Create(Array.Empty<NavigationMapInstance>()).RetainedBytes
            + NavigationWorldGraph.EmptyMapIndexRetainedBytes
            + NavigationWorldGraph.EmptyClosedStructuralComponentsRetainedBytes
            + NavigationCompositionIndex.Empty.RetainedBytes
            + catalog.RetainedBytes);
        graph.PersistentPageCount.Should().Be(
            NavigationInstanceDirectory.Create(Array.Empty<NavigationMapInstance>()).PersistentPageCount
            + NavigationWorldGraph.EmptyMapIndexPersistentPageCount
            + NavigationWorldGraph.EmptyClosedStructuralComponentsPersistentPageCount
            + NavigationCompositionIndex.Empty.PersistentPageCount
            + catalog.PersistentPageCount);
    }

    private static NavigationWorldGraphStore CreateStore(TrailblazerWorldContextSettings settings) => new(
        settings.MaxActiveSnapshots,
        settings.MaxRetiredSnapshots,
        settings.MaxRetiredSnapshotBytes,
        settings.MaxActiveSnapshotBytes,
        settings.MaxPersistentGraphPages,
        settings.MaxConcurrentPathQueries,
        settings.MaxActiveQueryResultBytes);

    private static TrailblazerWorldContextSettings QuerySettings(
        int maxConcurrentQueries = 2,
        long maxActiveWorkspaceBytes = 1_000_000,
        long maxRetainedWorkspaceBytes = 1_000_000,
        long maxActiveResultBytes = 1_000_000,
        int maxRetiredSnapshots = 2,
        long maxRetiredSnapshotBytes = 2_000_000)
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        return new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            defaults.MaintenanceBudget,
            maxIngressEntries: 32,
            maxIngressBytes: 32 * 256,
            maxActiveSnapshots: 3,
            maxActiveSnapshotBytes: 1_000_000,
            maxRetiredSnapshots,
            maxRetiredSnapshotBytes,
            maxPersistentGraphPages: 1_000,
            maxDynamicCellSlotsPerMap: defaults.MaxDynamicCellSlotsPerMap,
            maxDynamicCellSlots: defaults.MaxDynamicCellSlots,
            navigationAreaCount: 1,
            maxAreaPolicies: 8,
            maxAreaRulesPerPolicy: 32,
            maxAreaRules: 64,
            maxConcurrentPathQueries: maxConcurrentQueries,
            maxActiveWorkspaceBytes,
            maxRetainedWorkspaceBytes,
            maxActiveQueryResultBytes: maxActiveResultBytes);
    }
}

//=======================================================================
// NavigationWorldGraphStoreTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FluentAssertions;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationWorldGraphStoreTests
{
    [Theory]
    [InlineData(0, 1_000_000)]
    [InlineData(2, 0)]
    [InlineData(0, 0)]
    public void DisabledRetirement_ShouldAllowReadersAndBlockPublicationUntilReturn(
        int maxRetiredSnapshots,
        long maxRetiredBytes)
    {
        using var store = CreateStore(maxRetiredSnapshots, maxRetiredBytes);
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
    public void GraphStore_ShouldBoundConcurrentLeasesAndCloseAdmissionOnDispose()
    {
        var store = CreateStore(maxConcurrentLeases: 2);
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
    public void PendingSafety_ShouldBlockRawSnapshotLeaseUntilCatchupCompletes()
    {
        using var store = CreateStore(maxConcurrentLeases: 2);

        store.MarkSafetyPending();
        store.TryAcquire().Should().BeNull();

        store.ClearSafetyPending();
        using NavigationWorldGraphLease lease = store.TryAcquire()!;
        lease.Should().NotBeNull();
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
            + NavigationExplicitConnectionIndex.Empty.RetainedBytes
            + catalog.RetainedBytes);
        graph.PersistentPageCount.Should().Be(
            NavigationInstanceDirectory.Create(Array.Empty<NavigationMapInstance>()).PersistentPageCount
            + NavigationWorldGraph.EmptyMapIndexPersistentPageCount
            + NavigationWorldGraph.EmptyClosedStructuralComponentsPersistentPageCount
            + NavigationCompositionIndex.Empty.PersistentPageCount
            + NavigationExplicitConnectionIndex.Empty.PersistentPageCount
            + catalog.PersistentPageCount);
    }

    private static NavigationWorldGraphStore CreateStore(
        int maxRetiredSnapshots = 2,
        long maxRetiredBytes = 1_000_000,
        int maxConcurrentLeases = 2) => new(
            maxActiveSnapshots: 3,
            maxRetiredSnapshots,
            maxRetiredBytes,
            maxActiveBytes: 1_000_000,
            maxPersistentPages: 1_000,
            maxConcurrentLeases);
}

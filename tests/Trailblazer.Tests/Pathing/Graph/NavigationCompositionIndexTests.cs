using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Topology;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationCompositionIndexTests
{
    private static readonly NavigationCell Cell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        Fixed64.Zero,
        Fixed64.One);

    [Fact]
    public void UpdateWork_ShouldBuildDisconnectedComponentsFromEmptyWithoutSyncAuthority()
    {
        using var world = new GridWorld();
        NavigationInstanceDirectory directory = NavigationInstanceDirectory.Create(new[]
        {
            CreateInstance(world, CreateMap("A"), null, 1),
            CreateInstance(world, CreateMap("B"), null, 1),
            CreateInstance(world, CreateMap("C"), null, 1)
        });

        NavigationCompositionIndex result = BuildComposition(directory, 1);

        result.ComponentCount.Should().Be(3);
        result.GetComponentRecord("A").Should().NotBeSameAs(result.GetComponentRecord("B"));
        result.GetComponentRecord("B").Should().NotBeSameAs(result.GetComponentRecord("C"));
    }

    [Fact]
    public void UpdateWork_RemovingSingleton_ShouldDeleteItsComponentAndMembership()
    {
        using var world = new GridWorld();
        NavigationInstanceDirectory sourceDirectory = NavigationInstanceDirectory.Create(new[]
        {
            CreateInstance(world, CreateMap("A"), null, 1)
        });
        NavigationCompositionIndex source = BuildComposition(sourceDirectory, 1);
        NavigationInstanceDirectory nextDirectory = sourceDirectory.Remove("A", out bool removed);
        removed.Should().BeTrue();

        NavigationCompositionIndex result = UpdateComposition(
            source,
            nextDirectory,
            NavigationExplicitConnectionIndex.Empty,
            new[] { "A" },
            2);

        result.ComponentCount.Should().Be(0);
        result.TryGetComponentRecord("A", out _).Should().BeFalse();
    }

    [Fact]
    public void UpdateWork_RemovingMember_ShouldDeleteOnlyItsMembership()
    {
        using var world = new GridWorld();
        NavigationInstanceDirectory sourceDirectory = NavigationInstanceDirectory.Create(new[]
        {
            CreateInstance(world, CreateMap("A"), null, 1),
            CreateInstance(world, CreateMap("B"), null, 1)
        });
        NavigationExplicitConnectionIndex sourceEdges =
            NavigationExplicitConnectionIndex.Empty.SetOwner(
                CreateExplicitRecord("A", "B"),
                out _);
        NavigationCompositionIndex source = BuildComposition(sourceDirectory, 1, sourceEdges);
        NavigationInstanceDirectory nextDirectory = sourceDirectory.Remove("A", out bool removed);
        removed.Should().BeTrue();

        NavigationCompositionIndex result = UpdateComposition(
            source,
            nextDirectory,
            NavigationExplicitConnectionIndex.Empty,
            new[] { "A" },
            2);

        result.ComponentCount.Should().Be(1);
        result.TryGetComponentRecord("A", out _).Should().BeFalse();
        result.TryGetComponentRecord("B", out _).Should().BeTrue();
        NavigationCompositionIndex clean = BuildComposition(nextDirectory, 2);
        result.RetainedBytes.Should().Be(clean.RetainedBytes,
            "deleted members must not leave unreachable membership records retained");
        result.PersistentPageCount.Should().Be(clean.PersistentPageCount);
    }

    [Fact]
    public void UpdateWork_ShouldNotScanDisconnectedMapsOutsideTheAffectedDomain()
    {
        using var world = new GridWorld();
        var instances = new NavigationMapInstance[129];
        for (int i = 0; i < instances.Length; i++)
        {
            string id = $"map-{i:D3}";
            instances[i] = CreateInstance(
                world,
                CreateMap(id, i == 0 ? "dormant" : null, i == 0 ? "edge" : null),
                null,
                1);
        }
        NavigationInstanceDirectory directory = NavigationInstanceDirectory.Create(instances);
        NavigationCompositionIndex source = BuildComposition(directory, 1);
        NavigationStructuralComponent untouched = source.GetComponentRecord("map-128");
        NavigationMapOverlayState suppressed = NavigationMapOverlayState.Empty.Apply(
            new NavigationMapOverlayDelta(
                "map-000",
                transitions: new[] { TraversalTransitionOverlayOperation.Suppress("edge") }),
            2);
        NavigationMapInstance changed = CreateInstance(world, instances[0].Map, suppressed, 2);
        NavigationInstanceDirectory nextDirectory = directory.Set("map-000", changed, out _);
        var work = new NavigationCompositionIndex.UpdateWork(
            source,
            nextDirectory,
            CreateChangeRoot(new[] { "map-000" }),
            2,
            new NavigationCompositionWorkspace(instances.Length));
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(8, 8, 8, 1, 1, 8));

        for (int frame = 0; frame < 32 && !work.IsComplete; frame++)
        {
            meter.Reset();
            work.Advance(meter);
            meter.ComponentNodes.Should().BeLessThanOrEqualTo(1);
        }

        work.IsComplete.Should().BeTrue();
        work.Result.LastUpdate.VisitedMaps.Should().Be(1);
        work.Result.LastUpdate.CopiedMembershipRecords.Should().BeLessThanOrEqualTo(2);
        work.Result.GetComponentRecord("map-128").Should().BeSameAs(untouched,
            "unrelated component roots must remain structurally untouched");
        work.Result.ComponentCount.Should().Be(129);
        for (int i = 0; i < instances.Length; i++)
        {
            work.Result.TryGetComponentRecord($"map-{i:D3}", out _)
                .Should().BeTrue();
        }
    }

    [Fact]
    public void UpdateWork_RepeatedSameKeyReplacement_ShouldChargeOnlyReachablePersistentCopies()
    {
        const int MapCount = 128;
        using var world = new GridWorld();
        var originalInstances = new NavigationMapInstance[MapCount];
        var replacementInstances = new NavigationMapInstance[MapCount];
        var changedMapIds = new string[MapCount];
        for (int i = 0; i < MapCount; i++)
        {
            string mapId = $"map-{i:D3}";
            NavigationMap map = CreateMap(mapId);
            originalInstances[i] = CreateInstance(world, map, null, 1);
            replacementInstances[i] = CreateInstance(world, map, null, 2);
            changedMapIds[i] = mapId;
        }
        NavigationCompositionIndex source = BuildComposition(
            NavigationInstanceDirectory.Create(originalInstances),
            1);
        var work = new NavigationCompositionIndex.UpdateWork(
            source,
            NavigationInstanceDirectory.Create(replacementInstances),
            CreateChangeRoot(changedMapIds),
            2,
            new NavigationCompositionWorkspace(MapCount));
        var meter = new MaintenanceWorkMeter(TrailblazerWorldContextSettings.Default.MaintenanceBudget);

        for (int frame = 0; frame < 128 && !work.IsComplete; frame++)
        {
            meter.Reset();
            work.Advance(meter);
        }

        work.IsComplete.Should().BeTrue();
        work.PersistentPageCount.Should().Be(
            work.Result.PersistentPageCount
                + work.RetainedCopiedPersistentPages
                + work.PayloadAdditionalPersistentPages,
            "work ownership must equal the reachable result plus live COW roots and source-coexisting payloads");
    }

    [Fact]
    public void UpdateWork_SameSizePayloadReplacement_ShouldRetainWorkingComponentPayload()
    {
        using var world = new GridWorld();
        NavigationMap map = CreateMap("A");
        NavigationInstanceDirectory sourceDirectory = NavigationInstanceDirectory.Create(new[]
        {
            CreateInstance(world, map, null, 1)
        });
        NavigationCompositionIndex source = BuildComposition(sourceDirectory, 1);
        NavigationInstanceDirectory nextDirectory = sourceDirectory.Set(
            "A",
            CreateInstance(world, map, null, 2),
            out _);
        var work = new NavigationCompositionIndex.UpdateWork(
            source,
            nextDirectory,
            CreateChangeRoot(new[] { "A" }),
            2,
            new NavigationCompositionWorkspace(1));
        var meter = new MaintenanceWorkMeter(
            TrailblazerWorldContextSettings.Default.MaintenanceBudget);

        while (!work.IsComplete)
        {
            work.Advance(meter);
            meter.Reset();
        }

        work.PayloadAdditionalRetainedBytes.Should().Be(200,
            "the replacement singleton component wrapper and fixed member page coexist with the source payload");
        work.PayloadAdditionalPersistentPages.Should().Be(3);
    }

    [Fact]
    public void UpdateWork_SameSizeIncomingReplacement_ShouldOwnExactProductionPayload()
    {
        using var world = new GridWorld();
        NavigationInstanceDirectory directory = NavigationInstanceDirectory.Create(new[]
        {
            CreateInstance(world, CreateMap("A"), null, 1),
            CreateInstance(world, CreateMap("B"), null, 1),
            CreateInstance(world, CreateMap("C"), null, 1),
            CreateInstance(world, CreateMap("D"), null, 1)
        });
        NavigationExplicitConnectionIndex sourceEdges =
            NavigationExplicitConnectionIndex.Empty;
        sourceEdges = sourceEdges.SetOwner(CreateExplicitRecord("A", "B"), out _);
        sourceEdges = sourceEdges.SetOwner(CreateExplicitRecord("C", "B"), out _);
        NavigationCompositionIndex source = BuildComposition(directory, 1, sourceEdges);
        NavigationExplicitConnectionIndex nextEdges =
            NavigationExplicitConnectionIndex.Empty;
        nextEdges = nextEdges.SetOwner(CreateExplicitRecord("C", "B"), out _);
        nextEdges = nextEdges.SetOwner(CreateExplicitRecord("D", "B"), out _);
        var work = new NavigationCompositionIndex.UpdateWork(
            source,
            directory,
            nextEdges,
            CreateChangeRoot(new[] { "A", "D" }),
            2,
            new NavigationCompositionWorkspace(4));
        var meter = new MaintenanceWorkMeter(
            TrailblazerWorldContextSettings.Default.MaintenanceBudget);

        while (!work.IsComplete)
        {
            work.Advance(meter);
            meter.Reset();
        }

        work.PayloadAdditionalRetainedBytes.Should().Be(848,
            "the production total owns the rebuilt B row and changed structural payload");
        work.PayloadAdditionalPersistentPages.Should().Be(12);
    }

    [Fact]
    public void UpdateWork_ManySameSizeIncomingReplacements_ShouldOwnEveryReachableInnerPathAtCapacity()
    {
        const int SourceCount = 8;
        const int DestinationCount = 4;
        using var world = new GridWorld();
        var instances = new NavigationMapInstance[(SourceCount * 2) + DestinationCount];
        for (int i = 0; i < SourceCount; i++)
        {
            instances[i] = CreateInstance(world, CreateMap($"old-{i:D2}"), null, 1);
            instances[SourceCount + i] = CreateInstance(
                world,
                CreateMap($"new-{i:D2}"),
                null,
                1);
        }
        for (int i = 0; i < DestinationCount; i++)
        {
            instances[(SourceCount * 2) + i] = CreateInstance(
                world,
                CreateMap($"destination-{i:D2}"),
                null,
                1);
        }
        NavigationInstanceDirectory directory = NavigationInstanceDirectory.Create(instances);
        NavigationExplicitConnectionIndex sourceEdges = NavigationExplicitConnectionIndex.Empty;
        NavigationExplicitConnectionIndex nextEdges = NavigationExplicitConnectionIndex.Empty;
        for (int sourceIndex = 0; sourceIndex < SourceCount; sourceIndex++)
        {
            for (int destinationIndex = 0;
                 destinationIndex < DestinationCount;
                 destinationIndex++)
            {
                string destination = $"destination-{destinationIndex:D2}";
                string edgeId = $"edge-{destinationIndex:D2}";
                sourceEdges = sourceEdges.SetOwner(
                    CreateExplicitRecord($"old-{sourceIndex:D2}", destination, edgeId),
                    out _);
                nextEdges = nextEdges.SetOwner(
                    CreateExplicitRecord($"new-{sourceIndex:D2}", destination, edgeId),
                    out _);
            }
        }
        NavigationCompositionIndex source = BuildComposition(directory, 1, sourceEdges);
        var changedMapIds = new string[SourceCount * 2];
        for (int i = 0; i < SourceCount; i++)
        {
            changedMapIds[i] = $"old-{i:D2}";
            changedMapIds[SourceCount + i] = $"new-{i:D2}";
        }
        var work = new NavigationCompositionIndex.UpdateWork(
            source,
            directory,
            nextEdges,
            CreateChangeRoot(changedMapIds),
            2,
            new NavigationCompositionWorkspace(instances.Length));
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            defaults.MaintenanceBudget.MaxOverlaySlots,
            defaults.MaintenanceBudget.MaxComponentNodes,
            defaults.MaintenanceBudget.MaxExplicitEdges,
            maxDependencyEntries: 1));
        long exactPeak = work.RetainedBytes;
        int dependencyUnits = 0;

        while (!work.IsComplete)
        {
            work.Advance(meter);
            exactPeak = Math.Max(exactPeak, work.RetainedBytes);
            meter.DependencyEntries.Should().BeLessThanOrEqualTo(1);
            dependencyUnits += meter.DependencyEntries;
            meter.Reset();
        }

        work.PayloadAdditionalRetainedBytes.Should().Be(6_304,
            "production payload includes the rebuilt incoming rows, changed nodes, and components");
        work.PayloadAdditionalPersistentPages.Should().Be(89);
        dependencyUnits.Should().Be(164,
            "96 edge/link updates, four row starts, and 64 merged keys must be debited");

        var exact = new NavigationCompositionIndex.UpdateWork(
            source,
            directory,
            nextEdges,
            CreateChangeRoot(changedMapIds),
            2,
            new NavigationCompositionWorkspace(instances.Length));
        while (!exact.IsComplete)
        {
            exact.Advance(meter);
            exact.RetainedBytes.Should().BeLessThanOrEqualTo(exactPeak);
            meter.Reset();
        }

        NavigationStructuralComponent published = source.GetComponentRecord("old-00");
        var below = new NavigationCompositionIndex.UpdateWork(
            source,
            directory,
            nextEdges,
            CreateChangeRoot(changedMapIds),
            2,
            new NavigationCompositionWorkspace(instances.Length));
        bool rejected = false;
        for (int frame = 0; frame < 4_096 && !below.IsComplete; frame++)
        {
            below.Advance(meter);
            if (below.RetainedBytes > exactPeak - 1)
            {
                rejected = true;
                break;
            }
            meter.Reset();
        }

        rejected.Should().BeTrue();
        source.GetComponentRecord("old-00").Should().BeSameAs(published,
            "capacity rejection must leave the published composition untouched");
    }

    [Fact]
    public void UpdateWork_MidNodeCursor_ShouldRetainPagedBuilderScratchPages()
    {
        const int DestinationCount = 9;
        using var world = new GridWorld();
        NavigationInstanceDirectory directory = NavigationInstanceDirectory.Create(new[]
        {
            CreateInstance(world, CreateMap("A"), null, 1)
        });
        NavigationExplicitConnectionIndex edges = NavigationExplicitConnectionIndex.Empty;
        for (int i = 0; i < DestinationCount; i++)
        {
            edges = edges.SetOwner(
                CreateExplicitRecord("A", $"D-{i:D2}", $"edge-{i:D2}"),
                out _);
        }
        var work = new NavigationCompositionIndex.UpdateWork(
            NavigationCompositionIndex.Empty,
            directory,
            edges,
            CreateChangeRoot(new[] { "A" }),
            1,
            new NavigationCompositionWorkspace(1));
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            defaults.MaintenanceBudget.MaxOverlaySlots,
            maxComponentNodes: 1,
            maxExplicitEdges: DestinationCount,
            maxDependencyEntries: 1));
        int initialPages = work.PersistentPageCount;

        work.Advance(meter).Should().BeFalse();

        meter.DependencyEntries.Should().Be(1);
        work.PersistentPageCount.Should().Be(initialPages + DestinationCount + 4,
            "the node-work object, count root, builder, and first fixed link page remain live");

        for (int i = 1; i < DestinationCount; i++)
        {
            meter.Reset();
            work.Advance(meter).Should().BeFalse();
        }

        work.PersistentPageCount.Should().Be(initialPages + DestinationCount + 6,
            "sealing transfers the two-page builder into an equally live two-page result");
    }

    [Fact]
    public void UpdateWork_HighDegreeUnchangedNode_ShouldUseLinearDebitedLinkTraversal()
    {
        const int DestinationCount = 65;
        using var world = new GridWorld();
        NavigationMap map = CreateMap("A");
        NavigationInstanceDirectory sourceDirectory = NavigationInstanceDirectory.Create(new[]
        {
            CreateInstance(world, map, null, 1)
        });
        NavigationExplicitConnectionIndex edges = NavigationExplicitConnectionIndex.Empty;
        for (int i = 0; i < DestinationCount; i++)
        {
            edges = edges.SetOwner(
                CreateExplicitRecord("A", $"D-{i:D2}", $"edge-{i:D2}"),
                out _);
        }
        NavigationCompositionIndex source = BuildComposition(sourceDirectory, 1, edges);
        NavigationInstanceDirectory nextDirectory = sourceDirectory.Set(
            "A",
            CreateInstance(world, map, null, 2),
            out _);
        var work = new NavigationCompositionIndex.UpdateWork(
            source,
            nextDirectory,
            edges,
            CreateChangeRoot(new[] { "A" }),
            2,
            new NavigationCompositionWorkspace(1));
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            defaults.MaintenanceBudget.MaxOverlaySlots,
            defaults.MaintenanceBudget.MaxComponentNodes,
            defaults.MaintenanceBudget.MaxExplicitEdges,
            maxDependencyEntries: 1));
        int dependencyWork = 0;

        for (int frame = 0; frame < 512 && !work.IsComplete; frame++)
        {
            work.Advance(meter);
            dependencyWork += meter.DependencyEntries;
            meter.DependencyEntries.Should().BeLessThanOrEqualTo(1);
            meter.Reset();
        }

        work.IsComplete.Should().BeTrue();
        dependencyWork.Should().Be(DestinationCount,
            "each generated link is compared while its existing append debit is active");
        work.PayloadAdditionalRetainedBytes.Should().Be(200,
            "an unchanged high-degree node must reuse its source payload by reference");
    }

    [Fact]
    public void UpdateWork_Constructor_ShouldAllocateConstantWorkObjectOnly()
    {
        using var world = new GridWorld();
        NavigationInstanceDirectory one = NavigationInstanceDirectory.Create(new[]
        {
            CreateInstance(world, CreateMap("single"), null, 1)
        });
        var manyInstances = new NavigationMapInstance[128];
        for (int i = 0; i < manyInstances.Length; i++)
        {
            manyInstances[i] = CreateInstance(
                world,
                CreateMap($"many-{i:D3}"),
                null,
                1);
        }
        NavigationInstanceDirectory many = NavigationInstanceDirectory.Create(manyInstances);
        PersistentStringMap<bool> oneChange = CreateChangeRoot(new[] { "single" });
        var manyIds = new string[manyInstances.Length];
        for (int i = 0; i < manyIds.Length; i++)
            manyIds[i] = manyInstances[i].MapId;
        PersistentStringMap<bool> manyChanges = CreateChangeRoot(manyIds);
        var oneWorkspace = new NavigationCompositionWorkspace(1);
        var manyWorkspace = new NavigationCompositionWorkspace(128);
        _ = new NavigationCompositionIndex.UpdateWork(
            NavigationCompositionIndex.Empty,
            one,
            oneChange,
            1,
            oneWorkspace);

        long beforeOne = GC.GetAllocatedBytesForCurrentThread();
        var oneWork = new NavigationCompositionIndex.UpdateWork(
            NavigationCompositionIndex.Empty,
            one,
            oneChange,
            1,
            oneWorkspace);
        long oneBytes = GC.GetAllocatedBytesForCurrentThread() - beforeOne;
        long beforeMany = GC.GetAllocatedBytesForCurrentThread();
        var manyWork = new NavigationCompositionIndex.UpdateWork(
            NavigationCompositionIndex.Empty,
            many,
            manyChanges,
            1,
            manyWorkspace);
        long manyBytes = GC.GetAllocatedBytesForCurrentThread() - beforeMany;

        GC.KeepAlive(oneWork);
        GC.KeepAlive(manyWork);
        manyBytes.Should().Be(oneBytes,
            "constructor work must retain only references into the context-owned workspace");
    }

    private static PersistentStringMap<bool> CreateChangeRoot(string[] mapIds)
    {
        PersistentStringMap<bool> result = PersistentStringMap<bool>.Empty;
        for (int i = 0; i < mapIds.Length; i++)
            result = result.Set(mapIds[i], true);
        return result;
    }

    private static NavigationCompositionIndex BuildComposition(
        NavigationInstanceDirectory directory,
        long version) => BuildComposition(
        directory,
        version,
        NavigationExplicitConnectionIndex.Empty);

    private static NavigationCompositionIndex BuildComposition(
        NavigationInstanceDirectory directory,
        long version,
        NavigationExplicitConnectionIndex explicitConnections)
    {
        var mapIds = new string[directory.Count];
        for (int i = 0; i < mapIds.Length; i++)
            mapIds[i] = directory.Get(i).MapId;
        var work = new NavigationCompositionIndex.UpdateWork(
            NavigationCompositionIndex.Empty,
            directory,
            explicitConnections,
            CreateChangeRoot(mapIds),
            version,
            new NavigationCompositionWorkspace(Math.Max(1, directory.Count)));
        var meter = new MaintenanceWorkMeter(
            TrailblazerWorldContextSettings.Default.MaintenanceBudget);
        for (int frame = 0; frame < 4096 && !work.IsComplete; frame++)
        {
            work.Advance(meter);
            meter.Reset();
        }
        work.IsComplete.Should().BeTrue();
        return work.Result;
    }

    private static NavigationCompositionIndex UpdateComposition(
        NavigationCompositionIndex source,
        NavigationInstanceDirectory directory,
        NavigationExplicitConnectionIndex explicitConnections,
        string[] changedMapIds,
        long version)
    {
        var work = new NavigationCompositionIndex.UpdateWork(
            source,
            directory,
            explicitConnections,
            CreateChangeRoot(changedMapIds),
            version,
            new NavigationCompositionWorkspace(Math.Max(1, directory.Count)));
        var meter = new MaintenanceWorkMeter(
            TrailblazerWorldContextSettings.Default.MaintenanceBudget);
        for (int frame = 0; frame < 4096 && !work.IsComplete; frame++)
        {
            work.Advance(meter);
            meter.Reset();
        }
        work.IsComplete.Should().BeTrue();
        return work.Result;
    }

    private static NavigationExplicitConnectionRecord CreateExplicitRecord(
        string sourceMapId,
        string destinationMapId,
        string connectionId = "edge")
    {
        var definition = new NavigationConnection(
            connectionId,
            default,
            new NavigationCellAddress(destinationMapId, default),
            Vector3d.Zero,
            Vector3d.Zero,
            Fixed64.Zero,
            Fixed64.One);
        return new NavigationExplicitConnectionRecord(
            new NavigationConnectionOwnerKey(sourceMapId, definition.Id),
            definition,
            isActive: true,
            Fixed64.Zero,
            NavigationPagedSequence<Vector3d>.Empty);
    }

    private static NavigationMapInstance CreateInstance(
        GridWorld world,
        NavigationMap map,
        NavigationMapOverlayState? overlay,
        long version)
    {
        var state = new NavigationOperationCandidate.MapState(
            map,
            version,
            0,
            overlay ?? NavigationMapOverlayState.Empty,
            1);
        return NavigationMapInstanceTestFactory.Compose(world, state, null, version);
    }

    private static NavigationMap CreateMap(
        string mapId,
        string? destinationMapId = null,
        string? transitionId = null)
    {
        int origin = mapId[0] * 2;
        var configuration = new GridConfiguration(
            new Vector3d(origin, 0, 0),
            new Vector3d(origin, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var builder = new NavigationMapBuilder(mapId, binding).AddCell(default, Cell);
        if (destinationMapId != null)
        {
            builder.AddTransition(new TraversalTransitionDefinition(
                transitionId!,
                TraversalTransitionType.Climb,
                default,
                TraversalMedium.Solid,
                new NavigationCellAddress(destinationMapId, default),
                TraversalMedium.Solid,
                TraversalCapability.Climb));
        }
        return builder.Build();
    }
}

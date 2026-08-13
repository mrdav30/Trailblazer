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
    public void Update_ShouldReuseDisconnectedComponentRecords()
    {
        using var world = new GridWorld();
        NavigationMapInstance a = CreateInstance(world, CreateMap("A", "B", "ab"), null, 1);
        NavigationMapInstance b = CreateInstance(world, CreateMap("B"), null, 1);
        NavigationMapInstance c = CreateInstance(world, CreateMap("C", "D", "cd"), null, 1);
        NavigationMapInstance d = CreateInstance(world, CreateMap("D"), null, 1);
        NavigationInstanceDirectory directory = NavigationInstanceDirectory.Create(new[] { a, b, c, d });
        NavigationCompositionIndex original = NavigationCompositionIndex.Build(directory, 1);
        NavigationStructuralComponent untouched = original.GetComponentRecord("C");

        NavigationMapOverlayState suppressed = NavigationMapOverlayState.Empty.Apply(
            new NavigationMapOverlayDelta(
                "A",
                transitions: new[] { TraversalTransitionOverlayOperation.Suppress("ab") }),
            2);
        NavigationMapInstance nextA = CreateInstance(world, a.Map, suppressed, 2);
        NavigationInstanceDirectory nextDirectory = directory.Set("A", nextA);

        NavigationCompositionIndex next = original.Update(nextDirectory, new[] { "A" }, 2);

        next.GetComponentRecord("A").Should().NotBeSameAs(next.GetComponentRecord("B"));
        next.GetComponentRecord("C").Should().BeSameAs(untouched);
        next.GetComponentRecord("C").Should().BeSameAs(next.GetComponentRecord("D"));
        next.LastUpdate.VisitedMaps.Should().BeLessThan(4);
        next.LastUpdate.ReusedComponents.Should().BePositive();
    }

    [Fact]
    public void Update_ShouldSplitBridgeHonestlyThenRejoinWithoutRewritingMembers()
    {
        using var world = new GridWorld();
        NavigationMapInstance a = CreateInstance(world, CreateMap("A", "B", "ab"), null, 1);
        NavigationMapInstance b = CreateInstance(world, CreateMap("B", "C", "bc"), null, 1);
        NavigationMapInstance c = CreateInstance(world, CreateMap("C"), null, 1);
        NavigationInstanceDirectory directory = NavigationInstanceDirectory.Create(new[] { a, b, c });
        NavigationCompositionIndex original = NavigationCompositionIndex.Build(directory, 1);

        NavigationMapOverlayState suppressed = NavigationMapOverlayState.Empty.Apply(
            new NavigationMapOverlayDelta(
                "B",
                transitions: new[] { TraversalTransitionOverlayOperation.Suppress("bc") }),
            2);
        NavigationMapInstance splitB = CreateInstance(world, b.Map, suppressed, 2);
        NavigationInstanceDirectory splitDirectory = directory.Set("B", splitB);
        NavigationCompositionIndex split = original.Update(splitDirectory, new[] { "B" }, 2);

        split.ComponentCount.Should().Be(2);
        split.GetComponentRecord("A").Should().BeSameAs(split.GetComponentRecord("B"));
        split.GetComponentRecord("B").Should().NotBeSameAs(split.GetComponentRecord("C"));
        split.LastUpdate.VisitedMaps.Should().BeGreaterThanOrEqualTo(3);

        NavigationMapInstance rejoinedB = CreateInstance(
            world,
            b.Map,
            NavigationMapOverlayState.Empty,
            3);
        NavigationCompositionIndex rejoined = split.Update(
            splitDirectory.Set("B", rejoinedB),
            new[] { "B" },
            3);

        rejoined.ComponentCount.Should().Be(1);
        rejoined.GetComponentRecord("A").Should().BeSameAs(rejoined.GetComponentRecord("C"));
        rejoined.LastUpdate.VisitedMaps.Should().Be(1);
        rejoined.LastUpdate.CopiedMembershipRecords.Should().Be(0);
    }

    [Fact]
    public void Update_ShouldActivateDormantReverseDependencyWhenTargetAppears()
    {
        using var world = new GridWorld();
        NavigationMapInstance a = CreateInstance(world, CreateMap("A", "B", "ab"), null, 1);
        NavigationMapInstance b = CreateInstance(world, CreateMap("B"), null, 2);
        NavigationInstanceDirectory directory = NavigationInstanceDirectory.Create(new[] { a });
        NavigationCompositionIndex original = NavigationCompositionIndex.Build(directory, 1);

        NavigationCompositionIndex activated = original.Update(
            directory.Set("B", b),
            new[] { "B" },
            2);

        activated.ComponentCount.Should().Be(1);
        activated.GetComponentRecord("A").Should().BeSameAs(activated.GetComponentRecord("B"));
        activated.LastUpdate.VisitedMaps.Should().Be(1);

        NavigationCompositionIndex removed = activated.Update(
            directory,
            new[] { "B" },
            3);
        removed.ComponentCount.Should().Be(1);
        removed.GetComponentRecord("A").MemberCount.Should().Be(1);
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
        NavigationCompositionIndex source = NavigationCompositionIndex.Build(directory, 1);
        NavigationStructuralComponent untouched = source.GetComponentRecord("map-128");
        NavigationMapOverlayState suppressed = NavigationMapOverlayState.Empty.Apply(
            new NavigationMapOverlayDelta(
                "map-000",
                transitions: new[] { TraversalTransitionOverlayOperation.Suppress("edge") }),
            2);
        NavigationMapInstance changed = CreateInstance(world, instances[0].Map, suppressed, 2);
        NavigationInstanceDirectory nextDirectory = directory.Set("map-000", changed);
        var work = new NavigationCompositionIndex.UpdateWork(
            source,
            nextDirectory,
            new[] { "map-000" },
            2);
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(8, 8, 8, 8, 1, 8, 1, 8, 8));

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
        NavigationCompositionIndex source = NavigationCompositionIndex.Build(
            NavigationInstanceDirectory.Create(originalInstances),
            1);
        var work = new NavigationCompositionIndex.UpdateWork(
            source,
            NavigationInstanceDirectory.Create(replacementInstances),
            changedMapIds,
            2);
        var meter = new MaintenanceWorkMeter(TrailblazerWorldContextSettings.Default.MaintenanceBudget);

        for (int frame = 0; frame < 128 && !work.IsComplete; frame++)
        {
            meter.Reset();
            work.Advance(meter);
        }

        work.IsComplete.Should().BeTrue();
        int reachableRootNodes = work.Result.PersistentPageCount - 5 + MapCount;
        work.PersistentPageCount.Should().Be(5 + (reachableRootNodes * 2),
            "historical Set/Remove paths cannot outlive the current roots, while one reachable COW root set remains charged");
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
        return NavigationMapInstance.Compose(world, state, null, version);
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

using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Storage;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

[Collection("PathingCollection")]
public sealed class NavigationMediumGraphTests
{
    private static readonly NavigationCell GasCell = Cell(TraversalMedia.Gas);
    private static readonly NavigationCell SolidLiquidCell = Cell(
        TraversalMedia.Solid | TraversalMedia.Liquid);

    [Fact]
    public void DefaultComposition_ShouldCreateOnlyPhysicalNodesWithExactMediumStates()
    {
        GridConfiguration configuration = RectangularConfiguration(GridStorageKind.Dense);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        using var denseWorld = new GridWorld();
        denseWorld.TryAddGrid(configuration, out _).Should().BeTrue();
        NavigationMap denseMap = new NavigationMapBuilder("map", binding)
            .SetDefaultCell(GasCell)
            .AddCell(new VoxelIndex(1, 0, 0), SolidLiquidCell)
            .Build();
        NavigationMapInstance denseInstance = Compose(denseWorld, denseMap);
        var denseGraph = new NavigationWorldGraph(1, new[] { denseInstance });
        var defaultAddress = new NavigationCellAddress("map", default);
        var explicitAddress = new NavigationCellAddress("map", new VoxelIndex(1, 0, 0));

        denseGraph.TryGetMediumStateRef(
                defaultAddress,
                TraversalMedium.Gas,
                out NavigationMediumStateRef gas)
            .Should().BeTrue();
        denseGraph.TryGetMediumStateRef(defaultAddress, TraversalMedium.Solid, out _)
            .Should().BeFalse();
        denseGraph.TryGetMediumStateRef(
                explicitAddress,
                TraversalMedium.Solid,
                out NavigationMediumStateRef solid)
            .Should().BeTrue();
        denseGraph.TryGetMediumStateRef(
                explicitAddress,
                TraversalMedium.Liquid,
                out NavigationMediumStateRef liquid)
            .Should().BeTrue();
        solid.Node.Should().Be(liquid.Node,
            "multiple media are lightweight states over one physical node");
        denseInstance.AddressCount.Should().Be(2,
            "medium flags must not materialize duplicate graph nodes");

        gas.IsValid.Should().BeTrue();
        gas.Medium.Should().Be(TraversalMedium.Gas);
        gas.Should().Be(new NavigationMediumStateRef(gas.Node, TraversalMedium.Gas));
        gas.Should().NotBe(new NavigationMediumStateRef(gas.Node, TraversalMedium.Liquid));
        solid.CompareTo(liquid).Should().BeLessThan(0,
            "states over one node use exact medium as the final tie-breaker");
        gas.GetHashCode().Should().Be(
            new NavigationMediumStateRef(gas.Node, TraversalMedium.Gas).GetHashCode());

        using var sparseWorld = new GridWorld();
        sparseWorld.TryAddGrid(
                RectangularConfiguration(GridStorageKind.Sparse),
                new[] { default(VoxelIndex) },
                out _)
            .Should().BeTrue();
        NavigationMapInstance sparseInstance = Compose(sparseWorld, new NavigationMapBuilder(
                "map",
                binding)
            .SetDefaultCell(GasCell)
            .Build());
        var sparseGraph = new NavigationWorldGraph(1, new[] { sparseInstance });

        sparseGraph.TryGetMediumStateRef(defaultAddress, TraversalMedium.Gas, out _)
            .Should().BeTrue();
        sparseGraph.TryGetNodeRef(explicitAddress, out _).Should().BeFalse(
            "a default supplies semantics only to physically present sparse addresses");
        sparseInstance.AddressCount.Should().Be(1);
    }

    [Fact]
    public void SparseDefaultDiscovery_ShouldAtomicallyTrackLaterPhysicalInsertionAndRemoval()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(3, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        var initiallyPresent = new[] { default(VoxelIndex), new VoxelIndex(3, 0, 0) };
        context.World.TryAddGrid(configuration, initiallyPresent, out ushort gridIndex)
            .Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var install = new NavigationMapCommitOperation(
            new PreparedNavigationMap(new NavigationMapBuilder("map", binding)
                .SetDefaultCell(GasCell)
                .Build(), 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 1);
        context.Pathing.Admit(install).Should().BeTrue();
        var first = new NavigationCellAddress("map", default);
        SimulateUntil(
            context,
            () => install.Receipt.Status != NavigationOperationStatus.Pending
                && HasMediumState(context, first, TraversalMedium.Gas));
        install.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        var hole = new NavigationCellAddress("map", new VoxelIndex(1, 0, 0));
        var unrelated = new NavigationCellAddress("map", new VoxelIndex(3, 0, 0));

        long initialDynamicGeneration;
        using (NavigationWorldGraphLease initial = context.Pathing.TryAcquireNavigationGraph()!)
        {
            initialDynamicGeneration = initial.Graph.GetInstance(0).DynamicSlotGeneration;
            initial.Graph.TryGetMediumStateRef(first, TraversalMedium.Gas, out _)
                .Should().BeTrue();
            initial.Graph.TryGetNodeRef(hole, out _).Should().BeFalse(
                "an absent in-bounds sparse hole must not receive a default-backed node");
            initial.Graph.TryGetMediumStateRef(unrelated, TraversalMedium.Gas, out _)
                .Should().BeTrue();
        }

        VoxelGrid grid = context.World.ActiveGrids[gridIndex];
        grid.TryAddVoxel(hole.Index, out _).Should().BeTrue();
        SimulateUntil(context, () => HasMediumState(context, hole, TraversalMedium.Gas));

        NavigationSurfaceComponentKey unrelatedKey;
        long unrelatedVersion;
        using (NavigationWorldGraphLease inserted = context.Pathing.TryAcquireNavigationGraph()!)
        {
            inserted.Graph.GetInstance(0).DynamicSlotGeneration.Should().NotBe(
                initialDynamicGeneration,
                "discovering a genuinely new default-backed slot changes the generation");
            inserted.Graph.TryGetMediumStateRef(hole, TraversalMedium.Gas, out _)
                .Should().BeTrue();
            inserted.Graph.TryGetSurfaceComponent(
                    unrelated,
                    TraversalMedium.Gas,
                    out unrelatedKey,
                    out unrelatedVersion)
                .Should().BeTrue();
        }

        long insertedDynamicGeneration;
        using (NavigationWorldGraphLease inserted = context.Pathing.TryAcquireNavigationGraph()!)
            insertedDynamicGeneration = inserted.Graph.GetInstance(0).DynamicSlotGeneration;
        grid.TryRemoveVoxel(first.Index).Should().BeTrue();
        SimulateUntil(context, () => !HasNode(context, first));
        using NavigationWorldGraphLease removed = context.Pathing.TryAcquireNavigationGraph()!;
        removed.Graph.GetInstance(0).DynamicSlotGeneration.Should().NotBe(
            insertedDynamicGeneration,
            "retiring an omitted default-backed slot changes the generation");
        removed.Graph.TryGetNodeRef(first, out _).Should().BeFalse(
            "an unauthored default slot should retire when sparse matter is removed");
        removed.Graph.TryGetSurfaceComponent(first, TraversalMedium.Gas, out _, out _)
            .Should().BeFalse();
        removed.Graph.TryGetSurfaceComponent(
                unrelated,
                TraversalMedium.Gas,
                out NavigationSurfaceComponentKey currentUnrelatedKey,
                out long currentUnrelatedVersion)
            .Should().BeTrue();
        currentUnrelatedKey.Should().Be(unrelatedKey);
        currentUnrelatedVersion.Should().Be(unrelatedVersion,
            "an unrelated disconnected component must remain current");
    }

    [Fact]
    public void DefaultBaselineResnapshot_ShouldPreserveGenerationOnlyForTheExactSlotSet()
    {
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        using var world = new GridWorld();
        world.TryAddGrid(configuration, new[] { default(VoxelIndex) }, out ushort gridIndex)
            .Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMapInstance initial = Compose(
            world,
            new NavigationMapBuilder("map", binding).SetDefaultCell(GasCell).Build());

        NavigationMapInstance unchanged = ResnapshotDefault(world, initial, instanceVersion: 2);
        unchanged.DynamicSlotGeneration.Should().Be(initial.DynamicSlotGeneration,
            "rediscovering every omitted source slot is an exact no-op");

        VoxelIndex addedIndex = new(1, 0, 0);
        VoxelGrid grid = world.ActiveGrids[gridIndex];
        grid.TryAddVoxel(addedIndex, out _).Should().BeTrue();
        NavigationMapInstance added = ResnapshotDefault(world, unchanged, instanceVersion: 3);
        added.DynamicSlotGeneration.Should().NotBe(unchanged.DynamicSlotGeneration);

        grid.TryRemoveVoxel(addedIndex).Should().BeTrue();
        NavigationMapInstance removed = ResnapshotDefault(world, added, instanceVersion: 4);
        removed.DynamicSlotGeneration.Should().NotBe(added.DynamicSlotGeneration);
    }

    [Theory]
    [InlineData(2, true)]
    [InlineData(1, false)]
    public void DefaultMaterialization_ShouldRecheckPerMapDynamicSlotCeiling(
        int maximumPerMap,
        bool expectedApplied)
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSettings(
                defaults.MaintenanceBudget,
                maximumPerMap,
                maximumDynamicSlots: 2,
                operationLimits: CreateOperationLimits(
                    maximumPerMap,
                    maximumOverlayCells: 2)));
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        context.World.TryAddGrid(
                configuration,
                new[] { default(VoxelIndex), new VoxelIndex(1, 0, 0) },
                out _)
            .Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var install = new NavigationMapCommitOperation(
            new PreparedNavigationMap(
                new NavigationMapBuilder("map", binding).SetDefaultCell(GasCell).Build(),
                1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 1);
        context.Pathing.Admit(install).Should().BeTrue();

        SimulateUntil(context, () => install.Receipt.Status != NavigationOperationStatus.Pending);
        for (int frame = 0; frame < 16; frame++)
            context.Simulate();

        if (expectedApplied)
        {
            install.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
            using NavigationWorldGraphLease graph = context.Pathing.TryAcquireNavigationGraph()!;
            graph.Graph.GetInstance(0).DynamicSlotCount.Should().Be(2);
        }
        else
        {
            using NavigationWorldGraphLease graph = context.Pathing.TryAcquireNavigationGraph()!;
            graph.Graph.GetInstance(0).DynamicSlotCount.Should().BeLessThanOrEqualTo(1,
                "one-over default discovery must not publish an oversized candidate");
        }
    }

    [Fact]
    public void DefaultCoveredCapacity_ShouldCountAllMapsAtTheTotalCeiling()
    {
        using var world = new GridWorld();
        var instances = new NavigationMapInstance[2];
        for (int ordinal = 0; ordinal < 2; ordinal++)
        {
            int x = ordinal * 4;
            GridConfiguration configuration = new(
                new Vector3d(x, 0, 0),
                new Vector3d(x, 0, 0),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
                storageKind: GridStorageKind.Sparse);
            world.TryAddGrid(configuration, new[] { default(VoxelIndex) }, out _)
                .Should().BeTrue();
            configuration.TryNormalize(out NormalizedGridConfiguration binding)
                .Should().BeTrue();
            instances[ordinal] = Compose(
                world,
                new NavigationMapBuilder($"map-{ordinal}", binding)
                    .SetDefaultCell(GasCell)
                    .Build());
        }
        var graph = new NavigationWorldGraph(1, instances);

        graph.IsWithinDynamicSlotCapacity(maximumPerMap: 1, maximumTotal: 2)
            .Should().BeTrue();
        graph.IsWithinDynamicSlotCapacity(maximumPerMap: 1, maximumTotal: 1)
            .Should().BeFalse("one byte-like slot below the total must fail closed");
    }

    [Fact]
    public void MediumStateResolution_ShouldHonorOnlyTheRequestedMediumClosure()
    {
        GridConfiguration configuration = RectangularConfiguration(GridStorageKind.Dense);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        using var world = new GridWorld();
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        NavigationWorldGraph graph = BuildComponentGraph(
            world,
            new NavigationMapBuilder("map", binding)
                .AddCell(
                    default,
                    Cell(TraversalMedia.Solid | TraversalMedia.Gas | TraversalMedia.Liquid))
                .Build());
        var address = new NavigationCellAddress("map", default);
        graph.TryGetSurfaceComponent(
                address,
                TraversalMedium.Gas,
                out NavigationSurfaceComponentKey gasKey,
                out _)
            .Should().BeTrue();
        graph.TryGetSurfaceComponent(
                address,
                TraversalMedium.Solid,
                out NavigationSurfaceComponentKey solidKey,
                out _)
            .Should().BeTrue();

        NavigationWorldGraph gasClosed = graph.WithClosedStructuralComponents(
            NavigationSurfaceComponentKeySet.Empty.Add(gasKey),
            closeAllStructuralComponents: false,
            graph.GraphVersion + 1);
        gasClosed.TryGetMediumStateRef(address, TraversalMedium.Gas, out _)
            .Should().BeFalse();
        gasClosed.TryGetMediumStateRef(address, TraversalMedium.Liquid, out _)
            .Should().BeTrue("an unrelated medium closure cannot hide Liquid");
        graph.TryGetNodeRef(address, out NavigationNodeRef node).Should().BeTrue();
        gasClosed.TryGetNodeState(node, out _).Should().BeTrue(
            "the legacy physical-node API keeps its Solid closure behavior");

        NavigationWorldGraph solidClosed = graph.WithClosedStructuralComponents(
            NavigationSurfaceComponentKeySet.Empty.Add(solidKey),
            closeAllStructuralComponents: false,
            graph.GraphVersion + 1);
        solidClosed.TryGetMediumStateRef(address, TraversalMedium.Gas, out _)
            .Should().BeTrue("an unrelated Solid closure cannot hide Gas");
        solidClosed.TryGetNodeState(node, out _).Should().BeFalse();
    }

    [Fact]
    public void SparseDefaultDiscovery_OneBelowBudget_ShouldNeverPublishAPartialStateSet()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var budget = new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            maxBaselineAddresses: 1,
            defaults.MaintenanceBudget.MaxOverlaySlots,
            defaults.MaintenanceBudget.MaxComponentNodes,
            defaults.MaintenanceBudget.MaxSeamCandidateProbes,
            defaults.MaintenanceBudget.MaxExplicitEdges,
            defaults.MaintenanceBudget.MaxDependencyEntries,
            defaults.MaintenanceBudget.MaxSurfaceComponentEdges);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSettings(budget));
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        VoxelIndex secondIndex = new(1, 0, 0);
        context.World.TryAddGrid(configuration, new[] { default(VoxelIndex), secondIndex }, out _)
            .Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMapCommitOperation install = AdmitMap(
            context,
            new NavigationMapBuilder("map", binding)
                .SetDefaultCell(GasCell)
                .Build(),
            sequence: 1);
        var first = new NavigationCellAddress("map", default);
        var second = new NavigationCellAddress("map", secondIndex);
        int pendingFrameCount = 0;

        bool complete = false;
        for (int frame = 0; frame < 64 && !complete; frame++)
        {
            context.Simulate();
            pendingFrameCount++;
            using NavigationWorldGraphLease graph = context.Pathing.TryAcquireNavigationGraph()!;
            bool hasFirst = graph.Graph.TryGetMediumStateRef(
                first,
                TraversalMedium.Gas,
                out _);
            bool hasSecond = graph.Graph.TryGetMediumStateRef(
                second,
                TraversalMedium.Gas,
                out _);
            hasFirst.Should().Be(hasSecond,
                "a chunked default baseline must publish all present addresses atomically");
            complete = hasFirst && hasSecond;
        }

        pendingFrameCount.Should().BeGreaterThan(1,
            "one address of baseline budget cannot finish two present addresses in one boundary");
        install.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        complete.Should().BeTrue();
        HasMediumState(context, first, TraversalMedium.Gas).Should().BeTrue();
        HasMediumState(context, second, TraversalMedium.Gas).Should().BeTrue();
    }

    [Fact]
    public void DefaultReplacement_ShouldInvalidateOnlyChangedPageAndMediumComponent()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(64, 0, 0),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap gasMap = BuildDefaultReplacementMap(binding, GasCell);
        NavigationMapCommitOperation initial = AdmitMap(context, gasMap, sequence: 1);
        var defaultAddress = new NavigationCellAddress("map", new VoxelIndex(64, 0, 0));
        var solidAddress = new NavigationCellAddress("map", default);
        SimulateUntil(
            context,
            () => initial.Receipt.Status != NavigationOperationStatus.Pending
                && HasMediumState(context, defaultAddress, TraversalMedium.Gas));

        NavigationSurfaceComponentKey solidKey;
        long solidVersion;
        using (NavigationWorldGraphLease before = context.Pathing.TryAcquireNavigationGraph()!)
        {
            before.Graph.TryGetSurfaceComponent(
                    solidAddress,
                    TraversalMedium.Solid,
                    out solidKey,
                    out solidVersion)
                .Should().BeTrue();
        }

        NavigationMap liquidMap = BuildDefaultReplacementMap(
            binding,
            Cell(TraversalMedia.Liquid));
        var replacement = new NavigationMapCommitOperation(
            new PreparedNavigationMap(liquidMap, 2),
            OverlayReplacementPolicy.Clear,
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(replacement).Should().BeTrue();
        SimulateUntil(
            context,
            () => replacement.Receipt.Status != NavigationOperationStatus.Pending
                && HasMediumState(context, defaultAddress, TraversalMedium.Liquid));

        using NavigationWorldGraphLease after = context.Pathing.TryAcquireNavigationGraph()!;
        after.Graph.TryGetMediumStateRef(defaultAddress, TraversalMedium.Gas, out _)
            .Should().BeFalse();
        after.Graph.TryGetMediumStateRef(defaultAddress, TraversalMedium.Liquid, out _)
            .Should().BeTrue();
        after.Graph.TryGetSurfaceComponent(defaultAddress, TraversalMedium.Gas, out _, out _)
            .Should().BeFalse();
        after.Graph.TryGetSurfaceComponent(defaultAddress, TraversalMedium.Liquid, out _, out _)
            .Should().BeTrue();
        after.Graph.TryGetSurfaceComponent(
                solidAddress,
                TraversalMedium.Solid,
                out NavigationSurfaceComponentKey currentSolidKey,
                out long currentSolidVersion)
            .Should().BeTrue();
        currentSolidKey.Should().Be(solidKey);
        currentSolidVersion.Should().Be(solidVersion);
    }

    [Fact]
    public void OverlayMediumChange_ShouldPreserveUnrelatedPageAndComponentDependencies()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(128, 0, 0),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var builder = new NavigationMapBuilder("map", binding);
        for (int slot = 0; slot < 65; slot++)
            builder.AddCell(new VoxelIndex(slot * 2, 0, 0), Cell(TraversalMedia.Solid));
        NavigationMapCommitOperation install = AdmitMap(context, builder.Build(), sequence: 1);
        var unaffectedAddress = new NavigationCellAddress("map", default);
        var changedAddress = new NavigationCellAddress("map", new VoxelIndex(128, 0, 0));
        SimulateUntil(
            context,
            () => install.Receipt.Status != NavigationOperationStatus.Pending
                && HasMediumState(context, changedAddress, TraversalMedium.Solid));

        NavigationSurfaceComponentKey unaffectedKey;
        long unaffectedVersion;
        GraphPageDependency unaffectedPage;
        GraphPageDependency changedPage;
        using (NavigationWorldGraphLease before = context.Pathing.TryAcquireNavigationGraph()!)
        {
            before.Graph.TryGetSurfaceComponent(
                    unaffectedAddress,
                    TraversalMedium.Solid,
                    out unaffectedKey,
                    out unaffectedVersion)
                .Should().BeTrue();
            before.Graph.TryGetPageDependency(
                    new GraphPageDependencyAddress("map", 0),
                    out unaffectedPage)
                .Should().BeTrue();
            before.Graph.TryGetPageDependency(
                    new GraphPageDependencyAddress("map", 1),
                    out changedPage)
                .Should().BeTrue();
        }

        var overlay = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta("map", new[]
                {
                    NavigationCellOverlayOperation.Set(
                        changedAddress.Index,
                        Cell(TraversalMedia.Liquid))
                })
            })),
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(overlay).Should().BeTrue();
        SimulateUntil(context, () => overlay.Receipt.Status != NavigationOperationStatus.Pending);

        using NavigationWorldGraphLease after = context.Pathing.TryAcquireNavigationGraph()!;
        after.Graph.TryGetMediumStateRef(changedAddress, TraversalMedium.Solid, out _)
            .Should().BeFalse();
        after.Graph.TryGetMediumStateRef(changedAddress, TraversalMedium.Liquid, out _)
            .Should().BeTrue();
        after.Graph.TryGetSurfaceComponent(
                unaffectedAddress,
                TraversalMedium.Solid,
                out NavigationSurfaceComponentKey currentKey,
                out long currentVersion)
            .Should().BeTrue();
        currentKey.Should().Be(unaffectedKey);
        currentVersion.Should().Be(unaffectedVersion);
        after.Graph.TryGetPageDependency(
                new GraphPageDependencyAddress("map", 0),
                out GraphPageDependency currentUnaffectedPage)
            .Should().BeTrue();
        currentUnaffectedPage.Should().Be(unaffectedPage);
        after.Graph.TryGetPageDependency(
                new GraphPageDependencyAddress("map", 1),
                out GraphPageDependency currentChangedPage)
            .Should().BeTrue();
        currentChangedPage.Should().NotBe(changedPage);
    }

    [Fact]
    public void OverlayMediumAddition_ShouldMergeOnlyTheIncidentVolumeComponent()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = RectangularConfiguration(GridStorageKind.Dense);
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        VoxelIndex secondIndex = new(1, 0, 0);
        NavigationMapCommitOperation install = AdmitMap(
            context,
            new NavigationMapBuilder("map", binding)
                .AddCell(default, GasCell)
                .AddCell(secondIndex, Cell(TraversalMedia.Solid))
                .Build(),
            sequence: 1);
        var first = new NavigationCellAddress("map", default);
        var second = new NavigationCellAddress("map", secondIndex);
        SimulateUntil(
            context,
            () => install.Receipt.Status != NavigationOperationStatus.Pending
                && HasMediumState(context, first, TraversalMedium.Gas));

        var overlay = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta("map", new[]
                {
                    NavigationCellOverlayOperation.Set(secondIndex, GasCell)
                })
            })),
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(overlay).Should().BeTrue();
        SimulateUntil(context, () => overlay.Receipt.Status != NavigationOperationStatus.Pending);

        using NavigationWorldGraphLease graph = context.Pathing.TryAcquireNavigationGraph()!;
        graph.Graph.AreInSameSurfaceComponent(
                first,
                TraversalMedium.Gas,
                second,
                TraversalMedium.Gas)
            .Should().BeTrue();
        graph.Graph.TryGetSurfaceComponent(second, TraversalMedium.Solid, out _, out _)
            .Should().BeFalse();
    }

    [Fact]
    public void RectangularVolumeComponents_ShouldUseSameMediumPositiveFacesOnly()
    {
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(1, 1, 1),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        using var world = new GridWorld();
        world.TryAddGrid(configuration, out ushort gridIndex).Should().BeTrue();
        VoxelGrid grid = world.ActiveGrids[gridIndex];
        NavigationCell permissive = new(
            TraversalMedia.Gas | TraversalMedia.Liquid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        NavigationCell constrained = new(
            TraversalMedia.Gas | TraversalMedia.Liquid,
            TraversalCapability.Fly,
            new NavigationAreaId(7),
            (Fixed64)99,
            Fixed64.Zero,
            Fixed64.Zero);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .SetDefaultCell(permissive)
            .AddCell(new VoxelIndex(1, 1, 1), constrained)
            .Build();
        grid.TryGetVoxel(new VoxelIndex(1, 1, 1), out Voxel? blocked).Should().BeTrue();
        grid.TryAddObstacle(blocked!, world.AllocateObstacleToken()).Should().BeTrue();
        NavigationWorldGraph graph = BuildComponentGraph(world, map);
        var first = new NavigationCellAddress("map", default);
        var last = new NavigationCellAddress("map", new VoxelIndex(1, 1, 1));

        graph.AreInSameSurfaceComponent(
                first,
                TraversalMedium.Gas,
                last,
                TraversalMedium.Gas)
            .Should().BeTrue(
                "structural connectivity ignores query passability, obstacles, cost, and clearance");
        graph.TryGetSurfaceComponent(
                first,
                TraversalMedium.Gas,
                out NavigationSurfaceComponentKey gasKey,
                out _)
            .Should().BeTrue();
        graph.TryGetSurfaceComponent(
                first,
                TraversalMedium.Liquid,
                out NavigationSurfaceComponentKey liquidKey,
                out _)
            .Should().BeTrue();
        gasKey.Should().NotBe(liquidKey);

        using var diagonalWorld = new GridWorld();
        diagonalWorld.TryAddGrid(configuration, out _).Should().BeTrue();
        NavigationMap diagonalMap = new NavigationMapBuilder("map", binding)
            .AddCell(default, GasCell)
            .AddCell(new VoxelIndex(1, 1, 1), GasCell)
            .Build();
        NavigationWorldGraph diagonalGraph = BuildComponentGraph(diagonalWorld, diagonalMap);

        diagonalGraph.AreInSameSurfaceComponent(
                first,
                TraversalMedium.Gas,
                last,
                TraversalMedium.Gas)
            .Should().BeFalse("a rectangular corner is not one of the six positive faces");
    }

    [Fact]
    public void MultiMediumAddress_ShouldOwnThreeIndependentComponentMemberships()
    {
        GridConfiguration configuration = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        using var world = new GridWorld();
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        NavigationWorldGraph graph = BuildComponentGraph(
            world,
            new NavigationMapBuilder("map", binding)
                .AddCell(default, Cell(
                    TraversalMedia.Solid | TraversalMedia.Gas | TraversalMedia.Liquid))
                .Build());
        var address = new NavigationCellAddress("map", default);

        graph.TryGetSurfaceComponent(
                address,
                TraversalMedium.Solid,
                out NavigationSurfaceComponentKey solid,
                out _)
            .Should().BeTrue();
        graph.TryGetSurfaceComponent(
                address,
                TraversalMedium.Gas,
                out NavigationSurfaceComponentKey gas,
                out _)
            .Should().BeTrue();
        graph.TryGetSurfaceComponent(
                address,
                TraversalMedium.Liquid,
                out NavigationSurfaceComponentKey liquid,
                out _)
            .Should().BeTrue();
        new[] { solid, gas, liquid }.Should().OnlyHaveUniqueItems();
        solid.Medium.Should().Be(TraversalMedium.Solid);
        gas.Medium.Should().Be(TraversalMedium.Gas);
        liquid.Medium.Should().Be(TraversalMedium.Liquid);
        graph.SurfaceComponents.TryGet(solid, out _).Should().BeTrue();
        graph.SurfaceComponents.TryGet(gas, out _).Should().BeTrue();
        graph.SurfaceComponents.TryGet(liquid, out _).Should().BeTrue();
    }

    [Fact]
    public void MediumReplacement_ShouldPreserveUnchangedSolidComponentOnly()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var install = new NavigationMapCommitOperation(
            new PreparedNavigationMap(new NavigationMapBuilder("map", binding)
                .AddCell(default, Cell(TraversalMedia.Solid | TraversalMedia.Gas))
                .Build(), 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 1);
        context.Pathing.Admit(install).Should().BeTrue();
        var address = new NavigationCellAddress("map", default);
        SimulateUntil(context, () => HasMediumComponent(
            context,
            address,
            TraversalMedium.Gas,
            out _,
            out _));
        NavigationSurfaceComponentKey solidKey;
        long solidVersion;
        using (NavigationWorldGraphLease initial = context.Pathing.TryAcquireNavigationGraph()!)
        {
            initial.Graph.TryGetSurfaceComponent(
                    address,
                    TraversalMedium.Solid,
                    out solidKey,
                    out solidVersion)
                .Should().BeTrue();
        }

        var overlay = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta("map", new[]
                {
                    NavigationCellOverlayOperation.Set(
                        default,
                        Cell(TraversalMedia.Solid | TraversalMedia.Liquid))
                })
            })),
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(overlay).Should().BeTrue();
        SimulateUntil(context, () => overlay.Receipt.Status != NavigationOperationStatus.Pending);

        using NavigationWorldGraphLease changed = context.Pathing.TryAcquireNavigationGraph()!;
        changed.Graph.TryGetSurfaceComponent(
                address,
                TraversalMedium.Solid,
                out NavigationSurfaceComponentKey currentSolidKey,
                out long currentSolidVersion)
            .Should().BeTrue();
        currentSolidKey.Should().Be(solidKey);
        currentSolidVersion.Should().Be(solidVersion,
            "an unchanged medium retains its exact dependency record");
        changed.Graph.TryGetSurfaceComponent(address, TraversalMedium.Gas, out _, out _)
            .Should().BeFalse();
        changed.Graph.TryGetSurfaceComponent(address, TraversalMedium.Liquid, out _, out _)
            .Should().BeTrue();
    }

    [Fact]
    public void ExplicitConnection_ShouldJoinSolidButNeverVolumeComponents()
    {
        using var world = new GridWorld();
        GridConfiguration sourceConfiguration = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        GridConfiguration destinationConfiguration = new(
            new Vector3d(10, 0, 0),
            new Vector3d(10, 0, 0),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        world.TryAddGrid(sourceConfiguration, out _).Should().BeTrue();
        world.TryAddGrid(destinationConfiguration, out _).Should().BeTrue();
        sourceConfiguration.TryNormalize(out NormalizedGridConfiguration sourceBinding)
            .Should().BeTrue();
        destinationConfiguration.TryNormalize(out NormalizedGridConfiguration destinationBinding)
            .Should().BeTrue();
        var source = new NavigationCellAddress("source", default);
        var destination = new NavigationCellAddress("destination", default);
        NavigationMapInstance sourceInstance = Compose(
            world,
            new NavigationMapBuilder("source", sourceBinding)
                .AddCell(default, Cell(TraversalMedia.Solid | TraversalMedia.Gas))
                .Build());
        NavigationMapInstance destinationInstance = Compose(
            world,
            new NavigationMapBuilder("destination", destinationBinding)
                .AddCell(default, Cell(TraversalMedia.Solid | TraversalMedia.Gas))
                .Build());
        var definition = new NavigationConnection(
            "one-way",
            default,
            destination,
            GetFoot(sourceBinding, default),
            GetFoot(destinationBinding, default),
            Fixed64.Zero,
            Fixed64.One);
        var record = new NavigationExplicitConnectionRecord(
            new NavigationConnectionOwnerKey("source", definition.Id),
            definition,
            isActive: true,
            corridorCost: Fixed64.One,
            NavigationPagedSequence<GridNavigationPortal>.Empty);
        NavigationExplicitConnectionIndex connections =
            NavigationExplicitConnectionIndex.Empty.SetOwner(record, out _);
        var owners = new NavigationPagedSequence<NavigationConnectionOwnerKey>.Builder(16);
        owners.Append(record.Owner);
        NavigationPagedSequence<NavigationConnectionOwnerKey> row = owners.Seal();
        connections = connections.SetEndpointRow(
            source,
            NavigationPagedSequence<NavigationConnectionOwnerKey>.Empty,
            row,
            out _);
        connections = connections.SetEndpointRow(
            destination,
            NavigationPagedSequence<NavigationConnectionOwnerKey>.Empty,
            row,
            out _);
        var baseGraph = new NavigationWorldGraph(
            1,
            new[] { destinationInstance, sourceInstance },
            explicitConnections: connections);
        var graph = new NavigationWorldGraph(
            1,
            new[] { destinationInstance, sourceInstance },
            explicitConnections: connections,
            surfaceComponents: NavigationSurfaceComponentTestFactory.Build(baseGraph));

        graph.AreInSameSurfaceComponent(
                source,
                TraversalMedium.Solid,
                destination,
                TraversalMedium.Solid)
            .Should().BeTrue();
        graph.AreInSameSurfaceComponent(
                source,
                TraversalMedium.Gas,
                destination,
                TraversalMedium.Gas)
            .Should().BeFalse("authored explicit links are never positive-face volume adjacency");
    }

    [Fact]
    public void AutomaticSeam_ShouldJoinSameMediumVolumeComponentsAcrossMaps()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        GridConfiguration firstConfiguration = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyMetrics: metrics);
        GridConfiguration secondConfiguration = new(
            new Vector3d(1, 0, 0),
            new Vector3d(1, 0, 0),
            topologyMetrics: metrics);
        context.World.TryAddGrid(firstConfiguration, out _).Should().BeTrue();
        context.World.TryAddGrid(secondConfiguration, out _).Should().BeTrue();
        firstConfiguration.TryNormalize(out NormalizedGridConfiguration firstBinding)
            .Should().BeTrue();
        secondConfiguration.TryNormalize(out NormalizedGridConfiguration secondBinding)
            .Should().BeTrue();
        NavigationMapCommitOperation first = AdmitMap(
            context,
            new NavigationMapBuilder("first", firstBinding)
                .AddCell(default, GasCell)
                .Build(),
            sequence: 1);
        NavigationMapCommitOperation second = AdmitMap(
            context,
            new NavigationMapBuilder("second", secondBinding)
                .AddCell(default, GasCell)
                .Build(),
            sequence: 2);
        var firstAddress = new NavigationCellAddress("first", default);
        var secondAddress = new NavigationCellAddress("second", default);
        SimulateUntil(
            context,
            () => first.Receipt.Status != NavigationOperationStatus.Pending
                && second.Receipt.Status != NavigationOperationStatus.Pending);
        first.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        second.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        HasMediumState(context, firstAddress, TraversalMedium.Gas).Should().BeTrue();
        HasMediumState(context, secondAddress, TraversalMedium.Gas).Should().BeTrue();
        SimulateUntil(
            context,
            () => AreSameComponent(
                context,
                firstAddress,
                secondAddress,
                TraversalMedium.Gas));

        AreSameComponent(context, firstAddress, secondAddress, TraversalMedium.Gas)
            .Should().BeTrue();
    }

    [Theory]
    [InlineData(TraversalMedium.Gas)]
    [InlineData(TraversalMedium.Liquid)]
    public void AutomaticSeamLifecycle_ShouldMergeAndSplitExactVolumeMedium(
        TraversalMedium medium)
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        GridConfiguration firstConfiguration = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyMetrics: metrics);
        GridConfiguration secondConfiguration = new(
            new Vector3d(1, 0, 0),
            new Vector3d(1, 0, 0),
            topologyMetrics: metrics);
        GridConfiguration unrelatedConfiguration = new(
            new Vector3d(10, 0, 0),
            new Vector3d(10, 0, 0),
            topologyMetrics: metrics);
        context.World.TryAddGrid(firstConfiguration, out _).Should().BeTrue();
        context.World.TryAddGrid(secondConfiguration, out ushort secondGridIndex)
            .Should().BeTrue();
        context.World.TryAddGrid(unrelatedConfiguration, out _).Should().BeTrue();
        firstConfiguration.TryNormalize(out NormalizedGridConfiguration firstBinding)
            .Should().BeTrue();
        secondConfiguration.TryNormalize(out NormalizedGridConfiguration secondBinding)
            .Should().BeTrue();
        unrelatedConfiguration.TryNormalize(out NormalizedGridConfiguration unrelatedBinding)
            .Should().BeTrue();
        TraversalMedia media = (TraversalMedia)NavigationMediumSlots<byte>.GetBit(medium);
        NavigationCell cell = Cell(media);
        NavigationMapCommitOperation first = AdmitMap(
            context,
            new NavigationMapBuilder("first", firstBinding).AddCell(default, cell).Build(),
            sequence: 1);
        NavigationMapCommitOperation second = AdmitMap(
            context,
            new NavigationMapBuilder("second", secondBinding).AddCell(default, cell).Build(),
            sequence: 2);
        NavigationMapCommitOperation unrelated = AdmitMap(
            context,
            new NavigationMapBuilder("unrelated", unrelatedBinding)
                .AddCell(default, cell)
                .Build(),
            sequence: 3);
        var firstAddress = new NavigationCellAddress("first", default);
        var secondAddress = new NavigationCellAddress("second", default);
        var unrelatedAddress = new NavigationCellAddress("unrelated", default);
        SimulateUntil(
            context,
            () => first.Receipt.Status != NavigationOperationStatus.Pending
                && second.Receipt.Status != NavigationOperationStatus.Pending
                && unrelated.Receipt.Status != NavigationOperationStatus.Pending
                && AreSameComponent(context, firstAddress, secondAddress, medium));

        NavigationSurfaceComponentKey unrelatedKey;
        long unrelatedVersion;
        using (NavigationWorldGraphLease graph = context.Pathing.TryAcquireNavigationGraph()!)
        {
            graph.Graph.TryGetSurfaceComponent(
                    unrelatedAddress,
                    medium,
                    out unrelatedKey,
                    out unrelatedVersion)
                .Should().BeTrue();
        }

        context.World.TryRemoveGrid(secondGridIndex).Should().BeTrue();
        SimulateUntil(
            context,
            () => context.Pathing.RetainedCompositionWorkCount == 0
                && !AreSameComponent(context, firstAddress, secondAddress, medium));
        using (NavigationWorldGraphLease split = context.Pathing.TryAcquireNavigationGraph()!)
        {
            split.Graph.TryGetSurfaceComponent(
                    unrelatedAddress,
                    medium,
                    out NavigationSurfaceComponentKey currentKey,
                    out long currentVersion)
                .Should().BeTrue();
            currentKey.Should().Be(unrelatedKey);
            currentVersion.Should().Be(unrelatedVersion);
        }

        context.World.TryAddGrid(secondConfiguration, out _).Should().BeTrue();
        SimulateUntil(
            context,
            () => AreSameComponent(context, firstAddress, secondAddress, medium));
        using NavigationWorldGraphLease merged = context.Pathing.TryAcquireNavigationGraph()!;
        merged.Graph.TryGetSurfaceComponent(
                unrelatedAddress,
                medium,
                out NavigationSurfaceComponentKey mergedKey,
                out long mergedVersion)
            .Should().BeTrue();
        mergedKey.Should().Be(unrelatedKey);
        mergedVersion.Should().Be(unrelatedVersion);
    }

    [Fact]
    public void DefaultMaterialization_ShouldDiscoverSeamsBeforePublishingVolumeComponents()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        GridConfiguration firstConfiguration = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyMetrics: metrics);
        GridConfiguration secondConfiguration = new(
            new Vector3d(1, 0, 0),
            new Vector3d(1, 0, 0),
            topologyMetrics: metrics);
        context.World.TryAddGrid(firstConfiguration, out _).Should().BeTrue();
        context.World.TryAddGrid(secondConfiguration, out _).Should().BeTrue();
        firstConfiguration.TryNormalize(out NormalizedGridConfiguration firstBinding)
            .Should().BeTrue();
        secondConfiguration.TryNormalize(out NormalizedGridConfiguration secondBinding)
            .Should().BeTrue();
        NavigationMapCommitOperation first = AdmitMap(
            context,
            new NavigationMapBuilder("first", firstBinding)
                .SetDefaultCell(GasCell)
                .Build(),
            sequence: 1);
        NavigationMapCommitOperation second = AdmitMap(
            context,
            new NavigationMapBuilder("second", secondBinding)
                .SetDefaultCell(GasCell)
                .Build(),
            sequence: 2);
        var firstAddress = new NavigationCellAddress("first", default);
        var secondAddress = new NavigationCellAddress("second", default);

        SimulateUntil(
            context,
            () => first.Receipt.Status != NavigationOperationStatus.Pending
                && second.Receipt.Status != NavigationOperationStatus.Pending);
        first.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        second.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        SimulateUntil(
            context,
            () => HasMediumState(context, firstAddress, TraversalMedium.Gas)
                && HasMediumState(context, secondAddress, TraversalMedium.Gas));
        HasMediumState(context, firstAddress, TraversalMedium.Gas).Should().BeTrue();
        HasMediumState(context, secondAddress, TraversalMedium.Gas).Should().BeTrue();
        SimulateUntil(
            context,
            () => AreSameComponent(
                context,
                firstAddress,
                secondAddress,
                TraversalMedium.Gas));

        AreSameComponent(context, firstAddress, secondAddress, TraversalMedium.Gas)
            .Should().BeTrue(
                "default-backed boundary nodes must refresh seams before components publish");
    }

    [Fact]
    public void SparseDefaultBoundaryInsertion_ShouldRefreshSeamAndComponentAtomically()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        GridConfiguration firstConfiguration = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Sparse);
        GridConfiguration secondConfiguration = new(
            new Vector3d(1, 0, 0),
            new Vector3d(1, 0, 0),
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Sparse);
        context.World.TryAddGrid(
                firstConfiguration,
                System.Array.Empty<VoxelIndex>(),
                out ushort firstGridIndex)
            .Should().BeTrue();
        context.World.TryAddGrid(secondConfiguration, new[] { default(VoxelIndex) }, out _)
            .Should().BeTrue();
        firstConfiguration.TryNormalize(out NormalizedGridConfiguration firstBinding)
            .Should().BeTrue();
        secondConfiguration.TryNormalize(out NormalizedGridConfiguration secondBinding)
            .Should().BeTrue();
        NavigationMapCommitOperation first = AdmitMap(
            context,
            new NavigationMapBuilder("first", firstBinding)
                .SetDefaultCell(GasCell)
                .Build(),
            sequence: 1);
        NavigationMapCommitOperation second = AdmitMap(
            context,
            new NavigationMapBuilder("second", secondBinding)
                .SetDefaultCell(GasCell)
                .Build(),
            sequence: 2);
        var firstAddress = new NavigationCellAddress("first", default);
        var secondAddress = new NavigationCellAddress("second", default);
        SimulateUntil(
            context,
            () => first.Receipt.Status != NavigationOperationStatus.Pending
                && second.Receipt.Status != NavigationOperationStatus.Pending
                && HasMediumState(context, secondAddress, TraversalMedium.Gas)
                && context.Pathing.RetainedCompositionWorkCount == 0);
        HasNode(context, firstAddress).Should().BeFalse();

        context.World.ActiveGrids[firstGridIndex].TryAddVoxel(default, out _)
            .Should().BeTrue();
        SimulateUntil(
            context,
            () => AreSameComponent(
                context,
                firstAddress,
                secondAddress,
                TraversalMedium.Gas));

        AreSameComponent(context, firstAddress, secondAddress, TraversalMedium.Gas)
            .Should().BeTrue(
                "new sparse boundary matter and its automatic seam publish as one graph");
    }

    [Fact]
    public void StaleDefaultSeamProbe_ShouldRebuildItsCompletedPhysicalBaselineBeforeRetry()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var budget = new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            defaults.MaintenanceBudget.MaxOverlaySlots,
            maxComponentNodes: 1,
            maxSeamCandidateProbes: 1,
            defaults.MaintenanceBudget.MaxExplicitEdges,
            defaults.MaintenanceBudget.MaxDependencyEntries,
            maxSurfaceComponentEdges: 1);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSettings(budget));
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        GridConfiguration firstConfiguration = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Sparse);
        GridConfiguration secondConfiguration = new(
            new Vector3d(1, 0, 0),
            new Vector3d(1, 0, 0),
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Sparse);
        context.World.TryAddGrid(
                firstConfiguration,
                Array.Empty<VoxelIndex>(),
                out ushort firstGridIndex)
            .Should().BeTrue();
        context.World.TryAddGrid(secondConfiguration, new[] { default(VoxelIndex) }, out _)
            .Should().BeTrue();
        firstConfiguration.TryNormalize(out NormalizedGridConfiguration firstBinding)
            .Should().BeTrue();
        secondConfiguration.TryNormalize(out NormalizedGridConfiguration secondBinding)
            .Should().BeTrue();
        NavigationMapCommitOperation first = AdmitMap(
            context,
            new NavigationMapBuilder("first", firstBinding)
                .SetDefaultCell(GasCell)
                .Build(),
            sequence: 1);
        NavigationMapCommitOperation second = AdmitMap(
            context,
            new NavigationMapBuilder("second", secondBinding)
                .SetDefaultCell(GasCell)
                .Build(),
            sequence: 2);
        var firstAddress = new NavigationCellAddress("first", default);
        var secondAddress = new NavigationCellAddress("second", default);
        SimulateUntil(
            context,
            () => first.Receipt.Status != NavigationOperationStatus.Pending
                && second.Receipt.Status != NavigationOperationStatus.Pending
                && HasMediumState(context, secondAddress, TraversalMedium.Gas)
                && context.Pathing.RetainedCompositionWorkCount == 0);

        VoxelGrid firstGrid = context.World.ActiveGrids[firstGridIndex];
        firstGrid.TryAddVoxel(default, out _).Should().BeTrue();
        bool invalidatedProbe = false;
        bool restartedPhysicalBaseline = false;
        for (int frame = 0; frame < 512; frame++)
        {
            context.Simulate();
            MaintenanceWorkMeter meter = context.Pathing.NavigationMaintenanceMeter;
            if (!invalidatedProbe
                && context.Pathing.RetainedCompositionWorkCount != 0
                && meter.SeamCandidateProbes > 0)
            {
                invalidatedProbe = true;
                firstGrid.TryRemoveVoxel(default).Should().BeTrue();
                continue;
            }
            if (invalidatedProbe && meter.BaselineAddresses > 0)
                restartedPhysicalBaseline = true;
            if (invalidatedProbe
                && restartedPhysicalBaseline
                && context.Pathing.RetainedCompositionWorkCount == 0
                && !HasNode(context, firstAddress))
            {
                break;
            }
        }

        invalidatedProbe.Should().BeTrue();
        restartedPhysicalBaseline.Should().BeTrue(
            "a stale completed capture cannot be reused after the covered grid generation changes");
        context.Pathing.RetainedCompositionWorkCount.Should().Be(0);
        using (NavigationWorldGraphLease final = context.Pathing.TryAcquireNavigationGraph()!)
            final.Graph.HasClosedStructuralScope.Should().BeFalse();
        HasNode(context, firstAddress).Should().BeFalse();
    }

    [Fact]
    public void MultiMediumComponentIndex_ShouldAccountEachPartitionExactly()
    {
        var address = new NavigationCellAddress("map", default);
        NavigationSurfaceComponentIndex index = NavigationSurfaceComponentIndex.Empty;
        long emptyBytes = index.RetainedBytes;
        int emptyPages = index.PersistentPageCount;
        long expectedBytes = emptyBytes;
        int expectedPages = emptyPages;
        long[] partitionBytes = { 384L, 384L, 320L };
        int[] partitionPages = { 6, 6, 4 };

        for (TraversalMedium medium = TraversalMedium.Solid;
             medium <= TraversalMedium.Liquid;
             medium++)
        {
            var members = new NavigationPagedSequence<NavigationCellAddress>.Builder(24);
            members.Append(address);
            var component = new NavigationSurfaceComponent(
                new NavigationSurfaceComponentKey(address, medium),
                version: 1,
                members.Seal(),
                allSurfaceEdgesEuclideanCertified: true);
            index = index.AddComponentRecord(component, out _);
            index = index.AddMembership(address, component.Key, out _);
            int partition = (int)medium - (int)TraversalMedium.Solid;
            expectedBytes = checked(
                expectedBytes + partitionBytes[partition] + component.RetainedBytes);
            expectedPages = checked(
                expectedPages + partitionPages[partition] + component.PersistentPageCount);
        }

        index.RetainedBytes.Should().Be(expectedBytes);
        index.PersistentPageCount.Should().Be(expectedPages);
        (index.RetainedBytes <= expectedBytes - 1).Should().BeFalse(
            "one byte below the exact multi-medium index retention must fail closed");
        for (TraversalMedium medium = TraversalMedium.Solid;
             medium <= TraversalMedium.Liquid;
             medium++)
        {
            index.TryGet(address, medium, out _).Should().BeTrue();
        }
        index.TryGet(address, TraversalMedium.Unknown, out _).Should().BeFalse();
    }

    [Fact]
    public void RuntimeMaterializedTransitionReplacement_ShouldApplyAtExactPeakAndRejectOneByteBelow()
    {
        const long ExactPeak = 1_073_852L;

        using (var exactWorld = new GridWorld())
        using (NavigationGraphRuntime exact = CreateMaterializedCapacityScenario(
            exactWorld,
            ExactPeak,
            out NavigationMapCommitOperation accepted))
        {
            SimulateUntilTerminal(exact, accepted);
            accepted.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        }

        using var belowWorld = new GridWorld();
        using NavigationGraphRuntime below = CreateMaterializedCapacityScenario(
            belowWorld,
            ExactPeak - 1,
            out NavigationMapCommitOperation rejected);
        SimulateUntilTerminal(below, rejected);
        rejected.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        rejected.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        for (int frame = 0;
             frame < 128 && below.RetainedCompositionWorkCount != 0;
             frame++)
        {
            below.Maintain(frame + 2049);
        }
        below.RetainedCompositionWorkCount.Should().Be(0);
        below.RetainedOperationWorkCount.Should().Be(0);
    }

    [Fact]
    public void CapacityAbandon_ShouldRequeueDetachedUnrelatedGridPrefix()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        world.TryAddGrid(configuration, out ushort gridIndex).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        NavigationWorldGraph candidate = BuildComponentGraph(
            world,
            new NavigationMapBuilder("map", binding)
                .AddCell(default, GasCell)
                .Build());
        var changedState = new NavigationSurfaceComponentKey(
            new NavigationCellAddress("map", default),
            TraversalMedium.Gas);
        var work = new NavigationMaterializedComponentWork(
            candidate,
            NavigationSurfaceComponentKeySet.Empty.Add(changedState),
            NavigationSurfaceComponentKeySet.Empty,
            NavigationCellAddressSet.Empty,
            affectedMemberCount: 0,
            world: null,
            baselineCaptures: null,
            affectedMapOrdinals: null,
            affectedMapCount: 0,
            events: new GridEventInfo[1],
            eventCount: 1);
        long initialWorkBytes = work.RetainedBytes;
        long exactInitialRetention = checked(candidate.RetainedBytes + initialWorkBytes);
        using var runtime = new NavigationGraphRuntime(
            world,
            CreateSettings(
                TrailblazerWorldContextSettings.Default.MaintenanceBudget,
                maximumActiveBytes: exactInitialRetention));
        runtime.Store.TryPublish(candidate).Should().Be(NavigationCandidatePublication.Published);
        System.Reflection.FieldInfo eventsField = typeof(NavigationGraphRuntime).GetField(
            "_maintenanceEvents",
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic)!;
        var retainedEvents = (GridEventInfo[])eventsField.GetValue(runtime)!;
        VoxelGrid grid = world.ActiveGrids[gridIndex];
        retainedEvents[0] = new GridEventInfo(
            world.SpawnToken,
            grid.GridIndex,
            grid.SpawnToken,
            configuration,
            grid.Version,
            GridEventKind.GridChanged,
            changeStamp: new GridChangeStamp(1, 1));
        typeof(NavigationGraphRuntime).GetField(
                "_materializedComponentWork",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(runtime, work);

        runtime.Maintain(frame: 1);

        work.RetainedBytes.Should().BeGreaterThan(initialWorkBytes,
            "the retained work must exceed its exact admitted ceiling only after advancing");
        runtime.RetainedCompositionWorkCount.Should().Be(0);
        HasPendingGridChangeIngress(runtime).Should().BeTrue(
            "capacity abandonment cannot discard the prefix retained by materialized work");
    }

    [Fact]
    public void MaterializedComponentFrontScan_ShouldDebitEveryMapAndExactStateInspection()
    {
        GridConfiguration configuration = RectangularConfiguration(GridStorageKind.Dense);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        using var world = new GridWorld();
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        NavigationWorldGraph candidate = BuildComponentGraph(
            world,
            new NavigationMapBuilder("map", binding).AddCell(default, GasCell).Build());
        NavigationMapInstance instance = candidate.GetInstance(0);
        var state = new NavigationSurfaceComponentKey(
            new NavigationCellAddress("map", default),
            TraversalMedium.Gas);
        var captures = new NavigationGridBaselineCapture[1];
        captures[0] = new NavigationGridBaselineCapture(
            instance,
            NavigationSurfaceComponentKeySet.Empty.Add(state),
            defaultPhysicalAddressSetChanged: false,
            addressCount: 1,
            highWaterSequence: 1,
            worldSpawnToken: 1,
            gridIndex: 0,
            gridSpawnToken: 1,
            gridHighWaterSequence: 1,
            instance.Map.GridBinding.Key);
        var work = new NavigationMaterializedComponentWork(
            candidate,
            NavigationSurfaceComponentKeySet.Empty,
            NavigationSurfaceComponentKeySet.Empty,
            NavigationCellAddressSet.Empty,
            affectedMemberCount: 0,
            world,
            captures,
            new[] { 0 },
            affectedMapCount: 1,
            events: Array.Empty<GridEventInfo>(),
            eventCount: 0);
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(
            maxConsumedEnvelopes: 1,
            maxBaselineAddresses: 1,
            maxOverlaySlots: 1,
            maxComponentNodes: 1,
            maxSeamCandidateProbes: 1,
            maxExplicitEdges: 1,
            maxDependencyEntries: 1,
            maxSurfaceComponentEdges: 1));

        work.Advance(meter).Should().BeFalse();
        meter.ComponentNodes.Should().Be(1,
            "the affected-map inspection is part of resumable component work");
        meter.DependencyEntries.Should().Be(1,
            "the exact structural state is consumed directly and charged once");
    }

    [Theory]
    [InlineData(HexOrientation.PointyTop)]
    [InlineData(HexOrientation.FlatTop)]
    public void HexVolumeComponents_ShouldUseSixPlanarAndTwoVerticalFaces(
        HexOrientation orientation)
    {
        GridConfiguration configuration = new(
            new Vector3d(-8, 0, -8),
            new Vector3d(8, 2, 8),
            topologyKind: GridTopologyKind.HexPrism,
            topologyMetrics: GridTopologyMetrics.Hex(Fixed64.One, Fixed64.One, orientation),
            storageKind: GridStorageKind.Sparse);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        VoxelIndex center = FindHexCenter(binding);
        var cells = new VoxelIndex[HexDirectionUtility.Primary.Length + 1];
        cells[0] = center;
        var builder = new NavigationMapBuilder("map", binding).AddCell(center, GasCell);
        for (int i = 0; i < HexDirectionUtility.Primary.Length; i++)
        {
            VoxelIndex offset = HexDirectionUtility.GetOffset(HexDirectionUtility.Primary[i]);
            cells[i + 1] = Add(center, offset);
            builder.AddCell(cells[i + 1], GasCell);
        }
        using var world = new GridWorld();
        world.TryAddGrid(configuration, cells, out _).Should().BeTrue();
        NavigationWorldGraph graph = BuildComponentGraph(world, builder.Build());
        var centerAddress = new NavigationCellAddress("map", center);

        for (int i = 1; i < cells.Length; i++)
        {
            graph.AreInSameSurfaceComponent(
                    centerAddress,
                    TraversalMedium.Gas,
                    new NavigationCellAddress("map", cells[i]),
                    TraversalMedium.Gas)
                .Should().BeTrue($"primary hex direction {i - 1} shares a positive face");
        }

        VoxelIndex diagonal = Add(
            center,
            HexDirectionUtility.GetOffset(HexDirection.AboveQPositive));
        using var diagonalWorld = new GridWorld();
        diagonalWorld.TryAddGrid(configuration, new[] { center, diagonal }, out _)
            .Should().BeTrue();
        NavigationMap diagonalMap = new NavigationMapBuilder("map", binding)
            .AddCell(center, GasCell)
            .AddCell(diagonal, GasCell)
            .Build();
        NavigationWorldGraph diagonalGraph = BuildComponentGraph(diagonalWorld, diagonalMap);

        diagonalGraph.AreInSameSurfaceComponent(
                centerAddress,
                TraversalMedium.Gas,
                new NavigationCellAddress("map", diagonal),
                TraversalMedium.Gas)
            .Should().BeFalse("a vertical-planar diagonal is not one of the eight faces");
    }

    [Fact]
    public void CenteredVolumeFootAnchor_ShouldUseCheckedPrismCenterMathAcrossTopologies()
    {
        GridConfiguration[] configurations =
        {
            new(
                new Vector3d(-4, -4, -4),
                new Vector3d(4, 4, 4),
                topologyMetrics: GridTopologyMetrics.Rectangular(
                    (Fixed64)2,
                    (Fixed64)3,
                    (Fixed64)5)),
            new(
                new Vector3d(-4, -4, -4),
                new Vector3d(4, 4, 4),
                topologyKind: GridTopologyKind.HexPrism,
                topologyMetrics: GridTopologyMetrics.Hex(
                    (Fixed64)2,
                    (Fixed64)3,
                    HexOrientation.PointyTop)),
            new(
                new Vector3d(-4, -4, -4),
                new Vector3d(4, 4, 4),
                topologyKind: GridTopologyKind.HexPrism,
                topologyMetrics: GridTopologyMetrics.Hex(
                    (Fixed64)2,
                    (Fixed64)3,
                    HexOrientation.FlatTop))
        };
        Fixed64[] heights = { Fixed64.Half, (Fixed64)3, (Fixed64)7 };

        for (int configurationIndex = 0;
             configurationIndex < configurations.Length;
             configurationIndex++)
        {
            GridConfiguration configuration = configurations[configurationIndex];
            configuration.TryNormalize(out NormalizedGridConfiguration binding)
                .Should().BeTrue();
            using var world = new GridWorld();
            world.TryAddGrid(configuration, out _).Should().BeTrue();
            NavigationWorldGraph graph = BuildComponentGraph(
                world,
                new NavigationMapBuilder("map", binding)
                    .AddCell(default, GasCell)
                    .Build());
            graph.TryGetNodeRef(new NavigationCellAddress("map", default), out NavigationNodeRef node)
                .Should().BeTrue();
            graph.TryGetNodeState(node, out NavigationNodeState state).Should().BeTrue();

            for (int heightIndex = 0; heightIndex < heights.Length; heightIndex++)
            {
                Fixed64 height = heights[heightIndex];
                Fixed64.TryMultiplyAdd(height, -Fixed64.Half, state.Center.Y, out Fixed64 expectedY)
                    .Should().BeTrue();
                state.TryGetCenteredVolumeFootAnchor(height, out Vector3d anchor)
                    .Should().BeTrue();
                anchor.Should().Be(new Vector3d(state.Center.X, expectedY, state.Center.Z));
            }
        }

        var unrepresentable = new NavigationNodeState(
            GasCell,
            isPresent: true,
            obstacleCount: 0,
            new Vector3d(Fixed64.Zero, Fixed64.MinValue, Fixed64.Zero),
            Vector3d.Zero);
        unrepresentable.TryGetCenteredVolumeFootAnchor(Fixed64.Zero, out _)
            .Should().BeFalse();
        unrepresentable.TryGetCenteredVolumeFootAnchor(-Fixed64.One, out _)
            .Should().BeFalse();
        unrepresentable.TryGetCenteredVolumeFootAnchor(Fixed64.MaxValue, out _)
            .Should().BeFalse();
    }

    private static NavigationCell Cell(TraversalMedia media) => new(
        media,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        Fixed64.Zero,
        Fixed64.One);

    private static NavigationMap BuildDefaultReplacementMap(
        NormalizedGridConfiguration binding,
        NavigationCell defaultCell)
    {
        var builder = new NavigationMapBuilder("map", binding).SetDefaultCell(defaultCell);
        for (int x = 0; x < 64; x++)
            builder.AddCell(new VoxelIndex(x, 0, 0), Cell(TraversalMedia.Solid));
        return builder.Build();
    }

    private static TrailblazerWorldContextSettings CreateSettings(
        MaintenanceWorkBudget maintenanceBudget,
        int? maximumDynamicSlotsPerMap = null,
        int? maximumDynamicSlots = null,
        NavigationOperationLimits? operationLimits = null,
        long? maximumActiveBytes = null)
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        return new TrailblazerWorldContextSettings(
            operationLimits ?? defaults.OperationLimits,
            maintenanceBudget,
            defaults.GuideSampleBudget,
            defaults.MaxIngressEntries,
            defaults.MaxIngressBytes,
            defaults.MaxActiveSnapshots,
            maximumActiveBytes ?? defaults.MaxActiveSnapshotBytes,
            defaults.MaxRetiredSnapshots,
            defaults.MaxRetiredSnapshotBytes,
            defaults.MaxPersistentGraphPages,
            maximumDynamicSlotsPerMap ?? defaults.MaxDynamicCellSlotsPerMap,
            maximumDynamicSlots ?? defaults.MaxDynamicCellSlots,
            defaults.NavigationAreaCount,
            defaults.MaxAreaPolicies,
            defaults.MaxAreaRulesPerPolicy,
            defaults.MaxAreaRules,
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits);
    }

    private static NavigationOperationLimits CreateOperationLimits(
        int maximumOverlayCellsPerMap,
        int maximumOverlayCells) => new(
        maxPendingOperations: 8,
        maxPendingDescriptorBytes: 1_000_000,
        maxPreparedMapBytes: 1_000_000,
        maxBatchItems: 8,
        maxBatchDescriptorBytes: 1_000_000,
        maxBatchSortScratchBytes: 1_000_000,
        maxCorridorCells: 8,
        maxMaps: 4,
        maxRetainedMapIdentities: 8,
        maxOverlayCellsPerMap: maximumOverlayCellsPerMap,
        maxOverlayConnectionsPerMap: 1,
        maxOverlayTransitionsPerMap: 1,
        maxOverlayCells: maximumOverlayCells,
        maxOverlayConnections: 1,
        maxOverlayTransitions: 1,
        maxTransitionRulesPerMap: 1,
        maxTransitionRules: 1);

    private static GridConfiguration RectangularConfiguration(GridStorageKind storage) => new(
        Vector3d.Zero,
        new Vector3d(1, 0, 0),
        topologyKind: GridTopologyKind.RectangularPrism,
        topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
        storageKind: storage);

    private static NavigationMapInstance Compose(GridWorld world, NavigationMap map)
    {
        var prepared = new PreparedNavigationMap(map, bakeVersion: 1);
        var state = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        return NavigationMapInstanceTestFactory.Compose(
            world,
            state,
            previous: null,
            instanceVersion: 1);
    }

    private static NavigationWorldGraph BuildComponentGraph(GridWorld world, NavigationMap map)
    {
        NavigationMapInstance instance = Compose(world, map);
        var graph = new NavigationWorldGraph(1, new[] { instance });
        NavigationSurfaceComponentIndex components =
            NavigationSurfaceComponentTestFactory.Build(graph);
        graph = new NavigationWorldGraph(1, new[] { instance }, surfaceComponents: components);
        if (map.TransitionSpan.Length == 0 && map.TransitionRuleSpan.Length == 0)
            return graph;
        var work = new NavigationTransitionRefreshWork(
            NavigationWorldGraph.Empty,
            graph,
            operationCandidate: null,
            PersistentStringMap<bool>.Empty.Set(map.MapId, true),
            rebuildRules: true,
            version: 1);
        var meter = new MaintenanceWorkMeter(
            TrailblazerWorldContextSettings.Default.MaintenanceBudget);
        for (int frame = 0; frame < 4096; frame++)
        {
            if (work.Advance(meter))
                return graph.WithTransitionPublication(work.Pages, work.Rules);
            meter.Reset();
        }
        throw new Xunit.Sdk.XunitException("Expected transition publication to complete.");
    }

    private static NavigationMapInstance ResnapshotDefault(
        GridWorld world,
        NavigationMapInstance source,
        long instanceVersion)
    {
        int capacity = source.Map.GridBinding.AddressCount;
        var addresses = new VoxelIndex[capacity];
        var covered = new GridCoveredAddress[capacity];
        var rebuild = new NavigationBaselineRebuild(source);
        for (int frame = 0; frame < 64; frame++)
        {
            rebuild.Advance(
                world,
                source,
                capacity,
                long.MaxValue,
                int.MaxValue,
                addresses,
                covered,
                out NavigationGridBaselineCapture capture,
                out bool complete);
            if (complete)
                return source.Materialize(capture, instanceVersion);
        }
        throw new Xunit.Sdk.XunitException("Expected default resnapshot to complete.");
    }

    private static NavigationGraphRuntime CreateMaterializedCapacityScenario(
        GridWorld world,
        long? maximumActiveBytes,
        out NavigationMapCommitOperation replacement)
    {
        MaintenanceWorkBudget defaults = TrailblazerWorldContextSettings.Default
            .MaintenanceBudget;
        var budget = new MaintenanceWorkBudget(
            defaults.MaxConsumedEnvelopes,
            defaults.MaxBaselineAddresses,
            maxOverlaySlots: 1,
            maxComponentNodes: 1,
            maxSeamCandidateProbes: 1,
            maxExplicitEdges: 1,
            maxDependencyEntries: defaults.MaxDependencyEntries,
            maxSurfaceComponentEdges: 1);
        var runtime = new NavigationGraphRuntime(
            world,
            CreateSettings(budget, maximumActiveBytes: maximumActiveBytes));
        try
        {
            GridConfiguration configuration = new(
                Vector3d.Zero,
                new Vector3d(63, 0, 0),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
                storageKind: GridStorageKind.Dense);
            world.TryAddGrid(configuration, out _).Should().BeTrue();
            configuration.TryNormalize(out NormalizedGridConfiguration binding)
                .Should().BeTrue();
            var sourceBuilder = new NavigationMapBuilder("map", binding);
            var replacementBuilder = new NavigationMapBuilder("map", binding);
            for (int x = 0; x < 64; x++)
            {
                var index = new VoxelIndex(x, 0, 0);
                sourceBuilder.AddCell(index, GasCell);
                replacementBuilder.AddCell(
                    index,
                    x == 32 ? Cell(TraversalMedia.Liquid) : GasCell);
            }
            sourceBuilder.AddTransition(new TraversalTransitionDefinition(
                "capacity-transition",
                TraversalTransitionType.Jump,
                new VoxelIndex(0, 0, 0),
                TraversalMedium.Gas,
                new NavigationCellAddress("map", new VoxelIndex(1, 0, 0)),
                TraversalMedium.Gas));
            replacementBuilder.AddTransition(new TraversalTransitionDefinition(
                "capacity-transition",
                TraversalTransitionType.Jump,
                new VoxelIndex(0, 0, 0),
                TraversalMedium.Gas,
                new NavigationCellAddress("map", new VoxelIndex(2, 0, 0)),
                TraversalMedium.Gas));
            NavigationWorldGraph source = BuildComponentGraph(world, sourceBuilder.Build());
            runtime.Store.TryPublish(source).Should().Be(NavigationCandidatePublication.Published);
            replacement = new NavigationMapCommitOperation(
                new PreparedNavigationMap(replacementBuilder.Build(), 2),
                OverlayReplacementPolicy.Clear,
                operationSequence: 2,
                effectiveFrame: 1);
            runtime.Admit(replacement).Should().BeTrue();
            return runtime;
        }
        catch
        {
            runtime.Dispose();
            throw;
        }
    }

    private static void SimulateUntilTerminal(
        NavigationGraphRuntime runtime,
        NavigationMapCommitOperation operation)
    {
        for (int frame = 0;
             frame < 2048 && operation.Receipt.Status == NavigationOperationStatus.Pending;
             frame++)
        {
            runtime.Maintain(frame + 1);
        }
    }

    private static bool HasPendingGridChangeIngress(NavigationGraphRuntime runtime)
    {
        object ingress = typeof(NavigationGraphRuntime).GetField(
                "_ingress",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(runtime)!;
        System.Type type = ingress.GetType();
        int count = (int)type.GetField(
                "_count",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(ingress)!;
        bool overflowed = (bool)type.GetField(
                "_overflowed",
                System.Reflection.BindingFlags.Instance
                | System.Reflection.BindingFlags.NonPublic)!
            .GetValue(ingress)!;
        return count != 0 || overflowed;
    }

    private static bool HasMediumState(
        TrailblazerWorldContext context,
        NavigationCellAddress address,
        TraversalMedium medium)
    {
        using NavigationWorldGraphLease? lease = context.Pathing.TryAcquireNavigationGraph();
        return lease != null && lease.Graph.TryGetMediumStateRef(address, medium, out _);
    }

    private static bool HasNode(
        TrailblazerWorldContext context,
        NavigationCellAddress address)
    {
        using NavigationWorldGraphLease? lease = context.Pathing.TryAcquireNavigationGraph();
        return lease != null && lease.Graph.TryGetNodeRef(address, out _);
    }

    private static bool HasMediumComponent(
        TrailblazerWorldContext context,
        NavigationCellAddress address,
        TraversalMedium medium,
        out NavigationSurfaceComponentKey key,
        out long version)
    {
        using NavigationWorldGraphLease? lease = context.Pathing.TryAcquireNavigationGraph();
        if (lease != null
            && lease.Graph.TryGetSurfaceComponent(address, medium, out key, out version))
        {
            return true;
        }
        key = default;
        version = 0;
        return false;
    }

    private static NavigationMapCommitOperation AdmitMap(
        TrailblazerWorldContext context,
        NavigationMap map,
        long sequence)
    {
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, sequence),
            OverlayReplacementPolicy.Clear,
            sequence,
            context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        return operation;
    }

    private static bool AreSameComponent(
        TrailblazerWorldContext context,
        NavigationCellAddress first,
        NavigationCellAddress second,
        TraversalMedium medium)
    {
        using NavigationWorldGraphLease? lease = context.Pathing.TryAcquireNavigationGraph();
        return lease != null && lease.Graph.AreInSameSurfaceComponent(
            first,
            medium,
            second,
            medium);
    }

    private static Vector3d GetFoot(
        NormalizedGridConfiguration binding,
        VoxelIndex index)
    {
        binding.TryGetCellPrism(index, out GridCellPrism prism).Should().BeTrue();
        return new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z);
    }

    private static void SimulateUntil(
        TrailblazerWorldContext context,
        System.Func<bool> predicate,
        int maximumFrames = 64)
    {
        for (int frame = 0; frame < maximumFrames && !predicate(); frame++)
            context.Simulate();
        predicate().Should().BeTrue("maintenance should converge within the bounded frame limit");
    }

    private static VoxelIndex FindHexCenter(NormalizedGridConfiguration binding)
    {
        for (int q = 1; q < binding.Width - 1; q++)
        {
            for (int y = 1; y < binding.Height - 1; y++)
            {
                for (int r = 1; r < binding.Length - 1; r++)
                {
                    VoxelIndex candidate = new(q, y, r);
                    bool valid = true;
                    for (int i = 0; i < HexDirectionUtility.Primary.Length; i++)
                    {
                        valid &= binding.IsValidIndex(Add(
                            candidate,
                            HexDirectionUtility.GetOffset(HexDirectionUtility.Primary[i])));
                    }
                    if (valid)
                        return candidate;
                }
            }
        }
        throw new System.InvalidOperationException("No interior hex prism was available.");
    }

    private static VoxelIndex Add(VoxelIndex left, VoxelIndex right) => new(
        left.x + right.x,
        left.y + right.y,
        left.z + right.z);
}

using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing.Graph;

public sealed class NavigationSurfaceComponentTests
{
    private static readonly NavigationCell SolidCell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        Fixed64.Zero,
        Fixed64.One);

    [Fact]
    public void RetainedBytes_ShouldMatchEmptyAndSingletonPersistentLayouts()
    {
        using var world = new GridWorld();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(default, SolidCell)
            .Build();
        NavigationWorldGraph graph = new(
            1,
            new[] { Compose(world, map) });

        NavigationSurfaceComponentIndex.Empty.RetainedBytes.Should().Be(128L);
        NavigationSurfaceComponentIndex.Empty.PersistentPageCount.Should().Be(2);

        NavigationSurfaceComponentIndex singleton =
            NavigationSurfaceComponentTestFactory.Build(graph);
        singleton.RetainedBytes.Should().Be(776L);
        singleton.PersistentPageCount.Should().Be(10);
    }

    [Fact]
    public void DisconnectedIslandsInOneMap_ShouldPublishDistinctExactComponents()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("islands", binding)
            .AddCell(new VoxelIndex(0, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(2, 0, 0), SolidCell)
            .Build();
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        SimulateUntilTerminal(context, operation.Receipt);

        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var left = new NavigationCellAddress("islands", new VoxelIndex(0, 0, 0));
        var right = new NavigationCellAddress("islands", new VoxelIndex(2, 0, 0));

        lease.Graph.TryGetSurfaceComponent(left, out NavigationSurfaceComponentKey leftKey, out _)
            .Should().BeTrue();
        lease.Graph.TryGetSurfaceComponent(right, out NavigationSurfaceComponentKey rightKey, out _)
            .Should().BeTrue();
        leftKey.Should().NotBe(rightKey,
            "missing native cells split weak structural connectivity inside one map");
        lease.Graph.AreInSameSurfaceComponent(left, right).Should().BeFalse();
    }

    [Fact]
    public void ExactClosure_ShouldLeaveUnrelatedSameMapDependencyCurrent()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("islands", binding)
            .AddCell(new VoxelIndex(0, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(2, 0, 0), SolidCell)
            .Build();
        AdmitMaps(context, map);
        var left = new NavigationCellAddress("islands", new VoxelIndex(0, 0, 0));
        var right = new NavigationCellAddress("islands", new VoxelIndex(2, 0, 0));

        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetSurfaceComponent(left, out NavigationSurfaceComponentKey leftKey, out _)
            .Should().BeTrue();
        lease.Graph.TryGetSurfaceComponent(right, out NavigationSurfaceComponentKey rightKey, out _)
            .Should().BeTrue();
        NavigationWorldGraph closed = lease.Graph.WithClosedStructuralComponents(
            NavigationSurfaceComponentKeySet.Empty.Add(leftKey),
            false,
            lease.Graph.GraphVersion + 1);

        closed.TryGetComponentDependency(leftKey, out _).Should().BeFalse();
        closed.TryGetComponentDependency(rightKey, out _).Should().BeTrue();
        closed.IsSurfaceAddressClosed(right).Should().BeFalse(
            "an exact closure must not degrade to its representative MapId");
    }

    [Fact]
    public void OneWayExplicitEdge_ShouldJoinBothDirectionsOfWeakMembership()
    {
        using var world = new GridWorld();
        var sourceConfiguration = new GridConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        var destinationConfiguration = new GridConfiguration(
            new Vector3d(10, 0, 0),
            new Vector3d(10, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        world.TryAddGrid(sourceConfiguration, out _).Should().BeTrue();
        world.TryAddGrid(destinationConfiguration, out _).Should().BeTrue();
        sourceConfiguration.TryNormalize(out NormalizedGridConfiguration sourceBinding)
            .Should().BeTrue();
        destinationConfiguration.TryNormalize(out NormalizedGridConfiguration destinationBinding)
            .Should().BeTrue();
        var source = new NavigationCellAddress("source", default);
        var destination = new NavigationCellAddress("destination", default);
        var definition = new NavigationConnection(
            "one-way",
            default,
            destination,
            GetFoot(sourceBinding, default),
            GetFoot(destinationBinding, default),
            Fixed64.Zero,
            Fixed64.One);
        NavigationMap sourceMap = new NavigationMapBuilder("source", sourceBinding)
            .AddCell(default, SolidCell)
            .Build();
        NavigationMap destinationMap = new NavigationMapBuilder("destination", destinationBinding)
            .AddCell(default, SolidCell)
            .Build();
        NavigationMapInstance sourceInstance = Compose(world, sourceMap);
        NavigationMapInstance destinationInstance = Compose(world, destinationMap);
        var record = new NavigationExplicitConnectionRecord(
            new NavigationConnectionOwnerKey("source", definition.Id),
            definition,
            isActive: true,
            corridorCost: Fixed64.One,
            NavigationPagedSequence<GridNavigationPortal>.Empty);
        NavigationExplicitConnectionIndex connections =
            NavigationExplicitConnectionIndex.Empty.SetOwner(record, out _);
        var rowBuilder = new NavigationPagedSequence<NavigationConnectionOwnerKey>.Builder(16);
        rowBuilder.Append(record.Owner);
        NavigationPagedSequence<NavigationConnectionOwnerKey> row = rowBuilder.Seal();
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
        NavigationSurfaceComponentIndex components =
            NavigationSurfaceComponentTestFactory.Build(baseGraph);
        var graph = new NavigationWorldGraph(
            1,
            new[] { destinationInstance, sourceInstance },
            explicitConnections: connections,
            surfaceComponents: components);

        graph.AreInSameSurfaceComponent(source, destination).Should().BeTrue(
            "component membership treats the directed connection as an undirected structural link");
        graph.EnumerateSurfaceEdges(Resolve(graph, destination))
            .MoveNext().Should().BeFalse(
                "weak membership must not invent a reverse traversal edge");
    }

    [Fact]
    public void ArticulationSuppression_ShouldSplitAffectedComponentAndReuseUnrelatedRecord()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(6, 0, 0),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(new VoxelIndex(0, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(1, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(2, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(5, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(6, 0, 0), SolidCell)
            .Build();
        AdmitMaps(context, map);
        var left = new NavigationCellAddress("map", new VoxelIndex(0, 0, 0));
        var middle = new NavigationCellAddress("map", new VoxelIndex(1, 0, 0));
        var right = new NavigationCellAddress("map", new VoxelIndex(2, 0, 0));
        var unrelated = new NavigationCellAddress("map", new VoxelIndex(5, 0, 0));
        NavigationSurfaceComponent priorUnrelated;
        using (NavigationWorldGraphLease before = context.Pathing.TryAcquireNavigationGraph()!)
        {
            before.Graph.AreInSameSurfaceComponent(left, right).Should().BeTrue();
            before.Graph.SurfaceComponents.TryGet(unrelated, out priorUnrelated!).Should().BeTrue();
        }

        var suppress = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta("map", new[]
                {
                    NavigationCellOverlayOperation.Suppress(middle.Index)
                })
            })),
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(suppress).Should().BeTrue();
        SimulateUntilTerminal(context, suppress.Receipt);

        using NavigationWorldGraphLease after = context.Pathing.TryAcquireNavigationGraph()!;
        after.Graph.AreInSameSurfaceComponent(left, right).Should().BeFalse();
        after.Graph.TryGetSurfaceComponent(middle, out _, out _).Should().BeFalse();
        after.Graph.SurfaceComponents.TryGet(unrelated, out NavigationSurfaceComponent nextUnrelated)
            .Should().BeTrue();
        nextUnrelated.Should().BeSameAs(priorUnrelated,
            "a structural mutation must path-copy only the affected old component");
    }

    [Fact]
    public void MultiFrameSplit_ShouldCloseOnlyAffectedExactComponentUntilAtomicPublication()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateOneEdgeBudgetSettings());
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(6, 0, 0),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(new VoxelIndex(0, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(1, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(2, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(6, 0, 0), SolidCell)
            .Build();
        AdmitMaps(context, map);
        var left = new NavigationCellAddress("map", new VoxelIndex(0, 0, 0));
        var middle = new NavigationCellAddress("map", new VoxelIndex(1, 0, 0));
        var right = new NavigationCellAddress("map", new VoxelIndex(2, 0, 0));
        var unrelated = new NavigationCellAddress("map", new VoxelIndex(6, 0, 0));
        var suppress = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta("map", new[]
                {
                    NavigationCellOverlayOperation.Suppress(middle.Index)
                })
            })),
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(suppress).Should().BeTrue();

        for (int frame = 0;
             frame < 16 && context.Pathing.RetainedCompositionWorkCount == 0;
             frame++)
        {
            context.Simulate();
        }

        context.Pathing.RetainedCompositionWorkCount.Should().Be(1,
            $"receipt={suppress.Receipt.Status}, rejection={suppress.Receipt.Rejection}");
        long closureVersion;
        using (NavigationWorldGraphLease pending = context.Pathing.TryAcquireNavigationGraph()!)
        {
            closureVersion = pending.Graph.GraphVersion;
            pending.Graph.IsSurfaceAddressClosed(left).Should().BeTrue();
            pending.Graph.IsSurfaceAddressClosed(right).Should().BeTrue();
            pending.Graph.IsSurfaceAddressClosed(unrelated).Should().BeFalse();
            pending.Graph.AreInSameSurfaceComponent(left, right).Should().BeTrue(
                "the old exact index remains atomic while its component is closed");
        }

        SimulateUntilTerminal(context, suppress.Receipt);
        for (int frame = 0;
             frame < 512 && context.Pathing.RetainedCompositionWorkCount != 0;
             frame++)
        {
            context.Simulate();
        }
        context.Pathing.RetainedCompositionWorkCount.Should().Be(0);
        using NavigationWorldGraphLease published = context.Pathing.TryAcquireNavigationGraph()!;
        published.Graph.HasClosedStructuralScope.Should().BeFalse();
        published.Graph.GraphVersion.Should().BeGreaterThan(closureVersion,
            "the atomic result must publish after every intermediate closure generation");
        published.Graph.AreInSameSurfaceComponent(left, right).Should().BeFalse();
        published.Graph.IsSurfaceAddressClosed(unrelated).Should().BeFalse();
    }

    [Fact]
    public void ExactClosure_ShouldRetainPreexistingClosureRootWithoutUnionCopy()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateOneEdgeBudgetSettings());
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(4, 0, 0),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(new VoxelIndex(0, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(1, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(4, 0, 0), SolidCell)
            .Build();
        AdmitMaps(context, map);
        var affected = new NavigationCellAddress("map", default);
        var suppressed = new NavigationCellAddress("map", new VoxelIndex(1, 0, 0));
        var preclosed = new NavigationCellAddress("map", new VoxelIndex(4, 0, 0));
        NavigationSurfaceComponentKeySet baseline;
        NavigationWorldGraph seeded;
        using (NavigationWorldGraphLease prior = context.Pathing.TryAcquireNavigationGraph()!)
        {
            prior.Graph.TryGetSurfaceComponent(
                    preclosed,
                    out NavigationSurfaceComponentKey preclosedKey,
                    out _)
                .Should().BeTrue();
            baseline = NavigationSurfaceComponentKeySet.Empty.Add(preclosedKey);
            seeded = prior.Graph.WithClosedStructuralComponents(
                baseline,
                false,
                prior.Graph.GraphVersion + 1);
        }
        context.Pathing.NavigationGraphStore.TryPublish(seeded)
            .Should().Be(NavigationCandidatePublication.Published);
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta("map", new[]
                {
                    NavigationCellOverlayOperation.Suppress(suppressed.Index)
                })
            })),
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();

        bool observedExactClosure = false;
        for (int frame = 0; frame < 128 && operation.Receipt.Status == NavigationOperationStatus.Pending; frame++)
        {
            context.Simulate();
            using NavigationWorldGraphLease pending = context.Pathing.TryAcquireNavigationGraph()!;
            if (pending.Graph.AreAllStructuralComponentsClosed
                || !pending.Graph.IsSurfaceAddressClosed(affected))
            {
                continue;
            }
            observedExactClosure = true;
            pending.Graph.ClosedStructuralComponents.Should().BeSameAs(baseline,
                "the preexisting owner root must not be copied into this operation's closure");
            pending.Graph.IsSurfaceAddressClosed(preclosed).Should().BeTrue();
            break;
        }

        observedExactClosure.Should().BeTrue();
        SimulateUntilTerminal(context, operation.Receipt);
        using NavigationWorldGraphLease published = context.Pathing.TryAcquireNavigationGraph()!;
        published.Graph.IsSurfaceAddressClosed(preclosed).Should().BeTrue(
            "publication must restore the preexisting closure owner");
        published.Graph.IsSurfaceAddressClosed(affected).Should().BeFalse();
    }

    [Fact]
    public void AffectedSplit_ShouldMeterOnlyAffectedDomainAndReuseUnrelatedRecord()
    {
        using var world = new GridWorld();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(11, 0, 0),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap sourceMap = new NavigationMapBuilder("map", binding)
            .AddCell(new VoxelIndex(0, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(1, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(2, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(10, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(11, 0, 0), SolidCell)
            .Build();
        NavigationWorldGraph sourceWithoutComponents = new(
            1,
            new[] { Compose(world, sourceMap) });
        NavigationWorldGraph source = new(
            1,
            new[] { Compose(world, sourceMap) },
            surfaceComponents: NavigationSurfaceComponentTestFactory.Build(
                sourceWithoutComponents));
        NavigationMap nextMap = new NavigationMapBuilder("map", binding)
            .AddCell(new VoxelIndex(0, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(2, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(10, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(11, 0, 0), SolidCell)
            .Build();
        NavigationWorldGraph next = new(
            2,
            new[] { Compose(world, nextMap) },
            surfaceComponents: source.SurfaceComponents);
        var affectedAddress = new NavigationCellAddress("map", new VoxelIndex(0, 0, 0));
        var removedAddress = new NavigationCellAddress("map", new VoxelIndex(1, 0, 0));
        var unrelatedAddress = new NavigationCellAddress("map", new VoxelIndex(10, 0, 0));
        source.SurfaceComponents.TryGet(affectedAddress, out NavigationSurfaceComponent affected)
            .Should().BeTrue();
        source.SurfaceComponents.TryGet(unrelatedAddress, out NavigationSurfaceComponent unrelated)
            .Should().BeTrue();
        NavigationSurfaceComponentKeySet affectedKeys =
            NavigationSurfaceComponentKeySet.Empty.Add(affected.Key);
        NavigationCellAddressSet seeds = NavigationCellAddressSet.Empty
            .Add(removedAddress);
        var work = new NavigationSurfaceComponentBuildWork(
            next,
            source,
            affectedKeys,
            seeds,
            checked(affected.Members.Count + seeds.Count));
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue));

        work.Advance(meter).Should().BeTrue();

        meter.ComponentNodes.Should().Be(2,
            "the unrelated two-node island is outside the affected domain");
        work.Result.TryGet(unrelatedAddress, out NavigationSurfaceComponent reused)
            .Should().BeTrue();
        reused.Should().BeSameAs(unrelated);
        work.Result.TryGet(removedAddress, out _).Should().BeFalse();
        work.Result.TryGet(affectedAddress, out NavigationSurfaceComponent split)
            .Should().BeTrue();
        split.Should().NotBeSameAs(affected);
    }

    [Fact]
    public void ComponentSeal_ShouldDebitRecordAndEveryMembershipAcrossFrames()
    {
        using var world = new GridWorld();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(new VoxelIndex(0, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(1, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(2, 0, 0), SolidCell)
            .Build();
        NavigationWorldGraph graph = new(1, new[] { Compose(world, map) });
        NavigationSurfaceComponentBuildWork work =
            NavigationSurfaceComponentTestFactory.CreateBuildWork(graph);
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            int.MaxValue,
            maxDependencyEntries: 1,
            maxSurfaceComponentEdges: int.MaxValue));

        work.Advance(meter).Should().BeFalse(
            "sealing four persistent records must not escape a one-entry frame budget");
        meter.DependencyEntries.Should().Be(1);
        int frames = 1;
        while (!work.IsComplete && frames < 8)
        {
            meter.Reset();
            work.Advance(meter);
            meter.DependencyEntries.Should().BeLessThanOrEqualTo(1);
            frames++;
        }

        work.IsComplete.Should().BeTrue();
        frames.Should().BeGreaterThanOrEqualTo(4);
        work.Result.TryGet(new NavigationCellAddress("map", default), out _)
            .Should().BeTrue();
    }

    [Fact]
    public void NativeInsertion_ShouldMergeBothIncidentOldComponents()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One));
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(new VoxelIndex(0, 0, 0), SolidCell)
            .AddCell(new VoxelIndex(2, 0, 0), SolidCell)
            .Build();
        AdmitMaps(context, map);
        var left = new NavigationCellAddress("map", new VoxelIndex(0, 0, 0));
        var middle = new NavigationCellAddress("map", new VoxelIndex(1, 0, 0));
        var right = new NavigationCellAddress("map", new VoxelIndex(2, 0, 0));
        using (NavigationWorldGraphLease before = context.Pathing.TryAcquireNavigationGraph()!)
            before.Graph.AreInSameSurfaceComponent(left, right).Should().BeFalse();
        var merge = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta(
                    "map",
                    new[] { NavigationCellOverlayOperation.Set(middle.Index, SolidCell) })
            })),
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(merge).Should().BeTrue();
        SimulateUntilTerminal(context, merge.Receipt);

        using NavigationWorldGraphLease published = context.Pathing.TryAcquireNavigationGraph()!;
        published.Graph.AreInSameSurfaceComponent(left, right).Should().BeTrue();
        published.Graph.TryGetSurfaceComponent(middle, out _, out _).Should().BeTrue();
    }

    private static NavigationNodeRef Resolve(
        NavigationWorldGraph graph,
        NavigationCellAddress address)
    {
        graph.TryGetNodeRef(address, out NavigationNodeRef node).Should().BeTrue();
        return node;
    }

    private static Vector3d GetFoot(
        NormalizedGridConfiguration binding,
        VoxelIndex index)
    {
        binding.TryGetCellPrism(index, out GridCellPrism prism).Should().BeTrue();
        return new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z);
    }

    private static NavigationMapInstance Compose(GridWorld world, NavigationMap map)
    {
        var prepared = new PreparedNavigationMap(map, 1);
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

    private static void AdmitMaps(
        TrailblazerWorldContext context,
        params NavigationMap[] maps)
    {
        NavigationOperationReceipt? last = null;
        for (int i = 0; i < maps.Length; i++)
        {
            var operation = new NavigationMapCommitOperation(
                new PreparedNavigationMap(maps[i], 1),
                OverlayReplacementPolicy.Clear,
                operationSequence: i + 1,
                effectiveFrame: context.FrameCount + 1);
            context.Pathing.Admit(operation).Should().BeTrue();
            last = operation.Receipt;
        }
        SimulateUntilTerminal(context, last!);
    }

    private static void SimulateUntilTerminal(
        TrailblazerWorldContext context,
        NavigationOperationReceipt receipt,
        int maximumFrames = 512)
    {
        for (int frame = 0;
             frame < maximumFrames && receipt.Status == NavigationOperationStatus.Pending;
             frame++)
        {
            context.Simulate();
        }
        receipt.Status.Should().Be(
            NavigationOperationStatus.Applied,
            $"rejection={receipt.Rejection}");
    }

    private static TrailblazerWorldContextSettings CreateOneEdgeBudgetSettings()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        MaintenanceWorkBudget budget = defaults.MaintenanceBudget;
        return new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            new MaintenanceWorkBudget(
                budget.MaxConsumedEnvelopes,
                budget.MaxBaselineAddresses,
                budget.MaxOverlaySlots,
                budget.MaxComponentNodes,
                budget.MaxSeamCandidateProbes,
                budget.MaxExplicitEdges,
                budget.MaxDependencyEntries,
                maxSurfaceComponentEdges: 1),
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
            defaults.QueryLimits);
    }
}

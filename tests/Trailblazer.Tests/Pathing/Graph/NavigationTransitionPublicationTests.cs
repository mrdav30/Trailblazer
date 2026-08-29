using System;
using System.Runtime.CompilerServices;
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
public sealed class NavigationTransitionPublicationTests
{
    [Fact]
    public void TransitionRecordAccounting_ShouldUseCompiledStructLayouts()
    {
        NavigationTransitionPage.OutgoingRecordBytes.Should().Be(
            Unsafe.SizeOf<NavigationPublishedTransition>());
        NavigationTransitionPage.IncomingRecordBytes.Should().Be(
            Unsafe.SizeOf<NavigationIncomingTransitionRef>());
        NavigationTransitionRuleTable.RecordRetainedBytes.Should().Be(
            Unsafe.SizeOf<TraversalTransitionRule>());
        var work = new NavigationTransitionRefreshWork(
            NavigationWorldGraph.Empty,
            NavigationWorldGraph.Empty,
            operationCandidate: null,
            PersistentStringMap<bool>.Empty,
            rebuildRules: false,
            version: 1);
        work.RetainedBytes.Should().Be(600L,
            "the modeled shell is the isolated 528-byte allocation plus the repository's "
            + "8-byte margin and two empty 32-byte persistent roots");
        NavigationMaterializedComponentWork.BaseRetainedBytes.Should().Be(3_856L,
            "the modeled shell includes the four Task 4 references and the allocation margin");
        var preparation = new NavigationWorldGraph.StructuralPreparationWork(
            NavigationWorldGraph.Empty,
            new NavigationOperationCandidate(navigationAreaCount: 1),
            Array.Empty<NavigationOperationFrameChange>(),
            changeCount: 0,
            PersistentStringMap<bool>.Empty,
            version: 1);
        preparation.RetainedBytes.Should().Be(192L,
            "the modeled shell includes the Task 4 publication references and state flags");
    }

    [Fact]
    public void BakedTransitions_ShouldPublishCanonicalOutgoingAndIncomingPages()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = Configuration(3);
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell allMedia = Cell(
            TraversalMedia.Solid | TraversalMedia.Gas | TraversalMedia.Liquid);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(new VoxelIndex(0, 0, 0), allMedia)
            .AddCell(new VoxelIndex(1, 0, 0), allMedia)
            .AddCell(new VoxelIndex(2, 0, 0), allMedia)
            .AddTransition(Definition(
                "last-owner",
                TraversalTransitionType.Jump,
                source: 0,
                TraversalMedium.Solid,
                destination: 2,
                TraversalMedium.Gas))
            .AddTransition(Definition(
                "first-owner",
                TraversalTransitionType.Climb,
                source: 0,
                TraversalMedium.Solid,
                destination: 1,
                TraversalMedium.Liquid))
            .AddTransition(Definition(
                "same-medium",
                TraversalTransitionType.Jump,
                source: 0,
                TraversalMedium.Solid,
                destination: 0,
                TraversalMedium.Solid))
            .AddTransition(Definition(
                "z-from-one",
                TraversalTransitionType.Climb,
                source: 1,
                TraversalMedium.Solid,
                destination: 2,
                TraversalMedium.Gas))
            .AddTransition(Definition(
                "a-from-two",
                TraversalTransitionType.Jump,
                source: 2,
                TraversalMedium.Solid,
                destination: 2,
                TraversalMedium.Gas))
            .Build();
        NavigationWorldGraph graph = ComposeGraph(world, map);
        var sourceAddress = new NavigationCellAddress("map", new VoxelIndex(0, 0, 0));
        graph.TryGetMediumStateRef(sourceAddress, TraversalMedium.Solid, out NavigationMediumStateRef source)
            .Should().BeTrue();

        NavigationTransitionPage.Enumerator outgoing = graph.EnumerateOutgoingTransitions(source);
        ReadIds(ref outgoing).Should().Equal("same-medium", "first-owner", "last-owner");
        outgoing = graph.EnumerateOutgoingTransitions(source);
        outgoing.MoveNext().Should().BeTrue();
        outgoing.Current.Definition.SourceMedium.Should().Be(TraversalMedium.Solid);
        outgoing.Current.Definition.DestinationMedium.Should().Be(TraversalMedium.Solid,
            "same-medium authored actions are first-class transitions");

        for (int destination = 0; destination < 3; destination++)
        {
            var destinationAddress = new NavigationCellAddress(
                "map",
                new VoxelIndex(destination, 0, 0));
            TraversalMedium medium = destination switch
            {
                0 => TraversalMedium.Solid,
                1 => TraversalMedium.Liquid,
                _ => TraversalMedium.Gas
            };
            graph.TryGetMediumStateRef(destinationAddress, medium, out NavigationMediumStateRef state)
                .Should().BeTrue();
            NavigationTransitionPage.Enumerator incoming = graph.EnumerateIncomingTransitions(state);
            if (destination == 2)
            {
                ReadIds(ref incoming).Should().Equal(
                    "last-owner",
                    "z-from-one",
                    "a-from-two");
                continue;
            }
            incoming.MoveNext().Should().BeTrue();
            incoming.Current.Definition.Id.Should().Be(destination switch
            {
                0 => "same-medium",
                1 => "first-owner",
                _ => "last-owner"
            });
            incoming.MoveNext().Should().BeFalse();
        }
    }

    [Fact]
    public void OverlayAndCellChanges_ShouldReplaceSuppressRestoreAndDormantTransitions()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = Configuration(3);
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell allMedia = Cell(
            TraversalMedia.Solid | TraversalMedia.Gas | TraversalMedia.Liquid);
        TraversalTransitionDefinition baked = Definition(
            "route",
            TraversalTransitionType.Climb,
            source: 0,
            TraversalMedium.Solid,
            destination: 1,
            TraversalMedium.Liquid);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(new VoxelIndex(0, 0, 0), allMedia)
            .AddCell(new VoxelIndex(1, 0, 0), allMedia)
            .AddCell(new VoxelIndex(2, 0, 0), allMedia)
            .AddTransition(baked)
            .Build();
        NavigationWorldGraph graph = ComposeGraph(world, map);

        TraversalTransitionDefinition replacement = Definition(
            "route",
            TraversalTransitionType.Jump,
            source: 0,
            TraversalMedium.Solid,
            destination: 2,
            TraversalMedium.Gas);
        graph = ComposeGraph(
            world,
            map,
            NavigationMapOverlayState.Empty.Apply(
                TraversalTransitionOverlayOperation.Upsert(replacement),
                operationSequence: 2),
            graph,
            version: 2);
        ReadSingleOutgoing(graph).Definition.Should().Be(replacement);

        NavigationMapOverlayState suppressed = NavigationMapOverlayState.Empty.Apply(
            TraversalTransitionOverlayOperation.Suppress("route"),
            operationSequence: 3);
        graph = ComposeGraph(world, map, suppressed, graph, version: 3);
        CountOutgoing(graph).Should().Be(0);

        NavigationMapOverlayState reverted = suppressed.Apply(
            TraversalTransitionOverlayOperation.RevertToBake("route"),
            operationSequence: 4);
        NavigationWorldGraph restored = ComposeGraph(
            world,
            map,
            reverted,
            graph,
            version: 4);
        ReadSingleOutgoing(restored).Definition.Should().Be(baked);
        NavigationTransitionPageRoot activePages = restored.TransitionPages;

        NavigationMapOverlayState flooded = NavigationMapOverlayState.Empty.Apply(
            NavigationCellOverlayOperation.Suppress(new VoxelIndex(1, 0, 0)),
            operationSequence: 5);
        NavigationWorldGraph dormant = ComposeGraph(
            world,
            map,
            flooded,
            restored,
            version: 5);
        dormant.TransitionPages.Should().BeSameAs(activePages,
            "environmental media loss makes the definition dormant without rewriting it");
        CountOutgoing(dormant).Should().Be(0);

        NavigationWorldGraph reactivated = ComposeGraph(
            world,
            map,
            NavigationMapOverlayState.Empty,
            dormant,
            version: 6);
        reactivated.TransitionPages.Should().BeSameAs(activePages);
        ReadSingleOutgoing(reactivated).Definition.Should().Be(baked);
    }

    [Fact]
    public void MapFold_ShouldRejectDuplicateRuleIdsAcrossMaps()
    {
        NavigationMap first = RuleMap("first", origin: 0, "shared-rule");
        NavigationMap second = RuleMap("second", origin: 10, "shared-rule");
        NavigationOperationCandidate candidate = new(navigationAreaCount: 1);

        NavigationOperationRejection firstRejection = FoldMap(
            candidate,
            new PreparedNavigationMap(first, 1),
            out candidate);
        NavigationOperationRejection secondRejection = FoldMap(
            candidate,
            new PreparedNavigationMap(second, 1),
            out NavigationOperationCandidate rejectedCandidate);

        firstRejection.Should().Be(NavigationOperationRejection.None);
        secondRejection.Should().Be(NavigationOperationRejection.ValidationFailed);
        rejectedCandidate.MapCount.Should().Be(1,
            "duplicate global rule ownership rejects the candidate transaction");
    }

    [Fact]
    public void MapFold_ShouldMeterEveryRulelessTargetMapVisit()
    {
        NavigationOperationCandidate candidate = new(navigationAreaCount: 1);
        for (int i = 0; i < 8; i++)
        {
            FoldMap(
                candidate,
                new PreparedNavigationMap(
                    RulelessMap($"ruleless-{i}", origin: i * 10, TraversalMedia.Solid),
                    bakeVersion: 1),
                out candidate).Should().Be(NavigationOperationRejection.None);
        }

        TrailblazerWorldContextSettings settings = TrailblazerWorldContextSettings.Default;
        MaintenanceWorkBudget defaults = settings.MaintenanceBudget;
        var budget = new MaintenanceWorkBudget(
            defaults.MaxConsumedEnvelopes,
            defaults.MaxBaselineAddresses,
            defaults.MaxOverlaySlots,
            defaults.MaxComponentNodes,
            defaults.MaxSeamCandidateProbes,
            defaults.MaxExplicitEdges,
            maxDependencyEntries: 1,
            defaults.MaxSurfaceComponentEdges);
        int capacity = settings.OperationLimits.MaxCorridorCells;
        var work = new NavigationMapFoldWork(
            candidate,
            new PreparedNavigationMap(RuleMap("source", origin: 100, "source-rule"), 1),
            OverlayReplacementPolicy.Clear,
            settings.OperationLimits,
            new GridCellPrism[capacity],
            new Vector3d[(capacity * 2) - 2],
            new NavigationCellAddress[capacity],
            new NavigationAddressStampSet(capacity));
        var meter = new MaintenanceWorkMeter(budget);

        for (int frame = 1; frame < 8; frame++)
        {
            work.Advance(meter, out NavigationOperationRejection rejection).Should().BeFalse(
                "one dependency unit cannot visit all eight candidate target maps");
            rejection.Should().Be(NavigationOperationRejection.None);
            meter.DependencyEntries.Should().Be(1);
            meter.Reset();
        }
        work.Advance(meter, out NavigationOperationRejection finalRejection).Should().BeTrue();
        finalRejection.Should().Be(NavigationOperationRejection.None);
        work.Candidate.MapCount.Should().Be(9);
    }

    [Fact]
    public void DestinationPublication_ShouldKeepMissingTargetsDormantAndRejectFirstIncompatibleMap()
    {
        GridConfiguration sourceConfiguration = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        sourceConfiguration.TryNormalize(out NormalizedGridConfiguration sourceBinding)
            .Should().BeTrue();
        NavigationMap source = new NavigationMapBuilder("source", sourceBinding)
            .AddCell(new VoxelIndex(0, 0, 0), Cell(TraversalMedia.Solid))
            .AddTransition(new TraversalTransitionDefinition(
                "cross-map",
                TraversalTransitionType.Jump,
                new VoxelIndex(0, 0, 0),
                TraversalMedium.Solid,
                new NavigationCellAddress("target", new VoxelIndex(0, 0, 0)),
                TraversalMedium.Liquid))
            .Build();
        NavigationOperationCandidate candidate = new(navigationAreaCount: 1);
        FoldMap(candidate, new PreparedNavigationMap(source, 1), out candidate)
            .Should().Be(NavigationOperationRejection.None,
                "a missing destination map keeps the definition dormant");

        NavigationMap incompatible = RulelessMap("target", origin: 10, TraversalMedia.Gas);
        NavigationOperationRejection rejection = FoldMap(
            candidate,
            new PreparedNavigationMap(incompatible, 1),
            out NavigationOperationCandidate rejected);

        rejection.Should().Be(NavigationOperationRejection.ValidationFailed);
        rejected.MapCount.Should().Be(1);

        NavigationMap compatible = RulelessMap("target", origin: 10, TraversalMedia.Liquid);
        FoldMap(candidate, new PreparedNavigationMap(compatible, 1), out NavigationOperationCandidate accepted)
            .Should().Be(NavigationOperationRejection.None);
        accepted.MapCount.Should().Be(2);

        var flood = new NavigationOverlayTransaction(new[]
        {
            new NavigationMapOverlayDelta(
                "target",
                cells: new[]
                {
                    NavigationCellOverlayOperation.Set(
                        new VoxelIndex(0, 0, 0),
                        Cell(TraversalMedia.Gas))
                })
        });
        FoldOverlay(accepted, flood, out NavigationOperationCandidate dormant)
            .Should().Be(NavigationOperationRejection.None,
                "environmental media replacement makes the definition dormant");
        dormant.MapCount.Should().Be(2);
    }

    [Fact]
    public void DestinationOnlyRefresh_ShouldRebuildDormantCrossMapSourceFromIncomingRelation()
    {
        using var world = new GridWorld();
        GridConfiguration sourceConfiguration = ConfigurationAt(origin: 0);
        GridConfiguration targetConfiguration = ConfigurationAt(origin: 10);
        world.TryAddGrid(sourceConfiguration, out _).Should().BeTrue();
        world.TryAddGrid(targetConfiguration, out _).Should().BeTrue();
        sourceConfiguration.TryNormalize(out NormalizedGridConfiguration sourceBinding)
            .Should().BeTrue();
        targetConfiguration.TryNormalize(out NormalizedGridConfiguration targetBinding)
            .Should().BeTrue();
        TraversalTransitionDefinition definition = new(
            "cross-map",
            TraversalTransitionType.Jump,
            new VoxelIndex(0, 0, 0),
            TraversalMedium.Solid,
            new NavigationCellAddress("target", new VoxelIndex(0, 0, 0)),
            TraversalMedium.Liquid);
        NavigationMap sourceMap = new NavigationMapBuilder("source", sourceBinding)
            .AddCell(new VoxelIndex(0, 0, 0), Cell(TraversalMedia.Solid))
            .AddTransition(definition)
            .Build();
        NavigationMap targetMap = new NavigationMapBuilder("target", targetBinding)
            .AddCell(new VoxelIndex(0, 0, 0), Cell(TraversalMedia.Liquid))
            .Build();
        NavigationOperationCandidate operationCandidate = new(navigationAreaCount: 1);
        FoldMap(
            operationCandidate,
            new PreparedNavigationMap(sourceMap, 1),
            out operationCandidate).Should().Be(NavigationOperationRejection.None);

        NavigationWorldGraph sourceRaw = ComposeRawGraph(
            world,
            new[] { sourceMap },
            version: 1);
        PersistentStringMap<bool> sourceChanged = PersistentStringMap<bool>.Empty
            .Set("source", true);
        NavigationWorldGraph sourceGraph = Refresh(
            NavigationWorldGraph.Empty,
            sourceRaw,
            sourceChanged,
            rebuildRules: true,
            version: 1,
            operationCandidate);
        var sourcePageAddress = new NavigationTransitionPageAddress("source", 0);
        sourceGraph.TransitionPages.TryGet(sourcePageAddress, out NavigationTransitionPage dormantPage)
            .Should().BeTrue();
        dormantPage.TryGetOutgoing(
            new NavigationTransitionOwnerKey("source", "cross-map"),
            out NavigationPublishedTransition dormant).Should().BeTrue();
        dormant.DestinationPage.Should().BeNull();

        FoldMap(
            operationCandidate,
            new PreparedNavigationMap(targetMap, 2),
            out operationCandidate).Should().Be(NavigationOperationRejection.None);
        NavigationWorldGraph candidate = ComposeRawGraph(
            world,
            new[] { sourceMap, targetMap },
            version: 2);
        NavigationWorldGraph active = Refresh(
            sourceGraph,
            candidate,
            PersistentStringMap<bool>.Empty.Set("target", true),
            rebuildRules: true,
            version: 2,
            operationCandidate);

        active.TryGetMediumStateRef(
            definition.Destination,
            TraversalMedium.Liquid,
            out NavigationMediumStateRef destination).Should().BeTrue();
        NavigationTransitionPage.Enumerator incoming = active.EnumerateIncomingTransitions(destination);
        incoming.MoveNext().Should().BeTrue();
        incoming.Current.Definition.Should().Be(definition);
        incoming.MoveNext().Should().BeFalse();
    }

    [Fact]
    public void DependencyStamp_ShouldTrackTransitionPagesAndOptionalRuleGeneration()
    {
        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("transition-policy", 1),
            new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        NavigationAreaCatalog.Empty.TryPublish(
            policy,
            maxPolicies: 1,
            requiredRuleCount: 1,
            maxRulesPerPolicy: 1,
            maxRules: 1,
            next: out NavigationAreaCatalog catalog).Should().Be(NavigationOperationRejection.None);
        NavigationWorldGraph graph = new NavigationWorldGraph(
            1,
            Array.Empty<NavigationMapInstance>(),
            catalog).WithTransitionPublication(
                NavigationTransitionPageRoot.Empty,
                RuleTable(version: 7));

        graph.TryGetDependencyStamp(
            policy.Key,
            ReadOnlySpan<NavigationSurfaceComponentKey>.Empty,
            ReadOnlySpan<GraphPageDependencyAddress>.Empty,
            includeTransitionRules: true,
            out GraphDependencyStamp rules).Should().BeTrue();
        graph.TryGetDependencyStamp(
            policy.Key,
            ReadOnlySpan<NavigationSurfaceComponentKey>.Empty,
            ReadOnlySpan<GraphPageDependencyAddress>.Empty,
            out GraphDependencyStamp pagesOnly).Should().BeTrue();

        rules.HasTransitionRuleDependency.Should().BeTrue();
        rules.TransitionRuleVersion.Should().Be(7);
        NavigationWorldGraph changed = graph.WithTransitionPublication(
            NavigationTransitionPageRoot.Empty,
            RuleTable(version: 8));
        changed.IsDependencyCurrent(rules).Should().BeFalse();
        changed.IsDependencyCurrent(pagesOnly).Should().BeTrue();
    }

    [Fact]
    public void DependencyStamp_ShouldTrackExactTransitionPageAndIgnoreUnrelatedPageChanges()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = Configuration(130);
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell allMedia = Cell(TraversalMedia.Solid | TraversalMedia.Gas);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(new VoxelIndex(0, 0, 0), allMedia)
            .AddCell(new VoxelIndex(1, 0, 0), allMedia)
            .AddCell(new VoxelIndex(128, 0, 0), allMedia)
            .AddCell(new VoxelIndex(129, 0, 0), allMedia)
            .Build();
        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("transition-page-policy", 1),
            new[] { new NavigationAreaRule(true, Fixed64.Zero) });
        NavigationAreaCatalog.Empty.TryPublish(
            policy,
            maxPolicies: 1,
            requiredRuleCount: 1,
            maxRulesPerPolicy: 1,
            maxRules: 1,
            next: out NavigationAreaCatalog catalog).Should().Be(NavigationOperationRejection.None);
        NavigationTransitionPage first = TransitionPage(
            "first-page",
            source: 0,
            destination: 1,
            pageIndex: 0,
            version: 1);
        NavigationTransitionPage other = TransitionPage(
            "other-page",
            source: 129,
            destination: 128,
            pageIndex: 2,
            version: 1);
        NavigationTransitionPageRoot pages = NavigationTransitionPageRoot.Empty
            .Set(first, out _)
            .Set(other, out _);
        NavigationWorldGraph graph = ComposeRawGraph(
                world,
                map,
                NavigationMapOverlayState.Empty,
                version: 1)
            .WithAreaCatalog(catalog, graphVersion: 1)
            .WithTransitionPublication(pages, RuleTable(version: 7));
        GraphPageDependencyAddress[] addresses =
        {
            new("map", 0)
        };

        graph.TryGetDependencyStamp(
            policy.Key,
            ReadOnlySpan<NavigationSurfaceComponentKey>.Empty,
            addresses,
            includeTransitionRules: true,
            out GraphDependencyStamp exact).Should().BeTrue();
        graph.TryGetDependencyStamp(
            policy.Key,
            ReadOnlySpan<NavigationSurfaceComponentKey>.Empty,
            addresses,
            out GraphDependencyStamp legacy).Should().BeTrue();
        exact.Pages[0].TransitionVersion.Should().Be(1);
        legacy.Pages[0].TransitionVersion.Should().Be(0);

        NavigationTransitionPageRoot unrelatedPages = pages.Set(
            TransitionPage(
                "other-page",
                source: 129,
                destination: 128,
                pageIndex: 2,
                version: 2),
            out _);
        NavigationWorldGraph unrelated = graph.WithTransitionPublication(
            unrelatedPages,
            graph.TransitionRules);
        unrelated.IsDependencyCurrent(exact).Should().BeTrue();

        NavigationTransitionPageRoot changedPages = pages.Set(
            TransitionPage(
                "first-page",
                source: 0,
                destination: 1,
                pageIndex: 0,
                version: 2),
            out _);
        NavigationWorldGraph changed = graph.WithTransitionPublication(
            changedPages,
            graph.TransitionRules);
        changed.IsDependencyCurrent(exact).Should().BeFalse();
        changed.IsDependencyCurrent(legacy).Should().BeTrue();
    }

    [Fact]
    public void MaterializedFrontWork_ShouldRefreshTransitionPagesBeforeEarlyCompletion()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = Configuration(2);
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell allMedia = Cell(TraversalMedia.Solid | TraversalMedia.Liquid);
        TraversalTransitionDefinition definition = Definition(
            "materialized",
            TraversalTransitionType.Jump,
            source: 0,
            TraversalMedium.Solid,
            destination: 1,
            TraversalMedium.Liquid);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(new VoxelIndex(0, 0, 0), allMedia)
            .AddCell(new VoxelIndex(1, 0, 0), allMedia)
            .AddTransition(definition)
            .Build();
        NavigationWorldGraph raw = ComposeRawGraph(world, map, NavigationMapOverlayState.Empty, 1);
        var pageAddress = new NavigationTransitionPageAddress("map", 0);
        var outgoingOnly = new NavigationTransitionPage(
            pageAddress,
            version: 1,
            new[]
            {
                new NavigationPublishedTransition(
                    "map",
                    definition,
                    pageAddress,
                    destinationPage: null)
            },
            Array.Empty<NavigationIncomingTransitionRef>());
        NavigationTransitionPageRoot root = NavigationTransitionPageRoot.Empty.Set(
            outgoingOnly,
            out _);
        NavigationWorldGraph candidate = raw.WithTransitionPublication(
            root,
            NavigationTransitionRuleTable.Empty);
        NavigationMapInstance instance = candidate.GetInstance(0);
        var captures = new NavigationGridBaselineCapture[1];
        captures[0] = new NavigationGridBaselineCapture(
            instance,
            NavigationSurfaceComponentKeySet.Empty,
            defaultPhysicalAddressSetChanged: false,
            addressCount: 2,
            capturedChangeSequence: 1,
            worldSpawnToken: 1,
            gridIndex: 0,
            gridSpawnToken: 1,
            gridLastChangeSequence: 1,
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
        Advance(work);

        NavigationWorldGraph result = work.Result;
        result.TryGetMediumStateRef(
            new NavigationCellAddress("map", new VoxelIndex(1, 0, 0)),
            TraversalMedium.Liquid,
            out NavigationMediumStateRef destination).Should().BeTrue();
        NavigationTransitionPage.Enumerator incoming = result.EnumerateIncomingTransitions(destination);
        incoming.MoveNext().Should().BeTrue();
        incoming.Current.Definition.Should().Be(definition);
    }

    [Fact]
    public void MaterializedWork_ShouldCountEachDistinctRetainedGraphWrapper()
    {
        var work = new NavigationMaterializedComponentWork(
            NavigationWorldGraph.Empty,
            NavigationSurfaceComponentKeySet.Empty,
            NavigationSurfaceComponentKeySet.Empty,
            NavigationCellAddressSet.Empty,
            affectedMemberCount: 0,
            world: null,
            baselineCaptures: null,
            affectedMapOrdinals: null,
            affectedMapCount: 0,
            events: null,
            eventCount: 0);
        long baseline = work.RetainedBytes;
        NavigationWorldGraph transition = NavigationWorldGraph.Empty.WithGraphVersion(1);
        NavigationWorldGraph component = transition.WithGraphVersion(2);
        System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic;

        typeof(NavigationMaterializedComponentWork).GetField("_transitionGraph", flags)!
            .SetValue(work, transition);
        work.RetainedBytes.Should().Be(
            baseline + NavigationWorldGraph.BaseRetainedBytes,
            "a transition wrapper remains live while seam refresh is blocked");

        typeof(NavigationMaterializedComponentWork).GetField("_componentGraph", flags)!
            .SetValue(work, component);
        work.RetainedBytes.Should().Be(
            baseline + (2L * NavigationWorldGraph.BaseRetainedBytes),
            "the transition and seam/component wrappers coexist until publication");
    }

    [Fact]
    public void StructuralPreparation_ShouldCountDistinctPublishedGraphWrapper()
    {
        var work = new NavigationWorldGraph.StructuralPreparationWork(
            NavigationWorldGraph.Empty,
            new NavigationOperationCandidate(navigationAreaCount: 1),
            Array.Empty<NavigationOperationFrameChange>(),
            changeCount: 0,
            PersistentStringMap<bool>.Empty,
            version: 1);
        long baseline = work.RetainedBytes;
        NavigationWorldGraph prepared = NavigationWorldGraph.Empty.WithGraphVersion(1);
        NavigationWorldGraph published = prepared.WithGraphVersion(2);
        System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.NonPublic;

        typeof(NavigationWorldGraph.StructuralPreparationWork).GetField("_prepared", flags)!
            .SetValue(work, prepared);
        typeof(NavigationWorldGraph.StructuralPreparationWork)
            .GetField("<Result>k__BackingField", flags)!
            .SetValue(work, published);

        work.RetainedBytes.Should().Be(
            baseline + NavigationWorldGraph.BaseRetainedBytes,
            "prepared and transition-published wrappers coexist until store publication");
    }

    [Fact]
    public void MaterializedDefaultSource_ShouldPublishWhenItsPhysicalSlotFirstAppears()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
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
        NavigationCell allMedia = Cell(TraversalMedia.Solid | TraversalMedia.Gas);
        TraversalTransitionDefinition definition = Definition(
            "default-source",
            TraversalTransitionType.Jump,
            source: 0,
            TraversalMedium.Solid,
            destination: 1,
            TraversalMedium.Gas);
        var install = new NavigationMapCommitOperation(
            new PreparedNavigationMap(
                new NavigationMapBuilder("map", binding)
                    .SetDefaultCell(allMedia)
                    .AddTransition(definition)
                    .Build(),
                bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 1);
        context.Pathing.Admit(install).Should().BeTrue();
        NavigationMediumStateRef source = default;
        bool sourceMaterialized = false;
        for (int i = 0; i < 4096 && !sourceMaterialized; i++)
        {
            context.Simulate();
            using NavigationWorldGraphLease? current =
                context.Pathing.TryAcquireNavigationGraph();
            sourceMaterialized = current?.Graph.TryGetMediumStateRef(
                new NavigationCellAddress("map", default),
                TraversalMedium.Solid,
                out source) ?? false;
        }
        install.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        sourceMaterialized.Should().BeTrue();

        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationTransitionPage.Enumerator outgoing =
            lease.Graph.EnumerateOutgoingTransitions(source);
        outgoing.MoveNext().Should().BeTrue();
        outgoing.Current.Definition.Should().Be(definition);
        outgoing.MoveNext().Should().BeFalse();
    }

    [Fact]
    public void DormantDefaultSource_ShouldPublishWhenSparseMatterAppearsLater()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        context.World.TryAddGrid(
                configuration,
                new[] { new VoxelIndex(1, 0, 0) },
                out ushort gridIndex)
            .Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell allMedia = Cell(TraversalMedia.Solid | TraversalMedia.Gas);
        TraversalTransitionDefinition definition = Definition(
            "appears-later",
            TraversalTransitionType.Jump,
            source: 0,
            TraversalMedium.Solid,
            destination: 1,
            TraversalMedium.Gas);
        var install = new NavigationMapCommitOperation(
            new PreparedNavigationMap(
                new NavigationMapBuilder("map", binding)
                    .SetDefaultCell(allMedia)
                    .AddTransition(definition)
                    .Build(),
                bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 1);
        context.Pathing.Admit(install).Should().BeTrue();
        for (int i = 0;
             i < 4096 && install.Receipt.Status == NavigationOperationStatus.Pending;
             i++)
        {
            context.Simulate();
        }
        install.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        var sourceAddress = new NavigationCellAddress("map", default);
        using (NavigationWorldGraphLease dormant = context.Pathing.TryAcquireNavigationGraph()!)
            dormant.Graph.TryGetNodeRef(sourceAddress, out _).Should().BeFalse();

        context.World.ActiveGrids[gridIndex].TryAddVoxel(default, out _).Should().BeTrue();
        NavigationMediumStateRef source = default;
        bool materialized = false;
        for (int i = 0; i < 4096 && !materialized; i++)
        {
            context.Simulate();
            using NavigationWorldGraphLease? current = context.Pathing.TryAcquireNavigationGraph();
            materialized = current?.Graph.TryGetMediumStateRef(
                sourceAddress,
                TraversalMedium.Solid,
                out source) ?? false;
        }
        materialized.Should().BeTrue();
        using NavigationWorldGraphLease active = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationTransitionPage.Enumerator outgoing =
            active.Graph.EnumerateOutgoingTransitions(source);
        outgoing.MoveNext().Should().BeTrue();
        outgoing.Current.Definition.Should().Be(definition);
        outgoing.MoveNext().Should().BeFalse();
    }

    [Fact]
    public void DenseSamePageRefresh_ShouldCarryOverEveryCountFillSortAndCopy()
    {
        const int TransitionCount = 32;
        using var world = new GridWorld();
        GridConfiguration configuration = Configuration(TransitionCount + 1);
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell allMedia = Cell(TraversalMedia.Solid | TraversalMedia.Gas);
        var builder = new NavigationMapBuilder("map", binding);
        for (int i = 0; i <= TransitionCount; i++)
            builder.AddCell(new VoxelIndex(i, 0, 0), allMedia);
        for (int i = TransitionCount; i > 0; i--)
        {
            builder.AddTransition(Definition(
                $"transition-{i:D2}",
                TraversalTransitionType.Jump,
                source: 0,
                TraversalMedium.Solid,
                destination: i,
                TraversalMedium.Gas));
        }
        NavigationWorldGraph candidate = ComposeRawGraph(
            world,
            builder.Build(),
            NavigationMapOverlayState.Empty,
            version: 1);
        var work = new NavigationTransitionRefreshWork(
            NavigationWorldGraph.Empty,
            candidate,
            operationCandidate: null,
            PersistentStringMap<bool>.Empty.Set("map", true),
            rebuildRules: true,
            version: 1);
        var tiny = new MaintenanceWorkBudget(1, 1, 1, 1, 1, 1, 1, 1);
        var meter = new MaintenanceWorkMeter(tiny);
        int frames = 0;
        while (!work.Advance(meter) && frames < 4096)
        {
            frames++;
            meter.Reset();
        }

        work.IsComplete.Should().BeTrue();
        frames.Should().BeGreaterThan(
            TransitionCount * 4,
            "dense page visits, copies, and merge-sort outputs must each consume work");
        NavigationWorldGraph graph = candidate.WithTransitionPublication(work.Pages, work.Rules);
        graph.TryGetMediumStateRef(
            new NavigationCellAddress("map", default),
            TraversalMedium.Solid,
            out NavigationMediumStateRef source).Should().BeTrue();
        NavigationTransitionPage.Enumerator outgoing = graph.EnumerateOutgoingTransitions(source);
        int count = 0;
        while (outgoing.MoveNext())
            count++;
        count.Should().Be(TransitionCount);
    }

    [Fact]
    public void PageSealCarryover_ShouldRetainUnpublishedFinalArraysAtExactBytesAndPages()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = Configuration(130);
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell allMedia = Cell(TraversalMedia.Solid | TraversalMedia.Gas);
        var builder = new NavigationMapBuilder("map", binding);
        for (int i = 0; i < 130; i++)
            builder.AddCell(new VoxelIndex(i, 0, 0), allMedia);
        builder.AddTransition(Definition(
            "first",
            TraversalTransitionType.Jump,
            source: 0,
            TraversalMedium.Solid,
            destination: 1,
            TraversalMedium.Gas));
        builder.AddTransition(Definition(
            "last",
            TraversalTransitionType.Jump,
            source: 129,
            TraversalMedium.Solid,
            destination: 128,
            TraversalMedium.Gas));
        NavigationWorldGraph candidate = ComposeRawGraph(
            world,
            builder.Build(),
            NavigationMapOverlayState.Empty,
            version: 1);
        var work = new NavigationTransitionRefreshWork(
            NavigationWorldGraph.Empty,
            candidate,
            operationCandidate: null,
            PersistentStringMap<bool>.Empty.Set("map", true),
            rebuildRules: false,
            version: 1);
        long initialRetainedBytes = work.RetainedBytes;
        var defaults = TrailblazerWorldContextSettings.Default.MaintenanceBudget;
        var budget = new MaintenanceWorkBudget(
            defaults.MaxConsumedEnvelopes,
            defaults.MaxBaselineAddresses,
            defaults.MaxOverlaySlots,
            maxComponentNodes: 1,
            defaults.MaxSeamCandidateProbes,
            defaults.MaxExplicitEdges,
            defaults.MaxDependencyEntries,
            defaults.MaxSurfaceComponentEdges);
        var meter = new MaintenanceWorkMeter(budget);
        NavigationTransitionPageAddress firstAddress = new("map", 0);
        NavigationTransitionPageAddress lastAddress = new("map", 2);
        for (int frame = 0; frame < 32; frame++)
        {
            work.Advance(meter).Should().BeFalse();
            if (work.Pages.TryGet(firstAddress, out _)
                && !work.Pages.TryGet(lastAddress, out _))
            {
                break;
            }
            meter.Reset();
        }
        work.Pages.TryGet(firstAddress, out _).Should().BeTrue();
        work.Pages.TryGet(lastAddress, out _).Should().BeFalse();

        PersistentStringMap<bool> oneMap = PersistentStringMap<bool>.Empty.Set("map", true);
        PersistentIntMap<bool> twoPages = PersistentIntMap<bool>.Empty
            .Set(0, true)
            .Set(2, true);
        long pendingArrays = NavigationTransitionPage.GetArrayBytes(
                1,
                NavigationTransitionPage.OutgoingRecordBytes)
            + NavigationTransitionPage.GetArrayBytes(
                1,
                NavigationTransitionPage.IncomingRecordBytes);
        long expectedBytes = initialRetainedBytes
            + oneMap.RetainedBytes - PersistentStringMap<bool>.Empty.RetainedBytes
            + oneMap.RetainedBytes - PersistentStringMap<bool>.Empty.RetainedBytes
            + twoPages.RetainedBytes
            + (2L * 240L)
            + pendingArrays
            + work.Pages.RetainedBytes;
        int expectedPages = 1
            + oneMap.PersistentNodeCount
            + oneMap.PersistentNodeCount
            + twoPages.PersistentNodeCount
            + 2
            + 2
            + work.Pages.PersistentPageCount;

        work.RetainedBytes.Should().Be(expectedBytes);
        work.PersistentPageCount.Should().Be(expectedPages);
    }

    [Fact]
    public void RuleTable_ShouldBeGlobalIdFirstExactSizedAndReuseEqualContent()
    {
        using var world = new GridWorld();
        GridConfiguration firstConfiguration = ConfigurationAt(origin: 0);
        GridConfiguration secondConfiguration = ConfigurationAt(origin: 10);
        world.TryAddGrid(firstConfiguration, out _).Should().BeTrue();
        world.TryAddGrid(secondConfiguration, out _).Should().BeTrue();
        NavigationMap first = RuleMap("first", origin: 0, "z-rule");
        NavigationMap second = RuleMap("second", origin: 10, "a-rule");
        NavigationWorldGraph raw = ComposeRawGraph(world, new[] { first, second }, version: 1);
        PersistentStringMap<bool> changed = PersistentStringMap<bool>.Empty
            .Set("first", true)
            .Set("second", true);
        NavigationWorldGraph graph = Refresh(
            NavigationWorldGraph.Empty,
            raw,
            changed,
            rebuildRules: true,
            version: 1);

        graph.TransitionRules.Count.Should().Be(2);
        graph.TransitionRules[0].Id.Should().Be("a-rule");
        graph.TransitionRules[1].Id.Should().Be("z-rule");
        graph.TransitionRules.RetainedBytes.Should().Be(
            NavigationTransitionRuleTable.BaseRetainedBytes
            + 24L
            + (2L * NavigationTransitionRuleTable.RecordRetainedBytes));

        NavigationWorldGraph same = Refresh(
            graph,
            raw.WithGraphVersion(2),
            changed,
            rebuildRules: true,
            version: 2);
        same.TransitionRules.Should().BeSameAs(graph.TransitionRules);
        same.TransitionRules.Version.Should().Be(1);
    }

    [Fact]
    public void TransitionRoots_ShouldParticipateInExactStoreByteAndPageCapacity()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = Configuration(2);
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell allMedia = Cell(TraversalMedia.Solid | TraversalMedia.Gas);
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(new VoxelIndex(0, 0, 0), allMedia)
            .AddCell(new VoxelIndex(1, 0, 0), allMedia)
            .AddTransition(Definition(
                "explicit",
                TraversalTransitionType.Jump,
                source: 0,
                TraversalMedium.Solid,
                destination: 1,
                TraversalMedium.Gas))
            .AddTransitionRule(new TraversalTransitionRule(
                "rule",
                TraversalTransitionType.Custom,
                TraversalMedium.Solid,
                TraversalMedium.Gas,
                TraversalTransitionRuleScope.SameCell,
                TraversalCapability.None,
                Fixed64.Zero,
                TraversalTransitionLocomotionHints.None))
            .Build();
        NavigationWorldGraph graph = ComposeGraph(world, map);

        using var exact = new NavigationWorldGraphStore(
            maxActiveSnapshots: 1,
            maxRetiredSnapshots: 0,
            maxRetiredBytes: 0,
            maxActiveBytes: graph.RetainedBytes,
            maxPersistentPages: graph.PersistentPageCount,
            maxConcurrentLeases: 1);
        exact.TryPublish(graph).Should().Be(NavigationCandidatePublication.Published);

        using var byteShort = new NavigationWorldGraphStore(
            maxActiveSnapshots: 1,
            maxRetiredSnapshots: 0,
            maxRetiredBytes: 0,
            maxActiveBytes: graph.RetainedBytes - 1,
            maxPersistentPages: graph.PersistentPageCount,
            maxConcurrentLeases: 1);
        byteShort.TryPublish(graph).Should().Be(NavigationCandidatePublication.PermanentCapacity);

        using var pageShort = new NavigationWorldGraphStore(
            maxActiveSnapshots: 1,
            maxRetiredSnapshots: 0,
            maxRetiredBytes: 0,
            maxActiveBytes: graph.RetainedBytes,
            maxPersistentPages: graph.PersistentPageCount - 1,
            maxConcurrentLeases: 1);
        pageShort.TryPublish(graph).Should().Be(NavigationCandidatePublication.PermanentCapacity);
    }

    [Fact]
    public void TransitionPages_ShouldChangeOnlyAffectedPageAndDrainWithStoreLease()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = Configuration(130);
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationCell allMedia = Cell(TraversalMedia.Solid | TraversalMedia.Gas);
        var builder = new NavigationMapBuilder("map", binding);
        for (int i = 0; i < 130; i++)
            builder.AddCell(new VoxelIndex(i, 0, 0), allMedia);
        NavigationMap map = builder
            .AddTransition(Definition(
                "first",
                TraversalTransitionType.Jump,
                source: 0,
                TraversalMedium.Solid,
                destination: 1,
                TraversalMedium.Gas))
            .AddTransition(Definition(
                "last",
                TraversalTransitionType.Jump,
                source: 129,
                TraversalMedium.Solid,
                destination: 128,
                TraversalMedium.Gas))
            .Build();
        NavigationWorldGraph first = ComposeGraph(world, map);
        first.TransitionPages.TryGet(
            new NavigationTransitionPageAddress("map", 2),
            out NavigationTransitionPage unaffected).Should().BeTrue();

        NavigationMapOverlayState overlay = NavigationMapOverlayState.Empty.Apply(
            TraversalTransitionOverlayOperation.Upsert(Definition(
                "first",
                TraversalTransitionType.Climb,
                source: 0,
                TraversalMedium.Solid,
                destination: 2,
                TraversalMedium.Gas)),
            operationSequence: 2);
        NavigationWorldGraph second = ComposeGraph(world, map, overlay, first, version: 2);
        second.TransitionPages.TryGet(
            new NavigationTransitionPageAddress("map", 2),
            out NavigationTransitionPage stillUnaffected).Should().BeTrue();
        stillUnaffected.Should().BeSameAs(unaffected);
        second.TransitionPages.TryGet(
            new NavigationTransitionPageAddress("map", 0),
            out NavigationTransitionPage affected).Should().BeTrue();
        affected.Version.Should().Be(2);

        using var store = new NavigationWorldGraphStore(
            maxActiveSnapshots: 3,
            maxRetiredSnapshots: 2,
            maxRetiredBytes: long.MaxValue,
            maxActiveBytes: long.MaxValue,
            maxPersistentPages: int.MaxValue,
            maxConcurrentLeases: 1);
        store.TryPublish(first).Should().Be(NavigationCandidatePublication.Published);
        NavigationWorldGraphLease lease = store.TryAcquire()!;
        store.TryPublish(second).Should().Be(NavigationCandidatePublication.Published);
        store.RetiredGenerationCount.Should().Be(1);
        store.RetiredBytes.Should().Be(first.RetainedBytes);
        lease.Dispose();
        store.RetiredGenerationCount.Should().Be(0);
        store.RetiredBytes.Should().Be(0);
    }

    private static string[] ReadIds(ref NavigationTransitionPage.Enumerator enumerator)
    {
        var ids = new string[3];
        int count = 0;
        while (enumerator.MoveNext())
            ids[count++] = enumerator.Current.Definition.Id;
        count.Should().Be(ids.Length);
        return ids;
    }

    private static NavigationWorldGraph ComposeGraph(GridWorld world, NavigationMap map) =>
        ComposeGraph(
            world,
            map,
            NavigationMapOverlayState.Empty,
            NavigationWorldGraph.Empty,
            version: 1);

    private static NavigationWorldGraph ComposeGraph(
        GridWorld world,
        NavigationMap map,
        NavigationMapOverlayState overlay,
        NavigationWorldGraph source,
        long version)
    {
        NavigationWorldGraph candidate = ComposeRawGraph(world, map, overlay, version);
        PersistentStringMap<bool> changed = PersistentStringMap<bool>.Empty.Set(map.MapId, true);
        var work = new NavigationTransitionRefreshWork(
            source,
            candidate,
            operationCandidate: null,
            changed,
            rebuildRules: source.MapCount == 0,
            version);
        var meter = new MaintenanceWorkMeter(
            TrailblazerWorldContextSettings.Default.MaintenanceBudget);
        work.Advance(meter).Should().BeTrue();
        return candidate.WithTransitionPublication(work.Pages, work.Rules);
    }

    private static NavigationWorldGraph ComposeRawGraph(
        GridWorld world,
        NavigationMap map,
        NavigationMapOverlayState overlay,
        long version)
    {
        var prepared = new PreparedNavigationMap(map, version);
        var state = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            overlay,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        NavigationMapInstance instance = NavigationMapInstanceTestFactory.Compose(
            world,
            state,
            previous: null,
            instanceVersion: version);
        return new NavigationWorldGraph(version, new[] { instance });
    }

    private static void Advance(NavigationMaterializedComponentWork work)
    {
        var meter = new MaintenanceWorkMeter(
            TrailblazerWorldContextSettings.Default.MaintenanceBudget);
        for (int i = 0; i < 4096; i++)
        {
            if (work.Advance(meter))
                return;
            meter.Reset();
        }
        throw new Xunit.Sdk.XunitException("Materialized transition refresh did not complete.");
    }

    private static NavigationPublishedTransition ReadSingleOutgoing(NavigationWorldGraph graph)
    {
        var address = new NavigationCellAddress("map", new VoxelIndex(0, 0, 0));
        graph.TryGetMediumStateRef(address, TraversalMedium.Solid, out NavigationMediumStateRef source)
            .Should().BeTrue();
        NavigationTransitionPage.Enumerator outgoing = graph.EnumerateOutgoingTransitions(source);
        outgoing.MoveNext().Should().BeTrue();
        NavigationPublishedTransition result = outgoing.Current;
        outgoing.MoveNext().Should().BeFalse();
        return result;
    }

    private static int CountOutgoing(NavigationWorldGraph graph)
    {
        var address = new NavigationCellAddress("map", new VoxelIndex(0, 0, 0));
        graph.TryGetMediumStateRef(address, TraversalMedium.Solid, out NavigationMediumStateRef source)
            .Should().BeTrue();
        NavigationTransitionPage.Enumerator outgoing = graph.EnumerateOutgoingTransitions(source);
        int count = 0;
        while (outgoing.MoveNext())
            count++;
        return count;
    }

    private static NavigationMap RuleMap(string mapId, int origin, string ruleId)
    {
        GridConfiguration configuration = ConfigurationAt(origin);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        return new NavigationMapBuilder(mapId, binding)
            .AddCell(new VoxelIndex(0, 0, 0), Cell(TraversalMedia.Solid))
            .AddTransitionRule(new TraversalTransitionRule(
                ruleId,
                TraversalTransitionType.Custom,
                TraversalMedium.Solid,
                TraversalMedium.Gas,
                TraversalTransitionRuleScope.SameCell,
                TraversalCapability.None,
                Fixed64.Zero,
                TraversalTransitionLocomotionHints.None))
            .Build();
    }

    private static NavigationTransitionRuleTable RuleTable(long version) => new(
        new[]
        {
            new TraversalTransitionRule(
                "rule",
                TraversalTransitionType.Custom,
                TraversalMedium.Solid,
                TraversalMedium.Gas,
                TraversalTransitionRuleScope.SameCell,
                TraversalCapability.None,
                Fixed64.Zero,
                TraversalTransitionLocomotionHints.None)
        },
        version);

    private static NavigationTransitionPage TransitionPage(
        string id,
        int source,
        int destination,
        int pageIndex,
        long version)
    {
        TraversalTransitionDefinition definition = Definition(
            id,
            TraversalTransitionType.Jump,
            source,
            TraversalMedium.Solid,
            destination,
            TraversalMedium.Gas);
        var address = new NavigationTransitionPageAddress("map", pageIndex);
        return new NavigationTransitionPage(
            address,
            version,
            new[]
            {
                new NavigationPublishedTransition(
                    "map",
                    definition,
                    address,
                    address)
            },
            Array.Empty<NavigationIncomingTransitionRef>());
    }

    private static NavigationMap RulelessMap(
        string mapId,
        int origin,
        TraversalMedia media)
    {
        GridConfiguration configuration = new(
            new Vector3d(origin, 0, 0),
            new Vector3d(origin, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        return new NavigationMapBuilder(mapId, binding)
            .AddCell(new VoxelIndex(0, 0, 0), Cell(media))
            .Build();
    }

    private static NavigationWorldGraph ComposeRawGraph(
        GridWorld world,
        NavigationMap[] maps,
        long version)
    {
        var instances = new NavigationMapInstance[maps.Length];
        for (int i = 0; i < maps.Length; i++)
        {
            PreparedNavigationMap prepared = new(maps[i], version);
            var state = new NavigationOperationCandidate.MapState(
                prepared.Map,
                prepared.BakeVersion,
                prepared.RetainedBytes,
                NavigationMapOverlayState.Empty,
                dynamicSlotGeneration: 0,
                bakedCellLookup: prepared.BakedCellLookup);
            instances[i] = NavigationMapInstanceTestFactory.Compose(
                world,
                state,
                previous: null,
                instanceVersion: version);
        }
        return new NavigationWorldGraph(version, instances);
    }

    private static NavigationWorldGraph Refresh(
        NavigationWorldGraph source,
        NavigationWorldGraph candidate,
        PersistentStringMap<bool> changed,
        bool rebuildRules,
        long version,
        NavigationOperationCandidate? operationCandidate = null)
    {
        var work = new NavigationTransitionRefreshWork(
            source,
            candidate,
            operationCandidate,
            changed,
            rebuildRules,
            version);
        var meter = new MaintenanceWorkMeter(
            TrailblazerWorldContextSettings.Default.MaintenanceBudget);
        for (int i = 0; i < 4096; i++)
        {
            if (work.Advance(meter))
            {
                return candidate.WithTransitionPublication(work.Pages, work.Rules);
            }
            meter.Reset();
        }
        throw new Xunit.Sdk.XunitException("Transition refresh did not complete.");
    }

    private static GridConfiguration ConfigurationAt(int origin) => new(
        new Vector3d(origin, 0, 0),
        new Vector3d(origin, 0, 0),
        topologyKind: GridTopologyKind.RectangularPrism,
        topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
        storageKind: GridStorageKind.Dense);

    private static NavigationOperationRejection FoldMap(
        NavigationOperationCandidate source,
        PreparedNavigationMap prepared,
        out NavigationOperationCandidate candidate)
    {
        TrailblazerWorldContextSettings settings = TrailblazerWorldContextSettings.Default;
        int capacity = settings.OperationLimits.MaxCorridorCells;
        var work = new NavigationMapFoldWork(
            source,
            prepared,
            OverlayReplacementPolicy.Clear,
            settings.OperationLimits,
            new GridCellPrism[capacity],
            new Vector3d[(capacity * 2) - 2],
            new NavigationCellAddress[capacity],
            new NavigationAddressStampSet(capacity));
        var meter = new MaintenanceWorkMeter(settings.MaintenanceBudget);
        for (int i = 0; i < 4096; i++)
        {
            if (work.Advance(meter, out NavigationOperationRejection rejection))
            {
                candidate = work.Candidate;
                return rejection;
            }
            rejection.Should().Be(NavigationOperationRejection.None);
            meter.Reset();
        }
        throw new Xunit.Sdk.XunitException("Map fold did not complete.");
    }

    private static NavigationOperationRejection FoldOverlay(
        NavigationOperationCandidate source,
        NavigationOverlayTransaction transaction,
        out NavigationOperationCandidate candidate)
    {
        TrailblazerWorldContextSettings settings = TrailblazerWorldContextSettings.Default;
        int capacity = settings.OperationLimits.MaxCorridorCells;
        var work = new NavigationOverlayFoldWork(
            source,
            transaction,
            operationSequence: 2,
            settings.OperationLimits,
            new GridCellPrism[capacity],
            new Vector3d[(capacity * 2) - 2],
            new NavigationCellAddress[capacity],
            new NavigationAddressStampSet(capacity));
        var meter = new MaintenanceWorkMeter(settings.MaintenanceBudget);
        for (int i = 0; i < 4096; i++)
        {
            if (work.Advance(meter, out NavigationOperationRejection rejection))
            {
                candidate = work.Candidate;
                return rejection;
            }
            rejection.Should().Be(NavigationOperationRejection.None);
            meter.Reset();
        }
        throw new Xunit.Sdk.XunitException("Overlay fold did not complete.");
    }

    private static TraversalTransitionDefinition Definition(
        string id,
        TraversalTransitionType type,
        int source,
        TraversalMedium sourceMedium,
        int destination,
        TraversalMedium destinationMedium) => new(
        id,
        type,
        new VoxelIndex(source, 0, 0),
        sourceMedium,
        new NavigationCellAddress("map", new VoxelIndex(destination, 0, 0)),
        destinationMedium);

    private static NavigationCell Cell(TraversalMedia media) => new(
        media,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        Fixed64.Zero,
        Fixed64.One);

    private static GridConfiguration Configuration(int cellCount) => new(
        Vector3d.Zero,
        new Vector3d(cellCount - 1, 0, 0),
        topologyKind: GridTopologyKind.RectangularPrism,
        topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
        storageKind: GridStorageKind.Dense);
}

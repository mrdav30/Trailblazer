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

public sealed class NavigationStructuralCompositionCarryoverTests
{
    private static readonly NavigationCell Cell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        Fixed64.Zero,
        Fixed64.One);

    [Fact]
    public void BridgeRemoval_ShouldFailClosedOnlyAffectedComponentUntilAtomicPublication()
    {
        using TrailblazerWorldContext context = CreateConnectedContext(out long sequence);
        NavigationGraphDiagnosticsSnapshot before = context.Pathing.GetNavigationGraphDiagnostics();
        int priorComponent = FindMap(before, "A").ComponentId;
        var removal = SuppressTransition(context, ++sequence, "B", "bc");

        context.Simulate();

        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        (context.Pathing.RetainedCompositionWorkCount
            + context.Pathing.RetainedOperationWorkCount).Should().Be(1);
        context.Pathing.NavigationMaintenanceMeter.OverlaySlots.Should().Be(1);
        context.Pathing.NavigationMaintenanceMeter.ImplicitEdges.Should().Be(0);
        context.Pathing.NavigationMaintenanceMeter.SeamCandidates.Should().Be(0);
        context.Pathing.NavigationMaintenanceMeter.CacheInvalidations.Should().Be(0);
        GetCell(context, "A").IsMaterialized.Should().BeFalse();
        GetCell(context, "B").IsMaterialized.Should().BeFalse();
        GetCell(context, "C").IsMaterialized.Should().BeFalse();
        GetCell(context, "U").IsMaterialized.Should().BeTrue();
        NavigationGraphDiagnosticsSnapshot blocked = context.Pathing.GetNavigationGraphDiagnostics();
        FindMap(blocked, "A").ComponentId.Should().Be(priorComponent);
        FindMap(blocked, "C").ComponentId.Should().Be(priorComponent);
        blocked.ActiveSnapshotBytes.Should().BeGreaterThan(0);
        blocked.PersistentGraphPageCount.Should().BeGreaterThan(0);
        blocked.ActiveSnapshotBytes.Should().BeLessThanOrEqualTo(
            context.Settings.MaxActiveSnapshotBytes);
        blocked.PersistentGraphPageCount.Should().BeLessThanOrEqualTo(
            context.Settings.MaxPersistentGraphPages);

        SimulateUntilTerminal(context, removal.Receipt);

        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        context.Pathing.RetainedCompositionWorkCount.Should().Be(0);
        GetCell(context, "A").IsMaterialized.Should().BeTrue();
        GetCell(context, "C").IsMaterialized.Should().BeTrue();
        NavigationGraphDiagnosticsSnapshot published = context.Pathing.GetNavigationGraphDiagnostics();
        FindMap(published, "A").ComponentId.Should().NotBe(FindMap(published, "C").ComponentId);
    }

    [Fact]
    public void DeferredStructuralBatch_ShouldNotAllowLaterOperationToOvertake()
    {
        using TrailblazerWorldContext context = CreateConnectedContext(out long sequence);
        var removal = SuppressTransition(context, ++sequence, "B", "bc");
        context.Simulate();
        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);

        NavigationMap laterMap = AddGridAndCreateMap(context, "V", 50, null, null);
        var later = new NavigationMapCommitOperation(
            new PreparedNavigationMap(laterMap, 1),
            OverlayReplacementPolicy.Clear,
            ++sequence,
            context.FrameCount + 1);
        context.Pathing.Admit(later).Should().BeTrue();

        SimulateUntilTerminal(context, removal.Receipt);

        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        later.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        context.Pathing.TryGetNavigationGraphCellState("V", default, out _).Should().BeFalse();
        SimulateUntilTerminal(context, later.Receipt);
        later.Receipt.PublishedFrame.Should().BeGreaterThan(removal.Receipt.PublishedFrame);
    }

    [Fact]
    public void StructuralCarryover_ShouldPublishOnSameDeterministicFrame()
    {
        int first = RunBridgeRemoval();
        int second = RunBridgeRemoval();

        first.Should().Be(second);
        first.Should().BeGreaterThan(1);
    }

    [Fact]
    public void Reset_ShouldReleaseUnpublishedStructuralWork()
    {
        using TrailblazerWorldContext context = CreateConnectedContext(out long sequence);
        NavigationOverlayCommitOperation removal = SuppressTransition(
            context,
            ++sequence,
            "B",
            "bc");
        context.Simulate();
        (context.Pathing.RetainedCompositionWorkCount
            + context.Pathing.RetainedOperationWorkCount).Should().Be(1);

        context.Reset();

        context.Pathing.RetainedCompositionWorkCount.Should().Be(0);
        context.Pathing.RetainedOperationWorkCount.Should().Be(0);
        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Superseded);
        context.Pathing.TryGetNavigationGraphCellState("A", default, out _).Should().BeFalse();
    }

    [Fact]
    public void SameFrameStructuralCompletion_ShouldPublishUpdateWorkFlatComponent()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(settings: defaults);
        NavigationMap a = AddGridAndCreateMap(context, "A", 0, "B", "ab");
        NavigationMap b = AddGridAndCreateMap(context, "B", 10, null, null);
        NavigationMapCommitOperation first = AdmitMap(context, a, 1);
        NavigationMapCommitOperation second = AdmitMap(context, b, 2);

        context.Simulate();

        first.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        second.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        context.Pathing.RetainedCompositionWorkCount.Should().Be(0);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.Composition.TryGetComponentRecord("A", out NavigationStructuralComponent component)
            .Should().BeTrue();
        component.FlatMembers.Should().Equal("A", "B");
    }

    [Fact]
    public void StructuralWork_ShouldExposeSameCountMultiMapCopiesBeyondTightCap()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        GridCellPrism[] corridorPrisms = new GridCellPrism[defaults.OperationLimits.MaxCorridorCells];
        Vector3d[] corridorWaypoints = new Vector3d[(defaults.OperationLimits.MaxCorridorCells * 2) - 2];
        NormalizedGridConfiguration firstBinding = CreateBinding(0);
        NormalizedGridConfiguration secondBinding = CreateBinding(10);
        TraversalTransitionDefinition firstTransition = CreateTransition("ab", "B", Fixed64.Zero);
        TraversalTransitionDefinition secondTransition = CreateTransition("ba", "A", Fixed64.Zero);
        NavigationMap first = new NavigationMapBuilder("A", firstBinding)
            .AddCell(default, Cell)
            .AddTransition(firstTransition)
            .Build();
        NavigationMap second = new NavigationMapBuilder("B", secondBinding)
            .AddCell(default, Cell)
            .AddTransition(secondTransition)
            .Build();
        var candidate = new NavigationOperationCandidate(navigationAreaCount: 1);
        candidate.ApplyMap(
            new PreparedNavigationMap(first, 1),
            OverlayReplacementPolicy.Clear,
            defaults.OperationLimits,
            corridorPrisms,
            corridorWaypoints).Should().Be(NavigationOperationRejection.None);
        candidate.ApplyMap(
            new PreparedNavigationMap(second, 1),
            OverlayReplacementPolicy.Clear,
            defaults.OperationLimits,
            corridorPrisms,
            corridorWaypoints).Should().Be(NavigationOperationRejection.None);
        var initialOverlay = new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
        {
            new NavigationMapOverlayDelta(
                "A",
                transitions: new[] { TraversalTransitionOverlayOperation.Upsert(firstTransition) }),
            new NavigationMapOverlayDelta(
                "B",
                transitions: new[] { TraversalTransitionOverlayOperation.Upsert(secondTransition) })
        }));
        candidate.ApplyOverlay(
            initialOverlay.Transaction,
            1,
            defaults.OperationLimits,
            corridorPrisms,
            corridorWaypoints).Should().Be(NavigationOperationRejection.None);
        NavigationOperationCandidate.MapState[] states = candidate.CaptureStates();
        var instances = new NavigationMapInstance[states.Length];
        for (int i = 0; i < states.Length; i++)
        {
            instances[i] = NavigationMapInstance.ComposeDetached(
                states[i],
                previous: null,
                instanceVersion: 1);
        }
        var source = new NavigationWorldGraph(1, instances);

        NavigationOperationCandidate replacementCandidate = candidate.Clone();
        var replacementOverlay = new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
        {
            new NavigationMapOverlayDelta(
                "A",
                transitions: new[]
                {
                    TraversalTransitionOverlayOperation.Upsert(
                        CreateTransition("ab", "B", Fixed64.One))
                }),
            new NavigationMapOverlayDelta(
                "B",
                transitions: new[]
                {
                    TraversalTransitionOverlayOperation.Upsert(
                        CreateTransition("ba", "A", Fixed64.One))
                })
        }));
        replacementCandidate.ApplyOverlay(
            replacementOverlay.Transaction,
            2,
            defaults.OperationLimits,
            corridorPrisms,
            corridorWaypoints).Should().Be(NavigationOperationRejection.None);
        NavigationOperationFrameChange[] changes =
        {
            NavigationOperationFrameChange.Overlay(replacementOverlay, 2)
        };
        var work = new NavigationStructuralCompositionWork(
            source,
            replacementCandidate,
            changes,
            changes.Length,
            updateComposition: true);
        long tightByteCap = work.RetainedBytes;
        int tightPageCap = work.PersistentPageCount;
        var meter = new MaintenanceWorkMeter(defaults.MaintenanceBudget);

        work.Advance(meter).Should().BeTrue();

        work.IsComplete.Should().BeTrue();
        (work.RetainedBytes > tightByteCap || work.PersistentPageCount > tightPageCap)
            .Should().BeTrue("same-count structural replacements retain copied persistent paths");
        work.Result.Composition.GetIncidentEdgeCount(0).Should().Be(2);
        work.Result.Composition.GetIncidentEdgeCount(1).Should().Be(2);
    }

    [Fact]
    public void ConcurrentAreaPolicyAndStructuralBridge_ShouldConvergeUnderExactDependencyBudget()
    {
        using TrailblazerWorldContext context = CreateConnectedContext(out long sequence);
        var policyKey = new NavigationAreaPolicyKey("ground", 1);
        var policy = new NavigationAreaPolicyCommitOperation(
            new NavigationAreaPolicy(
                policyKey,
                new[] { new NavigationAreaRule(true, Fixed64.Zero) }),
            publicationSequence: 1,
            effectiveFrame: context.FrameCount + 1);
        NavigationOverlayCommitOperation removal = SuppressTransition(
            context,
            ++sequence,
            "B",
            "bc");
        context.Pathing.Admit(policy).Should().BeTrue();

        for (int i = 0;
             i < 64 && (policy.Receipt.Status == NavigationOperationStatus.Pending
                 || removal.Receipt.Status == NavigationOperationStatus.Pending);
             i++)
        {
            context.Simulate();
        }

        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        policy.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        context.Pathing.TryResolveNavigationAreaPolicy(policyKey, out NavigationAreaPolicy? resolved)
            .Should().BeTrue();
        resolved.Should().BeSameAs(policy.Policy);
        context.Pathing.RetainedCompositionWorkCount.Should().Be(0);
        context.Pathing.RetainedOperationWorkCount.Should().Be(0);
    }

    [Fact]
    public void UnrelatedObstacle_ShouldPublishDuringStructuralCarryoverWithoutBeingReverted()
    {
        using TrailblazerWorldContext context = CreateConnectedContext(out long sequence);
        NavigationOverlayCommitOperation removal = SuppressTransition(
            context,
            ++sequence,
            "B",
            "bc");
        context.Simulate();
        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        VoxelGrid unrelatedGrid = context.World.ActiveGrids[3];
        unrelatedGrid.TryGetVoxel(default(VoxelIndex), out Voxel? voxel).Should().BeTrue();
        unrelatedGrid.TryAddObstacle(voxel!, context.World.AllocateObstacleToken()).Should().BeTrue();
        long versionBeforeSafetyMaintenance = context.Pathing
            .GetNavigationGraphDiagnostics()
            .GraphVersion;

        context.Simulate();

        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        NavigationGraphCellState updated = GetCell(context, "U");
        updated.IsMaterialized.Should().BeTrue();
        updated.ObstacleCount.Should().Be(1);
        context.Pathing.GetNavigationGraphDiagnostics().GraphVersion
            .Should().Be(versionBeforeSafetyMaintenance + 1,
                "one maintenance boundary may publish at most one immutable root");

        SimulateUntilTerminal(context, removal.Receipt);

        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        NavigationGraphCellState retained = GetCell(context, "U");
        retained.IsMaterialized.Should().BeTrue();
        retained.ObstacleCount.Should().Be(1,
            "publishing older structural work must not revert reconciled GridForge state");
    }

    private static int RunBridgeRemoval()
    {
        using TrailblazerWorldContext context = CreateConnectedContext(out long sequence);
        NavigationOverlayCommitOperation removal = SuppressTransition(
            context,
            ++sequence,
            "B",
            "bc");
        int start = context.FrameCount;
        SimulateUntilTerminal(context, removal.Receipt);
        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        return removal.Receipt.PublishedFrame - start;
    }

    private static TrailblazerWorldContext CreateConnectedContext(out long sequence)
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var budget = new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            defaults.MaintenanceBudget.MaxOverlaySlots,
            defaults.MaintenanceBudget.MaxSeamCandidates,
            maxComponentNodes: 1,
            defaults.MaintenanceBudget.MaxImplicitEdges,
            maxExplicitEdges: 1,
            maxDependencyEntries: 3,
            defaults.MaintenanceBudget.MaxCacheInvalidations);
        var settings = new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            budget,
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
            maxAreaPolicies: 1,
            defaults.MaxAreaRulesPerPolicy,
            defaults.MaxAreaRules,
            defaults.MaxConcurrentPathQueries,
            defaults.MaxActiveWorkspaceBytes,
            defaults.MaxRetainedWorkspaceBytes,
            defaults.MaxActiveQueryResultBytes);
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(settings: settings);
        NavigationMap a = AddGridAndCreateMap(context, "A", 0, "B", "ab");
        NavigationMap b = AddGridAndCreateMap(context, "B", 10, "C", "bc");
        NavigationMap c = AddGridAndCreateMap(context, "C", 20, null, null);
        NavigationMap u = AddGridAndCreateMap(context, "U", 30, null, null);
        var operations = new[]
        {
            AdmitMap(context, a, 1),
            AdmitMap(context, b, 2),
            AdmitMap(context, c, 3),
            AdmitMap(context, u, 4)
        };
        for (int i = 0; i < 256 && operations[3].Receipt.Status == NavigationOperationStatus.Pending; i++)
            context.Simulate();
        operations.Should().OnlyContain(operation =>
            operation.Receipt.Status == NavigationOperationStatus.Applied);
        using (NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!)
        {
            foreach (string mapId in new[] { "A", "B", "C", "U" })
            {
                lease.Graph.Composition.TryGetComponentRecord(mapId, out _)
                    .Should().BeTrue($"{mapId} must have structural membership");
            }
        }
        sequence = 4;
        return context;
    }

    private static NavigationMap AddGridAndCreateMap(
        TrailblazerWorldContext context,
        string mapId,
        int origin,
        string? destinationMapId,
        string? transitionId)
    {
        var configuration = new GridConfiguration(
            new Vector3d(origin, 0, 0),
            new Vector3d(origin, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
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

    private static NormalizedGridConfiguration CreateBinding(int origin)
    {
        var configuration = new GridConfiguration(
            new Vector3d(origin, 0, 0),
            new Vector3d(origin, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        return binding;
    }

    private static TraversalTransitionDefinition CreateTransition(
        string id,
        string destinationMapId,
        Fixed64 additionalCost) => new(
        id,
        TraversalTransitionType.Climb,
        default,
        TraversalMedium.Solid,
        new NavigationCellAddress(destinationMapId, default),
        TraversalMedium.Solid,
        TraversalCapability.Climb,
        additionalCost);

    private static NavigationMapCommitOperation AdmitMap(
        TrailblazerWorldContext context,
        NavigationMap map,
        long sequence)
    {
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, 1),
            OverlayReplacementPolicy.Clear,
            sequence,
            1);
        context.Pathing.Admit(operation).Should().BeTrue();
        return operation;
    }

    private static NavigationOverlayCommitOperation SuppressTransition(
        TrailblazerWorldContext context,
        long sequence,
        string mapId,
        string transitionId)
    {
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(
                new NavigationOverlayTransaction(
                    new[]
                    {
                        new NavigationMapOverlayDelta(
                            mapId,
                            transitions: new[]
                            {
                                TraversalTransitionOverlayOperation.Suppress(transitionId)
                            })
                    })),
            sequence,
            context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        return operation;
    }

    private static void SimulateUntilTerminal(
        TrailblazerWorldContext context,
        NavigationOperationReceipt receipt)
    {
        for (int i = 0; i < 64 && receipt.Status == NavigationOperationStatus.Pending; i++)
            context.Simulate();
    }

    private static NavigationGraphCellState GetCell(
        TrailblazerWorldContext context,
        string mapId)
    {
        context.Pathing.TryGetNavigationGraphCellState(mapId, default, out NavigationGraphCellState state)
            .Should().BeTrue();
        return state;
    }

    private static NavigationGraphMapDiagnostic FindMap(
        NavigationGraphDiagnosticsSnapshot snapshot,
        string mapId)
    {
        foreach (NavigationGraphMapDiagnostic map in snapshot.Maps)
        {
            if (map.MapId == mapId)
                return map;
        }
        throw new Xunit.Sdk.XunitException($"Map '{mapId}' was not found.");
    }
}

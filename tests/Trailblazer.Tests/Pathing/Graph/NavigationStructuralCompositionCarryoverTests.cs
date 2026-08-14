using FixedMathSharp;
using System;
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
    public void ChangedMapCapture_ShouldNotScanBeforeMeteredAdvance()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        NavigationMap first = new NavigationMapBuilder("A", CreateBinding(0))
            .AddCell(default, Cell)
            .Build();
        NavigationMap second = new NavigationMapBuilder("B", CreateBinding(10))
            .AddCell(default, Cell)
            .Build();
        PreparedNavigationMap preparedFirst = new(first, 1);
        PreparedNavigationMap preparedSecond = new(second, 1);
        NavigationOperationCandidate candidate = FoldMap(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            preparedFirst,
            OverlayReplacementPolicy.Clear,
            defaults);
        candidate = FoldMap(
            candidate,
            preparedSecond,
            OverlayReplacementPolicy.Clear,
            defaults);
        NavigationOperationFrameChange[] changes =
        {
            NavigationOperationFrameChange.MapCommit(
                preparedFirst,
                OverlayReplacementPolicy.Clear,
                1),
            NavigationOperationFrameChange.MapCommit(
                preparedSecond,
                OverlayReplacementPolicy.Clear,
                2),
            NavigationOperationFrameChange.MapCommit(
                preparedFirst,
                OverlayReplacementPolicy.Clear,
                3)
        };

        var work = new NavigationStructuralCompositionWork(
            NavigationWorldGraph.Empty,
            candidate,
            changes,
            changes.Length,
            updateComposition: true,
            new NavigationCompositionWorkspace(defaults.OperationLimits.MaxMaps));

        work.CapturedChangedMapCount.Should().Be(0,
            "construction may retain sources but must not scan or copy changed map IDs");
        work.IsChangedMapCaptureComplete.Should().BeFalse();

        MaintenanceWorkBudget defaultsBudget = defaults.MaintenanceBudget;
        var budget = new MaintenanceWorkBudget(
            defaultsBudget.MaxConsumedEnvelopes,
            defaultsBudget.MaxBaselineAddresses,
            defaultsBudget.MaxOverlaySlots,
            maxComponentNodes: 1,
            defaultsBudget.MaxExplicitEdges,
            maxDependencyEntries: 2);
        var meter = new MaintenanceWorkMeter(budget);
        int componentUnits = 0;
        int dependencyUnits = 0;
        for (int frame = 0; frame < changes.Length; frame++)
        {
            work.Advance(meter).Should().BeFalse();
            componentUnits += meter.ComponentNodes;
            dependencyUnits += meter.DependencyEntries;
            meter.ComponentNodes.Should().Be(1,
                "each frame may inspect exactly one raw changed-map ID");
            meter.Reset();
        }

        work.IsChangedMapCaptureComplete.Should().BeTrue();
        work.CapturedChangedMapCount.Should().Be(2,
            "duplicate IDs must be canonicalized in the metered root");
        work.GetCapturedChangedMapIdAt(0).Should().Be("A");
        work.GetCapturedChangedMapIdAt(1).Should().Be("B");
        componentUnits.Should().Be(3);
        dependencyUnits.Should().Be(2,
            "only unique changed-root insertions consume dependency units when no prior component exists");
    }

    [Fact]
    public void AffectedClosurePublication_ShouldTransferPreparedRootOwnership()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NavigationMap map = AddGridAndCreateMap(context, "A", 0, null, null, null);
        NavigationMapCommitOperation install = AdmitMap(context, map, 1);
        SimulateUntilTerminal(context, install.Receipt);
        install.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var prepared = new PreparedNavigationMap(map, 2);
        NavigationOperationCandidate candidate = FoldMap(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            prepared,
            OverlayReplacementPolicy.Clear,
            defaults);
        NavigationOperationFrameChange[] changes =
        {
            NavigationOperationFrameChange.MapCommit(
                prepared,
                OverlayReplacementPolicy.Clear,
                2)
        };
        var work = new NavigationStructuralCompositionWork(
            lease.Graph,
            candidate,
            changes,
            changes.Length,
            updateComposition: true,
            new NavigationCompositionWorkspace(defaults.OperationLimits.MaxMaps));
        work.MarkAllClosePublished();

        var meter = new MaintenanceWorkMeter(defaults.MaintenanceBudget);
        work.Advance(meter).Should().BeFalse();
        work.RequiresAffectedClosurePublication.Should().BeTrue();
        PersistentStringMap<bool> affected = work.AffectedComponents;
        affected.Count.Should().Be(1);
        long ownedBytes = affected.RetainedBytes
            - PersistentStringMap<bool>.Empty.RetainedBytes;
        int ownedPages = affected.PersistentNodeCount;
        long retainedBytes = work.RetainedBytes;
        int retainedPages = work.PersistentPageCount;

        work.MarkAffectedClosurePublished();

        work.RetainedBytes.Should().Be(retainedBytes - ownedBytes,
            "the graph owns the exact affected-component root after publication");
        work.PersistentPageCount.Should().Be(retainedPages - ownedPages);
    }

    [Fact]
    public void ShrinkingCompositionUpdate_ShouldRetainReplacementPayloadSeparately()
    {
        const int MapCount = 16;
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        var maps = new NavigationMap[MapCount];
        NavigationMapCommitOperation last = default;
        NavigationOperationCandidate candidate = new(navigationAreaCount: 1);
        for (int i = 0; i < MapCount; i++)
        {
            string? destination = i + 1 < MapCount ? $"map-{i + 1:D2}" : null;
            maps[i] = AddGridAndCreateMap(
                context,
                $"map-{i:D2}",
                i * 3,
                destination,
                destination == null ? null : (i + 1) * 3,
                destination == null ? null : $"edge-{i:D2}");
            last = AdmitMap(context, maps[i], i + 1);
            candidate = FoldMap(
                candidate,
                new PreparedNavigationMap(maps[i], 1),
                OverlayReplacementPolicy.Clear,
                defaults);
        }
        SimulateUntilTerminal(context, last.Receipt);
        last.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        var changes = new NavigationOperationFrameChange[MapCount - 1];
        for (int i = 1; i < MapCount; i++)
        {
            candidate = FoldMapRemoval(candidate, maps[i].MapId, defaults);
            changes[i - 1] = NavigationOperationFrameChange.MapRemove(maps[i].MapId, i);
        }
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var preparationOnly = new NavigationStructuralCompositionWork(
            lease.Graph,
            candidate,
            changes,
            changes.Length,
            updateComposition: false,
            new NavigationCompositionWorkspace(MapCount));
        var withUpdate = new NavigationStructuralCompositionWork(
            lease.Graph,
            candidate,
            changes,
            changes.Length,
            updateComposition: true,
            new NavigationCompositionWorkspace(MapCount));
        var meter = new MaintenanceWorkMeter(defaults.MaintenanceBudget);
        for (int frame = 0; frame < 4096 && !preparationOnly.IsComplete; frame++)
        {
            preparationOnly.Advance(meter);
            meter.Reset();
        }
        for (int frame = 0; frame < 4096 && !withUpdate.IsComplete; frame++)
        {
            withUpdate.Advance(meter);
            meter.Reset();
        }

        preparationOnly.IsComplete.Should().BeTrue();
        withUpdate.IsComplete.Should().BeTrue();
        (withUpdate.RetainedBytes - preparationOnly.RetainedBytes).Should().Be(
            224,
            "the new singleton payload and positive non-payload delta coexist with the much larger source composition");
        (withUpdate.PersistentPageCount - preparationOnly.PersistentPageCount)
            .Should().Be(3);
    }

    [Fact]
    public void EmptyCompositionUpdate_ShouldNotSubtractPublishedObjectHeader()
    {
        var changes = Array.Empty<NavigationOperationFrameChange>();
        var candidate = new NavigationOperationCandidate(navigationAreaCount: 1);
        var preparationOnly = new NavigationStructuralCompositionWork(
            NavigationWorldGraph.Empty,
            candidate,
            changes,
            changeCount: 0,
            updateComposition: false,
            new NavigationCompositionWorkspace(1));
        var withUpdate = new NavigationStructuralCompositionWork(
            NavigationWorldGraph.Empty,
            candidate,
            changes,
            changeCount: 0,
            updateComposition: true,
            new NavigationCompositionWorkspace(1));
        var meter = new MaintenanceWorkMeter(
            TrailblazerWorldContextSettings.Default.MaintenanceBudget);
        preparationOnly.Advance(meter).Should().BeTrue();
        meter.Reset();
        withUpdate.Advance(meter).Should().BeTrue();

        (withUpdate.RetainedBytes - preparationOnly.RetainedBytes).Should().Be(
            192L + NavigationCompositionWorkspace.GetRetainedBytes(1),
            "the distinct update object and workspace coexist while only published roots are shared");
    }

    [Fact]
    public void BridgeRemoval_ShouldFailClosedOnlyAffectedComponentUntilAtomicPublication()
    {
        using TrailblazerWorldContext context = CreateConnectedContext(out long sequence);
        NavigationGraphDiagnosticsSnapshot before = context.Pathing.GetNavigationGraphDiagnostics();
        int priorComponent = FindMap(before, "A").ComponentId;
        FindMap(before, "C").ComponentId.Should().Be(priorComponent,
            "the installed explicit A-B-C bridge must begin as one component");
        var removal = SuppressConnection(context, ++sequence, "B", "bc");

        context.Simulate();

        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        (context.Pathing.RetainedCompositionWorkCount
            + context.Pathing.RetainedOperationWorkCount).Should().Be(1);
        context.Pathing.NavigationMaintenanceMeter.OverlaySlots.Should().BeGreaterThan(0);
        GetCell(context, "A").IsMaterialized.Should().BeFalse();
        GetCell(context, "B").IsMaterialized.Should().BeFalse();
        GetCell(context, "C").IsMaterialized.Should().BeFalse();
        GetCell(context, "U").IsMaterialized.Should().BeFalse(
            "explicit incidence is conservatively unknown before its metered gather completes");
        for (int i = 0;
             i < 64 && removal.Receipt.Status == NavigationOperationStatus.Pending
                 && !GetCell(context, "U").IsMaterialized;
             i++)
        {
            context.Simulate();
        }
        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        GetCell(context, "U").IsMaterialized.Should().BeTrue();
        NavigationGraphDiagnosticsSnapshot blocked = context.Pathing.GetNavigationGraphDiagnostics();
        NavigationGraphMapDiagnostic blockedA = FindMap(blocked, "A");
        NavigationGraphMapDiagnostic blockedC = FindMap(blocked, "C");
        blockedA.ComponentId.Should().Be(priorComponent,
            $"graph={blocked.GraphVersion}, beforeComponent={priorComponent}, "
            + $"A={blockedA.ComponentId}/{blockedA.ComponentVersion}, "
            + $"C={blockedC.ComponentId}/{blockedC.ComponentVersion}");
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
        var removal = SuppressConnection(context, ++sequence, "B", "bc");
        context.Simulate();
        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);

        NavigationMap laterMap = AddGridAndCreateMap(context, "V", 50, null, null, null);
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
        NavigationOverlayCommitOperation removal = SuppressConnection(
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
        NavigationMap a = AddGridAndCreateMap(context, "A", 0, "B", 3, "ab");
        NavigationMap b = AddGridAndCreateMap(context, "B", 3, null, null, null);
        NavigationMapCommitOperation first = AdmitMap(context, b, 1);
        NavigationMapCommitOperation second = AdmitMap(context, a, 2);

        context.Simulate();

        first.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        second.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        context.Pathing.RetainedCompositionWorkCount.Should().Be(0);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.Composition.TryGetComponentRecord("A", out NavigationStructuralComponent component)
            .Should().BeTrue();
        component.FlatMembers.Count.Should().Be(2);
        component.FlatMembers[0].Should().Be("A");
        component.FlatMembers[1].Should().Be("B");
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
        NavigationOverlayCommitOperation removal = SuppressConnection(
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
        NavigationOverlayCommitOperation removal = SuppressConnection(
            context,
            ++sequence,
            "B",
            "bc");
        context.Simulate();
        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        for (int i = 0;
             i < 64 && removal.Receipt.Status == NavigationOperationStatus.Pending
                 && !GetCell(context, "U").IsMaterialized;
             i++)
        {
            context.Simulate();
        }
        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        GetCell(context, "U").IsMaterialized.Should().BeTrue();
        VoxelGrid unrelatedGrid = context.World.ActiveGrids[3];
        unrelatedGrid.TryGetVoxel(default(VoxelIndex), out Voxel? voxel).Should().BeTrue();
        unrelatedGrid.TryAddObstacle(voxel!, context.World.AllocateObstacleToken()).Should().BeTrue();
        NavigationGraphDiagnosticsSnapshot beforeSafety = context.Pathing
            .GetNavigationGraphDiagnostics();
        long versionBeforeSafetyMaintenance = beforeSafety.GraphVersion;
        long componentVersionBeforeSafetyMaintenance = beforeSafety.Maps[3].ComponentVersion;
        long compositionVersionBeforeSafetyMaintenance;
        using (NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!)
            compositionVersionBeforeSafetyMaintenance = lease.Graph.Composition.Version;

        context.Simulate();

        NavigationGraphCellState updated = GetCell(context, "U");
        for (int i = 0;
             i < 64 && removal.Receipt.Status == NavigationOperationStatus.Pending
                 && updated.ObstacleCount == 0;
             i++)
        {
            context.Simulate();
            updated = GetCell(context, "U");
        }
        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        updated.IsMaterialized.Should().BeTrue();
        updated.ObstacleCount.Should().Be(1);
        NavigationGraphDiagnosticsSnapshot afterSafety = context.Pathing
            .GetNavigationGraphDiagnostics();
        afterSafety.GraphVersion
            .Should().Be(versionBeforeSafetyMaintenance + 1,
                "one maintenance boundary may publish at most one immutable root");
        afterSafety.Maps[3].ComponentVersion.Should()
            .BeGreaterThan(componentVersionBeforeSafetyMaintenance);
        long componentVersionAfterSafetyMaintenance = afterSafety.Maps[3].ComponentVersion;
        using (NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!)
            lease.Graph.Composition.Version.Should().Be(compositionVersionBeforeSafetyMaintenance);

        SimulateUntilTerminal(context, removal.Receipt);

        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        NavigationGraphCellState retained = GetCell(context, "U");
        retained.IsMaterialized.Should().BeTrue();
        retained.ObstacleCount.Should().Be(1,
            "publishing older structural work must not revert reconciled GridForge state");
        context.Pathing.GetNavigationGraphDiagnostics().Maps[3].ComponentVersion
            .Should().BeGreaterThanOrEqualTo(componentVersionAfterSafetyMaintenance,
                "publishing older structural work must not revert the physical component clock");
    }

    private static int RunBridgeRemoval()
    {
        using TrailblazerWorldContext context = CreateConnectedContext(out long sequence);
        NavigationOverlayCommitOperation removal = SuppressConnection(
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
            maxComponentNodes: 1,
            maxExplicitEdges: 1,
            maxDependencyEntries: 3);
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
            defaults.MaxConcurrentSnapshotLeases);
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(settings: settings);
        NavigationMap a = AddGridAndCreateMap(context, "A", 0, "B", 3, "ab");
        NavigationMap b = AddGridAndCreateMap(context, "B", 3, "C", 6, "bc");
        NavigationMap c = AddGridAndCreateMap(context, "C", 6, null, null, null);
        NavigationMap u = AddGridAndCreateMap(context, "U", 10, null, null, null);
        var operations = new[]
        {
            AdmitMap(context, c, 1),
            AdmitMap(context, b, 2),
            AdmitMap(context, a, 3),
            AdmitMap(context, u, 4)
        };
        for (int i = 0; i < 256 && operations[3].Receipt.Status == NavigationOperationStatus.Pending; i++)
            context.Simulate();
        operations.Should().OnlyContain(operation =>
            operation.Receipt.Status == NavigationOperationStatus.Applied);
        using (NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!)
        {
            lease.Graph.ExplicitConnections.TryGet(
                    new NavigationConnectionOwnerKey("A", "ab"),
                    out NavigationExplicitConnectionRecord ab)
                .Should().BeTrue();
            ab.IsActive.Should().BeTrue();
            lease.Graph.ExplicitConnections.TryGet(
                    new NavigationConnectionOwnerKey("B", "bc"),
                    out NavigationExplicitConnectionRecord bc)
                .Should().BeTrue();
            bc.IsActive.Should().BeTrue();
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
        int? destinationOrigin,
        string? transitionId)
    {
        var configuration = new GridConfiguration(
            new Vector3d(origin, 0, 0),
            new Vector3d(destinationMapId == null ? origin : origin + 2, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var builder = new NavigationMapBuilder(mapId, binding).AddCell(default, Cell);
        if (destinationMapId != null)
        {
            var sourceIndex = new VoxelIndex(2, 0, 0);
            builder.AddCell(sourceIndex, Cell);
            NormalizedGridConfiguration destinationBinding =
                CreateBinding(destinationOrigin!.Value);
            builder.AddConnection(new NavigationConnection(
                transitionId!,
                sourceIndex,
                new NavigationCellAddress(destinationMapId, default),
                GetFoot(binding, sourceIndex),
                GetFoot(destinationBinding, default),
                Fixed64.Zero,
                Fixed64.One));
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

    private static Vector3d GetFoot(
        NormalizedGridConfiguration binding,
        VoxelIndex index)
    {
        binding.TryGetCellPrism(index, out GridCellPrism prism).Should().BeTrue();
        return new Vector3d(prism.Center.X, prism.VerticalMin, prism.Center.Z);
    }

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

    private static NavigationOperationCandidate FoldMap(
        NavigationOperationCandidate source,
        PreparedNavigationMap prepared,
        OverlayReplacementPolicy replacementPolicy,
        TrailblazerWorldContextSettings settings)
    {
        int capacity = settings.OperationLimits.MaxCorridorCells;
        var work = new NavigationMapFoldWork(
            source,
            prepared,
            replacementPolicy,
            settings.OperationLimits,
            new GridCellPrism[capacity],
            new Vector3d[(capacity * 2) - 2],
            new NavigationCellAddress[capacity],
            new NavigationAddressStampSet(capacity));
        AdvanceFold(work, settings.MaintenanceBudget);
        return work.Candidate;
    }

    private static NavigationOperationCandidate FoldMapRemoval(
        NavigationOperationCandidate source,
        string mapId,
        TrailblazerWorldContextSettings settings)
    {
        int capacity = settings.OperationLimits.MaxCorridorCells;
        var work = new NavigationMapFoldWork(
            source,
            mapId,
            new GridCellPrism[capacity],
            new Vector3d[(capacity * 2) - 2],
            new NavigationCellAddress[capacity],
            new NavigationAddressStampSet(capacity));
        AdvanceFold(work, settings.MaintenanceBudget);
        return work.Candidate;
    }

    private static NavigationOperationCandidate FoldOverlay(
        NavigationOperationCandidate source,
        NavigationOverlayTransaction transaction,
        long sequence,
        TrailblazerWorldContextSettings settings)
    {
        int capacity = settings.OperationLimits.MaxCorridorCells;
        var work = new NavigationOverlayFoldWork(
            source,
            transaction,
            sequence,
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
                rejection.Should().Be(NavigationOperationRejection.None);
                return work.Candidate;
            }
            rejection.Should().Be(NavigationOperationRejection.None);
            meter.Reset();
        }
        throw new Xunit.Sdk.XunitException("Overlay fold did not complete.");
    }

    private static void AdvanceFold(
        NavigationMapFoldWork work,
        MaintenanceWorkBudget budget)
    {
        var meter = new MaintenanceWorkMeter(budget);
        for (int i = 0; i < 4096; i++)
        {
            if (work.Advance(meter, out NavigationOperationRejection rejection))
            {
                rejection.Should().Be(NavigationOperationRejection.None);
                return;
            }
            rejection.Should().Be(NavigationOperationRejection.None);
            meter.Reset();
        }
        throw new Xunit.Sdk.XunitException("Map fold did not complete.");
    }

    private static NavigationOverlayCommitOperation SuppressConnection(
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
                            connections: new[]
                            {
                                NavigationConnectionOverlayOperation.Suppress(transitionId)
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

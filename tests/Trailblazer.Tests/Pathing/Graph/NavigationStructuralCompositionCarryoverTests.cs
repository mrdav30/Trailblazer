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

    [Theory]
    [InlineData(false, null, null, 0, 0, null, false)]
    [InlineData(true, null, true, 0, 0, null, false)]
    [InlineData(true, false, true, 0, 0, null, false)]
    [InlineData(true, true, null, 0, 0, null, false)]
    [InlineData(true, true, false, 0, 0, null, false)]
    [InlineData(true, true, true, 0, 0, null, true)]
    [InlineData(true, true, true, 1, 0, null, false)]
    [InlineData(true, true, true, 0, 1, false, false)]
    [InlineData(true, true, true, 1, 1, true, true)]
    public void StructuralCompletionPolicy_ShouldRequirePreparationAndAffectedRecomposition(
        bool captureComplete,
        bool? preparationComplete,
        bool? seamRefreshComplete,
        int affectedComponentCount,
        int affectedAddressCount,
        bool? componentUpdateComplete,
        bool expected)
    {
        NavigationStructuralCompositionWork.IsLifecycleComplete(
                captureComplete,
                preparationComplete,
                seamRefreshComplete,
                affectedComponentCount,
                affectedAddressCount,
                componentUpdateComplete)
            .Should().Be(expected);
    }

    [Fact]
    public void CompositionPageCeilings_ShouldRejectEachMeasuredGrowthAndReopenOwnedClosure()
    {
        (GridWorld probeWorld, NavigationGraphRuntime probe, NavigationOperationReceipt probeReceipt) =
            CreateCompositionPageScenario(
                TrailblazerWorldContextSettings.Default.MaxPersistentGraphPages);
        using (probeWorld)
        using (probe)
        {
            int priorPages = -1;
            int priorRootPages = -1;
            int priorOperationPages = -1;
            int priorCompositionPages = -1;
            int priorBaselinePages = -1;
            bool priorOwnedComposition = false;
            int firstPreGrowthBoundary = 0;
            int finalPreGrowthBoundary = 0;
            for (int frame = 1;
                 frame < 512 && probeReceipt.Status == NavigationOperationStatus.Pending;
                 frame++)
            {
                probe.Maintain(frame);
                NavigationGraphDiagnosticsSnapshot retained = probe.GetDiagnostics(0);
                int retainedPages = retained.PersistentGraphPageCount;
                bool ownsComposition = probe.RetainedCompositionWorkCount == 1;
                if (priorOwnedComposition
                    && ownsComposition
                    && priorPages >= TrailblazerWorldContextSettings.MinimumPersistentGraphPages
                    && probe.Current.PersistentPageCount == priorRootPages
                    && probe.RetainedOperationWorkPageCount == priorOperationPages
                    && retained.BaselineRebuildPageCount == priorBaselinePages
                    && probe.RetainedCompositionWorkPageCount > priorCompositionPages)
                {
                    if (firstPreGrowthBoundary == 0)
                        firstPreGrowthBoundary = priorPages;
                    finalPreGrowthBoundary = priorPages;
                }
                priorPages = retainedPages;
                priorRootPages = probe.Current.PersistentPageCount;
                priorOperationPages = probe.RetainedOperationWorkPageCount;
                priorCompositionPages = probe.RetainedCompositionWorkPageCount;
                priorBaselinePages = retained.BaselineRebuildPageCount;
                priorOwnedComposition = ownsComposition;
            }
            firstPreGrowthBoundary.Should().BePositive(
                "the bounded structural cursor must expose an initial retained-page growth step");
            finalPreGrowthBoundary.Should().BeGreaterThan(firstPreGrowthBoundary,
                "the bounded structural cursor must expose a final retained-page growth step");

            foreach (int exactPreGrowthBoundary in new[]
                {
                    firstPreGrowthBoundary,
                    finalPreGrowthBoundary
                })
            {
                (GridWorld constrainedWorld,
                    NavigationGraphRuntime constrained,
                    NavigationOperationReceipt constrainedReceipt) =
                    CreateCompositionPageScenario(exactPreGrowthBoundary);
                using (constrainedWorld)
                using (constrained)
                {
                    bool observedOwnedClosure = false;
                    int maximumRetainedPages = 0;
                    int frame = 1;
                    for (;
                         frame < 512
                             && constrainedReceipt.Status == NavigationOperationStatus.Pending;
                         frame++)
                    {
                        constrained.Maintain(frame);
                        using NavigationWorldGraphLease? lease = constrained.TryAcquire();
                        observedOwnedClosure |= lease?.Graph.HasClosedStructuralScope ?? false;
                        int retainedPages = constrained.GetDiagnostics(0).PersistentGraphPageCount;
                        retainedPages.Should().BeLessThanOrEqualTo(exactPreGrowthBoundary);
                        maximumRetainedPages = System.Math.Max(maximumRetainedPages, retainedPages);
                    }

                    observedOwnedClosure.Should().BeTrue(
                        "structural membership must fail closed while its bounded composition is incomplete");
                    maximumRetainedPages.Should().Be(exactPreGrowthBoundary,
                        "the cursor must reach the measured inclusive boundary before its next page is rejected");
                    constrainedReceipt.Status.Should().Be(NavigationOperationStatus.Rejected);
                    constrainedReceipt.Rejection.Should().Be(
                        NavigationOperationRejection.CapacityExceeded);
                    constrained.RetainedOperationWorkCount.Should().Be(0);
                    constrained.RetainedCompositionWorkCount.Should().Be(0);
                    for (int remaining = 64; remaining > 0; remaining--)
                    {
                        bool isClosed;
                        using (NavigationWorldGraphLease current = constrained.TryAcquire()!)
                            isClosed = current.Graph.HasClosedStructuralScope;
                        if (!isClosed)
                            break;
                        constrained.Maintain(frame++);
                    }
                    using NavigationWorldGraphLease published = constrained.TryAcquire()!;
                    published.Graph.HasClosedStructuralScope.Should().BeFalse(
                        "the next maintenance pass must restore the pre-operation scope after terminal rejection releases its owner");
                    published.Graph.TryGetMap("probe", out _).Should().BeFalse();
                }
            }
        }
    }

    [Fact]
    public void InitialCompositionPageCeilingSweep_ShouldRemainBoundedAndAtomic()
    {
        int rejectedCeilings = 0;
        int appliedCeiling = 0;
        int minimum = TrailblazerWorldContextSettings.MinimumPersistentGraphPages;
        for (int ceiling = minimum;
             ceiling <= minimum + 256 && appliedCeiling == 0;
             ceiling++)
        {
            (GridWorld world, NavigationGraphRuntime runtime, NavigationOperationReceipt receipt) =
                CreateCompositionPageScenario(ceiling, useBoundedWorkBudget: false);
            using (world)
            using (runtime)
            {
                int frame = 1;
                for (; frame < 128 && receipt.Status == NavigationOperationStatus.Pending; frame++)
                {
                    runtime.Maintain(frame);
                    runtime.GetDiagnostics(0).PersistentGraphPageCount
                        .Should().BeLessThanOrEqualTo(ceiling);
                }

                if (receipt.Status == NavigationOperationStatus.Applied)
                {
                    appliedCeiling = ceiling;
                    runtime.TryGetCellState("probe", default, out NavigationGraphCellState cell)
                        .Should().BeTrue();
                    cell.HasCell.Should().BeTrue();
                    continue;
                }

                receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
                receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
                rejectedCeilings++;
                runtime.TryGetCellState("probe", default, out _).Should().BeFalse(
                    "no page ceiling may publish a partial initial map");
                for (int remaining = 16; remaining > 0; remaining--)
                {
                    bool isClosed;
                    using (NavigationWorldGraphLease current = runtime.TryAcquire()!)
                        isClosed = current.Graph.HasClosedStructuralScope;
                    if (!isClosed)
                        break;
                    runtime.Maintain(frame++);
                }
                using NavigationWorldGraphLease published = runtime.TryAcquire()!;
                published.Graph.HasClosedStructuralScope.Should().BeFalse();
            }
        }

        rejectedCeilings.Should().BePositive();
        appliedCeiling.Should().BePositive(
            "the bounded sweep must cross from atomic rejection to the first sufficient page ceiling");
    }

    [Fact]
    public void InitialClosure_ShouldRetainConcurrentPhysicalResnapshotWithoutDuplicateFallback()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var budget = new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            maxOverlaySlots: 1,
            maxComponentNodes: 1,
            defaults.MaintenanceBudget.MaxSeamCandidateProbes,
            defaults.MaintenanceBudget.MaxExplicitEdges,
            defaults.MaintenanceBudget.MaxDependencyEntries);
        var settings = new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            budget,
            defaults.GuideSampleBudget,
            defaults.MovementGroupPadding,
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
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: settings);
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(1, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        context.World.TryAddGrid(
                configuration,
                new[] { default(VoxelIndex), new VoxelIndex(1, 0, 0) },
                out ushort gridIndex)
            .Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var install = new NavigationMapCommitOperation(
            new PreparedNavigationMap(
                new NavigationMapBuilder("map", binding)
                    .SetDefaultCell(Cell)
                    .Build(),
                bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 1);
        context.Pathing.Admit(install).Should().BeTrue();
        SimulateUntilTerminal(context, install.Receipt);
        install.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        bool materialized = false;
        for (int frame = 0; frame < 64; frame++)
        {
            materialized = context.Pathing.TryGetNavigationGraphCellState(
                    "map",
                    default,
                    out NavigationGraphCellState settled)
                && settled.IsMaterialized;
            if (materialized && context.Pathing.RetainedCompositionWorkCount == 0)
                break;
            context.Simulate();
        }
        materialized.Should().BeTrue();

        var overlay = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(
                new[]
                {
                    new NavigationMapOverlayDelta(
                        "map",
                        new[]
                        {
                            NavigationCellOverlayOperation.Suppress(default),
                            NavigationCellOverlayOperation.Suppress(new VoxelIndex(1, 0, 0))
                        })
                })),
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(overlay).Should().BeTrue();
        context.Simulate();
        overlay.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        context.Pathing.RetainedOperationWorkCount.Should().Be(1);
        context.Pathing.RetainedCompositionWorkCount.Should().Be(0);

        VoxelGrid grid = context.World.ActiveGrids[gridIndex];
        grid.TryRemoveVoxel(default).Should().BeTrue();
        long versionBeforePhysicalClosure = context.Pathing
            .GetNavigationGraphDiagnostics().GraphVersion;
        context.Simulate();

        overlay.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        context.Pathing.RetainedOperationWorkCount.Should().Be(1);
        context.Pathing.RetainedCompositionWorkCount.Should().Be(1);
        context.Pathing.NavigationMaintenanceMeter.ConsumedEnvelopes.Should().Be(1,
            "the retained operation's initial closure must own the concurrent physical event");
        using (NavigationWorldGraphLease retained = context.Pathing.TryAcquireNavigationGraph()!)
        {
            retained.Graph.AreAllStructuralComponentsClosed.Should().BeTrue();
            retained.Graph.GraphVersion.Should().Be(versionBeforePhysicalClosure + 1,
                "the retained physical owner publishes the requested closure once without a fallback publication");
        }

        SimulateUntilTerminal(context, overlay.Receipt);
        overlay.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
    }

    [Fact]
    public void CompletedComposition_ShouldRejectPostSnapshotDynamicSlotOverflowAtomically()
    {
        int framesFromCompositionToCompletion = 0;
        using (TrailblazerWorldContext probe = CreatePostCompositionDynamicSlotScenario(
            out NavigationOverlayCommitOperation probeOperation,
            out _,
            out _))
        {
            SimulateUntilCompositionRetained(probe, probeOperation.Receipt);
            for (int frame = 1;
                 frame <= 512 && probeOperation.Receipt.Status == NavigationOperationStatus.Pending;
                 frame++)
            {
                probe.Simulate();
                framesFromCompositionToCompletion = frame;
            }
            probeOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        }
        framesFromCompositionToCompletion.Should().BeGreaterThan(1,
            "the one-node composition budget must expose a deterministic final-publication predecessor");

        var newAddress = new VoxelIndex(8, 0, 0);
        int rejectionOffset = -1;
        for (int offset = 0;
             offset < framesFromCompositionToCompletion && rejectionOffset < 0;
             offset++)
        {
            using TrailblazerWorldContext offsetProbe =
                CreatePostCompositionDynamicSlotScenario(
                    out NavigationOverlayCommitOperation offsetOperation,
                    out VoxelGrid offsetGrid,
                    out _);
            SimulateUntilCompositionRetained(offsetProbe, offsetOperation.Receipt);
            for (int frame = 0;
                 frame < offset
                     && offsetOperation.Receipt.Status == NavigationOperationStatus.Pending;
                 frame++)
            {
                offsetProbe.Simulate();
            }
            if (offsetOperation.Receipt.Status != NavigationOperationStatus.Pending)
                continue;
            offsetGrid.TryAddVoxel(newAddress, out _).Should().BeTrue();
            SimulateUntilTerminal(offsetProbe, offsetOperation.Receipt);
            if (offsetOperation.Receipt.Status == NavigationOperationStatus.Rejected)
            {
                offsetOperation.Receipt.Rejection.Should().Be(
                    NavigationOperationRejection.CapacityExceeded);
                rejectionOffset = offset;
            }
        }
        rejectionOffset.Should().BeGreaterThanOrEqualTo(0,
            "one deterministic retained-composition boundary must reconcile the ninth physical slot with final publication");

        using TrailblazerWorldContext context = CreatePostCompositionDynamicSlotScenario(
            out NavigationOverlayCommitOperation operation,
            out VoxelGrid grid,
            out ushort lifecycleGridIndex);
        SimulateUntilCompositionRetained(context, operation.Receipt);
        for (int frame = 0;
             frame < rejectionOffset
                 && operation.Receipt.Status == NavigationOperationStatus.Pending;
             frame++)
        {
            context.Simulate();
        }

        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);

        grid.TryAddVoxel(newAddress, out _).Should().BeTrue();
        context.World.TryRemoveGrid(lifecycleGridIndex).Should().BeTrue();
        SimulateUntilTerminal(context, operation.Receipt);

        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        operation.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        context.Pathing.TryAcquireNavigationGraph().Should().BeNull(
            "the unpublishable all-close rebuild must block readers of the affected-only stale topology");
        grid.TryRemoveVoxel(newAddress).Should().BeTrue();

        NavigationWorldGraphLease? recovered = null;
        for (int frame = 0; frame < 512 && recovered == null; frame++)
        {
            context.Simulate();
            NavigationWorldGraphLease? candidate =
                context.Pathing.TryAcquireNavigationGraph();
            if (candidate?.Graph.HasClosedStructuralScope ?? false)
            {
                candidate.Dispose();
                continue;
            }
            recovered = candidate;
        }

        using (recovered)
        {
            recovered.Should().NotBeNull(
                "the exact topology rebuild and physical resnapshot must eventually reopen admission");
            recovered!.Graph.HasClosedStructuralScope.Should().BeFalse();
        }
        context.Pathing.TryGetNavigationGraphCellState(
                "map",
                default,
                out NavigationGraphCellState retained)
            .Should().BeTrue();
        retained.HasCell.Should().BeTrue(
            "a capacity-rejected suppression must not leak into the published graph");
        context.Pathing.TryGetNavigationGraphCellState("physical", newAddress, out _)
            .Should().BeFalse(
                "the ninth default-backed lifetime slot must never publish past the configured ceiling");
    }

    [Fact]
    public void ConcurrentGridChangeCapacityRejection_ShouldStayClosedUntilPhysicalResnapshot()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var budget = new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            maxOverlaySlots: 1,
            defaults.MaintenanceBudget.MaxComponentNodes,
            defaults.MaintenanceBudget.MaxSeamCandidateProbes,
            defaults.MaintenanceBudget.MaxExplicitEdges,
            defaults.MaintenanceBudget.MaxDependencyEntries);
        var settings = new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            budget,
            defaults.GuideSampleBudget,
            defaults.MovementGroupPadding,
            defaults.MaxIngressEntries,
            defaults.MaxIngressBytes,
            defaults.MaxActiveSnapshots,
            defaults.MaxActiveSnapshotBytes,
            defaults.MaxRetiredSnapshots,
            defaults.MaxRetiredSnapshotBytes,
            maxPersistentGraphPages: 98,
            defaults.MaxDynamicCellSlotsPerMap,
            defaults.MaxDynamicCellSlots,
            defaults.NavigationAreaCount,
            maxAreaPolicies: 1,
            defaults.MaxAreaRulesPerPolicy,
            defaults.MaxAreaRules,
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(settings: settings);
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(7, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(configuration, out ushort gridIndex).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var builder = new NavigationMapBuilder("existing", binding);
        for (int x = 0; x < 8; x++)
            builder.AddCell(new VoxelIndex(x, 0, 0), Cell);
        var mapOperation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(builder.Build(), 1),
            OverlayReplacementPolicy.Clear,
            1,
            1);
        context.Pathing.Admit(mapOperation).Should().BeTrue();
        SimulateUntilTerminal(context, mapOperation.Receipt);
        mapOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        var cells = new NavigationCellOverlayOperation[8];
        for (int x = 0; x < cells.Length; x++)
            cells[x] = NavigationCellOverlayOperation.Suppress(new VoxelIndex(x, 0, 0));
        var overlay = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(
                new[] { new NavigationMapOverlayDelta("existing", cells) })),
            2,
            context.FrameCount + 1);
        context.Pathing.Admit(overlay).Should().BeTrue();
        context.Simulate();
        overlay.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        context.Pathing.RetainedOperationWorkCount.Should().Be(1);
        using (NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!)
            lease.Graph.HasClosedStructuralScope.Should().BeTrue();
        VoxelGrid grid = context.World.ActiveGrids[gridIndex];
        for (int x = 0; x < 8; x++)
        {
            grid.TryGetVoxel(new VoxelIndex(x, 0, 0), out Voxel? voxel).Should().BeTrue();
            grid.TryAddObstacle(voxel!, context.World.AllocateObstacleToken()).Should().BeTrue();
        }

        context.Simulate();

        overlay.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        overlay.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);
        context.Pathing.RetainedOperationWorkCount.Should().Be(0);
        context.Pathing.RetainedCompositionWorkCount.Should().Be(0);
        using (NavigationWorldGraphLease rejected = context.Pathing.TryAcquireNavigationGraph()!)
            rejected.Graph.HasClosedStructuralScope.Should().BeTrue(
                "committed physical changes are still queued for an exact resnapshot");
        context.Pathing.TryGetNavigationGraphCellState(
                "existing",
                default,
                out NavigationGraphCellState closed)
            .Should().BeTrue();
        closed.IsMaterialized.Should().BeFalse(
            "the stale zero-obstacle snapshot must not become traversable during rollback");

        for (int frame = 0; frame < 64; frame++)
        {
            context.Simulate();
            context.Pathing.TryGetNavigationGraphCellState(
                    "existing",
                    default,
                    out NavigationGraphCellState candidate)
                .Should().BeTrue();
            if (candidate.IsMaterialized && candidate.ObstacleCount == 1)
                break;
        }

        context.Pathing.TryGetNavigationGraphCellState(
                "existing",
                default,
                out NavigationGraphCellState recovered)
            .Should().BeTrue();
        recovered.IsMaterialized.Should().BeTrue();
        recovered.ObstacleCount.Should().Be(1);
        recovered.HasCell.Should().BeTrue(
            "the capacity-rejected suppression overlay must not leak into the published map");
        using NavigationWorldGraphLease published = context.Pathing.TryAcquireNavigationGraph()!;
        published.Graph.HasClosedStructuralScope.Should().BeFalse();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RejectedRetainedOperation_ShouldRestoreOrLeaveFullRebuildClosureOwned(
        bool topologyFullRebuildPending)
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var budget = new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            maxOverlaySlots: 1,
            defaults.MaintenanceBudget.MaxComponentNodes,
            defaults.MaintenanceBudget.MaxSeamCandidateProbes,
            defaults.MaintenanceBudget.MaxExplicitEdges,
            defaults.MaintenanceBudget.MaxDependencyEntries);
        var settings = new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            budget,
            defaults.GuideSampleBudget,
            defaults.MovementGroupPadding,
            defaults.MaxIngressEntries,
            defaults.MaxIngressBytes,
            defaults.MaxActiveSnapshots,
            defaults.MaxActiveSnapshotBytes,
            maxRetiredSnapshots: 0,
            maxRetiredSnapshotBytes: 0,
            defaults.MaxPersistentGraphPages,
            defaults.MaxDynamicCellSlotsPerMap,
            defaults.MaxDynamicCellSlots,
            defaults.NavigationAreaCount,
            maxAreaPolicies: 1,
            defaults.MaxAreaRulesPerPolicy,
            defaults.MaxAreaRules,
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: settings);
        var mappedConfiguration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(7, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(mappedConfiguration, out _).Should().BeTrue();
        ushort lifecycleGridIndex = 0;
        if (topologyFullRebuildPending)
        {
            var lifecycleConfiguration = new GridConfiguration(
                new Vector3d(10, 0, 0),
                new Vector3d(10, 0, 0),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
                storageKind: GridStorageKind.Dense);
            context.World.TryAddGrid(lifecycleConfiguration, out lifecycleGridIndex)
                .Should().BeTrue();
        }
        mappedConfiguration.TryNormalize(out NormalizedGridConfiguration binding)
            .Should().BeTrue();
        var builder = new NavigationMapBuilder("map", binding);
        for (int x = 0; x < 8; x++)
            builder.AddCell(new VoxelIndex(x, 0, 0), Cell);
        var install = new NavigationMapCommitOperation(
            new PreparedNavigationMap(builder.Build(), 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 1);
        context.Pathing.Admit(install).Should().BeTrue();
        SimulateUntilTerminal(context, install.Receipt);
        install.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        var changes = new NavigationCellOverlayOperation[9];
        for (int x = 0; x < 8; x++)
            changes[x] = NavigationCellOverlayOperation.Suppress(new VoxelIndex(x, 0, 0));
        changes[8] = NavigationCellOverlayOperation.Suppress(new VoxelIndex(8, 0, 0));
        var rejected = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(
                new[] { new NavigationMapOverlayDelta("map", changes) })),
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(rejected).Should().BeTrue();
        context.Simulate();
        rejected.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        context.Pathing.RetainedOperationWorkCount.Should().Be(1);

        if (topologyFullRebuildPending)
        {
            using NavigationWorldGraphLease pressure = context.Pathing.TryAcquireNavigationGraph()!;
            pressure.Graph.HasClosedStructuralScope.Should().BeTrue();
            context.World.TryRemoveGrid(lifecycleGridIndex).Should().BeTrue();
            context.Simulate();
            context.Pathing.RetainedOperationWorkCount.Should().Be(1,
                "writer pressure drains topology ownership without releasing the folded operation");
        }

        SimulateUntilTerminal(context, rejected.Receipt);
        rejected.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        rejected.Receipt.Rejection.Should().Be(NavigationOperationRejection.ValidationFailed);
        context.Pathing.TryGetNavigationGraphCellState(
                "map",
                default,
                out NavigationGraphCellState retained)
            .Should().BeTrue();
        retained.HasCell.Should().BeTrue(
            "the invalid atomic overlay cannot leak its earlier suppressed prefix");
        for (int frame = 0; frame < 64; frame++)
        {
            context.Simulate();
            using NavigationWorldGraphLease? probe = context.Pathing.TryAcquireNavigationGraph();
            if (probe != null && !probe.Graph.HasClosedStructuralScope)
                break;
        }
        using NavigationWorldGraphLease published = context.Pathing.TryAcquireNavigationGraph()!;
        published.Graph.HasClosedStructuralScope.Should().BeFalse();
    }

    [Fact]
    public void DormantMapCommit_ShouldRemainAllClosedUntilBudgetedCompositionCompletes()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var budget = new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            defaults.MaintenanceBudget.MaxOverlaySlots,
            maxComponentNodes: 1,
            maxSeamCandidateProbes: 1,
            maxExplicitEdges: 1,
            defaults.MaintenanceBudget.MaxDependencyEntries);
        var settings = new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            budget,
            defaults.GuideSampleBudget,
            defaults.MovementGroupPadding,
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
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: settings);
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(
                new NavigationMapBuilder("dormant", binding)
                    .AddCell(default, Cell)
                    .Build(),
                bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 1);
        context.Pathing.Admit(operation).Should().BeTrue();

        context.Simulate();

        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        context.Pathing.RetainedCompositionWorkCount.Should().Be(1);
        using (NavigationWorldGraphLease safety = context.Pathing.TryAcquireNavigationGraph()!)
        {
            safety.Graph.MapCount.Should().Be(0);
            safety.Graph.AreAllStructuralComponentsClosed.Should().BeTrue(
                "even an empty published root must remain fail-closed while its map composition carries");
        }

        SimulateUntilTerminal(context, operation.Receipt);

        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        context.Pathing.RetainedCompositionWorkCount.Should().Be(0);
        using NavigationWorldGraphLease published = context.Pathing.TryAcquireNavigationGraph()!;
        published.Graph.TryGetMap("dormant", out NavigationMapInstance? dormant).Should().BeTrue();
        dormant!.IsMaterialized.Should().BeFalse();
        published.Graph.HasClosedStructuralScope.Should().BeFalse();
    }

    [Fact]
    public void MinimumScratch_ShouldReserveTheUnconditionalSeamRefreshShell()
    {
        const int SourceMaps = 2;
        const int CandidateMaps = 3;
        const int ChangedMaps = 1;
        const long OverlayCells = 0;
        NavigationStructuralCompositionWork.GetMinimumScratchBytes(
                SourceMaps,
                CandidateMaps,
                ChangedMaps,
                OverlayCells)
            .Should().Be(
                256L
                + (CandidateMaps * 256L)
                + NavigationAutomaticSeamRefreshWork.FixedRetainedBytes);
        NavigationStructuralCompositionWork.GetMinimumScratchPages(
                CandidateMaps,
                OverlayCells)
            .Should().Be(2 + (CandidateMaps * 2) + 4);
    }

    [Fact]
    public void StructuralGrowth_ShouldRejectEachExactPriorRetainedByteCeilingAndResume()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        using TrailblazerWorldContext context = CreateConnectedContext(out long sequence);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        NavigationOperationCandidate candidate = new(navigationAreaCount: 1);
        NavigationMapInstance changed = lease.Graph.GetInstance(0);
        for (int mapOrdinal = 0; mapOrdinal < lease.Graph.MapCount; mapOrdinal++)
        {
            NavigationMapInstance instance = lease.Graph.GetInstance(mapOrdinal);
            long bakeVersion = instance.MapId == changed.MapId
                ? instance.BakeVersion + 1
                : instance.BakeVersion;
            candidate = FoldMap(
                candidate,
                new PreparedNavigationMap(instance.Map, bakeVersion),
                OverlayReplacementPolicy.Clear,
                defaults);
        }
        var changedMap = new PreparedNavigationMap(changed.Map, changed.BakeVersion + 1);
        NavigationOperationFrameChange[] changes =
        {
            NavigationOperationFrameChange.MapCommit(
                changedMap,
                OverlayReplacementPolicy.Clear,
                ++sequence)
        };
        var work = new NavigationStructuralCompositionWork(
            context.World,
            lease.Graph,
            candidate,
            changes,
            changes.Length);
        var meter = new MaintenanceWorkMeter(defaults.MaintenanceBudget);
        int capacityStops = 0;

        for (int step = 0; step < 4_096 && !work.IsComplete; step++)
        {
            long exactPriorBytes = work.RetainedBytes;
            work.Advance(
                meter,
                exactPriorBytes,
                int.MaxValue,
                out bool capacityExceeded);
            if (capacityExceeded)
            {
                capacityStops++;
                work.RetainedBytes.Should().BeGreaterThan(exactPriorBytes,
                    "every rejection follows a newly retained deterministic work root");
            }
            meter.Reset();
        }

        work.IsComplete.Should().BeTrue(
            "accepting each newly observed exact ceiling must resume the same structural cursor");
        capacityStops.Should().BeGreaterThan(1,
            "preparation and automatic-seam journal growth are independently capacity checked");
    }

    [Fact]
    public void HighBudgetSeamRefresh_ShouldRejectAtOneByteBelowItsInternalPeak()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        using var world = new GridWorld();
        GridTopologyMetrics sourceMetrics = GridTopologyMetrics.Rectangular((Fixed64)2);
        GridTopologyMetrics targetMetrics = GridTopologyMetrics.Rectangular(
            (Fixed64)2,
            (Fixed64)2,
            Fixed64.One);
        var sourceConfiguration = new GridConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: sourceMetrics,
            storageKind: GridStorageKind.Dense);
        var targetMinimum = new Vector3d((Fixed64)2, Fixed64.Zero, -Fixed64.One);
        var targetMaximum = new Vector3d((Fixed64)2, Fixed64.Zero, Fixed64.Zero);
        var targetConfiguration = new GridConfiguration(
            targetMinimum,
            targetMaximum,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: targetMetrics,
            storageKind: GridStorageKind.Dense);
        world.TryAddGrid(sourceConfiguration, out _).Should().BeTrue();
        world.TryAddGrid(targetConfiguration, out _).Should().BeTrue();
        sourceConfiguration.TryNormalize(out NormalizedGridConfiguration sourceBinding)
            .Should().BeTrue();
        targetConfiguration.TryNormalize(out NormalizedGridConfiguration targetBinding)
            .Should().BeTrue();
        PreparedNavigationMap source = new(
            new NavigationMapBuilder("source", sourceBinding)
                .AddCell(default, Cell)
                .Build(),
            1);
        PreparedNavigationMap target = new(
            new NavigationMapBuilder("target", targetBinding)
                .AddCell(default, Cell)
                .AddCell(new VoxelIndex(0, 0, 1), Cell)
                .Build(),
            1);
        NavigationOperationCandidate candidate = FoldMap(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            source,
            OverlayReplacementPolicy.Clear,
            defaults);
        candidate = FoldMap(
            candidate,
            target,
            OverlayReplacementPolicy.Clear,
            defaults);
        NavigationOperationFrameChange[] changes =
        {
            NavigationOperationFrameChange.MapCommit(
                source,
                OverlayReplacementPolicy.Clear,
                1),
            NavigationOperationFrameChange.MapCommit(
                target,
                OverlayReplacementPolicy.Clear,
                2)
        };

        const long exactPeak = 23_212L;
        const long oneBelowPeak = 23_211L;

        RunSeamWork(world, candidate, changes, exactPeak, out bool exactExceeded)
            .Should().BeTrue();
        exactExceeded.Should().BeFalse();
        RunSeamWork(world, candidate, changes, oneBelowPeak, out bool belowExceeded)
            .Should().BeFalse();
        belowExceeded.Should().BeTrue(
            "the exact peak includes the direct-discovery root, strongly held graph roots, "
            + "and two shared seam portals times the approved 32-byte certificate expansion; "
            + "one byte below cannot retain that boundary");
    }

    [Fact]
    public void ChangedMapCapture_ShouldNotScanBeforeMeteredAdvance()
    {
        using var world = new GridWorld();
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
            world,
            NavigationWorldGraph.Empty,
            candidate,
            changes,
            changes.Length);

        work.CapturedChangedMapCount.Should().Be(0,
            "construction may retain sources but must not scan or copy changed map IDs");
        work.IsChangedMapCaptureComplete.Should().BeFalse();

        MaintenanceWorkBudget defaultsBudget = defaults.MaintenanceBudget;
        var budget = new MaintenanceWorkBudget(
            defaultsBudget.MaxConsumedEnvelopes,
            defaultsBudget.MaxBaselineAddresses,
            defaultsBudget.MaxOverlaySlots,
            maxComponentNodes: 1,
            maxSeamCandidateProbes: defaultsBudget.MaxSeamCandidateProbes,
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

        work.IsChangedMapCaptureComplete.Should().BeFalse(
            "operation IDs are canonical before the bounded automatic-seam capture completes");
        work.CapturedChangedMapCount.Should().Be(2,
            "duplicate IDs must be canonicalized in the metered root");
        work.GetCapturedChangedMapIdAt(0).Should().Be("A");
        work.GetCapturedChangedMapIdAt(1).Should().Be("B");
        componentUnits.Should().Be(3);
        dependencyUnits.Should().Be(4,
            "each changed map and each exact authored seed is inserted under the dependency budget");
        for (int frame = 0; frame < 512 && !work.IsChangedMapCaptureComplete; frame++)
        {
            meter.Reset();
            work.Advance(meter);
        }
        work.IsChangedMapCaptureComplete.Should().BeTrue();
    }

    [Fact]
    public void MediaExpansion_ShouldRespectOneEntryDependencyBudgetAndPublishEveryMedium()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NavigationMap map = AddGridAndCreateMap(context, "A", 0, null, null, null);
        NavigationMapCommitOperation install = AdmitMap(context, map, sequence: 1);
        SimulateUntilTerminal(context, install.Receipt);
        install.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var address = new NavigationCellAddress(map.MapId, default);
        lease.Graph.TryGetSurfaceComponent(
                address,
                TraversalMedium.Solid,
                out NavigationSurfaceComponentKey priorComponent,
                out _)
            .Should().BeTrue();

        var expandedCell = new NavigationCell(
            TraversalMedia.Solid | TraversalMedia.Gas | TraversalMedia.Liquid,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        var delta = new NavigationMapOverlayDelta(
            map.MapId,
            new[] { NavigationCellOverlayOperation.Set(default, expandedCell) });
        var transaction = new NavigationOverlayTransaction(new[] { delta });
        var preparedOverlay = new PreparedNavigationOverlay(transaction);
        NavigationOperationCandidate candidate = FoldMap(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            new PreparedNavigationMap(map, bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            defaults);
        candidate = FoldOverlay(candidate, transaction, sequence: 2, defaults);
        NavigationOperationFrameChange[] changes =
        {
            NavigationOperationFrameChange.Overlay(preparedOverlay, operationSequence: 2)
        };
        var work = new NavigationStructuralCompositionWork(
            context.World,
            lease.Graph,
            candidate,
            changes,
            changes.Length);
        MaintenanceWorkBudget source = defaults.MaintenanceBudget;
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(
            source.MaxConsumedEnvelopes,
            source.MaxBaselineAddresses,
            source.MaxOverlaySlots,
            source.MaxComponentNodes,
            source.MaxSeamCandidateProbes,
            source.MaxExplicitEdges,
            maxDependencyEntries: 1,
            source.MaxSurfaceComponentEdges));

        work.Advance(meter);
        work.AffectedComponents.Contains(priorComponent).Should().BeFalse(
            "the exact component remains pending after the changed-map ID consumes the frame debit");
        meter.DependencyEntries.Should().Be(1);

        meter.Reset();
        work.Advance(meter).Should().BeFalse();
        work.AffectedComponents.Contains(priorComponent).Should().BeFalse(
            "Solid remains structurally unchanged, so only the exact address is debited");
        meter.DependencyEntries.Should().Be(1);
        work.IsChangedMapCaptureComplete.Should().BeFalse(
            "the address debit leaves the first newly affected medium pending");

        int dependencyLimitedFrames = 0;
        for (int frame = 0; frame < 4096 && !work.IsChangedMapCaptureComplete; frame++)
        {
            meter.Reset();
            work.Advance(meter);
            meter.DependencyEntries.Should().BeLessThanOrEqualTo(1);
            if (meter.DependencyEntries == 1)
                dependencyLimitedFrames++;
        }
        work.IsChangedMapCaptureComplete.Should().BeTrue();
        dependencyLimitedFrames.Should().BeGreaterThanOrEqualTo(2,
            "Gas and Liquid must each survive a distinct one-entry maintenance frame");

        meter.Reset();
        for (int frame = 0; frame < 4096 && !work.IsComplete; frame++)
        {
            work.Advance(meter);
            meter.Reset();
        }

        work.IsComplete.Should().BeTrue();
        work.Result.TryGetMap(map.MapId, out NavigationMapInstance expanded).Should().BeTrue();
        expanded.GetEffectiveMedia(default).Should().Be(expandedCell.Media,
            "the resumed exact-address capture must publish every newly affected medium");
    }

    [Fact]
    public void CellSuppression_ShouldRetryTheChangedSourceComponentAfterTheMapDebit()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NavigationMap map = AddGridAndCreateMap(context, "A", 0, null, null, null);
        NavigationMapCommitOperation install = AdmitMap(context, map, sequence: 1);
        SimulateUntilTerminal(context, install.Receipt);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var address = new NavigationCellAddress(map.MapId, default);
        lease.Graph.TryGetSurfaceComponent(
                address,
                TraversalMedium.Solid,
                out NavigationSurfaceComponentKey priorComponent,
                out _)
            .Should().BeTrue();

        var delta = new NavigationMapOverlayDelta(
            map.MapId,
            new[] { NavigationCellOverlayOperation.Suppress(default) });
        var transaction = new NavigationOverlayTransaction(new[] { delta });
        NavigationOperationCandidate candidate = FoldMap(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            new PreparedNavigationMap(map, bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            defaults);
        candidate = FoldOverlay(candidate, transaction, sequence: 2, defaults);
        NavigationOperationFrameChange[] changes =
        {
            NavigationOperationFrameChange.Overlay(
                new PreparedNavigationOverlay(transaction),
                operationSequence: 2)
        };
        var work = new NavigationStructuralCompositionWork(
            context.World,
            lease.Graph,
            candidate,
            changes,
            changes.Length);
        MaintenanceWorkBudget source = defaults.MaintenanceBudget;
        var meter = new MaintenanceWorkMeter(new MaintenanceWorkBudget(
            source.MaxConsumedEnvelopes,
            source.MaxBaselineAddresses,
            source.MaxOverlaySlots,
            source.MaxComponentNodes,
            source.MaxSeamCandidateProbes,
            source.MaxExplicitEdges,
            maxDependencyEntries: 1,
            source.MaxSurfaceComponentEdges));

        work.Advance(meter).Should().BeFalse();
        meter.DependencyEntries.Should().Be(1,
            "the first frame owns only the changed-map scope");
        work.AffectedComponents.Contains(priorComponent).Should().BeFalse();

        meter.Reset();
        work.Advance(meter).Should().BeFalse();
        meter.DependencyEntries.Should().Be(1);
        work.AffectedComponents.Contains(priorComponent).Should().BeTrue(
            "the exact changed Solid component must resume before the address row");
    }

    [Fact]
    public void RepeatedExactOverlayScopes_ShouldCoalesceComponentAndAddressOwnership()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NavigationMap map = AddGridAndCreateMap(context, "A", 0, null, null, null);
        NavigationMapCommitOperation install = AdmitMap(context, map, sequence: 1);
        SimulateUntilTerminal(context, install.Receipt);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var address = new NavigationCellAddress(map.MapId, default);
        lease.Graph.TryGetSurfaceComponent(
                address,
                TraversalMedium.Solid,
                out NavigationSurfaceComponentKey priorComponent,
                out _)
            .Should().BeTrue();

        var expanded = new NavigationCell(
            TraversalMedia.Solid | TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One);
        var setTransaction = new NavigationOverlayTransaction(new[]
        {
            new NavigationMapOverlayDelta(map.MapId, new[]
            {
                NavigationCellOverlayOperation.Set(default, expanded)
            })
        });
        var suppressTransaction = new NavigationOverlayTransaction(new[]
        {
            new NavigationMapOverlayDelta(map.MapId, new[]
            {
                NavigationCellOverlayOperation.Suppress(default)
            })
        });
        NavigationOperationCandidate candidate = FoldMap(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            new PreparedNavigationMap(map, bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            defaults);
        candidate = FoldOverlay(candidate, setTransaction, sequence: 2, defaults);
        candidate = FoldOverlay(candidate, suppressTransaction, sequence: 3, defaults);
        NavigationOperationFrameChange[] changes =
        {
            NavigationOperationFrameChange.Overlay(
                new PreparedNavigationOverlay(setTransaction),
                operationSequence: 2),
            NavigationOperationFrameChange.Overlay(
                new PreparedNavigationOverlay(suppressTransaction),
                operationSequence: 3)
        };
        var work = new NavigationStructuralCompositionWork(
            context.World,
            lease.Graph,
            candidate,
            changes,
            changes.Length);
        var meter = new MaintenanceWorkMeter(defaults.MaintenanceBudget);

        for (int frame = 0; frame < 4096 && !work.IsComplete; frame++)
        {
            work.Advance(meter);
            meter.Reset();
        }

        work.IsComplete.Should().BeTrue();
        work.CapturedChangedMapCount.Should().Be(1);
        work.AffectedComponents.Count.Should().Be(1,
            "both exact scopes originate in the same pre-change Solid component");
        work.AffectedComponents.Contains(priorComponent).Should().BeTrue();
        work.Result.TryGetMap(map.MapId, out NavigationMapInstance result).Should().BeTrue();
        result.TryGetEffectiveCell(default, out _).Should().BeFalse(
            "the later exact scope owns the atomically published final state");
    }

    [Fact]
    public void SeamRemoval_ShouldCaptureTheUnchangedPeerMapAsStructurallyChanged()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NavigationMap firstMap = AddGridAndCreateMap(context, "A", 0, null, null, null);
        NavigationMap secondMap = AddGridAndCreateMap(context, "B", 1, null, null, null);
        NavigationMapCommitOperation first = AdmitMap(context, firstMap, sequence: 1);
        NavigationMapCommitOperation second = AdmitMap(context, secondMap, sequence: 2);
        SimulateUntilTerminal(context, second.Receipt);
        first.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        second.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var firstAddress = new NavigationCellAddress(firstMap.MapId, default);
        NavigationAutomaticSeamIndex.EndpointEnumerator seams =
            lease.Graph.AutomaticSeams.GetActiveEndpointEnumerator(firstAddress);
        seams.MoveNext().Should().BeTrue(
            "the adjacent published maps own a structural seam before suppression");
        seams.Current.Destination.MapId.Should().Be(secondMap.MapId);

        var transaction = new NavigationOverlayTransaction(new[]
        {
            new NavigationMapOverlayDelta(firstMap.MapId, new[]
            {
                NavigationCellOverlayOperation.Suppress(default)
            })
        });
        NavigationOperationCandidate candidate = FoldMap(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            new PreparedNavigationMap(firstMap, bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            defaults);
        candidate = FoldMap(
            candidate,
            new PreparedNavigationMap(secondMap, bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            defaults);
        candidate = FoldOverlay(candidate, transaction, sequence: 3, defaults);
        NavigationOperationFrameChange[] changes =
        {
            NavigationOperationFrameChange.Overlay(
                new PreparedNavigationOverlay(transaction),
                operationSequence: 3)
        };
        var work = new NavigationStructuralCompositionWork(
            context.World,
            lease.Graph,
            candidate,
            changes,
            changes.Length);
        var meter = new MaintenanceWorkMeter(defaults.MaintenanceBudget);

        for (int frame = 0; frame < 4096 && !work.IsChangedMapCaptureComplete; frame++)
        {
            work.Advance(meter);
            meter.Reset();
        }

        work.IsChangedMapCaptureComplete.Should().BeTrue();
        work.CapturedChangedMapCount.Should().Be(2,
            "removing A's seam changes both A's row and unchanged peer B's row");
        work.GetCapturedChangedMapIdAt(0).Should().Be(firstMap.MapId);
        work.GetCapturedChangedMapIdAt(1).Should().Be(secondMap.MapId);
    }

    [Fact]
    public void StructuralWorkBeforeSeamCapture_ShouldBePublicationRevalidationNeutral()
    {
        using var world = new GridWorld();
        var work = new NavigationStructuralCompositionWork(
            world,
            NavigationWorldGraph.Empty,
            new NavigationOperationCandidate(navigationAreaCount: 1),
            System.Array.Empty<NavigationOperationFrameChange>(),
            changeCount: 0);

        work.RevalidateAutomaticSeamsForPublication().Should().BeTrue(
            "before a seam result is captured there is no GridForge cursor to invalidate");
        var meter = new MaintenanceWorkMeter(
            TrailblazerWorldContextSettings.Default.MaintenanceBudget);
        work.Advance(
                meter,
                maximumRetainedBytes: 0,
                maximumPersistentPages: 0,
                out bool capacityExceeded)
            .Should().BeFalse();
        capacityExceeded.Should().BeTrue(
            "the first prepared seam workspace must be rejected before any over-capacity result escapes");
    }

    [Fact]
    public void WholeMapRemoval_ShouldRetryMembershipAndOwnershipRowsIndependently()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NavigationMap unrelated = AddLinearGridAndCreateMap(context, "A", 0, 64);
        NavigationMap removed = AddLinearGridAndCreateMap(context, "Z", 100, 1);
        NavigationMapCommitOperation first = AdmitMap(context, unrelated, 1);
        NavigationMapCommitOperation second = AdmitMap(context, removed, 2);
        SimulateUntilTerminal(context, second.Receipt);
        first.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        second.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var removedAddress = new NavigationCellAddress("Z", default);
        lease.Graph.TryGetSurfaceComponent(
                removedAddress,
                TraversalMedium.Solid,
                out NavigationSurfaceComponentKey removedComponent,
                out _)
            .Should().BeTrue();
        NavigationOperationCandidate candidate = FoldMap(
            new NavigationOperationCandidate(navigationAreaCount: 1),
            new PreparedNavigationMap(unrelated, 1),
            OverlayReplacementPolicy.Clear,
            defaults);
        candidate = FoldMap(
            candidate,
            new PreparedNavigationMap(removed, 1),
            OverlayReplacementPolicy.Clear,
            defaults);
        candidate = FoldMapRemoval(candidate, removed.MapId, defaults);
        NavigationOperationFrameChange[] changes =
        {
            NavigationOperationFrameChange.MapRemove(removed.MapId, 3)
        };
        var work = new NavigationStructuralCompositionWork(
            context.World,
            lease.Graph,
            candidate,
            changes,
            changes.Length);
        MaintenanceWorkBudget source = defaults.MaintenanceBudget;
        var budget = new MaintenanceWorkBudget(
            source.MaxConsumedEnvelopes,
            source.MaxBaselineAddresses,
            source.MaxOverlaySlots,
            source.MaxComponentNodes,
            source.MaxSeamCandidateProbes,
            source.MaxExplicitEdges,
            maxDependencyEntries: 1);
        var meter = new MaintenanceWorkMeter(budget);

        work.Advance(meter).Should().BeFalse(
            "the first dependency entry captures only the raw changed-map scope");
        work.AffectedComponents.Contains(removedComponent).Should().BeFalse();
        meter.DependencyEntries.Should().Be(1);
        meter.Reset();

        work.Advance(meter).Should().BeFalse();
        meter.DependencyEntries.Should().Be(1);
        work.AffectedComponents.Contains(removedComponent).Should().BeTrue(
            "the pending component must survive the first dependency-budget stop");
        work.IsChangedMapCaptureComplete.Should().BeFalse(
            "the whole-map ownership row still requires its own debit");
        meter.Reset();

        work.Advance(meter);
        meter.DependencyEntries.Should().Be(1);
        work.AffectedComponents.Contains(removedComponent).Should().BeTrue(
            "recording the whole-map row must not recapture or drop its component");
        for (int frame = 0; frame < 4096 && !work.IsChangedMapCaptureComplete; frame++)
        {
            meter.Reset();
            work.Advance(meter);
        }
        work.IsChangedMapCaptureComplete.Should().BeTrue(
            "the retried whole-map ownership must survive the later seam and exact capture phases");
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
        NavigationMap replacement = new NavigationMapBuilder(map.MapId, map.GridBinding)
            .Build();
        var prepared = new PreparedNavigationMap(replacement, 2);
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
            context.World,
            lease.Graph,
            candidate,
            changes,
            changes.Length);
        work.MarkInitialClosurePublished();

        var meter = new MaintenanceWorkMeter(defaults.MaintenanceBudget);
        work.Advance(meter).Should().BeFalse();
        work.RequiresAffectedClosurePublication.Should().BeTrue();
        NavigationSurfaceComponentKeySet affected = work.AffectedComponents;
        affected.Count.Should().Be(1);
        long ownedBytes = affected.RetainedBytes;
        int ownedPages = affected.PersistentPageCount;
        long retainedBytes = work.RetainedBytes;
        int retainedPages = work.PersistentPageCount;

        work.RecordAffectedClosurePublication(NavigationCandidatePublication.Deferred);
        work.RequiresAffectedClosurePublication.Should().BeTrue();
        work.RetainedBytes.Should().Be(retainedBytes);
        work.PersistentPageCount.Should().Be(retainedPages);

        work.RecordAffectedClosurePublication(NavigationCandidatePublication.Published);

        work.RetainedBytes.Should().Be(retainedBytes - ownedBytes,
            "the graph owns the exact affected-component root after publication");
        work.PersistentPageCount.Should().Be(retainedPages - ownedPages);
        work.RequiresAffectedClosurePublication.Should().BeFalse();

        work.RecordAllClosePublication(NavigationCandidatePublication.Deferred);
        work.RequiresAffectedClosurePublication.Should().BeFalse();
        work.RecordAllClosePublication(NavigationCandidatePublication.Published);
        work.RequiresAffectedClosurePublication.Should().BeTrue(
            "broadening back to all-closed transfers the affected root to work until its narrowed closure republishes");
        work.RetainedBytes.Should().Be(retainedBytes);
        work.PersistentPageCount.Should().Be(retainedPages);
    }

    [Fact]
    public void CompletedSeamInvalidation_ShouldRequireAllCloseRepublication()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        NavigationMap firstMap = AddGridAndCreateMap(context, "A", 0, null, null, null);
        NavigationMap secondMap = AddGridAndCreateMap(context, "B", 1, null, null, null);
        NavigationMapCommitOperation first = AdmitMap(context, firstMap, 1);
        SimulateUntilTerminal(context, first.Receipt);
        first.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.AutomaticSeams.PairCount.Should().Be(0,
            "the adjacent grid has not entered the navigation graph yet");
        var prepared = new PreparedNavigationMap(secondMap, 1);
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
            context.World,
            lease.Graph,
            candidate,
            changes,
            changes.Length);
        work.MarkInitialClosurePublished();
        var meter = new MaintenanceWorkMeter(defaults.MaintenanceBudget);

        for (int frame = 0;
             frame < 4096 && !work.RequiresAffectedClosurePublication;
             frame++)
        {
            meter.Reset();
            work.Advance(meter).Should().BeFalse(
                "completed seam capture must first transfer its affected closure");
        }
        work.RequiresAffectedClosurePublication.Should().BeTrue();
        work.RecordAffectedClosurePublication(NavigationCandidatePublication.Published);

        bool completed = false;
        for (int frame = 0; frame < 4096 && !completed; frame++)
        {
            meter.Reset();
            completed = work.Advance(meter);
        }
        completed.Should().BeTrue();
        work.Result.AutomaticSeams.PairCount.Should().BePositive(
            "adding the adjacent map must retain a real completed boundary-contact cursor");
        lease.Graph.TryGetMap(secondMap.MapId, out NavigationMapInstance _)
            .Should().BeFalse("the second map exists only in the prepared graph");
        context.World.TryGetGrid(new Vector3d(1, 0, 0), out VoxelGrid? secondGrid)
            .Should().BeTrue();
        context.World.TryRemoveGrid(secondGrid!.GridIndex).Should().BeTrue();

        work.RevalidateAutomaticSeamsForPublication().Should().BeFalse(
            "removing the final discovered grid generation invalidates its retained validator");
        work.RequiresAllClosePublication.Should().BeTrue(
            "invalidating completed work broadens its published affected closure before retry");
    }

    [Fact]
    public void BridgeRemoval_ShouldFailClosedOnlyExactEndpointComponentUntilAtomicPublication()
    {
        using TrailblazerWorldContext context = CreateConnectedContext(out long sequence);
        var bridgeSource = new NavigationCellAddress("B", new VoxelIndex(2, 0, 0));
        var bridgeDestination = new NavigationCellAddress("C", default);
        NavigationSurfaceComponentKey priorComponent;
        long priorComponentVersion;
        using (NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!)
        {
            lease.Graph.TryGetSurfaceComponent(
                    bridgeSource,
                    TraversalMedium.Solid,
                    out priorComponent,
                    out priorComponentVersion)
                .Should().BeTrue();
            lease.Graph.AreInSameSurfaceComponent(
                    bridgeSource,
                    TraversalMedium.Solid,
                    bridgeDestination,
                    TraversalMedium.Solid)
                .Should().BeTrue();
        }
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
        context.Pathing.TryGetNavigationGraphCellState(
                bridgeSource.MapId,
                bridgeSource.Index,
                out NavigationGraphCellState blockedSource)
            .Should().BeTrue();
        blockedSource.IsMaterialized.Should().BeFalse();
        NavigationGraphDiagnosticsSnapshot blocked = context.Pathing.GetNavigationGraphDiagnostics();
        using (NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!)
        {
            lease.Graph.TryGetSurfaceComponent(
                    bridgeSource,
                    TraversalMedium.Solid,
                    out NavigationSurfaceComponentKey blockedComponent,
                    out long blockedVersion)
                .Should().BeTrue();
            blockedComponent.Should().Be(priorComponent);
            blockedVersion.Should().Be(priorComponentVersion,
                "the exact index remains the old atomic root while its component is closed");
        }
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
        using NavigationWorldGraphLease published = context.Pathing.TryAcquireNavigationGraph()!;
        published.Graph.AreInSameSurfaceComponent(
                bridgeSource,
                TraversalMedium.Solid,
                bridgeDestination,
                TraversalMedium.Solid)
            .Should().BeFalse();
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
        lease.Graph.ExplicitConnections.TryGet(
                new NavigationConnectionOwnerKey("A", "ab"),
                out NavigationExplicitConnectionRecord explicitEdge)
            .Should().BeTrue();
        explicitEdge.IsActive.Should().BeTrue();
        var aAddress = new NavigationCellAddress("A", new VoxelIndex(2, 0, 0));
        lease.Graph.ExplicitConnections.GetEndpointOwnerRow(aAddress).Count
            .Should().Be(1, "the source endpoint row must contain the active owner");
        var bAddress = new NavigationCellAddress("B", default);
        lease.Graph.ExplicitConnections.GetEndpointOwnerRow(bAddress).Count
            .Should().Be(1, "the destination endpoint row must contain the active owner");
        lease.Graph.TryGetNodeRef(aAddress, out NavigationNodeRef aNode).Should().BeTrue();
        NavigationSurfaceEdgeEnumerator outgoing =
            lease.Graph.EnumerateStructuralSurfaceEdges(aNode);
        outgoing.MoveNext().Should().BeTrue(
            "the active A-B explicit edge must be visible to structural component traversal");
        lease.Graph.SurfaceComponents.TryGet(
                aAddress,
                TraversalMedium.Solid,
                out NavigationSurfaceComponent component)
            .Should().BeTrue();
        component.Members.Count.Should().Be(2);
        lease.Graph.AreInSameSurfaceComponent(
                aAddress,
                TraversalMedium.Solid,
                bAddress,
                TraversalMedium.Solid)
            .Should().BeTrue();
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
        updated.ObstacleCount.Should().Be(1);
        NavigationGraphDiagnosticsSnapshot afterSafety = context.Pathing
            .GetNavigationGraphDiagnostics();
        afterSafety.GraphVersion
            .Should().Be(versionBeforeSafetyMaintenance + 2,
                "the bounded state inspection first broadens the safety closure, then publishes "
                + "the reconciled immutable root on a later maintenance boundary");
        afterSafety.Maps[3].ComponentVersion.Should().Be(
            componentVersionBeforeSafetyMaintenance,
            "physical obstacles are page-level traversal state and do not rebuild structural components");
        long componentVersionAfterSafetyMaintenance = afterSafety.Maps[3].ComponentVersion;
        SimulateUntilTerminal(context, removal.Receipt);

        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        NavigationGraphCellState retained = GetCell(context, "U");
        for (int i = 0;
             i < 64 && (!retained.IsMaterialized || retained.ObstacleCount == 0);
             i++)
        {
            context.Simulate();
            retained = GetCell(context, "U");
        }
        retained.IsMaterialized.Should().BeTrue();
        retained.ObstacleCount.Should().Be(1,
            "publishing older structural work must not revert reconciled GridForge state");
        context.Pathing.GetNavigationGraphDiagnostics().Maps[3].ComponentVersion
            .Should().BeGreaterThanOrEqualTo(componentVersionAfterSafetyMaintenance,
                "publishing older structural work must not revert the physical component clock");
    }

    [Fact]
    public void MaterializedCatchUp_ShouldPreserveAffectedClosureUntilStructuralPublication()
    {
        using TrailblazerWorldContext context = CreateConnectedContext(out long sequence);
        NavigationOverlayCommitOperation removal = SuppressConnection(
            context,
            ++sequence,
            "B",
            "bc");

        bool reachedAffectedPublicationBoundary = false;
        for (int i = 0; i < 64 && removal.Receipt.Status == NavigationOperationStatus.Pending; i++)
        {
            context.Simulate();
            using NavigationWorldGraphLease pending = context.Pathing.TryAcquireNavigationGraph()!;
            MaintenanceWorkMeter meter = context.Pathing.NavigationMaintenanceMeter;
            if (!pending.Graph.AreAllStructuralComponentsClosed
                || context.Pathing.RetainedCompositionWorkCount != 1
                || meter.OverlaySlots != 0
                || meter.ComponentNodes != 0
                || meter.SeamCandidateProbes != 0
                || meter.ExplicitEdges != 0
                || meter.DependencyEntries != 2)
            {
                continue;
            }

            reachedAffectedPublicationBoundary = true;
            break;
        }
        reachedAffectedPublicationBoundary.Should().BeTrue(
            "the exact bounded cursor must stop immediately before affected-closure publication");

        VoxelGrid unrelatedGrid = context.World.ActiveGrids[3];
        unrelatedGrid.TryGetVoxel(default(VoxelIndex), out Voxel? voxel).Should().BeTrue();
        unrelatedGrid.TryAddObstacle(voxel!, context.World.AllocateObstacleToken()).Should().BeTrue();

        bool observedRetainedMaterializedWork = false;
        bool observedAffectedCatchUp = false;
        for (int i = 0; i < 64 && removal.Receipt.Status == NavigationOperationStatus.Pending; i++)
        {
            context.Simulate();
            observedRetainedMaterializedWork |= context.Pathing.RetainedCompositionWorkCount == 2;
            using NavigationWorldGraphLease pending = context.Pathing.TryAcquireNavigationGraph()!;
            NavigationGraphCellState unrelated = GetCell(context, "U");
            if (pending.Graph.AreAllStructuralComponentsClosed
                || !unrelated.IsMaterialized
                || unrelated.ObstacleCount == 0)
            {
                continue;
            }

            observedAffectedCatchUp = true;
            pending.Graph.HasClosedStructuralScope.Should().BeTrue(
                "the narrowed publication must still own its affected component root");
            pending.Graph.IsSurfaceAddressClosed(
                    new NavigationCellAddress("B", default),
                    TraversalMedium.Solid)
                .Should().BeFalse(
                    "the unaffected B cell proves this is the narrowed closure, not all-close safety");
            pending.Graph.IsSurfaceAddressClosed(
                    new NavigationCellAddress("B", new VoxelIndex(2, 0, 0)),
                    TraversalMedium.Solid)
                .Should().BeTrue(
                    "materialized catch-up must retain the additional affected-component root");
            break;
        }

        observedRetainedMaterializedWork.Should().BeTrue(
            "the affected publication must retain both structural and materialized work");
        observedAffectedCatchUp.Should().BeTrue(
            "the physical update must publish while the affected structural operation remains pending");
        removal.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
    }

    [Fact]
    public void PhysicalChangeCarryover_ShouldRetainExactCaptureUntilComponentPublication()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var budget = new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            defaults.MaintenanceBudget.MaxOverlaySlots,
            maxComponentNodes: 1,
            defaults.MaintenanceBudget.MaxSeamCandidateProbes,
            defaults.MaintenanceBudget.MaxExplicitEdges,
            defaults.MaintenanceBudget.MaxDependencyEntries);
        var settings = new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            budget,
            defaults.GuideSampleBudget,
            defaults.MovementGroupPadding,
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
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits);
        using var world = new GridWorld();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(7, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        world.TryAddGrid(configuration, out ushort gridIndex).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var builder = new NavigationMapBuilder("map", binding);
        for (int x = 0; x < 8; x++)
            builder.AddCell(new VoxelIndex(x, 0, 0), Cell);
        using var runtime = new NavigationGraphRuntime(world, settings);
        var install = new NavigationMapCommitOperation(
            new PreparedNavigationMap(builder.Build(), 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 1);
        runtime.Admit(install).Should().BeTrue();
        int frame = 1;
        for (; frame < 512 && install.Receipt.Status == NavigationOperationStatus.Pending; frame++)
            runtime.Maintain(frame);
        install.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        runtime.RetainedCompositionWorkCount.Should().Be(0);
        VoxelGrid grid = world.ActiveGrids[gridIndex];
        grid.TryGetVoxel(default(VoxelIndex), out Voxel? voxel).Should().BeTrue();
        grid.TryAddObstacle(voxel!, world.AllocateObstacleToken()).Should().BeTrue();
        runtime.EnqueueCommittedChange(new GridEventInfo(
            world.SpawnToken,
            grid.GridIndex,
            grid.SpawnToken,
            configuration,
            grid.Version,
            GridEventKind.GridChanged,
            changeStamp: new GridChangeStamp(world.ChangeSequence, world.ChangeSequence)));

        runtime.Maintain(frame++);

        runtime.RetainedCompositionWorkCount.Should().Be(1);
        runtime.RetainedBaselineCaptureCount.Should().Be(1,
            "bounded component inspection still reads the detached physical capture");
        NavigationGraphDiagnosticsSnapshot retained = runtime.GetDiagnostics(maximumCells: 8);
        retained.ActiveSnapshotBytes.Should().Be(checked(
            runtime.Current.RetainedBytes
            + retained.BaselineRebuildBytes
            + runtime.RetainedCompositionWorkBytes
            + runtime.RetainedOperationWorkBytes),
            "materialized work and its operation owner must be charged exactly once");
        retained.PersistentGraphPageCount.Should().Be(checked(
            runtime.Current.PersistentPageCount
            + retained.BaselineRebuildPageCount
            + runtime.RetainedCompositionWorkPageCount
            + runtime.RetainedOperationWorkPageCount));
        runtime.TryGetCellState("map", default, out NavigationGraphCellState closed)
            .Should().BeTrue();
        closed.IsMaterialized.Should().BeFalse();

        for (int remaining = 64;
             remaining > 0 && runtime.RetainedCompositionWorkCount != 0;
             remaining--)
            runtime.Maintain(frame++);

        runtime.RetainedBaselineCaptureCount.Should().Be(0);
        runtime.RetainedCompositionWorkCount.Should().Be(0);
        runtime.TryGetCellState("map", default, out NavigationGraphCellState published)
            .Should().BeTrue();
        published.IsMaterialized.Should().BeTrue();
        published.ObstacleCount.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResumedComposition_ShouldRejectDynamicSlotOverflowWithoutLeakingSafetyClosure(
        bool topologyFullRebuildPending)
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var budget = new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            defaults.MaintenanceBudget.MaxOverlaySlots,
            maxComponentNodes: 1,
            defaults.MaintenanceBudget.MaxSeamCandidateProbes,
            defaults.MaintenanceBudget.MaxExplicitEdges,
            defaults.MaintenanceBudget.MaxDependencyEntries);
        NavigationOperationLimits defaultLimits = defaults.OperationLimits;
        var operationLimits = new NavigationOperationLimits(
            defaultLimits.MaxPendingOperations,
            defaultLimits.MaxPendingDescriptorBytes,
            defaultLimits.MaxPreparedMapBytes,
            defaultLimits.MaxBatchItems,
            defaultLimits.MaxBatchDescriptorBytes,
            defaultLimits.MaxBatchSortScratchBytes,
            defaultLimits.MaxCorridorCells,
            defaultLimits.MaxMaps,
            defaultLimits.MaxRetainedMapIdentities,
            maxOverlayCellsPerMap: 1,
            defaultLimits.MaxOverlayConnectionsPerMap,
            defaultLimits.MaxOverlayTransitionsPerMap,
            maxOverlayCells: 1,
            defaultLimits.MaxOverlayConnections,
            defaultLimits.MaxOverlayTransitions,
            defaultLimits.MaxTransitionRulesPerMap,
            defaultLimits.MaxTransitionRules);
        var settings = new TrailblazerWorldContextSettings(
            operationLimits,
            budget,
            defaults.GuideSampleBudget,
            defaults.MovementGroupPadding,
            defaults.MaxIngressEntries,
            defaults.MaxIngressBytes,
            defaults.MaxActiveSnapshots,
            defaults.MaxActiveSnapshotBytes,
            defaults.MaxRetiredSnapshots,
            defaults.MaxRetiredSnapshotBytes,
            defaults.MaxPersistentGraphPages,
            maxDynamicCellSlotsPerMap: 1,
            maxDynamicCellSlots: 1,
            defaults.NavigationAreaCount,
            maxAreaPolicies: 1,
            defaults.MaxAreaRulesPerPolicy,
            defaults.MaxAreaRules,
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: settings);
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(2, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        ushort lifecycleGridIndex = 0;
        if (topologyFullRebuildPending)
        {
            var lifecycleConfiguration = new GridConfiguration(
                new Vector3d(10, 0, 0),
                new Vector3d(10, 0, 0),
                topologyKind: GridTopologyKind.RectangularPrism,
                topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
                storageKind: GridStorageKind.Dense);
            context.World.TryAddGrid(lifecycleConfiguration, out lifecycleGridIndex)
                .Should().BeTrue();
        }
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var install = new NavigationMapCommitOperation(
            new PreparedNavigationMap(
                new NavigationMapBuilder("map", binding)
                    .AddCell(default, Cell)
                    .Build(),
                bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 1);
        context.Pathing.Admit(install).Should().BeTrue();
        SimulateUntilTerminal(context, install.Receipt);
        install.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        var allocate = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta("map", new[]
                {
                    NavigationCellOverlayOperation.Set(new VoxelIndex(1, 0, 0), Cell)
                })
            })),
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(allocate).Should().BeTrue();
        SimulateUntilTerminal(context, allocate.Receipt);
        allocate.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        var revert = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta("map", new[]
                {
                    NavigationCellOverlayOperation.RevertToBake(new VoxelIndex(1, 0, 0))
                })
            })),
            operationSequence: 3,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(revert).Should().BeTrue();
        SimulateUntilTerminal(context, revert.Receipt);
        revert.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        var overflow = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(new[]
            {
                new NavigationMapOverlayDelta("map", new[]
                {
                    NavigationCellOverlayOperation.Set(new VoxelIndex(2, 0, 0), Cell)
                })
            })),
            operationSequence: 4,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(overflow).Should().BeTrue();

        bool observedClosure = false;
        for (int frame = 0;
             frame < 512
                 && overflow.Receipt.Status == NavigationOperationStatus.Pending
                 && !observedClosure;
             frame++)
        {
            context.Simulate();
            using NavigationWorldGraphLease? lease = context.Pathing.TryAcquireNavigationGraph();
            observedClosure = context.Pathing.RetainedCompositionWorkCount == 1
                && (lease?.Graph.HasClosedStructuralScope ?? false);
        }

        observedClosure.Should().BeTrue(
            "the one-node budget must carry the candidate through operation-owned safety");
        if (topologyFullRebuildPending)
        {
            context.World.TryRemoveGrid(lifecycleGridIndex).Should().BeTrue();
            context.Simulate();
            context.Pathing.NavigationMaintenanceMeter.ConsumedEnvelopes.Should().BePositive(
                "the retained composition must consume the unrelated topology lifecycle before rejecting");
            overflow.Receipt.Status.Should().Be(NavigationOperationStatus.Pending,
                "full-rebuild ownership must be established before the later dynamic-slot rejection");
            using NavigationWorldGraphLease? safety =
                context.Pathing.TryAcquireNavigationGraph();
            if (safety != null)
            {
                safety.Graph.AreAllStructuralComponentsClosed.Should().BeTrue(
                    "reader admission may remain open only when the published source is already all-closed");
            }
        }
        SimulateUntilTerminal(context, overflow.Receipt);
        overflow.Receipt.Status.Should().Be(NavigationOperationStatus.Rejected);
        overflow.Receipt.Rejection.Should().Be(NavigationOperationRejection.CapacityExceeded);

        NavigationWorldGraphLease? published = context.Pathing.TryAcquireNavigationGraph();
        if (topologyFullRebuildPending)
        {
            if (published != null)
            {
                published.Graph.AreAllStructuralComponentsClosed.Should().BeTrue(
                    "a rejected operation may expose only an all-closed graph until the topology lifecycle publishes");
                published.Dispose();
                published = null;
            }
            for (int frame = 0; frame < 512 && published == null; frame++)
            {
                context.Simulate();
                NavigationWorldGraphLease? candidate =
                    context.Pathing.TryAcquireNavigationGraph();
                if (candidate?.Graph.HasClosedStructuralScope ?? false)
                {
                    candidate.Dispose();
                    continue;
                }
                published = candidate;
            }
        }

        using (published)
        {
            published.Should().NotBeNull(
                "the topology lifecycle must eventually publish its exact rebuilt graph");
            published!.Graph.HasClosedStructuralScope.Should().BeFalse();
            published.Graph.TryGetMap("map", out NavigationMapInstance? retained).Should().BeTrue();
            retained!.DynamicSlotCount.Should().Be(1,
                "the reverted lifetime slot remains reserved while the over-limit slot is rejected");
            retained.TryGetSlot(new VoxelIndex(2, 0, 0), out _).Should().BeFalse();
        }
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
            maxSeamCandidateProbes: 1,
            maxExplicitEdges: 1,
            maxDependencyEntries: 3);
        var settings = new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            budget,
            defaults.GuideSampleBudget,
            defaults.MovementGroupPadding,
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
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits);
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
                lease.Graph.SurfaceComponents.TryGet(
                        new NavigationCellAddress(mapId, default),
                        TraversalMedium.Solid,
                        out _)
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

    private static NavigationMap AddLinearGridAndCreateMap(
        TrailblazerWorldContext context,
        string mapId,
        int origin,
        int cellCount)
    {
        var configuration = new GridConfiguration(
            new Vector3d(origin, 0, 0),
            new Vector3d(origin + cellCount - 1, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var builder = new NavigationMapBuilder(mapId, binding);
        for (int i = 0; i < cellCount; i++)
            builder.AddCell(new VoxelIndex(i, 0, 0), Cell);
        return builder.Build();
    }

    private static bool RunSeamWork(
        GridWorld world,
        NavigationOperationCandidate candidate,
        NavigationOperationFrameChange[] changes,
        long maximumRetainedBytes,
        out bool capacityExceeded)
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var work = new NavigationStructuralCompositionWork(
            world,
            NavigationWorldGraph.Empty,
            candidate,
            changes,
            changes.Length);
        var meter = new MaintenanceWorkMeter(defaults.MaintenanceBudget);
        return work.Advance(
            meter,
            maximumRetainedBytes,
            int.MaxValue,
            out capacityExceeded);
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
                            new[]
                            {
                                NavigationCellOverlayOperation.Suppress(
                                    new VoxelIndex(2, 0, 0))
                            },
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

    private static TrailblazerWorldContextSettings CreateSettings(
        MaintenanceWorkBudget budget,
        int maximumPersistentGraphPages)
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        return new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            budget,
            defaults.GuideSampleBudget,
            defaults.MovementGroupPadding,
            defaults.MaxIngressEntries,
            defaults.MaxIngressBytes,
            defaults.MaxActiveSnapshots,
            defaults.MaxActiveSnapshotBytes,
            defaults.MaxRetiredSnapshots,
            defaults.MaxRetiredSnapshotBytes,
            maximumPersistentGraphPages,
            defaults.MaxDynamicCellSlotsPerMap,
            defaults.MaxDynamicCellSlots,
            defaults.NavigationAreaCount,
            maxAreaPolicies: 1,
            defaults.MaxAreaRulesPerPolicy,
            defaults.MaxAreaRules,
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits);
    }

    private static (
        GridWorld World,
        NavigationGraphRuntime Runtime,
        NavigationOperationReceipt Receipt) CreateCompositionPageScenario(
            int maximumPersistentGraphPages,
            bool useBoundedWorkBudget = true)
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        MaintenanceWorkBudget budget = useBoundedWorkBudget
            ? new MaintenanceWorkBudget(
                defaults.MaintenanceBudget.MaxConsumedEnvelopes,
                defaults.MaintenanceBudget.MaxBaselineAddresses,
                defaults.MaintenanceBudget.MaxOverlaySlots,
                maxComponentNodes: 1,
                maxSeamCandidateProbes: 1,
                maxExplicitEdges: 1,
                maxDependencyEntries: 3)
            : defaults.MaintenanceBudget;
        var settings = new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            budget,
            defaults.GuideSampleBudget,
            defaults.MovementGroupPadding,
            defaults.MaxIngressEntries,
            defaults.MaxIngressBytes,
            defaults.MaxActiveSnapshots,
            defaults.MaxActiveSnapshotBytes,
            defaults.MaxRetiredSnapshots,
            defaults.MaxRetiredSnapshotBytes,
            maximumPersistentGraphPages,
            defaults.MaxDynamicCellSlotsPerMap,
            defaults.MaxDynamicCellSlots,
            defaults.NavigationAreaCount,
            maxAreaPolicies: 1,
            defaults.MaxAreaRulesPerPolicy,
            defaults.MaxAreaRules,
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits);
        var world = new GridWorld();
        var configuration = new GridConfiguration(
            Vector3d.Zero,
            new Vector3d(63, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        world.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var builder = new NavigationMapBuilder("probe", binding);
        for (int x = 0; x < 64; x++)
            builder.AddCell(new VoxelIndex(x, 0, 0), Cell);
        var runtime = new NavigationGraphRuntime(world, settings);
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(builder.Build(), 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 1);
        runtime.Admit(operation).Should().BeTrue();
        return (world, runtime, operation.Receipt);
    }

    private static TrailblazerWorldContext CreatePostCompositionDynamicSlotScenario(
        out NavigationOverlayCommitOperation operation,
        out VoxelGrid grid,
        out ushort lifecycleGridIndex)
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        NavigationOperationLimits defaultLimits = defaults.OperationLimits;
        var limits = new NavigationOperationLimits(
            defaultLimits.MaxPendingOperations,
            defaultLimits.MaxPendingDescriptorBytes,
            defaultLimits.MaxPreparedMapBytes,
            defaultLimits.MaxBatchItems,
            defaultLimits.MaxBatchDescriptorBytes,
            defaultLimits.MaxBatchSortScratchBytes,
            defaultLimits.MaxCorridorCells,
            defaultLimits.MaxMaps,
            defaultLimits.MaxRetainedMapIdentities,
            maxOverlayCellsPerMap: 1,
            defaultLimits.MaxOverlayConnectionsPerMap,
            defaultLimits.MaxOverlayTransitionsPerMap,
            maxOverlayCells: 1,
            defaultLimits.MaxOverlayConnections,
            defaultLimits.MaxOverlayTransitions,
            defaultLimits.MaxTransitionRulesPerMap,
            defaultLimits.MaxTransitionRules);
        var budget = new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            defaults.MaintenanceBudget.MaxOverlaySlots,
            maxComponentNodes: 1,
            defaults.MaintenanceBudget.MaxSeamCandidateProbes,
            defaults.MaintenanceBudget.MaxExplicitEdges,
            defaults.MaintenanceBudget.MaxDependencyEntries);
        var settings = new TrailblazerWorldContextSettings(
            limits,
            budget,
            defaults.GuideSampleBudget,
            defaults.MovementGroupPadding,
            defaults.MaxIngressEntries,
            defaults.MaxIngressBytes,
            defaults.MaxActiveSnapshots,
            defaults.MaxActiveSnapshotBytes,
            defaults.MaxRetiredSnapshots,
            defaults.MaxRetiredSnapshotBytes,
            defaults.MaxPersistentGraphPages,
            maxDynamicCellSlotsPerMap: 8,
            maxDynamicCellSlots: 8,
            defaults.NavigationAreaCount,
            maxAreaPolicies: 1,
            defaults.MaxAreaRulesPerPolicy,
            defaults.MaxAreaRules,
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits);
        TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(settings: settings);
        var operationConfiguration = new GridConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(operationConfiguration, out _).Should().BeTrue();
        operationConfiguration.TryNormalize(out NormalizedGridConfiguration operationBinding)
            .Should().BeTrue();
        var operationMap = new NavigationMapCommitOperation(
            new PreparedNavigationMap(
                new NavigationMapBuilder("map", operationBinding)
                    .AddCell(default, Cell)
                    .Build(),
                bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: 1);
        context.Pathing.Admit(operationMap).Should().BeTrue();
        SimulateUntilTerminal(context, operationMap.Receipt);
        operationMap.Receipt.Status.Should().Be(
            NavigationOperationStatus.Applied,
            $"frame={context.FrameCount}, operation={context.Pathing.RetainedOperationWorkCount}, " +
            $"composition={context.Pathing.RetainedCompositionWorkCount}, " +
            $"baseline={context.Pathing.RetainedBaselineCaptureCount}");

        var physicalConfiguration = new GridConfiguration(
            new Vector3d(10, 0, 0),
            new Vector3d(18, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Sparse);
        var physical = new VoxelIndex[8];
        for (int x = 0; x < physical.Length; x++)
            physical[x] = new VoxelIndex(x, 0, 0);
        context.World.TryAddGrid(physicalConfiguration, physical, out ushort gridIndex)
            .Should().BeTrue();
        grid = context.World.ActiveGrids[gridIndex];
        physicalConfiguration.TryNormalize(out NormalizedGridConfiguration physicalBinding)
            .Should().BeTrue();
        var physicalMap = new NavigationMapCommitOperation(
            new PreparedNavigationMap(
                new NavigationMapBuilder("physical", physicalBinding)
                    .SetDefaultCell(Cell)
                    .Build(),
                bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(physicalMap).Should().BeTrue();
        SimulateUntilTerminal(context, physicalMap.Receipt);
        physicalMap.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        var lifecycleConfiguration = new GridConfiguration(
            new Vector3d(30, 0, 0),
            new Vector3d(30, 0, 0),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(lifecycleConfiguration, out lifecycleGridIndex)
            .Should().BeTrue();
        bool settled = false;
        for (int frame = 0; frame < 64; frame++)
        {
            context.Simulate();
            int dynamicSlotCount;
            bool hasClosedScope;
            using (NavigationWorldGraphLease published = context.Pathing.TryAcquireNavigationGraph()!)
            {
                published.Graph.TryGetMap("physical", out NavigationMapInstance? map).Should().BeTrue();
                dynamicSlotCount = map!.DynamicSlotCount;
                hasClosedScope = published.Graph.HasClosedStructuralScope;
            }
            if (dynamicSlotCount == 8
                && context.Pathing.RetainedCompositionWorkCount == 0
                && context.Pathing.NavigationMaintenanceMeter.ConsumedEnvelopes == 0
                && !hasClosedScope)
            {
                settled = true;
                break;
            }
        }
        settled.Should().BeTrue("the default-backed physical baseline must quiesce before the measured operation");
        using (NavigationWorldGraphLease published = context.Pathing.TryAcquireNavigationGraph()!)
        {
            published.Graph.TryGetMap("physical", out NavigationMapInstance? map).Should().BeTrue();
            map!.DynamicSlotCount.Should().Be(8);
        }
        context.Pathing.RetainedCompositionWorkCount.Should().Be(0);

        operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(new NavigationOverlayTransaction(
                new[]
                {
                    new NavigationMapOverlayDelta(
                        "map",
                        new[] { NavigationCellOverlayOperation.Suppress(default) })
                })),
            operationSequence: 3,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        return context;
    }

    private static void SimulateUntilCompositionRetained(
        TrailblazerWorldContext context,
        NavigationOperationReceipt receipt)
    {
        for (int frame = 0;
             frame < 512
                 && receipt.Status == NavigationOperationStatus.Pending
                 && (context.Pathing.RetainedOperationWorkCount != 1
                     || context.Pathing.RetainedCompositionWorkCount != 1
                     || context.Pathing.RetainedBaselineCaptureCount != 0);
             frame++)
            context.Simulate();
        receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        context.Pathing.RetainedOperationWorkCount.Should().Be(1);
        context.Pathing.RetainedCompositionWorkCount.Should().Be(1);
        context.Pathing.RetainedBaselineCaptureCount.Should().Be(0);
    }

    private static void SimulateUntilTerminal(
        TrailblazerWorldContext context,
        NavigationOperationReceipt receipt)
    {
        for (int i = 0; i < 512 && receipt.Status == NavigationOperationStatus.Pending; i++)
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

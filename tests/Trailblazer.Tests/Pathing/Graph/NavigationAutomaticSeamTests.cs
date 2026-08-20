//=======================================================================
// NavigationAutomaticSeamTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
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
public sealed class NavigationAutomaticSeamTests
{
    private static readonly NavigationCell SeamCell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        (Fixed64)4,
        (Fixed64)4);

    [Fact]
    public void SeamPairRetainedSize_ShouldMatchOneSharedPortalPayload()
    {
        Unsafe.SizeOf<NavigationCellAddress>().Should().Be(24);
        Unsafe.SizeOf<GridNavigationPortal>().Should().Be(GridNavigationPortal.SizeInBytes);
        NavigationAutomaticSeamPair.RetainedSize.Should().Be(
            16L
            + (2L * Unsafe.SizeOf<NavigationCellAddress>())
            + Unsafe.SizeOf<GridNavigationPortal>());
        NavigationAutomaticSeamPairRecord.RetainedSize.Should().Be(32L);
    }

    [Fact]
    public void SeamRefreshFixedRetainedAccounting_ShouldCoverItsExactConstructorAllocations()
    {
        using var world = new GridWorld();
        NavigationOperationFrameChange[] changes = Array.Empty<NavigationOperationFrameChange>();
        NavigationAutomaticSeamRefreshWork? last = null;
        for (int i = 0; i < 512; i++)
        {
            last = new NavigationAutomaticSeamRefreshWork(
                world,
                NavigationWorldGraph.Empty,
                NavigationWorldGraph.Empty,
                changes,
                0);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        const int Iterations = 256;
        for (int i = 0; i < Iterations; i++)
        {
            last = new NavigationAutomaticSeamRefreshWork(
                world,
                NavigationWorldGraph.Empty,
                NavigationWorldGraph.Empty,
                changes,
                0);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(last);

        long expected = NavigationAutomaticSeamRefreshWork.FixedRetainedBytes * Iterations;
        allocated.Should().BeInRange(expected, expected + Iterations - 1,
            "any per-instance allocation drift must exceed the tolerated one-time counter noise");
        last!.RetainedBytes.Should().Be(NavigationAutomaticSeamRefreshWork.FixedRetainedBytes,
            "empty static roots and unattached payloads are not work-owned allocations");
        last.PersistentPageCount.Should().Be(4,
            "the work, two cursors, and one contact array are the fixed WIP objects");
    }

    [Fact]
    public void SeamLifecycleFixedRetainedAccounting_ShouldCoverItsExactConstructorAllocations()
    {
        using var world = new GridWorld();
        GridEventInfo[] events = Array.Empty<GridEventInfo>();
        NavigationAutomaticSeamLifecycleWork? last = null;
        for (int i = 0; i < 512; i++)
        {
            last = new NavigationAutomaticSeamLifecycleWork(
                world,
                NavigationWorldGraph.Empty,
                events,
                0);
        }
        long before = GC.GetAllocatedBytesForCurrentThread();
        const int Iterations = 256;
        for (int i = 0; i < Iterations; i++)
        {
            last = new NavigationAutomaticSeamLifecycleWork(
                world,
                NavigationWorldGraph.Empty,
                events,
                0);
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(last);

        long expectedPerWork =
            NavigationAutomaticSeamLifecycleWork.BaseRetainedBytes
            + NavigationAutomaticSeamRefreshWork.FixedRetainedBytes;
        long expected = expectedPerWork * Iterations;
        allocated.Should().BeInRange(expected, expected + Iterations - 1,
            "any per-instance allocation drift must exceed the tolerated one-time counter noise");
        last!.RetainedBytes.Should().Be(expectedPerWork);
        last.PersistentPageCount.Should().Be(5);
    }

    [Fact]
    public void SeamLifecycleCapacity_ShouldAcceptExactMinimumAndRejectOneByteBelow()
    {
        using var world = new GridWorld();
        GridEventInfo[] events = Array.Empty<GridEventInfo>();
        long exactBytes = NavigationAutomaticSeamLifecycleWork.BaseRetainedBytes
            + NavigationAutomaticSeamRefreshWork.FixedRetainedBytes;
        var exact = new NavigationAutomaticSeamLifecycleWork(
            world,
            NavigationWorldGraph.Empty,
            events,
            0);
        var below = new NavigationAutomaticSeamLifecycleWork(
            world,
            NavigationWorldGraph.Empty,
            events,
            0);

        exact.AdvanceOne(
                new MaintenanceWorkMeter(
                    TrailblazerWorldContextSettings.Default.MaintenanceBudget),
                exactBytes,
                maximumPersistentPages: 5)
            .Should().NotBe(NavigationAutomaticSeamLifecycleWork.AdvanceStatus.CapacityExceeded);
        below.AdvanceOne(
                new MaintenanceWorkMeter(
                    TrailblazerWorldContextSettings.Default.MaintenanceBudget),
                exactBytes - 1,
                maximumPersistentPages: 5)
            .Should().Be(NavigationAutomaticSeamLifecycleWork.AdvanceStatus.CapacityExceeded);
    }

    [Fact]
    public void SeamCandidateProbeBudget_ShouldDebitIndependentlyFromAcceptedEdges()
    {
        var budget = new MaintenanceWorkBudget(
            maxConsumedEnvelopes: 1,
            maxBaselineAddresses: 1,
            maxOverlaySlots: 1,
            maxComponentNodes: 1,
            maxSeamCandidateProbes: 2,
            maxExplicitEdges: 3,
            maxDependencyEntries: 4);
        var meter = new MaintenanceWorkMeter(budget);

        TrailblazerWorldContextSettings.Default.MaintenanceBudget.MaxSeamCandidateProbes
            .Should().BeGreaterThan(0);
        meter.TryConsumeSeamCandidateProbes(2).Should().BeTrue();
        meter.TryConsumeSeamCandidateProbes(1).Should().BeFalse();
        meter.SeamCandidateProbes.Should().Be(2);
        meter.RemainingSeamCandidateProbes.Should().Be(0);
        meter.ExplicitEdges.Should().Be(0,
            "rejected seam candidates cannot consume the accepted-edge budget");
    }

    [Fact]
    public void PositiveAreaOneToManySeams_ShouldPublishEveryDirectedContactCanonically()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        SeamScenario scenario = CreateOneToManyScenario(context);

        using (NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!)
        {
            GetCrossMapTargets(lease.Graph, scenario.Source, "target").Should().Equal(
                scenario.FirstTarget,
                scenario.SecondTarget);
            GetIncomingSources(lease.Graph, scenario.Source, "target").Should().Equal(
                scenario.FirstTarget,
                scenario.SecondTarget);
            lease.Graph.AreInSameSurfaceComponent(
                    scenario.Source,
                    scenario.FirstTarget)
                .Should().BeTrue(
                    "an active automatic seam joins its endpoint nodes in weak membership");
        }

        var remove = new NavigationMapRemoveOperation(
            "target",
            operationSequence: 3,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(remove).Should().BeTrue();
        SimulateUntilTerminal(context, remove.Receipt);
        remove.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        using NavigationWorldGraphLease removed = context.Pathing.TryAcquireNavigationGraph()!;
        GetCrossMapTargets(removed.Graph, scenario.Source, "target").Should().BeEmpty(
            "map removal must invalidate every durable seam incidence row");
    }

    [Theory]
    [InlineData(4, 4, 4)]
    [InlineData(4, 4, 0)]
    [InlineData(2, 0, 0)]
    public void NonFaceContacts_ShouldNeverMaterializeAutomaticEdges(
        int targetXHalves,
        int targetYHalves,
        int targetZHalves)
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics sourceMetrics = GridTopologyMetrics.Rectangular((Fixed64)2);
        GridTopologyMetrics targetMetrics = GridTopologyMetrics.Rectangular((Fixed64)2);
        GridConfiguration sourceConfiguration = CreateConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            sourceMetrics,
            GridStorageKind.Dense);
        var targetCenter = new Vector3d(
            (Fixed64)targetXHalves / (Fixed64)2,
            (Fixed64)targetYHalves / (Fixed64)2,
            (Fixed64)targetZHalves / (Fixed64)2);
        GridConfiguration targetConfiguration = CreateConfiguration(
            targetCenter,
            targetCenter,
            targetMetrics,
            GridStorageKind.Dense);
        NormalizedGridConfiguration source = AddDenseGrid(context, sourceConfiguration);
        NormalizedGridConfiguration target = AddDenseGrid(context, targetConfiguration);
        GridCellGeometry.TryCreateNavigationPortal(
                GetPrism(source, default),
                GetPrism(target, default),
                out _)
            .Should().BeFalse("point, edge, and volume contacts are not navigation faces");
        AdmitMap(context, "source", source, new[] { default(VoxelIndex) }, 1);
        NavigationMapCommitOperation targetOperation =
            AdmitMap(context, "target", target, new[] { default(VoxelIndex) }, 2);
        SimulateUntilTerminal(context, targetOperation.Receipt);

        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        GetCrossMapTargets(
                lease.Graph,
                new NavigationCellAddress("source", default),
                "target")
            .Should().BeEmpty();
    }

    [Fact]
    public void SparsePhysicalPresence_ShouldDormantRemoveAndRestartExactSeams()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        GridConfiguration sourceConfiguration = CreateConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            metrics,
            GridStorageKind.Dense);
        var targetCenter = new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero);
        GridConfiguration targetConfiguration = CreateConfiguration(
            targetCenter,
            targetCenter,
            metrics,
            GridStorageKind.Sparse);
        NormalizedGridConfiguration source = AddDenseGrid(context, sourceConfiguration);
        NormalizedGridConfiguration target = AddSparseGrid(
            context,
            targetConfiguration,
            Array.Empty<VoxelIndex>(),
            out VoxelGrid targetGrid);
        AdmitMap(context, "source", source, new[] { default(VoxelIndex) }, 1);
        NavigationMapCommitOperation targetOperation =
            AdmitMap(context, "target", target, new[] { default(VoxelIndex) }, 2);
        SimulateUntilTerminal(context, targetOperation.Receipt);
        var sourceAddress = new NavigationCellAddress("source", default);
        CountCrossMapTargets(context, sourceAddress, "target").Should().Be(0);
        long dormantComponentVersion;
        using (NavigationWorldGraphLease dormant = context.Pathing.TryAcquireNavigationGraph()!)
        {
            dormant.Graph.SurfaceComponents.TryGet(
                    sourceAddress,
                    out NavigationSurfaceComponent sourceComponent)
                .Should().BeTrue();
            dormant.Graph.SurfaceComponents.TryGet(
                    new NavigationCellAddress("target", default),
                    out NavigationSurfaceComponent targetComponent)
                .Should().BeTrue();
            sourceComponent.Key.Should().Be(targetComponent.Key,
                "physical absence makes the durable seam dormant, not structurally nonexistent");
            dormantComponentVersion = sourceComponent.Version;
        }

        targetGrid.TryAddVoxel(default, out _).Should().BeTrue();
        SimulateUntilCrossMapCount(context, sourceAddress, "target", 1);
        CountCrossMapTargets(context, sourceAddress, "target").Should().Be(1);
        long activeComponentVersion;
        using (NavigationWorldGraphLease active = context.Pathing.TryAcquireNavigationGraph()!)
        {
            active.Graph.SurfaceComponents.TryGet(
                    sourceAddress,
                    out NavigationSurfaceComponent activeComponent)
                .Should().BeTrue();
            activeComponentVersion = activeComponent.Version;
            activeComponentVersion.Should().Be(dormantComponentVersion,
                "physical presence is runtime traversal state and must not rebuild structural components");
        }

        targetGrid.TryRemoveVoxel(default).Should().BeTrue();
        SimulateUntilCrossMapCount(context, sourceAddress, "target", 0);
        CountCrossMapTargets(context, sourceAddress, "target").Should().Be(0);
        using (NavigationWorldGraphLease dormantAgain = context.Pathing.TryAcquireNavigationGraph()!)
        {
            dormantAgain.Graph.SurfaceComponents.TryGet(
                    sourceAddress,
                    out NavigationSurfaceComponent dormantAgainComponent)
                .Should().BeTrue();
            dormantAgainComponent.Version.Should().Be(activeComponentVersion);
        }

        targetGrid.TryAddVoxel(default, out _).Should().BeTrue();
        SimulateUntilCrossMapCount(context, sourceAddress, "target", 1);
        CountCrossMapTargets(context, sourceAddress, "target").Should().Be(1,
            "a high-water change must restart from a fresh cursor generation");
    }

    [Fact]
    public void UnauthoredBoundaryCellSet_ShouldActivateRetainedGeometryWithoutSeamProbe()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        GridConfiguration sourceConfiguration = CreateConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            metrics,
            GridStorageKind.Dense);
        var targetCenter = new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero);
        GridConfiguration targetConfiguration = CreateConfiguration(
            targetCenter,
            targetCenter,
            metrics,
            GridStorageKind.Dense);
        NormalizedGridConfiguration source = AddDenseGrid(context, sourceConfiguration);
        NormalizedGridConfiguration target = AddDenseGrid(context, targetConfiguration);
        AdmitMap(context, "source", source, new[] { default(VoxelIndex) }, 1);
        NavigationMapCommitOperation targetOperation =
            AdmitMap(context, "target", target, Array.Empty<VoxelIndex>(), 2);
        SimulateUntilTerminal(context, targetOperation.Receipt);
        var sourceAddress = new NavigationCellAddress("source", default);
        CountCrossMapTargets(context, sourceAddress, "target").Should().Be(0);

        var activate = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(
                new NavigationOverlayTransaction(
                    new[]
                    {
                        new NavigationMapOverlayDelta(
                            "target",
                            new[] { NavigationCellOverlayOperation.Set(default, SeamCell) })
                    })),
            operationSequence: 3,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(activate).Should().BeTrue();
        for (int frame = 0;
            frame < 512 && activate.Receipt.Status == NavigationOperationStatus.Pending;
            frame++)
        {
            context.Simulate();
            context.Pathing.NavigationMaintenanceMeter.SeamCandidateProbes.Should().Be(0,
                "semantic activation must use retained address dependency incidence");
        }

        activate.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        CountCrossMapTargets(context, sourceAddress, "target").Should().Be(1);
    }

    [Fact]
    public void SuppressAndRevert_ShouldToggleRetainedSeamWithoutCandidateProbe()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        GridConfiguration sourceConfiguration = CreateConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            metrics,
            GridStorageKind.Dense);
        var targetCenter = new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero);
        GridConfiguration targetConfiguration = CreateConfiguration(
            targetCenter,
            targetCenter,
            metrics,
            GridStorageKind.Dense);
        NormalizedGridConfiguration source = AddDenseGrid(context, sourceConfiguration);
        NormalizedGridConfiguration target = AddDenseGrid(context, targetConfiguration);
        AdmitMap(context, "source", source, new[] { default(VoxelIndex) }, 1);
        NavigationMapCommitOperation targetOperation =
            AdmitMap(context, "target", target, new[] { default(VoxelIndex) }, 2);
        SimulateUntilTerminal(context, targetOperation.Receipt);
        var sourceAddress = new NavigationCellAddress("source", default);

        NavigationOverlayCommitOperation suppress = AdmitCellOverlay(
            context,
            "target",
            NavigationCellOverlayOperation.Suppress(default),
            sequence: 3);
        SimulateOverlayWithNoSeamProbes(context, suppress);
        CountCrossMapTargets(context, sourceAddress, "target").Should().Be(0);

        NavigationOverlayCommitOperation revert = AdmitCellOverlay(
            context,
            "target",
            NavigationCellOverlayOperation.RevertToBake(default),
            sequence: 4);
        SimulateOverlayWithNoSeamProbes(context, revert);
        CountCrossMapTargets(context, sourceAddress, "target").Should().Be(1,
            "revert must reactivate the same retained durable geometry pair");
    }

    [Fact]
    public void TwoNewMapsInOneBatch_ShouldResolveExactParticipantsAndSuppressDuplicateDiscovery()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        GridConfiguration sourceConfiguration = CreateConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            metrics,
            GridStorageKind.Dense);
        var targetCenter = new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero);
        GridConfiguration targetConfiguration = CreateConfiguration(
            targetCenter,
            targetCenter,
            metrics,
            GridStorageKind.Dense);
        NormalizedGridConfiguration source = AddDenseGrid(context, sourceConfiguration);
        NormalizedGridConfiguration target = AddDenseGrid(context, targetConfiguration);
        NavigationMapCommitOperation sourceOperation =
            AdmitMap(context, "source", source, new[] { default(VoxelIndex) }, 1);
        NavigationMapCommitOperation targetOperation =
            AdmitMap(context, "target", target, new[] { default(VoxelIndex) }, 2);

        SimulateUntilTerminal(context, targetOperation.Receipt);
        sourceOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        targetOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        var sourceAddress = new NavigationCellAddress("source", default);
        var targetAddress = new NavigationCellAddress("target", default);
        CountCrossMapTargets(context, sourceAddress, "target").Should().Be(1);
        CountCrossMapTargets(context, targetAddress, "source").Should().Be(1,
            "the two incident filtered scans must converge on one durable pair");
    }

    [Fact]
    public void GridRemoveAndRespawn_ShouldRemoveThenRecompileIncidentSeamGeometry()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        GridConfiguration sourceConfiguration = CreateConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            metrics,
            GridStorageKind.Dense);
        var targetCenter = new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero);
        GridConfiguration targetConfiguration = CreateConfiguration(
            targetCenter,
            targetCenter,
            metrics,
            GridStorageKind.Dense);
        context.World.TryAddGrid(sourceConfiguration, out _).Should().BeTrue();
        context.World.TryAddGrid(targetConfiguration, out ushort targetGridIndex).Should().BeTrue();
        sourceConfiguration.TryNormalize(out NormalizedGridConfiguration source).Should().BeTrue();
        targetConfiguration.TryNormalize(out NormalizedGridConfiguration target).Should().BeTrue();
        AdmitMap(context, "source", source, new[] { default(VoxelIndex) }, 1);
        NavigationMapCommitOperation targetOperation =
            AdmitMap(context, "target", target, new[] { default(VoxelIndex) }, 2);
        SimulateUntilTerminal(context, targetOperation.Receipt);
        var sourceAddress = new NavigationCellAddress("source", default);

        context.World.TryRemoveGrid(targetGridIndex).Should().BeTrue();
        SimulateUntilCrossMapCount(context, sourceAddress, "target", 0);
        CountCrossMapTargets(context, sourceAddress, "target").Should().Be(0);

        context.World.TryAddGrid(targetConfiguration, out _).Should().BeTrue();
        bool observedRespawnProbe = false;
        for (int frame = 0;
            frame < 512 && CountCrossMapTargets(context, sourceAddress, "target") == 0;
            frame++)
        {
            context.Simulate();
            observedRespawnProbe |=
                context.Pathing.NavigationMaintenanceMeter.SeamCandidateProbes > 0;
        }
        observedRespawnProbe.Should().BeTrue(
            "a respawned grid generation requires a fresh filtered geometry query");
        CountCrossMapTargets(context, sourceAddress, "target").Should().Be(1);
    }

    [Fact]
    public void GridRemovalLifecycle_ShouldStayAllClosedUntilTinyBudgetPublication()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateTinyBudgetSettings());
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        GridConfiguration sourceConfiguration = CreateConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            metrics,
            GridStorageKind.Dense);
        GridConfiguration targetConfiguration = CreateConfiguration(
            new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
            new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
            metrics,
            GridStorageKind.Dense);
        GridConfiguration unrelatedConfiguration = CreateConfiguration(
            new Vector3d((Fixed64)100, Fixed64.Zero, Fixed64.Zero),
            new Vector3d((Fixed64)100, Fixed64.Zero, Fixed64.Zero),
            metrics,
            GridStorageKind.Dense);
        NormalizedGridConfiguration source = AddDenseGrid(context, sourceConfiguration);
        context.World.TryAddGrid(targetConfiguration, out ushort targetGridIndex).Should().BeTrue();
        targetConfiguration.TryNormalize(out NormalizedGridConfiguration target).Should().BeTrue();
        NormalizedGridConfiguration unrelated = AddDenseGrid(context, unrelatedConfiguration);
        AdmitMap(context, "source", source, new[] { default(VoxelIndex) }, 1);
        AdmitMap(context, "unrelated", unrelated, new[] { default(VoxelIndex) }, 2);
        NavigationMapCommitOperation targetOperation =
            AdmitMap(context, "target", target, new[] { default(VoxelIndex) }, 3);
        SimulateUntilTerminal(context, targetOperation.Receipt);

        context.World.TryRemoveGrid(targetGridIndex).Should().BeTrue();
        context.Simulate();

        context.Pathing.RetainedCompositionWorkCount.Should().Be(1);
        using (NavigationWorldGraphLease pending = context.Pathing.TryAcquireNavigationGraph()!)
        {
            pending.Graph.HasClosedStructuralScope.Should().BeTrue();
            pending.Graph.AreAllStructuralComponentsClosed.Should().BeTrue(
                "unknown lifecycle scope must fail closed before bounded incidence finishes");
            pending.Graph.AutomaticSeams.PairCount.Should().Be(1,
                "the old pair remains private to the closed published source until atomic replacement");
        }

        for (int frame = 0;
            frame < 512 && context.Pathing.RetainedCompositionWorkCount != 0;
            frame++)
        {
            context.Simulate();
        }

        context.Pathing.RetainedCompositionWorkCount.Should().Be(0);
        using NavigationWorldGraphLease published = context.Pathing.TryAcquireNavigationGraph()!;
        published.Graph.HasClosedStructuralScope.Should().BeFalse();
        published.Graph.AutomaticSeams.PairCount.Should().Be(0);
        published.Graph.AreAllStructuralComponentsClosed.Should().BeFalse();
    }

    [Fact]
    public void LifecycleWithoutStructuralLinkChange_ShouldAdvanceGraphVersion()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateTinyBudgetSettings());
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        GridConfiguration configuration = CreateConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            metrics,
            GridStorageKind.Dense);
        context.World.TryAddGrid(configuration, out ushort gridIndex).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMapCommitOperation operation =
            AdmitMap(context, "only", binding, new[] { default(VoxelIndex) }, 1);
        SimulateUntilTerminal(context, operation.Receipt);
        long priorVersion;
        using (NavigationWorldGraphLease prior = context.Pathing.TryAcquireNavigationGraph()!)
            priorVersion = prior.Graph.GraphVersion;

        context.World.TryRemoveGrid(gridIndex).Should().BeTrue();
        context.Simulate();
        for (int frame = 0;
            frame < 512 && context.Pathing.RetainedCompositionWorkCount != 0;
            frame++)
        {
            context.Simulate();
        }

        using NavigationWorldGraphLease published = context.Pathing.TryAcquireNavigationGraph()!;
        published.Graph.AutomaticSeams.PairCount.Should().Be(0);
        published.Graph.GraphVersion.Should().BeGreaterThan(priorVersion,
            "lifecycle publication advances dependency time even without component recompute");
    }

    [Fact]
    public void WorldResetWithMappedZeroPairGraph_ShouldAdvanceGraphVersion()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateTinyBudgetSettings());
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        GridConfiguration configuration = CreateConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            metrics,
            GridStorageKind.Dense);
        NormalizedGridConfiguration binding = AddDenseGrid(context, configuration);
        NavigationMapCommitOperation operation =
            AdmitMap(context, "only", binding, new[] { default(VoxelIndex) }, 1);
        SimulateUntilTerminal(context, operation.Receipt);
        long priorVersion;
        using (NavigationWorldGraphLease prior = context.Pathing.TryAcquireNavigationGraph()!)
        {
            prior.Graph.AutomaticSeams.PairCount.Should().Be(0);
            priorVersion = prior.Graph.GraphVersion;
        }

        context.World.Reset();
        for (int frame = 0; frame < 512; frame++)
        {
            context.Simulate();
            if (context.Pathing.RetainedCompositionWorkCount != 0)
                continue;
            NavigationWorldGraphLease? probe = context.Pathing.TryAcquireNavigationGraph();
            if (probe == null)
                continue;
            bool complete = !probe.Graph.HasClosedStructuralScope
                && probe.Graph.GraphVersion > priorVersion;
            probe.Dispose();
            if (complete)
                break;
        }

        using NavigationWorldGraphLease published = context.Pathing.TryAcquireNavigationGraph()!;
        published.Graph.HasClosedStructuralScope.Should().BeFalse();
        published.Graph.GraphVersion.Should().BeGreaterThan(priorVersion);
    }

    [Fact]
    public void RespawnLifecycle_ShouldRequeueWholePrefixWhenCompletedProbeBecomesStale()
    {
        const int CandidateProbeBudget = 64;
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSeamBudgetSettings(
                maxSeamCandidateProbes: CandidateProbeBudget,
                maxExplicitEdges: 16,
                maxDependencyEntries: 3));
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        GridConfiguration sourceConfiguration = CreateConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            metrics,
            GridStorageKind.Dense);
        GridConfiguration targetConfiguration = CreateConfiguration(
            new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
            new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
            metrics,
            GridStorageKind.Sparse);
        NormalizedGridConfiguration source = AddDenseGrid(context, sourceConfiguration);
        NormalizedGridConfiguration target = AddSparseGrid(
            context,
            targetConfiguration,
            new[] { default(VoxelIndex) },
            out VoxelGrid initialTarget);
        ushort targetGridIndex = initialTarget.GridIndex;
        AdmitMap(context, "source", source, new[] { default(VoxelIndex) }, 1);
        NavigationMapCommitOperation targetOperation =
            AdmitMap(context, "target", target, new[] { default(VoxelIndex) }, 2);
        SimulateUntilTerminal(context, targetOperation.Receipt);
        context.World.TryRemoveGrid(targetGridIndex).Should().BeTrue();
        for (int frame = 0;
            frame < 512 && context.Pathing.RetainedCompositionWorkCount != 0;
            frame++)
        {
            context.Simulate();
        }
        context.World.TryAddGrid(
                targetConfiguration,
                new[] { default(VoxelIndex) },
                out ushort respawnedIndex)
            .Should().BeTrue();
        context.World.TryGetGrid(respawnedIndex, out VoxelGrid? respawned).Should().BeTrue();

        bool invalidatedCompletedProbe = false;
        bool observedRestartedProbe = false;
        for (int frame = 0; frame < 512; frame++)
        {
            context.Simulate();
            MaintenanceWorkMeter meter = context.Pathing.NavigationMaintenanceMeter;
            if (!invalidatedCompletedProbe
                && context.Pathing.RetainedCompositionWorkCount != 0
                && meter.SeamCandidateProbes > 0
                && meter.ExplicitEdges >= 2)
            {
                invalidatedCompletedProbe = true;
                respawned!.TryRemoveVoxel(default).Should().BeTrue();
                continue;
            }
            if (invalidatedCompletedProbe && meter.SeamCandidateProbes > 0)
                observedRestartedProbe = true;
            if (invalidatedCompletedProbe
                && observedRestartedProbe
                && context.Pathing.RetainedCompositionWorkCount == 0)
            {
                break;
            }
        }

        GC.KeepAlive(initialTarget);
        invalidatedCompletedProbe.Should().BeTrue();
        observedRestartedProbe.Should().BeTrue(
            "stale work must requeue its GridAdded prefix and restart with later ingress");
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.HasClosedStructuralScope.Should().BeFalse();
        lease.Graph.AutomaticSeams.PairCount.Should().Be(1,
            "the restarted discovery retains dormant geometry for a physically absent sparse cell");
        CountCrossMapTargets(
                context,
                new NavigationCellAddress("source", default),
                "target")
            .Should().Be(0);
    }

    [Fact]
    public void BlockedLifecycle_ShouldPreservePrefixUntilOverflowRestartsFullRebuild()
    {
        TrailblazerWorldContextSettings budgeted = CreateSeamBudgetSettings(
            maxSeamCandidateProbes: 64,
            maxExplicitEdges: 16,
            maxDependencyEntries: 3);
        var settings = new TrailblazerWorldContextSettings(
            budgeted.OperationLimits,
            budgeted.MaintenanceBudget,
            budgeted.GuideSampleBudget,
            maxIngressEntries: 1,
            budgeted.MaxIngressBytes,
            budgeted.MaxActiveSnapshots,
            budgeted.MaxActiveSnapshotBytes,
            maxRetiredSnapshots: 0,
            budgeted.MaxRetiredSnapshotBytes,
            budgeted.MaxPersistentGraphPages,
            budgeted.MaxDynamicCellSlotsPerMap,
            budgeted.MaxDynamicCellSlots,
            budgeted.NavigationAreaCount,
            budgeted.MaxAreaPolicies,
            budgeted.MaxAreaRulesPerPolicy,
            budgeted.MaxAreaRules,
            budgeted.MaxConcurrentSnapshotLeases,
            budgeted.QueryLimits);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: settings);
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        GridConfiguration sourceConfiguration = CreateConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            metrics,
            GridStorageKind.Dense);
        GridConfiguration targetConfiguration = CreateConfiguration(
            new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
            new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
            metrics,
            GridStorageKind.Sparse);
        context.World.TryAddGrid(sourceConfiguration, out ushort sourceGridIndex).Should().BeTrue();
        context.World.TryGetGrid(sourceGridIndex, out VoxelGrid? sourceGrid).Should().BeTrue();
        sourceConfiguration.TryNormalize(out NormalizedGridConfiguration source).Should().BeTrue();
        NormalizedGridConfiguration target = AddSparseGrid(
            context,
            targetConfiguration,
            new[] { default(VoxelIndex) },
            out VoxelGrid initialTarget);
        AdmitMap(context, "source", source, new[] { default(VoxelIndex) }, 1);
        NavigationMapCommitOperation targetOperation =
            AdmitMap(context, "target", target, new[] { default(VoxelIndex) }, 2);
        SimulateUntilTerminal(context, targetOperation.Receipt);

        context.World.TryRemoveGrid(initialTarget.GridIndex).Should().BeTrue();
        context.Simulate();
        for (int frame = 0;
            frame < 512 && context.Pathing.RetainedCompositionWorkCount != 0;
            frame++)
        {
            context.Simulate();
        }
        context.Pathing.RetainedCompositionWorkCount.Should().Be(0);
        using (NavigationWorldGraphLease removed = context.Pathing.TryAcquireNavigationGraph()!)
            removed.Graph.AutomaticSeams.PairCount.Should().Be(0);
        context.World.TryAddGrid(
                targetConfiguration,
                new[] { default(VoxelIndex) },
                out ushort respawnedIndex)
            .Should().BeTrue();
        context.World.TryGetGrid(respawnedIndex, out VoxelGrid? respawned).Should().BeTrue();

        NavigationWorldGraphLease? blockedLease = null;
        for (int frame = 0; frame < 512 && blockedLease == null; frame++)
        {
            context.Simulate();
            MaintenanceWorkMeter meter = context.Pathing.NavigationMaintenanceMeter;
            if (context.Pathing.RetainedCompositionWorkCount != 0
                && meter.SeamCandidateProbes > 0
                && meter.ExplicitEdges >= 2)
            {
                blockedLease = context.Pathing.TryAcquireNavigationGraph();
            }
        }

        blockedLease.Should().NotBeNull();
        blockedLease!.Graph.HasClosedStructuralScope.Should().BeTrue();
        respawned!.TryRemoveVoxel(default).Should().BeTrue();
        context.Simulate();
        VoxelGrid activeSourceGrid = sourceGrid!;
        activeSourceGrid.TryGetVoxel(default(VoxelIndex), out Voxel? sourceVoxel).Should().BeTrue();
        activeSourceGrid.TryAddObstacle(sourceVoxel!, context.World.AllocateObstacleToken()).Should().BeTrue();
        context.Simulate();
        context.Pathing.RetainedCompositionWorkCount.Should().Be(1,
            "publication pressure may not discard or overwrite retained lifecycle work");
        blockedLease.Dispose();

        bool observedRestartedProbe = false;
        for (int frame = 0; frame < 512; frame++)
        {
            context.Simulate();
            observedRestartedProbe |=
                context.Pathing.NavigationMaintenanceMeter.SeamCandidateProbes > 0;
            if (context.Pathing.RetainedCompositionWorkCount != 0)
                continue;
            NavigationWorldGraphLease? probe = context.Pathing.TryAcquireNavigationGraph();
            if (probe == null)
                continue;
            bool complete = !probe.Graph.HasClosedStructuralScope
                && probe.Graph.AutomaticSeams.PairCount == 1;
            probe.Dispose();
            if (complete)
                break;
        }

        observedRestartedProbe.Should().BeTrue();
        using NavigationWorldGraphLease published = context.Pathing.TryAcquireNavigationGraph()!;
        published.Graph.HasClosedStructuralScope.Should().BeFalse();
        published.Graph.AutomaticSeams.PairCount.Should().Be(1,
            "the retained GridAdded prefix must arm a full rebuild after ingress overflow");
        GetCrossMapTargets(
                published.Graph,
                new NavigationCellAddress("source", default),
                "target")
            .Should().BeEmpty(
                "the rebuilt seam stays dormant while the respawned sparse cell is absent");
    }

    [Fact]
    public void GridLifecycleDrainedUnderSnapshotPressure_ShouldRebuildSeamsAfterLeaseRelease()
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var settings = new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            defaults.MaintenanceBudget,
            defaults.GuideSampleBudget,
            defaults.MaxIngressEntries,
            defaults.MaxIngressBytes,
            defaults.MaxActiveSnapshots,
            defaults.MaxActiveSnapshotBytes,
            maxRetiredSnapshots: 0,
            maxRetiredSnapshotBytes: 1_000_000,
            defaults.MaxPersistentGraphPages,
            defaults.MaxDynamicCellSlotsPerMap,
            defaults.MaxDynamicCellSlots,
            navigationAreaCount: 1,
            maxAreaPolicies: 1,
            maxAreaRulesPerPolicy: 1,
            maxAreaRules: 1,
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits);
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(settings: settings);
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        GridConfiguration sourceConfiguration = CreateConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            metrics,
            GridStorageKind.Dense);
        GridConfiguration targetConfiguration = CreateConfiguration(
            new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
            new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
            metrics,
            GridStorageKind.Dense);
        context.World.TryAddGrid(sourceConfiguration, out _).Should().BeTrue();
        context.World.TryAddGrid(targetConfiguration, out ushort targetGridIndex).Should().BeTrue();
        sourceConfiguration.TryNormalize(out NormalizedGridConfiguration source).Should().BeTrue();
        targetConfiguration.TryNormalize(out NormalizedGridConfiguration target).Should().BeTrue();
        AdmitMap(context, "source", source, new[] { default(VoxelIndex) }, 1);
        NavigationMapCommitOperation targetOperation =
            AdmitMap(context, "target", target, new[] { default(VoxelIndex) }, 2);
        SimulateUntilTerminal(context, targetOperation.Receipt);

        NavigationWorldGraphLease pressureLease = context.Pathing.TryAcquireNavigationGraph()!;
        context.World.TryRemoveGrid(targetGridIndex).Should().BeTrue();
        context.Simulate();
        context.Pathing.TryAcquireNavigationGraph().Should().BeNull(
            "the leased current generation must force the lifecycle event through the pressure drain");
        pressureLease.Dispose();
        for (int frame = 0; frame < 512; frame++)
        {
            context.Simulate();
            if (context.Pathing.RetainedCompositionWorkCount == 0
                && context.Pathing.TryAcquireNavigationGraph() is NavigationWorldGraphLease probe)
            {
                probe.Dispose();
                break;
            }
        }

        using NavigationWorldGraphLease recovered = context.Pathing.TryAcquireNavigationGraph()!;
        recovered.Graph.HasClosedStructuralScope.Should().BeFalse();
        recovered.Graph.AutomaticSeams.PairCount.Should().Be(0,
            "pressure-drained GridRemoved must trigger a final-world seam rebuild, not only a physical resnapshot");
    }

    [Fact]
    public void GridRemovalDuringRetainedComposition_ShouldStayAllClosedUntilFullRebuild()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateTinyBudgetSettings());
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        GridConfiguration sourceConfiguration = CreateConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            metrics,
            GridStorageKind.Dense);
        GridConfiguration targetConfiguration = CreateConfiguration(
            new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
            new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero),
            metrics,
            GridStorageKind.Dense);
        GridConfiguration unrelatedConfiguration = CreateConfiguration(
            new Vector3d((Fixed64)100, Fixed64.Zero, Fixed64.Zero),
            new Vector3d((Fixed64)100, Fixed64.Zero, Fixed64.Zero),
            metrics,
            GridStorageKind.Dense);
        NormalizedGridConfiguration source = AddDenseGrid(context, sourceConfiguration);
        context.World.TryAddGrid(targetConfiguration, out ushort targetGridIndex).Should().BeTrue();
        targetConfiguration.TryNormalize(out NormalizedGridConfiguration target).Should().BeTrue();
        NormalizedGridConfiguration unrelated = AddDenseGrid(context, unrelatedConfiguration);
        AdmitMap(context, "source", source, new[] { default(VoxelIndex) }, 1);
        NavigationMapCommitOperation targetOperation =
            AdmitMap(context, "target", target, new[] { default(VoxelIndex) }, 2);
        SimulateUntilTerminal(context, targetOperation.Receipt);

        NavigationMapCommitOperation unrelatedOperation =
            AdmitMap(context, "unrelated", unrelated, new[] { default(VoxelIndex) }, 3);
        context.Simulate();
        unrelatedOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Pending);
        context.Pathing.RetainedCompositionWorkCount.Should().BeGreaterThan(0);

        context.World.TryRemoveGrid(targetGridIndex).Should().BeTrue();
        context.Simulate();
        context.Pathing.NavigationMaintenanceMeter.ConsumedEnvelopes.Should().BeGreaterThan(0,
            "the retained composition path must consume the generation event under test");
        using (NavigationWorldGraphLease pending = context.Pathing.TryAcquireNavigationGraph()!)
        {
            pending.Graph.HasClosedStructuralScope.Should().BeTrue();
            pending.Graph.AreAllStructuralComponentsClosed.Should().BeTrue(
                "a consumed generation event must widen retained operation safety to all-close");
        }

        for (int frame = 0; frame < 1024; frame++)
        {
            context.Simulate();
            if (unrelatedOperation.Receipt.Status == NavigationOperationStatus.Pending
                || context.Pathing.RetainedCompositionWorkCount != 0)
            {
                continue;
            }
            NavigationWorldGraphLease? probe = context.Pathing.TryAcquireNavigationGraph();
            if (probe == null)
                continue;
            bool complete = !probe.Graph.HasClosedStructuralScope
                && probe.Graph.AutomaticSeams.PairCount == 0;
            probe.Dispose();
            if (complete)
                break;
        }

        unrelatedOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        context.Pathing.RetainedCompositionWorkCount.Should().Be(0);
        using NavigationWorldGraphLease published = context.Pathing.TryAcquireNavigationGraph()!;
        published.Graph.HasClosedStructuralScope.Should().BeFalse();
        published.Graph.AutomaticSeams.PairCount.Should().Be(0,
            "the post-operation idle lifecycle must remove the consumed GridRemoved seam");
    }

    [Fact]
    public void SeamEvaluation_ShouldApplyExactPortalCapacityInclusively()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(
            (Fixed64)2,
            (Fixed64)2,
            (Fixed64)2);
        GridConfiguration sourceConfiguration = CreateConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            metrics,
            GridStorageKind.Dense);
        var targetCenter = new Vector3d((Fixed64)2, Fixed64.Zero, Fixed64.Zero);
        GridConfiguration targetConfiguration = CreateConfiguration(
            targetCenter,
            targetCenter,
            metrics,
            GridStorageKind.Dense);
        NormalizedGridConfiguration sourceBinding = AddDenseGrid(context, sourceConfiguration);
        NormalizedGridConfiguration targetBinding = AddDenseGrid(context, targetConfiguration);
        GridCellGeometry.TryCreateNavigationPortal(
                GetPrism(sourceBinding, default),
                GetPrism(targetBinding, default),
                out GridNavigationPortal expectedPortal)
            .Should().BeTrue();
        AdmitMap(context, "source", sourceBinding, new[] { default(VoxelIndex) }, 1);
        NavigationMapCommitOperation targetOperation =
            AdmitMap(context, "target", targetBinding, new[] { default(VoxelIndex) }, 2);
        SimulateUntilTerminal(context, targetOperation.Receipt);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        var sourceAddress = new NavigationCellAddress("source", default);
        lease.Graph.SurfaceComponents.TryGet(
                sourceAddress,
                out NavigationSurfaceComponent sourceComponent)
            .Should().BeTrue();
        sourceComponent.AllSurfaceEdgesEuclideanCertified.Should().BeTrue();
        NavigationGraphEdge edge = FindCrossMapEdge(lease.Graph, sourceAddress, "target");
        lease.Graph.TryGetNodeRef(sourceAddress, out NavigationNodeRef sourceNode).Should().BeTrue();
        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("seam", 1),
            new[] { new NavigationAreaRule(isAllowed: true, additionalEnterCost: Fixed64.Zero) });

        CreateEvaluator(
                lease.Graph,
                expectedPortal.MaximumHorizontalRadius,
                expectedPortal.MaximumBodyHeight,
                policy)
            .EvaluateEdge(sourceNode, edge, out _)
            .Should().Be(TraversalEvaluationStatus.Passable);
        CreateEvaluator(
                lease.Graph,
                expectedPortal.MaximumHorizontalRadius + Fixed64.MinIncrement,
                expectedPortal.MaximumBodyHeight,
                policy)
            .EvaluateEdge(sourceNode, edge, out _)
            .Should().Be(TraversalEvaluationStatus.Impassable);
    }

    [Fact]
    public void HorizontalSeamReverseEvaluation_ShouldSwapCanonicalPortalFeet()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(
            (Fixed64)2,
            (Fixed64)2,
            (Fixed64)2);
        GridConfiguration lowerConfiguration = CreateConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            metrics,
            GridStorageKind.Dense);
        var upperCenter = new Vector3d(Fixed64.Zero, (Fixed64)2, Fixed64.Zero);
        GridConfiguration upperConfiguration = CreateConfiguration(
            upperCenter,
            upperCenter,
            metrics,
            GridStorageKind.Dense);
        NormalizedGridConfiguration lower = AddDenseGrid(context, lowerConfiguration);
        NormalizedGridConfiguration upper = AddDenseGrid(context, upperConfiguration);
        GridCellGeometry.TryCreateNavigationPortal(
                GetPrism(lower, default),
                GetPrism(upper, default),
                out GridNavigationPortal portal)
            .Should().BeTrue();
        portal.TryResolveProfile(
                Fixed64.Zero,
                Fixed64.One,
                out Vector3d lowerPortalFoot,
                out Vector3d upperPortalFoot)
            .Should().BeTrue();
        lowerPortalFoot.Y.Should().Be(Fixed64.Zero);
        upperPortalFoot.Y.Should().Be(Fixed64.One);
        AdmitMap(context, "lower", lower, new[] { default(VoxelIndex) }, 1);
        NavigationMapCommitOperation upperOperation =
            AdmitMap(context, "upper", upper, new[] { default(VoxelIndex) }, 2);
        SimulateUntilTerminal(context, upperOperation.Receipt);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.SurfaceComponents.TryGet(
                new NavigationCellAddress("lower", default),
                out NavigationSurfaceComponent lowerComponent)
            .Should().BeTrue();
        lowerComponent.AllSurfaceEdgesEuclideanCertified.Should().BeFalse(
                "horizontal seam evaluation omits the portal-foot vertical span");
        var lowerAddress = new NavigationCellAddress("lower", default);
        var upperAddress = new NavigationCellAddress("upper", default);
        NavigationGraphEdge up = FindCrossMapEdge(lease.Graph, lowerAddress, "upper");
        NavigationGraphEdge down = FindCrossMapEdge(lease.Graph, upperAddress, "lower");
        lease.Graph.TryGetNodeRef(lowerAddress, out NavigationNodeRef lowerNode).Should().BeTrue();
        lease.Graph.TryGetNodeRef(upperAddress, out NavigationNodeRef upperNode).Should().BeTrue();
        var policy = new NavigationAreaPolicy(
            new NavigationAreaPolicyKey("seam-reverse", 1),
            new[] { new NavigationAreaRule(isAllowed: true, additionalEnterCost: Fixed64.Zero) });
        var evaluator = new TraversalEvaluator(
            lease.Graph,
            new NavigationAgentProfile(
                new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
                maxStepUp: (Fixed64)2,
                maxDropDown: (Fixed64)2,
                arrivalRadius: Fixed64.Zero,
                allowedMedia: TraversalMedia.Solid,
                capabilities: TraversalCapability.None),
            policy,
            TraversalMedium.Solid);

        evaluator.EvaluateEdge(lowerNode, up, out TraversalEdgeEvidence upEvidence)
            .Should().Be(TraversalEvaluationStatus.Passable);
        evaluator.EvaluateEdge(upperNode, down, out TraversalEdgeEvidence downEvidence)
            .Should().Be(TraversalEvaluationStatus.Passable);
        upEvidence.Cost.Should().Be(Fixed64.One);
        downEvidence.Cost.Should().Be(upEvidence.Cost,
            "reverse traversal must swap the one canonical portal's resolved feet");
    }

    [Fact]
    public void ActiveSeamGeometryReplacement_ShouldRebuildCertificationWithoutCountChange()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(
            (Fixed64)2,
            (Fixed64)2,
            (Fixed64)2);
        GridConfiguration sourceConfiguration = CreateConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            metrics,
            GridStorageKind.Dense);
        var rightCenter = new Vector3d((Fixed64)2, Fixed64.Zero, Fixed64.Zero);
        GridConfiguration rightConfiguration = CreateConfiguration(
            rightCenter,
            rightCenter,
            metrics,
            GridStorageKind.Dense);
        var upperCenter = new Vector3d(Fixed64.Zero, (Fixed64)2, Fixed64.Zero);
        GridConfiguration upperConfiguration = CreateConfiguration(
            upperCenter,
            upperCenter,
            metrics,
            GridStorageKind.Dense);
        NormalizedGridConfiguration source = AddDenseGrid(context, sourceConfiguration);
        NormalizedGridConfiguration right = AddDenseGrid(context, rightConfiguration);
        NormalizedGridConfiguration upper = AddDenseGrid(context, upperConfiguration);
        AdmitMap(context, "source", source, new[] { default(VoxelIndex) }, 1);
        NavigationMapCommitOperation install =
            AdmitMap(context, "target", right, new[] { default(VoxelIndex) }, 2);
        SimulateUntilTerminal(context, install.Receipt);
        using (NavigationWorldGraphLease vertical = context.Pathing.TryAcquireNavigationGraph()!)
        {
            vertical.Graph.AutomaticSeams.PairCount.Should().Be(1);
            vertical.Graph.SurfaceComponents.TryGet(
                    new NavigationCellAddress("source", default),
                    out NavigationSurfaceComponent component)
                .Should().BeTrue();
            component.AllSurfaceEdgesEuclideanCertified.Should().BeTrue();
        }

        NavigationMap replacementMap = new NavigationMapBuilder("target", upper)
            .AddCell(default, SeamCell)
            .Build();
        var replace = new NavigationMapCommitOperation(
            new PreparedNavigationMap(replacementMap, bakeVersion: 2),
            OverlayReplacementPolicy.Clear,
            operationSequence: 3,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(replace).Should().BeTrue();
        SimulateUntilTerminal(context, replace.Receipt);
        replace.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        using NavigationWorldGraphLease horizontal = context.Pathing.TryAcquireNavigationGraph()!;
        horizontal.Graph.TryGetMap("target", out NavigationMapInstance? targetInstance)
            .Should().BeTrue();
        targetInstance!.Map.GridBinding.Key.Should().Be(upper.Key);
        horizontal.Graph.AutomaticSeams.PairCount.Should().Be(1,
            "the durable endpoint pair remains active across the geometry-only replacement");
        horizontal.Graph.AutomaticSeams.TryGetPair(
                new NavigationCellAddress("source", default),
                new NavigationCellAddress("target", default),
                out NavigationAutomaticSeamPair horizontalPair)
            .Should().BeTrue();
        horizontalPair.Portal.FaceKind.Should().Be(VoxelContactFaceKind.Horizontal);
        NavigationPagedSequence<NavigationStructuralLink>.Enumerator links =
            horizontal.Graph.AutomaticSeams.GetStructuralLinks("source").GetEnumerator();
        links.MoveNext().Should().BeTrue();
        links.Current.UncertifiedCount.Should().Be(1);
        horizontal.Graph.SurfaceComponents.TryGet(
                new NavigationCellAddress("source", default),
                out NavigationSurfaceComponent horizontalComponent)
            .Should().BeTrue();
        horizontalComponent.AllSurfaceEdgesEuclideanCertified.Should().BeFalse();
    }

    [Fact]
    public void SurfaceEnumeration_ShouldThreeWayMergeExplicitSeamAndNativeByDurableEndpoint()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        var explicitCenter = new Vector3d((Fixed64)(-1), Fixed64.Zero, Fixed64.Zero);
        GridConfiguration explicitConfiguration = CreateConfiguration(
            explicitCenter,
            explicitCenter,
            metrics,
            GridStorageKind.Dense);
        GridConfiguration sourceConfiguration = CreateConfiguration(
            Vector3d.Zero,
            new Vector3d(Fixed64.Zero, Fixed64.Zero, Fixed64.One),
            metrics,
            GridStorageKind.Dense);
        var seamCenter = new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero);
        GridConfiguration seamConfiguration = CreateConfiguration(
            seamCenter,
            seamCenter,
            metrics,
            GridStorageKind.Dense);
        NormalizedGridConfiguration explicitBinding = AddDenseGrid(context, explicitConfiguration);
        NormalizedGridConfiguration sourceBinding = AddDenseGrid(context, sourceConfiguration);
        NormalizedGridConfiguration seamBinding = AddDenseGrid(context, seamConfiguration);
        VoxelIndex sourceIndex = default;
        VoxelIndex nativeIndex = new(0, 0, 1);
        var explicitAddress = new NavigationCellAddress("a-explicit", default);
        var seamAddress = new NavigationCellAddress("b-seam", default);
        var nativeAddress = new NavigationCellAddress("z-source", nativeIndex);
        GridCellPrism sourcePrism = GetPrism(sourceBinding, sourceIndex);
        GridCellPrism explicitPrism = GetPrism(explicitBinding, default);
        var connection = new NavigationConnection(
            "authored",
            sourceIndex,
            explicitAddress,
            new Vector3d(sourcePrism.Center.X, sourcePrism.VerticalMin, sourcePrism.Center.Z),
            new Vector3d(explicitPrism.Center.X, explicitPrism.VerticalMin, explicitPrism.Center.Z),
            Fixed64.Zero,
            Fixed64.One);
        NavigationMapCommitOperation explicitOperation = AdmitMap(
            context,
            new NavigationMapBuilder("a-explicit", explicitBinding)
                .AddCell(default, SeamCell)
                .Build(),
            1);
        NavigationMapCommitOperation sourceOperation = AdmitMap(
            context,
            new NavigationMapBuilder("z-source", sourceBinding)
                .AddCell(sourceIndex, SeamCell)
                .AddCell(nativeIndex, SeamCell)
                .AddConnection(connection)
                .Build(),
            2);
        NavigationMapCommitOperation seamOperation = AdmitMap(
            context,
            new NavigationMapBuilder("b-seam", seamBinding)
                .AddCell(default, SeamCell)
                .Build(),
            3);
        SimulateUntilTerminal(context, seamOperation.Receipt);
        explicitOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        sourceOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        seamOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        lease.Graph.TryGetNodeRef(
                new NavigationCellAddress("z-source", sourceIndex),
                out NavigationNodeRef sourceNode)
            .Should().BeTrue();
        var endpoints = new List<NavigationCellAddress>();
        var kinds = new List<NavigationGraphEdgeKind>();
        NavigationSurfaceEdgeEnumerator edges = lease.Graph.EnumerateSurfaceEdges(sourceNode);
        while (edges.MoveNext())
        {
            lease.Graph.TryGetNodeAddress(edges.Current.Target, out NavigationCellAddress endpoint)
                .Should().BeTrue();
            endpoints.Add(endpoint);
            kinds.Add(edges.Current.Kind);
        }

        endpoints.Should().Equal(explicitAddress, explicitAddress, seamAddress, nativeAddress);
        kinds.Should().HaveCount(4);
        kinds[0].Should().Be(NavigationGraphEdgeKind.Explicit);
        ((byte)kinds[1]).Should().Be(2, "automatic seams own the third canonical edge kind");
        ((byte)kinds[2]).Should().Be(2);
        kinds[3].Should().Be(NavigationGraphEdgeKind.Native);
    }

    [Fact]
    public void UnrelatedGridSlotOrder_ShouldNotChangeSeamOrderOrCompletionFrame()
    {
        (int firstFrames, List<NavigationCellAddress> firstEdges) =
            RunUnrelatedGridOrderingScenario(addUnrelatedFirst: true);
        (int lastFrames, List<NavigationCellAddress> lastEdges) =
            RunUnrelatedGridOrderingScenario(addUnrelatedFirst: false);

        firstEdges.Should().Equal(new NavigationCellAddress("target", default));
        lastEdges.Should().Equal(firstEdges);
        lastFrames.Should().Be(firstFrames,
            "filtered seam work cannot depend on unrelated GridForge slot order");
    }

    [Fact]
    public void TinyBudgetSeamComposition_ShouldCarryAndPublishAllRowsAtomically()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateTinyBudgetSettings());
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        GridConfiguration sourceConfiguration = CreateConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            metrics,
            GridStorageKind.Dense);
        var targetCenter = new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero);
        GridConfiguration targetConfiguration = CreateConfiguration(
            targetCenter,
            targetCenter,
            metrics,
            GridStorageKind.Dense);
        NormalizedGridConfiguration source = AddDenseGrid(context, sourceConfiguration);
        NormalizedGridConfiguration target = AddDenseGrid(context, targetConfiguration);
        NavigationMapCommitOperation sourceOperation =
            AdmitMap(context, "source", source, new[] { default(VoxelIndex) }, 1);
        SimulateUntilTerminal(context, sourceOperation.Receipt);
        var targetOperation = AdmitMap(
            context,
            "target",
            target,
            new[] { default(VoxelIndex) },
            2);
        var sourceAddress = new NavigationCellAddress("source", default);
        var targetAddress = new NavigationCellAddress("target", default);
        bool observedCarryover = false;
        for (int frame = 0;
            frame < 512 && targetOperation.Receipt.Status == NavigationOperationStatus.Pending;
            frame++)
        {
            context.Simulate();
            context.Pathing.NavigationMaintenanceMeter.ExplicitEdges.Should().BeLessThanOrEqualTo(1);
            context.Pathing.NavigationMaintenanceMeter.DependencyEntries.Should().BeLessThanOrEqualTo(3);
            if (targetOperation.Receipt.Status != NavigationOperationStatus.Pending)
                break;
            observedCarryover = true;
            using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
            int forward = GetCrossMapTargets(lease.Graph, sourceAddress, "target").Count;
            int reverse = GetCrossMapTargets(lease.Graph, targetAddress, "source").Count;
            forward.Should().Be(reverse,
                "an unpublished cursor may not expose one directed seam row ahead of its pair");
            forward.Should().Be(0);
        }

        observedCarryover.Should().BeTrue();
        targetOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        CountCrossMapTargets(context, sourceAddress, "target").Should().Be(1);
        CountCrossMapTargets(context, targetAddress, "source").Should().Be(1);
    }

    [Fact]
    public void CompletedSeamProbe_ShouldRestartWhenGridForgeChangesBeforePublication()
    {
        const int CandidateProbeBudget = 64;
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSeamBudgetSettings(
                maxSeamCandidateProbes: CandidateProbeBudget,
                maxExplicitEdges: 16,
                maxDependencyEntries: 3));
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        GridConfiguration sourceConfiguration = CreateConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            metrics,
            GridStorageKind.Dense);
        var targetCenter = new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero);
        GridConfiguration targetConfiguration = CreateConfiguration(
            targetCenter,
            targetCenter,
            metrics,
            GridStorageKind.Sparse);
        NormalizedGridConfiguration source = AddDenseGrid(context, sourceConfiguration);
        NormalizedGridConfiguration target = AddSparseGrid(
            context,
            targetConfiguration,
            new[] { default(VoxelIndex) },
            out VoxelGrid targetGrid);
        NavigationMapCommitOperation sourceOperation =
            AdmitMap(context, "source", source, new[] { default(VoxelIndex) }, 1);
        SimulateUntilTerminal(context, sourceOperation.Receipt);
        NavigationMapCommitOperation targetOperation =
            AdmitMap(context, "target", target, new[] { default(VoxelIndex) }, 2);
        bool observedCompletedUnpublishedProbe = false;
        for (int frame = 0;
            frame < 512 && targetOperation.Receipt.Status == NavigationOperationStatus.Pending;
            frame++)
        {
            context.Simulate();
            MaintenanceWorkMeter meter = context.Pathing.NavigationMaintenanceMeter;
            meter.SeamCandidateProbes.Should().BeLessThanOrEqualTo(CandidateProbeBudget);
            if (targetOperation.Receipt.Status == NavigationOperationStatus.Pending
                && meter.SeamCandidateProbes > 0
                && meter.ExplicitEdges >= 2)
            {
                observedCompletedUnpublishedProbe = true;
                targetGrid.TryRemoveVoxel(default).Should().BeTrue();
                break;
            }
        }

        observedCompletedUnpublishedProbe.Should().BeTrue(
            "the complete cursor must remain retained while bounded incidence work carries");
        SimulateUntilTerminal(context, targetOperation.Receipt);
        targetOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        var sourceAddress = new NavigationCellAddress("source", default);
        var targetAddress = new NavigationCellAddress("target", default);
        CountCrossMapTargets(context, sourceAddress, "target").Should().Be(0);
        CountCrossMapTargets(context, targetAddress, "source").Should().Be(0,
            "stale completed output must be discarded and restarted at candidate ordinal zero");
    }

    private static SeamScenario CreateOneToManyScenario(TrailblazerWorldContext context)
    {
        GridTopologyMetrics sourceMetrics = GridTopologyMetrics.Rectangular((Fixed64)2);
        GridTopologyMetrics targetMetrics = GridTopologyMetrics.Rectangular(
            (Fixed64)2,
            (Fixed64)2,
            Fixed64.One);
        GridConfiguration sourceConfiguration = CreateConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            sourceMetrics,
            GridStorageKind.Dense);
        var targetMinimum = new Vector3d((Fixed64)2, Fixed64.Zero, -Fixed64.One);
        var targetMaximum = new Vector3d((Fixed64)2, Fixed64.Zero, Fixed64.Zero);
        GridConfiguration targetConfiguration = CreateConfiguration(
            targetMinimum,
            targetMaximum,
            targetMetrics,
            GridStorageKind.Dense);
        NormalizedGridConfiguration source = AddDenseGrid(context, sourceConfiguration);
        NormalizedGridConfiguration target = AddDenseGrid(context, targetConfiguration);
        var sourceAddress = new NavigationCellAddress("source", default);
        var firstTarget = new NavigationCellAddress("target", default);
        var secondTarget = new NavigationCellAddress("target", new VoxelIndex(0, 0, 1));
        GridCellGeometry.TryCreateNavigationPortal(
            GetPrism(source, sourceAddress.Index),
            GetPrism(target, firstTarget.Index),
            out _).Should().BeTrue();
        GridCellGeometry.TryCreateNavigationPortal(
            GetPrism(source, sourceAddress.Index),
            GetPrism(target, secondTarget.Index),
            out _).Should().BeTrue();
        AdmitMap(context, "source", source, new[] { sourceAddress.Index }, 1);
        NavigationMapCommitOperation targetOperation = AdmitMap(
            context,
            "target",
            target,
            new[] { firstTarget.Index, secondTarget.Index },
            2);
        SimulateUntilTerminal(context, targetOperation.Receipt);
        targetOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        return new SeamScenario(sourceAddress, firstTarget, secondTarget);
    }

    private static (int Frames, List<NavigationCellAddress> Edges)
        RunUnrelatedGridOrderingScenario(bool addUnrelatedFirst)
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned(
            settings: CreateSeamBudgetSettings(
                maxSeamCandidateProbes: 1,
                maxExplicitEdges: 2,
                maxDependencyEntries: 4));
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(Fixed64.One);
        GridConfiguration sourceConfiguration = CreateConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            metrics,
            GridStorageKind.Dense);
        var targetCenter = new Vector3d(Fixed64.One, Fixed64.Zero, Fixed64.Zero);
        GridConfiguration targetConfiguration = CreateConfiguration(
            targetCenter,
            targetCenter,
            metrics,
            GridStorageKind.Dense);
        var unrelatedCenter = new Vector3d((Fixed64)100, Fixed64.Zero, Fixed64.Zero);
        GridConfiguration unrelatedConfiguration = CreateConfiguration(
            unrelatedCenter,
            unrelatedCenter,
            metrics,
            GridStorageKind.Dense);
        if (addUnrelatedFirst)
            AddDenseGrid(context, unrelatedConfiguration);
        NormalizedGridConfiguration source = AddDenseGrid(context, sourceConfiguration);
        NormalizedGridConfiguration target = AddDenseGrid(context, targetConfiguration);
        if (!addUnrelatedFirst)
            AddDenseGrid(context, unrelatedConfiguration);
        NavigationMapCommitOperation sourceOperation =
            AdmitMap(context, "source", source, new[] { default(VoxelIndex) }, 1);
        SimulateUntilTerminal(context, sourceOperation.Receipt);
        NavigationMapCommitOperation targetOperation =
            AdmitMap(context, "target", target, new[] { default(VoxelIndex) }, 2);
        int frames = 0;
        while (frames < 512 && targetOperation.Receipt.Status == NavigationOperationStatus.Pending)
        {
            context.Simulate();
            frames++;
        }
        targetOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        return (
            frames,
            GetCrossMapTargets(
                lease.Graph,
                new NavigationCellAddress("source", default),
                "target"));
    }

    private static GridConfiguration CreateConfiguration(
        Vector3d minimum,
        Vector3d maximum,
        GridTopologyMetrics metrics,
        GridStorageKind storage) => new(
            minimum,
            maximum,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: metrics,
            storageKind: storage);

    private static NormalizedGridConfiguration AddDenseGrid(
        TrailblazerWorldContext context,
        GridConfiguration configuration)
    {
        context.World.TryAddGrid(configuration, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        return binding;
    }

    private static NormalizedGridConfiguration AddSparseGrid(
        TrailblazerWorldContext context,
        GridConfiguration configuration,
        VoxelIndex[] physical,
        out VoxelGrid grid)
    {
        context.World.TryAddGrid(configuration, physical, out ushort gridIndex).Should().BeTrue();
        context.World.TryGetGrid(gridIndex, out VoxelGrid? resolved).Should().BeTrue();
        grid = resolved!;
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        return binding;
    }

    private static NavigationMapCommitOperation AdmitMap(
        TrailblazerWorldContext context,
        string mapId,
        NormalizedGridConfiguration binding,
        VoxelIndex[] cells,
        long sequence)
    {
        var builder = new NavigationMapBuilder(mapId, binding);
        for (int i = 0; i < cells.Length; i++)
            builder.AddCell(cells[i], SeamCell);
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(builder.Build(), bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            sequence,
            context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        return operation;
    }

    private static NavigationMapCommitOperation AdmitMap(
        TrailblazerWorldContext context,
        NavigationMap map,
        long sequence)
    {
        var operation = new NavigationMapCommitOperation(
            new PreparedNavigationMap(map, bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            sequence,
            context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        return operation;
    }

    private static NavigationOverlayCommitOperation AdmitCellOverlay(
        TrailblazerWorldContext context,
        string mapId,
        NavigationCellOverlayOperation cell,
        long sequence)
    {
        var operation = new NavigationOverlayCommitOperation(
            new PreparedNavigationOverlay(
                new NavigationOverlayTransaction(
                    new[] { new NavigationMapOverlayDelta(mapId, new[] { cell }) })),
            operationSequence: sequence,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(operation).Should().BeTrue();
        return operation;
    }

    private static void SimulateOverlayWithNoSeamProbes(
        TrailblazerWorldContext context,
        NavigationOverlayCommitOperation operation)
    {
        for (int frame = 0;
            frame < 512 && operation.Receipt.Status == NavigationOperationStatus.Pending;
            frame++)
        {
            context.Simulate();
            context.Pathing.NavigationMaintenanceMeter.SeamCandidateProbes.Should().Be(0,
                "same-binding semantic changes must use retained seam dependencies");
        }
        operation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
    }

    private static GridCellPrism GetPrism(
        NormalizedGridConfiguration binding,
        VoxelIndex index)
    {
        binding.TryGetCellPrism(index, out GridCellPrism prism).Should().BeTrue();
        return prism;
    }

    private static List<NavigationCellAddress> GetCrossMapTargets(
        NavigationWorldGraph graph,
        NavigationCellAddress source,
        string targetMapId)
    {
        var result = new List<NavigationCellAddress>();
        if (!graph.TryGetNodeRef(source, out NavigationNodeRef sourceNode))
            return result;
        NavigationSurfaceEdgeEnumerator edges = graph.EnumerateSurfaceEdges(sourceNode);
        while (edges.MoveNext())
        {
            if (graph.TryGetNodeAddress(edges.Current.Target, out NavigationCellAddress target)
                && string.Equals(target.MapId, targetMapId, StringComparison.Ordinal))
            {
                result.Add(target);
            }
        }
        return result;
    }

    private static List<NavigationCellAddress> GetIncomingSources(
        NavigationWorldGraph graph,
        NavigationCellAddress destination,
        string sourceMapId)
    {
        var result = new List<NavigationCellAddress>();
        if (!graph.TryGetNodeRef(destination, out NavigationNodeRef destinationNode))
            return result;
        var edges = new NavigationSurfaceEdgeEnumerator(
            graph,
            destinationNode,
            incoming: true,
            includeNative: false,
            includeAutomaticSeams: true);
        while (edges.MoveNext())
        {
            if (graph.TryGetNodeAddress(edges.Current.Target, out NavigationCellAddress source)
                && string.Equals(source.MapId, sourceMapId, StringComparison.Ordinal))
            {
                result.Add(source);
            }
        }
        return result;
    }

    private static NavigationGraphEdge FindCrossMapEdge(
        NavigationWorldGraph graph,
        NavigationCellAddress source,
        string targetMapId)
    {
        graph.TryGetNodeRef(source, out NavigationNodeRef sourceNode).Should().BeTrue();
        NavigationSurfaceEdgeEnumerator edges = graph.EnumerateSurfaceEdges(sourceNode);
        while (edges.MoveNext())
        {
            if (graph.TryGetNodeAddress(edges.Current.Target, out NavigationCellAddress target)
                && string.Equals(target.MapId, targetMapId, StringComparison.Ordinal))
            {
                return edges.Current;
            }
        }
        throw new Xunit.Sdk.XunitException("Expected a cross-grid automatic seam edge.");
    }

    private static int CountCrossMapTargets(
        TrailblazerWorldContext context,
        NavigationCellAddress source,
        string targetMapId)
    {
        using NavigationWorldGraphLease lease = context.Pathing.TryAcquireNavigationGraph()!;
        return GetCrossMapTargets(lease.Graph, source, targetMapId).Count;
    }

    private static void SimulateUntilCrossMapCount(
        TrailblazerWorldContext context,
        NavigationCellAddress source,
        string targetMapId,
        int count)
    {
        for (int frame = 0;
            frame < 512 && CountCrossMapTargets(context, source, targetMapId) != count;
            frame++)
        {
            context.Simulate();
        }
    }

    private static void SimulateUntilTerminal(
        TrailblazerWorldContext context,
        NavigationOperationReceipt receipt)
    {
        for (int frame = 0;
            frame < 512 && receipt.Status == NavigationOperationStatus.Pending;
            frame++)
        {
            context.Simulate();
        }
    }

    private static TraversalEvaluator CreateEvaluator(
        NavigationWorldGraph graph,
        Fixed64 radius,
        Fixed64 height,
        NavigationAreaPolicy policy) => new(
            graph,
            new NavigationAgentProfile(
                new KinematicBodyShape(radius, height, Fixed64.Zero),
                maxStepUp: Fixed64.Zero,
                maxDropDown: Fixed64.Zero,
                arrivalRadius: Fixed64.Zero,
                allowedMedia: TraversalMedia.Solid,
                capabilities: TraversalCapability.None),
            policy,
            TraversalMedium.Solid);

    private static TrailblazerWorldContextSettings CreateTinyBudgetSettings()
        => CreateSeamBudgetSettings(
            maxSeamCandidateProbes: 1,
            maxExplicitEdges: 1,
            maxDependencyEntries: 3);

    private static TrailblazerWorldContextSettings CreateSeamBudgetSettings(
        int maxSeamCandidateProbes,
        int maxExplicitEdges,
        int maxDependencyEntries)
    {
        TrailblazerWorldContextSettings defaults = TrailblazerWorldContextSettings.Default;
        var budget = new MaintenanceWorkBudget(
            defaults.MaintenanceBudget.MaxConsumedEnvelopes,
            defaults.MaintenanceBudget.MaxBaselineAddresses,
            defaults.MaintenanceBudget.MaxOverlaySlots,
            maxComponentNodes: 1,
            maxSeamCandidateProbes: maxSeamCandidateProbes,
            maxExplicitEdges: maxExplicitEdges,
            maxDependencyEntries: maxDependencyEntries);
        return new TrailblazerWorldContextSettings(
            defaults.OperationLimits,
            budget,
            defaults.GuideSampleBudget,
            defaults.MaxIngressEntries,
            defaults.MaxIngressBytes,
            defaults.MaxActiveSnapshots,
            defaults.MaxActiveSnapshotBytes,
            defaults.MaxRetiredSnapshots,
            defaults.MaxRetiredSnapshotBytes,
            defaults.MaxPersistentGraphPages,
            defaults.MaxDynamicCellSlotsPerMap,
            defaults.MaxDynamicCellSlots,
            navigationAreaCount: 1,
            maxAreaPolicies: 1,
            maxAreaRulesPerPolicy: 1,
            maxAreaRules: 1,
            defaults.MaxConcurrentSnapshotLeases,
            defaults.QueryLimits);
    }

    private readonly struct SeamScenario
    {
        internal SeamScenario(
            NavigationCellAddress source,
            NavigationCellAddress firstTarget,
            NavigationCellAddress secondTarget)
        {
            Source = source;
            FirstTarget = firstTarget;
            SecondTarget = secondTarget;
        }

        internal NavigationCellAddress Source { get; }

        internal NavigationCellAddress FirstTarget { get; }

        internal NavigationCellAddress SecondTarget { get; }
    }
}

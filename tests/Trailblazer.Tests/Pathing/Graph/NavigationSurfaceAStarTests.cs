//=======================================================================
// NavigationSurfaceAStarTests.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

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
public sealed class NavigationSurfaceAStarTests
{
    private static readonly NavigationCell Cell = new(
        TraversalMedia.Solid,
        TraversalCapability.None,
        default,
        Fixed64.Zero,
        (Fixed64)4,
        (Fixed64)4);

    private static readonly NavigationAreaPolicy Policy = new(
        new NavigationAreaPolicyKey("astar", 1),
        new[] { new NavigationAreaRule(true, Fixed64.Zero) });

    [Fact]
    public void Advance_WithOneSimplificationRay_ShouldRetainOnlyDirectNodeFeet()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 0)
        };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(cells.Length),
                cells,
                "map");
        Vector3d start = NavigationAStarExitTestHarness.GetFoot(fixture.Binding, cells[0]);
        Vector3d end = NavigationAStarExitTestHarness.GetFoot(fixture.Binding, cells[2]);
        PathQuery query = CreateSimplificationQuery(fixture, cells[0], cells[2], 1);

        NavigationAStarExitTestHarness.SearchResult result =
            NavigationAStarExitTestHarness.RunAStar(world, fixture.Graph, query);

        result.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        result.Cost.Should().Be((Fixed64)2);
        result.Payload!.GuidePoints.Should().Equal(
            new NavigationAStarGuidePoint(
                new NavigationCellAddress(fixture.MapId, cells[0]),
                start,
                TraversalMedium.Solid),
            new NavigationAStarGuidePoint(
                new NavigationCellAddress(fixture.MapId, cells[2]),
                end,
                TraversalMedium.Solid));
    }

    [Fact]
    public void Advance_WhenDirectSimplificationRayIsBlocked_ShouldRetainRawGuide()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 0),
            new(2, 0, 1)
        };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                new GridConfiguration(
                    Vector3d.Zero,
                    new Vector3d((Fixed64)3, (Fixed64)2, (Fixed64)2),
                    topologyKind: GridTopologyKind.RectangularPrism,
                    topologyMetrics: GridTopologyMetrics.Rectangular(
                        Fixed64.One,
                        (Fixed64)2,
                        Fixed64.One),
                    storageKind: GridStorageKind.Sparse),
                cells,
                "map");
        NavigationAStarExitTestHarness.SearchResult raw =
            NavigationAStarExitTestHarness.RunAStar(
                world,
                fixture.Graph,
                CreateSimplificationQuery(fixture, cells[0], cells[^1], 0));
        NavigationAStarExitTestHarness.SearchResult simplified =
            NavigationAStarExitTestHarness.RunAStar(
                world,
                fixture.Graph,
                CreateSimplificationQuery(fixture, cells[0], cells[^1], 1));
        NavigationAStarExitTestHarness.SearchResult nearer =
            NavigationAStarExitTestHarness.RunAStar(
                world,
                fixture.Graph,
                CreateSimplificationQuery(fixture, cells[0], cells[^1], 2));

        simplified.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        simplified.Cost.Should().Be(raw.Cost);
        simplified.Payload!.GuidePoints.Should().Equal(raw.Payload!.GuidePoints);
        simplified.Payload.WorldChangeSequence.Should().Be(world.ChangeSequence);
        NavigationAStarGuidePoint[] rawPoints = raw.Payload.GuidePoints;
        Vector3d nearerFoot = NavigationAStarExitTestHarness.GetFoot(
            fixture.Binding,
            cells[2]);
        int rawSuffix = Array.FindIndex(
            rawPoints,
            point => point.Address == new NavigationCellAddress(fixture.MapId, cells[2])
                && point.Position == nearerFoot);
        rawSuffix.Should().BeGreaterThan(0);
        var expected = new NavigationAStarGuidePoint[rawPoints.Length - rawSuffix + 1];
        expected[0] = rawPoints[0];
        Array.Copy(rawPoints, rawSuffix, expected, 1, rawPoints.Length - rawSuffix);
        nearer.Payload!.GuidePoints.Should().Equal(expected,
            "the blocked farthest candidate falls back to the nearer node foot and keeps its raw suffix");
    }

    [Fact]
    public void Advance_WhenOnlyAdjacentPortalBearingCandidateSucceeds_ShouldCompactThatInterval()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(1, 0, 1)
        };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                new GridConfiguration(
                    Vector3d.Zero,
                    new Vector3d((Fixed64)2, (Fixed64)2, (Fixed64)2),
                    topologyKind: GridTopologyKind.RectangularPrism,
                    topologyMetrics: GridTopologyMetrics.Rectangular(
                        Fixed64.One,
                        (Fixed64)2,
                        Fixed64.One),
                    storageKind: GridStorageKind.Sparse),
                cells,
                "adjacent-portal");
        NavigationAStarExitTestHarness.SearchResult raw =
            NavigationAStarExitTestHarness.RunAStar(
                world,
                fixture.Graph,
                CreateSimplificationQuery(fixture, cells[0], cells[^1], 0));
        NavigationAStarExitTestHarness.SearchResult simplified =
            NavigationAStarExitTestHarness.RunAStar(
                world,
                fixture.Graph,
                CreateSimplificationQuery(fixture, cells[0], cells[^1], 2));
        NavigationAStarGuidePoint[] rawPoints = raw.Payload!.GuidePoints;
        Vector3d middleFoot = NavigationAStarExitTestHarness.GetFoot(
            fixture.Binding,
            cells[1]);
        int middleOrdinal = Array.FindIndex(
            rawPoints,
            point => point.Address == new NavigationCellAddress(fixture.MapId, cells[1])
                && point.Position == middleFoot);
        middleOrdinal.Should().BeGreaterThan(1,
            "the adjacent node foot follows its raw portal point");
        var expected = new NavigationAStarGuidePoint[rawPoints.Length - middleOrdinal + 1];
        expected[0] = rawPoints[0];
        Array.Copy(rawPoints, middleOrdinal, expected, 1, rawPoints.Length - middleOrdinal);

        simplified.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        simplified.Payload!.GuidePoints.Should().Equal(expected);
    }

    [Fact]
    public void Advance_WhenFirstSourceExhaustsCandidates_ShouldStillSimplifyFromNextSource()
    {
        using var world = new GridWorld();
        var first = new VoxelIndex(0, 0, 0);
        var witness = new VoxelIndex(0, 0, 1);
        var bend = new VoxelIndex(1, 0, 1);
        var middle = new VoxelIndex(2, 0, 1);
        var last = new VoxelIndex(3, 0, 1);
        VoxelIndex[] cells = { first, witness, bend, middle, last };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                new GridConfiguration(
                    Vector3d.Zero,
                    new Vector3d((Fixed64)4, (Fixed64)2, (Fixed64)2),
                    topologyKind: GridTopologyKind.RectangularPrism,
                    topologyMetrics: GridTopologyMetrics.Rectangular(
                        Fixed64.One,
                        (Fixed64)2,
                        Fixed64.One),
                    storageKind: GridStorageKind.Sparse),
                cells,
                "exhausted-source",
                new[]
                {
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "first-to-bend",
                        first,
                        bend,
                        corridorCost: Fixed64.One,
                        radiusClearance: Fixed64.One,
                        witnesses: new[] { witness })
                });
        NavigationAStarExitTestHarness.SearchResult raw =
            NavigationAStarExitTestHarness.RunAStar(
                world,
                fixture.Graph,
                CreateSimplificationQuery(fixture, first, last, 0));
        NavigationAStarExitTestHarness.SearchResult simplified =
            NavigationAStarExitTestHarness.RunAStar(
                world,
                fixture.Graph,
                CreateSimplificationQuery(fixture, first, last, 4));
        NavigationAStarGuidePoint[] rawPoints = raw.Payload!.GuidePoints;
        Vector3d bendFoot = NavigationAStarExitTestHarness.GetFoot(fixture.Binding, bend);
        int bendOrdinal = Array.FindIndex(
            rawPoints,
            point => point.Address == new NavigationCellAddress(fixture.MapId, bend)
                && point.Position == bendFoot);
        bendOrdinal.Should().BeGreaterThan(1,
            "the first raw interval follows its authored bend");
        var expected = new NavigationAStarGuidePoint[bendOrdinal + 2];
        Array.Copy(rawPoints, expected, bendOrdinal + 1);
        expected[^1] = rawPoints[^1];

        simplified.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        simplified.Cost.Should().Be(raw.Cost);
        simplified.Payload!.GuidePoints.Should().Equal(expected,
            "exhausting the first source preserves only its raw interval before the straight tail shortcut");
    }

    [Fact]
    public void Advance_WhenOptionalFinalizationCeilingDoesNotFit_ShouldRunMandatoryCapture()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 0)
        };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(cells.Length),
                cells,
                "finalization-floor");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        var workspace = new NavigationAStarWorkspace(1, 8, 10, 8, 8, 8, 8);
        using var admission = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        admission.Begin(
            store.TryAcquire()!,
            CreateSimplificationQuery(
                fixture,
                cells[0],
                cells[^1],
                simplificationRays: 0,
                lookupProbes: 8),
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        while (admission.Status == NavigationQueryAdmissionStatus.Pending)
            admission.Advance(64, 16);
        admission.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
        using var search = new NavigationSurfaceAStarWork(
            world,
            store,
            admission.Result,
            workspace,
            admission.RayWork,
            long.MaxValue);

        while (search.Status == NavigationSurfaceAStarStatus.Pending)
            search.Advance(64, 64, 64, 64);

        search.Status.Should().Be(NavigationSurfaceAStarStatus.BudgetExceeded);
        admission.Meter.LookupProbes.Should().Be(8,
            "mandatory dependency capture consumes the remaining probe even when the optional ceiling cannot be reserved");
    }

    [Fact]
    public void Advance_WithOneLookupStep_ShouldCompleteAtomicRayUnionThenYieldBeforeFinalization()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 0)
        };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(cells.Length),
                cells,
                "atomic-unit");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        var workspace = new NavigationAStarWorkspace(1, 8, 10, 8, 8, 8, 8);
        using var admission = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        admission.Begin(
            store.TryAcquire()!,
            CreateSimplificationQuery(fixture, cells[0], cells[^1], 1),
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        while (admission.Status == NavigationQueryAdmissionStatus.Pending)
            admission.Advance(64, 16);
        admission.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
        using var search = new NavigationSurfaceAStarWork(
            world,
            store,
            admission.Result,
            workspace,
            admission.RayWork,
            long.MaxValue);
        int lookupBeforeAtomicUnit;
        int traceBeforeAtomicUnit;
        int coverageBeforeAtomicUnit;
        do
        {
            lookupBeforeAtomicUnit = admission.Meter.LookupProbes;
            traceBeforeAtomicUnit = admission.Meter.TraceIntervals;
            coverageBeforeAtomicUnit = admission.Meter.CoveredVoxelIntervals;
            search.Advance(lookupStepLimit: 1, 64, 64, 64);
        }
        while (admission.Meter.SimplificationRays == 0);

        search.Status.Should().Be(NavigationSurfaceAStarStatus.Pending,
            "the terminal ray decision yields before dependency finalization");
        admission.Meter.LookupProbes.Should().Be(lookupBeforeAtomicUnit + 8,
            "one grid probe, three mapped intervals, and two two-entry dependency passes form the bounded atomic unit");
        admission.Meter.TraceIntervals.Should().Be(traceBeforeAtomicUnit + 3);
        admission.Meter.CoveredVoxelIntervals.Should().Be(coverageBeforeAtomicUnit + 8);
        admission.Meter.SimplificationRays.Should().Be(1);
        int lookupAfterAtomicUnit = admission.Meter.LookupProbes;

        search.Advance(lookupStepLimit: 1, 64, 64, 64)
            .Should().Be(NavigationSurfaceAStarStatus.Pending);
        admission.Meter.LookupProbes.Should().Be(lookupAfterAtomicUnit + 1,
            "the next call starts the separately step-limited final dependency work");
    }

    [Fact]
    public void Advance_WhenWorldMutatesAfterCompletedProofBeforeCapture_ShouldReturnStale()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 0)
        };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(cells.Length),
                cells,
                "map");
        using NavigationWorldGraphStore store =
            NavigationAStarExitTestHarness.CreateStore(fixture.Graph, 2);
        PathQuery query = CreateSimplificationQuery(fixture, cells[0], cells[^1], 1);
        NavigationAStarExitTestHarness.RunAStar(world, fixture.Graph, query)
            .Status.Should().Be(NavigationSurfaceAStarStatus.Success,
                "the measured simplification pass must be JIT-warmed");
        var workspace = new NavigationAStarWorkspace(1, 8, 10, 8, 8, 8, 8);
        using var admission = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        admission.Begin(
            store.TryAcquire()!,
            query,
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        while (admission.Status == NavigationQueryAdmissionStatus.Pending)
            admission.Advance(64, 16);
        admission.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
        using var search = new NavigationSurfaceAStarWork(
            world,
            store,
            admission.Result,
            workspace,
            admission.RayWork,
            long.MaxValue);

        long allocationStart = GC.GetAllocatedBytesForCurrentThread();
        while (search.Status == NavigationSurfaceAStarStatus.Pending
            && admission.Meter.SimplificationRays == 0)
        {
            search.Advance(64, 64, 64, 64);
        }
        long candidateAllocatedBytes =
            GC.GetAllocatedBytesForCurrentThread() - allocationStart;

        admission.Meter.SimplificationRays.Should().Be(1);
        candidateAllocatedBytes.Should().Be(0,
            "a warmed candidate proof must reuse its workspace; only the final immutable payload may allocate");
        search.Status.Should().Be(NavigationSurfaceAStarStatus.Pending,
            "the outer advance must yield after the terminal ray decision");
        GridConfiguration mutation = new(
            new Vector3d((Fixed64)100, Fixed64.Zero, Fixed64.Zero),
            new Vector3d((Fixed64)100, Fixed64.Zero, Fixed64.Zero),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                Fixed64.One,
                Fixed64.One,
                Fixed64.One),
            storageKind: GridStorageKind.Dense);
        world.TryAddGrid(mutation, out _).Should().BeTrue();

        search.Advance(64, 64, 64, 64)
            .Should().Be(NavigationSurfaceAStarStatus.Stale);
        admission.Meter.RemainingLookupProbes.Should().Be(
            query.Budget.MaxLookupProbes - admission.Meter.LookupProbes,
            "terminal cleanup must release the simplification lookup floor");
    }

    [Fact]
    public void FinalizationLookupCeiling_ShouldReserveAtExactBudgetAndRejectOneBelow()
    {
        int ceiling = checked(
            NavigationDependencySortWork.GetMaximumComparisonCount(2, 3) + 5);
        var exact = new NavigationWorkMeter(new NavigationWorkBudget(
            ceiling, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));
        var oneBelow = new NavigationWorkMeter(new NavigationWorkBudget(
            ceiling - 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));

        exact.TrySetLookupReservationFloor(ceiling).Should().BeTrue();
        exact.RemainingLookupProbes.Should().Be(0);
        oneBelow.TrySetLookupReservationFloor(ceiling).Should().BeFalse();
    }

    [Fact]
    public void Advance_WhenOptionalRayBudgetIsUnavailable_ShouldKeepRawSuffixUnbound()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 0)
        };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(cells.Length),
                cells,
                "ray-budget");
        NavigationAStarExitTestHarness.SearchResult raw =
            NavigationAStarExitTestHarness.RunAStar(
                world,
                fixture.Graph,
                CreateSimplificationQuery(fixture, cells[0], cells[^1], 0));
        NavigationAStarExitTestHarness.SearchResult exhausted =
            NavigationAStarExitTestHarness.RunAStar(
                world,
                fixture.Graph,
                CreateSimplificationQuery(
                    fixture,
                    cells[0],
                    cells[^1],
                    simplificationRays: 1,
                    traceIntervals: 0,
                    coveredVoxelIntervals: 0));

        exhausted.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        exhausted.Payload!.GuidePoints.Should().Equal(raw.Payload!.GuidePoints);
        exhausted.Payload.WorldChangeSequence.Should().BeNull(
            "a partial budget-exhausted proof must not bind the raw payload");
    }

    [Fact]
    public void Advance_WhenDirectRayCostsMoreThanAStarSubpath_ShouldRetainRawGuide()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells =
        {
            new(0, 0, 0),
            new(1, 0, 0),
            new(2, 0, 0),
            new(0, 0, 1),
            new(1, 0, 1),
            new(2, 0, 1)
        };
        NavigationCell[] navigationCells =
        {
            NavigationAStarExitTestHarness.Cell,
            NavigationAStarExitTestHarness.ExpensiveCell,
            NavigationAStarExitTestHarness.Cell,
            NavigationAStarExitTestHarness.Cell,
            NavigationAStarExitTestHarness.Cell,
            NavigationAStarExitTestHarness.Cell
        };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                new GridConfiguration(
                    Vector3d.Zero,
                    new Vector3d((Fixed64)3, (Fixed64)2, (Fixed64)2),
                    topologyKind: GridTopologyKind.RectangularPrism,
                    topologyMetrics: GridTopologyMetrics.Rectangular(
                        Fixed64.One,
                        (Fixed64)2,
                        Fixed64.One),
                    storageKind: GridStorageKind.Sparse),
                cells,
                "cost-detour",
                navigationCells);
        NavigationAStarExitTestHarness.SearchResult raw =
            NavigationAStarExitTestHarness.RunAStar(
                world,
                fixture.Graph,
                CreateSimplificationQuery(fixture, cells[0], cells[2], 0));
        NavigationAStarExitTestHarness.SearchResult simplified =
            NavigationAStarExitTestHarness.RunAStar(
                world,
                fixture.Graph,
                CreateSimplificationQuery(fixture, cells[0], cells[2], 1));

        raw.Cost.Should().Be((Fixed64)4);
        raw.Payload!.GuidePoints.Length.Should().BeGreaterThan(2);
        simplified.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        simplified.Cost.Should().Be(raw.Cost);
        simplified.Payload!.GuidePoints.Should().Equal(raw.Payload.GuidePoints,
            "the successful direct ray is cost-ineligible");
        simplified.Payload.WorldChangeSequence.Should().Be(world.ChangeSequence,
            "the completed cost-ineligible proof is merged");
    }

    [Fact]
    public void DependencyUnion_WhenOneUniqueEntryDoesNotFit_ShouldNotMutateTarget()
    {
        var target = new NavigationDependencyWorkspace(1, 1);
        var source = new NavigationDependencyWorkspace(2, 2);
        var existingAddress = new NavigationCellAddress("map", default);
        var extraAddress = new NavigationCellAddress("map", new VoxelIndex(64, 0, 0));
        target.TryRecordComponent(new NavigationSurfaceComponentKey(
                existingAddress,
                TraversalMedium.Solid))
            .Should().BeTrue();
        target.TryRecordPage("map", 0).Should().BeTrue();
        source.TryRecordComponent(new NavigationSurfaceComponentKey(
                existingAddress,
                TraversalMedium.Solid))
            .Should().BeTrue();
        source.TryRecordPage("map", 0).Should().BeTrue();
        source.TryRecordComponent(new NavigationSurfaceComponentKey(
                extraAddress,
                TraversalMedium.Solid))
            .Should().BeTrue();
        source.TryRecordPage("map", 1).Should().BeTrue();
        var meter = new NavigationWorkMeter(new NavigationWorkBudget(
            4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));

        target.TryCountMissing(source, meter, out int components, out int pages)
            .Should().BeTrue();

        components.Should().Be(1);
        pages.Should().Be(1);
        target.CanFit(components, pages).Should().BeFalse();
        target.ComponentCount.Should().Be(1);
        target.PageCount.Should().Be(1);
        target.Components[0].Representative.Should().Be(existingAddress);
        target.Pages[0].Should().Be(new GraphPageDependencyAddress("map", 0));
    }

    [Fact]
    public void DependencyUnion_WhenOnlyDuplicatesRemain_ShouldCommitAtExactFit()
    {
        var target = new NavigationDependencyWorkspace(1, 1);
        var source = new NavigationDependencyWorkspace(1, 1);
        var address = new NavigationCellAddress("map", default);
        var component = new NavigationSurfaceComponentKey(
            address,
            TraversalMedium.Solid);
        target.TryRecordComponent(component).Should().BeTrue();
        target.TryRecordPage("map", 0).Should().BeTrue();
        source.TryRecordComponent(component).Should().BeTrue();
        source.TryRecordPage("map", 0).Should().BeTrue();
        var meter = new NavigationWorkMeter(new NavigationWorkBudget(
            4, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0));

        target.TryCountMissing(source, meter, out int components, out int pages)
            .Should().BeTrue();
        meter.TryConsumeLookupProbes(source.ComponentCount + source.PageCount)
            .Should().BeTrue();
        target.CommitMerge(source);

        components.Should().Be(0);
        pages.Should().Be(0);
        target.ComponentCount.Should().Be(1);
        target.PageCount.Should().Be(1);
        meter.LookupProbes.Should().Be(4);
    }

    [Fact]
    public void Payload_ShouldRetainGuidePointsWithoutALegacyNodeAlias()
    {
        var dependencies = new GraphDependencyStamp(
            Policy.Key,
            Array.Empty<GraphComponentDependency>(),
            Array.Empty<GraphPageDependency>());
        var address = new NavigationCellAddress("map", default);
        var guidePoint = new NavigationAStarGuidePoint(
            address,
            Vector3d.One,
            TraversalMedium.Solid);
        var payload = new NavigationAStarPayload(
            default,
            new[] { guidePoint },
            Array.Empty<NavigationTransitionInstruction>(),
            Fixed64.Zero,
            dependencies,
            null,
            NavigationSurfaceAStarStatus.Success);

        payload.GuidePoints.Should().Equal(guidePoint);
        typeof(NavigationAStarPayload).GetProperty(
                "Nodes",
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
            .Should().BeNull();
    }

    [Fact]
    public void RetainedBytes_ShouldMatchMaximumForEmptyAndPopulatedLogicalLayouts()
    {
        var emptyDependencies = new GraphDependencyStamp(
            Policy.Key,
            new GraphComponentDependency[0],
            new GraphPageDependency[0]);
        var components = new[]
        {
            new GraphComponentDependency(
                new NavigationSurfaceComponentKey(
                    new NavigationCellAddress("map-a", default),
                    TraversalMedium.Solid),
                1),
            new GraphComponentDependency(
                new NavigationSurfaceComponentKey(
                    new NavigationCellAddress("map-b", default),
                    TraversalMedium.Solid),
                2)
        };
        var pages = new[]
        {
            new GraphPageDependency("map-a", 1, 0, 0, 1, 1),
            new GraphPageDependency("map-a", 1, 0, 1, 2, 2),
            new GraphPageDependency("map-b", 2, 0, 0, 3, 3),
            new GraphPageDependency("map-b", 2, 0, 1, 4, 4)
        };
        var populatedDependencies = new GraphDependencyStamp(
            Policy.Key,
            components,
            pages);

        emptyDependencies.RetainedBytes.Should().Be(64L);
        GraphDependencyStamp.GetRetainedBytes(componentCount: 0, pageCount: 0)
            .Should().Be(emptyDependencies.RetainedBytes);
        populatedDependencies.RetainedBytes.Should().Be(400L);
        GraphDependencyStamp.GetRetainedBytes(components.Length, pages.Length)
            .Should().Be(populatedDependencies.RetainedBytes);

        var query = new PathQuery(
            new NavigationEndpoint(Vector3d.Zero),
            new NavigationEndpoint(Vector3d.One),
            Profile(),
            Policy.Key,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            allowTransitions: false);
        var start = new NavigationCellAddress("map-a", default);
        var end = new NavigationCellAddress("map-b", new VoxelIndex(2, 0, 0));
        var key = new NavigationAStarPayloadKey(
            query,
            start,
            end,
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        var emptyPayload = new NavigationAStarPayload(
            key,
            Array.Empty<NavigationAStarGuidePoint>(),
            Array.Empty<NavigationTransitionInstruction>(),
            Fixed64.Zero,
            emptyDependencies,
            null,
            NavigationSurfaceAStarStatus.NoPath);
        var populatedPayload = new NavigationAStarPayload(
            key,
            new[]
            {
                new NavigationAStarGuidePoint(
                    start,
                    Vector3d.Zero,
                    TraversalMedium.Solid),
                new NavigationAStarGuidePoint(
                    new NavigationCellAddress("map-a", new VoxelIndex(1, 0, 0)),
                    Vector3d.One,
                    TraversalMedium.Solid),
                new NavigationAStarGuidePoint(
                    end,
                    Vector3d.One + Vector3d.One,
                    TraversalMedium.Solid)
            },
            Array.Empty<NavigationTransitionInstruction>(),
            Fixed64.One,
            populatedDependencies,
            null,
            NavigationSurfaceAStarStatus.Success);
        var worstPayload = new NavigationAStarPayload(
            key,
            (NavigationAStarGuidePoint[])populatedPayload.GuidePoints.Clone(),
            new NavigationTransitionInstruction[populatedPayload.GuidePoints.Length],
            Fixed64.One,
            populatedDependencies,
            null,
            NavigationSurfaceAStarStatus.Success);

        Unsafe.SizeOf<NavigationAStarPayloadKey>().Should().Be(296);
        Unsafe.SizeOf<NavigationAStarGuidePoint>().Should().Be(56);
        Unsafe.SizeOf<NavigationTransitionInstruction>().Should().Be(160);
        emptyPayload.RetainedBytes.Should().Be(432L);
        NavigationAStarPayload.GetMaximumRetainedBytes(0, 0, 0, 0)
            .Should().Be(emptyPayload.RetainedBytes);
        populatedPayload.RetainedBytes.Should().Be(960L);
        NavigationAStarPayload.GetRetainedBytes(
                populatedPayload.GuidePoints.Length,
                transitionInstructionCount: 0,
                populatedDependencies)
            .Should().Be(populatedPayload.RetainedBytes);
        worstPayload.RetainedBytes.Should().Be(1_464L);
        NavigationAStarPayload.GetMaximumRetainedBytes(
                populatedPayload.GuidePoints.Length,
                worstPayload.TransitionInstructions.Length,
                components.Length,
                pages.Length)
            .Should().Be(worstPayload.RetainedBytes);
    }

    [Fact]
    public void Advance_ShouldFindCanonicalFixedPointNativePathUnderUnitChunks()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d((Fixed64)6, (Fixed64)2, (Fixed64)4),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                (Fixed64)2,
                (Fixed64)2,
                (Fixed64)4),
            storageKind: GridStorageKind.Sparse);
        var addresses = new[]
        {
            new VoxelIndex(0, 0, 0),
            new VoxelIndex(1, 0, 0),
            new VoxelIndex(2, 0, 0)
        };
        world.TryAddGrid(configuration, addresses, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(addresses[0], Cell)
            .AddCell(addresses[1], Cell)
            .AddCell(addresses[2], Cell)
            .Build();
        var prepared = new PreparedNavigationMap(map, bakeVersion: 1);
        var state = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        NavigationMapInstance instance = NavigationMapInstanceTestFactory.Compose(
            world,
            state,
            previous: null,
            instanceVersion: 1);
        NavigationWorldGraph graph = CreateGraph(instance);
        graph.SurfaceComponents.TryGet(
                new NavigationCellAddress("map", addresses[0]),
                TraversalMedium.Solid,
                out NavigationSurfaceComponent component)
            .Should().BeTrue();
        component.AllSurfaceEdgesEuclideanCertified.Should().BeTrue();
        using NavigationWorldGraphStore store = CreateStore(
            graph,
            maxConcurrentLeases: 2);
        NavigationWorldGraphLease? lease = store.TryAcquire();
        lease.Should().NotBeNull();
        binding.TryGetCellPrism(addresses[0], out GridCellPrism startPrism).Should().BeTrue();
        binding.TryGetCellPrism(addresses[2], out GridCellPrism endPrism).Should().BeTrue();
        var query = new PathQuery(
            new NavigationEndpoint(new Vector3d(
                startPrism.Center.X,
                startPrism.VerticalMin,
                startPrism.Center.Z)),
            new NavigationEndpoint(new Vector3d(
                endPrism.Center.X,
                endPrism.VerticalMin,
                endPrism.Center.Z)),
            Profile(),
            Policy.Key,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(
                maxLookupProbes: 64,
                maxEndpointCandidates: 2,
                maxExpandedNodes: 3,
                maxEvaluatedEdges: 6,
                maxConnectionLegs: 0,
                maxTransitionCandidates: 0,
                maxTransitionPairs: 0,
                maxStagedLegAttempts: 0,
                maxTraceIntervals: 0,
                maxCoveredVoxelIntervals: 0,
                maxSimplificationRays: 0),
            allowTransitions: false);
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: 4,
            componentCapacity: 6,
            nodeCapacity: 8,
            rayCoveredAddressCapacity: 8,
            rayTraceIntervalCapacity: 8,
            guidePointCapacity: 8);
        using var admission = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        admission.Begin(
            lease!,
            query,
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        for (int step = 0;
             step < 64 && admission.Status == NavigationQueryAdmissionStatus.Pending;
             step++)
        {
            admission.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 1);
        }
        admission.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
        using var search = new NavigationSurfaceAStarWork(
            world,
            store,
            admission.Result,
            workspace,
            admission.RayWork,
            long.MaxValue);

        for (int step = 0;
             step < 64 && search.Status == NavigationSurfaceAStarStatus.Pending;
             step++)
        {
            search.Advance(
                lookupStepLimit: 1,
                nodeStepLimit: 1,
                edgeStepLimit: 1,
                connectionStepLimit: 1);
        }

        search.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        search.Result.Cost.Should().Be((Fixed64)4);
        search.Result.GuidePoints[0].Address.Should().Be(
            new NavigationCellAddress("map", addresses[0]));
        search.Result.GuidePoints[^1].Address.Should().Be(
            new NavigationCellAddress("map", addresses[2]));
        binding.TryGetCellPrism(addresses[1], out GridCellPrism middlePrism)
            .Should().BeTrue();
        GridCellGeometry.TryCreateNavigationPortal(
                startPrism,
                middlePrism,
                out GridNavigationPortal firstPortal)
            .Should().BeTrue();
        GridCellGeometry.TryCreateNavigationPortal(
                middlePrism,
                endPrism,
                out GridNavigationPortal secondPortal)
            .Should().BeTrue();
        firstPortal.TryResolveProfile(
                Profile().Shape.Radius,
                Profile().Shape.Height,
                out _,
                out Vector3d firstTargetAnchor)
            .Should().BeTrue();
        secondPortal.TryResolveProfile(
                Profile().Shape.Radius,
                Profile().Shape.Height,
                out _,
                out Vector3d secondTargetAnchor)
            .Should().BeTrue();
        Vector3d middleFoot = new(
            middlePrism.Center.X,
            middlePrism.VerticalMin,
            middlePrism.Center.Z);
        var expectedGuidePoints = new[]
        {
            new NavigationAStarGuidePoint(
                new NavigationCellAddress("map", addresses[0]),
                query.Start.Position,
                TraversalMedium.Solid),
            new NavigationAStarGuidePoint(
                new NavigationCellAddress("map", addresses[1]),
                firstTargetAnchor,
                TraversalMedium.Solid),
            new NavigationAStarGuidePoint(
                new NavigationCellAddress("map", addresses[1]),
                middleFoot,
                TraversalMedium.Solid),
            new NavigationAStarGuidePoint(
                new NavigationCellAddress("map", addresses[2]),
                secondTargetAnchor,
                TraversalMedium.Solid),
            new NavigationAStarGuidePoint(
                new NavigationCellAddress("map", addresses[2]),
                query.End.Position,
                TraversalMedium.Solid)
        };
        search.Result.GuidePoints.Should().Equal(expectedGuidePoints);
        workspace.NodeTable.TryGetSlot(
                new NavigationMediumStateRef(
                    admission.Result.Start.Node,
                    admission.Result.StartMedium),
                out int startSlot)
            .Should().BeTrue();
        NavigationAStarNodeRecord startRecord = workspace.NodeTable.GetRecord(startSlot);
        startRecord.Heuristic.Should().Be((Fixed64)4);
        startRecord.EstimatedTotalCost.Should().Be((Fixed64)4);
        admission.Meter.ExpandedNodes.Should().Be(3);
        admission.Meter.EvaluatedEdges.Should().Be(6);
        search.Result.RetainedBytes.Should().BeGreaterThan(0);
        var resultBoundedCache = new NavigationAStarPayloadCache(
            world,
            maxEntries: 0,
            maxReusableBytes: 0,
            maxSinglePayloadBytes: search.Result.RetainedBytes - 1,
            maxActivePayloadBytes: search.Result.RetainedBytes - 1);
        resultBoundedCache.TryReservePayload(
                search.Result.RetainedBytes,
                out NavigationAStarPayloadReservation rejectedReservation)
            .Should().BeFalse("one payload larger than the concrete cache ceiling is rejected");
        rejectedReservation.Should().Be(default(NavigationAStarPayloadReservation));

        long maximumPayloadBytes = NavigationAStarPayload.GetMaximumRetainedBytes(
            workspace.GuidePoints.Length,
            workspace.PathNodes.Length - 1,
            workspace.EndpointComponents.Length,
            workspace.EndpointPages.Length);
        var cache = new NavigationAStarPayloadCache(
            world,
            maxEntries: 1,
            maxReusableBytes: search.Result.RetainedBytes,
            maxSinglePayloadBytes: maximumPayloadBytes);
        NavigationAStarPayloadLease canonicalLease = PublishPayload(cache, store, search.Result);
        canonicalLease.Payload.Should().BeSameAs(search.Result);
        cache.CachedBytes.Should().Be(search.Result.RetainedBytes);
        cache.TryCheckout(search.Result.Key, graph, out NavigationAStarPayloadLease checkoutLease)
            .Should().BeTrue();
        checkoutLease.Payload.Should().BeSameAs(search.Result);
        var duplicate = new NavigationAStarPayload(
            search.Result.Key,
            (NavigationAStarGuidePoint[])search.Result.GuidePoints.Clone(),
            (NavigationTransitionInstruction[])search.Result.TransitionInstructions.Clone(),
            search.Result.Cost,
            search.Result.Dependencies,
            search.Result.WorldChangeSequence,
            search.Result.Status);
        FluentActions.Invoking(() => new NavigationAStarPayload(
                search.Result.Key,
                (NavigationAStarGuidePoint[])search.Result.GuidePoints.Clone(),
                (NavigationTransitionInstruction[])search.Result.TransitionInstructions.Clone(),
                search.Result.Cost,
                search.Result.Dependencies,
                search.Result.WorldChangeSequence,
                NavigationSurfaceAStarStatus.BudgetExceeded))
            .Should().Throw<ArgumentException>(
                "terminal failures must never become reusable payloads");
        NavigationAStarPayloadLease racedLease = PublishPayload(cache, store, duplicate);
        racedLease.Payload.Should().BeSameAs(search.Result,
            "same-key publications converge on one immutable payload");
        NavigationAStarGuideLease? guide = null;
        NavigationGuideLease publicGuide = default;
        using (NavigationAStarQueryWork cachedQuery = BeginReservedQuery(
            world,
            store,
            query,
            workspace,
            cache))
        {
            DrainQuery(cachedQuery, 64);
            cachedQuery.Status.Should().Be(NavigationAStarQueryStatus.Success);
            NavigationAStarPayloadLease queryLease = cachedQuery.TakeResult();
            queryLease.Payload.Should().BeSameAs(search.Result);
            cache.TryCreateGuide(store, queryLease, out guide)
                .Should().Be(NavigationAStarQueryStatus.Success);
            guide.Should().NotBeNull();
            publicGuide = new NavigationGuideLease(guide!);
            publicGuide.TryGetCurrentWaypoint(
                    out NavigationCellAddress waypoint,
                    out Vector3d waypointPosition)
                .Should().Be(NavigationGuideStatus.Success);
            waypoint.Should().Be(new NavigationCellAddress("map", addresses[0]));
            waypointPosition.Should().Be(new Vector3d(
                startPrism.Center.X,
                startPrism.VerticalMin,
                startPrism.Center.Z));
            store.ActiveLeaseCount.Should().Be(0,
                "a guide does not retain its short graph lease between calls");
            publicGuide.TryAdvanceWaypoint().Should().Be(NavigationGuideStatus.Success);
            publicGuide.CurrentWaypointIndex.Should().Be(1);
        }
        store.ActiveLeaseCount.Should().Be(0,
            "cached guide acquisition must not retain the graph snapshot lease");
        for (int i = 0; i < 8; i++)
        {
            cache.TryCheckout(
                    search.Result.Key,
                    graph,
                    out NavigationAStarPayloadLease warmPayloadLease)
                .Should().BeTrue();
            cache.TryCreateGuide(store, warmPayloadLease, out NavigationAStarGuideLease? warmGuide)
                .Should().Be(NavigationAStarQueryStatus.Success);
            new NavigationGuideLease(warmGuide!).Dispose();
        }
        long beforeGuideCheckout = System.GC.GetAllocatedBytesForCurrentThread();
        bool guideCheckoutSucceeded = true;
        for (int i = 0; i < 256; i++)
        {
            if (!cache.TryCheckout(
                    search.Result.Key,
                    graph,
                    out NavigationAStarPayloadLease warmPayloadLease)
                || cache.TryCreateGuide(store, warmPayloadLease, out NavigationAStarGuideLease? warmGuide)
                    != NavigationAStarQueryStatus.Success)
            {
                guideCheckoutSucceeded = false;
                break;
            }
            new NavigationGuideLease(warmGuide!).Dispose();
        }
        long guideCheckoutAllocations =
            System.GC.GetAllocatedBytesForCurrentThread() - beforeGuideCheckout;
        guideCheckoutSucceeded.Should().BeTrue();
        guideCheckoutAllocations.Should().Be(0,
            "warmed guide checkout and return reuse cache-owned lease shells");
        NavigationWorldGraph topologyChanged = graph
            .WithSurfaceComponents(NavigationSurfaceComponentIndex.Empty)
            .WithGraphVersion(graph.GraphVersion + 1);
        store.TryPublish(topologyChanged).Should().Be(NavigationCandidatePublication.Published);
        publicGuide.TryGetCurrentWaypoint(out _, out _)
            .Should().Be(NavigationGuideStatus.Stale);
        publicGuide.Status.Should().Be(NavigationGuideStatus.Stale);
        cache.ActiveLeaseCount.Should().Be(4,
            "a stale guide remains bounded by the active lease ceiling until disposal");
        publicGuide.Dispose();
        cache.ActiveLeaseCount.Should().Be(3);
        cache.TryCheckout(search.Result.Key, topologyChanged, out _).Should().BeFalse();
        cache.Count.Should().Be(0);
        cache.CachedBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(search.Result.RetainedBytes,
            "invalidating a checked-out payload must detach rather than invalidate its leases");
        canonicalLease.Payload.Should().BeSameAs(search.Result);
        checkoutLease.Payload.Should().BeSameAs(search.Result);
        racedLease.Payload.Should().BeSameAs(search.Result);
        racedLease.Dispose();
        checkoutLease.Dispose();
        canonicalLease.Dispose();
        cache.ActiveLeaseCount.Should().Be(0);
        cache.LeasedBytes.Should().Be(0);
        cache.DetachedBytes.Should().Be(0);
        using NavigationWorldGraphStore capacityStore = CreateStore(graph);
        var undersized = new NavigationAStarPayloadCache(
            world,
            maxEntries: 1,
            maxReusableBytes: search.Result.RetainedBytes - 1,
            maxSinglePayloadBytes: search.Result.RetainedBytes - 1);
        undersized.TryReservePayload(search.Result.RetainedBytes, out _).Should().BeFalse();
        undersized.Count.Should().Be(0);

        var detachedOnly = new NavigationAStarPayloadCache(
            world,
            maxEntries: 0,
            maxReusableBytes: 0,
            maxSinglePayloadBytes: search.Result.RetainedBytes,
            maxActivePayloadBytes: search.Result.RetainedBytes);
        NavigationAStarPayloadLease detachedLease = PublishPayload(
            detachedOnly,
            capacityStore,
            search.Result);
        detachedOnly.Count.Should().Be(0);
        detachedOnly.CachedBytes.Should().Be(0);
        detachedOnly.DetachedBytes.Should().Be(search.Result.RetainedBytes);
        detachedOnly.TryReservePayload(duplicate.RetainedBytes, out _).Should().BeFalse(
            "the exact active-payload ceiling cannot retain a second detached result");
        detachedLease.Dispose();
        detachedOnly.DetachedBytes.Should().Be(0);
        NavigationAStarPayloadLease recoveredDetachedLease = PublishPayload(
            detachedOnly,
            capacityStore,
            duplicate);
        recoveredDetachedLease.Dispose();

        var leaseCapped = new NavigationAStarPayloadCache(
            world,
            maxEntries: 1,
            maxReusableBytes: search.Result.RetainedBytes,
            maxSinglePayloadBytes: search.Result.RetainedBytes,
            maxActivePayloadBytes: search.Result.RetainedBytes,
            maxActiveLeases: 1);
        NavigationAStarPayloadLease soleLease = PublishPayload(
            leaseCapped,
            capacityStore,
            search.Result);
        leaseCapped.TryCheckout(search.Result.Key, graph, out _).Should().BeFalse(
            "same-payload checkout count is independently bounded from retained bytes");
        soleLease.Dispose();
        leaseCapped.TryCheckout(
                search.Result.Key,
                graph,
                out NavigationAStarPayloadLease recoveredLease)
            .Should().BeTrue();
        recoveredLease.Dispose();

        NavigationAStarPayload second = ClonePayload(
            search.Result,
            new NavigationCellAddress("map", new VoxelIndex(3, 0, 0)));
        NavigationAStarPayload third = ClonePayload(
            search.Result,
            new NavigationCellAddress("map", new VoxelIndex(4, 0, 0)));
        long twoPayloadBytes = checked(search.Result.RetainedBytes + second.RetainedBytes);
        var lru = new NavigationAStarPayloadCache(
            world,
            maxEntries: 2,
            maxReusableBytes: twoPayloadBytes,
            maxSinglePayloadBytes: search.Result.RetainedBytes);
        NavigationAStarPayloadLease firstLease = PublishPayload(lru, capacityStore, search.Result);
        NavigationAStarPayloadLease secondLease = PublishPayload(lru, capacityStore, second);
        firstLease.Dispose();
        lru.TryCheckout(search.Result.Key, graph, out NavigationAStarPayloadLease recentLease)
            .Should().BeTrue();
        recentLease.Dispose();
        NavigationAStarPayloadLease thirdLease = PublishPayload(lru, capacityStore, third);

        lru.TryCheckout(second.Key, graph, out _).Should().BeFalse(
            "the least-recently-used entry is evicted deterministically");
        lru.TryCheckout(search.Result.Key, graph, out NavigationAStarPayloadLease retainedLease)
            .Should().BeTrue();
        retainedLease.Dispose();
        secondLease.Payload.Should().BeSameAs(second,
            "an evicted active payload remains valid until its final lease returns");
        lru.DetachedBytes.Should().Be(second.RetainedBytes);
        secondLease.Dispose();
        lru.DetachedBytes.Should().Be(0);
        thirdLease.Dispose();
        lru.ActiveLeaseCount.Should().Be(0);
        for (int i = 0; i < 8; i++)
        {
            lru.TryCheckout(
                    search.Result.Key,
                    graph,
                    out NavigationAStarPayloadLease warmLease)
                .Should().BeTrue();
            warmLease.Dispose();
        }
        long beforeCheckout = System.GC.GetAllocatedBytesForCurrentThread();
        bool checkoutSucceeded = true;
        for (int i = 0; i < 256; i++)
        {
            if (!lru.TryCheckout(
                    search.Result.Key,
                    graph,
                    out NavigationAStarPayloadLease hotLease))
            {
                checkoutSucceeded = false;
                break;
            }
            hotLease.Dispose();
        }
        long checkoutAllocations =
            System.GC.GetAllocatedBytesForCurrentThread() - beforeCheckout;
        checkoutSucceeded.Should().BeTrue();
        checkoutAllocations.Should().Be(0,
            "warmed cache-hit checkout and return use a cache-owned lease shell");
    }

    [Theory]
    [InlineData(2, (int)NavigationSurfaceAStarStatus.CapacityExceeded)]
    [InlineData(3, (int)NavigationSurfaceAStarStatus.Success)]
    public void Advance_ShouldEnforceExactGuidePointCapacity(
        int guidePointCapacity,
        int expectedStatusValue)
    {
        using var world = new GridWorld();
        VoxelIndex start = default;
        var end = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                new GridConfiguration(
                    Vector3d.Zero,
                    new Vector3d((Fixed64)4, (Fixed64)2, (Fixed64)2),
                    topologyKind: GridTopologyKind.RectangularPrism,
                    topologyMetrics: GridTopologyMetrics.Rectangular(
                        (Fixed64)2,
                        (Fixed64)2,
                        (Fixed64)2),
                    storageKind: GridStorageKind.Sparse),
                new[] { start, end },
                "capacity");
        using NavigationWorldGraphStore store = CreateStore(fixture.Graph);
        NavigationWorldGraphLease lease = store.TryAcquire()!;
        var workspace = new NavigationAStarWorkspace(
            1,
            4,
            6,
            2,
            4,
            4,
            guidePointCapacity);
        using var admission = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        admission.Begin(
            lease,
            fixture.CreateQuery(start, end, fixture.DefaultProfile),
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        while (admission.Status == NavigationQueryAdmissionStatus.Pending)
            admission.Advance(64, 8);
        admission.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
        using var search = new NavigationSurfaceAStarWork(
            world,
            store,
            admission.Result,
            workspace,
            admission.RayWork,
            long.MaxValue);

        while (search.Status == NavigationSurfaceAStarStatus.Pending)
            search.Advance(64, 64, 64, 64);

        search.Status.Should().Be((NavigationSurfaceAStarStatus)expectedStatusValue);
        if (search.Status == NavigationSurfaceAStarStatus.Success)
            search.Result.GuidePoints.Should().HaveCount(3);
    }

    [Fact]
    public void Payload_ShouldReplayAZeroWitnessExplicitPortalWithoutWitnessFeet()
    {
        using var world = new GridWorld();
        VoxelIndex source = default;
        var destination = new VoxelIndex(1, 0, 0);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { source, destination },
                "zero-witness",
                new[]
                {
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "preferred",
                        source,
                        destination,
                        corridorCost: Fixed64.Zero,
                        radiusClearance: Fixed64.One)
                });
        NavigationAStarExitTestHarness.SearchResult result =
            NavigationAStarExitTestHarness.RunAStar(
                world,
                fixture.Graph,
                fixture.CreateQuery(source, destination, fixture.DefaultProfile));
        GridCellPrism sourcePrism = GetPrism(fixture.Binding, source);
        GridCellPrism targetPrism = GetPrism(fixture.Binding, destination);
        GridCellGeometry.TryCreateNavigationPortal(
                sourcePrism,
                targetPrism,
                out GridNavigationPortal portal)
            .Should().BeTrue();

        result.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        result.Cost.Should().Be(Fixed64.Zero);
        result.Payload!.GuidePoints.Should().Equal(
            new NavigationAStarGuidePoint(
                new NavigationCellAddress(fixture.MapId, source),
                NavigationAStarExitTestHarness.GetFoot(fixture.Binding, source),
                TraversalMedium.Solid),
            new NavigationAStarGuidePoint(
                new NavigationCellAddress(fixture.MapId, destination),
                portal.CanonicalFacePoint,
                TraversalMedium.Solid),
            new NavigationAStarGuidePoint(
                new NavigationCellAddress(fixture.MapId, destination),
                NavigationAStarExitTestHarness.GetFoot(fixture.Binding, destination),
                TraversalMedium.Solid));
    }

    [Fact]
    public void Search_ShouldRejectAPositiveRadiusCornerClippingExplicitEntryLegAndUseTheNativeAlternative()
    {
        using var world = new GridWorld();
        VoxelIndex source = default;
        var destination = new VoxelIndex(1, 0, 0);
        Vector3d blockedOffset = new(
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Half);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { source, destination },
                "corner-alternative",
                new[]
                {
                    new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
                        "blocked-corner",
                        source,
                        destination,
                        corridorCost: Fixed64.Zero,
                        radiusClearance: Fixed64.One,
                        entryOffset: blockedOffset)
                });
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                Fixed64.One / (Fixed64)4,
                Fixed64.One,
                Fixed64.Zero),
            maxStepUp: Fixed64.Zero,
            maxDropDown: Fixed64.Zero,
            arrivalRadius: Fixed64.Zero,
            allowedMedia: TraversalMedia.Solid,
            capabilities: TraversalCapability.None);

        NavigationAStarExitTestHarness.SearchResult result =
            NavigationAStarExitTestHarness.RunAStar(
                world,
                fixture.Graph,
                fixture.CreateQuery(source, destination, profile));
        Vector3d blockedAnchor = NavigationAStarExitTestHarness.GetFoot(
                fixture.Binding,
                source)
            + blockedOffset;

        result.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        result.Cost.Should().Be(Fixed64.One);
        result.Payload!.GuidePoints.Should().NotContain(
            point => point.Position == blockedAnchor,
            "the cheaper explicit endpoint leg clips a non-selected wall");
    }

    [Fact]
    public void Search_ShouldRejectAnExplicitExitToTargetFootLegThatClipsTheOppositeWall()
    {
        using var world = new GridWorld();
        var sourceConfiguration = new GridConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                (Fixed64)3 / (Fixed64)2,
                (Fixed64)2,
                Fixed64.One),
            storageKind: GridStorageKind.Dense);
        var targetCenter = new Vector3d(
            Fixed64.One,
            Fixed64.Zero,
            Fixed64.Zero);
        var targetConfiguration = new GridConfiguration(
            targetCenter,
            targetCenter,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                Fixed64.Half,
                (Fixed64)2,
                Fixed64.One),
            storageKind: GridStorageKind.Dense);
        world.TryAddGrid(sourceConfiguration, out _).Should().BeTrue();
        world.TryAddGrid(targetConfiguration, out _).Should().BeTrue();
        sourceConfiguration.TryNormalize(out NormalizedGridConfiguration sourceBinding)
            .Should().BeTrue();
        targetConfiguration.TryNormalize(out NormalizedGridConfiguration targetBinding)
            .Should().BeTrue();
        GridCellPrism sourcePrism = GetPrism(sourceBinding, default);
        GridCellPrism targetPrism = GetPrism(targetBinding, default);
        GridCellGeometry.TryCreateNavigationPortal(
                sourcePrism,
                targetPrism,
                out GridNavigationPortal portal)
            .Should().BeTrue();
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(
                (Fixed64)3 / (Fixed64)10,
                Fixed64.One,
                Fixed64.Zero),
            maxStepUp: Fixed64.Zero,
            maxDropDown: Fixed64.Zero,
            arrivalRadius: Fixed64.Zero,
            allowedMedia: TraversalMedia.Solid,
            capabilities: TraversalCapability.None);
        portal.TryResolveProfile(
                profile.Shape.Radius,
                profile.Shape.Height,
                out Vector3d sourcePortalAnchor,
                out Vector3d targetPortalAnchor)
            .Should().BeTrue();
        Vector3d exitAnchor = targetPortalAnchor + new Vector3d(
            Fixed64.One / (Fixed64)20,
            Fixed64.Zero,
            Fixed64.Zero);
        var targetFoot = new Vector3d(
            targetPrism.Center.X,
            targetPrism.VerticalMin,
            targetPrism.Center.Z);

        GridCellGeometry.IsNavigationBodySegmentValid(
                targetPrism,
                targetPortalAnchor,
                exitAnchor,
                profile.Shape.Radius,
                profile.Shape.Height,
                portal,
                default,
                GridNavigationBodySegmentEndpointAllowance.None)
            .Should().BeTrue("the target-side portal-to-exit leg must reach the predicate under test");
        GridCellGeometry.IsNavigationBodySegmentValid(
                targetPrism,
                exitAnchor,
                targetFoot,
                profile.Shape.Radius,
                profile.Shape.Height,
                portal,
                default,
                GridNavigationBodySegmentEndpointAllowance.None)
            .Should().BeFalse("the target foot is closer than the body radius to the opposite wall");

        NavigationMapInstance Compose(string mapId, NormalizedGridConfiguration binding)
        {
            NavigationMap map = new NavigationMapBuilder(mapId, binding)
                .AddCell(default, Cell)
                .Build();
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

        NavigationMapInstance sourceInstance = Compose("A", sourceBinding);
        NavigationMapInstance targetInstance = Compose("B", targetBinding);
        var definition = new NavigationConnection(
            "blocked-exit",
            default,
            new NavigationCellAddress("B", default),
            sourcePortalAnchor,
            exitAnchor,
            portalRadiusClearance: Fixed64.One,
            portalHeightClearance: (Fixed64)2);
        var portalBuilder = new NavigationPagedSequence<GridNavigationPortal>.Builder(
            GridNavigationPortal.SizeInBytes);
        portalBuilder.Append(portal);
        var record = new NavigationExplicitConnectionRecord(
            new NavigationConnectionOwnerKey("A", definition.Id),
            definition,
            isActive: true,
            corridorCost: Fixed64.Zero,
            portalBuilder.Seal());
        NavigationExplicitConnectionIndex connections =
            NavigationExplicitConnectionIndex.Empty.SetOwner(record, out _);
        var rowBuilder =
            new NavigationPagedSequence<NavigationConnectionOwnerKey>.Builder(16);
        rowBuilder.Append(record.Owner);
        NavigationPagedSequence<NavigationConnectionOwnerKey> row = rowBuilder.Seal();
        connections = connections.SetEndpointRow(
            record.Source,
            NavigationPagedSequence<NavigationConnectionOwnerKey>.Empty,
            row,
            out _);
        connections = connections.SetEndpointRow(
            record.Destination,
            NavigationPagedSequence<NavigationConnectionOwnerKey>.Empty,
            row,
            out _);
        NavigationWorldGraph graph = CreateGraph(
            new[] { sourceInstance, targetInstance },
            connections);
        Vector3d sourceFoot = new(
            sourcePrism.Center.X,
            sourcePrism.VerticalMin,
            sourcePrism.Center.Z);
        var query = new PathQuery(
            new NavigationEndpoint(sourceFoot, mapId: "A"),
            new NavigationEndpoint(targetFoot, mapId: "B"),
            profile,
            Policy.Key,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(128, 2, 2, 2, 2, 0, 0, 0, 0, 0, 0),
            allowTransitions: false);

        NavigationAStarExitTestHarness.SearchResult result =
            NavigationAStarExitTestHarness.RunAStar(world, graph, query);

        result.Status.Should().Be(NavigationSurfaceAStarStatus.NoPath);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EqualCostParallelExplicitEdges_ShouldReplayTheCanonicalEdgeGeometry(
        bool reverseDefinitionOrder)
    {
        using var world = new GridWorld();
        VoxelIndex source = default;
        var destination = new VoxelIndex(1, 0, 0);
        Vector3d canonicalOffset = new(
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.One / (Fixed64)4);
        Vector3d alternateOffset = -canonicalOffset;
        var canonical = new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
            "alpha",
            source,
            destination,
            corridorCost: Fixed64.Zero,
            radiusClearance: Fixed64.One,
            entryOffset: canonicalOffset,
            exitOffset: canonicalOffset);
        var alternate = new NavigationAStarExitTestHarness.ExplicitEdgeSpec(
            "zeta",
            source,
            destination,
            corridorCost: Fixed64.Zero,
            radiusClearance: Fixed64.One,
            entryOffset: alternateOffset,
            exitOffset: alternateOffset);
        NavigationAStarExitTestHarness.ExplicitEdgeSpec[] edges = reverseDefinitionOrder
            ? new[] { alternate, canonical }
            : new[] { canonical, alternate };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateExplicitMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(2),
                new[] { source, destination },
                "parallel-geometry",
                edges);

        NavigationAStarExitTestHarness.SearchResult result =
            NavigationAStarExitTestHarness.RunAStar(
                world,
                fixture.Graph,
                fixture.CreateQuery(source, destination, fixture.DefaultProfile));
        Vector3d sourceFoot = NavigationAStarExitTestHarness.GetFoot(
            fixture.Binding,
            source);
        Vector3d targetFoot = NavigationAStarExitTestHarness.GetFoot(
            fixture.Binding,
            destination);

        result.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        result.Cost.Should().Be(Fixed64.Half);
        result.Payload!.GuidePoints.Should().Contain(
            point => point.Position == sourceFoot + canonicalOffset);
        result.Payload.GuidePoints.Should().Contain(
            point => point.Position == targetFoot + canonicalOffset);
        result.Payload.GuidePoints.Should().NotContain(
            point => point.Position == sourceFoot + alternateOffset
                || point.Position == targetFoot + alternateOffset,
            "equal-cost parallel geometry is reconstructed by the exact canonical edge ordinal");
    }

    [Fact]
    public void Advance_ShouldTraverseAutomaticSeamAndCaptureBothMapPages()
    {
        using TrailblazerWorldContext context = TrailblazerWorldContext.CreateOwned();
        GridTopologyMetrics metrics = GridTopologyMetrics.Rectangular(
            (Fixed64)2,
            (Fixed64)2,
            (Fixed64)2);
        GridConfiguration sourceConfiguration = new(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Dense);
        var targetCenter = new Vector3d((Fixed64)2, Fixed64.Zero, Fixed64.Zero);
        GridConfiguration targetConfiguration = new(
            targetCenter,
            targetCenter,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: metrics,
            storageKind: GridStorageKind.Dense);
        context.World.TryAddGrid(sourceConfiguration, out _).Should().BeTrue();
        context.World.TryAddGrid(targetConfiguration, out _).Should().BeTrue();
        sourceConfiguration.TryNormalize(out NormalizedGridConfiguration sourceBinding)
            .Should().BeTrue();
        targetConfiguration.TryNormalize(out NormalizedGridConfiguration targetBinding)
            .Should().BeTrue();
        NavigationMapCommitOperation sourceOperation = new(
            new PreparedNavigationMap(
                new NavigationMapBuilder("source", sourceBinding)
                    .AddCell(default, Cell)
                    .Build(),
                bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 1,
            effectiveFrame: context.FrameCount + 1);
        NavigationMapCommitOperation targetOperation = new(
            new PreparedNavigationMap(
                new NavigationMapBuilder("target", targetBinding)
                    .AddCell(default, Cell)
                    .Build(),
                bakeVersion: 1),
            OverlayReplacementPolicy.Clear,
            operationSequence: 2,
            effectiveFrame: context.FrameCount + 1);
        context.Pathing.Admit(sourceOperation).Should().BeTrue();
        context.Pathing.Admit(targetOperation).Should().BeTrue();
        for (int frame = 0;
            frame < 512 && targetOperation.Receipt.Status == NavigationOperationStatus.Pending;
            frame++)
        {
            context.Simulate();
        }
        sourceOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);
        targetOperation.Receipt.Status.Should().Be(NavigationOperationStatus.Applied);

        NavigationAreaCatalog.Empty.TryPublish(
                Policy,
                maxPolicies: 1,
                requiredRuleCount: 1,
                maxRulesPerPolicy: 1,
                maxRules: 1,
                out NavigationAreaCatalog catalog)
            .Should().Be(NavigationOperationRejection.None);
        NavigationWorldGraph graph;
        using (NavigationWorldGraphLease published =
            context.Pathing.TryAcquireNavigationGraph()!)
        {
            published.Graph.AutomaticSeams.PairCount.Should().Be(1);
            graph = published.Graph.WithAreaCatalog(
                catalog,
                published.Graph.GraphVersion);
        }
        using NavigationWorldGraphStore store = CreateStore(graph);
        GridCellPrism sourcePrism = GetPrism(sourceBinding, default);
        GridCellPrism targetPrism = GetPrism(targetBinding, default);
        var query = new PathQuery(
            new NavigationEndpoint(new Vector3d(
                sourcePrism.Center.X,
                sourcePrism.VerticalMin,
                sourcePrism.Center.Z)),
            new NavigationEndpoint(new Vector3d(
                targetPrism.Center.X,
                targetPrism.VerticalMin,
                targetPrism.Center.Z)),
            Profile(),
            Policy.Key,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(64, 2, 2, 2, 0, 0, 0, 0, 0, 0, 0),
            allowTransitions: false);
        var workspace = new NavigationAStarWorkspace(2, 4, 6, 4, 4, 4, 4);
        var cache = new NavigationAStarPayloadCache(context.World, 1);
        using NavigationAStarQueryWork queryWork = BeginReservedQuery(
            context.World,
            store,
            query,
            workspace,
            cache);
        DrainQuery(queryWork, 64);
        queryWork.Status.Should().Be(NavigationAStarQueryStatus.Success);
        NavigationAStarPayloadLease payloadLease = queryWork.TakeResult();
        NavigationAStarPayload payload = payloadLease.Payload;
        payload.GuidePoints[0].Address.Should().Be(
            new NavigationCellAddress("source", default));
        payload.GuidePoints[^1].Address.Should().Be(
            new NavigationCellAddress("target", default));
        payload.Dependencies.Pages.Should().Contain(
            dependency => dependency.MapId == "source");
        payload.Dependencies.Pages.Should().Contain(
            dependency => dependency.MapId == "target");
        payloadLease.Dispose();
        store.ActiveLeaseCount.Should().Be(0);
    }

    [Fact]
    public void Payload_ShouldRetainBothDirectedHorizontalSeamAnchors()
    {
        using NavigationAStarExitTestHarness.SeamFixture fixture =
            NavigationAStarExitTestHarness.CreateAutomaticSeam(stacked: true);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            maxStepUp: (Fixed64)2,
            maxDropDown: (Fixed64)2,
            arrivalRadius: Fixed64.Zero,
            allowedMedia: TraversalMedia.Solid,
            capabilities: TraversalCapability.None);
        PathQuery query = fixture.CreateQuery(profile);

        NavigationAStarExitTestHarness.SearchResult result =
            NavigationAStarExitTestHarness.RunAStar(
                fixture.Context.World,
                fixture.Graph,
                query);

        result.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        result.Payload.Should().NotBeNull();
        var sourceAddress = new NavigationCellAddress("source", default);
        var targetAddress = new NavigationCellAddress("target", default);
        fixture.Graph.TryGetSeamPrism(sourceAddress, out GridCellPrism sourcePrism)
            .Should().BeTrue();
        fixture.Graph.TryGetSeamPrism(targetAddress, out GridCellPrism targetPrism)
            .Should().BeTrue();
        GridCellGeometry.TryCreateNavigationPortal(
                sourcePrism,
                targetPrism,
                out GridNavigationPortal portal)
            .Should().BeTrue();
        portal.TryResolveProfile(
                profile.Shape.Radius,
                profile.Shape.Height,
                out Vector3d sourceAnchor,
                out Vector3d targetAnchor)
            .Should().BeTrue();
        targetAnchor.Should().Be(fixture.End,
            "the target portal anchor and graph node intentionally coalesce");
        result.Payload!.GuidePoints.Should().Equal(
            new NavigationAStarGuidePoint(
                sourceAddress,
                fixture.Start,
                TraversalMedium.Solid),
            new NavigationAStarGuidePoint(
                sourceAddress,
                sourceAnchor,
                TraversalMedium.Solid),
            new NavigationAStarGuidePoint(
                targetAddress,
                fixture.End,
                TraversalMedium.Solid));
    }

    [Fact]
    public void Advance_ShouldCapturePageReadFromImpassableAlternative()
    {
        const int LastAddress = 64;
        using var world = new GridWorld();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d((Fixed64)132, (Fixed64)2, (Fixed64)4),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                (Fixed64)2,
                (Fixed64)2,
                (Fixed64)4),
            storageKind: GridStorageKind.Sparse);
        var addresses = new VoxelIndex[LastAddress + 1];
        for (int i = 0; i < addresses.Length; i++)
            addresses[i] = new VoxelIndex(i, 0, 0);
        world.TryAddGrid(configuration, addresses, out _).Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var builder = new NavigationMapBuilder("map", binding);
        for (int i = 0; i < addresses.Length; i++)
        {
            NavigationCell cell = i == LastAddress
                ? new NavigationCell(
                    TraversalMedia.Gas,
                    TraversalCapability.None,
                    default,
                    Fixed64.Zero,
                    (Fixed64)4,
                    (Fixed64)4)
                : Cell;
            builder.AddCell(addresses[i], cell);
        }
        NavigationMap map = builder.Build();
        var prepared = new PreparedNavigationMap(map, bakeVersion: 1);
        var state = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        NavigationMapInstance instance = NavigationMapInstanceTestFactory.Compose(
            world,
            state,
            previous: null,
            instanceVersion: 1);
        NavigationWorldGraph graph = CreateGraph(instance);
        using NavigationWorldGraphStore store = CreateStore(graph);
        NavigationWorldGraphLease? lease = store.TryAcquire();
        lease.Should().NotBeNull();
        binding.TryGetCellPrism(addresses[63], out GridCellPrism startPrism).Should().BeTrue();
        binding.TryGetCellPrism(addresses[0], out GridCellPrism endPrism).Should().BeTrue();
        var query = new PathQuery(
            new NavigationEndpoint(new Vector3d(
                startPrism.Center.X,
                startPrism.VerticalMin,
                startPrism.Center.Z)),
            new NavigationEndpoint(new Vector3d(
                endPrism.Center.X,
                endPrism.VerticalMin,
                endPrism.Center.Z)),
            Profile(),
            Policy.Key,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(
                maxLookupProbes: 256,
                maxEndpointCandidates: 2,
                maxExpandedNodes: 64,
                maxEvaluatedEdges: 189,
                maxConnectionLegs: 0,
                maxTransitionCandidates: 0,
                maxTransitionPairs: 0,
                maxStagedLegAttempts: 0,
                maxTraceIntervals: 0,
                maxCoveredVoxelIntervals: 0,
                maxSimplificationRays: 0),
            allowTransitions: false);
        var workspace = new NavigationAStarWorkspace(
            mapCapacity: 1,
            endpointPageCapacity: 4,
            componentCapacity: 6,
            nodeCapacity: 65,
            rayCoveredAddressCapacity: 65,
            rayTraceIntervalCapacity: 65,
            guidePointCapacity: 256);
        using var admission = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        admission.Begin(
            lease!,
            query,
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        for (int step = 0;
             step < 256 && admission.Status == NavigationQueryAdmissionStatus.Pending;
             step++)
        {
            admission.Advance(lookupStepLimit: 1, endpointCandidateStepLimit: 1);
        }
        admission.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
        using var search = new NavigationSurfaceAStarWork(
            world,
            store,
            admission.Result,
            workspace,
            admission.RayWork,
            long.MaxValue);

        for (int step = 0;
             step < 2_048 && search.Status == NavigationSurfaceAStarStatus.Pending;
             step++)
        {
            search.Advance(1, 1, 1, 1);
        }

        search.Status.Should().Be(NavigationSurfaceAStarStatus.Success);
        search.Result.Dependencies.Pages.Should().HaveCount(2);
        search.Result.Dependencies.Pages[0].PageIndex.Should().Be(0);
        search.Result.Dependencies.Pages[1].PageIndex.Should().Be(1,
            "the denied neighbor's semantic/physical page was evaluated");
    }

    [Fact]
    public void Advance_ShouldRebuildCachedNegativeProofWhenBlockedExplicitWitnessChanges()
    {
        using var world = new GridWorld();
        var sourceConfiguration = new GridConfiguration(
            Vector3d.Zero,
            Vector3d.Zero,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        var witnessCenter = new Vector3d(1, 0, 0);
        var witnessConfiguration = new GridConfiguration(
            witnessCenter,
            witnessCenter,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        var destinationCenter = new Vector3d(2, 0, 0);
        var destinationConfiguration = new GridConfiguration(
            destinationCenter,
            destinationCenter,
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(Fixed64.One),
            storageKind: GridStorageKind.Dense);
        world.TryAddGrid(sourceConfiguration, out _).Should().BeTrue();
        world.TryAddGrid(witnessConfiguration, out ushort witnessGridIndex)
            .Should().BeTrue();
        world.TryAddGrid(destinationConfiguration, out _).Should().BeTrue();
        sourceConfiguration.TryNormalize(out NormalizedGridConfiguration sourceBinding)
            .Should().BeTrue();
        destinationConfiguration.TryNormalize(
                out NormalizedGridConfiguration destinationBinding)
            .Should().BeTrue();
        witnessConfiguration.TryNormalize(out NormalizedGridConfiguration witnessBinding)
            .Should().BeTrue();
        GridCellPrism sourcePrism = GetPrism(sourceBinding, default);
        GridCellPrism destinationPrism = GetPrism(destinationBinding, default);
        var sourceFoot = new Vector3d(
            sourcePrism.Center.X,
            sourcePrism.VerticalMin,
            sourcePrism.Center.Z);
        var destinationFoot = new Vector3d(
            destinationPrism.Center.X,
            destinationPrism.VerticalMin,
            destinationPrism.Center.Z);
        NavigationMap sourceMap = new NavigationMapBuilder("A", sourceBinding)
            .AddCell(default, Cell)
            .Build();
        NavigationMap destinationMap = new NavigationMapBuilder("B", destinationBinding)
            .AddCell(default, Cell)
            .Build();
        NavigationMap witnessMap = new NavigationMapBuilder("C", witnessBinding)
            .AddCell(default, Cell)
            .Build();
        NavigationOperationCandidate.MapState CreateState(NavigationMap map)
        {
            var prepared = new PreparedNavigationMap(map, bakeVersion: 1);
            return new NavigationOperationCandidate.MapState(
                prepared.Map,
                prepared.BakeVersion,
                prepared.RetainedBytes,
                NavigationMapOverlayState.Empty,
                dynamicSlotGeneration: 0,
                bakedCellLookup: prepared.BakedCellLookup);
        }
        NavigationOperationCandidate.MapState sourceState = CreateState(sourceMap);
        NavigationOperationCandidate.MapState destinationState = CreateState(destinationMap);
        NavigationOperationCandidate.MapState witnessState = CreateState(witnessMap);
        NavigationMapInstance sourceInstance = NavigationMapInstanceTestFactory.Compose(
            world,
            sourceState,
            previous: null,
            instanceVersion: 1);
        NavigationMapInstance destinationInstance = NavigationMapInstanceTestFactory.Compose(
            world,
            destinationState,
            previous: null,
            instanceVersion: 1);
        NavigationMapInstance witnessInstance = NavigationMapInstanceTestFactory.Compose(
            world,
            witnessState,
            previous: null,
            instanceVersion: 1);
        var portalBuilder = new NavigationPagedSequence<GridNavigationPortal>.Builder(
            GridNavigationPortal.SizeInBytes);
        GridCellPrism witnessPrism = GetPrism(witnessBinding, default);
        GridCellGeometry.TryCreateNavigationPortal(
                sourcePrism,
                witnessPrism,
                out GridNavigationPortal sourcePortal)
            .Should().BeTrue();
        GridCellGeometry.TryCreateNavigationPortal(
                witnessPrism,
                destinationPrism,
                out GridNavigationPortal destinationPortal)
            .Should().BeTrue();
        KinematicBodyShape shape = Profile().Shape;
        sourcePortal.TryResolveProfile(
                shape.Radius,
                shape.Height,
                out Vector3d entryAnchor,
                out _)
            .Should().BeTrue();
        destinationPortal.TryResolveProfile(
                shape.Radius,
                shape.Height,
                out _,
                out Vector3d exitAnchor)
            .Should().BeTrue();
        var connection = new NavigationConnection(
            "a-to-b",
            default,
            new NavigationCellAddress("B", default),
            entryAnchor,
            exitAnchor,
            portalRadiusClearance: Fixed64.One,
            portalHeightClearance: (Fixed64)2,
            witnesses: new[]
            {
                new NavigationCellAddress("C", default)
            });
        portalBuilder.Append(sourcePortal);
        portalBuilder.Append(destinationPortal);
        var record = new NavigationExplicitConnectionRecord(
            new NavigationConnectionOwnerKey("A", connection.Id),
            connection,
            isActive: true,
            corridorCost: (Fixed64)2,
            portalBuilder.Seal());
        NavigationExplicitConnectionIndex connections =
            NavigationExplicitConnectionIndex.Empty.SetOwner(record, out _);
        var endpointRowBuilder =
            new NavigationPagedSequence<NavigationConnectionOwnerKey>.Builder(16);
        endpointRowBuilder.Append(record.Owner);
        NavigationPagedSequence<NavigationConnectionOwnerKey> endpointRow =
            endpointRowBuilder.Seal();
        connections = connections.SetEndpointRow(
            record.Source,
            NavigationPagedSequence<NavigationConnectionOwnerKey>.Empty,
            endpointRow,
            out _);
        connections = connections.SetEndpointRow(
            record.Destination,
            NavigationPagedSequence<NavigationConnectionOwnerKey>.Empty,
            endpointRow,
            out _);
        NavigationWorldGraph graph = CreateGraph(
            new[] { sourceInstance, destinationInstance, witnessInstance },
            connections);
        var witnessComponents = new NavigationSurfaceComponentKey[1];
        graph.TryGetSurfaceComponent(
                new NavigationCellAddress("C", default),
                TraversalMedium.Solid,
                out witnessComponents[0],
                out _)
            .Should().BeTrue();
        graph.TryGetSurfaceComponent(
                new NavigationCellAddress("A", default),
                TraversalMedium.Solid,
                out NavigationSurfaceComponentKey sourceComponent,
                out _)
            .Should().BeTrue();
        witnessComponents.Should().NotContain(sourceComponent,
            "an authored corridor witness remains a dependency, not a graph edge endpoint");
        using NavigationWorldGraphStore store = CreateStore(graph);
        var query = new PathQuery(
            new NavigationEndpoint(sourceFoot, mapId: "A"),
            new NavigationEndpoint(destinationFoot, mapId: "B"),
            Profile(),
            Policy.Key,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(128, 2, 2, 2, 6, 0, 0, 0, 0, 0, 0),
            allowTransitions: false);
        var insufficientWorkspace = new NavigationAStarWorkspace(
            mapCapacity: 3,
            endpointPageCapacity: 3,
            componentCapacity: 1,
            nodeCapacity: 3,
            rayCoveredAddressCapacity: 4,
            rayTraceIntervalCapacity: 4,
            guidePointCapacity: 8);
        var insufficientCache = new NavigationAStarPayloadCache(world, 1);
        using (NavigationAStarQueryWork insufficient = BeginReservedQuery(
            world,
            store,
            query,
            insufficientWorkspace,
            insufficientCache))
        {
            DrainQuery(insufficient, 256);
            insufficient.Status.Should().Be(NavigationAStarQueryStatus.CapacityExceeded);
        }
        var workspace = new NavigationAStarWorkspace(3, 3, 2, 3, 4, 4, 8);
        var cache = new NavigationAStarPayloadCache(world, 1);
        GraphDependencyStamp dependencies;
        using (NavigationAStarQueryWork work = BeginReservedQuery(
            world,
            store,
            query,
            workspace,
            cache))
        {
            DrainQuery(work, 256);
            work.Status.Should().Be(NavigationAStarQueryStatus.Success);
            NavigationAStarPayloadLease payloadLease = work.TakeResult();
            payloadLease.Payload.GuidePoints[0].Address.Should().Be(
                new NavigationCellAddress("A", default));
            payloadLease.Payload.GuidePoints[^1].Address.Should().Be(
                new NavigationCellAddress("B", default));
            Vector3d witnessFoot = new(
                witnessPrism.Center.X,
                witnessPrism.VerticalMin,
                witnessPrism.Center.Z);
            payloadLease.Payload.GuidePoints.Should().NotContain(
                point => point.Position == witnessFoot,
                "semantic witness feet are dependencies, not authored guide points");
            payloadLease.Payload.GuidePoints.Should().ContainSingle(
                point => point.Address == new NavigationCellAddress("C", default)
                    && point.Position == sourcePortal.CanonicalFacePoint);
            payloadLease.Payload.GuidePoints.Should().ContainSingle(
                point => point.Address == new NavigationCellAddress("B", default)
                    && point.Position == destinationPortal.CanonicalFacePoint);
            foreach (NavigationSurfaceComponentKey witnessComponent in witnessComponents)
            {
                payloadLease.Payload.Dependencies.Components.Should().ContainSingle(
                    dependency => dependency.Key == witnessComponent);
            }
            payloadLease.Payload.Dependencies.Pages.Should().ContainSingle(
                dependency => dependency.MapId == "C" && dependency.PageIndex == 0);
            dependencies = payloadLease.Payload.Dependencies;
            graph.IsDependencyCurrent(dependencies).Should().BeTrue();
            payloadLease.Dispose();
        }

        VoxelGrid witnessGrid = world.ActiveGrids[witnessGridIndex];
        witnessGrid.TryGetVoxel(default(VoxelIndex), out Voxel? witnessVoxel)
            .Should().BeTrue();
        var witnessObstacle = world.AllocateObstacleToken();
        witnessGrid.TryAddObstacle(
                witnessVoxel!,
                witnessObstacle)
            .Should().BeTrue();
        NavigationMapInstance changedWitness = NavigationMapInstanceTestFactory.Compose(
            world,
            witnessState,
            witnessInstance,
            instanceVersion: 2);
        NavigationWorldGraph changedGraph = CreateGraph(
            new[] { sourceInstance, destinationInstance, changedWitness },
            connections).WithGraphVersion(graph.GraphVersion + 1);
        changedGraph.IsDependencyCurrent(dependencies).Should().BeFalse(
            "a physical mutation of the consumed witness page invalidates the result");
        store.TryPublish(changedGraph).Should().Be(NavigationCandidatePublication.Published);
        var blockedInsufficientWorkspace = new NavigationAStarWorkspace(
            mapCapacity: 3,
            endpointPageCapacity: 3,
            componentCapacity: 1,
            nodeCapacity: 3,
            rayCoveredAddressCapacity: 4,
            rayTraceIntervalCapacity: 4,
            guidePointCapacity: 8);
        var blockedInsufficientCache = new NavigationAStarPayloadCache(world, 1);
        using (NavigationAStarQueryWork blockedInsufficient = BeginReservedQuery(
            world,
            store,
            query,
            blockedInsufficientWorkspace,
            blockedInsufficientCache))
        {
            DrainQuery(blockedInsufficient, 256);
            blockedInsufficient.Status.Should().Be(
                NavigationAStarQueryStatus.CapacityExceeded);
        }
        using (NavigationAStarQueryWork blocked = BeginReservedQuery(
            world,
            store,
            query,
            workspace,
            cache))
        {
            DrainQuery(blocked, 256);
            blocked.Status.Should().Be(NavigationAStarQueryStatus.NoPath);
        }

        var key = new NavigationAStarPayloadKey(
            query,
            new NavigationCellAddress("A", default),
            new NavigationCellAddress("B", default),
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        cache.TryCheckout(
                key,
                changedGraph,
                out NavigationAStarPayloadLease blockedLease)
            .Should().BeTrue();
        GraphDependencyStamp blockedDependencies = blockedLease.Payload.Dependencies;
        changedGraph.TryGetSurfaceComponent(
                new NavigationCellAddress("C", default),
                TraversalMedium.Solid,
                out NavigationSurfaceComponentKey blockedWitnessComponent,
                out _)
            .Should().BeTrue();
        blockedDependencies.Components.Should().ContainSingle(
            dependency => dependency.Key == blockedWitnessComponent,
            "the rejected explicit witness component participates in the negative proof");
        blockedDependencies.Pages.Should().ContainSingle(
            dependency => dependency.MapId == "C" && dependency.PageIndex == 0,
            "the impassable witness page is part of the cached negative proof");
        NavigationWorldGraph componentChanged = changedGraph.WithClosedStructuralComponents(
            NavigationSurfaceComponentKeySet.Empty.Add(blockedWitnessComponent),
            closeAllStructuralComponents: false,
            changedGraph.GraphVersion + 1);
        componentChanged.IsDependencyCurrent(blockedDependencies).Should().BeFalse(
            "a component-only witness mutation invalidates the cached negative proof");
        blockedLease.Dispose();

        witnessGrid.TryRemoveObstacle(witnessVoxel!, witnessObstacle).Should().BeTrue();
        NavigationMapInstance reopenedWitness = NavigationMapInstanceTestFactory.Compose(
            world,
            witnessState,
            changedWitness,
            instanceVersion: 3);
        NavigationWorldGraph reopenedGraph = CreateGraph(
            new[] { sourceInstance, destinationInstance, reopenedWitness },
            connections).WithGraphVersion(changedGraph.GraphVersion + 1);
        reopenedGraph.IsDependencyCurrent(blockedDependencies).Should().BeFalse(
            "changing the blocked witness invalidates the cached negative proof");
        store.TryPublish(reopenedGraph).Should().Be(NavigationCandidatePublication.Published);
        using NavigationAStarQueryWork rebuilt = BeginReservedQuery(
            world,
            store,
            query,
            workspace,
            cache);
        DrainQuery(rebuilt, 256);
        rebuilt.Status.Should().Be(NavigationAStarQueryStatus.Success,
            "the stale negative result must be rebuilt after its witness changes");
        rebuilt.TakeResult().Dispose();

        var wrongMediumCell = new NavigationCell(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        NavigationMap wrongMediumMap = new NavigationMapBuilder("C", witnessBinding)
            .AddCell(default, wrongMediumCell)
            .Build();
        NavigationOperationCandidate.MapState wrongMediumState =
            CreateState(wrongMediumMap);
        NavigationMapInstance wrongMediumWitness =
            NavigationMapInstanceTestFactory.Compose(
                world,
                wrongMediumState,
                reopenedWitness,
                instanceVersion: 4);
        NavigationWorldGraph wrongMediumGraph = CreateGraph(
            new[] { sourceInstance, destinationInstance, wrongMediumWitness },
            connections).WithGraphVersion(reopenedGraph.GraphVersion + 1);
        wrongMediumGraph.TryGetSurfaceComponent(
                new NavigationCellAddress("C", default),
                TraversalMedium.Solid,
                out _,
                out _)
            .Should().BeFalse();
        store.TryPublish(wrongMediumGraph)
            .Should().Be(NavigationCandidatePublication.Published);
        using (NavigationAStarQueryWork wrongMedium = BeginReservedQuery(
            world,
            store,
            query,
            workspace,
            cache))
        {
            DrainQuery(wrongMedium, 256);
            wrongMedium.Status.Should().Be(NavigationAStarQueryStatus.NoPath,
                "a present wrong-medium corridor witness is semantic rejection, not capacity exhaustion");
        }
        cache.TryCheckout(
                key,
                wrongMediumGraph,
                out NavigationAStarPayloadLease wrongMediumLease)
            .Should().BeTrue();
        wrongMediumLease.Payload.Dependencies.Pages.Should().ContainSingle(
            dependency => dependency.MapId == "C" && dependency.PageIndex == 0,
            "the wrong-medium witness page remains part of the negative proof");
        wrongMediumLease.Dispose();
    }

    [Theory]
    [InlineData(6, (int)NavigationSurfaceAStarStatus.Success)]
    [InlineData(5, (int)NavigationSurfaceAStarStatus.BudgetExceeded)]
    public void Advance_ShouldMeterEveryExplicitConnectionLeg(
        int maximumConnectionLegs,
        int expectedStatusValue)
    {
        NavigationSurfaceAStarStatus expectedStatus =
            (NavigationSurfaceAStarStatus)expectedStatusValue;
        using var world = new GridWorld();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d((Fixed64)130, (Fixed64)2, (Fixed64)4),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                (Fixed64)2,
                (Fixed64)2,
                (Fixed64)4),
            storageKind: GridStorageKind.Sparse);
        VoxelIndex sourceIndex = new(63, 0, 0);
        VoxelIndex witnessIndex = new(64, 0, 0);
        VoxelIndex destinationIndex = new(65, 0, 0);
        world.TryAddGrid(
                configuration,
                new[] { sourceIndex, witnessIndex, destinationIndex },
                out _)
            .Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        var mapBuilder = new NavigationMapBuilder("map", binding);
        for (int i = 0; i <= destinationIndex.x; i++)
            mapBuilder.AddCell(new VoxelIndex(i, 0, 0), Cell);
        NavigationMap map = mapBuilder.Build();
        var prepared = new PreparedNavigationMap(map, bakeVersion: 1);
        var state = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        NavigationMapInstance instance = NavigationMapInstanceTestFactory.Compose(
            world,
            state,
            previous: null,
            instanceVersion: 1);
        binding.TryGetCellPrism(sourceIndex, out GridCellPrism sourcePrism).Should().BeTrue();
        binding.TryGetCellPrism(destinationIndex, out GridCellPrism destinationPrism)
            .Should().BeTrue();
        var definition = new NavigationConnection(
            "shortcut",
            sourceIndex,
            new NavigationCellAddress("map", destinationIndex),
            new Vector3d(sourcePrism.Center.X, sourcePrism.VerticalMin, sourcePrism.Center.Z),
            new Vector3d(
                destinationPrism.Center.X,
                destinationPrism.VerticalMin,
                destinationPrism.Center.Z),
            portalRadiusClearance: Fixed64.One,
            portalHeightClearance: (Fixed64)2,
            witnesses: new[] { new NavigationCellAddress("map", witnessIndex) });
        var record = new NavigationExplicitConnectionRecord(
            new NavigationConnectionOwnerKey("map", definition.Id),
            definition,
            isActive: true,
            corridorCost: Fixed64.One,
            CompilePortalSequence(
                binding,
                sourceIndex,
                witnessIndex,
                destinationIndex));
        var alternateDefinition = new NavigationConnection(
            "z-shortcut",
            sourceIndex,
            new NavigationCellAddress("map", destinationIndex),
            definition.EntryAnchor,
            definition.ExitAnchor,
            portalRadiusClearance: Fixed64.One,
            portalHeightClearance: (Fixed64)2,
            witnesses: new[] { new NavigationCellAddress("map", witnessIndex) });
        var alternateRecord = new NavigationExplicitConnectionRecord(
            new NavigationConnectionOwnerKey("map", alternateDefinition.Id),
            alternateDefinition,
            isActive: true,
            corridorCost: Fixed64.One / (Fixed64)2,
            CompilePortalSequence(
                binding,
                sourceIndex,
                witnessIndex,
                destinationIndex));
        NavigationExplicitConnectionIndex connections =
            NavigationExplicitConnectionIndex.Empty.SetOwner(record, out _);
        connections = connections.SetOwner(alternateRecord, out _);
        var ownerRowBuilder =
            new NavigationPagedSequence<NavigationConnectionOwnerKey>.Builder(16);
        ownerRowBuilder.Append(new NavigationConnectionOwnerKey("map", "a-missing"));
        ownerRowBuilder.Append(record.Owner);
        ownerRowBuilder.Append(alternateRecord.Owner);
        NavigationPagedSequence<NavigationConnectionOwnerKey> ownerRow =
            ownerRowBuilder.Seal();
        connections = connections.SetEndpointRow(
            record.Source,
            NavigationPagedSequence<NavigationConnectionOwnerKey>.Empty,
            ownerRow,
            out _);
        connections = connections.SetEndpointRow(
            record.Destination,
            NavigationPagedSequence<NavigationConnectionOwnerKey>.Empty,
            ownerRow,
            out _);
        NavigationWorldGraph graph = CreateGraph(instance, connections);
        graph.SurfaceComponents.TryGet(
                new NavigationCellAddress("map", sourceIndex),
                TraversalMedium.Solid,
                out NavigationSurfaceComponent component)
            .Should().BeTrue();
        component.AllSurfaceEdgesEuclideanCertified.Should().BeFalse(
                "an active uncertified self-edge disables the component heuristic even when the route does not use it");
        using NavigationWorldGraphStore store = CreateStore(graph);
        NavigationWorldGraphLease? lease = store.TryAcquire();
        lease.Should().NotBeNull();
        var query = new PathQuery(
            new NavigationEndpoint(definition.EntryAnchor),
            new NavigationEndpoint(definition.ExitAnchor),
            Profile(),
            Policy.Key,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(
                maxLookupProbes: 64,
                maxEndpointCandidates: 2,
                maxExpandedNodes: 2,
                maxEvaluatedEdges: 8,
                maxConnectionLegs: maximumConnectionLegs,
                maxTransitionCandidates: 0,
                maxTransitionPairs: 0,
                maxStagedLegAttempts: 0,
                maxTraceIntervals: 0,
                maxCoveredVoxelIntervals: 0,
                maxSimplificationRays: 0),
            allowTransitions: false);
        var workspace = new NavigationAStarWorkspace(1, 4, 6, 4, 4, 4, 8);
        using var admission = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        admission.Begin(
            lease!,
            query,
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        for (int step = 0;
             step < 64 && admission.Status == NavigationQueryAdmissionStatus.Pending;
             step++)
        {
            admission.Advance(1, 1);
        }
        admission.Status.Should().Be(NavigationQueryAdmissionStatus.Success);
        using var search = new NavigationSurfaceAStarWork(
            world,
            store,
            admission.Result,
            workspace,
            admission.RayWork,
            long.MaxValue);

        search.Advance(64, 64, 64, connectionStepLimit: 0)
            .Should().Be(NavigationSurfaceAStarStatus.Pending);
        admission.Meter.ConnectionLegs.Should().Be(0,
            "a zero local connection slice retains the explicit route work");

        for (int step = 0;
             step < 64 && search.Status == NavigationSurfaceAStarStatus.Pending;
             step++)
        {
            search.Advance(1, 1, 1, 1);
        }

        search.Status.Should().Be(expectedStatus);
        admission.Meter.EvaluatedEdges.Should().Be(8,
            "search and exact parent-edge reconstruction meter every canonical owner");
        admission.Meter.ConnectionLegs.Should().Be(maximumConnectionLegs);
        if (expectedStatus == NavigationSurfaceAStarStatus.Success)
        {
            search.Result.Cost.Should().Be(Fixed64.One / (Fixed64)2,
                "the later canonical parallel edge owns the reconstructed route");
            workspace.NodeTable.TryGetSlot(
                    new NavigationMediumStateRef(
                        admission.Result.Start.Node,
                        admission.Result.StartMedium),
                    out int startSlot)
                .Should().BeTrue();
            workspace.NodeTable.GetRecord(startSlot).Heuristic.Should().Be(Fixed64.Zero);
            search.Result.Dependencies.Pages.Should().ContainSingle(
                page => page.PageIndex == 1,
                "the explicit witness page was read during corridor evaluation");
        }
        else
        {
            search.Result.Should().BeNull(
                "an exhausted query has no reusable route or complete dependency payload");
        }
    }

    [Fact]
    public void Advance_ShouldProduceDependencyStampedNoPathPayload()
    {
        using var world = new GridWorld();
        GridConfiguration configuration = new(
            Vector3d.Zero,
            new Vector3d((Fixed64)6, (Fixed64)2, (Fixed64)4),
            topologyKind: GridTopologyKind.RectangularPrism,
            topologyMetrics: GridTopologyMetrics.Rectangular(
                (Fixed64)2,
                (Fixed64)2,
                (Fixed64)4),
            storageKind: GridStorageKind.Sparse);
        VoxelIndex startIndex = default;
        VoxelIndex endIndex = new(2, 0, 0);
        world.TryAddGrid(configuration, new[] { startIndex, endIndex }, out _)
            .Should().BeTrue();
        configuration.TryNormalize(out NormalizedGridConfiguration binding).Should().BeTrue();
        NavigationMap map = new NavigationMapBuilder("map", binding)
            .AddCell(startIndex, Cell)
            .AddCell(endIndex, Cell)
            .Build();
        var prepared = new PreparedNavigationMap(map, bakeVersion: 1);
        var state = new NavigationOperationCandidate.MapState(
            prepared.Map,
            prepared.BakeVersion,
            prepared.RetainedBytes,
            NavigationMapOverlayState.Empty,
            dynamicSlotGeneration: 0,
            bakedCellLookup: prepared.BakedCellLookup);
        NavigationMapInstance instance = NavigationMapInstanceTestFactory.Compose(
            world,
            state,
            previous: null,
            instanceVersion: 1);
        NavigationWorldGraph graph = CreateGraph(instance);
        using NavigationWorldGraphStore store = CreateStore(graph);
        binding.TryGetCellPrism(startIndex, out GridCellPrism startPrism).Should().BeTrue();
        binding.TryGetCellPrism(endIndex, out GridCellPrism endPrism).Should().BeTrue();
        var query = new PathQuery(
            new NavigationEndpoint(new Vector3d(
                startPrism.Center.X,
                startPrism.VerticalMin,
                startPrism.Center.Z)),
            new NavigationEndpoint(new Vector3d(
                endPrism.Center.X,
                endPrism.VerticalMin,
                endPrism.Center.Z)),
            Profile(),
            Policy.Key,
            new TraversalIntent(
                TraversalDomain.Surface,
                TraversalMedium.Solid,
                TraversalDomain.Surface),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(32, 2, 0, 0, 0, 0, 0, 0, 0, 0, 0),
            allowTransitions: false);
        var workspace = new NavigationAStarWorkspace(1, 2, 4, 2, 2, 2, 2);
        var cache = new NavigationAStarPayloadCache(world, 1);
        using NavigationAStarQueryWork work = BeginReservedQuery(
            world,
            store,
            query,
            workspace,
            cache);
        DrainQuery(work, 64);

        work.Status.Should().Be(NavigationAStarQueryStatus.NoPath);
        store.ActiveLeaseCount.Should().Be(0,
            "negative query completion must release its graph snapshot lease");
        var key = new NavigationAStarPayloadKey(
            query,
            new NavigationCellAddress("map", startIndex),
            new NavigationCellAddress("map", endIndex),
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        cache.TryCheckout(key, graph, out NavigationAStarPayloadLease payloadLease)
            .Should().BeTrue("dependency-stamped negative results remain reusable");
        payloadLease.Payload.Status.Should().Be(NavigationSurfaceAStarStatus.NoPath);
        payloadLease.Payload.HasPath.Should().BeFalse();
        payloadLease.Payload.Dependencies.Pages.Should().NotBeEmpty();
        payloadLease.Dispose();
    }

    private static NavigationWorldGraph CreateGraph(
        NavigationMapInstance instance,
        NavigationExplicitConnectionIndex? explicitConnections = null) =>
        CreateGraph(new[] { instance }, explicitConnections);

    private static NavigationWorldGraph CreateGraph(
        NavigationMapInstance[] instances,
        NavigationExplicitConnectionIndex? explicitConnections = null)
    {
        NavigationAreaCatalog.Empty.TryPublish(
                Policy,
                maxPolicies: 1,
                requiredRuleCount: 1,
                maxRulesPerPolicy: 1,
                maxRules: 1,
                out NavigationAreaCatalog catalog)
            .Should().Be(NavigationOperationRejection.None);
        var graph = new NavigationWorldGraph(
            1,
            instances,
            areaCatalog: catalog,
            explicitConnections: explicitConnections);
        return graph.WithSurfaceComponents(NavigationSurfaceComponentTestFactory.Build(graph));
    }

    private static NavigationWorldGraphStore CreateStore(
        NavigationWorldGraph graph,
        int maxConcurrentLeases = 1)
    {
        var store = new NavigationWorldGraphStore(
            maxActiveSnapshots: 2,
            maxRetiredSnapshots: 1,
            maxRetiredBytes: long.MaxValue,
            maxActiveBytes: long.MaxValue,
            maxPersistentPages: int.MaxValue,
            maxConcurrentLeases);
        store.TryPublish(graph).Should().Be(NavigationCandidatePublication.Published);
        return store;
    }

    private static NavigationAStarPayload ClonePayload(
        NavigationAStarPayload source,
        NavigationCellAddress end) => new(
        new NavigationAStarPayloadKey(
            source.Key.Query,
            source.Key.Start,
            end,
            source.Key.StartMedium,
            source.Key.TargetMedia),
        (NavigationAStarGuidePoint[])source.GuidePoints.Clone(),
        (NavigationTransitionInstruction[])source.TransitionInstructions.Clone(),
        source.Cost,
        source.Dependencies,
        source.WorldChangeSequence,
        source.Status);

    private static NavigationPagedSequence<GridNavigationPortal> CompilePortalSequence(
        NormalizedGridConfiguration binding,
        params VoxelIndex[] cells)
    {
        var builder = new NavigationPagedSequence<GridNavigationPortal>.Builder(
            GridNavigationPortal.SizeInBytes);
        for (int i = 0; i + 1 < cells.Length; i++)
        {
            binding.TryGetCellPrism(cells[i], out GridCellPrism source)
                .Should().BeTrue();
            binding.TryGetCellPrism(cells[i + 1], out GridCellPrism target)
                .Should().BeTrue();
            GridCellGeometry.TryCreateNavigationPortal(
                    source,
                    target,
                    out GridNavigationPortal portal)
                .Should().BeTrue();
            builder.Append(portal);
        }
        return builder.Seal();
    }

    private static NavigationAStarPayloadLease PublishPayload(
        NavigationAStarPayloadCache cache,
        NavigationWorldGraphStore store,
        NavigationAStarPayload payload)
    {
        cache.TryReservePayload(
                payload.RetainedBytes,
                out NavigationAStarPayloadReservation reservation)
            .Should().BeTrue();
        cache.TryPublish(payload, store, ref reservation, out NavigationAStarPayloadLease lease)
            .Should().BeTrue();
        reservation.Should().Be(default(NavigationAStarPayloadReservation));
        return lease;
    }

    private static NavigationAStarQueryWork BeginReservedQuery(
        GridWorld world,
        NavigationWorldGraphStore store,
        PathQuery query,
        NavigationAStarWorkspace workspace,
        NavigationAStarPayloadCache cache)
    {
        NavigationWorldGraphLease? graphLease = store.TryAcquire();
        graphLease.Should().NotBeNull();
        long maximumBytes = NavigationAStarPayload.GetMaximumRetainedBytes(
            workspace.GuidePoints.Length,
            workspace.PathNodes.Length - 1,
            workspace.EndpointComponents.Length,
            workspace.EndpointPages.Length);
        cache.TryReservePayload(
                maximumBytes,
                out NavigationAStarPayloadReservation reservation)
            .Should().BeTrue();
        var work = new NavigationAStarQueryWork(world, store, workspace, cache);
        work.BeginReserved(query, graphLease!, ref reservation);
        reservation.Should().Be(default(NavigationAStarPayloadReservation));
        return work;
    }

    private static void DrainQuery(NavigationAStarQueryWork work, int stepLimit)
    {
        for (int step = 0; step < stepLimit && !work.IsPrepared; step++)
            work.PrepareSearchOrCheckout(1, 1);
        for (int step = 0; step < stepLimit && !work.IsReadyToPublish; step++)
            work.AdvanceSearch(1, 1, 1, 1);
        if (work.IsReadyToPublish)
            work.Publish();
    }

    private static GridCellPrism GetPrism(
        NormalizedGridConfiguration binding,
        VoxelIndex index)
    {
        binding.TryGetCellPrism(index, out GridCellPrism prism).Should().BeTrue();
        return prism;
    }

    private static PathQuery CreateSimplificationQuery(
        NavigationAStarExitTestHarness.GraphFixture fixture,
        VoxelIndex start,
        VoxelIndex end,
        int simplificationRays,
        int lookupProbes = 8_192,
        int traceIntervals = 128,
        int coveredVoxelIntervals = 128) => new(
        new NavigationEndpoint(
            NavigationAStarExitTestHarness.GetFoot(fixture.Binding, start),
            fixture.MapId),
        new NavigationEndpoint(
            NavigationAStarExitTestHarness.GetFoot(fixture.Binding, end),
            fixture.MapId),
        fixture.DefaultProfile,
        NavigationAStarExitTestHarness.Policy.Key,
        new TraversalIntent(
            TraversalDomain.Surface,
            TraversalMedium.Solid,
            TraversalDomain.Surface),
        PathAlgorithm.AStar,
        new NavigationWorkBudget(
            maxLookupProbes: lookupProbes,
            maxEndpointCandidates: 32,
            maxExpandedNodes: 128,
            maxEvaluatedEdges: 1_024,
            maxConnectionLegs: 1_024,
            maxTransitionCandidates: 0,
            maxTransitionPairs: 0,
            maxStagedLegAttempts: 0,
            maxTraceIntervals: traceIntervals,
            maxCoveredVoxelIntervals: coveredVoxelIntervals,
            maxSimplificationRays: simplificationRays),
        allowTransitions: false);

    private static NavigationAgentProfile Profile() => new(
        new KinematicBodyShape(Fixed64.Half, Fixed64.One, Fixed64.Zero),
        maxStepUp: Fixed64.Zero,
        maxDropDown: Fixed64.Zero,
        arrivalRadius: Fixed64.Zero,
        allowedMedia: TraversalMedia.Solid,
        capabilities: TraversalCapability.None);

    [Fact]
    public void Constructor_WhenTransitionsAreEnabled_ShouldUseZeroHeuristic()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells = { default, new VoxelIndex(1, 0, 0) };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(cells.Length),
                cells,
                "transition-heuristic");
        using NavigationWorldGraphStore store = CreateStore(fixture.Graph);
        PathQuery baseline = fixture.CreateQuery(cells[0], cells[1], fixture.DefaultProfile);
        var query = new PathQuery(
            baseline.Start,
            baseline.End,
            baseline.Agent,
            baseline.AreaPolicy,
            baseline.Traversal,
            baseline.Algorithm,
            baseline.Budget,
            allowTransitions: true);
        var workspace = new NavigationAStarWorkspace(1, 4, 6, 4, 4, 4, 4);
        using var admission = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        admission.Begin(
            store.TryAcquire()!,
            query,
            TraversalMedium.Solid,
            TraversalMedia.Solid);
        while (admission.Status == NavigationQueryAdmissionStatus.Pending)
            admission.Advance(64, 8);
        admission.Status.Should().Be(NavigationQueryAdmissionStatus.Success);

        using var search = new NavigationSurfaceAStarWork(
            world,
            store,
            admission.Result,
            workspace,
            admission.RayWork,
            long.MaxValue);

        workspace.NodeTable.TryGetSlot(
                new NavigationMediumStateRef(
                    admission.Result.Start.Node,
                    admission.Result.StartMedium),
                out int startSlot)
            .Should().BeTrue();
        workspace.NodeTable.GetRecord(startSlot).Heuristic.Should().Be(Fixed64.Zero);
    }

    [Fact]
    public void Constructor_ForDisabledGasQuery_ShouldFloorCenteredVolumeAnchors()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells = { default, new VoxelIndex(1, 0, 0) };
        NavigationCell gas = new(
            TraversalMedia.Gas,
            TraversalCapability.None,
            default,
            Fixed64.Zero,
            (Fixed64)4,
            (Fixed64)4);
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(cells.Length),
                cells,
                "gas-heuristic",
                new[] { gas, gas });
        using NavigationWorldGraphStore store = CreateStore(fixture.Graph);
        var profile = new NavigationAgentProfile(
            new KinematicBodyShape(Fixed64.Zero, Fixed64.One, Fixed64.Zero),
            Fixed64.Zero,
            Fixed64.Zero,
            Fixed64.Zero,
            TraversalMedia.Gas,
            TraversalCapability.None);
        var query = new PathQuery(
            new NavigationEndpoint(
                NavigationAStarExitTestHarness.GetFoot(fixture.Binding, cells[0]),
                fixture.MapId),
            new NavigationEndpoint(
                NavigationAStarExitTestHarness.GetFoot(fixture.Binding, cells[1]),
                fixture.MapId),
            profile,
            NavigationAStarExitTestHarness.Policy.Key,
            new TraversalIntent(
                TraversalDomain.Volume,
                TraversalMedium.Gas,
                TraversalDomain.Volume),
            PathAlgorithm.AStar,
            new NavigationWorkBudget(
                128, 16, 16, 64, 64, 0, 0, 0, 0, 32, 0),
            allowTransitions: false);
        var workspace = new NavigationAStarWorkspace(1, 4, 6, 4, 16, 8, 4);
        using var admission = new NavigationQueryAdmissionWork(
            world,
            store,
            workspace.EndpointWorkspace,
            workspace.RayWorkspace,
            PathAlgorithm.AStar);
        admission.Begin(
            store.TryAcquire()!,
            query,
            TraversalMedium.Gas,
            TraversalMedia.Gas);
        while (admission.Status == NavigationQueryAdmissionStatus.Pending)
            admission.Advance(64, 8);
        admission.Status.Should().Be(NavigationQueryAdmissionStatus.Success);

        using var search = new NavigationSurfaceAStarWork(
            world,
            store,
            admission.Result,
            workspace,
            admission.RayWork,
            long.MaxValue);

        workspace.NodeTable.TryGetSlot(
                new NavigationMediumStateRef(
                    admission.Result.Start.Node,
                    admission.Result.StartMedium),
                out int startSlot)
            .Should().BeTrue();
        workspace.NodeTable.GetRecord(startSlot).Heuristic.Should().Be(Fixed64.One);
    }

    [Fact]
    public void QueryWork_UnsupportedResult_ShouldRejectSecondBeginBeforePublish()
    {
        using var world = new GridWorld();
        VoxelIndex[] cells = { default, new VoxelIndex(1, 0, 0) };
        NavigationAStarExitTestHarness.GraphFixture fixture =
            NavigationAStarExitTestHarness.CreateSingleMap(
                world,
                NavigationAStarExitTestHarness.RectangularLine(cells.Length),
                cells,
                "unsupported-reentry");
        using NavigationWorldGraphStore store = CreateStore(
            fixture.Graph,
            maxConcurrentLeases: 2);
        PathQuery baseline = fixture.CreateQuery(cells[0], cells[1], fixture.DefaultProfile);
        var query = new PathQuery(
            baseline.Start,
            baseline.End,
            baseline.Agent,
            baseline.AreaPolicy,
            baseline.Traversal,
            baseline.Algorithm,
            baseline.Budget,
            allowTransitions: true);
        var workspace = new NavigationAStarWorkspace(1, 4, 6, 4, 4, 4, 4);
        var cache = new NavigationAStarPayloadCache(
            world,
            maxEntries: 1,
            maxActiveLeases: 2);
        using var work = new NavigationAStarQueryWork(world, store, workspace, cache);
        long maximumBytes = NavigationAStarPayload.GetMaximumRetainedBytes(
            workspace.GuidePoints.Length,
            workspace.PathNodes.Length - 1,
            workspace.EndpointComponents.Length,
            workspace.EndpointPages.Length);
        cache.TryReservePayload(
                maximumBytes,
                out NavigationAStarPayloadReservation firstReservation)
            .Should().BeTrue();
        work.BeginReserved(query, store.TryAcquire()!, ref firstReservation);
        work.IsReadyToPublish.Should().BeTrue();
        cache.ReservedLeaseCount.Should().Be(1);

        using NavigationWorldGraphLease secondLease = store.TryAcquire()!;
        cache.TryReservePayload(
                maximumBytes,
                out NavigationAStarPayloadReservation secondReservation)
            .Should().BeTrue();
        Action secondBegin = () => work.BeginReserved(
            query,
            secondLease,
            ref secondReservation);

        secondBegin.Should().Throw<InvalidOperationException>();
        cache.ReservedLeaseCount.Should().Be(2);
        cache.ReleasePayloadReservation(ref secondReservation);
        work.Publish().Should().Be(NavigationAStarQueryStatus.Unsupported);
        cache.ReservedLeaseCount.Should().Be(0);
    }
}

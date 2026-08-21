using System;
using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using Trailblazer.Pathing;
using Xunit;

namespace Trailblazer.Tests.Pathing;

[Collection("PathingCollection")]
public sealed class PathManagerCoverageTailTests : IDisposable
{
    public PathManagerCoverageTailTests()
    {
        TestWorld.Setup();
    }

    public void Dispose()
    {
        PathManager.Reset();
        TestWorld.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SnapshotHelpers_ShouldReturnEmptyAndOnlyInitializedChartsInRegistrationOrder()
    {
        ReflectionUtility.InvokePrivateStatic<NavigationChart[]>(
            typeof(PathManager),
            "GetInitializedChartsSnapshot").Should().BeEmpty();

        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-8, -4, -4), new Vector3d(8, 4, 4)), out _);

        NavigationChart deferred = PathTestFactory.BuildSinglePointMap("SnapshotDeferred", Vector3d.Zero);
        NavigationChart initializedA = PathTestFactory.BuildSinglePointMap("SnapshotInitA", new Vector3d(2, 0, 0));
        NavigationChart initializedB = PathTestFactory.BuildSinglePointMap("SnapshotInitB", new Vector3d(4, 0, 0));

        PathManager.Register(deferred, initializeChart: false).Should().BeTrue();
        PathManager.Register(initializedA).Should().BeTrue();
        PathManager.Register(initializedB).Should().BeTrue();

        NavigationChart[] initializedCharts = ReflectionUtility.InvokePrivateStatic<NavigationChart[]>(
            typeof(PathManager),
            "GetInitializedChartsSnapshot");

        initializedCharts.Should().HaveCount(2);
        initializedCharts[0].Name.Should().Be("SnapshotInitA");
        initializedCharts[1].Name.Should().Be("SnapshotInitB");
    }

    [Fact]
    public void SnapshotHelpers_ShouldFilterInitializedChartsByBoundsAndGridTouches()
    {
        ReflectionUtility.InvokePrivateStatic<NavigationChart[]>(
            typeof(PathManager),
            "GetInitializedChartsIntersectingBoundsSnapshot",
            Vector3d.Zero,
            Vector3d.One).Should().BeEmpty();
        ReflectionUtility.InvokePrivateStatic<NavigationChart[]>(
            typeof(PathManager),
            "GetInitializedChartsTouchingGridSnapshot",
            (ushort)42).Should().BeEmpty();
        ReflectionUtility.InvokePrivateStatic<NavigationChart[]>(
            typeof(PathManager),
            "GetInitializedChartsWithAuthoredCellsIntersectingBoundsSnapshot",
            Vector3d.Zero,
            Vector3d.One).Should().BeEmpty();

        TestWorld.World.TryAddGrid(
            new GridConfiguration(new Vector3d(-8, -4, -4), new Vector3d(24, 8, 8)),
            out ushort gridIndex);

        NavigationChart initializedA = PathTestFactory.BuildSinglePointMap("SnapshotBoundsA", Vector3d.Zero);
        NavigationChart initializedB = PathTestFactory.BuildSinglePointMap("SnapshotBoundsB", new Vector3d(8, 0, 0));
        NavigationChart deferred = PathTestFactory.BuildSinglePointMap("SnapshotBoundsDeferred", new Vector3d(2, 0, 0));

        PathManager.Register(initializedA).Should().BeTrue();
        PathManager.Register(initializedB).Should().BeTrue();
        PathManager.Register(deferred, initializeChart: false).Should().BeTrue();

        NavigationChart[] boundsMatches = ReflectionUtility.InvokePrivateStatic<NavigationChart[]>(
            typeof(PathManager),
            "GetInitializedChartsIntersectingBoundsSnapshot",
            new Vector3d(-1, -1, -1),
            new Vector3d(1, 1, 1));
        boundsMatches.Should().ContainSingle(chart => chart.Name == initializedA.Name);

        NavigationChart[] authoredMatches = ReflectionUtility.InvokePrivateStatic<NavigationChart[]>(
            typeof(PathManager),
            "GetInitializedChartsWithAuthoredCellsIntersectingBoundsSnapshot",
            new Vector3d(-1, -1, -1),
            new Vector3d(1, 1, 1));
        authoredMatches.Should().ContainSingle(chart => chart.Name == initializedA.Name);

        NavigationChart[] gridMatches = ReflectionUtility.InvokePrivateStatic<NavigationChart[]>(
            typeof(PathManager),
            "GetInitializedChartsTouchingGridSnapshot",
            gridIndex);
        gridMatches.Should().ContainInOrder(initializedA, initializedB);
        gridMatches.Should().NotContain(chart => chart.Name == deferred.Name);

        ReflectionUtility.InvokePrivateStatic<int>(
                typeof(PathManager),
                "RebuildInitializedChartsAgainstExternalGridBounds",
                new[] { typeof(GridWorld), typeof(ushort), typeof(Vector3d), typeof(Vector3d), typeof(bool) },
                TestWorld.World,
                gridIndex,
                new Vector3d(-1, -1, -1),
                new Vector3d(1, 1, 1),
                false)
            .Should()
            .Be(1);
        ReflectionUtility.InvokePrivateStatic<int>(
                typeof(PathManager),
                "RebuildInitializedChartsAgainstExternalGridBounds",
                new[] { typeof(GridWorld), typeof(ushort), typeof(Vector3d), typeof(Vector3d), typeof(bool) },
                TestWorld.World,
                gridIndex,
                new Vector3d(-1, -1, -1),
                new Vector3d(1, 1, 1),
                true)
            .Should()
            .Be(2);
    }

    [Fact]
    public void ContextSelectionGuards_ShouldRejectMissingOrMismatchedWorlds()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(8, 8, 8)), out _);
        PathManager.ConfiguredWorld.Should().BeSameAs(TestWorld.World);

        PathManager.Tick();
        PathManager.Reset(TestWorld.World);

        using TrailblazerWorldContext otherContext = PathTestFactory.CreateContextWithGrid();
        NavigationChart mismatchedChart = PathTestFactory.BuildSinglePointMap("MismatchedWorldChart", Vector3d.Zero);

        Action mismatchedWorld = () => PathManager.Register(otherContext.World, mismatchedChart);
        mismatchedWorld.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*active Trailblazer pathing context*");

        TestWorld.Reset();
        using var detachedWorld = new GridWorld();
        NavigationChart detachedChart = PathTestFactory.BuildSinglePointMap("DetachedWorldChart", Vector3d.Zero);

        Action directWorldCall = () => PathManager.Register(detachedWorld, detachedChart);
        directWorldCall.Should()
            .Throw<InvalidOperationException>()
            .WithMessage("*TrailblazerWorldContext*");
    }

    [Fact]
    public void ExternalGridBridgeTailHelpers_ShouldHandleNoSelectionResetAndDuplicateRemovedEvents()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out ushort gridIndex);
        GridEventInfo eventInfo = CreateGridEventInfo(
            gridIndex,
            12,
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)),
            1);

        PendingExternalGridChange addedMerge = ReflectionUtility.InvokePrivateStatic<PendingExternalGridChange>(
            typeof(PathManagerExternalGridBridge),
            "MergePendingGridChangeForSameSpawnToken",
            new PendingExternalGridChange(12, 1, Vector3d.Zero, Vector3d.One, false, false),
            eventInfo,
            ExternalGridEventKind.Added);
        addedMerge.RequiresAuthoredCellBoundsSelection.Should().BeTrue();

        PendingExternalGridChange removedMerge = ReflectionUtility.InvokePrivateStatic<PendingExternalGridChange>(
            typeof(PathManagerExternalGridBridge),
            "MergePendingGridChangeForSameSpawnToken",
            new PendingExternalGridChange(12, 1, Vector3d.Zero, Vector3d.One, true, false),
            eventInfo,
            ExternalGridEventKind.Removed);
        removedMerge.RequiresLiveGridTouchSelection.Should().BeTrue();
        removedMerge.RequiresAuthoredCellBoundsSelection.Should().BeFalse();

        PathManager.ActiveState.PendingGridChangesByGridIndex[gridIndex] = new PendingExternalGridChange(
            12,
            1,
            Vector3d.Zero,
            Vector3d.One,
            requiresLiveGridTouchSelection: false,
            requiresAuthoredCellBoundsSelection: false);
        PathManager.ActiveState.PendingGridChangeOrder.Add(gridIndex);
        PathManagerExternalGridBridge.FlushPendingGridChanges();
        PathManager.ActiveState.PendingGridChangeOrder.Should().BeEmpty();

        PathManagerExternalGridBridge.HandleGridRemoved(eventInfo);
        PathManagerExternalGridBridge.HandleGridRemoved(eventInfo);
        PathManagerExternalGridBridge.GetDiagnosticsSnapshot().DuplicateRemoveEventSignaturesObserved.Should().Be(1);

        ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManagerExternalGridBridge),
            "HandleGridReset");
    }

    [Fact]
    public void ExternalGridBridgeDiagnostics_ShouldTrackChangedEvents_Rebuilds_AndIgnoredBounds()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out ushort nearGridIndex);
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(20, -4, -4), new Vector3d(28, 4, 4)), out ushort farGridIndex);

        PathManager.Register(PathTestFactory.BuildSinglePointMap("DiagnosticsNearChart", Vector3d.Zero)).Should().BeTrue();
        PathManagerExternalGridBridge.ResetDiagnostics();

        GridEventInfo intersectingChange = CreateGridEventInfo(
            nearGridIndex,
            101,
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)),
            8);
        GridEventInfo farChange = CreateGridEventInfo(
            farGridIndex,
            202,
            new GridConfiguration(new Vector3d(20, -4, -4), new Vector3d(28, 4, 4)),
            9);

        PathManagerExternalGridBridge.HandleGridChanged(intersectingChange);
        PathManagerExternalGridBridge.HandleGridChanged(farChange);

        ExternalGridBridgeDiagnosticsSnapshot snapshot = FlushExternalGridBridge();
        snapshot.TotalGridEventsReceived.Should().Be(2);
        snapshot.AddedEventsReceived.Should().Be(0);
        snapshot.RemovedEventsReceived.Should().Be(0);
        snapshot.ChangedEventsReceived.Should().Be(2);
        snapshot.DistinctGridSlotsObserved.Should().Be(2);
        snapshot.DuplicateEventSignaturesObserved.Should().Be(0);
        snapshot.RebuildPassesExecuted.Should().Be(1);
        snapshot.EventsIgnoredForNoIntersectingCharts.Should().Be(0);
        snapshot.TotalChartsSelectedForRebuild.Should().Be(1);
        snapshot.MaxChartsSelectedForSingleEvent.Should().Be(1);
        snapshot.MaxIdenticalEventStreak.Should().Be(1);
    }

    [Fact]
    public void ExternalGridBridgeDiagnostics_ShouldTrackDuplicateEventSignatures_PerKind()
    {
        PathManagerExternalGridBridge.ResetDiagnostics();

        GridEventInfo duplicateAdd = CreateGridEventInfo(
            7,
            91,
            new GridConfiguration(new Vector3d(-2, -2, -2), new Vector3d(2, 2, 2)),
            3);

        PathManagerExternalGridBridge.HandleGridAdded(duplicateAdd);
        PathManagerExternalGridBridge.HandleGridAdded(duplicateAdd);

        ExternalGridBridgeDiagnosticsSnapshot snapshot = FlushExternalGridBridge();
        snapshot.TotalGridEventsReceived.Should().Be(2);
        snapshot.AddedEventsReceived.Should().Be(2);
        snapshot.RemovedEventsReceived.Should().Be(0);
        snapshot.ChangedEventsReceived.Should().Be(0);
        snapshot.DistinctGridSlotsObserved.Should().Be(1);
        snapshot.DuplicateEventSignaturesObserved.Should().Be(1);
        snapshot.DuplicateAddEventSignaturesObserved.Should().Be(1);
        snapshot.DuplicateRemoveEventSignaturesObserved.Should().Be(0);
        snapshot.DuplicateChangeEventSignaturesObserved.Should().Be(0);
        snapshot.MaxIdenticalEventStreak.Should().Be(2);
        snapshot.RebuildPassesExecuted.Should().Be(0);
        snapshot.EventsIgnoredForNoIntersectingCharts.Should().Be(1);
        snapshot.TotalChartsSelectedForRebuild.Should().Be(0);
        snapshot.MaxChartsSelectedForSingleEvent.Should().Be(0);
    }

    [Fact]
    public void ExternalGridBridgeDiagnostics_ShouldSkipExactDuplicateIntersectingChangeEvents()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out ushort gridIndex);

        PathManager.Register(PathTestFactory.BuildSinglePointMap("DuplicateIntersectingGridChart", Vector3d.Zero)).Should().BeTrue();
        PathManagerExternalGridBridge.ResetDiagnostics();

        GridEventInfo duplicateChange = CreateGridEventInfo(
            gridIndex,
            404,
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)),
            11);

        PathManagerExternalGridBridge.HandleGridChanged(duplicateChange);
        PathManagerExternalGridBridge.HandleGridChanged(duplicateChange);

        ExternalGridBridgeDiagnosticsSnapshot snapshot = FlushExternalGridBridge();
        snapshot.TotalGridEventsReceived.Should().Be(2);
        snapshot.ChangedEventsReceived.Should().Be(2);
        snapshot.DuplicateEventSignaturesObserved.Should().Be(1);
        snapshot.DuplicateChangeEventSignaturesObserved.Should().Be(1);
        snapshot.RebuildPassesExecuted.Should().Be(1);
        snapshot.EventsIgnoredForNoIntersectingCharts.Should().Be(0);
        snapshot.TotalChartsSelectedForRebuild.Should().Be(1);
        snapshot.MaxChartsSelectedForSingleEvent.Should().Be(1);
        snapshot.MaxIdenticalEventStreak.Should().Be(2);
    }

    [Fact]
    public void ExternalGridBridgeDiagnostics_ShouldNotSkipGridVersionChanges()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out ushort gridIndex);

        PathManager.Register(PathTestFactory.BuildSinglePointMap("VersionBumpGridChart", Vector3d.Zero)).Should().BeTrue();
        PathManagerExternalGridBridge.ResetDiagnostics();

        GridEventInfo firstChange = CreateGridEventInfo(
            gridIndex,
            505,
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)),
            12);
        GridEventInfo versionBumpChange = CreateGridEventInfo(
            gridIndex,
            505,
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)),
            13);

        PathManagerExternalGridBridge.HandleGridChanged(firstChange);
        PathManagerExternalGridBridge.HandleGridChanged(versionBumpChange);

        ExternalGridBridgeDiagnosticsSnapshot preFlush = PathManagerExternalGridBridge.GetDiagnosticsSnapshot();
        preFlush.RebuildPassesExecuted.Should().Be(0);

        ExternalGridBridgeDiagnosticsSnapshot snapshot = FlushExternalGridBridge();
        snapshot.TotalGridEventsReceived.Should().Be(2);
        snapshot.ChangedEventsReceived.Should().Be(2);
        snapshot.DuplicateEventSignaturesObserved.Should().Be(0);
        snapshot.RebuildPassesExecuted.Should().Be(1);
        snapshot.EventsIgnoredForNoIntersectingCharts.Should().Be(0);
        snapshot.TotalChartsSelectedForRebuild.Should().Be(1);
        snapshot.MaxChartsSelectedForSingleEvent.Should().Be(1);
        snapshot.MaxIdenticalEventStreak.Should().Be(1);
    }

    [Fact]
    public void ExternalGridBridgeDiagnostics_ShouldNotSkipSpawnTokenChangesForSameGridSlot()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

        PathManager.Register(PathTestFactory.BuildSinglePointMap("SpawnTokenGridChart", Vector3d.Zero)).Should().BeTrue();
        PathManagerExternalGridBridge.ResetDiagnostics();

        GridEventInfo firstAdd = CreateGridEventInfo(
            6,
            606,
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)),
            1);
        GridEventInfo replacedAdd = CreateGridEventInfo(
            6,
            607,
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)),
            1);

        PathManagerExternalGridBridge.HandleGridAdded(firstAdd);
        PathManagerExternalGridBridge.HandleGridAdded(replacedAdd);

        ExternalGridBridgeDiagnosticsSnapshot snapshot = FlushExternalGridBridge();
        snapshot.TotalGridEventsReceived.Should().Be(2);
        snapshot.AddedEventsReceived.Should().Be(2);
        snapshot.DuplicateEventSignaturesObserved.Should().Be(0);
        snapshot.RebuildPassesExecuted.Should().Be(1);
        snapshot.EventsIgnoredForNoIntersectingCharts.Should().Be(0);
        snapshot.TotalChartsSelectedForRebuild.Should().Be(1);
        snapshot.MaxChartsSelectedForSingleEvent.Should().Be(1);
        snapshot.MaxIdenticalEventStreak.Should().Be(1);
    }

    [Fact]
    public void ExternalGridBridge_ShouldUseLiveGridTouchesInsteadOfChartBounds_ForChangedEvents()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(0, -4, -4), new Vector3d(4, 4, 4)), out _);
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(8, -4, -4), new Vector3d(12, 4, 4)), out ushort rightGridIndex);

        PathManager.Register(CreateSparseChart("SparseBoundsChart", Vector3d.Zero, authoredX: 0, sizeX: 12)).Should().BeTrue();
        PathManager.Register(PathTestFactory.BuildSinglePointMap("RightTouchChart", new Vector3d(9, 0, 0))).Should().BeTrue();
        PathManagerExternalGridBridge.ResetDiagnostics();

        GridEventInfo rightGridChange = CreateGridEventInfo(
            rightGridIndex,
            222,
            new GridConfiguration(new Vector3d(8, -4, -4), new Vector3d(12, 4, 4)),
            1);

        PathManagerExternalGridBridge.HandleGridChanged(rightGridChange);

        ExternalGridBridgeDiagnosticsSnapshot preFlush = PathManagerExternalGridBridge.GetDiagnosticsSnapshot();
        preFlush.RebuildPassesExecuted.Should().Be(0);

        ExternalGridBridgeDiagnosticsSnapshot snapshot = FlushExternalGridBridge();
        snapshot.RebuildPassesExecuted.Should().Be(1);
        snapshot.TotalChartsSelectedForRebuild.Should().Be(1);
        snapshot.MaxChartsSelectedForSingleEvent.Should().Be(1);
    }

    [Fact]
    public void ExternalGridBridge_ShouldUseAuthoredCellsInsteadOfChartBounds_ForAddedEvents()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(0, -4, -4), new Vector3d(4, 4, 4)), out _);

        PathManager.Register(CreateSparseChart("SparseAddBoundsChart", Vector3d.Zero, authoredX: 0, sizeX: 12)).Should().BeTrue();
        PathManagerExternalGridBridge.ResetDiagnostics();

        GridEventInfo rightGridAdd = CreateGridEventInfo(
            2,
            333,
            new GridConfiguration(new Vector3d(8, -4, -4), new Vector3d(12, 4, 4)),
            1);

        PathManagerExternalGridBridge.HandleGridAdded(rightGridAdd);

        ExternalGridBridgeDiagnosticsSnapshot snapshot = FlushExternalGridBridge();
        snapshot.RebuildPassesExecuted.Should().Be(0);
        snapshot.EventsIgnoredForNoIntersectingCharts.Should().Be(1);
        snapshot.TotalChartsSelectedForRebuild.Should().Be(0);
    }

    [Fact]
    public void ExternalGridBridge_ShouldUpdateLiveGridTouchesAfterChartCellMutations()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(0, -4, -4), new Vector3d(4, 4, 4)), out _);
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(8, -4, -4), new Vector3d(12, 4, 4)), out ushort rightGridIndex);

        NavigationChart chart = CreateSparseChart("MutableGridTouchChart", Vector3d.Zero, authoredX: 0, sizeX: 12);
        PathManager.Register(chart).Should().BeTrue();

        PathManager.TryUpdateChartCell(chart.Name, new Vector3d(9, 0, 0), NavigationChartCell.Solid).Should().BeTrue();
        PathManagerExternalGridBridge.ResetDiagnostics();

        GridEventInfo rightGridChange = CreateGridEventInfo(
            rightGridIndex,
            444,
            new GridConfiguration(new Vector3d(8, -4, -4), new Vector3d(12, 4, 4)),
            1);

        PathManagerExternalGridBridge.HandleGridChanged(rightGridChange);
        ExternalGridBridgeDiagnosticsSnapshot afterAdd = FlushExternalGridBridge();
        afterAdd.RebuildPassesExecuted.Should().Be(1);
        afterAdd.TotalChartsSelectedForRebuild.Should().Be(1);

        PathManager.TryUpdateChartCell(chart.Name, new Vector3d(9, 0, 0), NavigationChartCell.Empty).Should().BeTrue();
        PathManagerExternalGridBridge.ResetDiagnostics();

        PathManagerExternalGridBridge.HandleGridChanged(rightGridChange);
        ExternalGridBridgeDiagnosticsSnapshot afterRemove = FlushExternalGridBridge();
        afterRemove.RebuildPassesExecuted.Should().Be(0);
        afterRemove.EventsIgnoredForNoIntersectingCharts.Should().Be(1);
        afterRemove.TotalChartsSelectedForRebuild.Should().Be(0);
    }

    [Fact]
    public void ExternalGridBridge_ShouldCoalesceSpawnTokenReplacementIntoOneCombinedChartSelection()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(0, -4, -4), new Vector3d(4, 4, 4)), out ushort gridIndex);

        PathManager.Register(PathTestFactory.BuildSinglePointMap("OldGridTouchChart", Vector3d.Zero)).Should().BeTrue();
        PathManager.Register(PathTestFactory.BuildSinglePointMap("NewGridAuthoredChart", new Vector3d(10, 0, 0))).Should().BeTrue();
        PathManagerExternalGridBridge.ResetDiagnostics();

        GridEventInfo removedOldGrid = CreateGridEventInfo(
            gridIndex,
            701,
            new GridConfiguration(new Vector3d(0, -4, -4), new Vector3d(4, 4, 4)),
            2);
        GridEventInfo addedReplacementGrid = CreateGridEventInfo(
            gridIndex,
            702,
            new GridConfiguration(new Vector3d(8, -4, -4), new Vector3d(12, 4, 4)),
            1);

        PathManagerExternalGridBridge.HandleGridRemoved(removedOldGrid);
        PathManagerExternalGridBridge.HandleGridAdded(addedReplacementGrid);

        ExternalGridBridgeDiagnosticsSnapshot preFlush = PathManagerExternalGridBridge.GetDiagnosticsSnapshot();
        preFlush.RebuildPassesExecuted.Should().Be(0);

        ExternalGridBridgeDiagnosticsSnapshot snapshot = FlushExternalGridBridge();
        snapshot.TotalGridEventsReceived.Should().Be(2);
        snapshot.AddedEventsReceived.Should().Be(1);
        snapshot.RemovedEventsReceived.Should().Be(1);
        snapshot.DuplicateEventSignaturesObserved.Should().Be(0);
        snapshot.RebuildPassesExecuted.Should().Be(1);
        snapshot.EventsIgnoredForNoIntersectingCharts.Should().Be(0);
        snapshot.TotalChartsSelectedForRebuild.Should().Be(2);
        snapshot.MaxChartsSelectedForSingleEvent.Should().Be(2);
    }

    [Fact]
    public void ResolvedVoxelStateHelpers_ShouldAddRemoveAndIgnoreNonOwners()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

        NavigationChart chart = PathTestFactory.BuildSinglePointMap("ResolvedVoxelChart", Vector3d.Zero);
        NavigationChartRegistration registration = new(chart, registrationOrder: 1);
        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);

        SwiftDictionary<WorldVoxelIndex, ResolvedChartVoxelState> resolvedStates =
            PathManager.ActiveState.ResolvedChartVoxelStates;

        ResolvedChartVoxelState? state = null;
        ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManager),
            "TryUpdateResolvedVoxelStateForChartCell",
            registration,
            NavigationChartCell.Solid,
            voxel.WorldIndex,
            state!);
        resolvedStates.ContainsKey(voxel.WorldIndex).Should().BeTrue();

        state = resolvedStates[voxel.WorldIndex];
        ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManager),
            "TryUpdateResolvedVoxelStateForChartCell",
            registration,
            NavigationChartCell.Empty,
            voxel.WorldIndex,
            state);
        resolvedStates.ContainsKey(voxel.WorldIndex).Should().BeFalse();

        ResolvedChartVoxelState foreignOwnerState = new();
        foreignOwnerState.AddOwner("OtherChart", NavigationChartCell.Solid, priority: 0, registrationOrder: 1);
        resolvedStates[voxel.WorldIndex] = foreignOwnerState;

        ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManager),
            "TryUpdateResolvedVoxelStateForChartCell",
            registration,
            NavigationChartCell.Empty,
            voxel.WorldIndex,
            foreignOwnerState);
        resolvedStates.ContainsKey(voxel.WorldIndex).Should().BeTrue();
    }

    [Fact]
    public void ClearInitializedChartStateHelper_ShouldDropResolvedState_WhenGridVoxelNoLongerExists()
    {
        GridConfiguration config = new(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4));
        TestWorld.World.TryAddGrid(config, out ushort gridIndex).Should().BeTrue();

        NavigationChart chart = PathTestFactory.RegisterSingleWalkablePoint(TestWorld.Context, "ClearTailChart", Vector3d.Zero);
        PathManager.IsChartInitialized(chart).Should().BeTrue();

        TestWorld.World.TryRemoveGrid(gridIndex).Should().BeTrue();

        ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManager),
            "ClearInitializedChartLiveStatePreservingRegistration",
            new[] { typeof(GridWorld), typeof(NavigationChart) },
            TestWorld.World,
            chart);

        PathManager.IsChartInitialized(chart).Should().BeFalse();
        PathManager.TryGetEffectiveCell(Vector3d.Zero, out _).Should().BeFalse();
    }

    [Fact]
    public void ChartUpdateHelper_ShouldReturnEarlyForDeferredChart()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

        NavigationChart chart = PathTestFactory.BuildSinglePointMap("DeferredUpdateTailChart", Vector3d.Zero);
        PathManager.Register(chart, initializeChart: false).Should().BeTrue();
        PathManager.TryGetNavigationChartRegistration(chart.Name, out NavigationChartRegistration registration)
            .Should().BeTrue();

        SwiftHashSet<SolidChartPartition> partitionsToRebind = new();
        SwiftHashSet<string> invalidatedChartKeys = new();
        ReflectionUtility.InvokePrivateStatic<bool>(
            typeof(PathManager),
            "TryApplyChartCellUpdate",
            new[]
            {
                typeof(GridWorld),
                typeof(NavigationChartRegistration),
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(NavigationChartCell),
                typeof(SwiftHashSet<SolidChartPartition>),
                typeof(SwiftHashSet<string>)
            },
            TestWorld.World,
            registration,
            1,
            1,
            1,
            NavigationChartCell.Liquid,
            partitionsToRebind,
            invalidatedChartKeys).Should().BeTrue();

        invalidatedChartKeys.Should().BeEmpty();
        partitionsToRebind.Should().BeEmpty();
    }

    private static GridEventInfo CreateGridEventInfo(
        ushort gridIndex,
        long gridSpawnToken,
        GridConfiguration configuration,
        uint gridVersion)
    {
        return new GridEventInfo(
            TestWorld.World.SpawnToken,
            gridIndex,
            gridSpawnToken,
            configuration,
            gridVersion);
    }

    private static NavigationChart CreateSparseChart(string name, Vector3d minBounds, int authoredX, int sizeX)
    {
        NavigationChartCell[,,] data = new NavigationChartCell[1, sizeX, 1];
        data[0, authoredX, 0] = NavigationChartCell.Solid;
        return NavigationChart.From3D(name, data, minBounds, Fixed64.One);
    }

    private static ExternalGridBridgeDiagnosticsSnapshot FlushExternalGridBridge()
    {
        PathManagerExternalGridBridge.FlushPendingGridChanges();
        return PathManagerExternalGridBridge.GetDiagnosticsSnapshot();
    }
}

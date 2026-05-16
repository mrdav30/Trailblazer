using FixedMathSharp;
using FluentAssertions;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;
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
    public void ManagedTransitionHelpers_ShouldHandleMissingChartsAndIgnoredRefreshRequests()
    {
        Action refreshMissingChart = () => ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManager),
            "RefreshManagedGeneratedTransitionsForChart",
            new[] { typeof(GridWorld), typeof(string) },
            TestWorld.World,
            "MissingChart");
        Action refreshEmptySet = () => ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManager),
            "RefreshManagedGeneratedTransitionsForCharts",
            new[] { typeof(GridWorld), typeof(SwiftHashSet<string>), typeof(string) },
            TestWorld.World,
            new SwiftHashSet<string>(),
            null!);
        Action refreshUnknownVoxelChart = () => ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManager),
            "RefreshManagedGeneratedTransitionsForVoxel",
            new[] { typeof(GridWorld), typeof(Vector3d), typeof(SwiftHashSet<string>) },
            TestWorld.World,
            new Vector3d(99, 99, 99),
            new SwiftHashSet<string> { "MissingChart", string.Empty });

        refreshMissingChart.Should().NotThrow();
        refreshEmptySet.Should().NotThrow();
        refreshUnknownVoxelChart.Should().NotThrow();
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
    public void ManagedTransitionStateHelpers_ShouldHandleMissingAndExistingEntries()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

        SwiftDictionary<string, NavigationChartRegistration> states = PathManager.ActiveState.NavigationChartMap;

        ReflectionUtility.InvokePrivateStatic<string[]>(
            typeof(PathManager),
            "RemoveManagedGeneratedTransitions",
            "MissingChart").Should().BeEmpty();

        NavigationChartRegistration state = CreateRegistration("ManagedTailChart", "tail-prefix", priority: 3);
        state.TransitionIds.Add("keep-id");
        states["ManagedTailChart"] = state;

        TraversalTransition transition = CreateTransition("new-id", Vector3d.Zero, new Vector3d(1, 0, 0));
        ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManager),
            "AddManagedGeneratedTransitionIds",
            "ManagedTailChart",
            new[] { transition });
        state.TransitionIds.Should().Contain(new[] { "keep-id", "new-id" });

        ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManager),
            "RemoveManagedGeneratedTransitionIds",
            "ManagedTailChart",
            new[] { "keep-id" });
        state.TransitionIds.Should().NotContain("keep-id");

        string[] removed = ReflectionUtility.InvokePrivateStatic<string[]>(
            typeof(PathManager),
            "RemoveManagedGeneratedTransitions",
            "ManagedTailChart");
        removed.Should().ContainSingle(id => id == "new-id");
        states.ContainsKey("ManagedTailChart").Should().BeTrue();
        state.TransitionIds.Should().BeEmpty();
    }

    [Fact]
    public void ManagedTransitionStateHelpers_ShouldIgnoreAddAndRemoveRequests_WhenChartStateIsMissing()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

        Action addMissing = () => ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManager),
            "AddManagedGeneratedTransitionIds",
            "MissingChart",
            new[] { CreateTransition("missing-add", Vector3d.Zero, new Vector3d(1, 0, 0)) });
        Action removeMissing = () => ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManager),
            "RemoveManagedGeneratedTransitionIds",
            "MissingChart",
            new[] { "missing-remove" });

        addMissing.Should().NotThrow();
        removeMissing.Should().NotThrow();
    }

    [Fact]
    public void ManagedTransitionDeltaHelpers_ShouldRegisterMissingAndRemoveObsoleteTransitions()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

        SwiftDictionary<string, NavigationChartRegistration> states = PathManager.ActiveState.NavigationChartMap;

        NavigationChartRegistration state = CreateRegistration("DeltaChart", "delta-prefix", priority: 5);
        state.TransitionIds.Add("obsolete-id");
        states["DeltaChart"] = state;

        SwiftHashSet<string> desiredTransitionIds = new() { "new-id" };
        SwiftHashSet<string> activeTransitionIds = new() { "new-id" };
        TraversalTransition[] missingTransitions = { CreateTransition("new-id", Vector3d.Zero, new Vector3d(1, 0, 0)) };

        ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManager),
            "ApplyManagedGeneratedTransitionDelta",
            "DeltaChart",
            state,
            desiredTransitionIds,
            activeTransitionIds,
            missingTransitions);

        state.TransitionIds.Should().Contain("new-id");
        state.TransitionIds.Should().NotContain("obsolete-id");
        TraversalTransitionRegistry.IsRegistered("new-id").Should().BeTrue();
        TraversalTransitionRegistry.IsRegistered("obsolete-id").Should().BeFalse();
    }

    [Fact]
    public void ManagedTransitionComparisonHelpers_ShouldReturnEmptyWhenNoDeltaIsRequired()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

        NavigationChartRegistration state = CreateRegistration("CompareChart", "compare-prefix", priority: 1);
        TraversalTransition keepTransition = CreateTransition("keep-id", Vector3d.Zero, new Vector3d(1, 0, 0));
        state.TransitionIds.Add(keepTransition.Id);

        ReflectionUtility.InvokePrivateStatic<string[]>(
            typeof(PathManager),
            "GetObsoleteManagedGeneratedTransitionIds",
            new[] { typeof(NavigationChartRegistration), typeof(string[]), typeof(TraversalTransition[]) },
            state,
            new[] { keepTransition.Id },
            new[] { keepTransition }).Should().BeEmpty();

        ReflectionUtility.InvokePrivateStatic<TraversalTransition[]>(
            typeof(PathManager),
            "GetMissingManagedGeneratedTransitions",
            state,
            new[] { keepTransition }).Should().BeEmpty();
    }

    [Fact]
    public void ManagedTransitionCollectionHelpers_ShouldHandleEdgePairsAndOwnerResolution()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

        NavigationChartCell[,,] cells = new NavigationChartCell[1, 1, 1];
        cells[0, 0, 0] = new NavigationChartCell(
            TraversalMedia.Solid,
            generatedTransitionMedia: TraversalMedia.Solid);
        NavigationChart chart = NavigationChart.From3D("ManagedEdgeChart", cells, Vector3d.Zero, Fixed64.One);

        NavigationChartRegistration state = new(chart, registrationOrder: 1, "edge-prefix");
        TraversalTransition[] transitions = ReflectionUtility.InvokePrivateStatic<TraversalTransition[]>(
            typeof(PathManager),
            "CollectManagedGeneratedTransitionsForChart",
            new[]
            {
                typeof(GridWorld),
                typeof(NavigationChart),
                typeof(NavigationChartRegistration),
                typeof(SwiftHashSet<string>),
                typeof(SwiftHashSet<string>)
            },
            TestWorld.World,
            chart,
            state,
            new SwiftHashSet<string>(),
            new SwiftHashSet<string>());
        transitions.Should().BeEmpty();

        ResolvedChartVoxelState resolvedState = new();
        resolvedState.AddOwner(chart.Name, NavigationChartCell.Solid, priority: 0, registrationOrder: 1);
        SwiftDictionary<WorldVoxelIndex, ResolvedChartVoxelState> resolvedStates =
            PathManager.ActiveState.ResolvedChartVoxelStates;
        Voxel voxel = TestRequire.VoxelAt(TestWorld.Context, Vector3d.Zero);
        resolvedStates[voxel.WorldIndex] = resolvedState;

        ReflectionUtility.InvokePrivateStatic<bool>(
            typeof(PathManager),
            "IsChartEffectiveOwnerAtPosition",
            new[] { typeof(GridWorld), typeof(string), typeof(Vector3d) },
            TestWorld.World,
            chart.Name,
            Vector3d.Zero).Should().BeTrue();
        ReflectionUtility.InvokePrivateStatic<bool>(
            typeof(PathManager),
            "IsChartEffectiveOwnerAtPosition",
            new[] { typeof(GridWorld), typeof(string), typeof(Vector3d) },
            TestWorld.World,
            "OtherChart",
            Vector3d.Zero).Should().BeFalse();
        ReflectionUtility.InvokePrivateStatic<bool>(
            typeof(PathManager),
            "IsChartEffectiveOwnerAtPosition",
            new[] { typeof(GridWorld), typeof(string), typeof(Vector3d) },
            TestWorld.World,
            chart.Name,
            new Vector3d(9, 0, 0)).Should().BeFalse();
    }

    [Fact]
    public void ManagedTransitionCollectionHelpers_ShouldTrackActiveGeneratedTransitionsWithoutReportingMissingOnes()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

        string[,,] map =
        {
            {
                { "S!" },
                { "L!" }
            }
        };

        TraversalBuildResult buildResult = new TraversalAuthoringMap(
            chartName: "ManagedPairChart",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One).Build();

        buildResult.GeneratedTransitions.Should().NotBeEmpty();
        PathManager.Register(buildResult).Should().BeTrue();

        NavigationChartRegistration state = new(buildResult.Chart, registrationOrder: 1, buildResult.GeneratedTransitionIdPrefix);
        foreach (TraversalTransition transition in buildResult.GeneratedTransitions)
            state.TransitionIds.Add(transition.Id);

        SwiftHashSet<string> desiredTransitionIds = new();
        SwiftHashSet<string> activeTransitionIds = new();
        TraversalTransition[] missingTransitions = ReflectionUtility.InvokePrivateStatic<TraversalTransition[]>(
            typeof(PathManager),
            "CollectManagedGeneratedTransitionsForChart",
            new[]
            {
                typeof(GridWorld),
                typeof(NavigationChart),
                typeof(NavigationChartRegistration),
                typeof(SwiftHashSet<string>),
                typeof(SwiftHashSet<string>)
            },
            TestWorld.World,
            buildResult.Chart,
            state,
            desiredTransitionIds,
            activeTransitionIds);

        desiredTransitionIds.Count.Should().Be(buildResult.GeneratedTransitions.Length);
        activeTransitionIds.Count.Should().Be(buildResult.GeneratedTransitions.Length);
        missingTransitions.Should().BeEmpty();
    }

    [Fact]
    public void ManagedTransitionCollectionHelpers_ShouldReportMissingGeneratedTransitions_WhenStateIsEmpty()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

        string[,,] map =
        {
            {
                { "S!" },
                { "L!" }
            }
        };

        TraversalBuildResult buildResult = new TraversalAuthoringMap(
            chartName: "ManagedMissingChart",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One).Build();
        PathManager.Register(buildResult).Should().BeTrue();

        NavigationChartRegistration state = new(buildResult.Chart, registrationOrder: 1, buildResult.GeneratedTransitionIdPrefix);
        SwiftHashSet<string> desiredTransitionIds = new();
        SwiftHashSet<string> activeTransitionIds = new();

        TraversalTransition[] missingTransitions = ReflectionUtility.InvokePrivateStatic<TraversalTransition[]>(
            typeof(PathManager),
            "CollectManagedGeneratedTransitionsForChart",
            new[]
            {
                typeof(GridWorld),
                typeof(NavigationChart),
                typeof(NavigationChartRegistration),
                typeof(SwiftHashSet<string>),
                typeof(SwiftHashSet<string>)
            },
            TestWorld.World,
            buildResult.Chart,
            state,
            desiredTransitionIds,
            activeTransitionIds);

        missingTransitions.Should().HaveCount(buildResult.GeneratedTransitions.Length);
        desiredTransitionIds.Count.Should().Be(buildResult.GeneratedTransitions.Length);
        activeTransitionIds.Count.Should().Be(buildResult.GeneratedTransitions.Length);
    }

    [Fact]
    public void ManagedTransitionCollectionHelpers_ShouldTrackInactivePairsWithoutActivatingThem()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

        string[,,] map =
        {
            {
                { "S!" },
                { "L!" }
            }
        };

        TraversalBuildResult buildResult = new TraversalAuthoringMap(
            chartName: "ManagedInactivePairChart",
            sourceMap: map,
            minBounds: Vector3d.Zero,
            interval: Fixed64.One).Build();

        TraversalTransition sampleTransition = buildResult.GeneratedTransitions[0];
        buildResult.Chart.TryWorldToIndex(sampleTransition.Source.Position, out int firstX, out int firstY, out int firstZ)
            .Should().BeTrue();
        buildResult.Chart.TryWorldToIndex(sampleTransition.Destination.Position, out int secondX, out int secondY, out int secondZ)
            .Should().BeTrue();

        NavigationChartRegistration state = new(buildResult.Chart, registrationOrder: 1, buildResult.GeneratedTransitionIdPrefix);
        SwiftHashSet<string> desiredTransitionIds = new();
        SwiftHashSet<string> activeTransitionIds = new();
        SwiftList<TraversalTransition> missingTransitions = new();

        ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManager),
            "CollectManagedGeneratedTransitionsForPair",
            new[]
            {
                typeof(GridWorld),
                typeof(NavigationChart),
                typeof(NavigationChartRegistration),
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(int),
                typeof(SwiftHashSet<string>),
                typeof(SwiftHashSet<string>),
                typeof(SwiftList<TraversalTransition>)
            },
            TestWorld.World,
            buildResult.Chart,
            state,
            firstX,
            firstY,
            firstZ,
            secondX,
            secondY,
            secondZ,
            desiredTransitionIds,
            activeTransitionIds,
            missingTransitions);

        desiredTransitionIds.Should().NotBeEmpty();
        activeTransitionIds.Should().BeEmpty();
        missingTransitions.Should().NotBeEmpty();
    }

    [Fact]
    public void ResolvedVoxelStateHelpers_ShouldAddRemoveAndIgnoreNonOwners()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

        NavigationChart chart = PathTestFactory.BuildSinglePointMap("ResolvedVoxelChart", Vector3d.Zero);
        NavigationChartRegistration registration = new(chart, registrationOrder: 1, chart.Name);
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
    public void ChartUpdateHelpers_ShouldReturnEarlyForDeferredChartsWhileTrackingRefresh()
    {
        TestWorld.World.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

        NavigationChart chart = PathTestFactory.BuildSinglePointMap("DeferredUpdateTailChart", Vector3d.Zero);
        PathManager.Register(chart, initializeChart: false).Should().BeTrue();
        PathManager.TryGetNavigationChartRegistration(chart.Name, out NavigationChartRegistration registration)
            .Should().BeTrue();

        SwiftHashSet<SolidChartPartition> partitionsToRebind = new();
        SwiftHashSet<string> invalidatedChartKeys = new();
        SwiftHashSet<string> managedChartsToRefresh = new();

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
                typeof(SwiftHashSet<string>),
                typeof(SwiftHashSet<string>)
            },
            TestWorld.World,
            registration,
            1,
            1,
            1,
            NavigationChartCell.Liquid,
            partitionsToRebind,
            invalidatedChartKeys,
            managedChartsToRefresh).Should().BeTrue();

        managedChartsToRefresh.Should().Contain(chart.Name);
        invalidatedChartKeys.Should().BeEmpty();
        partitionsToRebind.Should().BeEmpty();
    }

    private static TraversalTransition CreateTransition(string id, Vector3d source, Vector3d destination)
    {
        return new TraversalTransition(
            id,
            TraversalTransitionType.Jump,
            TraversalTransitionAnchor.Solid(source),
            TraversalTransitionAnchor.Solid(destination));
    }

    private static NavigationChartRegistration CreateRegistration(
        string chartName,
        string transitionPrefix,
        int priority)
    {
        bool[,,] data =
        {
            {
                { true }
            }
        };

        NavigationChart chart = NavigationChart.From3D(
            chartName,
            data,
            Vector3d.Zero,
            Fixed64.One,
            priority: priority);
        return new NavigationChartRegistration(chart, registrationOrder: 1, transitionPrefix);
    }

    private static GridEventInfo CreateGridEventInfo(
        ushort gridIndex,
        int gridSpawnToken,
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

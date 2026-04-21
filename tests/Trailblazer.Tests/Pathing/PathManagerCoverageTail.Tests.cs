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
        if (GlobalGridManager.IsActive)
            GlobalGridManager.Reset();
        else
            GlobalGridManager.Setup();
    }

    public void Dispose()
    {
        PathManager.Reset();
        GlobalGridManager.Reset();
        TrailblazerManager.Reset();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void SnapshotHelpers_ShouldReturnEmptyAndOnlyInitializedChartsInRegistrationOrder()
    {
        ReflectionUtility.InvokePrivateStatic<NavigationChart[]>(
            typeof(PathManager),
            "GetInitializedChartsSnapshot").Should().BeEmpty();

        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-8, -4, -4), new Vector3d(8, 4, 4)), out _);

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
            "MissingChart");
        Action refreshEmptySet = () => ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManager),
            "RefreshManagedGeneratedTransitionsForCharts",
            new[] { typeof(SwiftHashSet<string>), typeof(string) },
            new SwiftHashSet<string>(),
            null!);
        Action refreshUnknownVoxelChart = () => ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManager),
            "RefreshManagedGeneratedTransitionsForVoxel",
            new[] { typeof(Vector3d), typeof(SwiftHashSet<string>) },
            new Vector3d(99, 99, 99),
            new SwiftHashSet<string> { "MissingChart", string.Empty });

        refreshMissingChart.Should().NotThrow();
        refreshEmptySet.Should().NotThrow();
        refreshUnknownVoxelChart.Should().NotThrow();
    }

    [Fact]
    public void ExternalGridBridgeDiagnostics_ShouldTrackChangedEvents_Rebuilds_AndIgnoredBounds()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(20, -4, -4), new Vector3d(28, 4, 4)), out _);

        PathManager.Register(PathTestFactory.BuildSinglePointMap("DiagnosticsNearChart", Vector3d.Zero)).Should().BeTrue();
        PathManagerExternalGridBridge.ResetDiagnostics();

        GridEventInfo intersectingChange = new(
            1,
            101,
            new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)),
            8);
        GridEventInfo farChange = new(
            2,
            202,
            new GridConfiguration(new Vector3d(20, -4, -4), new Vector3d(28, 4, 4)),
            9);

        PathManagerExternalGridBridge.HandleGridChanged(intersectingChange);
        PathManagerExternalGridBridge.HandleGridChanged(farChange);

        ExternalGridBridgeDiagnosticsSnapshot snapshot = PathManagerExternalGridBridge.GetDiagnosticsSnapshot();
        snapshot.TotalGridEventsReceived.Should().Be(2);
        snapshot.AddedEventsReceived.Should().Be(0);
        snapshot.RemovedEventsReceived.Should().Be(0);
        snapshot.ChangedEventsReceived.Should().Be(2);
        snapshot.DistinctGridSlotsObserved.Should().Be(2);
        snapshot.DuplicateEventSignaturesObserved.Should().Be(0);
        snapshot.RebuildPassesExecuted.Should().Be(1);
        snapshot.EventsIgnoredForNoIntersectingCharts.Should().Be(1);
        snapshot.TotalChartsSelectedForRebuild.Should().Be(1);
        snapshot.MaxChartsSelectedForSingleEvent.Should().Be(1);
        snapshot.MaxIdenticalEventStreak.Should().Be(1);
    }

    [Fact]
    public void ExternalGridBridgeDiagnostics_ShouldTrackDuplicateEventSignatures_PerKind()
    {
        PathManagerExternalGridBridge.ResetDiagnostics();

        GridEventInfo duplicateAdd = new(
            7,
            91,
            new GridConfiguration(new Vector3d(-2, -2, -2), new Vector3d(2, 2, 2)),
            3);

        PathManagerExternalGridBridge.HandleGridAdded(duplicateAdd);
        PathManagerExternalGridBridge.HandleGridAdded(duplicateAdd);

        ExternalGridBridgeDiagnosticsSnapshot snapshot = PathManagerExternalGridBridge.GetDiagnosticsSnapshot();
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
        snapshot.EventsIgnoredForNoIntersectingCharts.Should().Be(2);
        snapshot.TotalChartsSelectedForRebuild.Should().Be(0);
        snapshot.MaxChartsSelectedForSingleEvent.Should().Be(0);
    }

    [Fact]
    public void ManagedTransitionStateHelpers_ShouldHandleMissingAndExistingEntries()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

        SwiftDictionary<string, ManagedChartTransitionState> states =
            ReflectionUtility.GetPrivateStaticField<SwiftDictionary<string, ManagedChartTransitionState>>(
                typeof(PathManager),
                "_managedGeneratedTransitionsByChart");

        ReflectionUtility.InvokePrivateStatic<string[]>(
            typeof(PathManager),
            "RemoveManagedGeneratedTransitions",
            "MissingChart").Should().BeEmpty();

        ManagedChartTransitionState state = new("tail-prefix", priority: 3);
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
        states.ContainsKey("ManagedTailChart").Should().BeFalse();
    }

    [Fact]
    public void ManagedTransitionStateHelpers_ShouldIgnoreAddAndRemoveRequests_WhenChartStateIsMissing()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

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
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

        SwiftDictionary<string, ManagedChartTransitionState> states =
            ReflectionUtility.GetPrivateStaticField<SwiftDictionary<string, ManagedChartTransitionState>>(
                typeof(PathManager),
                "_managedGeneratedTransitionsByChart");

        ManagedChartTransitionState state = new("delta-prefix", priority: 5);
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
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

        ManagedChartTransitionState state = new("compare-prefix", priority: 1);
        TraversalTransition keepTransition = CreateTransition("keep-id", Vector3d.Zero, new Vector3d(1, 0, 0));
        state.TransitionIds.Add(keepTransition.Id);

        ReflectionUtility.InvokePrivateStatic<string[]>(
            typeof(PathManager),
            "GetObsoleteManagedGeneratedTransitionIds",
            new[] { typeof(ManagedChartTransitionState), typeof(string[]), typeof(TraversalTransition[]) },
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
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

        NavigationChartCell[,,] cells = new NavigationChartCell[1, 1, 1];
        cells[0, 0, 0] = new NavigationChartCell(
            TraversalMedia.Solid,
            generatedTransitionMedia: TraversalMedia.Solid);
        NavigationChart chart = NavigationChart.From3D("ManagedEdgeChart", cells, Vector3d.Zero, Fixed64.One);

        ManagedChartTransitionState state = new("edge-prefix", priority: 0);
        TraversalTransition[] transitions = ReflectionUtility.InvokePrivateStatic<TraversalTransition[]>(
            typeof(PathManager),
            "CollectManagedGeneratedTransitionsForChart",
            chart,
            state,
            new SwiftHashSet<string>(),
            new SwiftHashSet<string>());
        transitions.Should().BeEmpty();

        ResolvedChartVoxelState resolvedState = new();
        resolvedState.AddOwner(chart.Name, NavigationChartCell.Solid, priority: 0, registrationOrder: 1);
        SwiftDictionary<GlobalVoxelIndex, ResolvedChartVoxelState> resolvedStates =
            ReflectionUtility.GetPrivateStaticField<SwiftDictionary<GlobalVoxelIndex, ResolvedChartVoxelState>>(
                typeof(PathManager),
                "_resolvedChartVoxelStates");
        GlobalGridManager.TryGetGridAndVoxel(Vector3d.Zero, out _, out Voxel voxel).Should().BeTrue();
        resolvedStates[voxel.GlobalIndex] = resolvedState;

        ReflectionUtility.InvokePrivateStatic<bool>(
            typeof(PathManager),
            "IsChartEffectiveOwnerAtPosition",
            chart.Name,
            Vector3d.Zero).Should().BeTrue();
        ReflectionUtility.InvokePrivateStatic<bool>(
            typeof(PathManager),
            "IsChartEffectiveOwnerAtPosition",
            "OtherChart",
            Vector3d.Zero).Should().BeFalse();
        ReflectionUtility.InvokePrivateStatic<bool>(
            typeof(PathManager),
            "IsChartEffectiveOwnerAtPosition",
            chart.Name,
            new Vector3d(9, 0, 0)).Should().BeFalse();
    }

    [Fact]
    public void ManagedTransitionCollectionHelpers_ShouldTrackActiveGeneratedTransitionsWithoutReportingMissingOnes()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

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

        ManagedChartTransitionState state = new(buildResult.GeneratedTransitionIdPrefix, priority: buildResult.Chart.Priority);
        foreach (TraversalTransition transition in buildResult.GeneratedTransitions)
            state.TransitionIds.Add(transition.Id);

        SwiftHashSet<string> desiredTransitionIds = new();
        SwiftHashSet<string> activeTransitionIds = new();
        TraversalTransition[] missingTransitions = ReflectionUtility.InvokePrivateStatic<TraversalTransition[]>(
            typeof(PathManager),
            "CollectManagedGeneratedTransitionsForChart",
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
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

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

        ManagedChartTransitionState state = new(buildResult.GeneratedTransitionIdPrefix, priority: buildResult.Chart.Priority);
        SwiftHashSet<string> desiredTransitionIds = new();
        SwiftHashSet<string> activeTransitionIds = new();

        TraversalTransition[] missingTransitions = ReflectionUtility.InvokePrivateStatic<TraversalTransition[]>(
            typeof(PathManager),
            "CollectManagedGeneratedTransitionsForChart",
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
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

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

        ManagedChartTransitionState state = new(buildResult.GeneratedTransitionIdPrefix, priority: buildResult.Chart.Priority);
        SwiftHashSet<string> desiredTransitionIds = new();
        SwiftHashSet<string> activeTransitionIds = new();
        SwiftList<TraversalTransition> missingTransitions = new();

        ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManager),
            "CollectManagedGeneratedTransitionsForPair",
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
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

        NavigationChart chart = PathTestFactory.BuildSinglePointMap("ResolvedVoxelChart", Vector3d.Zero);
        GlobalGridManager.TryGetGridAndVoxel(Vector3d.Zero, out _, out Voxel voxel).Should().BeTrue();

        SwiftDictionary<GlobalVoxelIndex, ResolvedChartVoxelState> resolvedStates =
            ReflectionUtility.GetPrivateStaticField<SwiftDictionary<GlobalVoxelIndex, ResolvedChartVoxelState>>(
                typeof(PathManager),
                "_resolvedChartVoxelStates");

        ResolvedChartVoxelState? state = null;
        ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManager),
            "TryUpdateResolvedVoxelStateForChartCell",
            chart,
            NavigationChartCell.Solid,
            voxel.GlobalIndex,
            state!);
        resolvedStates.ContainsKey(voxel.GlobalIndex).Should().BeTrue();

        state = resolvedStates[voxel.GlobalIndex];
        ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManager),
            "TryUpdateResolvedVoxelStateForChartCell",
            chart,
            NavigationChartCell.Empty,
            voxel.GlobalIndex,
            state);
        resolvedStates.ContainsKey(voxel.GlobalIndex).Should().BeFalse();

        ResolvedChartVoxelState foreignOwnerState = new();
        foreignOwnerState.AddOwner("OtherChart", NavigationChartCell.Solid, priority: 0, registrationOrder: 1);
        resolvedStates[voxel.GlobalIndex] = foreignOwnerState;

        ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManager),
            "TryUpdateResolvedVoxelStateForChartCell",
            chart,
            NavigationChartCell.Empty,
            voxel.GlobalIndex,
            foreignOwnerState);
        resolvedStates.ContainsKey(voxel.GlobalIndex).Should().BeTrue();
    }

    [Fact]
    public void ClearInitializedChartStateHelper_ShouldDropResolvedState_WhenGridVoxelNoLongerExists()
    {
        GridConfiguration config = new(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4));
        GlobalGridManager.TryAddGrid(config, out ushort gridIndex).Should().BeTrue();

        NavigationChart chart = PathTestFactory.RegisterSingleWalkablePoint("ClearTailChart", Vector3d.Zero);
        chart.IsInitialized.Should().BeTrue();

        GlobalGridManager.TryRemoveGrid(gridIndex).Should().BeTrue();

        ReflectionUtility.InvokePrivateStatic<object>(
            typeof(PathManager),
            "ClearInitializedChartLiveStatePreservingRegistration",
            chart);

        chart.IsInitialized.Should().BeFalse();
        PathManager.TryGetEffectiveCell(Vector3d.Zero, out _).Should().BeFalse();
    }

    [Fact]
    public void ChartUpdateHelpers_ShouldReturnEarlyForDeferredChartsWhileTrackingRefresh()
    {
        GlobalGridManager.TryAddGrid(new GridConfiguration(new Vector3d(-4, -4, -4), new Vector3d(4, 4, 4)), out _);

        NavigationChart chart = PathTestFactory.BuildSinglePointMap("DeferredUpdateTailChart", Vector3d.Zero);
        PathManager.Register(chart, initializeChart: false).Should().BeTrue();

        SwiftHashSet<SolidChartPartition> partitionsToRebind = new();
        SwiftHashSet<string> invalidatedChartKeys = new();
        SwiftHashSet<string> managedChartsToRefresh = new();

        ReflectionUtility.InvokePrivateStatic<bool>(
            typeof(PathManager),
            "TryApplyChartCellUpdate",
            chart,
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
}

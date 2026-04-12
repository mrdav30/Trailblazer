using FixedMathSharp;
using GridForge;
using GridForge.Grids;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;
using SwiftCollections.Pool;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Trailblazer.Pathing;

/// <summary>
/// Manages registration, initialization, and validation of navigation charts,
/// as well as providing global pathfinding utilities and neighbor discovery.
/// </summary>
public static class PathManager
{
    static PathManager()
    {
        // Subscribe once for the AppDomain lifetime. Both PathManager and GlobalGridManager are static
        // classes, so these subscriptions share that lifetime and never need to be torn down.
        // PathManager.Reset() is data-only and does not affect the subscriptions. The handlers are
        // safe on clean state: HandleExternalGridReset is idempotent, and HandleExternalGridChange
        // returns early when the chart map is empty, so no stale references can accumulate.
        GlobalGridManager.OnReset += HandleExternalGridReset;
        GlobalGridManager.OnActiveGridAdded += HandleExternalGridChange;
        GlobalGridManager.OnActiveGridRemoved += HandleExternalGridChange;
        GlobalGridManager.OnActiveGridChange += HandleExternalGridChange;
    }

    #region Pools

    internal static readonly SwiftHashSetPool<SolidChartPartition> PartitionSetPool = new SwiftHashSetPool<SolidChartPartition>();

    /// <summary>
    /// Pool of reusable <see cref="SolidChartPartition"/> instances used for partitioning the navigation grid.
    /// </summary>
    internal static readonly SwiftObjectPool<SolidChartPartition> PartitionPool = new(
        () => new SolidChartPartition(),
        actionOnRelease: partition => partition.Reset()
    );

    /// <summary>
    /// Pool of reusable <see cref="VolumeChartPartition"/> instances used for authored raw-volume traversal.
    /// </summary>
    internal static readonly SwiftObjectPool<VolumeChartPartition> VolumeChartPartitionPool = new(
        () => new VolumeChartPartition(),
        actionOnRelease: partition => partition.Reset()
    );

    #endregion

    #region Properties

    /// <summary>
    /// Gets an enumerable collection of all currently registered navigation charts.
    /// </summary>
    public static IEnumerable<NavigationChart> AllCharts
    {
        get
        {
            _navigationChartMapLock.EnterReadLock();
            try
            {
                if (_navigationChartMap.Count == 0)
                    return Array.Empty<NavigationChart>();

                NavigationChart[] charts = new NavigationChart[_navigationChartMap.Count];
                int index = 0;
                foreach (NavigationChart chart in _navigationChartMap.Values)
                    charts[index++] = chart;
                return charts;
            }
            finally { _navigationChartMapLock.ExitReadLock(); }
        }
    }

    /// <summary>
    /// Internal dictionary of all registered navigation charts, keyed by their unique names.
    /// </summary>
    private static readonly SwiftDictionary<string, NavigationChart> _navigationChartMap = new();

    private static readonly SwiftDictionary<string, ManagedChartTransitionState> _managedGeneratedTransitionsByChart = new(8, StringComparer.Ordinal);

    private static readonly SwiftDictionary<GlobalVoxelIndex, ResolvedChartVoxelState> _resolvedChartVoxelStates = new();

    /// <summary>
    /// Lock for managing concurrent access to <c>_navigationChartMap</c> operations.
    /// Ensures thread safety for read/write operations.
    /// </summary>
    private static readonly ReaderWriterLockSlim _navigationChartMapLock = new();

    private static int _activeAuthoredGasCellCount;

    private static int _activeAuthoredLiquidCellCount;

    private static int _nextChartRegistrationOrder;

    #endregion

    #region Lifecycle Hooks

    internal static void RegisterTrailblazerLifecycleHooks()
    {
        TrailblazerManager.RegisterOnSimulateCore(
            owner: "PathManager.Tick",
            order: TrailblazerLifecycleOrder.PathingMaintenance,
            callback: Tick);
    }

    internal static void Tick()
    {
        PathGuideFactory.CullExpiredGuides(TrailblazerManager.FrameCount);
    }

    private static void HandleExternalGridReset()
    {
        Reset();
    }

    private static void HandleExternalGridChange(GridEventInfo eventInfo)
    {
        RebuildInitializedChartsAgainstCurrentGrids(eventInfo.BoundsMin, eventInfo.BoundsMax);
    }

    /// <summary>
    /// Clears all registered maps, partitions, and guide pools.
    /// </summary>
    public static void Reset()
    {
        VolumeMediumRules.Reset();
        TraversalTransitionRegistry.Reset();
        ClearLiveGridState();

        _navigationChartMapLock.EnterWriteLock();
        try
        {
            MarkRegisteredChartsUninitialized_NoLock();
            _navigationChartMap.Clear();
            _managedGeneratedTransitionsByChart.Clear();
            _nextChartRegistrationOrder = 0;
        }
        finally
        {
            _navigationChartMapLock.ExitWriteLock();
        }

        if (PathGuideFactory.IsPooling)
            PathGuideFactory.FlushCache(true);
    }

    #endregion

    #region Navigation Map Management

    /// <summary>
    /// Attempts to register a new navigation chart with the manager.
    /// </summary>
    /// <param name="chart">The map to register.</param>
    /// <param name="initializeChart">Whether to initialize the chart after registration succeeds.</param>
    /// <returns>True if successful, false if a duplicate name exists.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="chart"/> is null.</exception>
    public static bool Register(NavigationChart chart, bool initializeChart = true)
    {
        SwiftThrowHelper.ThrowIfNull(chart, nameof(chart));

        return RegisterChartInternal(
            chart,
            generatedTransitionIdPrefix: chart.Name,
            precomputedGeneratedTransitions: null,
            initializeChart);
    }

    /// <summary>
    /// Attempts to register the chart and generated transitions produced by a traversal authoring build.
    /// </summary>
    /// <param name="buildResult">The build result to register.</param>
    /// <param name="initializeChart">Whether to initialize the built chart after registration succeeds.</param>
    /// <returns>True when the chart and all generated transitions are registered successfully; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="buildResult"/> is null.</exception>
    public static bool Register(TraversalBuildResult buildResult, bool initializeChart = true)
    {
        SwiftThrowHelper.ThrowIfNull(buildResult, nameof(buildResult));

        return RegisterChartInternal(
            buildResult.Chart,
            buildResult.GeneratedTransitionIdPrefix,
            buildResult.GeneratedTransitions,
            initializeChart);
    }

    private static bool RegisterChartInternal(
        NavigationChart chart,
        string generatedTransitionIdPrefix,
        TraversalTransition[] precomputedGeneratedTransitions,
        bool initializeChart)
    {
        _navigationChartMapLock.EnterWriteLock();
        try
        {
            if (_navigationChartMap.ContainsKey(chart.Name))
                return false;

            chart.RegistrationOrder = unchecked(++_nextChartRegistrationOrder);
            _navigationChartMap.Add(chart.Name, chart);
        }
        finally { _navigationChartMapLock.ExitWriteLock(); }

        if (!TryRegisterManagedGeneratedTransitions(
            chart,
            generatedTransitionIdPrefix,
            precomputedGeneratedTransitions))
        {
            RemoveChartFromRegistry(chart.Name);
            chart.IsInitialized = false;
            return false;
        }

        if (initializeChart)
            InitializeChart(chart.Name);

        return true;
    }

    /// <summary>
    /// Checks if a navigation map is already registered under the specified name.
    /// </summary>
    /// <param name="name">The map name to check.</param>
    /// <returns>True if registered; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsChartRegistered(string name)
    {
        _navigationChartMapLock.EnterReadLock();
        try { return _navigationChartMap.ContainsKey(name); }
        finally { _navigationChartMapLock.ExitReadLock(); }
    }

    /// <summary>
    /// Attempts to retrieve a registered navigation chart by name.
    /// </summary>
    /// <param name="name">The name of the map.</param>
    /// <param name="chart">The retrieved navigation chart.</param>
    /// <returns>True if the map exists; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetNavigationChart(string name, out NavigationChart chart)
    {
        _navigationChartMapLock.EnterReadLock();
        try { return _navigationChartMap.TryGetValue(name, out chart); }
        finally { _navigationChartMapLock.ExitReadLock(); }
    }

    /// <summary>
    /// Attempts to retrieve the winning effective authored cell at the provided voxel.
    /// </summary>
    /// <param name="voxelIndex">The voxel to inspect.</param>
    /// <param name="cell">The effective authored cell currently winning overlap resolution.</param>
    /// <returns>True when the voxel currently has an effective authored chart cell; otherwise, false.</returns>
    public static bool TryGetEffectiveCell(GlobalVoxelIndex voxelIndex, out NavigationChartCell cell)
    {
        if (TryGetResolvedChartVoxelState(voxelIndex, out ResolvedChartVoxelState state))
        {
            cell = state.EffectiveCell;
            return true;
        }

        cell = NavigationChartCell.Empty;
        return false;
    }

    /// <summary>
    /// Attempts to retrieve the winning effective authored cell at the provided world position.
    /// </summary>
    /// <param name="worldPosition">The world position to inspect.</param>
    /// <param name="cell">The effective authored cell currently winning overlap resolution.</param>
    /// <returns>True when the position resolves to a voxel with an effective authored chart cell; otherwise, false.</returns>
    public static bool TryGetEffectiveCell(Vector3d worldPosition, out NavigationChartCell cell)
    {
        if (TryGetResolvedChartVoxelState(worldPosition, out _, out ResolvedChartVoxelState state))
        {
            cell = state.EffectiveCell;
            return true;
        }

        cell = NavigationChartCell.Empty;
        return false;
    }

    /// <summary>
    /// Attempts to retrieve the chart currently winning overlap resolution at the provided voxel.
    /// </summary>
    /// <param name="voxelIndex">The voxel to inspect.</param>
    /// <param name="chartName">The effective chart owner.</param>
    /// <returns>True when the voxel currently has an effective chart owner; otherwise, false.</returns>
    public static bool TryGetEffectiveChartOwner(GlobalVoxelIndex voxelIndex, out string chartName)
    {
        if (TryGetResolvedChartVoxelState(voxelIndex, out ResolvedChartVoxelState state))
        {
            chartName = state.EffectiveChartOwner;
            return true;
        }

        chartName = null;
        return false;
    }

    /// <summary>
    /// Attempts to retrieve the chart currently winning overlap resolution at the provided world position.
    /// </summary>
    /// <param name="worldPosition">The world position to inspect.</param>
    /// <param name="chartName">The effective chart owner.</param>
    /// <returns>True when the position resolves to a voxel with an effective chart owner; otherwise, false.</returns>
    public static bool TryGetEffectiveChartOwner(Vector3d worldPosition, out string chartName)
    {
        if (TryGetResolvedChartVoxelState(worldPosition, out _, out ResolvedChartVoxelState state))
        {
            chartName = state.EffectiveChartOwner;
            return true;
        }

        chartName = null;
        return false;
    }

    /// <summary>
    /// Attempts to retrieve the closest currently active directed transition of the requested type.
    /// </summary>
    /// <param name="worldPosition">The position to measure from.</param>
    /// <param name="transitionType">The directed handoff family to search.</param>
    /// <param name="transition">
    /// The closest active directed transition. Bidirectional registrations may return the reversed
    /// directed view when that source anchor is closer.
    /// </param>
    /// <returns>True when at least one active directed transition of that type exists; otherwise, false.</returns>
    public static bool TryGetClosestActiveTransition(
        Vector3d worldPosition,
        TraversalTransitionType transitionType,
        out TraversalTransition transition)
    {
        int[] sourceGridIndices = TraversalTransitionQuery.GetSourceGridIndices(transitionType);
        if (sourceGridIndices.Length == 0)
        {
            transition = default;
            return false;
        }

        bool found = false;
        transition = default;
        Fixed64 closestDistanceSq = Fixed64.Zero;
        int originGridIndex = -1;

        if (GlobalGridManager.TryGetGrid(worldPosition, out VoxelGrid originGrid))
        {
            originGridIndex = originGrid.GlobalIndex;
            EvaluateClosestTransitionCandidates(
                TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(originGridIndex, transitionType),
                worldPosition,
                ref found,
                ref transition,
                ref closestDistanceSq);

            if (found && closestDistanceSq == Fixed64.Zero)
                return true;
        }

        for (int i = 0; i < sourceGridIndices.Length; i++)
        {
            int sourceGridIndex = sourceGridIndices[i];
            if (sourceGridIndex == originGridIndex
                || !GlobalGridManager.TryGetGrid(sourceGridIndex, out VoxelGrid sourceGrid)
                || (found && GetBoundsDistanceSq(worldPosition, sourceGrid.BoundsMin, sourceGrid.BoundsMax) >= closestDistanceSq))
            {
                continue;
            }

            EvaluateClosestTransitionCandidates(
                TraversalTransitionQuery.GetDirectedTransitionsFromSourceGrid(sourceGridIndex, transitionType),
                worldPosition,
                ref found,
                ref transition,
                ref closestDistanceSq);

            if (found && closestDistanceSq == Fixed64.Zero)
                break;
        }

        return found;
    }

    /// <summary>
    /// Initializes all registered navigation charts by materializing their authored surface and volume partitions.
    /// </summary>
    public static void InitializeAllCharts()
    {
        foreach (NavigationChart chart in AllCharts)
            InitializeChart(chart.Name);
    }

    private static void EvaluateClosestTransitionCandidates(
        TraversalTransition[] candidates,
        Vector3d worldPosition,
        ref bool found,
        ref TraversalTransition closestTransition,
        ref Fixed64 closestDistanceSq)
    {
        for (int i = 0; i < candidates.Length; i++)
        {
            Fixed64 candidateDistanceSq = (candidates[i].Source.Position - worldPosition).SqrMagnitude;
            if (!found || candidateDistanceSq < closestDistanceSq)
            {
                found = true;
                closestDistanceSq = candidateDistanceSq;
                closestTransition = candidates[i];
            }
        }
    }

    private static Fixed64 GetBoundsDistanceSq(
        Vector3d worldPosition,
        Vector3d boundsMin,
        Vector3d boundsMax)
    {
        Fixed64 xDistance = GetAxisDistanceToBounds(worldPosition.x, boundsMin.x, boundsMax.x);
        Fixed64 yDistance = GetAxisDistanceToBounds(worldPosition.y, boundsMin.y, boundsMax.y);
        Fixed64 zDistance = GetAxisDistanceToBounds(worldPosition.z, boundsMin.z, boundsMax.z);
        return xDistance * xDistance + yDistance * yDistance + zDistance * zDistance;
    }

    private static Fixed64 GetAxisDistanceToBounds(Fixed64 value, Fixed64 boundsMin, Fixed64 boundsMax)
    {
        if (value < boundsMin)
            return boundsMin - value;

        if (value > boundsMax)
            return value - boundsMax;

        return Fixed64.Zero;
    }

    /// <summary>
    /// Applies one authored cell mutation to a registered chart using chart-local indices.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the target cell was in bounds and the authored payload changed; otherwise, <c>false</c>.
    /// </returns>
    public static bool TryUpdateChartCell(string chartName, int x, int y, int z, NavigationChartCell cell)
    {
        if (!TryGetNavigationChart(chartName, out NavigationChart chart))
            return false;

        return TryUpdateChartCell(chart, x, y, z, cell);
    }

    private static bool TryUpdateChartCell(
        NavigationChart chart,
        int x,
        int y,
        int z,
        NavigationChartCell cell)
    {
        SwiftHashSet<SolidChartPartition> partitionsToRebind = PartitionSetPool.Rent();
        SwiftHashSet<string> invalidatedChartKeys = SwiftHashSetPool<string>.Shared.Rent();
        SwiftHashSet<string> managedChartsToRefresh = SwiftHashSetPool<string>.Shared.Rent();
        try
        {
            bool changed = TryApplyChartCellUpdate(
                chart,
                x,
                y,
                z,
                cell,
                partitionsToRebind,
                invalidatedChartKeys,
                managedChartsToRefresh);

            if (changed)
                RefreshManagedTransitionsForVoxel(
                    chart.GetWorldPosition(x, y, z),
                    managedChartsToRefresh);

            RebindAndInvalidate(partitionsToRebind, invalidatedChartKeys);
            return changed;
        }
        finally
        {
            PartitionSetPool.Release(partitionsToRebind);
            SwiftHashSetPool<string>.Shared.Release(invalidatedChartKeys);
            SwiftHashSetPool<string>.Shared.Release(managedChartsToRefresh);
        }
    }

    /// <summary>
    /// Applies one authored cell mutation to a registered chart using a world-space position.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the position resolves inside the chart and the authored payload changed; otherwise, <c>false</c>.
    /// </returns>
    public static bool TryUpdateChartCell(string chartName, Vector3d worldPosition, NavigationChartCell cell)
    {
        if (!TryGetNavigationChart(chartName, out NavigationChart chart)
            || !chart.TryWorldToIndex(worldPosition, out int x, out int y, out int z))
        {
            return false;
        }

        return TryUpdateChartCell(chart, x, y, z, cell);
    }

    /// <summary>
    /// Applies a sparse batch of authored cell mutations to a registered chart.
    /// </summary>
    /// <param name="chartName">The registered chart to mutate.</param>
    /// <param name="updates">The sparse set of cell changes to apply in order.</param>
    /// <returns>The number of authored cell mutations that changed the chart payload.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="updates"/> is null.</exception>
    public static int ApplyChartUpdates(string chartName, IReadOnlyList<NavigationChartCellUpdate> updates)
    {
        SwiftThrowHelper.ThrowIfNull(updates, nameof(updates));

        if (updates.Count == 0 || !TryGetNavigationChart(chartName, out NavigationChart chart))
            return 0;

        SwiftHashSet<SolidChartPartition> partitionsToRebind = PartitionSetPool.Rent();
        SwiftHashSet<string> invalidatedChartKeys = SwiftHashSetPool<string>.Shared.Rent();
        SwiftHashSet<string> managedChartsToRefresh = SwiftHashSetPool<string>.Shared.Rent();
        try
        {
            int changedCount = 0;
            for (int i = 0; i < updates.Count; i++)
            {
                managedChartsToRefresh.Clear();
                NavigationChartCellUpdate update = updates[i];
                if (TryApplyChartCellUpdate(
                    chart,
                    update.X,
                    update.Y,
                    update.Z,
                    update.Cell,
                    partitionsToRebind,
                    invalidatedChartKeys,
                    managedChartsToRefresh))
                {
                    changedCount++;
                    RefreshManagedTransitionsForVoxel(
                        chart.GetWorldPosition(update.X, update.Y, update.Z),
                        managedChartsToRefresh);
                }
            }

            RebindAndInvalidate(partitionsToRebind, invalidatedChartKeys);
            return changedCount;
        }
        finally
        {
            PartitionSetPool.Release(partitionsToRebind);
            SwiftHashSetPool<string>.Shared.Release(invalidatedChartKeys);
            SwiftHashSetPool<string>.Shared.Release(managedChartsToRefresh);
        }
    }

    /// <summary>
    /// Initializes a specific navigation chart by materializing its authored surface and volume partitions.
    /// </summary>
    /// <param name="chartKey">The name of the map to initialize.</param>
    public static void InitializeChart(string chartKey)
    {
        if (string.IsNullOrEmpty(chartKey)
            || !TryGetNavigationChart(chartKey, out var chart)
            || chart.IsInitialized)
        {
            return;
        }

        SwiftHashSet<SolidChartPartition> partitionsToRebind = PartitionSetPool.Rent();
        SwiftHashSet<string> affectedChartKeys = SwiftHashSetPool<string>.Shared.Rent();
        SwiftHashSet<GlobalVoxelIndex> touchedVoxelIndices = SwiftHashSetPool<GlobalVoxelIndex>.Shared.Rent();
        try
        {
            foreach ((Vector3d pos, NavigationChartCell cell) in chart.GetAuthoredCells())
            {
                if (!GlobalGridManager.TryGetVoxel(pos, out Voxel voxel))
                    continue;

                touchedVoxelIndices.Add(voxel.GlobalIndex);

                if (!_resolvedChartVoxelStates.TryGetValue(voxel.GlobalIndex, out ResolvedChartVoxelState state))
                {
                    state = new ResolvedChartVoxelState();
                    _resolvedChartVoxelStates[voxel.GlobalIndex] = state;
                }
                else if (state.HasAnyOwners)
                    state.AddChartOwnersTo(affectedChartKeys);

                NavigationChartCell previousEffectiveCell = state.EffectiveCell;
                state.AddOwner(chart.Name, cell, chart.Priority, chart.RegistrationOrder);
                ApplyResolvedVoxelState(voxel, state, previousEffectiveCell, partitionsToRebind);
            }

            foreach (SolidChartPartition part in partitionsToRebind)
                part.BindNeighbors();

            chart.IsInitialized = true;
            affectedChartKeys.Add(chart.Name);

            RefreshManagedManualTransitionsForVoxels(touchedVoxelIndices);
            RefreshManagedGeneratedTransitionsForCharts(affectedChartKeys);

            foreach (string affectedChartKey in affectedChartKeys)
                PathGuideFactory.InvalidateCacheFor(affectedChartKey);
        }
        finally
        {
            PartitionSetPool.Release(partitionsToRebind);
            SwiftHashSetPool<string>.Shared.Release(affectedChartKeys);
            SwiftHashSetPool<GlobalVoxelIndex>.Shared.Release(touchedVoxelIndices);
        }
    }

    public static void UnloadChart(string chartKey)
    {
        if (!TryGetNavigationChart(chartKey, out NavigationChart chart))
            return;

        UnloadChart(chart);
    }

    /// <summary>
    /// Unloads a navigation map by name and releases associated partitions.
    /// </summary>
    /// <param name="chart">The navigation chart to unload.</param>
    public static void UnloadChart(NavigationChart chart)
    {
        if (chart == null)
            return;

        string[] generatedTransitionIds = RemoveManagedGeneratedTransitions(chart.Name);

        if (!chart.IsInitialized)
        {
            RemoveChartFromRegistry(chart.Name);
            TraversalTransitionRegistry.UnregisterRange(generatedTransitionIds);
            chart.IsInitialized = false;
            return;
        }

        // invalidate any survey results currently using this chart
        PathGuideFactory.InvalidateCacheFor(chart.Name);

        SwiftHashSet<SolidChartPartition> partitionsToRebind = PartitionSetPool.Rent();
        SwiftHashSet<string> affectedChartKeys = SwiftHashSetPool<string>.Shared.Rent();
        SwiftHashSet<GlobalVoxelIndex> touchedVoxelIndices = SwiftHashSetPool<GlobalVoxelIndex>.Shared.Rent();
        try
        {
            affectedChartKeys.Add(chart.Name);
            foreach ((Vector3d position, _) in chart.GetAuthoredCells())
            {
                if (!GlobalGridManager.TryGetVoxel(position, out Voxel voxel))
                    continue;

                touchedVoxelIndices.Add(voxel.GlobalIndex);

                if (!_resolvedChartVoxelStates.TryGetValue(voxel.GlobalIndex, out ResolvedChartVoxelState state)
                    || !state.ContainsOwner(chart.Name))
                {
                    continue;
                }

                state.AddChartOwnersTo(affectedChartKeys);

                NavigationChartCell previousEffectiveCell = state.EffectiveCell;
                state.RemoveOwner(chart.Name);
                ApplyResolvedVoxelState(voxel, state, previousEffectiveCell, partitionsToRebind);

                if (!state.HasAnyOwners)
                    _resolvedChartVoxelStates.Remove(voxel.GlobalIndex);
            }

            foreach (SolidChartPartition part in partitionsToRebind)
                part.BindNeighbors();

            TraversalTransitionRegistry.UnregisterRange(generatedTransitionIds);
            RemoveChartFromRegistry(chart.Name);
            chart.IsInitialized = false;

            RefreshManagedManualTransitionsForVoxels(touchedVoxelIndices);
            RefreshManagedGeneratedTransitionsForCharts(affectedChartKeys, chart.Name);

            foreach (string affectedChartKey in affectedChartKeys)
            {
                if (affectedChartKey == chart.Name)
                    continue;

                PathGuideFactory.InvalidateCacheFor(affectedChartKey);
            }
        }
        finally
        {
            PartitionSetPool.Release(partitionsToRebind);
            SwiftHashSetPool<string>.Shared.Release(affectedChartKeys);
            SwiftHashSetPool<GlobalVoxelIndex>.Shared.Release(touchedVoxelIndices);
        }
    }

    #endregion

    #region Pathfinding Utilities

    private static void RebuildInitializedChartsAgainstCurrentGrids(
        Vector3d boundsMin,
        Vector3d boundsMax)
    {
        NavigationChart[] initializedCharts = GetInitializedChartsIntersectingBoundsSnapshot(boundsMin, boundsMax);
        if (initializedCharts.Length == 0)
            return;

        RebuildInitializedChartsAgainstCurrentGrids(initializedCharts);
    }

    private static void RebuildInitializedChartsAgainstCurrentGrids(NavigationChart[] chartsToRebuild)
    {
        SuppressManagedGeneratedTransitionsForCharts(chartsToRebuild);

        for (int i = 0; i < chartsToRebuild.Length; i++)
            ClearInitializedChartLiveStatePreservingRegistration(chartsToRebuild[i]);

        for (int i = 0; i < chartsToRebuild.Length; i++)
            InitializeChart(chartsToRebuild[i].Name);

        RefreshManagedGeneratedTransitionsForCharts(GetInitializedChartsSnapshot());
        TraversalTransitionRegistry.RefreshManagedManualTransitions();
    }

    private static void SuppressManagedGeneratedTransitionsForCharts(NavigationChart[] charts)
    {
        SwiftList<string> transitionIds = new();
        _navigationChartMapLock.EnterReadLock();
        try
        {
            for (int i = 0; i < charts.Length; i++)
            {
                NavigationChart chart = charts[i];
                if (!_managedGeneratedTransitionsByChart.TryGetValue(chart.Name, out ManagedChartTransitionState state)
                    || state.TransitionIds.Count == 0)
                {
                    continue;
                }

                foreach (string transitionId in state.TransitionIds)
                    transitionIds.Add(transitionId);
            }
        }
        finally
        {
            _navigationChartMapLock.ExitReadLock();
        }

        if (transitionIds.Count == 0)
            return;

        TraversalTransitionRegistry.SetManagedTransitionsSuppressed(
            transitionIds.ToArray(),
            suppressed: true);
    }

    private static NavigationChart[] GetInitializedChartsSnapshot()
    {
        _navigationChartMapLock.EnterReadLock();
        try
        {
            if (_navigationChartMap.Count == 0)
                return Array.Empty<NavigationChart>();

            SwiftList<NavigationChart> initializedCharts = new();
            foreach (NavigationChart chart in _navigationChartMap.Values)
            {
                if (chart.IsInitialized)
                    initializedCharts.Add(chart);
            }

            NavigationChart[] snapshot = initializedCharts.ToArray();
            Array.Sort(snapshot, CompareChartsByRegistrationOrder);
            return snapshot;
        }
        finally
        {
            _navigationChartMapLock.ExitReadLock();
        }
    }

    private static NavigationChart[] GetInitializedChartsIntersectingBoundsSnapshot(
        Vector3d boundsMin,
        Vector3d boundsMax)
    {
        _navigationChartMapLock.EnterReadLock();
        try
        {
            if (_navigationChartMap.Count == 0)
                return Array.Empty<NavigationChart>();

            SwiftList<NavigationChart> initializedCharts = new();
            foreach (NavigationChart chart in _navigationChartMap.Values)
            {
                if (!chart.IsInitialized
                    || !DoBoundsOverlap(chart.MinBounds, chart.MaxBounds, boundsMin, boundsMax))
                {
                    continue;
                }

                initializedCharts.Add(chart);
            }

            NavigationChart[] snapshot = initializedCharts.ToArray();
            Array.Sort(snapshot, CompareChartsByRegistrationOrder);
            return snapshot;
        }
        finally
        {
            _navigationChartMapLock.ExitReadLock();
        }
    }

    private static void ClearInitializedChartLiveStatePreservingRegistration(NavigationChart chart)
    {
        PathGuideFactory.InvalidateCacheFor(chart.Name);

        SwiftHashSet<SolidChartPartition> partitionsToRebind = PartitionSetPool.Rent();
        SwiftList<GlobalVoxelIndex> resolvedVoxelIndicesToRemove = new();
        try
        {
            foreach (KeyValuePair<GlobalVoxelIndex, ResolvedChartVoxelState> pair in _resolvedChartVoxelStates)
            {
                ResolvedChartVoxelState state = pair.Value;
                if (!state.ContainsOwner(chart.Name))
                    continue;

                NavigationChartCell previousEffectiveCell = state.EffectiveCell;
                state.RemoveOwner(chart.Name);

                bool hasLiveVoxel = GlobalGridManager.TryGetGridAndVoxel(pair.Key, out _, out Voxel voxel);
                if (hasLiveVoxel)
                    ApplyResolvedVoxelState(voxel, state, previousEffectiveCell, partitionsToRebind);

                if (!state.HasAnyOwners
                    || !hasLiveVoxel)
                {
                    resolvedVoxelIndicesToRemove.Add(pair.Key);
                }
            }

            for (int i = 0; i < resolvedVoxelIndicesToRemove.Count; i++)
                _resolvedChartVoxelStates.Remove(resolvedVoxelIndicesToRemove[i]);

            foreach (SolidChartPartition part in partitionsToRebind)
                part.BindNeighbors();

            chart.IsInitialized = false;
        }
        finally
        {
            PartitionSetPool.Release(partitionsToRebind);
        }
    }

    private static void ClearLiveGridState()
    {
        foreach (KeyValuePair<GlobalVoxelIndex, ResolvedChartVoxelState> pair in _resolvedChartVoxelStates)
        {
            if (!GlobalGridManager.TryGetGridAndVoxel(pair.Key, out _, out Voxel voxel))
                continue;

            RemoveLivePathingPartitions(voxel);
        }

        _resolvedChartVoxelStates.Clear();
        _activeAuthoredGasCellCount = 0;
        _activeAuthoredLiquidCellCount = 0;
    }

    private static bool DoBoundsOverlap(
        Vector3d firstMin,
        Vector3d firstMax,
        Vector3d secondMin,
        Vector3d secondMax)
    {
        return firstMin.x <= secondMax.x
            && firstMax.x >= secondMin.x
            && firstMin.y <= secondMax.y
            && firstMax.y >= secondMin.y
            && firstMin.z <= secondMax.z
            && firstMax.z >= secondMin.z;
    }

    private static void RemoveLivePathingPartitions(Voxel voxel)
    {
        if (voxel.TryGetPartition(out SolidChartPartition _))
            voxel.TryRemovePartition<SolidChartPartition>();

        if (voxel.TryGetPartition(out VolumeChartPartition _))
            voxel.TryRemovePartition<VolumeChartPartition>();
    }

    private static void MarkRegisteredChartsUninitialized_NoLock()
    {
        foreach (NavigationChart chart in _navigationChartMap.Values)
            chart.IsInitialized = false;
    }

    private static int CompareChartsByRegistrationOrder(NavigationChart left, NavigationChart right)
    {
        return left.RegistrationOrder.CompareTo(right.RegistrationOrder);
    }

    private static readonly (int Dx, int Dy, int Dz)[] ManagedGeneratedNeighborOffsets =
    {
        (1, 0, 0),
        (-1, 0, 0),
        (0, 1, 0),
        (0, -1, 0),
        (0, 0, 1),
        (0, 0, -1)
    };

    private static readonly (int Dx, int Dy, int Dz)[] PositiveManagedGeneratedNeighborOffsets =
    {
        (1, 0, 0),
        (0, 1, 0),
        (0, 0, 1)
    };

    private static bool TryRegisterManagedGeneratedTransitions(
        NavigationChart chart,
        string transitionIdPrefix,
        TraversalTransition[] precomputedGeneratedTransitions)
    {
        TraversalTransition[] generatedTransitions = precomputedGeneratedTransitions
            ?? GeneratedTraversalTransitionBuilder.BuildTransitions(chart, transitionIdPrefix);
        int transitionCount = generatedTransitions.Length;
        if (transitionCount > 0
            && !TraversalTransitionRegistry.RegisterGeneratedRange(
                generatedTransitions,
                chart.Priority,
                startSuppressed: true))
        {
            return false;
        }

        string[] registeredTransitionIds = transitionCount == 0
            ? Array.Empty<string>()
            : new string[transitionCount];
        for (int i = 0; i < transitionCount; i++)
            registeredTransitionIds[i] = generatedTransitions[i].Id;

        RememberManagedGeneratedTransitions(
            chart.Name,
            transitionIdPrefix,
            chart.Priority,
            registeredTransitionIds,
            transitionCount);
        return true;
    }

    private static void RememberManagedGeneratedTransitions(
        string chartName,
        string transitionIdPrefix,
        int priority,
        string[] transitionIds,
        int transitionCount)
    {
        _navigationChartMapLock.EnterWriteLock();
        try
        {
            var state = new ManagedChartTransitionState(transitionIdPrefix, priority);
            for (int i = 0; i < transitionCount; i++)
                state.TransitionIds.Add(transitionIds[i]);

            _managedGeneratedTransitionsByChart[chartName] = state;
        }
        finally { _navigationChartMapLock.ExitWriteLock(); }
    }

    private static string[] RemoveManagedGeneratedTransitions(string chartName)
    {
        _navigationChartMapLock.EnterWriteLock();
        try
        {
            if (!_managedGeneratedTransitionsByChart.TryGetValue(chartName, out ManagedChartTransitionState state))
                return Array.Empty<string>();

            _managedGeneratedTransitionsByChart.Remove(chartName);
            return CopyTransitionIds(state.TransitionIds);
        }
        finally { _navigationChartMapLock.ExitWriteLock(); }
    }

    private static bool TryGetManagedGeneratedTransitionState(
        string chartName,
        out ManagedChartTransitionState state)
    {
        _navigationChartMapLock.EnterReadLock();
        try { return _managedGeneratedTransitionsByChart.TryGetValue(chartName, out state); }
        finally { _navigationChartMapLock.ExitReadLock(); }
    }

    private static void RefreshManagedGeneratedTransitionsForCharts(
        SwiftHashSet<string> chartNames,
        string excludedChartName = null)
    {
        if (chartNames == null || chartNames.Count == 0)
            return;

        foreach (string chartName in chartNames)
        {
            if (string.IsNullOrEmpty(chartName)
                || string.Equals(chartName, excludedChartName, StringComparison.Ordinal))
            {
                continue;
            }

            RefreshManagedGeneratedTransitionsForChart(chartName);
        }
    }

    private static void RefreshManagedGeneratedTransitionsForCharts(NavigationChart[] charts)
    {
        SwiftHashSet<string> chartNames = SwiftHashSetPool<string>.Shared.Rent();
        try
        {
            for (int i = 0; i < charts.Length; i++)
                chartNames.Add(charts[i].Name);

            RefreshManagedGeneratedTransitionsForCharts(chartNames);
        }
        finally
        {
            SwiftHashSetPool<string>.Shared.Release(chartNames);
        }
    }

    private static void RefreshManagedTransitionsForVoxel(
        Vector3d worldPosition,
        SwiftHashSet<string> chartNames)
    {
        if (GlobalGridManager.TryGetVoxel(worldPosition, out Voxel voxel))
            TraversalTransitionRegistry.RefreshManagedManualTransitionsForVoxel(voxel.GlobalIndex);

        RefreshManagedGeneratedTransitionsForVoxel(worldPosition, chartNames);
    }

    private static void RefreshManagedManualTransitionsForVoxels(SwiftHashSet<GlobalVoxelIndex> voxelIndices)
    {
        if (voxelIndices == null || voxelIndices.Count == 0)
            return;

        foreach (GlobalVoxelIndex voxelIndex in voxelIndices)
            TraversalTransitionRegistry.RefreshManagedManualTransitionsForVoxel(voxelIndex);
    }

    private static void RefreshManagedGeneratedTransitionsForChart(string chartName)
    {
        if (!TryGetNavigationChart(chartName, out NavigationChart chart)
            || !TryGetManagedGeneratedTransitionState(chartName, out ManagedChartTransitionState state))
        {
            return;
        }

        SwiftHashSet<string> desiredTransitionIds = SwiftHashSetPool<string>.Shared.Rent();
        SwiftHashSet<string> activeTransitionIds = SwiftHashSetPool<string>.Shared.Rent();
        try
        {
            TraversalTransition[] missingTransitions = CollectManagedGeneratedTransitionsForChart(
                chart,
                state,
                desiredTransitionIds,
                activeTransitionIds);

            ApplyManagedGeneratedTransitionDelta(
                chartName,
                state,
                desiredTransitionIds,
                activeTransitionIds,
                missingTransitions);
        }
        finally
        {
            SwiftHashSetPool<string>.Shared.Release(desiredTransitionIds);
            SwiftHashSetPool<string>.Shared.Release(activeTransitionIds);
        }
    }

    private static TraversalTransition[] CollectManagedGeneratedTransitionsForChart(
        NavigationChart chart,
        ManagedChartTransitionState state,
        SwiftHashSet<string> desiredTransitionIds,
        SwiftHashSet<string> activeTransitionIds)
    {
        SwiftList<TraversalTransition> missingTransitions = new();
        int[] generatedIndices = chart.GetGeneratedTransitionIndices();
        for (int i = 0; i < generatedIndices.Length; i++)
        {
            chart.DecodeIndex(generatedIndices[i], out int x, out int y, out int z);
            for (int neighborOffsetIndex = 0; neighborOffsetIndex < PositiveManagedGeneratedNeighborOffsets.Length; neighborOffsetIndex++)
            {
                (int dx, int dy, int dz) = PositiveManagedGeneratedNeighborOffsets[neighborOffsetIndex];
                int neighborX = x + dx;
                int neighborY = y + dy;
                int neighborZ = z + dz;
                if (!chart.IsInBounds(neighborX, neighborY, neighborZ))
                    continue;

                CollectManagedGeneratedTransitionsForPair(
                    chart,
                    state,
                    x,
                    y,
                    z,
                    neighborX,
                    neighborY,
                    neighborZ,
                    desiredTransitionIds,
                    activeTransitionIds,
                    missingTransitions);
            }
        }

        return missingTransitions.Count == 0
            ? Array.Empty<TraversalTransition>()
            : missingTransitions.ToArray();
    }

    private static void RefreshManagedGeneratedTransitionsForVoxel(
        Vector3d worldPosition,
        SwiftHashSet<string> chartNames)
    {
        if (chartNames == null || chartNames.Count == 0)
            return;

        foreach (string chartName in chartNames)
        {
            if (string.IsNullOrEmpty(chartName)
                || !TryGetNavigationChart(chartName, out NavigationChart chart)
                || !TryGetManagedGeneratedTransitionState(chartName, out ManagedChartTransitionState state)
                || !chart.TryWorldToIndex(worldPosition, out int x, out int y, out int z))
            {
                continue;
            }

            RefreshManagedGeneratedTransitionsForVoxel(chartName, chart, state, x, y, z);
        }
    }

    private static void RefreshManagedGeneratedTransitionsForVoxel(
        string chartName,
        NavigationChart chart,
        ManagedChartTransitionState state,
        int x,
        int y,
        int z)
    {
        for (int i = 0; i < ManagedGeneratedNeighborOffsets.Length; i++)
        {
            (int dx, int dy, int dz) = ManagedGeneratedNeighborOffsets[i];
            int neighborX = x + dx;
            int neighborY = y + dy;
            int neighborZ = z + dz;
            if (!chart.IsInBounds(neighborX, neighborY, neighborZ))
                continue;

            if (neighborX < x
                || (neighborX == x && neighborY < y)
                || (neighborX == x && neighborY == y && neighborZ < z))
            {
                RefreshManagedGeneratedTransitionsForPair(
                    chartName,
                    chart,
                    state,
                    neighborX,
                    neighborY,
                    neighborZ,
                    x,
                    y,
                    z);
            }
            else
            {
                RefreshManagedGeneratedTransitionsForPair(
                    chartName,
                    chart,
                    state,
                    x,
                    y,
                    z,
                    neighborX,
                    neighborY,
                    neighborZ);
            }
        }
    }

    private static void RefreshManagedGeneratedTransitionsForPair(
        string chartName,
        NavigationChart chart,
        ManagedChartTransitionState state,
        int firstX,
        int firstY,
        int firstZ,
        int secondX,
        int secondY,
        int secondZ)
    {
        TraversalTransition[] desiredTransitions = GeneratedTraversalTransitionBuilder.BuildTransitionsForPair(
            chart,
            state.TransitionIdPrefix,
            firstX,
            firstY,
            firstZ,
            secondX,
            secondY,
            secondZ);

        string[] potentialTransitionIds = GeneratedTraversalTransitionBuilder.GetPotentialTransitionIdsForPair(
            state.TransitionIdPrefix,
            firstX,
            firstY,
            firstZ,
            secondX,
            secondY,
            secondZ);

        string[] obsoleteTransitionIds = GetObsoleteManagedGeneratedTransitionIds(
            state,
            potentialTransitionIds,
            desiredTransitions);
        if (obsoleteTransitionIds.Length > 0)
        {
            TraversalTransitionRegistry.UnregisterRange(obsoleteTransitionIds);
            RemoveManagedGeneratedTransitionIds(chartName, obsoleteTransitionIds);
        }

        TraversalTransition[] missingTransitions = GetMissingManagedGeneratedTransitions(state, desiredTransitions);
        if (missingTransitions.Length > 0
            && TraversalTransitionRegistry.RegisterGeneratedRange(
                missingTransitions,
                state.Priority,
                startSuppressed: true))
        {
            AddManagedGeneratedTransitionIds(chartName, missingTransitions);
        }

        if (desiredTransitions.Length == 0)
            return;

        string[] desiredTransitionIds = CopyTransitionIds(desiredTransitions);
        bool shouldBeActive = IsManagedGeneratedPairActive(
            chartName,
            chart,
            firstX,
            firstY,
            firstZ,
            secondX,
            secondY,
            secondZ);
        TraversalTransitionRegistry.SetManagedTransitionsSuppressed(
            desiredTransitionIds,
            suppressed: !shouldBeActive);
    }

    private static string[] GetObsoleteManagedGeneratedTransitionIds(
        ManagedChartTransitionState state,
        string[] potentialTransitionIds,
        TraversalTransition[] desiredTransitions)
    {
        if (potentialTransitionIds == null || potentialTransitionIds.Length == 0)
            return Array.Empty<string>();

        SwiftHashSet<string> desiredTransitionIds = SwiftHashSetPool<string>.Shared.Rent();
        try
        {
            for (int i = 0; i < desiredTransitions.Length; i++)
                desiredTransitionIds.Add(desiredTransitions[i].Id);

            SwiftList<string> obsoleteTransitionIds = new();
            for (int i = 0; i < potentialTransitionIds.Length; i++)
            {
                string transitionId = potentialTransitionIds[i];
                if (state.TransitionIds.Contains(transitionId)
                    && !desiredTransitionIds.Contains(transitionId))
                {
                    obsoleteTransitionIds.Add(transitionId);
                }
            }

            return obsoleteTransitionIds.Count == 0
                ? Array.Empty<string>()
                : obsoleteTransitionIds.ToArray();
        }
        finally
        {
            SwiftHashSetPool<string>.Shared.Release(desiredTransitionIds);
        }
    }

    private static TraversalTransition[] GetMissingManagedGeneratedTransitions(
        ManagedChartTransitionState state,
        TraversalTransition[] desiredTransitions)
    {
        if (desiredTransitions == null || desiredTransitions.Length == 0)
            return Array.Empty<TraversalTransition>();

        SwiftList<TraversalTransition> missingTransitions = new();
        for (int i = 0; i < desiredTransitions.Length; i++)
        {
            TraversalTransition transition = desiredTransitions[i];
            if (!state.TransitionIds.Contains(transition.Id))
                missingTransitions.Add(transition);
        }

        return missingTransitions.Count == 0
            ? Array.Empty<TraversalTransition>()
            : missingTransitions.ToArray();
    }

    private static void ApplyManagedGeneratedTransitionDelta(
        string chartName,
        ManagedChartTransitionState state,
        SwiftHashSet<string> desiredTransitionIds,
        SwiftHashSet<string> activeTransitionIds,
        TraversalTransition[] missingTransitions)
    {
        if (missingTransitions != null
            && missingTransitions.Length > 0
            && TraversalTransitionRegistry.RegisterGeneratedRange(
                missingTransitions,
                state.Priority,
                startSuppressed: true))
        {
            AddManagedGeneratedTransitionIds(chartName, missingTransitions);
        }

        string[] obsoleteTransitionIds = GetObsoleteManagedGeneratedTransitionIds(state, desiredTransitionIds);
        if (obsoleteTransitionIds.Length > 0)
        {
            TraversalTransitionRegistry.UnregisterRange(obsoleteTransitionIds);
            RemoveManagedGeneratedTransitionIds(chartName, obsoleteTransitionIds);
        }

        SyncManagedGeneratedTransitionSuppressions(state, activeTransitionIds);
    }

    private static string[] GetObsoleteManagedGeneratedTransitionIds(
        ManagedChartTransitionState state,
        SwiftHashSet<string> desiredTransitionIds)
    {
        if (state.TransitionIds.Count == 0)
            return Array.Empty<string>();

        SwiftList<string> obsoleteTransitionIds = new();
        foreach (string transitionId in state.TransitionIds)
        {
            if (!desiredTransitionIds.Contains(transitionId))
                obsoleteTransitionIds.Add(transitionId);
        }

        return obsoleteTransitionIds.Count == 0
            ? Array.Empty<string>()
            : obsoleteTransitionIds.ToArray();
    }

    private static void SyncManagedGeneratedTransitionSuppressions(
        ManagedChartTransitionState state,
        SwiftHashSet<string> activeTransitionIds)
    {
        if (state.TransitionIds.Count == 0)
            return;

        SwiftList<string> transitionsToSuppress = new();
        SwiftList<string> transitionsToUnsuppress = new();
        foreach (string transitionId in state.TransitionIds)
        {
            if (activeTransitionIds.Contains(transitionId))
                transitionsToUnsuppress.Add(transitionId);
            else
                transitionsToSuppress.Add(transitionId);
        }

        if (transitionsToSuppress.Count > 0)
            TraversalTransitionRegistry.SetManagedTransitionsSuppressed(
                transitionsToSuppress.ToArray(),
                suppressed: true);

        if (transitionsToUnsuppress.Count > 0)
            TraversalTransitionRegistry.SetManagedTransitionsSuppressed(
                transitionsToUnsuppress.ToArray(),
                suppressed: false);
    }

    private static void CollectManagedGeneratedTransitionsForPair(
        NavigationChart chart,
        ManagedChartTransitionState state,
        int firstX,
        int firstY,
        int firstZ,
        int secondX,
        int secondY,
        int secondZ,
        SwiftHashSet<string> desiredTransitionIds,
        SwiftHashSet<string> activeTransitionIds,
        SwiftList<TraversalTransition> missingTransitions)
    {
        TraversalTransition[] pairTransitions = GeneratedTraversalTransitionBuilder.BuildTransitionsForPair(
            chart,
            state.TransitionIdPrefix,
            firstX,
            firstY,
            firstZ,
            secondX,
            secondY,
            secondZ);
        if (pairTransitions.Length == 0)
            return;

        bool isActive = IsManagedGeneratedPairActive(
            chart.Name,
            chart,
            firstX,
            firstY,
            firstZ,
            secondX,
            secondY,
            secondZ);
        for (int i = 0; i < pairTransitions.Length; i++)
        {
            TraversalTransition transition = pairTransitions[i];
            desiredTransitionIds.Add(transition.Id);
            if (isActive)
                activeTransitionIds.Add(transition.Id);

            if (!state.TransitionIds.Contains(transition.Id))
                missingTransitions.Add(transition);
        }
    }

    private static bool IsManagedGeneratedPairActive(
        string chartName,
        NavigationChart chart,
        int firstX,
        int firstY,
        int firstZ,
        int secondX,
        int secondY,
        int secondZ)
    {
        if (!chart.IsInitialized)
            return false;

        return IsChartEffectiveOwnerAtPosition(chartName, chart.GetWorldPosition(firstX, firstY, firstZ))
            && IsChartEffectiveOwnerAtPosition(chartName, chart.GetWorldPosition(secondX, secondY, secondZ));
    }

    private static bool IsChartEffectiveOwnerAtPosition(string chartName, Vector3d worldPosition)
    {
        if (!TryGetResolvedChartVoxelState(worldPosition, out _, out ResolvedChartVoxelState state))
        {
            return false;
        }

        return string.Equals(state.EffectiveChartOwner, chartName, StringComparison.Ordinal);
    }

    private static void AddManagedGeneratedTransitionIds(
        string chartName,
        TraversalTransition[] transitions)
    {
        if (transitions == null || transitions.Length == 0)
            return;

        _navigationChartMapLock.EnterWriteLock();
        try
        {
            if (!_managedGeneratedTransitionsByChart.TryGetValue(chartName, out ManagedChartTransitionState state))
                return;

            for (int i = 0; i < transitions.Length; i++)
                state.TransitionIds.Add(transitions[i].Id);
        }
        finally { _navigationChartMapLock.ExitWriteLock(); }
    }

    private static void RemoveManagedGeneratedTransitionIds(
        string chartName,
        string[] transitionIds)
    {
        if (transitionIds == null || transitionIds.Length == 0)
            return;

        _navigationChartMapLock.EnterWriteLock();
        try
        {
            if (!_managedGeneratedTransitionsByChart.TryGetValue(chartName, out ManagedChartTransitionState state))
                return;

            for (int i = 0; i < transitionIds.Length; i++)
                state.TransitionIds.Remove(transitionIds[i]);
        }
        finally { _navigationChartMapLock.ExitWriteLock(); }
    }

    private static string[] CopyTransitionIds(SwiftHashSet<string> transitionIds)
    {
        if (transitionIds == null || transitionIds.Count == 0)
            return Array.Empty<string>();

        string[] copy = new string[transitionIds.Count];
        int index = 0;
        foreach (string transitionId in transitionIds)
            copy[index++] = transitionId;

        return copy;
    }

    private static string[] CopyTransitionIds(TraversalTransition[] transitions)
    {
        if (transitions == null || transitions.Length == 0)
            return Array.Empty<string>();

        string[] ids = new string[transitions.Length];
        for (int i = 0; i < transitions.Length; i++)
            ids[i] = transitions[i].Id;

        return ids;
    }

    private static void RemoveChartFromRegistry(string chartName)
    {
        _navigationChartMapLock.EnterWriteLock();
        try { _navigationChartMap.Remove(chartName); }
        finally { _navigationChartMapLock.ExitWriteLock(); }
    }

    private static bool TryGetResolvedChartVoxelState(
        Vector3d worldPosition,
        out GlobalVoxelIndex voxelIndex,
        out ResolvedChartVoxelState state)
    {
        if (GlobalGridManager.TryGetVoxel(worldPosition, out Voxel voxel))
        {
            voxelIndex = voxel.GlobalIndex;
            return TryGetResolvedChartVoxelState(voxelIndex, out state);
        }

        voxelIndex = default;
        state = null;
        return false;
    }

    private static bool TryGetResolvedChartVoxelState(
        GlobalVoxelIndex voxelIndex,
        out ResolvedChartVoxelState state)
    {
        if (_resolvedChartVoxelStates.TryGetValue(voxelIndex, out state)
            && state != null
            && state.HasAnyOwners
            && !string.IsNullOrEmpty(state.EffectiveChartOwner))
        {
            return true;
        }

        state = null;
        return false;
    }

    private static bool TryApplyChartCellUpdate(
        NavigationChart chart,
        int x,
        int y,
        int z,
        NavigationChartCell cell,
        SwiftHashSet<SolidChartPartition> partitionsToRebind,
        SwiftHashSet<string> invalidatedChartKeys,
        SwiftHashSet<string> managedChartsToRefresh)
    {
        if (!chart.TrySetCell(x, y, z, cell, out _))
            return false;

        TrackManagedChartRefresh(chart, managedChartsToRefresh);

        if (!chart.IsInitialized)
            return true;

        if (!TryGetChartUpdateVoxelContext(
            chart,
            x,
            y,
            z,
            managedChartsToRefresh,
            out Voxel voxel,
            out ResolvedChartVoxelState state,
            out NavigationChartCell previousEffectiveCell,
            out string previousEffectiveOwner))
        {
            return true;
        }

        TryUpdateResolvedVoxelStateForChartCell(chart, cell, voxel.GlobalIndex, ref state);

        ApplyResolvedVoxelState(voxel, state, previousEffectiveCell, partitionsToRebind);
        CollectEffectiveStateInvalidations(
            previousEffectiveOwner,
            previousEffectiveCell,
            state?.EffectiveChartOwner,
            state?.EffectiveCell ?? NavigationChartCell.Empty,
            invalidatedChartKeys);
        if (state != null && state.HasAnyOwners)
            state.AddChartOwnersTo(managedChartsToRefresh);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TrackManagedChartRefresh(
        NavigationChart chart,
        SwiftHashSet<string> managedChartsToRefresh)
    {
        managedChartsToRefresh?.Add(chart.Name);
    }

    private static bool TryGetChartUpdateVoxelContext(
        NavigationChart chart,
        int x,
        int y,
        int z,
        SwiftHashSet<string> managedChartsToRefresh,
        out Voxel voxel,
        out ResolvedChartVoxelState state,
        out NavigationChartCell previousEffectiveCell,
        out string previousEffectiveOwner)
    {
        voxel = null;
        state = null;
        previousEffectiveCell = NavigationChartCell.Empty;
        previousEffectiveOwner = null;

        Vector3d position = chart.GetWorldPosition(x, y, z);
        if (!GlobalGridManager.TryGetVoxel(position, out voxel))
            return false;

        _resolvedChartVoxelStates.TryGetValue(voxel.GlobalIndex, out state);
        if (state != null && state.HasAnyOwners)
        {
            state.AddChartOwnersTo(managedChartsToRefresh);
            previousEffectiveCell = state.EffectiveCell;
            previousEffectiveOwner = state.EffectiveChartOwner;
        }

        return true;
    }

    private static void TryUpdateResolvedVoxelStateForChartCell(
        NavigationChart chart,
        NavigationChartCell cell,
        GlobalVoxelIndex voxelIndex,
        ref ResolvedChartVoxelState state)
    {
        if (cell.HasTraversalData)
        {
            state ??= new ResolvedChartVoxelState();
            state.AddOwner(chart.Name, cell, chart.Priority, chart.RegistrationOrder);
            _resolvedChartVoxelStates[voxelIndex] = state;
            return;
        }

        if (state == null || !state.ContainsOwner(chart.Name))
            return;

        state.RemoveOwner(chart.Name);
        if (!state.HasAnyOwners)
            _resolvedChartVoxelStates.Remove(voxelIndex);
    }

    private static void RebindAndInvalidate(
        SwiftHashSet<SolidChartPartition> partitionsToRebind,
        SwiftHashSet<string> invalidatedChartKeys)
    {
        foreach (SolidChartPartition part in partitionsToRebind)
            part.BindNeighbors();

        foreach (string chartKey in invalidatedChartKeys)
            PathGuideFactory.InvalidateCacheFor(chartKey);
    }

    private static void CollectEffectiveStateInvalidations(
        string previousEffectiveOwner,
        NavigationChartCell previousEffectiveCell,
        string currentEffectiveOwner,
        NavigationChartCell currentEffectiveCell,
        SwiftHashSet<string> invalidatedChartKeys)
    {
        if (previousEffectiveCell.Equals(currentEffectiveCell)
            && string.Equals(previousEffectiveOwner, currentEffectiveOwner, StringComparison.Ordinal))
        {
            return;
        }

        if (!string.IsNullOrEmpty(previousEffectiveOwner))
            invalidatedChartKeys.Add(previousEffectiveOwner);

        if (!string.IsNullOrEmpty(currentEffectiveOwner))
            invalidatedChartKeys.Add(currentEffectiveOwner);
    }

    internal static bool HasAuthoredVolumeMedium(TraversalMedium medium)
    {
        return medium switch
        {
            TraversalMedium.Gas => _activeAuthoredGasCellCount > 0,
            TraversalMedium.Liquid => _activeAuthoredLiquidCellCount > 0,
            _ => false
        };
    }

    private static void ApplyResolvedVoxelState(
        Voxel voxel,
        ResolvedChartVoxelState state,
        NavigationChartCell previousEffectiveCell,
        SwiftHashSet<SolidChartPartition> partitionsToRebind)
    {
        NavigationChartCell effectiveCell = state?.EffectiveCell ?? NavigationChartCell.Empty;
        UpdateActiveVolumeMediumCounts(previousEffectiveCell, effectiveCell);

        bool solidPresenceChanged = previousEffectiveCell.HasSolid != effectiveCell.HasSolid;

        if (effectiveCell.HasSolid)
        {
            if (!voxel.TryGetPartition(out SolidChartPartition solidPartition))
            {
                solidPartition = PartitionPool.Rent();
                voxel.TryAddPartition(solidPartition);
            }

            solidPartition.ApplyAuthoredState(state, state.EffectiveChartOwner, effectiveCell);
            if (solidPresenceChanged)
                CollectSolidPartitionsForRebind(voxel, partitionsToRebind);
        }
        else if (previousEffectiveCell.HasSolid && voxel.TryGetPartition(out SolidChartPartition _))
        {
            voxel.TryRemovePartition<SolidChartPartition>();
            CollectSolidPartitionsForRebind(voxel, partitionsToRebind);
        }

        if (effectiveCell.HasVolume)
        {
            if (!voxel.TryGetPartition(out VolumeChartPartition volumePartition))
            {
                volumePartition = VolumeChartPartitionPool.Rent();
                voxel.TryAddPartition(volumePartition);
            }

            volumePartition.ApplyAuthoredState(state, state.EffectiveChartOwner, effectiveCell);
        }
        else if (previousEffectiveCell.HasVolume && voxel.TryGetPartition(out VolumeChartPartition _))
            voxel.TryRemovePartition<VolumeChartPartition>();
    }

    private static void UpdateActiveVolumeMediumCounts(
        NavigationChartCell previousEffectiveCell,
        NavigationChartCell currentEffectiveCell)
    {
        if (previousEffectiveCell.SupportsMedium(TraversalMedium.Gas))
            _activeAuthoredGasCellCount--;

        if (previousEffectiveCell.SupportsMedium(TraversalMedium.Liquid))
            _activeAuthoredLiquidCellCount--;

        if (currentEffectiveCell.SupportsMedium(TraversalMedium.Gas))
            _activeAuthoredGasCellCount++;

        if (currentEffectiveCell.SupportsMedium(TraversalMedium.Liquid))
            _activeAuthoredLiquidCellCount++;
    }

    private static void CollectSolidPartitionsForRebind(
        Voxel voxel,
        SwiftHashSet<SolidChartPartition> partitionsToRebind)
    {
        if (voxel.TryGetPartition(out SolidChartPartition currentPartition))
            partitionsToRebind.Add(currentPartition);

        foreach (SpatialDirection direction in SpatialAwareness.AllDirections)
        {
            if (voxel.TryGetNeighborFromDirection(direction, out Voxel neighborVoxel, useCache: true)
                && neighborVoxel.TryGetPartition(out SolidChartPartition neighborPartition))
            {
                partitionsToRebind.Add(neighborPartition);
            }
        }
    }

    #endregion

    #region Public Utility Methods

    /// <summary>
    /// Determines the maximum number of voxels to search based on the start and end voxel's grid sizes.
    /// </summary>
    /// <param name="start">The start voxel.</param>
    /// <param name="end">The end voxel.</param>
    /// <param name="maxSearchSize">The output max search size.</param>
    /// <returns>True if both voxels belong to valid grids; otherwise, false.</returns>
    public static bool TryGetMaxSearchSize(Voxel start, Voxel end, out int maxSearchSize)
    {
        if (!GlobalGridManager.TryGetGrid(start.GlobalIndex.GridIndex, out VoxelGrid startGrid)
            || !GlobalGridManager.TryGetGrid(end.GlobalIndex.GridIndex, out VoxelGrid endGrid))
        {
            maxSearchSize = 0;
            return false;
        }

        maxSearchSize = startGrid == endGrid ? startGrid.Size : startGrid.Size + endGrid.Size;
        return true;
    }

    /// <summary>
    /// Checks if a path is needed between the start and end positions based on traced voxels and unit size.
    /// </summary>
    /// <param name="startPos">The starting position.</param>
    /// <param name="endPos">The destination position.</param>
    /// <param name="unitSize">The size of the navigating unit.</param>
    /// <param name="allowUnwalkableEndpoints">Whether to permit unwalkable voxels.</param>
    /// <returns>True if a path is required; otherwise, false.</returns>
    public static bool NeedsPath(
        Vector3d startPos,
        Vector3d endPos,
        Fixed64 unitSize,
        bool allowUnwalkableEndpoints = false)
    {
        foreach (GridVoxelSet gridVoxelSet in GridTracer.TraceLine(startPos, endPos))
        {
            foreach (Voxel voxel in gridVoxelSet.Voxels)
            {
                // A path is required if a voxel doesn't exist in the traced line
                if (!voxel.TryGetPartition(out SolidChartPartition partition))
                    return true;

                if (!allowUnwalkableEndpoints && !voxel.IsBlocked && partition.IsImpassable(unitSize))
                    return true;
            }
        }
        return false;
    }

    #endregion
}

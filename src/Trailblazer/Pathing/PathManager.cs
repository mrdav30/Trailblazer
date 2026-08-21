//=======================================================================
// PathManager.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;

namespace Trailblazer.Pathing;

/// <summary>
/// Implements context-scoped chart registration, initialization, validation,
/// neighbor discovery, and related pathing operations.
/// </summary>
internal static class PathManager
{
    [ThreadStatic]
    private static PathingWorldState? _activeState;

    #region Pools

    internal static readonly SwiftHashSetPool<SolidChartPartition> PartitionSetPool = new();

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
                foreach (NavigationChartRegistration registration in _navigationChartMap.Values)
                    charts[index++] = registration.Chart;
                return charts;
            }
            finally { _navigationChartMapLock.ExitReadLock(); }
        }
    }

    /// <summary>
    /// Internal dictionary of all registered navigation charts, keyed by their unique names.
    /// </summary>
    private static SwiftDictionary<string, NavigationChartRegistration> _navigationChartMap =>
        ActiveState.NavigationChartMap;

    private static SwiftDictionary<WorldVoxelIndex, ResolvedChartVoxelState> _resolvedChartVoxelStates =>
        ActiveState.ResolvedChartVoxelStates;

    private static SwiftDictionary<ushort, SwiftDictionary<string, int>> _initializedChartTouchCountsByGridIndex =>
        ActiveState.InitializedChartTouchCountsByGridIndex;

    /// <summary>
    /// Lock for managing concurrent access to <c>_navigationChartMap</c> operations.
    /// Ensures thread safety for read/write operations.
    /// </summary>
    private static ReaderWriterLockSlim _navigationChartMapLock => ActiveState.NavigationChartMapLock;

    private static int _nextChartRegistrationOrder
    {
        get => ActiveState.NextChartRegistrationOrder;
        set => ActiveState.NextChartRegistrationOrder = value;
    }

    internal static PathingWorldState ActiveState => _activeState ?? throw new InvalidOperationException(
        "Trailblazer pathing operations require an explicit TrailblazerWorldContext.");

    internal static bool TryGetActiveState(out PathingWorldState? state)
    {
        if (_activeState != null)
        {
            state = _activeState;
            return true;
        }

        state = null;
        return false;
    }

    internal static IDisposable EnterState(PathingWorldState state)
    {
        return new PathingWorldStateScope(state);
    }

    private sealed class PathingWorldStateScope : IDisposable
    {
        private readonly PathingWorldState? _previousState;

        public PathingWorldStateScope(PathingWorldState state)
        {
            _previousState = _activeState;
            _activeState = state;
        }

        public void Dispose()
        {
            _activeState = _previousState;
        }
    }

    #endregion

    #region Lifecycle Hooks

    /// <summary>
    /// Gets whether Trailblazer currently has an active configured grid world.
    /// </summary>
    public static bool HasConfiguredWorld => _activeState != null;

    /// <summary>
    /// Gets the active configured grid world.
    /// </summary>
    public static GridWorld ConfiguredWorld => GetConfiguredWorld();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void LinkWorld(GridWorld world)
    {
        if (_activeState != null)
        {
            if (!ReferenceEquals(_activeState.World, world))
                throw new InvalidOperationException("The supplied GridWorld does not belong to the active Trailblazer pathing context.");

            return;
        }

        throw new InvalidOperationException(
            "PathManager operations require TrailblazerWorldContext.Pathing to select the owning context.");
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static GridWorld GetConfiguredWorld() => ActiveState.World;

    internal static void Tick()
    {
        if (HasConfiguredWorld)
            ActiveState.ExternalGridBridge.FlushPendingGridChanges();
    }

    /// <summary>
    /// Clears all registered maps, partitions, and guide pools.
    /// </summary>
    public static void Reset()
    {
        if (!HasConfiguredWorld)
            return;

        ResetPathingState(ActiveState, resetScopedRegistries: true, flushGuideCache: true);
    }

    /// <summary>
    /// Clears all registered maps, partitions, and guide pools.
    /// </summary>
    public static void Reset(GridWorld world)
    {
        LinkWorld(world);
        ResetPathingState(ActiveState, resetScopedRegistries: true, flushGuideCache: true);
    }

    internal static void ResetPathingState(
        PathingWorldState state,
        bool resetScopedRegistries,
        bool flushGuideCache)
    {
        using (EnterState(state))
        {
            ClearLiveGridState(state.World);
            PathManagerExternalGridBridge.ResetDiagnostics();

            _navigationChartMapLock.EnterWriteLock();
            try
            {
                _navigationChartMap.Clear();
                _nextChartRegistrationOrder = 0;
            }
            finally
            {
                _navigationChartMapLock.ExitWriteLock();
            }

            _resolvedChartVoxelStates.Clear();
            _initializedChartTouchCountsByGridIndex.Clear();
            ClearActiveAuthoredVolumeMediumCounts();

        }
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
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="chart"/>'s interval does not match the owning world's voxel size.
    /// </exception>
    public static bool Register(NavigationChart chart, bool initializeChart = true)
    {
        PathingWorldState state = ActiveState;
        using (EnterState(state))
            return Register(state.World, chart, initializeChart);
    }

    /// <summary>
    /// Attempts to register a new navigation chart with the manager.
    /// </summary>
    /// <param name="world">The grid world context for the chart.</param>
    /// <param name="chart">The map to register.</param>
    /// <param name="initializeChart">Whether to initialize the chart after registration succeeds.</param>
    /// <returns>True if successful, false if a duplicate name exists.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="chart"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="chart"/>'s interval does not match <paramref name="world"/>'s voxel size.
    /// </exception>
    public static bool Register(GridWorld world, NavigationChart chart, bool initializeChart = true)
    {
        SwiftThrowHelper.ThrowIfNull(chart, nameof(chart));
        ThrowIfDirectWorldRegisterCall();
        LinkWorld(world);

        return RegisterChartInternal(world, chart, initializeChart);
    }

    private static void ThrowIfDirectWorldRegisterCall()
    {
        if (_activeState != null)
            return;

        throw new InvalidOperationException(
            "PathManager.Register(world, ...) is no longer a multi-world registration API. " +
            "Create a TrailblazerWorldContext for that GridWorld and call context.Pathing.Register(...), " +
            "or initialize the single default facade and call PathManager.Register(chart).");
    }

    private static bool RegisterChartInternal(
        GridWorld world,
        NavigationChart chart,
        bool initializeChart)
    {
        _navigationChartMapLock.EnterWriteLock();
        try
        {
            if (_navigationChartMap.ContainsKey(chart.Name))
                return false;

            TrailblazerGridCompatibility.ValidateWorld(world, chart.Interval, nameof(chart));
            var registration = new NavigationChartRegistration(
                chart,
                unchecked(++_nextChartRegistrationOrder));
            _navigationChartMap.Add(chart.Name, registration);
        }
        finally { _navigationChartMapLock.ExitWriteLock(); }

        if (initializeChart)
            InitializeChart(world, chart.Name);

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
        try
        {
            if (_navigationChartMap.TryGetValue(name, out NavigationChartRegistration registration))
            {
                chart = registration.Chart;
                return true;
            }

            chart = null!;
            return false;
        }
        finally
        {
            _navigationChartMapLock.ExitReadLock();
        }
    }

    /// <summary>
    /// Attempts to retrieve live registration state for a chart by name.
    /// </summary>
    /// <param name="name">The registered chart name.</param>
    /// <param name="registration">The live chart registration.</param>
    /// <returns>True when a registration exists; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool TryGetNavigationChartRegistration(
        string name,
        out NavigationChartRegistration registration)
    {
        _navigationChartMapLock.EnterReadLock();
        try { return TryGetNavigationChartRegistration_NoLock(name, out registration); }
        finally { _navigationChartMapLock.ExitReadLock(); }
    }

    /// <summary>
    /// Gets whether a registered chart is currently initialized into live voxel state.
    /// </summary>
    /// <param name="name">The registered chart name.</param>
    /// <returns>True when the chart has an initialized live registration; otherwise, false.</returns>
    public static bool IsChartInitialized(string name)
    {
        return TryGetNavigationChartRegistration(name, out NavigationChartRegistration registration)
            && registration.IsInitialized;
    }

    /// <summary>
    /// Gets whether an authored chart's current registration is initialized.
    /// </summary>
    /// <param name="chart">The authored chart to inspect.</param>
    /// <returns>True when the chart is registered and initialized; otherwise, false.</returns>
    public static bool IsChartInitialized(NavigationChart chart)
    {
        return chart != null && IsChartInitialized(chart.Name);
    }

    private static bool TryGetNavigationChartRegistration_NoLock(
        string name,
        out NavigationChartRegistration registration)
    {
        if (_navigationChartMap.TryGetValue(name, out registration))
            return true;

        registration = null!;
        return false;
    }

    /// <summary>
    /// Attempts to retrieve the winning effective authored cell at the provided voxel.
    /// </summary>
    /// <param name="voxelIndex">The voxel to inspect.</param>
    /// <param name="cell">The effective authored cell currently winning overlap resolution.</param>
    /// <returns>True when the voxel currently has an effective authored chart cell; otherwise, false.</returns>
    public static bool TryGetEffectiveCell(WorldVoxelIndex voxelIndex, out NavigationChartCell cell)
    {
        if (TryGetResolvedChartVoxelState(voxelIndex, out ResolvedChartVoxelState? state))
        {
            cell = state!.EffectiveCell;
            return true;
        }

        cell = NavigationChartCell.Empty;
        return false;
    }

    /// <summary>
    /// Attempts to retrieve the winning effective authored cell at the provided world position.
    /// </summary>
    /// <param name="world">The grid world context to search.</param>
    /// <param name="worldPosition">The world position to inspect.</param>
    /// <param name="cell">The effective authored cell currently winning overlap resolution.</param>
    /// <returns>True when the position resolves to a voxel with an effective authored chart cell; otherwise, false.</returns>
    public static bool TryGetEffectiveCell(GridWorld world, Vector3d worldPosition, out NavigationChartCell cell)
    {
        LinkWorld(world);
        if (TryGetResolvedChartVoxelState(world, worldPosition, out _, out ResolvedChartVoxelState? state))
        {
            cell = state!.EffectiveCell;
            return true;
        }

        cell = NavigationChartCell.Empty;
        return false;
    }

    /// <summary>
    /// Attempts to retrieve the winning effective authored cell at the provided world position using the configured world.
    /// </summary>
    public static bool TryGetEffectiveCell(Vector3d worldPosition, out NavigationChartCell cell)
    {
        return TryGetEffectiveCell(GetConfiguredWorld(), worldPosition, out cell);
    }

    /// <summary>
    /// Attempts to retrieve the chart currently winning overlap resolution at the provided voxel.
    /// </summary>
    /// <param name="voxelIndex">The voxel to inspect.</param>
    /// <param name="chartName">The effective chart owner.</param>
    /// <returns>True when the voxel currently has an effective chart owner; otherwise, false.</returns>
    public static bool TryGetEffectiveChartOwner(WorldVoxelIndex voxelIndex, out string? chartName)
    {
        if (TryGetResolvedChartVoxelState(voxelIndex, out ResolvedChartVoxelState? state))
        {
            chartName = state!.EffectiveChartOwner;
            return true;
        }

        chartName = null;
        return false;
    }

    /// <summary>
    /// Attempts to retrieve the chart currently winning overlap resolution at the provided world position.
    /// </summary>
    /// <param name="world">The grid world context to search.</param>
    /// <param name="worldPosition">The world position to inspect.</param>
    /// <param name="chartName">The effective chart owner.</param>
    /// <returns>True when the position resolves to a voxel with an effective chart owner; otherwise, false.</returns>
    public static bool TryGetEffectiveChartOwner(GridWorld world, Vector3d worldPosition, out string? chartName)
    {
        LinkWorld(world);
        if (TryGetResolvedChartVoxelState(world, worldPosition, out _, out ResolvedChartVoxelState? state))
        {
            chartName = state!.EffectiveChartOwner;
            return true;
        }

        chartName = null;
        return false;
    }

    /// <summary>
    /// Attempts to retrieve the chart currently winning overlap resolution at the provided world position using the configured world.
    /// </summary>
    public static bool TryGetEffectiveChartOwner(Vector3d worldPosition, out string? chartName)
    {
        return TryGetEffectiveChartOwner(GetConfiguredWorld(), worldPosition, out chartName);
    }

    /// <summary>
    /// Initializes all registered navigation charts by materializing their authored surface and volume partitions.
    /// </summary>
    public static void InitializeAllCharts()
    {
        InitializeAllCharts(GetConfiguredWorld());
    }

    /// <summary>
    /// Initializes all registered navigation charts by materializing their authored surface and volume partitions.
    /// </summary>
    public static void InitializeAllCharts(GridWorld world)
    {
        LinkWorld(world);
        foreach (NavigationChart chart in AllCharts)
            InitializeChart(world, chart.Name);
    }

    /// <summary>
    /// Applies one authored cell mutation to a registered chart using chart-local indices.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the target cell was in bounds and the authored payload changed; otherwise, <c>false</c>.
    /// </returns>
    public static bool TryUpdateChartCell(string chartName, int x, int y, int z, NavigationChartCell cell)
    {
        return TryUpdateChartCell(GetConfiguredWorld(), chartName, x, y, z, cell);
    }

    /// <summary>
    /// Applies one authored cell mutation to a registered chart using chart-local indices.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the target cell was in bounds and the authored payload changed; otherwise, <c>false</c>.
    /// </returns>
    public static bool TryUpdateChartCell(GridWorld world, string chartName, int x, int y, int z, NavigationChartCell cell)
    {
        LinkWorld(world);
        if (!TryGetNavigationChartRegistration(chartName, out NavigationChartRegistration registration))
            return false;

        return TryUpdateChartCell(world, registration, x, y, z, cell);
    }

    private static bool TryUpdateChartCell(
        GridWorld world,
        NavigationChartRegistration registration,
        int x,
        int y,
        int z,
        NavigationChartCell cell)
    {
        NavigationChart chart = registration.Chart;
        SwiftHashSet<SolidChartPartition> partitionsToRebind = PartitionSetPool.Rent();
        SwiftHashSet<string> invalidatedChartKeys = SwiftHashSetPool<string>.Shared.Rent();
        try
        {
            bool changed = TryApplyChartCellUpdate(
                world,
                registration,
                x,
                y,
                z,
                cell,
                partitionsToRebind,
                invalidatedChartKeys);

            RebindAndInvalidate(partitionsToRebind, invalidatedChartKeys);
            return changed;
        }
        finally
        {
            PartitionSetPool.Release(partitionsToRebind);
            SwiftHashSetPool<string>.Shared.Release(invalidatedChartKeys);
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
        return TryUpdateChartCell(GetConfiguredWorld(), chartName, worldPosition, cell);
    }

    /// <summary>
    /// Applies one authored cell mutation to a registered chart using a world-space position.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the position resolves inside the chart and the authored payload changed; otherwise, <c>false</c>.
    /// </returns>
    public static bool TryUpdateChartCell(GridWorld world, string chartName, Vector3d worldPosition, NavigationChartCell cell)
    {
        LinkWorld(world);
        if (!TryGetNavigationChartRegistration(chartName, out NavigationChartRegistration registration)
            || !registration.Chart.TryWorldToIndex(worldPosition, out int x, out int y, out int z))
        {
            return false;
        }

        return TryUpdateChartCell(world, registration, x, y, z, cell);
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
        return ApplyChartUpdates(GetConfiguredWorld(), chartName, updates);
    }

    /// <summary>
    /// Applies a sparse batch of authored cell mutations to a registered chart.
    /// </summary>
    /// <param name="world">The grid world context for the chart.</param>
    /// <param name="chartName">The registered chart to mutate.</param>
    /// <param name="updates">The sparse set of cell changes to apply in order.</param>
    /// <returns>The number of authored cell mutations that changed the chart payload.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="updates"/> is null.</exception>
    public static int ApplyChartUpdates(GridWorld world, string chartName, IReadOnlyList<NavigationChartCellUpdate> updates)
    {
        SwiftThrowHelper.ThrowIfNull(updates, nameof(updates));
        LinkWorld(world);

        if (updates.Count == 0
            || !TryGetNavigationChartRegistration(chartName, out NavigationChartRegistration registration))
        {
            return 0;
        }

        NavigationChart chart = registration.Chart;
        SwiftHashSet<SolidChartPartition> partitionsToRebind = PartitionSetPool.Rent();
        SwiftHashSet<string> invalidatedChartKeys = SwiftHashSetPool<string>.Shared.Rent();
        try
        {
            int changedCount = 0;
            for (int i = 0; i < updates.Count; i++)
            {
                NavigationChartCellUpdate update = updates[i];
                if (TryApplyChartCellUpdate(
                    world,
                    registration,
                    update.X,
                    update.Y,
                    update.Z,
                    update.Cell,
                    partitionsToRebind,
                    invalidatedChartKeys))
                {
                    changedCount++;
                }
            }

            RebindAndInvalidate(partitionsToRebind, invalidatedChartKeys);
            return changedCount;
        }
        finally
        {
            PartitionSetPool.Release(partitionsToRebind);
            SwiftHashSetPool<string>.Shared.Release(invalidatedChartKeys);
        }
    }

    /// <summary>
    /// Initializes a specific navigation chart by materializing its authored surface and volume partitions.
    /// </summary>
    /// <param name="chartKey">The name of the map to initialize.</param>
    public static void InitializeChart(string chartKey)
    {
        InitializeChart(GetConfiguredWorld(), chartKey);
    }

    /// <summary>
    /// Initializes a specific navigation chart by materializing its authored surface and volume partitions.
    /// </summary>
    /// <param name="world">The grid world context for the chart.</param>
    /// <param name="chartKey">The name of the map to initialize.</param>
    public static void InitializeChart(GridWorld world, string chartKey)
    {
        LinkWorld(world);
        if (string.IsNullOrEmpty(chartKey)
            || !TryGetNavigationChartRegistration(chartKey, out NavigationChartRegistration registration)
            || registration.IsInitialized)
        {
            return;
        }

        NavigationChart chart = registration.Chart;
        PathManagerExternalGridBridge.FlushPendingGridChanges();

        SwiftHashSet<SolidChartPartition> partitionsToRebind = PartitionSetPool.Rent();
        try
        {
            foreach ((Vector3d pos, NavigationChartCell cell) in chart.GetAuthoredCells())
            {
                if (!world.TryGetVoxel(pos, out Voxel? voxel) || voxel == null)
                    continue;

                if (!_resolvedChartVoxelStates.TryGetValue(voxel.WorldIndex, out ResolvedChartVoxelState state))
                {
                    state = new ResolvedChartVoxelState();
                    _resolvedChartVoxelStates[voxel.WorldIndex] = state;
                }
                NavigationChartCell previousEffectiveCell = state.EffectiveCell;
                state.AddOwner(chart.Name, cell, chart.Priority, registration.RegistrationOrder);
                ApplyResolvedVoxelState(world, voxel, state, previousEffectiveCell, partitionsToRebind);
                TrackInitializedChartGridTouch(voxel.GridIndex, chart.Name);
            }

            BindCollectedSolidPartitions(partitionsToRebind);

            registration.IsInitialized = true;
        }
        finally
        {
            PartitionSetPool.Release(partitionsToRebind);
        }
    }

    /// <summary>
    /// Unloads the navigation chart identified by the specified key from the given world.
    /// </summary>
    /// <param name="chartKey">
    /// The unique key identifying the navigation chart to unload.
    /// If the key does not correspond to a loaded chart, no action is taken.</param>
    public static void UnloadChart(string chartKey)
    {
        UnloadChart(GetConfiguredWorld(), chartKey);
    }

    /// <summary>
    /// Unloads the navigation chart identified by the specified key from the given world.
    /// </summary>
    /// <param name="world">The world instance from which to unload the navigation chart.</param>
    /// <param name="chartKey">
    /// The unique key identifying the navigation chart to unload.
    /// If the key does not correspond to a loaded chart, no action is taken.</param>
    public static void UnloadChart(GridWorld world, string chartKey)
    {
        LinkWorld(world);
        if (!TryGetNavigationChartRegistration(chartKey, out NavigationChartRegistration registration))
            return;

        UnloadChart(world, registration);
    }

    /// <summary>
    /// Unloads a navigation map by name and releases associated partitions.
    /// </summary>
    /// <param name="chart">The navigation chart to unload.</param>
    public static void UnloadChart(NavigationChart chart)
    {
        UnloadChart(GetConfiguredWorld(), chart);
    }

    /// <summary>
    /// Unloads a navigation map by name and releases associated partitions.
    /// </summary>
    /// <param name="world">The grid world context for the chart.</param>
    /// <param name="chart">The navigation chart to unload.</param>
    public static void UnloadChart(GridWorld world, NavigationChart chart)
    {
        LinkWorld(world);
        if (chart == null)
            return;

        if (!TryGetNavigationChartRegistration(chart.Name, out NavigationChartRegistration registration))
            return;

        UnloadChart(world, registration);
    }

    private static void UnloadChart(GridWorld world, NavigationChartRegistration registration)
    {
        NavigationChart chart = registration.Chart;
        if (!registration.IsInitialized)
        {
            RemoveChartFromRegistry(chart.Name);
            return;
        }

        SwiftHashSet<SolidChartPartition> partitionsToRebind = PartitionSetPool.Rent();
        try
        {
            foreach ((Vector3d position, _) in chart.GetAuthoredCells())
            {
                if (!world.TryGetVoxel(position, out Voxel? voxel) || voxel == null)
                    continue;

                if (!_resolvedChartVoxelStates.TryGetValue(voxel.WorldIndex, out ResolvedChartVoxelState state)
                    || !state.ContainsOwner(chart.Name))
                {
                    continue;
                }

                NavigationChartCell previousEffectiveCell = state.EffectiveCell;
                state.RemoveOwner(chart.Name);
                ApplyResolvedVoxelState(world, voxel, state, previousEffectiveCell, partitionsToRebind);
                UntrackInitializedChartGridTouch(voxel.GridIndex, chart.Name);

                if (!state.HasAnyOwners)
                    _resolvedChartVoxelStates.Remove(voxel.WorldIndex);
            }

            BindCollectedSolidPartitions(partitionsToRebind);

            registration.IsInitialized = false;
            RemoveChartFromRegistry(chart.Name);
        }
        finally
        {
            PartitionSetPool.Release(partitionsToRebind);
        }
    }

    #endregion

    #region Pathfinding Utilities

    internal static int RebuildInitializedChartsAgainstExternalGridRequests(
        ExternalGridChartRebuildRequest[] rebuildRequests)
    {
        return RebuildInitializedChartsAgainstExternalGridRequests(GetConfiguredWorld(), rebuildRequests);
    }

    internal static int RebuildInitializedChartsAgainstExternalGridRequests(
        GridWorld world,
        ExternalGridChartRebuildRequest[] rebuildRequests)
    {
        if (rebuildRequests == null || rebuildRequests.Length == 0)
            return 0;

        NavigationChart[] initializedCharts = GetInitializedChartsAffectedByExternalGridRequestsSnapshot(rebuildRequests);
        RebuildInitializedChartsAgainstCurrentGrids(world, initializedCharts);
        return initializedCharts.Length;
    }

    internal static int RebuildInitializedChartsAgainstExternalGridBounds(
        GridWorld world,
        ushort gridIndex,
        Vector3d boundsMin,
        Vector3d boundsMax,
        bool useLiveGridTouchIndex)
    {
        ExternalGridChartRebuildRequest[] rebuildRequests =
        {
            new(
                gridIndex,
                boundsMin,
                boundsMax,
                includeLiveGridTouches: useLiveGridTouchIndex,
                includeAuthoredCellsInBounds: !useLiveGridTouchIndex)
        };

        return RebuildInitializedChartsAgainstExternalGridRequests(world, rebuildRequests);
    }

    private static void RebuildInitializedChartsAgainstCurrentGrids(GridWorld world, NavigationChart[] chartsToRebuild)
    {
        if (chartsToRebuild.Length > 0)
        {
            for (int i = 0; i < chartsToRebuild.Length; i++)
                ClearInitializedChartLiveStatePreservingRegistration(world, chartsToRebuild[i]);

            for (int i = 0; i < chartsToRebuild.Length; i++)
                InitializeChart(world, chartsToRebuild[i].Name);
        }

    }

    private static NavigationChart[] GetInitializedChartsSnapshot()
    {
        _navigationChartMapLock.EnterReadLock();
        try
        {
            if (_navigationChartMap.Count == 0)
                return Array.Empty<NavigationChart>();

            SwiftList<NavigationChartRegistration> initializedCharts = new();
            foreach (NavigationChartRegistration registration in _navigationChartMap.Values)
            {
                if (registration.IsInitialized)
                    initializedCharts.Add(registration);
            }

            return BuildInitializedChartSelectionSnapshot(initializedCharts);
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

            SwiftList<NavigationChartRegistration> initializedCharts = new();
            foreach (NavigationChartRegistration registration in _navigationChartMap.Values)
            {
                NavigationChart chart = registration.Chart;
                if (!registration.IsInitialized
                    || !DoBoundsOverlap(chart.MinBounds, chart.MaxBounds, boundsMin, boundsMax))
                {
                    continue;
                }

                initializedCharts.Add(registration);
            }

            return BuildInitializedChartSelectionSnapshot(initializedCharts);
        }
        finally
        {
            _navigationChartMapLock.ExitReadLock();
        }
    }

    private static NavigationChart[] GetInitializedChartsTouchingGridSnapshot(ushort gridIndex)
    {
        _navigationChartMapLock.EnterReadLock();
        try
        {
            if (_navigationChartMap.Count == 0)
                return Array.Empty<NavigationChart>();

            SwiftDictionary<string, NavigationChartRegistration> selectedCharts = new(4, StringComparer.Ordinal);
            AddInitializedChartsTouchingGrid_NoLock(gridIndex, selectedCharts);
            return BuildInitializedChartSelectionSnapshot_NoLock(selectedCharts);
        }
        finally
        {
            _navigationChartMapLock.ExitReadLock();
        }
    }

    private static NavigationChart[] GetInitializedChartsWithAuthoredCellsIntersectingBoundsSnapshot(
        Vector3d boundsMin,
        Vector3d boundsMax)
    {
        _navigationChartMapLock.EnterReadLock();
        try
        {
            if (_navigationChartMap.Count == 0)
                return Array.Empty<NavigationChart>();

            SwiftDictionary<string, NavigationChartRegistration> selectedCharts = new(4, StringComparer.Ordinal);
            AddInitializedChartsWithAuthoredCellsIntersectingBounds_NoLock(boundsMin, boundsMax, selectedCharts);
            return BuildInitializedChartSelectionSnapshot_NoLock(selectedCharts);
        }
        finally
        {
            _navigationChartMapLock.ExitReadLock();
        }
    }

    private static NavigationChart[] GetInitializedChartsAffectedByExternalGridRequestsSnapshot(
        ExternalGridChartRebuildRequest[] rebuildRequests)
    {
        _navigationChartMapLock.EnterReadLock();
        try
        {
            if (_navigationChartMap.Count == 0)
                return Array.Empty<NavigationChart>();

            SwiftDictionary<string, NavigationChartRegistration> selectedCharts = new(8, StringComparer.Ordinal);
            for (int i = 0; i < rebuildRequests.Length; i++)
            {
                ExternalGridChartRebuildRequest rebuildRequest = rebuildRequests[i];
                if (!rebuildRequest.HasSelectionCriteria)
                    continue;

                if (rebuildRequest.IncludeLiveGridTouches)
                    AddInitializedChartsTouchingGrid_NoLock(rebuildRequest.GridIndex, selectedCharts);

                if (rebuildRequest.IncludeAuthoredCellsInBounds)
                {
                    AddInitializedChartsWithAuthoredCellsIntersectingBounds_NoLock(
                        rebuildRequest.BoundsMin,
                        rebuildRequest.BoundsMax,
                        selectedCharts);
                }
            }

            return BuildInitializedChartSelectionSnapshot_NoLock(selectedCharts);
        }
        finally
        {
            _navigationChartMapLock.ExitReadLock();
        }
    }

    private static void AddInitializedChartsTouchingGrid_NoLock(
        ushort gridIndex,
        SwiftDictionary<string, NavigationChartRegistration> selectedCharts)
    {
        if (!_initializedChartTouchCountsByGridIndex.TryGetValue(gridIndex, out SwiftDictionary<string, int> chartTouches)
            || chartTouches.Count == 0)
        {
            return;
        }

        foreach (KeyValuePair<string, int> pair in chartTouches)
        {
            if (pair.Value <= 0
                || !_navigationChartMap.TryGetValue(pair.Key, out NavigationChartRegistration registration)
                || !registration.IsInitialized)
            {
                continue;
            }

            selectedCharts[registration.Chart.Name] = registration;
        }
    }

    private static void AddInitializedChartsWithAuthoredCellsIntersectingBounds_NoLock(
        Vector3d boundsMin,
        Vector3d boundsMax,
        SwiftDictionary<string, NavigationChartRegistration> selectedCharts)
    {
        foreach (NavigationChartRegistration registration in _navigationChartMap.Values)
        {
            NavigationChart chart = registration.Chart;
            if (!registration.IsInitialized
                || !DoBoundsOverlap(chart.MinBounds, chart.MaxBounds, boundsMin, boundsMax)
                || !ChartHasAuthoredCellInsideBounds(chart, boundsMin, boundsMax))
            {
                continue;
            }

            selectedCharts[chart.Name] = registration;
        }
    }

    private static NavigationChart[] BuildInitializedChartSelectionSnapshot_NoLock(
        SwiftDictionary<string, NavigationChartRegistration> selectedCharts)
    {
        if (selectedCharts.Count == 0)
            return Array.Empty<NavigationChart>();

        NavigationChartRegistration[] snapshot = new NavigationChartRegistration[selectedCharts.Count];
        int index = 0;
        foreach (NavigationChartRegistration registration in selectedCharts.Values)
            snapshot[index++] = registration;

        Array.Sort(snapshot, CompareRegistrationsByRegistrationOrder);
        return CopyCharts(snapshot);
    }

    private static bool ChartHasAuthoredCellInsideBounds(
        NavigationChart chart,
        Vector3d boundsMin,
        Vector3d boundsMax)
    {
        foreach ((Vector3d position, _) in chart.GetAuthoredCells())
        {
            if (IsPositionInsideBounds(position, boundsMin, boundsMax))
                return true;
        }

        return false;
    }

    private static void ClearInitializedChartLiveStatePreservingRegistration(GridWorld world, NavigationChart chart)
    {
        if (chart == null
            || !TryGetNavigationChartRegistration(chart.Name, out NavigationChartRegistration registration))
        {
            return;
        }

        ClearInitializedChartLiveStatePreservingRegistration(world, registration);
    }

    private static void ClearInitializedChartLiveStatePreservingRegistration(
        GridWorld world,
        NavigationChartRegistration registration)
    {
        NavigationChart chart = registration.Chart;
        SwiftHashSet<SolidChartPartition> partitionsToRebind = PartitionSetPool.Rent();
        SwiftList<WorldVoxelIndex> resolvedVoxelIndicesToRemove = new();
        try
        {
            foreach (KeyValuePair<WorldVoxelIndex, ResolvedChartVoxelState> pair in _resolvedChartVoxelStates)
            {
                ResolvedChartVoxelState state = pair.Value;
                if (!state.ContainsOwner(chart.Name))
                    continue;

                NavigationChartCell previousEffectiveCell = state.EffectiveCell;
                state.RemoveOwner(chart.Name);

                bool hasLiveVoxel = world.TryGetGridAndVoxel(pair.Key, out _, out Voxel? voxel);
                if (hasLiveVoxel)
                {
                    ApplyResolvedVoxelState(world, voxel!, state, previousEffectiveCell, partitionsToRebind);
                    UntrackInitializedChartGridTouch(voxel!.GridIndex, chart.Name);
                }

                if (!state.HasAnyOwners
                    || !hasLiveVoxel)
                {
                    resolvedVoxelIndicesToRemove.Add(pair.Key);
                }
            }

            for (int i = 0; i < resolvedVoxelIndicesToRemove.Count; i++)
                _resolvedChartVoxelStates.Remove(resolvedVoxelIndicesToRemove[i]);

            BindCollectedSolidPartitions(partitionsToRebind);

            registration.IsInitialized = false;
        }
        finally
        {
            PartitionSetPool.Release(partitionsToRebind);
        }
    }

    private static void ClearLiveGridState(GridWorld world)
    {
        foreach (KeyValuePair<WorldVoxelIndex, ResolvedChartVoxelState> pair in _resolvedChartVoxelStates)
        {
            if (!world.TryGetGridAndVoxel(pair.Key, out _, out Voxel? voxel))
                continue;

            RemoveLivePathingPartitions(voxel!);
        }

        ClearLiveGridState();
    }

    private static void ClearLiveGridState()
    {
        _resolvedChartVoxelStates.Clear();
        _initializedChartTouchCountsByGridIndex.Clear();
        ClearActiveAuthoredVolumeMediumCounts();
    }

    private static bool DoBoundsOverlap(
        Vector3d firstMin,
        Vector3d firstMax,
        Vector3d secondMin,
        Vector3d secondMax)
    {
        return firstMin.X <= secondMax.X
            && firstMax.X >= secondMin.X
            && firstMin.Y <= secondMax.Y
            && firstMax.Y >= secondMin.Y
            && firstMin.Z <= secondMax.Z
            && firstMax.Z >= secondMin.Z;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsPositionInsideBounds(
        Vector3d position,
        Vector3d boundsMin,
        Vector3d boundsMax)
    {
        return position.X >= boundsMin.X
            && position.X <= boundsMax.X
            && position.Y >= boundsMin.Y
            && position.Y <= boundsMax.Y
            && position.Z >= boundsMin.Z
            && position.Z <= boundsMax.Z;
    }

    private static void RemoveLivePathingPartitions(Voxel voxel)
    {
        if (voxel.TryGetPartition<SolidChartPartition>(out _))
            voxel.TryRemovePartition<SolidChartPartition>();

        if (voxel.TryGetPartition<VolumeChartPartition>(out _))
            voxel.TryRemovePartition<VolumeChartPartition>();
    }

    private static NavigationChart[] BuildInitializedChartSelectionSnapshot(
        SwiftList<NavigationChartRegistration> registrations)
    {
        if (registrations.Count == 0)
            return Array.Empty<NavigationChart>();

        NavigationChartRegistration[] snapshot = registrations.ToArray();
        Array.Sort(snapshot, CompareRegistrationsByRegistrationOrder);
        return CopyCharts(snapshot);
    }

    private static NavigationChart[] CopyCharts(NavigationChartRegistration[] registrations)
    {
        NavigationChart[] charts = new NavigationChart[registrations.Length];
        for (int i = 0; i < registrations.Length; i++)
            charts[i] = registrations[i].Chart;

        return charts;
    }

    private static int CompareRegistrationsByRegistrationOrder(
        NavigationChartRegistration left,
        NavigationChartRegistration right)
    {
        return left.RegistrationOrder.CompareTo(right.RegistrationOrder);
    }

    private static void TrackInitializedChartGridTouch(ushort gridIndex, string chartName)
    {
        if (!_initializedChartTouchCountsByGridIndex.TryGetValue(gridIndex, out SwiftDictionary<string, int> chartTouches))
        {
            chartTouches = new SwiftDictionary<string, int>(4, StringComparer.Ordinal);
            _initializedChartTouchCountsByGridIndex[gridIndex] = chartTouches;
        }

        chartTouches.TryGetValue(chartName, out int touchCount);
        chartTouches[chartName] = touchCount + 1;
    }

    private static void UntrackInitializedChartGridTouch(ushort gridIndex, string chartName)
    {
        if (!_initializedChartTouchCountsByGridIndex.TryGetValue(gridIndex, out SwiftDictionary<string, int> chartTouches)
            || !chartTouches.TryGetValue(chartName, out int touchCount))
        {
            return;
        }

        if (touchCount <= 1)
            chartTouches.Remove(chartName);
        else
            chartTouches[chartName] = touchCount - 1;

        if (chartTouches.Count == 0)
            _initializedChartTouchCountsByGridIndex.Remove(gridIndex);
    }

    private static void TrackInitializedChartGridTouchDelta(
        ushort gridIndex,
        string chartName,
        NavigationChartCell previousCell,
        NavigationChartCell currentCell)
    {
        if (previousCell.HasTraversalData == currentCell.HasTraversalData)
            return;

        if (previousCell.HasTraversalData)
            UntrackInitializedChartGridTouch(gridIndex, chartName);

        if (currentCell.HasTraversalData)
            TrackInitializedChartGridTouch(gridIndex, chartName);
    }

    private static void RemoveChartFromRegistry(string chartName)
    {
        _navigationChartMapLock.EnterWriteLock();
        try { _navigationChartMap.Remove(chartName); }
        finally { _navigationChartMapLock.ExitWriteLock(); }
    }

    private static bool TryGetResolvedChartVoxelState(
        GridWorld world,
        Vector3d worldPosition,
        out WorldVoxelIndex voxelIndex,
        out ResolvedChartVoxelState? state)
    {
        if (world.TryGetVoxel(worldPosition, out Voxel? voxel))
        {
            voxelIndex = voxel!.WorldIndex;
            return TryGetResolvedChartVoxelState(voxelIndex, out state);
        }

        voxelIndex = default;
        state = null;
        return false;
    }

    private static bool TryGetResolvedChartVoxelState(
        WorldVoxelIndex voxelIndex,
        out ResolvedChartVoxelState? state)
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
        GridWorld world,
        NavigationChartRegistration registration,
        int x,
        int y,
        int z,
        NavigationChartCell cell,
        SwiftHashSet<SolidChartPartition> partitionsToRebind,
        SwiftHashSet<string> invalidatedChartKeys)
    {
        NavigationChart chart = registration.Chart;
        if (!chart.TrySetCell(x, y, z, cell, out NavigationChartCell previousCell))
            return false;

        if (!registration.IsInitialized)
            return true;

        if (!TryGetChartUpdateVoxelContext(
            world,
            chart,
            x,
            y,
            z,
            out Voxel? voxel,
            out ResolvedChartVoxelState? state,
            out NavigationChartCell previousEffectiveCell,
            out string? previousEffectiveOwner))
        {
            return true;
        }

        TryUpdateResolvedVoxelStateForChartCell(registration, cell, voxel!.WorldIndex, ref state);
        TrackInitializedChartGridTouchDelta(voxel.GridIndex, chart.Name, previousCell, cell);

        ApplyResolvedVoxelState(world, voxel, state, previousEffectiveCell, partitionsToRebind);
        CollectEffectiveStateInvalidations(
            previousEffectiveOwner,
            previousEffectiveCell,
            state?.EffectiveChartOwner ?? string.Empty,
            state?.EffectiveCell ?? NavigationChartCell.Empty,
            invalidatedChartKeys);
        return true;
    }

    private static bool TryGetChartUpdateVoxelContext(
        GridWorld world,
        NavigationChart chart,
        int x,
        int y,
        int z,
        out Voxel? voxel,
        out ResolvedChartVoxelState? state,
        out NavigationChartCell previousEffectiveCell,
        out string? previousEffectiveOwner)
    {
        state = null;
        previousEffectiveCell = NavigationChartCell.Empty;
        previousEffectiveOwner = null;

        Vector3d position = chart.GetWorldPosition(x, y, z);
        if (!world.TryGetVoxel(position, out voxel))
            return false;

        _resolvedChartVoxelStates.TryGetValue(voxel!.WorldIndex, out state);
        if (state != null && state.HasAnyOwners)
        {
            previousEffectiveCell = state.EffectiveCell;
            previousEffectiveOwner = state.EffectiveChartOwner;
        }

        return true;
    }

    private static void TryUpdateResolvedVoxelStateForChartCell(
        NavigationChartRegistration registration,
        NavigationChartCell cell,
        WorldVoxelIndex voxelIndex,
        ref ResolvedChartVoxelState? state)
    {
        NavigationChart chart = registration.Chart;
        if (cell.HasTraversalData)
        {
            state ??= new ResolvedChartVoxelState();
            state.AddOwner(chart.Name, cell, chart.Priority, registration.RegistrationOrder);
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
        BindCollectedSolidPartitions(partitionsToRebind);

    }

    private static void CollectEffectiveStateInvalidations(
        string? previousEffectiveOwner,
        NavigationChartCell previousEffectiveCell,
        string? currentEffectiveOwner,
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
        return HasAuthoredVolumeMedium(ActiveState, medium);
    }

    internal static bool HasAuthoredVolumeMedium(PathingWorldState state, TraversalMedium medium)
    {
        return medium switch
        {
            TraversalMedium.Gas => state.ActiveAuthoredGasCellCount > 0,
            TraversalMedium.Liquid => state.ActiveAuthoredLiquidCellCount > 0,
            _ => false
        };
    }

    private static void ApplyResolvedVoxelState(
        GridWorld world,
        Voxel voxel,
        ResolvedChartVoxelState? state,
        NavigationChartCell previousEffectiveCell,
        SwiftHashSet<SolidChartPartition> partitionsToRebind)
    {
        NavigationChartCell effectiveCell = state?.EffectiveCell ?? NavigationChartCell.Empty;
        UpdateActiveVolumeMediumCounts(previousEffectiveCell, effectiveCell);

        bool solidPresenceChanged = previousEffectiveCell.HasSolid != effectiveCell.HasSolid;

        if (effectiveCell.HasSolid)
        {
            if (!voxel.TryGetPartition(out SolidChartPartition? solidPartition))
            {
                solidPartition = ActiveState.PartitionPool.Rent();
                solidPartition.SetOwner(ActiveState);
                voxel.TryAddPartition(solidPartition);
            }
            else if (solidPartition!.OwnerState == null)
                solidPartition.SetOwner(ActiveState);

            solidPartition!.ApplyAuthoredState(state, state?.EffectiveChartOwner, effectiveCell);
            if (solidPresenceChanged)
                CollectSolidPartitionsForRebind(world, voxel, partitionsToRebind);
        }
        else if (previousEffectiveCell.HasSolid && voxel.TryGetPartition<SolidChartPartition>(out _))
        {
            voxel.TryRemovePartition<SolidChartPartition>();
            CollectSolidPartitionsForRebind(world, voxel, partitionsToRebind);
        }

        if (effectiveCell.HasVolume)
        {
            if (!voxel.TryGetPartition(out VolumeChartPartition? volumePartition))
            {
                volumePartition = ActiveState.VolumeChartPartitionPool.Rent();
                volumePartition.SetOwner(ActiveState);
                voxel.TryAddPartition(volumePartition);
            }
            else if (volumePartition!.OwnerState == null)
                volumePartition.SetOwner(ActiveState);

            volumePartition!.ApplyAuthoredState(state, state?.EffectiveChartOwner, effectiveCell);
        }
        else if (previousEffectiveCell.HasVolume && voxel.TryGetPartition<VolumeChartPartition>(out _))
            voxel.TryRemovePartition<VolumeChartPartition>();
    }

    private static void UpdateActiveVolumeMediumCounts(
        NavigationChartCell previousEffectiveCell,
        NavigationChartCell currentEffectiveCell)
    {
        if (previousEffectiveCell.SupportsMedium(TraversalMedium.Gas))
            AdjustActiveAuthoredGasCellCount(-1);

        if (previousEffectiveCell.SupportsMedium(TraversalMedium.Liquid))
            AdjustActiveAuthoredLiquidCellCount(-1);

        if (currentEffectiveCell.SupportsMedium(TraversalMedium.Gas))
            AdjustActiveAuthoredGasCellCount(1);

        if (currentEffectiveCell.SupportsMedium(TraversalMedium.Liquid))
            AdjustActiveAuthoredLiquidCellCount(1);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ClearActiveAuthoredVolumeMediumCounts()
    {
        PathingWorldState state = ActiveState;
        state.ActiveAuthoredGasCellCount = 0;
        state.ActiveAuthoredLiquidCellCount = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AdjustActiveAuthoredGasCellCount(int delta)
    {
        ActiveState.ActiveAuthoredGasCellCount += delta;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AdjustActiveAuthoredLiquidCellCount(int delta)
    {
        ActiveState.ActiveAuthoredLiquidCellCount += delta;
    }

    private static void CollectSolidPartitionsForRebind(
        GridWorld world,
        Voxel voxel,
        SwiftHashSet<SolidChartPartition> partitionsToRebind)
    {
        if (!world.TryGetGrid(voxel.WorldIndex, out VoxelGrid? grid))
            return;

        if (voxel.TryGetPartition(out SolidChartPartition? currentPartition))
            partitionsToRebind.Add(currentPartition!);

        SwiftList<Voxel> contactNeighbors = SwiftListPool<Voxel>.Shared.Rent();
        try
        {
            voxel.GetNeighborsInto(
                grid!,
                contactNeighbors,
                VoxelNeighborScope.SourceGrid | VoxelNeighborScope.SameTopologyGrids);

            for (int i = 0; i < contactNeighbors.Count; i++)
            {
                if (contactNeighbors[i].TryGetPartition(out SolidChartPartition? neighborPartition))
                    partitionsToRebind.Add(neighborPartition!);
            }
        }
        finally
        {
            SwiftListPool<Voxel>.Shared.Release(contactNeighbors);
        }
    }

    private static void BindCollectedSolidPartitions(SwiftHashSet<SolidChartPartition> partitionsToRebind)
    {
        PathingWorldState activeState = ActiveState;
        foreach (SolidChartPartition partition in partitionsToRebind)
        {
            if (partition.IsPartitioned && ReferenceEquals(partition.OwnerState, activeState))
                partition.BindNeighbors();
        }
    }

    #endregion

    #region Public Utility Methods

    /// <summary>
    /// Determines the maximum number of voxels to search based on the start and end voxel's grid sizes.
    /// </summary>
    /// <param name="world">The grid world.</param>
    /// <param name="start">The start voxel.</param>
    /// <param name="end">The end voxel.</param>
    /// <param name="maxSearchSize">The output max search size.</param>
    /// <returns>True if both voxels belong to valid grids; otherwise, false.</returns>
    public static bool TryGetMaxSearchSize(GridWorld world, Voxel start, Voxel end, out int maxSearchSize)
    {
        LinkWorld(world);
        TrailblazerGridCompatibility.ValidateWorld(world);
        if (!world.TryGetGrid(start.WorldIndex, out VoxelGrid? startGrid)
            || !world.TryGetGrid(end.WorldIndex, out VoxelGrid? endGrid))
        {
            maxSearchSize = 0;
            return false;
        }

        maxSearchSize = startGrid == endGrid
            ? startGrid!.ConfiguredVoxelCount
            : startGrid!.ConfiguredVoxelCount + endGrid!.ConfiguredVoxelCount;
        return true;
    }

    /// <summary>
    /// Determines the maximum number of voxels to search based on the start and end voxel's grid sizes using the configured world.
    /// </summary>
    public static bool TryGetMaxSearchSize(Voxel start, Voxel end, out int maxSearchSize)
    {
        return TryGetMaxSearchSize(GetConfiguredWorld(), start, end, out maxSearchSize);
    }

    #endregion
}

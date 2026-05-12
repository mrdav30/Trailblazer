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
    [ThreadStatic]
    private static PathingWorldState? _activeState;

    #region Pools

    internal static readonly SwiftHashSetPool<SolidChartPartition> PartitionSetPool = new();

    /// <summary>
    /// Pool of reusable <see cref="SolidChartPartition"/> instances used for partitioning the navigation grid.
    /// </summary>
    internal static SwiftObjectPool<SolidChartPartition> PartitionPool => ActiveState.PartitionPool;

    /// <summary>
    /// Pool of reusable <see cref="VolumeChartPartition"/> instances used for authored raw-volume traversal.
    /// </summary>
    internal static SwiftObjectPool<VolumeChartPartition> VolumeChartPartitionPool => ActiveState.VolumeChartPartitionPool;

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

    private static int _activeAuthoredGasCellCount
    {
        get => ActiveState.ActiveAuthoredGasCellCount;
        set => ActiveState.ActiveAuthoredGasCellCount = value;
    }

    private static int _activeAuthoredLiquidCellCount
    {
        get => ActiveState.ActiveAuthoredLiquidCellCount;
        set => ActiveState.ActiveAuthoredLiquidCellCount = value;
    }

    private static int _nextChartRegistrationOrder
    {
        get => ActiveState.NextChartRegistrationOrder;
        set => ActiveState.NextChartRegistrationOrder = value;
    }

    internal static PathingWorldState ActiveState => _activeState ?? GetDefaultState();

    internal static bool TryGetActiveState(out PathingWorldState? state)
    {
        if (_activeState != null)
        {
            state = _activeState;
            return true;
        }

        if (TrailblazerManager.HasDefaultContext)
        {
            state = TrailblazerManager.DefaultContext.Pathing.State;
            return true;
        }

        if (TrailblazerWorldManager.IsActive)
        {
            TrailblazerManager.Initialize(TrailblazerWorldManager.World);
            state = TrailblazerManager.DefaultContext.Pathing.State;
            return true;
        }

        state = null;
        return false;
    }

    internal static IDisposable EnterState(PathingWorldState state)
    {
        return new PathingWorldStateScope(state);
    }

    private static PathingWorldState GetDefaultState()
    {
        if (TrailblazerManager.HasDefaultContext)
            return TrailblazerManager.DefaultContext.Pathing.State;

        if (TrailblazerWorldManager.IsActive)
        {
            TrailblazerManager.Initialize(TrailblazerWorldManager.World);
            return TrailblazerManager.DefaultContext.Pathing.State;
        }

        throw new InvalidOperationException(
            "Trailblazer requires an active pathing context. Create a TrailblazerWorldContext and use its Pathing service, " +
            "or initialize the default facade with TrailblazerManager.Initialize(world).");
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
    public static bool HasConfiguredWorld => TrailblazerManager.HasDefaultContext || TrailblazerWorldManager.IsActive;

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

        if (TrailblazerManager.HasDefaultContext)
        {
            if (!ReferenceEquals(TrailblazerManager.DefaultContext.World, world))
            {
                throw new InvalidOperationException(
                    "PathManager GridWorld overloads are default-context compatibility APIs. " +
                    "Use TrailblazerWorldContext.Pathing for independent multi-world pathing state.");
            }

            return;
        }

        TrailblazerManager.Initialize(world);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static GridWorld GetConfiguredWorld() => ActiveState.World;

    internal static void RegisterTrailblazerLifecycleHooks()
    {
        TrailblazerManager.RegisterOnSimulateCore(
            owner: "PathManager.Tick",
            order: TrailblazerLifecycleOrder.PathingMaintenance,
            callback: Tick);
    }

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
            if (resetScopedRegistries)
            {
                VolumeMediumRules.Reset();
                TraversalTransitionRegistry.Reset();
            }

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
            _activeAuthoredGasCellCount = 0;
            _activeAuthoredLiquidCellCount = 0;

            if (flushGuideCache && PathGuideFactory.IsPooling)
                PathGuideFactory.FlushCache(true);

            SolidPartitionReachability.Invalidate();
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
    public static bool Register(GridWorld world, NavigationChart chart, bool initializeChart = true)
    {
        SwiftThrowHelper.ThrowIfNull(chart, nameof(chart));
        ThrowIfDirectWorldRegisterCall();
        LinkWorld(world);

        return RegisterChartInternal(
            world,
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
        PathingWorldState state = ActiveState;
        using (EnterState(state))
            return Register(state.World, buildResult, initializeChart);
    }

    /// <summary>
    /// Attempts to register the chart and generated transitions produced by a traversal authoring build.
    /// </summary>
    /// <param name="world">The grid world context for the chart.</param>
    /// <param name="buildResult">The build result to register.</param>
    /// <param name="initializeChart">Whether to initialize the built chart after registration succeeds.</param>
    /// <returns>True when the chart and all generated transitions are registered successfully; otherwise, false.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="buildResult"/> is null.</exception>
    public static bool Register(GridWorld world, TraversalBuildResult buildResult, bool initializeChart = true)
    {
        SwiftThrowHelper.ThrowIfNull(buildResult, nameof(buildResult));
        ThrowIfDirectWorldRegisterCall();
        LinkWorld(world);

        return RegisterChartInternal(
            world,
            buildResult.Chart,
            buildResult.GeneratedTransitionIdPrefix,
            buildResult.GeneratedTransitions,
            initializeChart);
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
        string generatedTransitionIdPrefix,
        TraversalTransition[]? precomputedGeneratedTransitions,
        bool initializeChart)
    {
        _navigationChartMapLock.EnterWriteLock();
        try
        {
            if (_navigationChartMap.ContainsKey(chart.Name))
                return false;

            var registration = new NavigationChartRegistration(
                chart,
                unchecked(++_nextChartRegistrationOrder),
                generatedTransitionIdPrefix);
            _navigationChartMap.Add(chart.Name, registration);
        }
        finally { _navigationChartMapLock.ExitWriteLock(); }

        if (!TryRegisterManagedGeneratedTransitions(chart.Name, precomputedGeneratedTransitions))
        {
            RemoveChartFromRegistry(chart.Name);
            return false;
        }

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
    /// Attempts to retrieve the closest currently active directed transition of the requested type.
    /// </summary>
    /// <param name="world">The grid world context to search.</param>
    /// <param name="worldPosition">The position to measure from.</param>
    /// <param name="transitionType">The directed handoff family to search.</param>
    /// <param name="transition">
    /// The closest active directed transition. Bidirectional registrations may return the reversed
    /// directed view when that source anchor is closer.
    /// </param>
    /// <returns>True when at least one active directed transition of that type exists; otherwise, false.</returns>
    public static bool TryGetClosestActiveTransition(
        GridWorld world,
        Vector3d worldPosition,
        TraversalTransitionType transitionType,
        out TraversalTransition transition)
    {
        LinkWorld(world);
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

        if (world.TryGetGrid(worldPosition, out VoxelGrid? originGrid))
        {
            originGridIndex = originGrid!.GridIndex;
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
                || !world.TryGetGrid(sourceGridIndex, out VoxelGrid? sourceGrid)
                || (found && GetBoundsDistanceSq(worldPosition, sourceGrid!.BoundsMin, sourceGrid.BoundsMax) >= closestDistanceSq))
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
    /// Attempts to retrieve the closest currently active directed transition of the requested type using the configured world.
    /// </summary>
    public static bool TryGetClosestActiveTransition(
        Vector3d worldPosition,
        TraversalTransitionType transitionType,
        out TraversalTransition transition)
    {
        return TryGetClosestActiveTransition(GetConfiguredWorld(), worldPosition, transitionType, out transition);
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
        SwiftHashSet<string> managedChartsToRefresh = SwiftHashSetPool<string>.Shared.Rent();
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
                invalidatedChartKeys,
                managedChartsToRefresh);

            if (changed)
                RefreshManagedTransitionsForVoxel(
                    world,
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
        SwiftHashSet<string> managedChartsToRefresh = SwiftHashSetPool<string>.Shared.Rent();
        try
        {
            int changedCount = 0;
            for (int i = 0; i < updates.Count; i++)
            {
                managedChartsToRefresh.Clear();
                NavigationChartCellUpdate update = updates[i];
                if (TryApplyChartCellUpdate(
                    world,
                    registration,
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
                        world,
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
        SwiftHashSet<string> affectedChartKeys = SwiftHashSetPool<string>.Shared.Rent();
        SwiftHashSet<WorldVoxelIndex> touchedVoxelIndices = SwiftHashSetPool<WorldVoxelIndex>.Shared.Rent();
        try
        {
            foreach ((Vector3d pos, NavigationChartCell cell) in chart.GetAuthoredCells())
            {
                if (!world.TryGetVoxel(pos, out Voxel? voxel))
                    continue;

                touchedVoxelIndices.Add(voxel!.WorldIndex);

                if (!_resolvedChartVoxelStates.TryGetValue(voxel.WorldIndex, out ResolvedChartVoxelState state))
                {
                    state = new ResolvedChartVoxelState();
                    _resolvedChartVoxelStates[voxel.WorldIndex] = state;
                }
                else if (state.HasAnyOwners)
                    state.AddChartOwnersTo(affectedChartKeys);

                NavigationChartCell previousEffectiveCell = state.EffectiveCell;
                state.AddOwner(chart.Name, cell, chart.Priority, registration.RegistrationOrder);
                ApplyResolvedVoxelState(world, voxel, state, previousEffectiveCell, partitionsToRebind);
                TrackInitializedChartGridTouch(voxel.GridIndex, chart.Name);
            }

            foreach (SolidChartPartition part in partitionsToRebind)
                part.BindNeighbors();

            registration.IsInitialized = true;
            affectedChartKeys.Add(chart.Name);
            SolidPartitionReachability.Invalidate();

            RefreshManagedManualTransitionsForVoxels(touchedVoxelIndices);
            RefreshManagedGeneratedTransitionsForCharts(world, affectedChartKeys);

            foreach (string affectedChartKey in affectedChartKeys)
                PathGuideFactory.InvalidateCacheFor(affectedChartKey);
        }
        finally
        {
            PartitionSetPool.Release(partitionsToRebind);
            SwiftHashSetPool<string>.Shared.Release(affectedChartKeys);
            SwiftHashSetPool<WorldVoxelIndex>.Shared.Release(touchedVoxelIndices);
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
        string[] generatedTransitionIds = RemoveManagedGeneratedTransitions(chart.Name);

        if (!registration.IsInitialized)
        {
            RemoveChartFromRegistry(chart.Name);
            TraversalTransitionRegistry.UnregisterRange(generatedTransitionIds);
            return;
        }

        // invalidate any survey results currently using this chart
        PathGuideFactory.InvalidateCacheFor(chart.Name);

        SwiftHashSet<SolidChartPartition> partitionsToRebind = PartitionSetPool.Rent();
        SwiftHashSet<string> affectedChartKeys = SwiftHashSetPool<string>.Shared.Rent();
        SwiftHashSet<WorldVoxelIndex> touchedVoxelIndices = SwiftHashSetPool<WorldVoxelIndex>.Shared.Rent();
        try
        {
            affectedChartKeys.Add(chart.Name);
            foreach ((Vector3d position, _) in chart.GetAuthoredCells())
            {
                if (!world.TryGetVoxel(position, out Voxel? voxel))
                    continue;

                touchedVoxelIndices.Add(voxel!.WorldIndex);

                if (!_resolvedChartVoxelStates.TryGetValue(voxel.WorldIndex, out ResolvedChartVoxelState state)
                    || !state.ContainsOwner(chart.Name))
                {
                    continue;
                }

                state.AddChartOwnersTo(affectedChartKeys);

                NavigationChartCell previousEffectiveCell = state.EffectiveCell;
                state.RemoveOwner(chart.Name);
                ApplyResolvedVoxelState(world, voxel, state, previousEffectiveCell, partitionsToRebind);
                UntrackInitializedChartGridTouch(voxel.GridIndex, chart.Name);

                if (!state.HasAnyOwners)
                    _resolvedChartVoxelStates.Remove(voxel.WorldIndex);
            }

            foreach (SolidChartPartition part in partitionsToRebind)
                part.BindNeighbors();

            TraversalTransitionRegistry.UnregisterRange(generatedTransitionIds);
            registration.IsInitialized = false;
            RemoveChartFromRegistry(chart.Name);
            SolidPartitionReachability.Invalidate();

            RefreshManagedManualTransitionsForVoxels(touchedVoxelIndices);
            RefreshManagedGeneratedTransitionsForCharts(world, affectedChartKeys, chart.Name);

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
            SwiftHashSetPool<WorldVoxelIndex>.Shared.Release(touchedVoxelIndices);
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
        if (initializedCharts.Length == 0)
            return 0;

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
        SuppressManagedGeneratedTransitionsForCharts(chartsToRebuild);

        for (int i = 0; i < chartsToRebuild.Length; i++)
            ClearInitializedChartLiveStatePreservingRegistration(world, chartsToRebuild[i]);

        for (int i = 0; i < chartsToRebuild.Length; i++)
            InitializeChart(world, chartsToRebuild[i].Name);

        RefreshManagedGeneratedTransitionsForCharts(world, GetInitializedChartsSnapshot());
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
                if (!TryGetNavigationChartRegistration_NoLock(chart.Name, out NavigationChartRegistration registration)
                    || registration.TransitionIds.Count == 0)
                {
                    continue;
                }

                foreach (string transitionId in registration.TransitionIds)
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
        PathGuideFactory.InvalidateCacheFor(chart.Name);

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

            foreach (SolidChartPartition part in partitionsToRebind)
                part.BindNeighbors();

            registration.IsInitialized = false;
            SolidPartitionReachability.Invalidate();
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
        _activeAuthoredGasCellCount = 0;
        _activeAuthoredLiquidCellCount = 0;
        SolidPartitionReachability.Invalidate();
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsPositionInsideBounds(
        Vector3d position,
        Vector3d boundsMin,
        Vector3d boundsMax)
    {
        return position.x >= boundsMin.x
            && position.x <= boundsMax.x
            && position.y >= boundsMin.y
            && position.y <= boundsMax.y
            && position.z >= boundsMin.z
            && position.z <= boundsMax.z;
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

    private static readonly (int Dx, int Dy, int Dz)[] ManagedGeneratedNeighborOffsets =
    {
        (1, 0, 0),
        (-1, 0, 0),
        (0, 1, 0),
        (0, -1, 0),
        (0, 0, 1),
        (0, 0, -1)
    };

    private static bool TryRegisterManagedGeneratedTransitions(
        string chartName,
        TraversalTransition[]? precomputedGeneratedTransitions)
    {
        if (!TryGetNavigationChartRegistration(chartName, out NavigationChartRegistration registration))
            return false;

        NavigationChart chart = registration.Chart;
        TraversalTransition[] generatedTransitions = precomputedGeneratedTransitions
            ?? GeneratedTraversalTransitionBuilder.BuildTransitions(chart, registration.TransitionIdPrefix);
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

        RememberManagedGeneratedTransitions(chart.Name, registeredTransitionIds, transitionCount);
        return true;
    }

    private static void RememberManagedGeneratedTransitions(
        string chartName,
        string[] transitionIds,
        int transitionCount)
    {
        _navigationChartMapLock.EnterWriteLock();
        try
        {
            if (!TryGetNavigationChartRegistration_NoLock(chartName, out NavigationChartRegistration registration))
                return;

            registration.TransitionIds.Clear();
            for (int i = 0; i < transitionCount; i++)
                registration.TransitionIds.Add(transitionIds[i]);
        }
        finally { _navigationChartMapLock.ExitWriteLock(); }
    }

    private static string[] RemoveManagedGeneratedTransitions(string chartName)
    {
        _navigationChartMapLock.EnterWriteLock();
        try
        {
            if (!TryGetNavigationChartRegistration_NoLock(chartName, out NavigationChartRegistration registration))
                return Array.Empty<string>();

            string[] transitionIds = CopyTransitionIds(registration.TransitionIds);
            registration.TransitionIds.Clear();
            return transitionIds;
        }
        finally { _navigationChartMapLock.ExitWriteLock(); }
    }

    private static bool TryGetManagedGeneratedTransitionState(
        string chartName,
        out NavigationChartRegistration state)
    {
        _navigationChartMapLock.EnterReadLock();
        try { return TryGetNavigationChartRegistration_NoLock(chartName, out state); }
        finally { _navigationChartMapLock.ExitReadLock(); }
    }

    private static void RefreshManagedGeneratedTransitionsForCharts(
        GridWorld world,
        SwiftHashSet<string> chartNames,
        string? excludedChartName = null)
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

            RefreshManagedGeneratedTransitionsForChart(world, chartName);
        }
    }

    private static void RefreshManagedGeneratedTransitionsForCharts(GridWorld world, NavigationChart[] charts)
    {
        SwiftHashSet<string> chartNames = SwiftHashSetPool<string>.Shared.Rent();
        try
        {
            for (int i = 0; i < charts.Length; i++)
                chartNames.Add(charts[i].Name);

            RefreshManagedGeneratedTransitionsForCharts(world, chartNames);
        }
        finally
        {
            SwiftHashSetPool<string>.Shared.Release(chartNames);
        }
    }

    private static void RefreshManagedTransitionsForVoxel(
        GridWorld world,
        Vector3d worldPosition,
        SwiftHashSet<string> chartNames)
    {
        if (world.TryGetVoxel(worldPosition, out Voxel? voxel))
            TraversalTransitionRegistry.RefreshManagedManualTransitionsForVoxel(voxel!.WorldIndex);

        RefreshManagedGeneratedTransitionsForVoxel(world, worldPosition, chartNames);
    }

    private static void RefreshManagedManualTransitionsForVoxels(SwiftHashSet<WorldVoxelIndex> voxelIndices)
    {
        if (voxelIndices == null || voxelIndices.Count == 0)
            return;

        foreach (WorldVoxelIndex voxelIndex in voxelIndices)
            TraversalTransitionRegistry.RefreshManagedManualTransitionsForVoxel(voxelIndex);
    }

    private static void RefreshManagedGeneratedTransitionsForChart(GridWorld world, string chartName)
    {
        if (!TryGetNavigationChart(chartName, out NavigationChart chart)
            || !TryGetManagedGeneratedTransitionState(chartName, out NavigationChartRegistration state))
        {
            return;
        }

        SwiftHashSet<string> desiredTransitionIds = SwiftHashSetPool<string>.Shared.Rent();
        SwiftHashSet<string> activeTransitionIds = SwiftHashSetPool<string>.Shared.Rent();
        try
        {
            TraversalTransition[] missingTransitions = CollectManagedGeneratedTransitionsForChart(
                world,
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
        GridWorld world,
        NavigationChart chart,
        NavigationChartRegistration state,
        SwiftHashSet<string> desiredTransitionIds,
        SwiftHashSet<string> activeTransitionIds)
    {
        SwiftList<TraversalTransition> missingTransitions = new();
        int[] generatedIndices = chart.GetGeneratedTransitionIndices();
        for (int i = 0; i < generatedIndices.Length; i++)
        {
            chart.DecodeIndex(generatedIndices[i], out int x, out int y, out int z);
            for (int neighborOffsetIndex = 0; neighborOffsetIndex < ManagedGeneratedNeighborOffsets.Length; neighborOffsetIndex++)
            {
                (int dx, int dy, int dz) = ManagedGeneratedNeighborOffsets[neighborOffsetIndex];
                int neighborX = x + dx;
                int neighborY = y + dy;
                int neighborZ = z + dz;
                if (!chart.IsInBounds(neighborX, neighborY, neighborZ))
                    continue;

                NavigationChartCell neighborCell = chart.GetCell(neighborX, neighborY, neighborZ);
                if (!ShouldCollectManagedGeneratedPair(
                    x,
                    y,
                    z,
                    neighborX,
                    neighborY,
                    neighborZ,
                    neighborCell))
                {
                    continue;
                }

                CollectManagedGeneratedTransitionsForPair(
                    world,
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
        GridWorld world,
        Vector3d worldPosition,
        SwiftHashSet<string> chartNames)
    {
        if (chartNames == null || chartNames.Count == 0)
            return;

        foreach (string chartName in chartNames)
        {
            if (string.IsNullOrEmpty(chartName)
                || !TryGetNavigationChart(chartName, out NavigationChart chart)
                || !TryGetManagedGeneratedTransitionState(chartName, out NavigationChartRegistration state)
                || !chart.TryWorldToIndex(worldPosition, out int x, out int y, out int z))
            {
                continue;
            }

            RefreshManagedGeneratedTransitionsForVoxel(world, chartName, chart, state, x, y, z);
        }
    }

    private static void RefreshManagedGeneratedTransitionsForVoxel(
        GridWorld world,
        string chartName,
        NavigationChart chart,
        NavigationChartRegistration state,
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
                    world,
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
                    world,
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
        GridWorld world,
        string chartName,
        NavigationChart chart,
        NavigationChartRegistration state,
        int firstX,
        int firstY,
        int firstZ,
        int secondX,
        int secondY,
        int secondZ)
    {
        string[] potentialTransitionIds = GeneratedTraversalTransitionBuilder.GetPotentialTransitionIdsForPair(
            state.TransitionIdPrefix,
            firstX,
            firstY,
            firstZ,
            secondX,
            secondY,
            secondZ);

        if (!CanResolveManagedGeneratedPairAnchors(world, chart, firstX, firstY, firstZ, secondX, secondY, secondZ))
        {
            if (GeneratedTraversalTransitionBuilder.CanBuildTransitionsForPairFromChartData(
                chart,
                firstX,
                firstY,
                firstZ,
                secondX,
                secondY,
                secondZ))
            {
                TraversalTransitionRegistry.SetManagedTransitionsSuppressed(
                    potentialTransitionIds,
                    suppressed: true);
            }
            else
            {
                string[] obsoleteSuppressedTransitionIds = GetObsoleteManagedGeneratedTransitionIds(
                    state,
                    potentialTransitionIds,
                    Array.Empty<TraversalTransition>());
                if (obsoleteSuppressedTransitionIds.Length > 0)
                {
                    TraversalTransitionRegistry.UnregisterRange(obsoleteSuppressedTransitionIds);
                    RemoveManagedGeneratedTransitionIds(chartName, obsoleteSuppressedTransitionIds);
                }
            }

            return;
        }

        TraversalTransition[] desiredTransitions = GeneratedTraversalTransitionBuilder.BuildTransitionsForPair(
            chart,
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
            world,
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
        NavigationChartRegistration state,
        string[] potentialTransitionIds,
        TraversalTransition[] desiredTransitions)
    {
        if (potentialTransitionIds.Length == 0)
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
        NavigationChartRegistration state,
        TraversalTransition[] desiredTransitions)
    {
        if (desiredTransitions.Length == 0)
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
        NavigationChartRegistration state,
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
        NavigationChartRegistration state,
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
        NavigationChartRegistration state,
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
        GridWorld world,
        NavigationChart chart,
        NavigationChartRegistration state,
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
        if (!CanResolveManagedGeneratedPairAnchors(world, chart, firstX, firstY, firstZ, secondX, secondY, secondZ))
        {
            if (GeneratedTraversalTransitionBuilder.CanBuildTransitionsForPairFromChartData(
                chart,
                firstX,
                firstY,
                firstZ,
                secondX,
                secondY,
                secondZ))
            {
                AddPotentialManagedGeneratedTransitionIds(
                    state.TransitionIdPrefix,
                    firstX,
                    firstY,
                    firstZ,
                    secondX,
                    secondY,
                    secondZ,
                    desiredTransitionIds);
            }

            return;
        }

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
            world,
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

    private static bool CanResolveManagedGeneratedPairAnchors(
        GridWorld world,
        NavigationChart chart,
        int firstX,
        int firstY,
        int firstZ,
        int secondX,
        int secondY,
        int secondZ)
    {
        return world.TryGetVoxel(chart.GetWorldPosition(firstX, firstY, firstZ), out _)
            && world.TryGetVoxel(chart.GetWorldPosition(secondX, secondY, secondZ), out _);
    }

    private static void AddPotentialManagedGeneratedTransitionIds(
        string transitionIdPrefix,
        int firstX,
        int firstY,
        int firstZ,
        int secondX,
        int secondY,
        int secondZ,
        SwiftHashSet<string> desiredTransitionIds)
    {
        string[] potentialTransitionIds = GeneratedTraversalTransitionBuilder.GetPotentialTransitionIdsForPair(
            transitionIdPrefix,
            firstX,
            firstY,
            firstZ,
            secondX,
            secondY,
            secondZ);

        for (int i = 0; i < potentialTransitionIds.Length; i++)
            desiredTransitionIds.Add(potentialTransitionIds[i]);
    }

    private static bool ShouldCollectManagedGeneratedPair(
        int firstX,
        int firstY,
        int firstZ,
        int secondX,
        int secondY,
        int secondZ,
        NavigationChartCell secondCell)
    {
        if (!IsManagedGeneratedTransitionCandidate(secondCell))
            return true;

        return firstX < secondX
            || (firstX == secondX && firstY < secondY)
            || (firstX == secondX && firstY == secondY && firstZ < secondZ);
    }

    private static bool IsManagedGeneratedTransitionCandidate(NavigationChartCell cell)
    {
        return cell.CanGenerateTransition
            || (cell.Flags & NavigationChartCellFlags.ClimbSurfaceHint) != 0;
    }

    private static bool IsManagedGeneratedPairActive(
        GridWorld world,
        string chartName,
        NavigationChart chart,
        int firstX,
        int firstY,
        int firstZ,
        int secondX,
        int secondY,
        int secondZ)
    {
        if (!IsChartInitialized(chartName))
            return false;

        return IsChartEffectiveOwnerAtPosition(world, chartName, chart.GetWorldPosition(firstX, firstY, firstZ))
            && IsChartEffectiveOwnerAtPosition(world, chartName, chart.GetWorldPosition(secondX, secondY, secondZ));
    }

    private static bool IsChartEffectiveOwnerAtPosition(GridWorld world, string chartName, Vector3d worldPosition)
    {
        if (!TryGetResolvedChartVoxelState(world, worldPosition, out _, out ResolvedChartVoxelState? state))
            return false;

        return string.Equals(state!.EffectiveChartOwner, chartName, StringComparison.Ordinal);
    }

    private static void AddManagedGeneratedTransitionIds(
        string chartName,
        TraversalTransition[] transitions)
    {
        _navigationChartMapLock.EnterWriteLock();
        try
        {
            if (!TryGetNavigationChartRegistration_NoLock(chartName, out NavigationChartRegistration state))
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
        _navigationChartMapLock.EnterWriteLock();
        try
        {
            if (!TryGetNavigationChartRegistration_NoLock(chartName, out NavigationChartRegistration state))
                return;

            for (int i = 0; i < transitionIds.Length; i++)
                state.TransitionIds.Remove(transitionIds[i]);
        }
        finally { _navigationChartMapLock.ExitWriteLock(); }
    }

    private static string[] CopyTransitionIds(SwiftHashSet<string> transitionIds)
    {
        if (transitionIds.Count == 0)
            return Array.Empty<string>();

        string[] copy = new string[transitionIds.Count];
        int index = 0;
        foreach (string transitionId in transitionIds)
            copy[index++] = transitionId;

        return copy;
    }

    private static string[] CopyTransitionIds(TraversalTransition[] transitions)
    {
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
        SwiftHashSet<string> invalidatedChartKeys,
        SwiftHashSet<string> managedChartsToRefresh)
    {
        NavigationChart chart = registration.Chart;
        if (!chart.TrySetCell(x, y, z, cell, out NavigationChartCell previousCell))
            return false;

        TrackManagedChartRefresh(chart, managedChartsToRefresh);

        if (!registration.IsInitialized)
            return true;

        if (!TryGetChartUpdateVoxelContext(
            world,
            chart,
            x,
            y,
            z,
            managedChartsToRefresh,
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
        if (state != null && state.HasAnyOwners)
            state.AddChartOwnersTo(managedChartsToRefresh);

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void TrackManagedChartRefresh(
        NavigationChart chart,
        SwiftHashSet<string> managedChartsToRefresh)
    {
        managedChartsToRefresh.Add(chart.Name);
    }

    private static bool TryGetChartUpdateVoxelContext(
        GridWorld world,
        NavigationChart chart,
        int x,
        int y,
        int z,
        SwiftHashSet<string> managedChartsToRefresh,
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
            state.AddChartOwnersTo(managedChartsToRefresh);
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
        foreach (SolidChartPartition part in partitionsToRebind)
            part.BindNeighbors();

        if (partitionsToRebind.Count > 0 || invalidatedChartKeys.Count > 0)
            SolidPartitionReachability.Invalidate();

        foreach (string chartKey in invalidatedChartKeys)
            PathGuideFactory.InvalidateCacheFor(chartKey);
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
                solidPartition = PartitionPool.Rent();
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
                volumePartition = VolumeChartPartitionPool.Rent();
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
            _activeAuthoredGasCellCount--;

        if (previousEffectiveCell.SupportsMedium(TraversalMedium.Liquid))
            _activeAuthoredLiquidCellCount--;

        if (currentEffectiveCell.SupportsMedium(TraversalMedium.Gas))
            _activeAuthoredGasCellCount++;

        if (currentEffectiveCell.SupportsMedium(TraversalMedium.Liquid))
            _activeAuthoredLiquidCellCount++;
    }

    private static void CollectSolidPartitionsForRebind(
        GridWorld world,
        Voxel voxel,
        SwiftHashSet<SolidChartPartition> partitionsToRebind)
    {
        if (!world.TryGetGrid(voxel.WorldIndex.GridIndex, out VoxelGrid? grid))
            return;

        if (voxel.TryGetPartition(out SolidChartPartition? currentPartition))
            partitionsToRebind.Add(currentPartition!);

        foreach (SpatialDirection direction in SpatialAwareness.AllDirections)
        {
            if (voxel.TryGetNeighborFromDirection(grid!, direction, out Voxel? neighborVoxel, useCache: true)
                && neighborVoxel!.TryGetPartition(out SolidChartPartition? neighborPartition))
            {
                partitionsToRebind.Add(neighborPartition!);
            }
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
        if (!world.TryGetGrid(start.WorldIndex.GridIndex, out VoxelGrid? startGrid)
            || !world.TryGetGrid(end.WorldIndex.GridIndex, out VoxelGrid? endGrid))
        {
            maxSearchSize = 0;
            return false;
        }

        maxSearchSize = startGrid == endGrid
            ? startGrid!.Size
            : startGrid!.Size + endGrid!.Size;
        return true;
    }

    /// <summary>
    /// Determines the maximum number of voxels to search based on the start and end voxel's grid sizes using the configured world.
    /// </summary>
    public static bool TryGetMaxSearchSize(Voxel start, Voxel end, out int maxSearchSize)
    {
        return TryGetMaxSearchSize(GetConfiguredWorld(), start, end, out maxSearchSize);
    }

    /// <summary>
    /// Checks if a path is needed between the start and end positions based on traced voxels and unit size.
    /// </summary>
    /// <param name="world">The grid world.</param>
    /// <param name="startPos">The starting position.</param>
    /// <param name="endPos">The destination position.</param>
    /// <param name="unitSize">The size of the navigating unit.</param>
    /// <param name="includeEnd">Whether to permit unwalkable voxels.</param>
    /// <returns>True if a path is required; otherwise, false.</returns>
    public static bool NeedsPath(
        GridWorld world,
        Vector3d startPos,
        Vector3d endPos,
        Fixed64 unitSize,
        bool includeEnd = false)
    {
        LinkWorld(world);
        foreach (GridVoxelSet gridVoxelSet in GridTracer.TraceLine(world, startPos, endPos))
        {
            foreach (Voxel voxel in gridVoxelSet.Voxels)
            {
                // A path is required if a voxel doesn't exist in the traced line
                if (!voxel.TryGetPartition(out SolidChartPartition? partition))
                    return true;

                if (!includeEnd && !voxel.IsBlocked && partition!.IsImpassable(unitSize))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Checks if a path is needed between the start and end positions using the configured world.
    /// </summary>
    public static bool NeedsPath(
        Vector3d startPos,
        Vector3d endPos,
        Fixed64 unitSize,
        bool includeEnd = false)
    {
        return NeedsPath(GetConfiguredWorld(), startPos, endPos, unitSize, includeEnd);
    }

    #endregion
}

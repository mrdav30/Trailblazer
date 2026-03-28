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
    internal static void RegisterTrailblazerLifecycleHooks()
    {
        TrailblazerManager.RegisterOnSimulateCore(
            owner: "PathManager.Tick",
            order: TrailblazerLifecycleOrder.PathingMaintenance,
            callback: Tick);
    }

    #region Properties

    public static readonly int DefaultMaxPathSearchRange = 1000;

    /// <summary>
    /// Internal dictionary of all registered navigation charts, keyed by their unique names.
    /// </summary>
    private static readonly SwiftDictionary<string, NavigationChart> _navigationChartMap = new();

    private static readonly SwiftDictionary<string, GeneratedChartTransitionState> _generatedTransitionsByChart =
        new(8, StringComparer.Ordinal);

    private static readonly SwiftDictionary<GlobalVoxelIndex, ResolvedChartVoxelState> _resolvedChartVoxelStates =
        new();

    /// <summary>
    /// Lock for managing concurrent access to <c>_navigationChartMap</c> operations.
    /// Ensures thread safety for read/write operations.
    /// </summary>
    private static readonly ReaderWriterLockSlim _navigationChartMapLock = new();

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
                {
                    if (index >= charts.Length)
                        break;

                    charts[index++] = chart;
                }

                if (index == charts.Length)
                    return charts;

                NavigationChart[] trimmed = new NavigationChart[index];
                Array.Copy(charts, trimmed, index);
                return trimmed;
            }
            finally { _navigationChartMapLock.ExitReadLock(); }
        }
    }

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

    private static int _activeAuthoredGasCellCount;

    private static int _activeAuthoredLiquidCellCount;

    private static int _nextChartRegistrationOrder;

    #endregion

    internal static void Tick()
    {
        PathGuideFactory.CullExpiredGuides(TrailblazerManager.FrameCount);
    }

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
        if (chart == null)
            ThrowHelper.ThrowArgumentNullException(nameof(chart));

        _navigationChartMapLock.EnterWriteLock();
        try
        {
            if (_navigationChartMap.ContainsKey(chart.Name))
                return false;

            chart.RegistrationOrder = unchecked(++_nextChartRegistrationOrder);
            _navigationChartMap.Add(chart.Name, chart);
        }
        finally { _navigationChartMapLock.ExitWriteLock(); }

        if (initializeChart)
            InitializeChart(chart.Name);

        return true;
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
        if (buildResult == null)
            ThrowHelper.ThrowArgumentNullException(nameof(buildResult));

        if (!Register(buildResult.Chart, initializeChart: false))
            return false;

        TraversalTransition[] generatedTransitions = buildResult.GeneratedTransitions;
        string[] registeredTransitionIds = new string[generatedTransitions.Length];
        int registeredTransitionCount = 0;

        for (int i = 0; i < generatedTransitions.Length; i++)
        {
            TraversalTransition transition = generatedTransitions[i];
            if (!TraversalTransitionRegistry.RegisterGenerated(transition))
            {
                RollbackTraversalBuildRegistration(
                    buildResult.Chart,
                    registeredTransitionIds,
                    registeredTransitionCount);
                return false;
            }

            registeredTransitionIds[registeredTransitionCount++] = transition.Id;
        }

        RememberGeneratedTransitions(
            buildResult.Chart.Name,
            buildResult.GeneratedTransitionIdPrefix,
            registeredTransitionIds,
            registeredTransitionCount);

        if (initializeChart)
            InitializeChart(buildResult.Chart.Name);

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
    /// Initializes all registered navigation charts by materializing their authored surface and volume partitions.
    /// </summary>
    public static void InitializeAllCharts()
    {
        foreach (NavigationChart chart in AllCharts)
            InitializeChart(chart.Name);
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
        try
        {
            bool changed = TryApplyChartCellUpdate(
                chart,
                x,
                y,
                z,
                cell,
                partitionsToRebind,
                invalidatedChartKeys);

            if (changed)
                RefreshGeneratedTransitionsForChartMutation(chart, x, y, z);

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
        if (updates == null)
            ThrowHelper.ThrowArgumentNullException(nameof(updates));

        if (updates.Count == 0 || !TryGetNavigationChart(chartName, out NavigationChart chart))
            return 0;

        SwiftHashSet<SolidChartPartition> partitionsToRebind = PartitionSetPool.Rent();
        SwiftHashSet<string> invalidatedChartKeys = SwiftHashSetPool<string>.Shared.Rent();
        try
        {
            int changedCount = 0;
            SwiftList<NavigationChartCellUpdate> changedUpdates = new();
            for (int i = 0; i < updates.Count; i++)
            {
                NavigationChartCellUpdate update = updates[i];
                if (TryApplyChartCellUpdate(
                    chart,
                    update.X,
                    update.Y,
                    update.Z,
                    update.Cell,
                    partitionsToRebind,
                    invalidatedChartKeys))
                {
                    changedCount++;
                    changedUpdates.Add(update);
                }
            }

            for (int i = 0; i < changedUpdates.Count; i++)
            {
                NavigationChartCellUpdate update = changedUpdates[i];
                RefreshGeneratedTransitionsForChartMutation(chart, update.X, update.Y, update.Z);
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
        if (string.IsNullOrEmpty(chartKey)
            || !TryGetNavigationChart(chartKey, out var chart)
            || chart.IsInitialized)
        {
            return;
        }

        SwiftHashSet<SolidChartPartition> partitionsToRebind = PartitionSetPool.Rent();
        SwiftHashSet<string> affectedChartKeys = SwiftHashSetPool<string>.Shared.Rent();
        try
        {
            foreach ((Vector3d pos, NavigationChartCell cell) in chart.GetAuthoredCells())
            {
                if (!GlobalGridManager.TryGetVoxel(pos, out Voxel voxel))
                    continue;

                if (!_resolvedChartVoxelStates.TryGetValue(voxel.GlobalIndex, out ResolvedChartVoxelState state))
                {
                    state = new ResolvedChartVoxelState();
                    _resolvedChartVoxelStates[voxel.GlobalIndex] = state;
                }
                else if (state.HasAnyOwners)
                    ChartOwnerUtility.AddOwners(affectedChartKeys, state.ChartOwners);

                NavigationChartCell previousEffectiveCell = state.EffectiveCell;
                state.AddOwner(chart.Name, cell);
                ApplyResolvedVoxelState(voxel, state, previousEffectiveCell, partitionsToRebind);
            }

            foreach (SolidChartPartition part in partitionsToRebind)
                part.BindNeighbors();

            chart.IsInitialized = true;

            foreach (string affectedChartKey in affectedChartKeys)
                PathGuideFactory.InvalidateCacheFor(affectedChartKey);
        }
        finally
        {
            PartitionSetPool.Release(partitionsToRebind);
            SwiftHashSetPool<string>.Shared.Release(affectedChartKeys);
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

        string[] generatedTransitionIds = RemoveGeneratedTransitions(chart.Name);

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
        try
        {
            affectedChartKeys.Add(chart.Name);
            foreach ((Vector3d position, NavigationChartCell cell) in chart.GetAuthoredCells())
            {
                if (!GlobalGridManager.TryGetVoxel(position, out Voxel voxel))
                    continue;

                if (!_resolvedChartVoxelStates.TryGetValue(voxel.GlobalIndex, out ResolvedChartVoxelState state)
                    || !state.ChartOwners.Contains(chart.Name))
                {
                    continue;
                }

                ChartOwnerUtility.AddOwners(affectedChartKeys, state.ChartOwners);

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
        }
    }

    /// <summary>
    /// Clears all registered maps, partitions, and guide pools.
    /// </summary>
    public static void Reset()
    {
        VolumeMediumRules.Reset();
        TraversalTransitionRegistry.Reset();

        var allCharts = AllCharts;

        foreach (KeyValuePair<GlobalVoxelIndex, ResolvedChartVoxelState> pair in _resolvedChartVoxelStates)
        {
            if (!GlobalGridManager.TryGetGridAndVoxel(pair.Key, out _, out Voxel voxel))
                continue;

            if (voxel.TryGetPartition(out SolidChartPartition _))
                voxel.TryRemovePartition<SolidChartPartition>();

            if (voxel.TryGetPartition(out VolumeChartPartition _))
                voxel.TryRemovePartition<VolumeChartPartition>();
        }

        _resolvedChartVoxelStates.Clear();

        _navigationChartMapLock.EnterWriteLock();
        try
        {
            foreach (NavigationChart chart in allCharts)
                if (chart != null)
                    chart.IsInitialized = false;

            _navigationChartMap.Clear();
            _generatedTransitionsByChart.Clear();
            _activeAuthoredGasCellCount = 0;
            _activeAuthoredLiquidCellCount = 0;
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

    #region Pathfinding Utilities

    private static void RollbackTraversalBuildRegistration(
        NavigationChart chart,
        string[] registeredTransitionIds,
        int registeredTransitionCount)
    {
        TraversalTransitionRegistry.UnregisterRange(registeredTransitionIds, registeredTransitionCount);

        UnloadChart(chart);
    }

    private static void RememberGeneratedTransitions(
        string chartName,
        string transitionIdPrefix,
        string[] transitionIds,
        int transitionCount)
    {
        _navigationChartMapLock.EnterWriteLock();
        try
        {
            var state = new GeneratedChartTransitionState(transitionIdPrefix);
            for (int i = 0; i < transitionCount; i++)
                state.TransitionIds.Add(transitionIds[i]);

            _generatedTransitionsByChart[chartName] = state;
        }
        finally { _navigationChartMapLock.ExitWriteLock(); }
    }

    private static string[] RemoveGeneratedTransitions(string chartName)
    {
        _navigationChartMapLock.EnterWriteLock();
        try
        {
            if (!_generatedTransitionsByChart.TryGetValue(chartName, out GeneratedChartTransitionState state))
                return Array.Empty<string>();

            _generatedTransitionsByChart.Remove(chartName);
            return CopyTransitionIds(state.TransitionIds);
        }
        finally { _navigationChartMapLock.ExitWriteLock(); }
    }

    private static bool TryGetGeneratedTransitionState(
        string chartName,
        out GeneratedChartTransitionState state)
    {
        _navigationChartMapLock.EnterReadLock();
        try { return _generatedTransitionsByChart.TryGetValue(chartName, out state); }
        finally { _navigationChartMapLock.ExitReadLock(); }
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

    private static void RemoveChartFromRegistry(string chartName)
    {
        _navigationChartMapLock.EnterWriteLock();
        try { _navigationChartMap.Remove(chartName); }
        finally { _navigationChartMapLock.ExitWriteLock(); }
    }

    private static bool TryApplyChartCellUpdate(
        NavigationChart chart,
        int x,
        int y,
        int z,
        NavigationChartCell cell,
        SwiftHashSet<SolidChartPartition> partitionsToRebind,
        SwiftHashSet<string> invalidatedChartKeys)
    {
        if (chart == null || !chart.TrySetCell(x, y, z, cell, out _))
            return false;

        if (!chart.IsInitialized)
            return true;

        Vector3d position = chart.GetWorldPosition(x, y, z);
        if (!GlobalGridManager.TryGetVoxel(position, out Voxel voxel))
            return true;

        _resolvedChartVoxelStates.TryGetValue(voxel.GlobalIndex, out ResolvedChartVoxelState state);

        NavigationChartCell previousEffectiveCell = state?.EffectiveCell ?? NavigationChartCell.Empty;
        string previousEffectiveOwner = state?.EffectiveChartOwner;

        if (cell.HasTraversalData)
        {
            state ??= new ResolvedChartVoxelState();
            state.AddOwner(chart.Name, cell);
            _resolvedChartVoxelStates[voxel.GlobalIndex] = state;
        }
        else if (state != null && state.ChartOwners.Contains(chart.Name))
        {
            state.RemoveOwner(chart.Name);
            if (!state.HasAnyOwners)
                _resolvedChartVoxelStates.Remove(voxel.GlobalIndex);
        }
        else
            return true;

        ApplyResolvedVoxelState(voxel, state, previousEffectiveCell, partitionsToRebind);
        CollectEffectiveStateInvalidations(
            previousEffectiveOwner,
            previousEffectiveCell,
            state?.EffectiveChartOwner,
            state?.EffectiveCell ?? NavigationChartCell.Empty,
            invalidatedChartKeys);
        return true;
    }

    private static void RefreshGeneratedTransitionsForChartMutation(
        NavigationChart chart,
        int x,
        int y,
        int z)
    {
        if (chart == null || !TryGetGeneratedTransitionState(chart.Name, out GeneratedChartTransitionState state))
            return;

        RefreshGeneratedTransitionsForPair(chart, state, x, y, z, x - 1, y, z);
        RefreshGeneratedTransitionsForPair(chart, state, x, y, z, x + 1, y, z);
        RefreshGeneratedTransitionsForPair(chart, state, x, y, z, x, y - 1, z);
        RefreshGeneratedTransitionsForPair(chart, state, x, y, z, x, y + 1, z);
        RefreshGeneratedTransitionsForPair(chart, state, x, y, z, x, y, z - 1);
        RefreshGeneratedTransitionsForPair(chart, state, x, y, z, x, y, z + 1);
    }

    private static void RefreshGeneratedTransitionsForPair(
        NavigationChart chart,
        GeneratedChartTransitionState state,
        int firstX,
        int firstY,
        int firstZ,
        int secondX,
        int secondY,
        int secondZ)
    {
        if (!chart.IsInBounds(firstX, firstY, firstZ)
            || !chart.IsInBounds(secondX, secondY, secondZ))
        {
            return;
        }

        string[] potentialTransitionIds = GeneratedTraversalTransitionBuilder.GetPotentialTransitionIdsForPair(
            state.TransitionIdPrefix,
            firstX,
            firstY,
            firstZ,
            secondX,
            secondY,
            secondZ);

        SwiftList<string> existingPairIds = new();
        for (int i = 0; i < potentialTransitionIds.Length; i++)
        {
            if (state.TransitionIds.Contains(potentialTransitionIds[i]))
                existingPairIds.Add(potentialTransitionIds[i]);
        }

        TraversalTransition[] currentPairTransitions =
            GeneratedTraversalTransitionBuilder.BuildTransitionsForPair(
                chart,
                state.TransitionIdPrefix,
                firstX,
                firstY,
                firstZ,
                secondX,
                secondY,
                secondZ);

        if (AreEquivalentTransitionSets(existingPairIds, currentPairTransitions))
            return;

        if (existingPairIds.Count > 0)
        {
            TraversalTransitionRegistry.UnregisterRange(existingPairIds.ToArray());
            for (int i = 0; i < existingPairIds.Count; i++)
                state.TransitionIds.Remove(existingPairIds[i]);
        }

        for (int i = 0; i < currentPairTransitions.Length; i++)
        {
            TraversalTransition transition = currentPairTransitions[i];
            if (!TraversalTransitionRegistry.RegisterGenerated(transition))
                continue;

            state.TransitionIds.Add(transition.Id);
        }
    }

    private static bool AreEquivalentTransitionSets(
        SwiftList<string> existingPairIds,
        TraversalTransition[] currentPairTransitions)
    {
        if (existingPairIds.Count != currentPairTransitions.Length)
            return false;

        for (int i = 0; i < currentPairTransitions.Length; i++)
        {
            if (!existingPairIds.Contains(currentPairTransitions[i].Id))
                return false;
        }

        return true;
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

    internal static bool IsHigherChartPrecedence(string candidateChartName, string currentChartName)
    {
        if (string.Equals(candidateChartName, currentChartName, StringComparison.Ordinal))
            return false;

        return CompareChartPrecedence(candidateChartName, currentChartName) > 0;
    }

    private static int CompareChartPrecedence(string candidateChartName, string currentChartName)
    {
        bool hasCandidate = TryGetChartPrecedence(candidateChartName, out int candidatePriority, out int candidateOrder);
        bool hasCurrent = TryGetChartPrecedence(currentChartName, out int currentPriority, out int currentOrder);

        if (!hasCandidate)
            return hasCurrent ? -1 : 0;

        if (!hasCurrent)
            return 1;

        if (candidatePriority != currentPriority)
            return candidatePriority > currentPriority ? 1 : -1;

        if (candidateOrder != currentOrder)
            return candidateOrder > currentOrder ? 1 : -1;

        return string.CompareOrdinal(candidateChartName, currentChartName);
    }

    private static bool TryGetChartPrecedence(string chartName, out int priority, out int registrationOrder)
    {
        _navigationChartMapLock.EnterReadLock();
        try
        {
            if (_navigationChartMap.TryGetValue(chartName, out NavigationChart chart))
            {
                priority = chart.Priority;
                registrationOrder = chart.RegistrationOrder;
                return true;
            }
        }
        finally
        {
            _navigationChartMapLock.ExitReadLock();
        }

        priority = 0;
        registrationOrder = 0;
        return false;
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

            solidPartition.ApplyAuthoredState(state.ChartOwners, state.EffectiveChartOwner, effectiveCell);
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

            volumePartition.ApplyAuthoredState(state.ChartOwners, state.EffectiveChartOwner, effectiveCell);
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
        if (voxel == null || partitionsToRebind == null)
            return;

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

    #region Utility Methods

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

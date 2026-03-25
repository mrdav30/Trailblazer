using FixedMathSharp;
using GridForge;
using GridForge.Grids;
using GridForge.Spatial;
using GridForge.Utility;
using SwiftCollections;
using SwiftCollections.Pool;
using System;
using System.Collections.Generic;
using System.Linq;
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
            try { return _navigationChartMap.Values.ToArray(); }
            finally { _navigationChartMapLock.ExitReadLock(); }
        }
    }

    internal static readonly SwiftHashSetPool<PathPartition> PartitionSetPool = new SwiftHashSetPool<PathPartition>();

    /// <summary>
    /// Pool of reusable <see cref="PathPartition"/> instances used for partitioning the navigation grid.
    /// </summary>
    internal static readonly SwiftObjectPool<PathPartition> PartitionPool = new(
        () => new PathPartition(),
        actionOnRelease: partition => partition.Reset()
    );

    /// <summary>
    /// Pool of reusable <see cref="VolumePartition"/> instances used for authored raw-volume traversal.
    /// </summary>
    internal static readonly SwiftObjectPool<VolumePartition> VolumePartitionPool = new(
        () => new VolumePartition(),
        actionOnRelease: partition => partition.Reset()
    );

    private static int _activeAuthoredOpenVolumeCellCount;

    private static int _activeAuthoredWaterVolumeCellCount;

    private static int _volumeTopologyVersion;

    internal static int VolumeTopologyVersion => _volumeTopologyVersion;

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
    /// <returns>True if successful, false if a duplicate name exists.</returns>
    public static bool Register(NavigationChart chart)
    {
        if (IsChartRegistered(chart.Name))
            return false;

        _navigationChartMapLock.EnterWriteLock();
        try { _navigationChartMap.Add(chart.Name, chart); }
        finally { _navigationChartMapLock.ExitWriteLock(); }
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

        if (!Register(buildResult.Chart))
            return false;

        TraversalTransition[] generatedTransitions = buildResult.GeneratedTransitions;
        string[] registeredTransitionIds = new string[generatedTransitions.Length];
        int registeredTransitionCount = 0;

        for (int i = 0; i < generatedTransitions.Length; i++)
        {
            TraversalTransition transition = generatedTransitions[i];
            if (!TraversalTransitionRegistry.Register(transition))
            {
                RollbackTraversalBuildRegistration(
                    buildResult.Chart,
                    registeredTransitionIds,
                    registeredTransitionCount);
                return false;
            }

            registeredTransitionIds[registeredTransitionCount++] = transition.Id;
        }

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

        SwiftHashSet<PathPartition> allChartPartitions = PartitionSetPool.Rent();
        SwiftQueue<string> existingChartKeys = new(); // TODO: pool
        bool changedVolumeTopology = false;
        foreach ((Vector3d pos, NavigationChartCell cell) in chart.GetAuthoredCells())
        {
            if (!GlobalGridManager.TryGetVoxel(pos, out Voxel voxel))
                continue;

            if (cell.HasSurface)
            {
                if (!voxel.TryGetPartition(out PathPartition part))
                {
                    part = PartitionPool.Rent();
                    voxel.TryAddPartition(part);
                }

                if (part.HasAnyOwners)
                    existingChartKeys.EnqueueRange(part.ChartOwners);

                part.AddOwner(chart.Name, cell);
                allChartPartitions.Add(part);
            }

            if (!cell.HasVolume)
                continue;

            if (!voxel.TryGetPartition(out VolumePartition volumePart))
            {
                volumePart = VolumePartitionPool.Rent();
                voxel.TryAddPartition(volumePart);
            }

            volumePart.AddOwner(chart.Name, cell);
            TrackVolumeCell(cell, delta: 1);
            changedVolumeTopology = true;
        }

        // bind new neighbor pointers for each partition since they could have changed
        foreach (PathPartition part in allChartPartitions)
            part.BindNeighbors();

        PartitionSetPool.Release(allChartPartitions);
        chart.IsInitialized = true;

        // invalidate existing paths that new charts partitions are in
        while (existingChartKeys.Count > 0)
            PathGuideFactory.InvalidateCacheFor(existingChartKeys.Dequeue());
        // TODO: release existingChartKeys

        if (changedVolumeTopology)
            InvalidateVolumeTopology();
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

        if (!chart.IsInitialized)
        {
            RemoveChartFromRegistry(chart.Name);
            chart.IsInitialized = false;
            return;
        }

        // invalidate any survey results currently using this chart
        PathGuideFactory.InvalidateCacheFor(chart.Name);

        SwiftHashSet<PathPartition> stillActivePartitions = PartitionSetPool.Rent();
        bool changedVolumeTopology = false;
        foreach ((Vector3d position, NavigationChartCell cell) in chart.GetAuthoredCells())
        {
            if (!GlobalGridManager.TryGetVoxel(position, out Voxel voxel))
                continue;

            if (cell.HasSurface
                && voxel.TryGetPartition(out PathPartition part)
                && part.BelongsTo(chart.Name))
            {
                part.RemoveOwner(chart.Name);
                if (!part.HasAnyOwners)
                    voxel.TryRemovePartition<PathPartition>();
                else
                {
                    // if partition still belongs to a chart, reset its clearance values and rebind neighbors
                    part.HandleChange(GridChange.Update, voxel);
                    stillActivePartitions.Add(part);
                }
            }

            if (!cell.HasVolume
                || !voxel.TryGetPartition(out VolumePartition volumePart)
                || !volumePart.BelongsTo(chart.Name))
            {
                continue;
            }

            volumePart.RemoveOwner(chart.Name);
            if (!volumePart.HasAnyOwners)
                voxel.TryRemovePartition<VolumePartition>();

            TrackVolumeCell(cell, delta: -1);
            changedVolumeTopology = true;
        }

        // bind neighbor pointers for each partition
        foreach (PathPartition part in stillActivePartitions)
            part.BindNeighbors();

        PartitionSetPool.Release(stillActivePartitions);

        RemoveChartFromRegistry(chart.Name);
        chart.IsInitialized = false;

        if (changedVolumeTopology)
            InvalidateVolumeTopology();
    }

    /// <summary>
    /// Clears all registered maps, partitions, and guide pools.
    /// </summary>
    public static void Reset()
    {
        VolumeTraversalRules.Reset();
        TraversalTransitionRegistry.Reset();

        var allCharts = AllCharts;

        // remove all partitions from voxels and clear navigation map references
        _navigationChartMapLock.EnterWriteLock();
        try
        {
            foreach (NavigationChart chart in allCharts)
            {
                if (chart == null) continue;

                foreach ((Vector3d position, NavigationChartCell cell) in chart.GetAuthoredCells())
                {
                    if (!GlobalGridManager.TryGetVoxel(position, out Voxel voxel))
                        continue;

                    if (cell.HasSurface
                        && voxel.TryGetPartition(out PathPartition part)
                        && part.BelongsTo(chart.Name))
                    {
                        part.RemoveOwner(chart.Name);
                        if (!part.HasAnyOwners)
                            voxel.TryRemovePartition<PathPartition>();
                    }

                    if (cell.HasVolume
                        && voxel.TryGetPartition(out VolumePartition volumePart)
                        && volumePart.BelongsTo(chart.Name))
                    {
                        volumePart.RemoveOwner(chart.Name);
                        if (!volumePart.HasAnyOwners)
                            voxel.TryRemovePartition<VolumePartition>();
                    }
                }

                chart.IsInitialized = false;
            }

            _navigationChartMap.Clear();
            _activeAuthoredOpenVolumeCellCount = 0;
            _activeAuthoredWaterVolumeCellCount = 0;
            Interlocked.Increment(ref _volumeTopologyVersion);
        }
        finally
        {
            _navigationChartMapLock.ExitWriteLock();
        }

        if (PathGuideFactory.IsPooling)
            PathGuideFactory.FlushCache(true);
    }

    #endregion

    private static void RollbackTraversalBuildRegistration(
        NavigationChart chart,
        string[] registeredTransitionIds,
        int registeredTransitionCount)
    {
        for (int i = 0; i < registeredTransitionCount; i++)
            TraversalTransitionRegistry.Unregister(registeredTransitionIds[i]);

        UnloadChart(chart);
    }

    private static void RemoveChartFromRegistry(string chartName)
    {
        _navigationChartMapLock.EnterWriteLock();
        try { _navigationChartMap.Remove(chartName); }
        finally { _navigationChartMapLock.ExitWriteLock(); }
    }

    internal static bool HasAuthoredVolumeTraversal(VolumeTraversalMode traversalMode)
    {
        return traversalMode switch
        {
            VolumeTraversalMode.Open => _activeAuthoredOpenVolumeCellCount > 0,
            VolumeTraversalMode.Water => _activeAuthoredWaterVolumeCellCount > 0,
            _ => false
        };
    }

    private static void TrackVolumeCell(NavigationChartCell cell, int delta)
    {
        if (cell.SupportsVolumeTraversal(VolumeTraversalMode.Open))
            _activeAuthoredOpenVolumeCellCount += delta;

        if (cell.SupportsVolumeTraversal(VolumeTraversalMode.Water))
            _activeAuthoredWaterVolumeCellCount += delta;
    }

    private static void InvalidateVolumeTopology()
    {
        Interlocked.Increment(ref _volumeTopologyVersion);
        PathGuideFactory.InvalidateVolumeCache();
    }

    #region Neighbor Discovery

    /// <summary>Returns all walkable neighbors of the voxel.</summary>
    public static IEnumerable<TraversableVoxel> GetWalkableNeighbors(GlobalVoxelIndex idx)
    {
        if (!GlobalGridManager.TryGetGridAndVoxel(idx, out _, out Voxel voxel))
            yield break;
        foreach (TraversableVoxel tv in WalkableNeighborsOf(voxel))
            yield return tv;
    }

    /// <summary>Returns walkable neighbors for a specific voxel.</summary>
    public static IEnumerable<TraversableVoxel> WalkableNeighborsOf(Voxel voxel)
    {
        foreach (SpatialDirection dir in SpatialAwareness.AllDirections)
        {
            if (!voxel.TryGetNeighborFromDirection(dir, out Voxel neighbor)) continue;
            if (neighbor.IsBlocked || !neighbor.TryGetPartition(out PathPartition part)) continue;
            yield return new TraversableVoxel { Voxel = neighbor, Partition = part, Direction = dir };
        }
    }

    /// <summary>Returns all straight (orthogonal) neighbors.</summary>
    public static IEnumerable<TraversableVoxel> GetWalkablePerpendicularNeighbors(GlobalVoxelIndex idx)
    {
        if (!GlobalGridManager.TryGetGridAndVoxel(idx, out _, out Voxel voxel))
            yield break;
        foreach (TraversableVoxel tv in WalkablePerpendicularNeighborsOf(voxel))
            yield return tv;
    }

    /// <summary>Returns all straight (orthogonal) neighbors.</summary>
    public static IEnumerable<TraversableVoxel> WalkablePerpendicularNeighborsOf(Voxel voxel)
    {
        foreach (SpatialDirection dir in SpatialAwareness.PerpendicularDirections)
        {
            if (!voxel.TryGetNeighborFromDirection(dir, out Voxel neighbor)) continue;
            if (neighbor.IsBlocked || !neighbor.TryGetPartition(out PathPartition part)) continue;
            yield return new TraversableVoxel { Voxel = neighbor, Partition = part, Direction = dir };
        }
    }

    /// <summary>Returns all diagonal neighbors, avoiding edge-cutting.</summary>
    public static IEnumerable<TraversableVoxel> GetWalkableDiagonalNeighbors(GlobalVoxelIndex idx)
    {
        if (!GlobalGridManager.TryGetGridAndVoxel(idx, out _, out Voxel voxel))
            yield break;
        foreach (TraversableVoxel tv in WalkableDiagonalNeighborsOf(voxel))
            yield return tv;
    }

    /// <summary>Returns all diagonal neighbors, avoiding edge-cutting.</summary>
    public static IEnumerable<TraversableVoxel> WalkableDiagonalNeighborsOf(Voxel voxel)
    {
        foreach (SpatialDirection dir in SpatialAwareness.DiagonalDirections)
        {
            if (!voxel.TryGetNeighborFromDirection(dir, out Voxel neighbor)) continue;
            if (neighbor.IsBlocked || !neighbor.TryGetPartition(out PathPartition part)) continue;
            if (HasBlockedEdgeNeighbor(voxel, dir)) continue;
            yield return new TraversableVoxel { Voxel = neighbor, Partition = part, Direction = dir };
        }
    }

    /// <summary>
    /// For any multi-axis step (dx,dy,dz), ensures each single-axis "leg" is walkable.
    /// </summary>
    private static bool HasBlockedEdgeNeighbor(Voxel voxel, SpatialDirection dir)
    {
        (int dx, int dy, int dz) = SpatialAwareness.DirectionOffsets[(int)dir];
        // build legs for each non-zero axis
        foreach ((int ax, int ay, int az) in new[] { (dx, 0, 0), (0, dy, 0), (0, 0, dz) })
        {
            if (ax == 0 && ay == 0 && az == 0) continue;
            if (!voxel.TryGetNeighborFromOffset((ax, ay, az), out Voxel edgeVoxel)
                || edgeVoxel.IsBlocked
                || !edgeVoxel.HasPartition<PathPartition>())
            {
                return true;
            }
        }
        return false;
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
    /// <param name="allowUnwalkableEndNode">Whether to permit unwalkable voxels.</param>
    /// <returns>True if a path is required; otherwise, false.</returns>
    public static bool NeedsPath(
        Vector3d startPos,
        Vector3d endPos,
        Fixed64 unitSize,
        bool allowUnwalkableEndNode = false)
    {
        foreach (GridVoxelSet gridVoxelSet in GridTracer.TraceLine(startPos, endPos))
        {
            foreach (Voxel voxel in gridVoxelSet.Voxels)
            {
                // A path is required if a voxel doesn't exist in the traced line
                if (!voxel.TryGetPartition(out PathPartition partition))
                    return true;

                if (!allowUnwalkableEndNode && !voxel.IsBlocked && partition.IsImpassable(unitSize))
                    return true;
            }
        }
        return false;
    }

    #endregion
}

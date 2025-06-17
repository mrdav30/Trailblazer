using FixedMathSharp;
using GridForge.Grids;
using GridForge.Utility;
using SwiftCollections.Pool;
using SwiftCollections;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using GridForge.Spatial;
using System.Threading;
using System.Linq;
using System.Collections.Concurrent;

namespace Trailblazer.Pathing
{
    /// <summary>
    /// Manages registration, initialization, and validation of navigation charts,
    /// as well as providing global pathfinding utilities and neighbor discovery.
    /// </summary>
    public static class PathManager
    {
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

        /// <summary>
        /// Pool of reusable <see cref="PathPartition"/> instances used for partitioning the navigation grid.
        /// </summary>
        internal static readonly SwiftObjectPool<PathPartition> PartitionPool = new(
            () => new PathPartition(),
            actionOnRelease: partition => partition.Reset()
        );

        private static readonly Lazy<SwiftHashSetPool<PathPartition>> _partitionSetPool =
            new(() => new SwiftHashSetPool<PathPartition>());
        internal static SwiftHashSetPool<PathPartition> PartitionSetPool => _partitionSetPool.Value;

        /// <summary>
        /// All 26 neighbor directions excluding None.
        /// </summary>
        public static readonly LinearDirection[] AllDirections =
            Enum.GetValues(typeof(LinearDirection))
                .Cast<LinearDirection>()
                .Where(d => d != LinearDirection.None)
                .ToArray();

        public static readonly LinearDirection[] PerpendicularDirections
          = AllDirections
              .Where(IsPerpendicularNeighbor)
              .ToArray();

        public static readonly LinearDirection[] DiagonalDirections
          = AllDirections
              .Where(IsDiagonalNeighbor)
              .ToArray();

        #endregion

        internal static void Tick(int currentFrame)
        {
            PathGuideFactory.CullExpiredGuides(currentFrame);
            ProcessPendingUnloads();
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
        /// Initializes all registered navigation maps by assigning walkable voxels to partitions.
        /// </summary>
        public static void InitializeAllCharts()
        {
            foreach (NavigationChart chart in AllCharts)
                InitializeChart(chart.Name);
        }

        /// <summary>
        /// Initializes a specific navigation map, assigning voxels to partitions.
        /// </summary>
        /// <param name="name">The name of the map to initialize.</param>
        public static void InitializeChart(string name)
        {
            if (!TryGetNavigationChart(name, out var chart) || chart.IsInitialized) return;

            SwiftHashSet<PathPartition> allChartPartitions = PartitionSetPool.Rent();
            foreach (Vector3d pos in chart.GetWalkablePositions())
            {
                if (!GlobalGridManager.TryGetGridAndVoxel(pos, out _, out Voxel voxel))
                    continue;

                if (!voxel.TryGetPartition(out PathPartition part))
                {
                    part = PartitionPool.Rent();
                    voxel.TryAddPartition(part);
                }

                part.AddOwner(chart.Name);
                allChartPartitions.Add(part);
            }

            // bind neighbor pointers for each partition
            foreach (PathPartition part in allChartPartitions)
                part.BindNeighbors();

            PartitionSetPool.Release(allChartPartitions);
            chart.IsInitialized = true;
        }

        /// <summary>
        /// Unloads all registered maps, removing ownerships and partitions from walkable voxels.
        /// </summary>
        public static void UnloadAllCharts()
        {
            foreach (NavigationChart chart in AllCharts)
                UnloadChart(chart.Name);
        }

        /// <summary>
        /// Unloads a navigation map by name and releases associated partitions.
        /// </summary>
        /// <param name="name">The name of the map to unload.</param>
        public static void UnloadChart(string name)
        {
            if (!TryGetNavigationChart(name, out NavigationChart chart) || !chart.IsInitialized)
                return;

            foreach (Vector3d position in chart.GetWalkablePositions())
            {
                if (!GlobalGridManager.TryGetGridAndVoxel(position, out _, out Voxel voxel))
                    continue;

                if (voxel.TryGetPartition(out PathPartition partition) && partition.BelongsTo(name))
                {
                    partition.RemoveOwner(name);
                    if (!partition.HasAnyOwners)
                        voxel.TryRemovePartition<PathPartition>();

                }
            }

            _navigationChartMapLock.EnterWriteLock();
            try { _navigationChartMap.Remove(name); }
            finally { _navigationChartMapLock.ExitWriteLock(); }

            if (PathGuideFactory.IsPooling)
                PathGuideFactory.FlushPools();
        }

        /// <summary>
        /// Clears all registered maps, partitions, and guide pools.
        /// </summary>
        public static void ClearAll()
        {
            _navigationChartMapLock.EnterWriteLock();
            try { _navigationChartMap.Clear(); }
            finally { _navigationChartMapLock.ExitWriteLock(); }

            if (PathGuideFactory.IsPooling)
                PathGuideFactory.FlushPools();
        }

        #endregion

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
            foreach (LinearDirection dir in AllDirections)
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
            foreach (LinearDirection dir in PerpendicularDirections)
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
            foreach (LinearDirection dir in DiagonalDirections)
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
        private static bool HasBlockedEdgeNeighbor(Voxel voxel, LinearDirection dir)
        {
            var(dx, dy, dz) = GlobalGridManager.DirectionOffsets[(int)dir];
            // build legs for each non-zero axis
            foreach (var (ax, ay, az) in new[] { (dx, 0, 0), (0, dy, 0), (0, 0, dz) })
            {
                if (ax == 0 && ay == 0 && az == 0) continue;
                if (!voxel.TryGetNeighborFromOffset((ax, ay, az), out var edgeVoxel)
                    || edgeVoxel.IsBlocked
                    || !edgeVoxel.HasPartition<PathPartition>())
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>True for pure axis-aligned directions.</summary>
        public static bool IsPerpendicularNeighbor(LinearDirection dir) =>
            dir == LinearDirection.West || dir == LinearDirection.East ||
            dir == LinearDirection.North || dir == LinearDirection.South ||
            dir == LinearDirection.Above || dir == LinearDirection.Below;

        /// <summary>True for any diagonal step (multiple axes).</summary>
        public static bool IsDiagonalNeighbor(LinearDirection dir)
        {
            var (dx, dy, dz) = GlobalGridManager.DirectionOffsets[(int)dir];
            int axes = (dx != 0 ? 1 : 0) + (dy != 0 ? 1 : 0) + (dz != 0 ? 1 : 0);
            return axes >= 2;
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
        public static bool GetMaxSearchSize(Voxel start, Voxel end, out int maxSearchSize)
        {
            if (!GlobalGridManager.TryGetGrid(start.GlobalIndex.GridIndex, out VoxelGrid startGrid)
                || !GlobalGridManager.TryGetGrid(end.GlobalIndex.GridIndex, out VoxelGrid endGrid))
            {
                maxSearchSize = 0;
                return false;
            }

            maxSearchSize = startGrid.SpawnToken == endGrid.SpawnToken ? startGrid.Size : startGrid.Size + endGrid.Size;
            return true;
        }

        /// <summary>
        /// Checks if a path is needed between the start and end positions based on traced voxels and unit size.
        /// </summary>
        /// <param name="startPos">The starting position.</param>
        /// <param name="endPos">The destination position.</param>
        /// <param name="unitSize">The size of the navigating unit.</param>
        /// <param name="allowUnwalkable">Whether to permit unwalkable voxels.</param>
        /// <returns>True if a path is required; otherwise, false.</returns>
        public static bool NeedsPath(
            Vector3d startPos,
            Vector3d endPos,
            Fixed64 unitSize,
            bool allowUnwalkable = false)
        {
            foreach (GridVoxelSet gridVoxelSet in GridTracer.TraceLine(startPos, endPos))
            {
                foreach (Voxel voxel in gridVoxelSet.Voxels)
                {
                    // A path is required if a voxel doesn't exist in the traced line
                    if (!voxel.TryGetPartition(out PathPartition partition))
                        return true;

                    if (!allowUnwalkable && !voxel.IsBlocked && partition.IsImpassable(unitSize))
                        return true;
                }
            }
            return false;
        }

        #endregion
    }
}

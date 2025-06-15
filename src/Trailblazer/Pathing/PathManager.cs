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

namespace Trailblazer.Pathing
{
    /// <summary>
    /// Manages registration, initialization, and validation of navigation maps,
    /// as well as providing global pathfinding utilities like voxel validation,
    /// path necessity checks, and partition pooling.
    /// </summary>
    public static class PathManager
    {
        public static readonly int DefaultMaxPathSearchRange = 1000;

        /// <summary>
        /// Internal dictionary of all registered navigation charts, keyed by their unique names.
        /// </summary>
        private static readonly SwiftDictionary<string, NavigationChart> _loadedMaps = new();

        /// <summary>
        /// Gets an enumerable collection of all currently registered navigation charts.
        /// </summary>
        public static IEnumerable<NavigationChart> AllMaps => _loadedMaps.Values;

        /// <summary>
        /// Pool of reusable <see cref="PathPartition"/> instances used for partitioning the navigation grid.
        /// </summary>
        internal static readonly SwiftObjectPool<PathPartition> PartitionPool = new(
            () => new PathPartition(),
            actionOnRelease: partition => partition.Reset()
        );

        /// <summary>
        /// Attempts to get valid start and end voxels based on provided world positions.
        /// Falls back to the closest walkable neighbor if necessary.
        /// </summary>
        /// <param name="start">The start position in world space.</param>
        /// <param name="end">The end position in world space.</param>
        /// <param name="startVoxel">Resolved start voxel.</param>
        /// <param name="endVoxel">Resolved end voxel.</param>
        /// <returns>True if both voxels were resolved successfully; otherwise, false.</returns>
        public static bool GetValidPathRequest(
            Vector3d start, 
            Vector3d end, 
            out Voxel startVoxel, 
            out Voxel endVoxel)
        {
            endVoxel = null;
            if (!GlobalGridManager.TryGetGridAndVoxel(start, out _, out startVoxel))
            {
                Console.WriteLine("Unable to find a valid start voxel for {startPos}");
                return false;
            }

            if (startVoxel.IsBlocked || !startVoxel.TryGetPartition<PathPartition>(out _))
            {
                if (!VoxelFinder.TryGetClosestWalkableNeighbor(startVoxel, out Voxel closestNeighbor))
                    return false;
                startVoxel = closestNeighbor;
            }

            if (!GlobalGridManager.TryGetGridAndVoxel(end, out _, out endVoxel))
            {
                Console.WriteLine("Unable to find a valid end voxel for {targetPos}");
                return false;
            }

            if (endVoxel.IsBlocked || !endVoxel.TryGetPartition<PathPartition>(out _))
            {
                if (!VoxelFinder.TryGetClosestWalkableNeighbor(endVoxel, out Voxel closestNeighbor))
                    return false;
                endVoxel = closestNeighbor;
            }

            return true;
        }

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

                    if (!allowUnwalkable && !voxel.IsBlocked && partition.Unpassable(unitSize))
                        return true;
                }
            }
            return false;
        }

        #region Navigation Map Management

        /// <summary>
        /// Attempts to register a new navigation chart with the manager.
        /// </summary>
        /// <param name="map">The map to register.</param>
        /// <returns>True if successful, false if a duplicate name exists.</returns>
        public static bool Register(NavigationChart map)
        {
            if (IsMapRegistered(map.Name))
            {
                Debug.WriteLine($"Map named {map.Name} already exists!");
                return false;
            }

            _loadedMaps.Add(map.Name, map);
            return true;
        }

        /// <summary>
        /// Checks if a navigation map is already registered under the specified name.
        /// </summary>
        /// <param name="name">The map name to check.</param>
        /// <returns>True if registered; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsMapRegistered(string name) => _loadedMaps.ContainsKey(name);

        /// <summary>
        /// Attempts to retrieve a registered navigation chart by name.
        /// </summary>
        /// <param name="name">The name of the map.</param>
        /// <param name="map">The retrieved navigation chart.</param>
        /// <returns>True if the map exists; otherwise, false.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetNavigationMap(string name, out NavigationChart map) 
            => _loadedMaps.TryGetValue(name, out map);

        /// <summary>
        /// Initializes all registered navigation maps by assigning walkable voxels to partitions.
        /// </summary>
        public static void InitializeAllMaps()
        {
            foreach (NavigationChart navMap in AllMaps)
                InitializeMap(navMap.Name);
        }

        /// <summary>
        /// Initializes a specific navigation map, assigning voxels to partitions.
        /// </summary>
        /// <param name="name">The name of the map to initialize.</param>
        public static void InitializeMap(string name)
        {
            if (!TryGetNavigationMap(name, out NavigationChart map))
            {
                Debug.WriteLine($"Map named {map.Name} is not registered!");
                return;
            }

            if (map.IsInitialized)
                return;

            foreach (Vector3d pos in map.GetWalkablePositions())
            {
                if (!GlobalGridManager.TryGetGridAndVoxel(pos, out _, out Voxel voxel))
                    continue;

                if (!voxel.TryGetPartition(out PathPartition partition))
                {
                    partition = PartitionPool.Rent();
                    voxel.TryAddPartition(partition);
                }

                partition.AddOwner(map.Name);
            }

            map.IsInitialized = true;
        }

        /// <summary>
        /// Unloads all registered maps, removing ownerships and partitions from walkable voxels.
        /// </summary>
        public static void UnloadAllMaps()
        {
            foreach (NavigationChart navMap in AllMaps)
                Unload(navMap.Name);
        }

        /// <summary>
        /// Unloads a specific navigation map.
        /// </summary>
        /// <param name="navMap">The map to unload.</param>
        public static void Unload(NavigationChart navMap)
        {
            Unload(navMap.Name);
        }

        /// <summary>
        /// Unloads a navigation map by name and releases associated partitions.
        /// </summary>
        /// <param name="name">The name of the map to unload.</param>
        public static void Unload(string name)
        {
            if (!TryGetNavigationMap(name, out NavigationChart map))
            {
                Debug.WriteLine($"Map named {map.Name} is not registered!");
                return;
            }

            if (!map.IsInitialized)
                return;

            foreach (Vector3d position in map.GetWalkablePositions())
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

            _loadedMaps.Remove(name);

            // TODO: find a way to only clear relevant pools
            if (PathGuideFactory.IsPooling)
                PathGuideFactory.FlushPools();
        }

        /// <summary>
        /// Clears all registered maps, partitions, and guide pools.
        /// </summary>
        public static void ClearAll()
        {
            _loadedMaps.Clear();

            if (PathGuideFactory.IsPooling)
                PathGuideFactory.FlushPools();
        }

        #endregion

        #region Neighbor Discovery

        /// <summary>
        /// Checks if any edge neighbors of a diagonal neighbor are blocked.
        /// </summary>
        /// <param name="currentVoxel">The current voxel.</param>
        /// <param name="diagonalIndex">The index of the diagonal neighbor in the 3x3x3 grid.</param>
        /// <returns>True if any edge neighbors are blocked; otherwise, false.</returns>
        private static bool HasBlockedEdgeNeighbor(Voxel currentVoxel, LinearDirection diagonalIndex)
        {
            // Define the relative offsets for the two edge neighbors of each diagonal neighbor
            var edgeOffsets = diagonalIndex switch
            {
                LinearDirection.SouthWest => new[] { (x: -1, z: 0), (x: 0, z: -1) }, // South-West
                LinearDirection.NorthWest => new[] { (x: -1, z: 0), (x: 0, z: 1) },  // North-West
                LinearDirection.SouthEast => new[] { (x: 1, z: 0), (x: 0, z: -1) },  // South-East
                LinearDirection.NorthEast => new[] { (x: 1, z: 0), (x: 0, z: 1) },   // North-East
                _ => Array.Empty<(int x, int z)>()
            };

            foreach (var (xOffset, zOffset) in edgeOffsets)
            {
                // Calculate the linear index of the edge neighbor in the 3x3x3 grid
                LinearDirection edgeDirection = GlobalGridManager.GetNeighborDirectionFromOffset((xOffset, 0, zOffset));

                if (currentVoxel.TryGetNeighborFromDirection(edgeDirection, out Voxel edgeNeighbor))
                {
                    // Check if the edge neighbor is blocked or not walkable
                    if (edgeNeighbor.IsBlocked)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns all walkable neighbors of the partition’s current voxel.
        /// </summary>
        public static IEnumerable<TraversableVoxel> GetWalkableNeighbors(GlobalVoxelIndex coordinates)
        {
            if (!GlobalGridManager.TryGetGridAndVoxel(coordinates, out _, out Voxel voxel))
                yield break;

            // Get all neighbors and their associated information
            foreach (TraversableVoxel neighbor in WalkableNeighborsOf(voxel))
                yield return neighbor;
        }

        /// <summary>
        /// Returns walkable neighbors for a specific voxel.
        /// </summary>
        public static IEnumerable<TraversableVoxel> WalkableNeighborsOf(Voxel voxel)
        {
            // Get all neighbors and their associated information
            foreach ((LinearDirection direction, Voxel neighbor) in voxel.GetNeighbors())
            {
                if (neighbor == null) continue;

                // Skip blocked neighbors or neighbors that do not have a path partition
                if (neighbor.IsBlocked || !neighbor.TryGetPartition(out PathPartition neighborPartition))
                    continue;

                yield return new TraversableVoxel()
                {
                    Voxel = neighbor,
                    Partition = neighborPartition,
                    Direction = direction
                };
            }
        }

        /// <summary>
        /// Returns all walkable straight (orthogonal) neighbors of the partition’s voxel.
        /// </summary>
        public static IEnumerable<TraversableVoxel> GetWalkableStraightNeighbors(GlobalVoxelIndex coordinates)
        {
            if (!GlobalGridManager.TryGetGridAndVoxel(coordinates, out _, out Voxel voxel))
                yield break;

            // Get all neighbors and their associated information
            foreach (TraversableVoxel neighbor in WalkableStraightNeighborsOf(voxel))
                yield return neighbor;
        }

        /// <summary>
        /// Returns straight walkable neighbors for a specific voxel.
        /// </summary>
        public static IEnumerable<TraversableVoxel> WalkableStraightNeighborsOf(Voxel voxel)
        {
            foreach (LinearDirection direction in Enum.GetValues(typeof(StraightNeighbors)))
            {
                if (!voxel.TryGetNeighborFromDirection(direction, out Voxel neighbor))
                    continue;

                // Skip blocked neighbors or neighbors that do not have a path partition
                if (neighbor.IsBlocked || !neighbor.TryGetPartition(out PathPartition neighborPartition))
                    continue;

                yield return new TraversableVoxel()
                {
                    Voxel = neighbor,
                    Partition = neighborPartition,
                    Direction = direction
                };
            }
        }

        /// <summary>
        /// Returns all walkable diagonal neighbors of the partition’s voxel.
        /// </summary>
        public static IEnumerable<TraversableVoxel> GetWalkableDiagonalNeighbors(GlobalVoxelIndex coordinates)
        {
            if (!GlobalGridManager.TryGetGridAndVoxel(coordinates, out _, out Voxel voxel))
                yield break;

            // Get all neighbors and their associated information
            foreach (TraversableVoxel neighbor in WalkableDiagonalNeighborsOf(voxel))
                yield return neighbor;
        }

        /// <summary>
        /// Returns diagonal walkable neighbors for a specific voxel, avoiding blocked adjacent edges.
        /// </summary>
        public static IEnumerable<TraversableVoxel> WalkableDiagonalNeighborsOf(Voxel voxel)
        {
            foreach (LinearDirection direction in Enum.GetValues(typeof(DiagonalNeighbors)))
            {
                if (!voxel.TryGetNeighborFromDirection(direction, out Voxel neighbor))
                    continue;

                // Skip blocked neighbors or neighbors that do not have a path partition
                if (neighbor.IsBlocked || !neighbor.TryGetPartition(out PathPartition neighborPartition))
                    continue;

                // Check for edge neighbors that share an edge with the diagonal neighbor
                if (!HasBlockedEdgeNeighbor(voxel, direction))
                    yield return new TraversableVoxel()
                    {
                        Voxel = neighbor,
                        Partition = neighborPartition,
                        Direction = direction
                    };
            }
        }

        /// <summary>
        /// Determines if the given direction is considered straight (orthogonal).
        /// </summary>
        public static bool IsStraightNeighbor(LinearDirection direction)
        {
            return direction switch
            {
                LinearDirection.West
                or LinearDirection.South
                or LinearDirection.East
                or LinearDirection.North
                or LinearDirection.Below
                or LinearDirection.Above => true,
                _ => false,
            };
        }

        /// <summary>
        /// Determines if the given direction is considered diagonal.
        /// </summary>
        public static bool IsDiagnolNeighbor(LinearDirection direction)
        {
            return direction switch
            {
                LinearDirection.SouthWest
                or LinearDirection.NorthWest
                or LinearDirection.SouthEast
                or LinearDirection.NorthEast
                or LinearDirection.BelowWest
                or LinearDirection.BelowSouth
                or LinearDirection.BelowEast
                or LinearDirection.BelowNorth
                or LinearDirection.BelowSouthWest
                or LinearDirection.BelowNorthWest
                or LinearDirection.BelowSouthEast
                or LinearDirection.BelowNorthEast
                or LinearDirection.AboveWest
                or LinearDirection.AboveSouth
                or LinearDirection.AboveEast
                or LinearDirection.AboveNorth
                or LinearDirection.AboveSouthWest
                or LinearDirection.AboveNorthWest
                or LinearDirection.AboveSouthEast
                or LinearDirection.AboveNorthEast => true,
                _ => false,
            };
        }

        #endregion
    }
}

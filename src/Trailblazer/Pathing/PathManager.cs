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
    /// as well as providing global pathfinding utilities like node validation,
    /// path necessity checks, and partition pooling.
    /// </summary>
    public static class PathManager
    {
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
        /// Attempts to get valid start and end nodes based on provided world positions.
        /// Falls back to the closest walkable neighbor if necessary.
        /// </summary>
        /// <param name="start">The start position in world space.</param>
        /// <param name="end">The end position in world space.</param>
        /// <param name="startNode">Resolved start node.</param>
        /// <param name="endNode">Resolved end node.</param>
        /// <returns>True if both nodes were resolved successfully; otherwise, false.</returns>
        public static bool GetValidPathRequest(
            Vector3d start, 
            Vector3d end, 
            out Node startNode, 
            out Node endNode)
        {
            endNode = null;
            if (!GlobalGridManager.TryGetGridAndNode(start, out _, out startNode))
            {
                Console.WriteLine("Unable to find a valid start node for {startPos}");
                return false;
            }

            if (startNode.IsBlocked || !startNode.TryGetPartition<PathPartition>(out _))
            {
                if (!NodeFinder.TryGetClosestWalkableNeighbor(startNode, out Node closestNeighbor))
                    return false;
                startNode = closestNeighbor;
            }

            if (!GlobalGridManager.TryGetGridAndNode(end, out _, out endNode))
            {
                Console.WriteLine("Unable to find a valid end node for {targetPos}");
                return false;
            }

            if (endNode.IsBlocked || !endNode.TryGetPartition<PathPartition>(out _))
            {
                if (!NodeFinder.TryGetClosestWalkableNeighbor(endNode, out Node closestNeighbor))
                    return false;
                endNode = closestNeighbor;
            }

            return true;
        }

        /// <summary>
        /// Determines the maximum number of nodes to search based on the start and end node's grid sizes.
        /// </summary>
        /// <param name="start">The start node.</param>
        /// <param name="end">The end node.</param>
        /// <param name="maxSearchSize">The output max search size.</param>
        /// <returns>True if both nodes belong to valid grids; otherwise, false.</returns>
        public static bool GetMaxSearchSize(Node start, Node end, out int maxSearchSize)
        {
            if (!GlobalGridManager.TryGetGrid(start.GlobalCoordinates.GridIndex, out Grid startGrid) 
                || !GlobalGridManager.TryGetGrid(end.GlobalCoordinates.GridIndex, out Grid endGrid))
            {
                maxSearchSize = 0;
                return false;
            }

            maxSearchSize = startGrid.SpawnToken == endGrid.SpawnToken ? startGrid.Size : startGrid.Size + endGrid.Size;
            return true;
        }

        /// <summary>
        /// Checks if a path is needed between the start and end positions based on traced nodes and unit size.
        /// </summary>
        /// <param name="startPos">The starting position.</param>
        /// <param name="endPos">The destination position.</param>
        /// <param name="unitSize">The size of the navigating unit.</param>
        /// <param name="allowUnwalkable">Whether to permit unwalkable nodes.</param>
        /// <returns>True if a path is required; otherwise, false.</returns>
        public static bool NeedsPath(Vector3d startPos, Vector3d endPos, Fixed64 unitSize, bool allowUnwalkable = false)
        {
            foreach (GridNodeSet gridNodeSet in GridTracer.TraceLine(startPos, endPos))
            {
                foreach (Node node in gridNodeSet.Nodes)
                {
                    // A path is required if a node doesn't exist in the traced line
                    if (!node.TryGetPartition(out PathPartition partition))
                        return true;

                    if (!allowUnwalkable && !node.IsBlocked && partition.Unpassable(unitSize))
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
        /// Initializes all registered navigation maps by assigning walkable nodes to partitions.
        /// </summary>
        public static void InitializeAllMaps()
        {
            foreach (NavigationChart navMap in AllMaps)
                InitializeMap(navMap.Name);
        }

        /// <summary>
        /// Initializes a specific navigation map, assigning nodes to partitions.
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
                if (!GlobalGridManager.TryGetGridAndNode(pos, out _, out Node node))
                    continue;

                if (!node.TryGetPartition(out PathPartition partition))
                {
                    partition = PartitionPool.Rent();
                    node.TryAddPartition(partition);
                }

                partition.AddOwner(map.Name);
            }

            map.IsInitialized = true;
        }

        /// <summary>
        /// Unloads all registered maps, removing ownerships and partitions from walkable nodes.
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
                if (!GlobalGridManager.TryGetGridAndNode(position, out _, out Node node))
                    continue;

                if (node.TryGetPartition(out PathPartition partition) && partition.BelongsTo(name))
                {
                    partition.RemoveOwner(name);
                    if (!partition.HasAnyOwners)
                        node.TryRemovePartition<PathPartition>();
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
        /// <param name="currentNode">The current node.</param>
        /// <param name="diagonalIndex">The index of the diagonal neighbor in the 3x3x3 grid.</param>
        /// <returns>True if any edge neighbors are blocked; otherwise, false.</returns>
        private static bool HasBlockedEdgeNeighbor(Node currentNode, LinearDirection diagonalIndex)
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

                if (currentNode.TryGetNeighborFromDirection(edgeDirection, out Node edgeNeighbor))
                {
                    // Check if the edge neighbor is blocked or not walkable
                    if (edgeNeighbor.IsBlocked)
                        return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns all walkable neighbors of the partition’s current node.
        /// </summary>
        public static IEnumerable<TraversableNode> GetWalkableNeighbors(CoordinatesGlobal coordinates)
        {
            if (!GlobalGridManager.TryGetGridAndNode(coordinates, out _, out Node node))
                yield break;

            // Get all neighbors and their associated information
            foreach (TraversableNode neighbor in WalkableNeighborsOf(node))
                yield return neighbor;
        }

        /// <summary>
        /// Returns walkable neighbors for a specific node.
        /// </summary>
        public static IEnumerable<TraversableNode> WalkableNeighborsOf(Node node)
        {
            // Get all neighbors and their associated information
            foreach ((LinearDirection direction, Node neighbor) in node.GetNeighbors())
            {
                if (neighbor == null) continue;

                // Skip blocked neighbors or neighbors that do not have a path partition
                if (neighbor.IsBlocked || !neighbor.TryGetPartition(out PathPartition neighborPartition))
                    continue;

                yield return new TraversableNode()
                {
                    Node = neighbor,
                    Partition = neighborPartition,
                    Direction = direction
                };
            }
        }

        /// <summary>
        /// Returns all walkable straight (orthogonal) neighbors of the partition’s node.
        /// </summary>
        public static IEnumerable<TraversableNode> GetWalkableStraightNeighbors(CoordinatesGlobal coordinates)
        {
            if (!GlobalGridManager.TryGetGridAndNode(coordinates, out _, out Node node))
                yield break;

            // Get all neighbors and their associated information
            foreach (TraversableNode neighbor in WalkableStraightNeighborsOf(node))
                yield return neighbor;
        }

        /// <summary>
        /// Returns straight walkable neighbors for a specific node.
        /// </summary>
        public static IEnumerable<TraversableNode> WalkableStraightNeighborsOf(Node node)
        {
            foreach (LinearDirection direction in Enum.GetValues(typeof(StraightNeighbors)))
            {
                if (!node.TryGetNeighborFromDirection(direction, out Node neighbor))
                    continue;

                // Skip blocked neighbors or neighbors that do not have a path partition
                if (neighbor.IsBlocked || !neighbor.TryGetPartition(out PathPartition neighborPartition))
                    continue;

                yield return new TraversableNode()
                {
                    Node = neighbor,
                    Partition = neighborPartition,
                    Direction = direction
                };
            }
        }

        /// <summary>
        /// Returns all walkable diagonal neighbors of the partition’s node.
        /// </summary>
        public static IEnumerable<TraversableNode> GetWalkableDiagonalNeighbors(CoordinatesGlobal coordinates)
        {
            if (!GlobalGridManager.TryGetGridAndNode(coordinates, out _, out Node node))
                yield break;

            // Get all neighbors and their associated information
            foreach (TraversableNode neighbor in WalkableDiagonalNeighborsOf(node))
                yield return neighbor;
        }

        /// <summary>
        /// Returns diagonal walkable neighbors for a specific node, avoiding blocked adjacent edges.
        /// </summary>
        public static IEnumerable<TraversableNode> WalkableDiagonalNeighborsOf(Node node)
        {
            foreach (LinearDirection direction in Enum.GetValues(typeof(DiagonalNeighbors)))
            {
                if (!node.TryGetNeighborFromDirection(direction, out Node neighbor))
                    continue;

                // Skip blocked neighbors or neighbors that do not have a path partition
                if (neighbor.IsBlocked || !neighbor.TryGetPartition(out PathPartition neighborPartition))
                    continue;

                // Check for edge neighbors that share an edge with the diagonal neighbor
                if (!HasBlockedEdgeNeighbor(node, direction))
                    yield return new TraversableNode()
                    {
                        Node = neighbor,
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

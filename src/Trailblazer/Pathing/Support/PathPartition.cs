using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Collections.Generic;

namespace Trailblazer.Pathing
{
    /// <summary>
    /// Represents a partition attached to a Node that provides additional data used during pathfinding,
    /// such as clearance information, movement cost, and neighbor traversal helpers.
    /// </summary>
    public class PathPartition : INodePartition
    {
        #region Constants

        /// <summary>
        /// Cost applied for straight (orthogonal) pathfinding moves.
        /// </summary>
        public const int StraightCost = 100;

        /// <summary>
        /// Cost applied for diagonal pathfinding moves.
        /// </summary>
        public const int DiagonalCost = 141;

        /// <summary>
        /// Default value indicating unlimited clearance in degrees.
        /// </summary>
        public static readonly Fixed64 DefaultDegree = Fixed64.MAX_VALUE;

        /// <summary>
        /// Maximum clearance degree allowed for valid traversal.
        /// </summary>
        public static readonly Fixed64 DefaultDegreeCap = (Fixed64)8;

        #endregion

        /// <summary>
        /// The global coordinate of the node this partition is attached to.
        /// </summary>
        public CoordinatesGlobal ParentCoordinate { get; set; }

        /// <summary>
        /// The spawn token that uniquely identifies this node.
        /// </summary>
        public int NodeSpawnToken { get; private set; }

        /// <summary>
        /// The world-space position of the node.
        /// </summary>
        public Vector3d NodePosition { get; private set; }

        /// <summary>
        /// Indicates whether the node has been partitioned and is in use.
        /// </summary>
        public bool IsPartitioned { get; set; }

        /// <summary>
        /// The direction used when calculating neighbor clearance.
        /// </summary>
        [Transient]
        public LinearDirection ClearanceDirection { get; private set; }

        /// <summary>
        /// The number of traversable connections until the nearest unwalkable node.
        /// </summary>
        [Transient]
        public Fixed64 ClearanceDegree { get; private set; }

        /// <summary>
        /// Indicates whether the clearance degree has been computed and is valid.
        /// </summary>
        [Transient]
        public bool IsClearanceValid { get; private set; }

        #region Astar Properties

        /// <summary>
        /// The movement penalty cost of this node during A* pathfinding.
        /// </summary>
        [Transient]
        public int MovementCost { get; set; }

        /// <summary>
        /// The next node in the trail path, used during A* traversal.
        /// </summary>
        [Transient]
        public CoordinatesGlobal? NextTrailCoordinate { get; set; } = null;

        #endregion

        #region Heap Helpers

        /// <summary>
        /// The combined cost for use in pathfinding heap prioritization.
        /// </summary>
        [Transient]
        public int HeapCost { get; set; }

        /// <summary>
        /// A version used to distinguish between heap insertions across frames.
        /// </summary>
        [Transient]
        public uint HeapVersion { get; set; }

        /// <summary>
        /// A version used to track closed nodes in the heap for the current search.
        /// </summary>
        [Transient]
        public uint ClosedHeapVersion { get; set; }

        /// <summary>
        /// The index of this node in the heap.
        /// </summary>
        [Transient]
        public uint HeapIndex { get; set; }

        #endregion

        /// <summary>
        /// Maps that currently include this partition as part of their traversable space.
        /// </summary>
        private readonly SwiftHashSet<string> _mapOwners = new();

        /// <inheritdoc cref="_mapOwners">
        public SwiftHashSet<string> MapOwners => _mapOwners;

        /// <summary>
        /// Called when this partition is attached to a node, initializing key references and state.
        /// </summary>
        public void OnAddToNode(Node node)
        {
            node.OnObstacleChange += HandleChange;

            ParentCoordinate = node.GlobalCoordinates;
            NodeSpawnToken = node.SpawnToken;
            NodePosition = node.WorldPosition;

            ClearanceDegree = Fixed64.MAX_VALUE;
            ClearanceDirection = LinearDirection.None;

            IsPartitioned = true;
        }

        /// This will call <see cref="Reset"/> as an action on release
        public void OnRemoveFromNode(Node node)
        {
            node.OnObstacleChange -= HandleChange;

            PathManager.PartitionPool.Release(this);
        }

        /// <summary>
        /// Resets this partition's internal state, preparing it for reuse or reattachment.
        /// </summary>
        public void Reset()
        {
            ParentCoordinate = default;
            NodeSpawnToken = 0;

            IsClearanceValid = false;

            ClearanceDegree = DefaultDegree;
            ClearanceDirection = LinearDirection.None;

            MovementCost = 0;
            NextTrailCoordinate = null;

            _mapOwners.Clear();

            IsPartitioned = false;
        }

        /// <summary>
        /// Handles any obstacle changes on the associated node and invalidates clearance as needed.
        /// </summary>
        public void HandleChange(GridChange changeType, Node node)
        {
            // regardless of change type, we need to update clearance

            IsClearanceValid = false;
            CheckNeighborClearance();
        }

        /// <summary>
        /// If this unit is too fat to fit.
        /// </summary>
        internal bool Unpassable(Fixed64 size)
        {
            if (size <= Fixed64.Zero) return false;

            //  If there's an unwalkable within the size's number of connections, the unit cannot pass
            CheckNeighborClearance();
            return size > ClearanceDegree;
        }

        /// <summary>
        /// Returns the cached or recalculated clearance value to nearby obstacles.
        /// </summary>
        public Fixed64 GetNeighborClearance()
        {
            CheckNeighborClearance();
            return ClearanceDegree;
        }

        /// <summary>
        /// Validates or recalculates the clearance degree from nearby nodes.
        /// </summary>
        private void CheckNeighborClearance()
        {
            if (IsClearanceValid)
                return;

            if (!GlobalGridManager.TryGetGridAndNode(ParentCoordinate, out Grid grid, out Node node))
            {
                Console.WriteLine($"Invalidate coordiante provided to setup partition: {ParentCoordinate}");
                return;
            }

            if (node.IsBlocked)
            {
                ClearanceDegree = Fixed64.Zero;
                ClearanceDirection = LinearDirection.None;
                IsClearanceValid = true;
                return;
            }

            //  refresh source in case the map changed
            if (node.TryGetNeighborFromDirection(ClearanceDirection, out Node source)
                && source.TryGetPartition(out PathPartition sourcePartition))
            {
                Fixed64 prevSourceDegree = sourcePartition.ClearanceDegree;
                if (sourcePartition.ClearanceDegree < ClearanceDegree)
                {
                    sourcePartition.CheckNeighborClearance();

                    if (sourcePartition.ClearanceDegree != prevSourceDegree)
                    {
                        // Clearance from direction can no longer be trusted!
                        ClearanceDegree = DefaultDegree;
                        ClearanceDirection = LinearDirection.None;
                    }
                }
                else
                    ClearanceDegree = sourcePartition.ClearanceDegree + Fixed64.One;
            }

            //This method isn't always 100% accurate but after several updates, it will have a better picture of the map
            //TODO: Test this thoroughly and visualize
            foreach (LinearDirection direction in Enum.GetValues(typeof(LinearDirection)))
            {
                if (!node.TryGetNeighborFromDirection(direction, out Node neighbor)
                    || neighbor.IsBlocked
                    || !neighbor.TryGetPartition(out PathPartition neighborPartition))
                {
                    ClearanceDegree = Fixed64.One;
                    ClearanceDirection = direction;
                    break;
                }

                if (neighborPartition.ClearanceDegree < ClearanceDegree && neighborPartition.ClearanceDegree < DefaultDegreeCap)
                {
                    //  Cap clearance to 8. Something larger than that won't work very well with pathfinding.
                    ClearanceDegree = neighborPartition.ClearanceDegree + Fixed64.One;
                    ClearanceDirection = direction;
                }
            }

            IsClearanceValid = true;
        }

        #region TraversableNavMap Management

        /// <summary>
        /// Registers the map name as one that owns this partition.
        /// </summary>
        public void AddOwner(string mapName) => _mapOwners.Add(mapName);

        /// <summary>
        /// Removes the map name from those that reference this partition.
        /// </summary>
        public void RemoveOwner(string mapName) => _mapOwners.Remove(mapName);

        /// <summary>
        /// Returns true if any map currently references this partition.
        /// </summary>
        public bool HasAnyOwners => _mapOwners.Count > 0;

        /// <summary>
        /// Returns true if the partition is claimed by the given map name.
        /// </summary>
        public bool BelongsTo(string mapName) => _mapOwners.Contains(mapName);

        #endregion

        /// <summary>
        /// Calculates the heuristic cost for the current node based on the target node and the heuristic method used.
        /// This implementation takes into account the X, Y, and Z axes for pathfinding.
        /// </summary>
        public static int CalculateHeuristic(
            Vector3d currentNode,
            Vector3d targetNode,
            HeuristicMethod heuristicMethod)
        {
            Fixed64 heuristicCost = Fixed64.MAX_VALUE;

            // Calculate the absolute distance in each axis
            Vector3d dst = Vector3d.Abs(currentNode - targetNode);

            switch (heuristicMethod)
            {
                case HeuristicMethod.Manhattan:
                    // Sum the distances and multiply by 100 for the heuristic cost
                    heuristicCost = (dst.x + dst.y + dst.z) * StraightCost;
                    break;
                case HeuristicMethod.Octile:
                    // Find the max of the three distances
                    Fixed64 maxXY = FixedMath.Max(dst.x, dst.y);
                    Fixed64 max = FixedMath.Max(maxXY, dst.z);
                    // Calculate the heuristic cost using the max and sum of other distances
                    heuristicCost = (max * DiagonalCost) + ((dst.x + dst.y + dst.z - max - max) * StraightCost);
                    break;
                case HeuristicMethod.Euclidean:
                    // Calculate the squared distance and find the square root
                    Fixed64 d = dst.x * dst.x + dst.y * dst.y + dst.z * dst.z;
                    d = FixedMath.Sqrt(d);
                    // Multiply the result by 100 for the heuristic cost
                    heuristicCost = d * StraightCost;
                    break;
                default:
                    break;
            }

            return heuristicCost.CeilToInt();
        }

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
        public IEnumerable<TraversableNeighbor> GetWalkableNeighbors()
        {
            if (!GlobalGridManager.TryGetGridAndNode(ParentCoordinate, out _, out Node node))
                yield break;

            // Get all neighbors and their associated information
            foreach (TraversableNeighbor neighbor in WalkableNeighborsOf(node))
                yield return neighbor;
        }

        /// <summary>
        /// Returns walkable neighbors for a specific node.
        /// </summary>
        public static IEnumerable<TraversableNeighbor> WalkableNeighborsOf(Node node)
        {
            // Get all neighbors and their associated information
            foreach ((LinearDirection direction, Node neighbor) in node.GetNeighbors())
            {
                if (neighbor == null) continue;

                // Skip blocked neighbors or neighbors that do not have a path partition
                if (neighbor.IsBlocked || !neighbor.TryGetPartition(out PathPartition neighborPartition))
                    continue;

                yield return new TraversableNeighbor()
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
        public IEnumerable<TraversableNeighbor> GetWalkableStraightNeighbors()
        {
            if (!GlobalGridManager.TryGetGridAndNode(ParentCoordinate, out _, out Node node))
                yield break;

            // Get all neighbors and their associated information
            foreach (TraversableNeighbor neighbor in WalkableStraightNeighborsOf(node))
                yield return neighbor;
        }

        /// <summary>
        /// Returns straight walkable neighbors for a specific node.
        /// </summary>
        public static IEnumerable<TraversableNeighbor> WalkableStraightNeighborsOf(Node node)
        {
            foreach (LinearDirection direction in Enum.GetValues(typeof(StraightNeighbors)))
            {
                if (!node.TryGetNeighborFromDirection(direction, out Node neighbor))
                    continue;

                // Skip blocked neighbors or neighbors that do not have a path partition
                if (neighbor.IsBlocked || !neighbor.TryGetPartition(out PathPartition neighborPartition))
                    continue;

                yield return new TraversableNeighbor()
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
        public IEnumerable<TraversableNeighbor> GetWalkableDiagonalNeighbors()
        {
            if (!GlobalGridManager.TryGetGridAndNode(ParentCoordinate, out _, out Node node))
                yield break;

            // Get all neighbors and their associated information
            foreach (TraversableNeighbor neighbor in WalkableDiagonalNeighborsOf(node))
                yield return neighbor;
        }

        /// <summary>
        /// Returns diagonal walkable neighbors for a specific node, avoiding blocked adjacent edges.
        /// </summary>
        public static IEnumerable<TraversableNeighbor> WalkableDiagonalNeighborsOf(Node node)
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
                    yield return new TraversableNeighbor()
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

        public override int GetHashCode() => NodeSpawnToken;
    }
}

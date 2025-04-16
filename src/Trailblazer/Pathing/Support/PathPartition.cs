using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Collections.Generic;
using Trailblazer.Controllers.Locomotions;

namespace Trailblazer.Pathing
{
    public class PathPartition : INodePartition
    {
        #region Constants

        public const int StraightCost = 100;

        public const int DiagonalCost = 141;

        public const byte DefaultDegree = byte.MaxValue;

        public const LinearDirection DefaultSource = LinearDirection.None;

        #endregion

        public CoordinatesGlobal ParentCoordinate { get; set; }

        public int NodeSpawnToken { get; set; }

        public Vector3d NodePosition { get; set; }

        public bool IsPartitioned { get; set; }

        [Transient]
        public LinearDirection ClearanceSource { get; private set; }

        /// <summary>
        /// How many connections until the closest unwalkable node.
        /// If a big unit stands directly on this node, it won't be able to fit if the degree is too low.
        /// </summary>
        [Transient]
        public byte ClearanceDegree { get; private set; }

        #region Astar Properties

        [Transient]
        public int MovementCost { get; set; }

        [Transient]
        public CoordinatesGlobal? NextTrailCoordinate { get; set; } = null;

        #endregion

        #region Flow Field Properties

        [Transient]
        public bool HasLineOfSight { get; set; }

        #endregion

        #region Heap Helpers

        [Transient]
        public int HeapCost { get; set; }

        [Transient]
        public uint HeapVersion { get; set; }

        [Transient]
        public uint ClosedHeapVersion { get; set; }

        [Transient]
        public uint HeapIndex { get; set; }

        #endregion

        private readonly SwiftHashSet<string> _mapOwners = new();

        public void OnAddToNode(Node node)
        {
            node.OnObstacleChange += HandleChange;

            ParentCoordinate = node.GlobalCoordinates;
            NodeSpawnToken = node.SpawnToken;
            NodePosition = node.WorldPosition;

            ClearanceDegree = DefaultDegree;
            ClearanceSource = DefaultSource;

            IsPartitioned = true;
        }

        /// This will call <see cref="Reset"/> as an action on release
        public void OnRemoveFromNode(Node node)
        {
            node.OnObstacleChange -= HandleChange;

            PathingManager.PartitionPool.Release(this);
        }

        public void Reset()
        {
            ParentCoordinate = default;
            NodeSpawnToken = 0;

            MovementCost = 0;
            NextTrailCoordinate = null;

            HasLineOfSight = false;

            _mapOwners.Clear();

            IsPartitioned = false;
        }

        public void HandleChange(GridChange changeType, Node node)
        {
            // regardless of change type, we need to update clearance
            UpdateNeighborClearance();
        }

        /// <summary>
        /// If this unit is too fat to fit.
        /// </summary>
        internal bool Unpassable(int size)
        {
            if (size <= 0) return false;

            //  If there's an unwalkable within the size's number of connections, the unit cannot pass
            UpdateNeighborClearance();
            return size > ClearanceDegree;
        }

        public byte GetNeighborClearance()
        {
            UpdateNeighborClearance();
            return ClearanceDegree;
        }

        private void UpdateNeighborClearance()
        {
            if (!GlobalGridManager.TryGetGridAndNode(ParentCoordinate, out Grid grid, out Node node))
            {
                Console.WriteLine($"Invalidate coordiante provided to setup partition: {ParentCoordinate}");
                return;
            }

            if (node.CachedGridVersion == grid.Version)
                return; // nothing should have changed

            if (node.IsBlocked)
            {
                ClearanceDegree = 0;
                ClearanceSource = DefaultSource;
                return;
            }

            //  refresh source in case the map changed
            if (node.TryGetNeighborFromDirection(ClearanceSource, out Node source)
                && source.TryGetPartition(out PathPartition sourcePartition))
            {
                byte prevSourceDegree = sourcePartition.ClearanceDegree;
                if (sourcePartition.ClearanceDegree < ClearanceDegree)
                {
                    sourcePartition.UpdateNeighborClearance();
                    //Clearance from source can no longer be trusted!
                    if (sourcePartition.ClearanceDegree != prevSourceDegree)
                    {
                        ClearanceDegree = DefaultDegree;
                        ClearanceSource = DefaultSource;
                    }
                }
                else
                    ClearanceDegree = (byte)(sourcePartition.ClearanceDegree + 1);
            }

            //This method isn't always 100% accurate but after several updates, it will have a better picture of the map
            //TODO: Test this thoroughly and visualize
            foreach (LinearDirection direction in Enum.GetValues(typeof(LinearDirection)))
            {
                if (!node.TryGetNeighborFromDirection(direction, out Node neighbor)
                    || neighbor.IsBlocked
                    || !neighbor.TryGetPartition(out PathPartition neighborPartition))
                {
                    ClearanceDegree = 1;
                    ClearanceSource = direction;
                    break;
                }

                if (neighborPartition.ClearanceDegree < ClearanceDegree && neighborPartition.ClearanceDegree < 8)
                {
                    //  Cap clearance to 8. Something larger than that won't work very well with pathfinding.
                    ClearanceDegree = (byte)(neighborPartition.ClearanceDegree + 1);
                    ClearanceSource = direction;
                }
            }
        }

        #region TraversableNavMap Management

        public void AddOwner(string mapName) => _mapOwners.Add(mapName);
        public void RemoveOwner(string mapName) => _mapOwners.Remove(mapName);
        public bool HasAnyOwners => _mapOwners.Count > 0;
        public bool BelongsTo(string mapName) => _mapOwners.Contains(mapName);

        #endregion

        public override int GetHashCode() => NodeSpawnToken;

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

        public IEnumerable<TraversableNeighbor> GetWalkableNeighbors()
        {
            if (!GlobalGridManager.TryGetGridAndNode(ParentCoordinate, out _, out Node node))
                yield break;

            // Get all neighbors and their associated information
            foreach (TraversableNeighbor neighbor in WalkableNeighborsOf(node))
                yield return neighbor;
        }

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

        public IEnumerable<TraversableNeighbor> GetWalkableStraightNeighbors()
        {
            if (!GlobalGridManager.TryGetGridAndNode(ParentCoordinate, out _, out Node node))
                yield break;

            // Get all neighbors and their associated information
            foreach (TraversableNeighbor neighbor in WalkableStraightNeighborsOf(node))
                yield return neighbor;
        }

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

        public IEnumerable<TraversableNeighbor> GetWalkableDiagonalNeighbors()
        {
            if (!GlobalGridManager.TryGetGridAndNode(ParentCoordinate, out _, out Node node))
                yield break;

            // Get all neighbors and their associated information
            foreach (TraversableNeighbor neighbor in WalkableDiagonalNeighborsOf(node))
                yield return neighbor;
        }

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
    }
}

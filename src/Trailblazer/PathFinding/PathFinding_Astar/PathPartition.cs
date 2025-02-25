using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;

//TODO: pool partitions similiar to gridmanager
namespace Lockstep.Simulation.Pathfinding
{
    public enum HeuristicMethod
    {
        manhattan,
        octile,
        euclidean
    }

    public struct WalkableNode
    {
        public Node Node;
        public int Cost;
    }

    public class PathPartition : INodePartition
    {
        #region Constants

        public const int StraightCost = 100;

        public const int DiagonalCost = 141;

        public const byte DefaultDegree = byte.MaxValue;

        public const byte DefaultSource = byte.MaxValue;

        #endregion

        public CoordinatesGlobal ParentCoordinate { get; set; }

        public bool IsPartitioned { get; set; }

        public Fixed64 MovementCost { get; set; }

        public Fixed64 HeuristicCost { get; set; }

        public Fixed64 TotalCost { get; set; }

        public CoordinatesGlobal TrailNextCoordinate { get; set; }

        public byte ClearanceSource { get; private set; }

        /// <summary>
        /// How many connections until the closest unwalkable node.
        /// If a big unit stands directly on this node, it won't be able to fit if the degree is too low.
        /// </summary>
        public byte ClearanceDegree { get; private set; }

        private int _cachedUnpassableCheckSize;

        //  This is the system used for groups of pathfinding queries to the same destination
        //  If a 2nd query finds its way onto a node found in the first query, it will use the rest of the first query
        public CoordinatesLocal CombineTrailNodeCoordinates;

        public uint? CombinePathVersion;

        #region Collection Helpers

        public uint HeapVersion { get; set; }

        public uint ClosedHeapVersion { get; set; }

        public uint HeapIndex { get; set; }

        #endregion

        public bool IsAllocated { get; set; }

        public void OnAddToNode(Node node)
        {
            node.OnObstacleChange += HandleChange;

            ClearanceDegree = DefaultDegree;
            ClearanceSource = DefaultSource;

            IsAllocated = true;
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
            _cachedUnpassableCheckSize = size;
            //If there's an unwalkable within the size's number of connections, the unit cannot pass
            if (_cachedUnpassableCheckSize > 0)
            {
                UpdateNeighborClearance();
                return _cachedUnpassableCheckSize > ClearanceDegree;
            }

            return false;
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

            if (ClearanceSource <= 26) //  Cap clearance to 26.
            {
                //  refresh source in case the map changed
                if (node.TryGetNeighborFromDirection((LinearDirection)ClearanceSource, out Node source)
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
            }

            foreach ((LinearDirection direction, Node neighbor) kvp in node.GetNeighbors())
            {
                if (kvp.neighbor.IsBlocked || !kvp.neighbor.TryGetPartition(out PathPartition partition))
                {
                    ClearanceDegree = 1;
                    ClearanceSource = (byte)kvp.direction;
                    break;
                }

                if (partition.ClearanceDegree < ClearanceDegree && partition.ClearanceDegree < 8)
                {
                    //  Cap clearance to 8. Something larger than that won't work very well with pathfinding.
                    ClearanceDegree = (byte)(partition.ClearanceDegree + 1);
                    ClearanceSource = (byte)kvp.direction;
                }
            }
        }

        /// <summary>
        /// Calculates the heuristic cost for the current node based on the target node and the heuristic method used.
        /// This implementation takes into account the X, Y, and Z axes for pathfinding.
        /// </summary>
        /// <param name="targetNode">The target node for pathfinding.</param>
        public static (Fixed64, Fixed64) CalculateHeuristic(
            Vector3d currentNode,
            Vector3d targetNode,
            Fixed64 movementCost,
            HeuristicMethod heuristicMethod)
        {
            Fixed64 heuristicCost = Fixed64.MaxValue;

            // Calculate the absolute distance in each axis
            Vector3d dst = Vector3d.Abs(currentNode - targetNode);

            switch (heuristicMethod)
            {
                case HeuristicMethod.manhattan:
                    // Sum the distances and multiply by 100 for the heuristic cost
                    heuristicCost = (dst.x + dst.y + dst.z) * StraightCost;
                    break;
                case HeuristicMethod.octile:
                    // Find the max of the three distances
                    Fixed64 maxXY = FixedMath.Max(dst.x, dst.y);
                    Fixed64 max = FixedMath.Max(maxXY, dst.z);
                    // Calculate the heuristic cost using the max and sum of other distances
                    heuristicCost = (max * DiagonalCost) + ((dst.x + dst.y + dst.z - max - max) * StraightCost);
                    break;
                case HeuristicMethod.euclidean:
                    // Calculate the squared distance and find the square root
                    Fixed64 d = dst.x * dst.x + dst.y * dst.y + dst.z * dst.z;
                    d = FixedMath.Sqrt(d);
                    // Multiply the result by 100 for the heuristic cost
                    heuristicCost = d * StraightCost;
                    break;
                default:
                    break;
            }

            // Calculate the total cost (fCost) by adding the heuristic cost (hCost) to the movement cost (gCost)
            return (heuristicCost, movementCost + heuristicCost);
        }

        /// <summary>
        /// Returns the unobstructed neighbours of the given grid location.
        /// </summary>
        /// <remarks>
        /// Diagonals are only included if their neighbours are also unobstructed
        /// </remarks>
        /// <param name="currentNode"></param>
        /// <returns></returns>
        public static SwiftList<WalkableNode> WalkableNeighborsOf(Node currentNode)
        {
            SwiftList<WalkableNode> unblockedNeighbors = new SwiftList<WalkableNode>();

            // Get all neighbors and their associated information
            foreach ((LinearDirection direction, Node neighbor) kvp in currentNode.GetNeighbors())
            {
                // Skip blocked neighbors or neighbors that do not have a path partition
                if (kvp.neighbor.IsBlocked || !kvp.neighbor.TryGetPartition(out PathPartition _))
                    continue;

                if (GlobalGridManager.IsDiagonalNeighbor((int)kvp.direction))
                {
                    // Check for edge neighbors that share an edge with the diagonal neighbor
                    if (!HasBlockedEdgeNeighbor(currentNode, (int)kvp.direction))
                    {
                        unblockedNeighbors.Add(new WalkableNode
                        {
                            Node = kvp.neighbor,
                            Cost = DiagonalCost
                        });
                    }
                }
                else
                {
                    // Straight neighbors
                    unblockedNeighbors.Add(new WalkableNode
                    {
                        Node = kvp.neighbor,
                        Cost = StraightCost
                    });
                }
            }

            return unblockedNeighbors;
        }

        /// <summary>
        /// Checks if any edge neighbors of a diagonal neighbor are blocked.
        /// </summary>
        /// <param name="currentNode">The current node.</param>
        /// <param name="diagonalIndex">The index of the diagonal neighbor in the 3x3x3 grid.</param>
        /// <returns>True if any edge neighbors are blocked; otherwise, false.</returns>
        private static bool HasBlockedEdgeNeighbor(Node currentNode, int diagonalIndex)
        {
            // Define the relative offsets for the two edge neighbors of each diagonal neighbor
            var edgeOffsets = diagonalIndex switch
            {
                4 => new[] { (x: -1, z: 0), (x: 0, z: -1) }, // South-West
                5 => new[] { (x: -1, z: 0), (x: 0, z: 1) },  // North-West
                6 => new[] { (x: 1, z: 0), (x: 0, z: -1) },  // South-East
                7 => new[] { (x: 1, z: 0), (x: 0, z: 1) },   // North-East
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

        // TODO: return to a pool
        public void OnRemoveFromNode(Node node)
        {
            node.OnObstacleChange -= HandleChange;
            IsAllocated = false;
        }
    }
}

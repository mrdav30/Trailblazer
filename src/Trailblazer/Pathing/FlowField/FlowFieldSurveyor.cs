using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System.Threading;
using System;

namespace Trailblazer.Pathing
{
    /// <summary>
    /// Responsible for generating flow fields using a wavefront flood-fill algorithm.
    /// Provides pathfinding data suitable for many agents with shared destinations.
    /// </summary>
    public class FlowFieldSurveyor
    {
        #region Singleton Instance

        /// <summary>
        /// A lazily initialized singleton instance of the pathfinder.
        /// </summary>
        private static readonly Lazy<FlowFieldSurveyor> _instance =
            new(() => new FlowFieldSurveyor(), LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// Gets the shared instance of the pathfinder.
        /// </summary>
        public static FlowFieldSurveyor Shared => _instance.Value;

        #endregion

        private FlowFieldPathRequest _request;

        /// <summary>
        /// The maximum distance found during the flood phase of path generation.
        /// Used to weight blending toward the goal.
        /// </summary>
        private int _greatestDistance;

        /// <summary>
        /// The distance from the end node to the starting node during flood fill.
        /// Used to define the flood radius.
        /// </summary>
        private int _distanceToStart;

        /// <summary>
        /// Tracks partitions that were affected during the flood fill.
        /// These will be used to construct the final flow field result.
        /// </summary>
        private readonly SwiftHashSet<PathPartition> _marked = new();

        /// <summary>
        /// Attempts to create a shared flow field path from the start to the end node specified in the request.
        /// </summary>
        /// <param name="request">A flow field path request containing the start, end, and search parameters.</param>
        /// <param name="result">A dictionary of flow fields indexed by spawn token.</param>
        /// <returns><c>true</c> if a valid path is found; otherwise <c>false</c>.</returns>
        public bool FindPath(FlowFieldPathRequest request, out SwiftDictionary<int, FlowField> result)
        {
            result = null;
            if (!request.End.TryGetPartition(out PathPartition targetPartition)
                || request.Start.SpawnToken == request.End.SpawnToken)
            {
                return false;
            }

            _request = request;

            _marked.Clear();
            PathHeap.FastClear();

            _greatestDistance = 0;
            _distanceToStart = 0;

            // Start from the end and move towards the start node
            PathHeap.Add(targetPartition);

            if(!FloodPath())
                return false;

            if (_marked.Count > 0)
                result = GenerateFlowFields(_request.End);

            return result.Count > 0;
        }

        /// <summary>
        /// Executes the wavefront expansion (flood fill) phase of the flow field generation algorithm.
        /// Starts from the goal and expands outward until the start node is reached or search range is exceeded.
        /// </summary>
        /// <returns><c>true</c> if the start node is reached within the maximum range; otherwise <c>false</c>.</returns>
        public bool FloodPath()
        {
            bool targetReached = false;

            int iterations = 0;
            while (PathHeap.RemoveFirst(out PathPartition current) 
                && iterations++ < _request.MaxPathSearchRange)
            {
                // Check if we found our way to the start node
                if (!targetReached && current.NodeSpawnToken == _request.End.SpawnToken)
                {
                    _distanceToStart = current.HeapCost;
                    targetReached = true;
                }

                if (current.HeapCost > _greatestDistance)
                    _greatestDistance = current.HeapCost;

                AnalyzeNeighborDistance(current, _request.UnitSize);

                PathHeap.SetClosed(current);

                if (targetReached && current.HeapCost > _distanceToStart + _request.FieldSearchRange)
                    return true;
            }

            return targetReached;
        }

        /// <summary>
        /// Evaluates each walkable neighbor of the current partition and assigns a heap cost if a shorter path is found.
        /// Ensures the wavefront expands in an optimal order.
        /// </summary>
        /// <param name="current">The current path partition being evaluated.</param>
        /// <param name="unitSize">The size of the navigating agent.</param>
        public void AnalyzeNeighborDistance(PathPartition current, Fixed64 unitSize)
        {
            // Check each straight line neighbour of this node (no diagonals)
            // We will only ever visit every node once as we are always visiting nodes in the most efficient order
            foreach (TraversableNode neighbor in PathManager.GetWalkableStraightNeighbors(current.ParentCoordinate))
            {
                if (PathHeap.IsClosed(neighbor.Partition) || neighbor.Partition.Unpassable(unitSize))
                    continue;

                int neighborToll = current.HeapCost + 1;
                if (!PathHeap.Contains(neighbor.Partition))
                {
                    neighbor.Partition.HeapCost = neighborToll;
                    PathHeap.Add(neighbor.Partition);
                    _marked.Add(neighbor.Partition);
                }
                else if (neighborToll < neighbor.Partition.HeapCost)
                {
                    neighbor.Partition.HeapCost = neighborToll;
                    PathHeap.SortUp(neighbor.Partition);
                }
            }
        }

        /// <summary>
        /// Converts the results of the flood fill phase into directional flow fields pointing toward the goal.
        /// Each partition is assigned a direction vector blending shortest path and direct-to-goal direction.
        /// </summary>
        /// <param name="end">The goal node for the flow field.</param>
        /// <returns>A dictionary of directional flow field data indexed by node spawn tokens.</returns>
        public SwiftDictionary<int, FlowField> GenerateFlowFields(Node end)
        {
            SwiftDictionary<int, FlowField> output = new();

            Fixed64 totalDistance = _distanceToStart + Fixed64.One; // total flood radius
            foreach (PathPartition current in _marked)
            {
                if (current.NodeSpawnToken == end.SpawnToken)
                {
                    // End node shouldn't point anywhere
                    output.Add(current.NodeSpawnToken, new FlowField()
                    {
                        Direction = Vector3d.Zero,
                        IsGoal = true
                    });
                    continue;
                }

                FlowField currentFlow = new()
                {
                    NodeCoordinates = current.ParentCoordinate,
                    DistanceToTarget = current.HeapCost
                };

                // Go through all neighbours and find the one with the lowest distance
                PathPartition minPartition = null;
                int minDistance = _greatestDistance;
                foreach(TraversableNode neighbor in PathManager.GetWalkableNeighbors(current.ParentCoordinate))
                {
                    // check closed heap version to ensure neighbor was part of flood phase
                    if (!PathHeap.IsClosed(neighbor.Partition)) 
                        continue;

                    int dist = neighbor.Partition.HeapCost - current.HeapCost;
                    if (dist < minDistance)
                    {
                        minPartition = neighbor.Partition;
                        minDistance = dist;
                    }
                }

                // If we found a valid neighbour, point in its direction by applying distance-weighted blending
                if (minPartition != null)
                {
                    Fixed64 alpha = FixedMath.Clamp01((totalDistance - currentFlow.DistanceToTarget) / totalDistance); // closer = alpha → 1

                    // blend with the lowest-cost vector
                    Vector3d direct = (end.WorldPosition - current.NodePosition).Normalize();
                    Vector3d field = (minPartition.NodePosition - current.NodePosition).Normalize();

                    Vector3d blended = field * alpha + direct * (Fixed64.One - alpha);

                    currentFlow.Direction = blended.Normalize();
                }

                output.Add(current.NodeSpawnToken, currentFlow);
            }

            return output;
        }

        /// <summary>
        /// Samples an interpolated flow direction from a given world position using bilinear interpolation.
        /// Helps agents move smoothly between grid cells.
        /// </summary>
        /// <param name="worldPosition">The world-space position to sample from.</param>
        /// <param name="fields">A dictionary of flow field data.</param>
        /// <returns>An interpolated directional vector.</returns>
        public static Vector3d SampleFlowVector(Vector3d worldPosition, SwiftDictionary<int, FlowField> fields)
        {
            // Get bottom-left corner of the square the agent is standing in
            Vector3d corner = new(
                FixedMath.Floor(worldPosition.x / GlobalGridManager.NodeSize) * GlobalGridManager.NodeSize,
                FixedMath.Floor(worldPosition.y / GlobalGridManager.NodeSize) * GlobalGridManager.NodeSize,
                FixedMath.Floor(worldPosition.z / GlobalGridManager.NodeSize) * GlobalGridManager.NodeSize
            );

            // Compute normalized offset in cell (0..1)
            Fixed64 dx = (worldPosition.x - corner.x) / GlobalGridManager.NodeSize;
            Fixed64 dz = (worldPosition.z - corner.z) / GlobalGridManager.NodeSize;

            // Sample the 4 surrounding node centers
            Vector3d bottomLeft = corner;
            Vector3d bottomRight = corner + new Vector3d(GlobalGridManager.NodeSize, Fixed64.Zero, Fixed64.Zero);
            Vector3d topLeft = corner + new Vector3d(Fixed64.Zero, Fixed64.Zero, GlobalGridManager.NodeSize);
            Vector3d topRight = corner + new Vector3d(GlobalGridManager.NodeSize, Fixed64.Zero, GlobalGridManager.NodeSize);

            // Get flow vectors
            Vector3d f00 = GetFlowVector(bottomLeft, fields);
            Vector3d f10 = GetFlowVector(bottomRight, fields);
            Vector3d f01 = GetFlowVector(topLeft, fields);
            Vector3d f11 = GetFlowVector(topRight, fields);

            // Bilinear interpolation
            Vector3d zHigh = f00 * (Fixed64.One - dx) + f10 * dx;
            Vector3d zLow = f01 * (Fixed64.One - dx) + f11 * dx;
            Vector3d blended = zHigh * (Fixed64.One - dz) + zLow * dz;

            blended.Normalize();
            return blended;
        }

        /// <summary>
        /// Attempts to locate the closest valid node from which to begin flow-based movement.
        /// Useful for finding an initial entry point to the flow field.
        /// </summary>
        /// <param name="origin">The world-space origin to search from.</param>
        /// <param name="flowFields">Flow field data indexed by node spawn token.</param>
        /// <param name="result">The closest valid node, if found.</param>
        /// <param name="range">Maximum range to search.</param>
        /// <returns><c>true</c> if a nearby flow field anchor is found; otherwise <c>false</c>.</returns>
        public static bool TryGetNearestFlowAnchor(
            Vector3d origin,
            SwiftDictionary<int, FlowField> flowFields,
            out Node result,
            double range)
        {
            result = null;
            Fixed64 minDistanceSq = new(range * range);
            bool found = false;

            foreach (FlowField flow in flowFields.Values)
            {
                if (!GlobalGridManager.TryGetGridAndNode(flow.NodeCoordinates, out _, out Node flowNode))
                    continue;

                Fixed64 distSq = Vector3d.SqrDistance(origin, flowNode.WorldPosition);
                if (distSq <= minDistanceSq)
                {
                    result = flowNode;
                    minDistanceSq = distSq;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Retrieves the raw directional flow vector at the given world-space position, if available.
        /// </summary>
        /// <param name="position">The position to query within the flow field.</param>
        /// <param name="fields">Flow field data indexed by spawn token.</param>
        /// <returns>The direction vector, or <c>Vector3d.Zero</c> if no field exists.</returns>
        public static Vector3d GetFlowVector(Vector3d position, SwiftDictionary<int, FlowField> fields)
        {
            if (GlobalGridManager.TryGetGridAndNode(position, out _, out Node node))
            {
                if (fields.TryGetValue(node.SpawnToken, out FlowField field))
                    return field.Direction;
            }
            return Vector3d.Zero;
        }
    }
}
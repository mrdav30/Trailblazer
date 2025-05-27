using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System.Threading;
using System;

namespace Trailblazer.Pathing
{
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

        private int _greatestDistance;

        private int _startNodeDistance;

        private readonly SwiftHashSet<PathPartition> _markedPartitions = new();

        public void FindPath(FlowFieldPathRequest request)
        {
            if (!request.TargetNode.TryGetPartition(out PathPartition targetPartition)
                || request.FromNode.SpawnToken == request.TargetNode.SpawnToken)
            {
                request.OnComplete?.Invoke(false, null);
                return;
            }

            PathPartitionHeap.FastClear();

            _markedPartitions.Clear();

            _greatestDistance = 0;
            _startNodeDistance = 0;

            // Start from the end and move towards the start node
            PathPartitionHeap.Add(targetPartition);

            if(!FloodPath(request))
            {
                request.OnComplete?.Invoke(false, null);
                return;
            }

            SwiftDictionary<int, FlowField> output = null;
            if (_markedPartitions.Count > 0)
                output = GenerateFlowFields(request);

            request.OnComplete?.Invoke(true, output);
        }

        public bool FloodPath(FlowFieldPathRequest request)
        {
            bool targetReached = false;

            int iterations = 0;
            while (PathPartitionHeap.RemoveFirst(out PathPartition current) && iterations < request.MaxSearchSize)
            {
                // Check if we found our way to the start node
                if (!targetReached && current.NodeSpawnToken == request.FromNode.SpawnToken)
                {
                    _startNodeDistance = current.HeapCost;
                    targetReached = true;
                }

                if (current.HeapCost > _greatestDistance)
                    _greatestDistance = current.HeapCost;

                AnalyzeNeighborDistance(current, request);

                PathPartitionHeap.SetClosed(current);

                if (targetReached && current.HeapCost > _startNodeDistance + request.SearchRange)
                    return true;
            }

            return targetReached;
        }

        /// <summary>                        
        /// Wavefront algorithm to create a distance field.
        /// </summary>
        /// <returns>
        /// Returns 
        /// <c>true</c> if path was found and necessary
        /// <c>false</c> if path to End is impossible or not found.
        /// </returns>
        public void AnalyzeNeighborDistance(PathPartition currentPartition, FlowFieldPathRequest request)
        {
            // Check each straight line neighbour of this node (no diagonals)
            // We will only ever visit every node once as we are always visiting nodes in the most efficient order
            foreach (TraversableNeighbor neighbor in currentPartition.GetWalkableStraightNeighbors())
            {
                if (PathPartitionHeap.IsClosed(neighbor.Partition) || neighbor.Partition.Unpassable(request.UnitSize))
                    continue;

                int neighborToll = currentPartition.HeapCost + 1;
                if (!PathPartitionHeap.Contains(neighbor.Partition))
                {
                    neighbor.Partition.HeapCost = neighborToll;
                    PathPartitionHeap.Add(neighbor.Partition);
                    _markedPartitions.Add(neighbor.Partition);
                }
                else if (neighborToll < neighbor.Partition.HeapCost)
                {
                    neighbor.Partition.HeapCost = neighborToll;
                    PathPartitionHeap.SortUp(neighbor.Partition);
                }
            }
        }
        
        public SwiftDictionary<int, FlowField> GenerateFlowFields(FlowFieldPathRequest request)
        {
            SwiftDictionary<int, FlowField> output = new();

            Fixed64 totalDistance = _startNodeDistance + Fixed64.One; // total flood radius
            foreach (PathPartition current in _markedPartitions)
            {
                if (current.NodeSpawnToken == request.TargetNode.SpawnToken)
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
                foreach(TraversableNeighbor neighbor in current.GetWalkableNeighbors())
                {
                    // check closed heap version to ensure neighbor was part of flood phase
                    if (!PathPartitionHeap.IsClosed(neighbor.Partition)) 
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
                    Vector3d direct = (request.TargetNode.WorldPosition - current.NodePosition).Normalize();
                    Vector3d field = (minPartition.NodePosition - current.NodePosition).Normalize();

                    Vector3d blended = field * alpha + direct * (Fixed64.One - alpha);

                    currentFlow.Direction = blended.Normalize();
                }

                output.Add(current.NodeSpawnToken, currentFlow);
            }

            return output;
        }


        /// <summary>
        /// Samples an interpolated flow vector from a 2D grid using bilinear interpolation.
        /// </summary>
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
        /// Attempts to locate the closest available flow field anchor node from a given position.
        /// </summary>
        public static bool TryGetNearestFlowAnchor(
            Vector3d from,
            SwiftDictionary<int, FlowField> fields,
            out Vector3d closestTarget,
            double range = FlowFieldPathRequest.DefaultSearchRange)
        {
            closestTarget = Vector3d.Zero;
            Fixed64 minDistanceSq = new(range * range);
            bool found = false;

            foreach (FlowField flow in fields.Values)
            {
                if (!GlobalGridManager.TryGetGridAndNode(flow.NodeCoordinates, out _, out Node flowNode))
                    continue;

                Fixed64 distSq = Vector3d.SqrDistance(from, flowNode.WorldPosition);
                if (distSq <= minDistanceSq)
                {
                    closestTarget = flowNode.WorldPosition;
                    minDistanceSq = distSq;
                    found = true;
                }
            }

            return found;
        }

        /// <summary>
        /// Gets the raw flow vector at a specific world position, if available.
        /// </summary>
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
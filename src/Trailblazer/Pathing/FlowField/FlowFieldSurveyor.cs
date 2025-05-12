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

        public int FlowFieldSearchPadding { get; internal set; }

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

            FlowFieldSearchPadding = request.FlowFieldSearchPadding;

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

                if (targetReached && current.HeapCost > _startNodeDistance + FlowFieldSearchPadding)
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
                if (PathPartitionHeap.IsClosed(neighbor.Partition) || neighbor.Partition.Unpassable(request.RoverSize))
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
    }
}
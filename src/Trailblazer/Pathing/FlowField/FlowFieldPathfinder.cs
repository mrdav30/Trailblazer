using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System.Threading;
using System;

namespace Trailblazer.Pathing
{
    public class FlowFieldPathfinder
    {
        #region Singleton Instance

        /// <summary>
        /// A lazily initialized singleton instance of the pathfinder.
        /// </summary>
        private static readonly Lazy<FlowFieldPathfinder> _instance =
            new(() => new FlowFieldPathfinder(), LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// Gets the shared instance of the pathfinder.
        /// </summary>
        public static FlowFieldPathfinder Shared => _instance.Value;

        #endregion

        public const int FlowFieldSearchPadding = 10;

        private readonly SwiftHashSet<PathPartition> _markedPartitions = new();

        public void FindPath(FlowFieldPathRequest request)
        {
            if (!request.TargetNode.TryGetPartition(out PathPartition targetPartition))
                return;

            _markedPartitions.Clear();
            PathPartitionHeap.FastClear();

            // Start from the end and move towards the start node
            PathPartitionHeap.Add(targetPartition);

            int searchCount = 0;
            bool targetReached = false;
            int startNodeDistance = 0;
            int greatestDistance = 0;
            while (PathPartitionHeap.Count > 0 && searchCount < request.MaxSearchSize)
            {
                if (!PathPartitionHeap.RemoveFirst(out PathPartition currentPartition))
                    break;

                // Check if we found our way to the start node
                if (currentPartition.NodeSpawnToken == request.FromNode.SpawnToken)
                {
                    startNodeDistance = currentPartition.HeapCost;
                    targetReached = true;
                }

                currentPartition.HasLineOfSight = false;
                // This could be heavy depending on how far away we are!
                if (currentPartition.NodeSpawnToken != request.TargetNode.SpawnToken)
                    currentPartition.HasLineOfSight = !PathingManager.NeedsPath(currentPartition.NodePosition, request.TargetNode.WorldPosition, request.RoverSize);

                if (currentPartition.HeapCost > greatestDistance)
                    greatestDistance = currentPartition.HeapCost;

                AnalyzeNeighborDistance(currentPartition, request.FromNode, request.RoverSize);

                PathPartitionHeap.SetClosed(currentPartition);

                if (targetReached && currentPartition.HeapCost > startNodeDistance + FlowFieldSearchPadding)
                    break;
            }

            SwiftDictionary<int, FlowField> output = null;
            if (_markedPartitions.Count > 0)
                output = GenerateFlowFields(request.TargetNode, greatestDistance);

            request.OnComplete?.Invoke(targetReached, output);
        }

        /// <summary>                        
        /// Wavefront algorithm to create a distance field.
        /// </summary>
        /// <returns>
        /// Returns 
        /// <c>true</c> if path was found and necessary
        /// <c>false</c> if path to End is impossible or not found.
        /// </returns>
        public void AnalyzeNeighborDistance(PathPartition currentPartition, Node startNode, int roverSize)
        {
            // Check each straight line neighbour of this node (no diagonals)
            // We will only ever visit every node once as we are always visiting nodes in the most efficient order
            foreach (TraversableNeighbor neighbor in currentPartition.GetWalkableStraightNeighbors())
            {
                if (PathPartitionHeap.IsClosed(neighbor.Partition))
                    continue;

                // If neighbor is blocked and isn't start node, don't add to heap
                if (neighbor.Partition.Unpassable(roverSize) && neighbor.Node.SpawnToken != startNode.SpawnToken)
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

        public SwiftDictionary<int, FlowField> GenerateFlowFields(Node targetNode, int greatestDistance)
        {
            SwiftDictionary<int, FlowField> output = new();
            foreach (PathPartition currentPartition in _markedPartitions)
            {
                if (currentPartition.NodeSpawnToken == targetNode.SpawnToken)
                {
                    // End node shouldn't point anywhere
                    output.Add(currentPartition.NodeSpawnToken, new FlowField()
                    {
                        Direction = Vector3d.Zero,
                    });
                    continue;
                }

                //Go through all neighbours and find the one with the lowest distance
                PathPartition minDistancePartition = null;
                int minDistance = greatestDistance;
                foreach(TraversableNeighbor neighbor in currentPartition.GetWalkableNeighbors())
                {
                    int nDistance = PathPartitionHeap.IsClosed(neighbor.Partition) ? neighbor.Partition.HeapCost : greatestDistance;
                    int dist = nDistance - currentPartition.HeapCost;
                    if (dist < minDistance)
                    {
                        minDistancePartition = neighbor.Partition;
                        minDistance = dist;
                    }
                }

                Vector3d direction = Vector3d.Zero;  // default to no good direction
                //If we found a valid neighbour, point in its direction
                if (minDistancePartition != null)
                {
                    // If nodes has line of sight to destination, point in that direction instead
                    direction = currentPartition.HasLineOfSight
                        ? (targetNode.WorldPosition - currentPartition.NodePosition)
                        : (minDistancePartition.NodePosition - currentPartition.NodePosition);

                    direction.Normalize();
                }

                output.Add(currentPartition.NodeSpawnToken, new FlowField() 
                { 
                    NodeCoordinates = currentPartition.ParentCoordinate,
                    Direction = direction,
                    HasLineOfSight = currentPartition.HasLineOfSight
                });

                // Reset
                currentPartition.HasLineOfSight = false;
            }

            return output;
        }
    }
}
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Threading;

namespace Trailblazer.Pathing
{
    public class AStarPathFinder
    {
        #region Singleton Instance

        /// <summary>
        /// A lazily initialized singleton instance of the pathfinder.
        /// </summary>
        private static readonly Lazy<AStarPathFinder> _instance =
            new(() => new AStarPathFinder(), LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// Gets the shared instance of the pathfinder.
        /// </summary>
        public static AStarPathFinder Shared => _instance.Value;

        #endregion

        public HeuristicMethod Heuristic { get; internal set; }

        public Fixed64 MaxHeightDifference { get; internal set; }

        public void FindPath(AStarPathRequest request)
        {
            PathPartitionHeap.FastClear();

            if (!request.FromNode.TryGetPartition(out PathPartition startPartition))
            {
                request.OnComplete?.Invoke(false, null);
                return;
            }

            MaxHeightDifference = request.MaxHeightDifference;
            Heuristic = request.Heuristic;

            PathPartitionHeap.Add(startPartition);

            TracePath(request.TargetNode, request.RoverSize, request.MaxSearchSize, out bool targetReached);
            if (!targetReached)
            {
                request.OnComplete?.Invoke(false, null);
                return;
            }

            SwiftList<Node> rawNodePath = GetRawpath(request.FromNode, request.TargetNode);
            SwiftList<Vector3d> smoothVectorPath = SmoothPath(request.TargetNode, request.RoverSize, rawNodePath);

            // Call the OnComplete callback with the resulting path
            request.OnComplete?.Invoke(true, smoothVectorPath);
        }

        public void TracePath(Node targetNode, int roverSize, int searchSize, out bool targetReached)
        {
            int iterations = 0;
            targetReached = false;
            while (PathPartitionHeap.Count > 0 && iterations++ < searchSize)
            {
                PathPartition currentPartition = PathPartitionHeap.RemoveFirst();

                if (currentPartition.NodeSpawnToken == targetNode.SpawnToken)
                    return;

                ProcessNeighbors(currentPartition, targetNode, roverSize, out targetReached);
                if (targetReached)
                    return;

                PathPartitionHeap.SetClosed(currentPartition);
            }
        }

        private void ProcessNeighbors(PathPartition current, Node targetNode, int gridSize, out bool targetReached)
        {
            targetReached = false;
            int cost = current.MovementCost + PathPartition.StraightCost;

            foreach (var neighbor in current.GetWalkableStraightNeighbors())
            {
                targetReached |= ProcessNeighbor(current, neighbor, targetNode, gridSize, cost);
                if (targetReached) return;
            }

            cost = current.MovementCost + PathPartition.DiagonalCost;
            foreach (var neighbor in current.GetWalkableDiagonalNeighbors())
            {
                targetReached |= ProcessNeighbor(current, neighbor, targetNode, gridSize, cost);
                if (targetReached) return;
            }
        }

        private bool ProcessNeighbor(PathPartition current, PathPartition neighbor, Node target, int gridSize, int cost)
        {
            if (neighbor.NodeSpawnToken == target.SpawnToken)
            {
                SetPathPartitionData(neighbor, target.WorldPosition, current.ParentCoordinate, cost);
                return true;
            }

            UpsertPartitionOntoHeap(neighbor, current, target.WorldPosition, gridSize, cost);
            return false;
        }

        private void SetPathPartitionData(
            PathPartition pathPartition,
            Vector3d targetPosition,
            CoordinatesGlobal nextTrailCoordinates,
            int movementCost)
        {
            pathPartition.NextTrailCoordinate = nextTrailCoordinates;
            pathPartition.MovementCost = movementCost;

            int heuristicCost = PathPartition.CalculateHeuristic(
                pathPartition.NodePosition,
                targetPosition,
                Heuristic);

            // Calculate the total cost (fCost) by adding the heuristic cost (hCost) to the movement cost (gCost)
            pathPartition.HeapCost = movementCost + heuristicCost;
        }

        private void UpsertPartitionOntoHeap(PathPartition currrentPartition, PathPartition nextPartition, Vector3d targetPosition, int gridSize, int newCost)
        {
            if (PathPartitionHeap.IsClosed(currrentPartition) || currrentPartition.Unpassable(gridSize))
                return;

            // Skip neighbors that have a height difference greater than the allowed maximum
            Fixed64 heightDifference = (nextPartition.NodePosition.y - currrentPartition.NodePosition.y).Abs();
            if (heightDifference > MaxHeightDifference)
                return;

            if (!PathPartitionHeap.Contains(currrentPartition))
            {
                SetPathPartitionData(currrentPartition, targetPosition, nextPartition.ParentCoordinate, newCost);
                PathPartitionHeap.Add(currrentPartition);
            }
            else if (newCost < currrentPartition.MovementCost)
            {
                SetPathPartitionData(currrentPartition, targetPosition, nextPartition.ParentCoordinate, newCost);
                PathPartitionHeap.SortUp(currrentPartition);
            }
        }

        private SwiftList<Node> GetRawpath(Node startNode, Node targetNode)
        {
            SwiftList<Node> rawNodePath = new();

            Node currentNode = targetNode;
            while (currentNode.SpawnToken != startNode.SpawnToken)
            {
                rawNodePath.Insert(0, currentNode);

                currentNode.TryGetPartition(out PathPartition partition);
                if (!partition.NextTrailCoordinate.HasValue)
                    break; // break in the trail!

                if (GlobalGridManager.TryGetGridAndNode(partition.NextTrailCoordinate.Value, out _, out Node nextTrailNode))
                    currentNode = nextTrailNode;

                // Wipe out for next run
                partition.NextTrailCoordinate = null;
                partition.MovementCost = 0;
            }

            rawNodePath.Insert(0, startNode);
            return rawNodePath;
        }

        private static readonly Fixed64 _directionChangeTolerance = new(0.01);
        public SwiftList<Vector3d> SmoothPath(Node targetNode, int gridSize, SwiftList<Node> rawNodePath)
        {
            SwiftList<Vector3d> outputVectorPath = new();
            if (rawNodePath.Count == 0)
                return outputVectorPath;

            Vector3d lastDir = Vector3d.Zero;
            Vector3d startPos = rawNodePath[0].WorldPosition;
            outputVectorPath.Add(startPos);

            for (int i = 1; i < rawNodePath.Count - 1; i++)
            {
                Node current = rawNodePath[i];
                Node previous = rawNodePath[i - 1];

                if (!current.TryGetPartition(out PathPartition partition))
                    continue;

                // Preserve nodes near unwalkable tiles
                if (partition.GetNeighborClearance() <= gridSize + 1)
                {
                    outputVectorPath.Add(current.WorldPosition);
                    lastDir = Vector3d.Zero;
                    continue;
                }

                Vector3d dir = (current.WorldPosition - previous.WorldPosition).Normal;

                // Only add this node if direction changed
                if (!dir.FuzzyEqual(lastDir, _directionChangeTolerance))
                {
                    outputVectorPath.Add(current.WorldPosition);
                    lastDir = dir;
                }
            }

            outputVectorPath.Add(targetNode.WorldPosition);
            return outputVectorPath;
        }
    }
}
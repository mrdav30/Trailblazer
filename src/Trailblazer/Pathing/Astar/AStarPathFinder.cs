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

        public Fixed64 MaxClimbHeight { get; internal set; }

        public bool UseSplineSmoothing { get; internal set; }

        private static readonly Fixed64 _directionChangeTolerance = new(0.01);

#if DEBUG
#nullable enable
        public static Action<PathPartition, PathPartition, Fixed64>? OnHeightLimitViolated;
#nullable disable
#endif

        public void FindPath(AStarPathRequest request)
        {
            PathPartitionHeap.FastClear();

            if (request.FromNode.SpawnToken == request.TargetNode.SpawnToken)
            {
                request.OnComplete?.Invoke(true, new SwiftList<Vector3d> { request.FromNode.WorldPosition });
                return;
            }

            if (!request.FromNode.TryGetPartition(out PathPartition startPartition))
            {
                request.OnComplete?.Invoke(false, null);
                return;
            }

            MaxClimbHeight = request.MaxClimbHeight;
            Heuristic = request.Heuristic;
            UseSplineSmoothing = request.UseSplineSmoothing;

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
                if (!PathPartitionHeap.RemoveFirst(out PathPartition currentPartition))
                    return;

                if (currentPartition.NodeSpawnToken == targetNode.SpawnToken)
                {
                    targetReached = true;
                    return;
                }

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

            foreach (TraversableNeighbor neighbor in current.GetWalkableStraightNeighbors())
            {
                targetReached |= ProcessNeighbor(current, neighbor.Partition, targetNode, gridSize, cost);
                if (targetReached) return;
            }

            cost = current.MovementCost + PathPartition.DiagonalCost;
            foreach (TraversableNeighbor neighbor in current.GetWalkableDiagonalNeighbors())
            {
                targetReached |= ProcessNeighbor(current, neighbor.Partition, targetNode, gridSize, cost);
                if (targetReached) return;
            }
        }

        private bool ProcessNeighbor(PathPartition current, PathPartition neighbor, Node target, int gridSize, int cost)
        {
            if (PathPartitionHeap.IsClosed(neighbor) || neighbor.Unpassable(gridSize))
                return false;

            // Skip neighbors that have a height difference greater than the allowed maximum
            Fixed64 heightDifference = (current.NodePosition.y - neighbor.NodePosition.y).Abs();
            if (heightDifference > MaxClimbHeight)
            {
#if DEBUG
                OnHeightLimitViolated?.Invoke(current, neighbor, heightDifference);
#endif
                return false;
            }

            if (neighbor.NodeSpawnToken == target.SpawnToken)
            {
                SetPathPartitionData(neighbor, target.WorldPosition, current.ParentCoordinate, cost);
                return true;
            }

            if (!PathPartitionHeap.Contains(neighbor))
            {
                SetPathPartitionData(neighbor, target.WorldPosition, current.ParentCoordinate, cost);
                PathPartitionHeap.Add(neighbor);
            }
            else if (cost < neighbor.MovementCost)
            {
                SetPathPartitionData(neighbor, target.WorldPosition, current.ParentCoordinate, cost);
                PathPartitionHeap.SortUp(neighbor);
            }

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

            // Ensure start position is included
            rawNodePath.Insert(0, startNode);
            return rawNodePath;
        }

        public SwiftList<Vector3d> SmoothPath(Node targetNode, int gridSize, SwiftList<Node> rawNodePath)
        {
            SwiftList<Vector3d> outputVectorPath = new();
            if (rawNodePath.Count == 0)
                return outputVectorPath;

            Vector3d lastDir = Vector3d.Zero;

            // If the path actually goes somewhere → include the start
            if (rawNodePath[0].SpawnToken == rawNodePath.FromEnd(1).SpawnToken)
                outputVectorPath.Add(rawNodePath[0].WorldPosition);

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

            // Ensure target position is included
            outputVectorPath.Add(targetNode.WorldPosition);

            if (UseSplineSmoothing)
                outputVectorPath = CatmullSmooth(outputVectorPath);

            return outputVectorPath;
        }

        public static SwiftList<Vector3d> CatmullSmooth(SwiftList<Vector3d> input, int resolutionPerSegment = 3)
        {
            var output = new SwiftList<Vector3d>();
            if (input.Count < 4) return input;

            for (int i = 0; i < input.Count - 3; i++)
            {
                Vector3d p0 = input[i];
                Vector3d p1 = input[i + 1];
                Vector3d p2 = input[i + 2];
                Vector3d p3 = input[i + 3];

                for (int j = 0; j <= resolutionPerSegment; j++)
                {
                    Fixed64 t = new(j / (double)resolutionPerSegment);
                    output.Add(CatmullRom(p0, p1, p2, p3, t));
                }
            }

            // Add the final point
            int count = input.Count;
            if (count >= 2)
            {
                output.Add(input[count - 2]);
                output.Add(input[count - 1]);
            }

            return output;
        }

        public static Vector3d CatmullRom(Vector3d p0, Vector3d p1, Vector3d p2, Vector3d p3, Fixed64 t)
        {
            // Classic Catmull-Rom basis matrix
            Fixed64 t2 = t * t;
            Fixed64 t3 = t2 * t;

            return
                ((-t3 + 2 * t2 - t) * p0 +
                 (3 * t3 - 5 * t2 + 2) * p1 +
                 (-3 * t3 + 4 * t2 + t) * p2 +
                 (t3 - t2) * p3) / 2;
        }
    }
}
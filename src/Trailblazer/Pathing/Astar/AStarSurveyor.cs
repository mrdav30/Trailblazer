using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Threading;

namespace Trailblazer.Pathing
{
    public class AStarSurveyor
    {
        #region Singleton Instance

        /// <summary>
        /// A lazily initialized singleton instance of the pathfinder.
        /// </summary>
        private static readonly Lazy<AStarSurveyor> _instance =
            new(() => new AStarSurveyor(), LazyThreadSafetyMode.ExecutionAndPublication);

        /// <summary>
        /// Gets the shared instance of the pathfinder.
        /// </summary>
        public static AStarSurveyor Shared => _instance.Value;

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
            if (!request.FromNode.TryGetPartition(out PathPartition startPartition)
                || request.FromNode.SpawnToken == request.TargetNode.SpawnToken)
            {
                request.OnComplete?.Invoke(false, null);
                return;
            }

            PathPartitionHeap.FastClear();

            MaxClimbHeight = request.MaxClimbHeight;
            Heuristic = request.Heuristic;
            UseSplineSmoothing = request.UseSplineSmoothing;

            PathPartitionHeap.Add(startPartition);

            if (!TracePath(request))
            {
                request.OnComplete?.Invoke(false, null);
                return;
            }

            SwiftList<Node> rawNodePath = GetRawpath(request);
            SwiftList<Vector3d> smoothVectorPath = SmoothPath(rawNodePath, request);

            // Call the OnComplete callback with the resulting path
            request.OnComplete?.Invoke(true, smoothVectorPath);
        }

        public bool TracePath(AStarPathRequest request)
        {
            int iterations = 0;
            while (PathPartitionHeap.RemoveFirst(out PathPartition currentPartition) && iterations++ < request.MaxSearchSize)
            {
                if (currentPartition.NodeSpawnToken == request.TargetNode.SpawnToken)
                    return true;

                if (ProcessNeighbors(currentPartition, request))
                    return true;

                PathPartitionHeap.SetClosed(currentPartition);
            }

            return false;
        }

        private bool ProcessNeighbors(PathPartition current, AStarPathRequest request)
        {
            int cost = current.MovementCost + PathPartition.StraightCost;
            foreach (TraversableNeighbor neighbor in current.GetWalkableStraightNeighbors())
            {
                if (ProcessNeighbor(current, neighbor.Partition, cost, request)) 
                    return true;
            }

            cost = current.MovementCost + PathPartition.DiagonalCost;
            foreach (TraversableNeighbor neighbor in current.GetWalkableDiagonalNeighbors())
            {
                if (ProcessNeighbor(current, neighbor.Partition, cost, request)) 
                    return true;
            }

            return false;
        }

        private bool ProcessNeighbor(PathPartition current, PathPartition neighbor, int cost, AStarPathRequest request)
        {
            if (PathPartitionHeap.IsClosed(neighbor) || neighbor.Unpassable(request.RoverSize))
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

            if (neighbor.NodeSpawnToken == request.TargetNode.SpawnToken)
            {
                SetPathPartitionData(neighbor, request.TargetNode.WorldPosition, current.ParentCoordinate, cost);
                return true;
            }

            if (!PathPartitionHeap.Contains(neighbor))
            {
                SetPathPartitionData(neighbor, request.TargetNode.WorldPosition, current.ParentCoordinate, cost);
                PathPartitionHeap.Add(neighbor);
            }
            else if (cost < neighbor.MovementCost)
            {
                SetPathPartitionData(neighbor, request.TargetNode.WorldPosition, current.ParentCoordinate, cost);
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

        private SwiftList<Node> GetRawpath(AStarPathRequest request)
        {
            SwiftList<Node> rawNodePath = new();

            Node currentNode = request.TargetNode;
            while (currentNode.SpawnToken != request.FromNode.SpawnToken)
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
            rawNodePath.Insert(0, request.FromNode);
            return rawNodePath;
        }

        public SwiftList<Vector3d> SmoothPath(SwiftList<Node> rawNodePath, AStarPathRequest request)
        {
            SwiftList<Vector3d> outputVectorPath = new();
            if (rawNodePath.Count == 0)
                return outputVectorPath;

            Vector3d lastDir = Vector3d.Zero;

            // If the path actually goes somewhere → include the start
            if (rawNodePath[0].SpawnToken != rawNodePath.FromEnd(1).SpawnToken)
                outputVectorPath.Add(rawNodePath[0].WorldPosition);

            for (int i = 1; i < rawNodePath.Count - 1; i++)
            {
                Node current = rawNodePath[i];
                Node previous = rawNodePath[i - 1];

                if (!current.TryGetPartition(out PathPartition partition))
                    continue;

                // Preserve nodes near unwalkable tiles
                if (partition.GetNeighborClearance() <= request.RoverSize + 1)
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
            outputVectorPath.Add(request.TargetNode.WorldPosition);

            if (UseSplineSmoothing)
                outputVectorPath = CatmullSmooth(outputVectorPath);

            return outputVectorPath;
        }

        public static SwiftList<Vector3d> CatmullSmooth(SwiftList<Vector3d> input, int resolutionPerSegment = 3)
        {
            if (input.Count < 4) return input;

            // Add the starting point
            SwiftList<Vector3d> output = new() {
                input[0]
            };

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
            output.Add(input.FromEnd(1));

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
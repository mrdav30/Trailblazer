using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Threading;

namespace Trailblazer.Pathing
{
    /// <summary>
    /// Executes A* pathfinding logic using partitioned grids to find viable navigation paths for agents.
    /// Supports climb height constraints and optional spline smoothing of the final path.
    /// </summary>
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

        /// <summary>
        /// The directional tolerance used to detect changes in path heading when smoothing a path.
        /// </summary>
        private static readonly Fixed64 _directionChangeTolerance = new(0.01);

#nullable enable
        /// <summary>
        /// Optional callback triggered when a height difference exceeds the allowed climb height during pathfinding.
        /// </summary>
        public static Action<PathPartition, PathPartition, Fixed64>? OnHeightLimitViolated;
#nullable disable

        /// <summary>
        /// Attempts to find a path between the start and end points provided in the request. 
        /// Returns true if a valid path was found and outputs the resulting waypoint list.
        /// </summary>
        /// <param name="request">The pathfinding request containing start/end info and constraints.</param>
        /// <param name="result">The list of path waypoints if successful; otherwise null.</param>
        /// <returns>True if a path is found; false otherwise.</returns>
        public bool FindPath(AStarPathRequest request, out SwiftList<Vector3d> result)
        {
            result = null;
            if (!request.Start.TryGetPartition(out PathPartition startPartition)
                || request.Start.SpawnToken == request.End.SpawnToken)
            {
                return false;
            }

            PathPartitionHeap.FastClear();

            PathPartitionHeap.Add(startPartition);

            if (!TracePath(request))
                return false;

            SwiftList<Node> rawNodePath = GetRawpath(request.Start, request.End);
            result = SmoothPath(rawNodePath, request.End, request.UnitSize, request.UseSplineSmoothing);
            return true;
        }

        /// <summary>
        /// Executes the core A* loop to find a valid trail between the start and end nodes.
        /// </summary>
        /// <param name="request">The pathfinding request with all search parameters.</param>
        /// <returns>True if the path to the target was found; false otherwise.</returns>
        public bool TracePath(AStarPathRequest request)
        {
            int iterations = 0;
            while (PathPartitionHeap.RemoveFirst(out PathPartition currentPartition) && iterations++ < request.MaxPathSearchRange)
            {
                if (currentPartition.NodeSpawnToken == request.End.SpawnToken)
                    return true;

                if (ProcessNeighbors(request, currentPartition))
                    return true;

                PathPartitionHeap.SetClosed(currentPartition);
            }

            return false;
        }

        /// <summary>
        /// Indicates whether straight and diagonal neighbor nodes should be processed during pathfinding.
        /// </summary>
        private bool ProcessNeighbors(AStarPathRequest request, PathPartition current)
        {
            int cost = current.MovementCost + PathPartition.StraightCost;
            foreach (TraversableNeighbor neighbor in current.GetWalkableStraightNeighbors())
            {
                if (ProcessNeighbor(request, current, neighbor.Partition, cost)) 
                    return true;
            }

            cost = current.MovementCost + PathPartition.DiagonalCost;
            foreach (TraversableNeighbor neighbor in current.GetWalkableDiagonalNeighbors())
            {
                if (ProcessNeighbor(request, current, neighbor.Partition, cost)) 
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether a given neighbor node should be considered for path expansion.
        /// </summary>
        private bool ProcessNeighbor(
            AStarPathRequest request,
            PathPartition current, 
            PathPartition neighbor, 
            int cost)
        {
            if (PathPartitionHeap.IsClosed(neighbor) || neighbor.Unpassable(request.UnitSize))
                return false;

            // Skip neighbors that have a height difference greater than the allowed maximum
            Fixed64 heightDifference = (current.NodePosition.y - neighbor.NodePosition.y).Abs();
            if (heightDifference > request.MaxClimbHeight)
            {
                OnHeightLimitViolated?.Invoke(current, neighbor, heightDifference);
                return false;
            }

            if (neighbor.NodeSpawnToken == request.End.SpawnToken)
            {
                SetPathPartitionData(neighbor, request.End.WorldPosition, current.ParentCoordinate, cost, request.Heuristic);
                return true;
            }

            if (!PathPartitionHeap.Contains(neighbor))
            {
                SetPathPartitionData(neighbor, request.End.WorldPosition, current.ParentCoordinate, cost, request.Heuristic);
                PathPartitionHeap.Add(neighbor);
            }
            else if (cost < neighbor.MovementCost)
            {
                SetPathPartitionData(neighbor, request.End.WorldPosition, current.ParentCoordinate, cost, request.Heuristic);
                PathPartitionHeap.SortUp(neighbor);
            }

            return false;
        }

        /// <summary>
        /// Assigns pathfinding data to a path partition, including cost and direction toward the next trail node.
        /// </summary>
        /// <param name="pathPartition">The path partition being updated.</param>
        /// <param name="targetPosition">The destination's world position for heuristic estimation.</param>
        /// <param name="nextTrailCoordinates">The coordinates of the parent partition leading to this one.</param>
        /// <param name="movementCost">The cumulative movement cost to this partition.</param>
        /// <param name="heuristic">The heuristic method used to estimate cost-to-goal.</param>
        private void SetPathPartitionData(
            PathPartition pathPartition,
            Vector3d targetPosition,
            CoordinatesGlobal nextTrailCoordinates,
            int movementCost,
            HeuristicMethod heuristic)
        {
            pathPartition.NextTrailCoordinate = nextTrailCoordinates;
            pathPartition.MovementCost = movementCost;

            int heuristicCost = PathPartition.CalculateHeuristic(
                pathPartition.NodePosition,
                targetPosition,
                heuristic);

            // Calculate the total cost (fCost) by adding the heuristic cost (hCost) to the movement cost (gCost)
            pathPartition.HeapCost = movementCost + heuristicCost;
        }

        /// <summary>
        /// Reconstructs the raw node-based path from the destination to the origin by walking backwards through trail links.
        /// </summary>
        /// <param name="start">The origin node.</param>
        /// <param name="end">The destination node.</param>
        /// <returns>A list of nodes from start to end representing the raw path.</returns>
        private SwiftList<Node> GetRawpath(Node start, Node end)
        {
            SwiftList<Node> rawNodePath = new();

            Node currentNode = end;
            while (currentNode.SpawnToken != start.SpawnToken)
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
            rawNodePath.Insert(0, start);
            return rawNodePath;
        }

        /// <summary>
        /// Constructs a smoothed version of the path using direction changes and optional spline smoothing.
        /// </summary>
        /// <param name="rawNodePath">The unsmoothed list of nodes produced by pathfinding.</param>
        /// <param name="end">The target node.</param>
        /// <param name="unitSize">The agent’s unit size used to maintain spacing from obstacles.</param>
        /// <param name="useSplineSmoothing">True to apply Catmull-Rom spline smoothing to the path.</param>
        /// <returns>A smoothed list of world positions.</returns>
        public SwiftList<Vector3d> SmoothPath(
            SwiftList<Node> rawNodePath, 
            Node end,
            Fixed64 unitSize,
            bool useSplineSmoothing)
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
                if (partition.GetNeighborClearance() <= unitSize + 1)
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
            outputVectorPath.Add(end.WorldPosition);

            if (useSplineSmoothing)
                outputVectorPath = CatmullSmooth(outputVectorPath);

            return outputVectorPath;
        }

        /// <summary>
        /// Applies Catmull-Rom spline smoothing to a set of input path points to produce a smoother curve.
        /// </summary>
        /// <param name="input">The input path of waypoints.</param>
        /// <param name="resolutionPerSegment">The number of interpolated points per segment.</param>
        /// <returns>A smoothed path using Catmull-Rom spline interpolation.</returns>
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

        /// <summary>
        /// Computes the interpolated point along a Catmull-Rom spline given four control points.
        /// </summary>
        /// <param name="p0">The first control point.</param>
        /// <param name="p1">The second control point.</param>
        /// <param name="p2">The third control point.</param>
        /// <param name="p3">The fourth control point.</param>
        /// <param name="t">Interpolation factor between 0 and 1.</param>
        /// <returns>The interpolated point on the spline.</returns>
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
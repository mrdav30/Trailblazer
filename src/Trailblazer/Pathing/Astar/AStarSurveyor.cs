using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Threading;

namespace Trailblazer.Pathing
{
    internal struct AStarVoxelData
    {
        /// <summary>
        /// The movement penalty cost of this voxel.
        /// </summary>
        public int MovementCost;

        /// <summary>
        /// The next voxel in the trail path.
        /// </summary>
        public GlobalVoxelIndex? NextTrailIndex;
    }

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

        private AStarPathRequest _request;

        private readonly SwiftDictionary<int, AStarVoxelData> _voxelData = new();

#nullable enable
        /// <summary>
        /// Optional callback triggered when a height difference exceeds the allowed climb height during pathfinding.
        /// </summary>
        public static Action<GlobalVoxelIndex, GlobalVoxelIndex, Fixed64>? OnHeightLimitViolated;
#nullable disable

        /// <summary>
        /// Attempts to find a path between the start and end points provided in the request. 
        /// Returns true if a valid path was found and outputs the resulting waypoint list.
        /// </summary>
        /// <param name="request">The pathfinding request containing start/end info and constraints.</param>
        /// <returns>The list of path waypoints if successful; otherwise null.</returns>
        public AStarSurveyResult FindPath(AStarPathRequest request)
        {
            if (!request.IsValid
                || request.HasZeroDisplacement 
                || !request.Start.TryGetPartition(out PathPartition startPartition))
            {
                return AStarSurveyResult.Empty;
            }

            _request = request;

            _voxelData.Clear();
            PathHeap.FastClear();

            // Trace path from the start to the end
            _voxelData.Add(_request.Start.SpawnToken, new());
            PathHeap.Add(startPartition);

            if (!TracePath())
                return AStarSurveyResult.Empty;

            SwiftList<Voxel> rawVoxelPath = GetRawpath();
            return AStarSurveyResult.Create(SmoothPath(rawVoxelPath), request.RequestCacheKey);
        }

        /// <summary>
        /// Executes the core A* loop to find a valid trail between the start and end voxels.
        /// </summary>
        /// <returns>True if the path to the target was found; false otherwise.</returns>
        private bool TracePath()
        {
            int iterations = 0;
            int searchSize = _request.MaxPathSearchRange.Value;
            while (PathHeap.RemoveFirst(out PathPartition currentPartition) 
                && iterations++ < searchSize)
            {
                if (currentPartition.VoxelSpawnToken == _request.End.SpawnToken)
                    return true;

                if (ProcessNeighbors(currentPartition))
                    return true;

                PathHeap.SetClosed(currentPartition);
            }

            return false;
        }

        /// <summary>
        /// Indicates whether straight and diagonal neighbor voxels should be processed during pathfinding.
        /// </summary>
        /// <returns>True if any neighbor is the target destination.</returns>
        private bool ProcessNeighbors(PathPartition current)
        {
            if (!_voxelData.TryGetValue(current.VoxelSpawnToken, out AStarVoxelData data))
                return false;

            int cost = data.MovementCost + PathPartition.StraightCost;
            foreach (TraversableVoxel neighbor in PathManager.GetWalkableStraightNeighbors(current.GlobalIndex))
            {
                if (ProcessNeighbor(current, neighbor.Partition, cost))
                    return true;
            }

            cost = data.MovementCost + PathPartition.DiagonalCost;
            foreach (TraversableVoxel neighbor in PathManager.GetWalkableDiagonalNeighbors(current.GlobalIndex))
            {
                if (ProcessNeighbor(current, neighbor.Partition, cost))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Determines whether a given neighbor voxel should be considered for path expansion.
        /// </summary>
        /// <returns>True if the neighbor is the target destination.</returns>
        private bool ProcessNeighbor(
            PathPartition current,
            PathPartition neighbor,
            int cost)
        {
            if (PathHeap.IsClosed(neighbor) || neighbor.Unpassable(_request.UnitSize))
                return false;

            // Skip neighbors that have a height difference greater than the allowed maximum
            Fixed64 heightDifference = (current.VoxelPosition.y - neighbor.VoxelPosition.y).Abs();
            if (heightDifference > _request.MaxClimbHeight)
            {
                OnHeightLimitViolated?.Invoke(current.GlobalIndex, neighbor.GlobalIndex, heightDifference);
                return false;
            }

            if (neighbor.VoxelSpawnToken == _request.End.SpawnToken)
            {
                SetPathPartitionData(neighbor, current.GlobalIndex, cost);
                return true;
            }

            if (!PathHeap.Contains(neighbor))
            {
                SetPathPartitionData(neighbor, current.GlobalIndex, cost);
                PathHeap.Add(neighbor);
            }
            else if (_voxelData.TryGetValue(neighbor.VoxelSpawnToken, out AStarVoxelData data)
                && data.MovementCost > cost)
            {
                SetPathPartitionData(neighbor, current.GlobalIndex, cost);
                PathHeap.SortUp(neighbor);
            }

            return false;
        }

        /// <summary>
        /// Assigns pathfinding data to a path partition, including cost and direction toward the next trail voxel.
        /// </summary>
        /// <param name="partition">The path partition being updated.</param>
        /// <param name="nextTrailCoordinates">The coordinates of the parent partition leading to this one.</param>
        /// <param name="movementCost">The cumulative movement cost to this partition.</param>
        private void SetPathPartitionData(
            PathPartition partition,
            GlobalVoxelIndex nextTrailCoordinates,
            int movementCost)
        {
            _voxelData.Add(partition.VoxelSpawnToken, new AStarVoxelData
            {
                MovementCost = movementCost,
                NextTrailIndex = nextTrailCoordinates
            });

            int heuristicCost = PathPartition.CalculateHeuristic(
                partition.VoxelPosition,
                _request.End.WorldPosition,
                _request.Heuristic);

            // Calculate the total cost (fCost) by adding the heuristic cost (hCost) to the movement cost (gCost)
            partition.HeapCost = movementCost + heuristicCost;
        }

        /// <summary>
        /// Reconstructs the raw voxel-based path from the destination to the origin by walking backwards through trail links.
        /// </summary>
        /// <returns>A list of voxels from start to end representing the raw path.</returns>
        private SwiftList<Voxel> GetRawpath()
        {
            SwiftList<Voxel> rawVoxelPath = new();

            Voxel current = _request.End;
            while (current.SpawnToken != _request.Start.SpawnToken)
            {
                rawVoxelPath.Insert(0, current);

                if (!current.TryGetPartition(out PathPartition partition))
                    continue;

                if (!_voxelData.TryGetValue(current.SpawnToken, out AStarVoxelData data) || !data.NextTrailIndex.HasValue)
                    break; // break in the trail!

                if (!GlobalGridManager.TryGetGridAndVoxel(data.NextTrailIndex.Value, out _, out Voxel nextTrailVoxel))
                    break; // break in the trail!

                current = nextTrailVoxel;
                partition.ClearHeapState();
            }

            // Ensure start position is included
            rawVoxelPath.Insert(0, _request.Start);
            return rawVoxelPath;
        }

        /// <summary>
        /// Constructs a smoothed version of the path using direction changes and optional spline smoothing.
        /// </summary>
        /// <param name="rawVoxelPath">The unsmoothed list of voxels produced by pathfinding.</param>
        /// <returns>A smoothed list of world positions.</returns>
        private SwiftList<Vector3d> SmoothPath(SwiftList<Voxel> rawVoxelPath)
        {
            SwiftList<Vector3d> outputVectorPath = new();
            if (rawVoxelPath.Count == 0)
                return outputVectorPath;

            Vector3d lastDir = Vector3d.Zero;

            // If the path actually goes somewhere → include the start
            if (rawVoxelPath[0].SpawnToken != rawVoxelPath.FromEnd(1).SpawnToken)
                outputVectorPath.Add(rawVoxelPath[0].WorldPosition);

            for (int i = 1; i < rawVoxelPath.Count - 1; i++)
            {
                Voxel current = rawVoxelPath[i];
                Voxel previous = rawVoxelPath[i - 1];

                if (!current.TryGetPartition(out PathPartition partition))
                    continue;

                // Preserve voxels near unwalkable tiles
                if (partition.GetNeighborClearance() <= _request.UnitSize + 1)
                {
                    outputVectorPath.Add(current.WorldPosition);
                    lastDir = Vector3d.Zero;
                    continue;
                }

                Vector3d dir = (current.WorldPosition - previous.WorldPosition).Normal;

                // Only add this voxel if direction changed
                if (!dir.FuzzyEqual(lastDir, _directionChangeTolerance))
                {
                    outputVectorPath.Add(current.WorldPosition);
                    lastDir = dir;
                }
            }

            // Ensure target position is included
            outputVectorPath.Add(_request.End.WorldPosition);

            if (_request.UseSplineSmoothing)
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
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Trailblazer.Pathing
{
    internal struct AStarVoxelMeta
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
        #region Singleton Instances

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

        private readonly PathHeap _heap = new();

        private readonly SwiftDictionary<int, AStarVoxelMeta> _meta = new();

        private readonly SwiftList<PathPartition> _rawPath = new();

        private readonly SwiftList<AStarWaypoint> _waypoints = new();

        private readonly SwiftHashSet<string> _chartKeys = new();

        private AStarPathRequest _request;

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
            lock (SurveyorLock.GlobalLock)
            {
                if (!request.IsValid
                    || request.HasZeroDisplacement
                    || !request.Start.TryGetPartition(out PathPartition startPartition))
                {
                    return AStarSurveyResult.Empty;
                }

                _request = request;

                _heap.FastClear();
                _meta.Clear();
                _rawPath.FastClear();
                _waypoints.FastClear();
                _chartKeys.Clear();

                // Trace path from the start to the end
                _meta.Add(_request.Start.SpawnToken, new());
                startPartition.PathCost = 0;
                _heap.Add(startPartition);

                if (!TracePath())
                    return AStarSurveyResult.Empty;

                BuildRawPath();
                BuildWaypoints();

                AStarWaypoint[] waypoints = _waypoints.ToArray();
                string[] chartKeys = _chartKeys.ToArray();
                return AStarSurveyResult.Create(waypoints, chartKeys, request.RequestCacheKey);

                // TODO: should we also clear collections at the end for GC?  maybe flag to determine if dirty?
            }
        }

        /// <summary>
        /// Executes the core A* loop to find a valid trail between the start and end voxels.
        /// </summary>
        /// <returns>True if the path to the target was found; false otherwise.</returns>
        private bool TracePath()
        {
            int iterations = 0;
            int searchSize = _request.MaxPathSearchRange.Value;
            while (_heap.RemoveFirst(out PathPartition currentPartition)
                && iterations++ < searchSize)
            {
                if (currentPartition.VoxelToken == _request.End.SpawnToken)
                    return true;

                if (ProcessNeighbors(currentPartition))
                    return true;

                _heap.SetClosed(currentPartition);
            }

            return false;
        }

        /// <summary>
        /// Indicates whether straight and diagonal neighbor voxels should be processed during pathfinding.
        /// </summary>
        /// <returns>True if any neighbor is the target destination.</returns>
        private bool ProcessNeighbors(PathPartition current)
        {
            if (!_meta.TryGetValue(current.VoxelToken, out AStarVoxelMeta data))
                return false;

            // Loop through all perpendicular neighbors first
            int cost = data.MovementCost + PathPartition.StraightCost;
            foreach (LinearDirection dir in PathManager.PerpendicularDirections)
            {
                // pull the neighbor partition directly out of our baked neighbors[]
                PathPartition nPart = current.Neighbors[(int)dir];
                if (nPart is null || _heap.IsClosed(nPart) || nPart.IsImpassable(_request.UnitSize))
                    continue;  // either out-of-bounds or blocked

                if (ProcessNeighbor(current, nPart, cost))
                    return true;
            }

            cost = data.MovementCost + PathPartition.DiagonalCost;
            foreach (LinearDirection dir in PathManager.DiagonalDirections)
            {
                // 1) pull the neighbor partition directly
                PathPartition nPart = current.Neighbors[(int)dir];
                if (nPart is null || _heap.IsClosed(nPart) || nPart.IsImpassable(_request.UnitSize))
                    continue;  // either out-of-bounds or blocked

                // 2) get the raw offset for this dir
                (int dx, int dy, int dz) = GlobalGridManager.DirectionOffsets[(int)dir];

                // 3) ensure each single‐axis “leg” is passable
                bool blocked = false;

                // X‐leg
                if (dx != 0)
                {
                    LinearDirection legDir = dx > 0
                               ? LinearDirection.North
                               : LinearDirection.West;
                    PathPartition legPart = current.Neighbors[(int)legDir];
                    if (legPart == null || legPart.IsImpassable(_request.UnitSize))
                        blocked = true;
                }

                // Y‐leg
                if (!blocked && dy != 0)
                {
                    LinearDirection legDir = dy > 0
                               ? LinearDirection.Above
                               : LinearDirection.Below;
                    PathPartition legPart = current.Neighbors[(int)legDir];
                    if (legPart == null || legPart.IsImpassable(_request.UnitSize))
                        blocked = true;
                }

                // Z‐leg
                if (!blocked && dz != 0)
                {
                    LinearDirection legDir = dz > 0
                               ? LinearDirection.East
                               : LinearDirection.South;
                    PathPartition legPart = current.Neighbors[(int)legDir];
                    if (legPart == null || legPart.IsImpassable(_request.UnitSize))
                        blocked = true;
                }

                if (blocked)
                    continue;

                // 4) finally, process the neighbor
                if (ProcessNeighbor(current, nPart, cost))
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
            // Skip neighbors that have a height difference greater than the allowed maximum
            Fixed64 heightDifference = (current.VoxelPosition.y - neighbor.VoxelPosition.y).Abs();
            if (heightDifference > _request.MaxClimbHeight)
            {
                OnHeightLimitViolated?.Invoke(current.GlobalIndex, neighbor.GlobalIndex, heightDifference);
                return false;
            }

            if (neighbor.VoxelToken == _request.End.SpawnToken)
            {
                SetPathPartitionData(neighbor, current.GlobalIndex, cost);
                return true;
            }

            if (!_heap.Contains(neighbor))
            {
                SetPathPartitionData(neighbor, current.GlobalIndex, cost);
                _heap.Add(neighbor);
            }
            else if (_meta.TryGetValue(neighbor.VoxelToken, out AStarVoxelMeta neighborData)
                && neighborData.MovementCost > cost)
            {
                SetPathPartitionData(neighbor, current.GlobalIndex, cost);
                _heap.SortUp(neighbor);
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
            _meta.Add(partition.VoxelToken, new AStarVoxelMeta
            {
                MovementCost = movementCost,
                NextTrailIndex = nextTrailCoordinates
            });

            int heuristicCost = PathPartition.CalculateHeuristic(
                partition.VoxelPosition,
                _request.End.WorldPosition,
                _request.Heuristic);

            // Calculate the total cost (fCost) by adding modifier to the heuristic cost (hCost)
            // and to the movement cost (gCost)
            partition.PathCost = partition.PathCostModifier + movementCost + heuristicCost;
        }

        /// <summary>
        /// Reconstructs the raw voxel-based path from the destination to the origin by walking backwards through trail links.
        /// </summary>
        /// <returns>A list of voxels from start to end representing the raw path.</returns>
        private void BuildRawPath()
        {
            Voxel current = _request.End;
            while (current.SpawnToken != _request.Start.SpawnToken)
            {
                PathPartition currentPartition = current.GetPartitionOrDefault<PathPartition>();
                _rawPath.Insert(0, currentPartition);

                if (!_meta.TryGetValue(current.SpawnToken, out AStarVoxelMeta data) || !data.NextTrailIndex.HasValue)
                    break; // break in the trail!

                if (!GlobalGridManager.TryGetGridAndVoxel(data.NextTrailIndex.Value, out _, out Voxel nextTrailVoxel))
                    break; // break in the trail!

                current = nextTrailVoxel;
            }

            // Ensure start position is included
            PathPartition startPartition = _request.Start.GetPartitionOrDefault<PathPartition>();
            _rawPath.Insert(0, startPartition);
        }

        /// <summary>
        /// Constructs a smoothed version of the path using direction changes and optional spline smoothing.
        /// </summary>
        /// <returns>A smoothed list of world positions.</returns>
        private void BuildWaypoints()
        {
            // return early if the start is the same as the end
            if (_rawPath.Count == 0 || _rawPath[0].VoxelToken == _rawPath.FromEnd(1).VoxelToken)
                return;

            _waypoints.EnsureCapacity(_rawPath.Count);
            PathPartition start = _rawPath[0];
            _waypoints.Add(new()
            {
                Position = start.VoxelPosition,
                PathCost = start.PathCost,
                GlobalIndex = start.GlobalIndex
            });
            start.PathCost = int.MaxValue;
            _chartKeys.AddRange(start.ChartOwners);

            Vector3d lastDirection = Vector3d.Zero;

            for (int i = 1; i < _rawPath.Count - 1; i++)
            {
                Vector3d direction = (_rawPath[i + 1].VoxelPosition - _rawPath[i].VoxelPosition).Normalize();

                bool preserveUnwalkable = _rawPath[i].GetNeighborClearance() <= (byte)_request.UnitSize + 1;
                bool directionChanged = !lastDirection.FuzzyEqual(direction);

                if (preserveUnwalkable || directionChanged)
                {
                    _waypoints.Add(new()
                    {
                        Position = _rawPath[i].VoxelPosition,
                        PathCost = _rawPath[i].PathCost,
                        GlobalIndex = _rawPath[i].GlobalIndex
                    });
                }

                lastDirection = direction;
                _rawPath[i].PathCost = int.MaxValue;
                _chartKeys.AddRange(_rawPath[i].ChartOwners);
            }

            PathPartition end = _rawPath.FromEnd(1);
            _waypoints.Add(new()
            {
                Position = end.VoxelPosition,
                PathCost = end.PathCost,
                GlobalIndex = end.GlobalIndex,
                IsGoal = true
            });
            end.PathCost = int.MaxValue;
            _chartKeys.AddRange(end.ChartOwners);
        }

        /// <summary>
        /// Applies Catmull-Rom spline smoothing to a set of input path points to produce a smoother curve.
        /// </summary>
        /// <param name="input">The input path of waypoints.</param>
        /// <param name="resolutionPerSegment">The number of interpolated points per segment.</param>
        /// <returns>A smoothed path using Catmull-Rom spline interpolation.</returns>
        public static AStarWaypoint[] CatmullSmooth(AStarWaypoint[] input, int resolutionPerSegment = 3)
        {
            if (input.Length < 4) return input;

            // size = smoothing points + 2 for start/end points
            AStarWaypoint[] output = new AStarWaypoint[((input.Length - 3) * resolutionPerSegment) + 2];

            // Add the starting point
            output[0] = input[0];

            int outputIndex = 1; // Start at 1 because output[0] = input[0] 
            for (int i = 0; i < input.Length - 3; i++)
            {
                Vector3d p0 = input[i].Position;
                Vector3d p1 = input[i + 1].Position;
                Vector3d p2 = input[i + 2].Position;
                Vector3d p3 = input[i + 3].Position;

                // j starts at 1 to skip duplicate of first point
                for (int j = 1; j <= resolutionPerSegment; j++)
                {
                    Fixed64 t = new(j / (double)resolutionPerSegment);

                    // You should create a new waypoint here:
                    output[outputIndex] = new AStarWaypoint
                    {
                        Position = CatmullRom(p0, p1, p2, p3, t),
                        GlobalIndex = input[i + 1].GlobalIndex,
                        PathCost = input[i + 1].PathCost,
                        IsGoal = false
                    };

                    outputIndex++;
                }
            }

            // Add the final point
            output[outputIndex] = input[input.Length - 1];
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
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static Vector3d CatmullRom(Vector3d p0, Vector3d p1, Vector3d p2, Vector3d p3, Fixed64 t)
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
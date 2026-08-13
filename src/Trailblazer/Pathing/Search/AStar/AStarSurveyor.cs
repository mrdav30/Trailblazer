//=======================================================================
// AStarSurveyor.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;

namespace Trailblazer.Pathing;

internal struct AStarVoxelMeta
{
    /// <summary>
    /// The movement penalty cost of this voxel.
    /// </summary>
    public int MovementCost;

    /// <summary>
    /// The total survey path cost recorded for this voxel.
    /// </summary>
    public int PathCost;

    /// <summary>
    /// The next voxel in the trail path.
    /// </summary>
    public WorldVoxelIndex? NextTrailIndex;
}

/// <summary>
/// Executes A* pathfinding logic using partitioned grids to find viable navigation paths for agents.
/// Supports climb height constraints and optional spline smoothing of the final path.
/// </summary>
public class AStarSurveyor
{
    #region Constants

    /// <summary>
    /// Cost applied for straight (orthogonal) pathfinding moves.
    /// </summary>
    public const int StraightCost = 100;

    /// <summary>
    /// Cost applied for diagonal pathfinding moves.
    /// </summary>
    public const int DiagonalCost = 141;

    #endregion

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

    private readonly SurveyorLock _scratchLock = new();

    private readonly PathHeap<SolidChartPartition> _heap = new();

    // Key by stable voxel index so reconstruction is independent of GridForge voxel wrapper identity.
    private readonly SwiftDictionary<WorldVoxelIndex, AStarVoxelMeta> _meta = new();

    private readonly SwiftList<SolidChartPartition> _rawPath = new();

    private readonly SwiftList<AStarWaypoint> _waypoints = new();

    private readonly SwiftHashSet<string> _chartKeys = new();

    private AStarPathRequest? _request;

    /// <summary>
    /// Attempts to find a path between the start and end points provided in the request.
    /// Returns true if a valid path was found and outputs the resulting waypoint list.
    /// </summary>
    /// <param name="request">The pathfinding request containing start/end info and constraints.</param>
    /// <returns>The list of path waypoints if successful; otherwise null.</returns>
    public AStarSurveyResult FindPath(AStarPathRequest request)
    {
        lock (_scratchLock)
        {
            if (request == null
                || request.HasZeroDisplacement
                || request.StartNode!.TryGetPartition(out SolidChartPartition? startPartition) != true)
            {
                return AStarSurveyResult.Empty;
            }

            _request = request;

            ClearWorkingState();
            // Trace path from the start to the end
            _meta[_request.StartNode.WorldIndex] = new AStarVoxelMeta
            {
                PathCost = 0
            };
            _heap.Add(startPartition!, pathCost: 0);

            if (!TracePath())
            {
                ClearWorkingState();
                return AStarSurveyResult.Empty;
            }

            BuildRawPath();
            BuildWaypoints();

            AStarWaypoint[] waypoints = _waypoints.ToArray();
            string[] chartKeys = _chartKeys.ToArray();
            AStarSurveyResult result = AStarSurveyResult.Create(request.Context, waypoints, chartKeys, request.RequestCacheKey);
            ClearWorkingState();
            return result;
        }
    }

    /// <summary>
    /// Executes the core A* loop to find a valid trail between the start and end voxels.
    /// </summary>
    /// <returns>True if the path to the target was found; false otherwise.</returns>
    private bool TracePath()
    {
        int iterations = 0;
        int searchSize = _request!.MaxPathSearchRange;
        while (_heap.RemoveFirst(out SolidChartPartition? currentPartition)
            && currentPartition != null
            && iterations++ < searchSize)
        {
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
    private bool ProcessNeighbors(SolidChartPartition current)
    {
        if (!_meta.TryGetValue(current.WorldIndex, out AStarVoxelMeta data))
            return false;

        if (TryProcessDirection(current, RectangularDirectionUtility.Perpendicular, data.MovementCost + StraightCost))
            return true;
        if (TryProcessDirection(current, RectangularDirectionUtility.Diagonal, data.MovementCost + DiagonalCost, true))
            return true;

        return false;
    }

    private bool TryProcessDirection(SolidChartPartition current, ReadOnlySpan<RectangularDirection> directions, int cost, bool checkEdges = false)
    {
        foreach (RectangularDirection dir in directions)
        {
            SolidChartPartition? neighbor = current.Neighbors?[(int)dir] ?? null;
            if (neighbor is null || _heap.IsClosed(neighbor) || neighbor.IsImpassable(_request!.UnitSize))
                continue;

            if (checkEdges && !HasValidDiagonalLegs(current, dir))
                continue;

            if (ProcessNeighbor(current, neighbor, cost))
                return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool HasValidDiagonalLegs(SolidChartPartition current, RectangularDirection diagonal)
    {
        (int dx, int dy, int dz) = RectangularDirectionUtility.Offsets[(int)diagonal];

        if (dx != 0 && !IsLegClear(current, DiagonalTraversalLegs.ForXOffset(dx)))
            return false;

        if (dy != 0 && !IsLegClear(current, DiagonalTraversalLegs.ForYOffset(dy)))
            return false;

        if (dz != 0 && !IsLegClear(current, DiagonalTraversalLegs.ForZOffset(dz)))
            return false;

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsLegClear(SolidChartPartition current, RectangularDirection legDir)
    {
        SolidChartPartition? leg = current.Neighbors?[(int)legDir] ?? null;
        // Diagonal legality is a topology check, not a search-order check. Requiring closed legs
        // makes admissible heuristics flood open planes before diagonal routes can form.
        return leg != null && !leg.IsImpassable(_request!.UnitSize);
    }

    /// <summary>
    /// Determines whether a given neighbor voxel should be considered for path expansion.
    /// </summary>
    /// <returns>True if the neighbor is the target destination.</returns>
    private bool ProcessNeighbor(
        SolidChartPartition current,
        SolidChartPartition neighbor,
        int cost)
    {
        // Skip neighbors that have a height difference greater than the allowed maximum
        Fixed64 heightDifference = (current.VoxelPosition.Y - neighbor.VoxelPosition.Y).Abs();
        if (heightDifference > _request!.MaxClimbHeight)
            return false;

        if (neighbor.Voxel == _request.EndNode)
        {
            int endPathCost = CalculatePathCost(neighbor, cost);
            SetPathPartitionData(neighbor, current.WorldIndex, cost, endPathCost);
            return true;
        }

        int pathCost = CalculatePathCost(neighbor, cost, out int heuristicCost);
        if (!_heap.Contains(neighbor))
        {
            SetPathPartitionData(neighbor, current.WorldIndex, cost, pathCost);
            _heap.Add(neighbor, pathCost, heuristicCost);
        }
        else if (_meta.TryGetValue(neighbor.WorldIndex, out AStarVoxelMeta neighborData)
            && neighborData.MovementCost > cost)
        {
            SetPathPartitionData(neighbor, current.WorldIndex, cost, pathCost);
            _heap.UpdatePathCost(neighbor, pathCost, heuristicCost);
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
    /// <param name="pathCost">The total survey path cost recorded for this partition.</param>
    private void SetPathPartitionData(
        SolidChartPartition partition,
        WorldVoxelIndex nextTrailCoordinates,
        int movementCost,
        int pathCost)
    {
        _meta[partition.WorldIndex] = new AStarVoxelMeta
        {
            MovementCost = movementCost,
            NextTrailIndex = nextTrailCoordinates,
            PathCost = pathCost
        };
    }

    /// <summary>
    /// Reconstructs the raw voxel-based path from the destination to the origin by walking backwards through trail links.
    /// </summary>
    /// <returns>A list of voxels from start to end representing the raw path.</returns>
    private void BuildRawPath()
    {
        Voxel? current = _request!.EndNode;
        while (current != null && current != _request.StartNode)
        {
            SolidChartPartition? currentPartition = current.GetPartitionOrDefault<SolidChartPartition>();
            if (currentPartition == null)
                break;

            _rawPath.Insert(0, currentPartition);

            if (!_meta.TryGetValue(current.WorldIndex, out AStarVoxelMeta data) || !data.NextTrailIndex.HasValue)
                break; // break in the trail!

            if (!_request.Context.World.TryGetGridAndVoxel(data.NextTrailIndex.Value, out _, out Voxel? nextTrailVoxel))
                break; // break in the trail!

            current = nextTrailVoxel;
        }

        // Ensure start position is included
        SolidChartPartition? startPartition = _request!.StartNode!.GetPartitionOrDefault<SolidChartPartition>();
        if (startPartition != null)
            _rawPath.Insert(0, startPartition);
    }

    /// <summary>
    /// Constructs a smoothed version of the path using direction changes and optional spline smoothing.
    /// </summary>
    /// <returns>A smoothed list of world positions.</returns>
    private void BuildWaypoints()
    {
        _waypoints.EnsureCapacity(_rawPath.Count);
        SolidChartPartition start = _rawPath[0];
        _waypoints.Add(new()
        {
            Position = start.VoxelPosition,
            PathCost = GetPathCost(start.Voxel),
            GlobalIndex = start.WorldIndex
        });
        ChartOwnerUtility.AddOwners(_chartKeys, start.ChartOwners);

        Vector3d lastDirection = Vector3d.Zero;

        // add 1 to ensure we preserve unwalkable voxels that are close enough to matter for the unit size
        byte scaledUnitSize = (byte)((_request!.UnitSize / _request.Context.VoxelSize).CeilToInt() + 1);
        for (int i = 1; i < _rawPath.Count - 1; i++)
        {
            Vector3d direction = (_rawPath[i + 1].VoxelPosition - _rawPath[i].VoxelPosition).Normalized;


            bool preserveUnwalkable = _rawPath[i].GetNeighborClearance() <= scaledUnitSize;
            bool directionChanged = !lastDirection.FuzzyEqual(direction);

            if (preserveUnwalkable || directionChanged)
            {
                _waypoints.Add(new()
                {
                    Position = _rawPath[i].VoxelPosition,
                    PathCost = GetPathCost(_rawPath[i].Voxel),
                    GlobalIndex = _rawPath[i].WorldIndex
                });
            }

            lastDirection = direction;
            ChartOwnerUtility.AddOwners(_chartKeys, _rawPath[i].ChartOwners);
        }

        SolidChartPartition end = _rawPath.FromEnd(1);
        _waypoints.Add(new()
        {
            Position = end.VoxelPosition,
            PathCost = GetPathCost(end.Voxel),
            GlobalIndex = end.WorldIndex,
            IsGoal = true
        });
        ChartOwnerUtility.AddOwners(_chartKeys, end.ChartOwners);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CalculatePathCost(SolidChartPartition partition, int movementCost) =>
        CalculatePathCost(partition, movementCost, out _);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CalculatePathCost(
        SolidChartPartition partition,
        int movementCost,
        out int heuristicCost)
    {
        heuristicCost = CalculateHeuristic(
            partition.VoxelPosition,
            _request!.EndNode!.WorldPosition,
            _request.Heuristic);

        return partition.PathCostModifier + movementCost + heuristicCost;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetPathCost(Voxel voxel)
    {
        return _meta[voxel.WorldIndex].PathCost;
    }

    private void ClearWorkingState()
    {
        _heap.FastClear();
        _meta.Clear();
        _rawPath.FastClear();
        _waypoints.FastClear();
        _chartKeys.Clear();
    }

    /// <summary>
    /// Calculates the heuristic cost for the current voxel based on the target voxel and the heuristic method used.
    /// This implementation takes into account the X, Y, and Z axes for pathfinding.
    /// </summary>
    public static int CalculateHeuristic(
        Vector3d currentPosition,
        Vector3d targetPosition,
        HeuristicMethod heuristicMethod)
    {
        Fixed64 heuristicCost = Fixed64.MaxValue;

        // Calculate the absolute distance in each axis
        Vector3d dst = Vector3d.Abs(currentPosition - targetPosition);

        switch (heuristicMethod)
        {
            case HeuristicMethod.Manhattan:
                // Sum the distances and multiply by 100 for the heuristic cost
                heuristicCost = (dst.X + dst.Y + dst.Z) * StraightCost;
                break;
            case HeuristicMethod.Octile:
                Fixed64 maxXY = FixedMath.Max(dst.X, dst.Y);
                Fixed64 max = FixedMath.Max(maxXY, dst.Z);
                Fixed64 minXY = FixedMath.Min(dst.X, dst.Y);
                Fixed64 min = FixedMath.Min(minXY, dst.Z);
                Fixed64 middle = dst.X + dst.Y + dst.Z - max - min;
                heuristicCost = (middle * DiagonalCost) + ((max - middle) * StraightCost);
                break;
            case HeuristicMethod.Euclidean:
                // Calculate the squared distance and find the square root
                Fixed64 d = dst.X * dst.X + dst.Y * dst.Y + dst.Z * dst.Z;
                d = FixedMath.Sqrt(d);
                // Multiply the result by 100 for the heuristic cost
                heuristicCost = d * StraightCost;
                break;
            default:
                break;
        }

        return heuristicCost.CeilToInt();
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
                Fixed64 t = (Fixed64)j / (Fixed64)resolutionPerSegment;

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
        output[outputIndex] = input[^1];
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

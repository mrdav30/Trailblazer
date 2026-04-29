using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Trailblazer.Pathing;

/// <summary>
/// Responsible for generating flow fields using a wavefront flood-fill algorithm.
/// Provides pathfinding data suitable for many agents with shared destinations.
/// </summary>
public class FlowFieldSurveyor
{
    #region Singleton Instances

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

    private readonly PathHeap<SolidChartPartition> _heap = new();

    private readonly SwiftHashSet<string> _chartKeys = new();

    private FlowFieldPathRequest? _request;

    /// <summary>
    /// Attempts to create a shared flow field path from the start to the end voxel specified in the request.
    /// </summary>
    /// <param name="request">A flow field path request containing the start, end, and search parameters.</param>
    /// <returns>A dictionary of flow fields indexed by spawn token.</returns>
    public FlowFieldSurveyResult FindPath(FlowFieldPathRequest request)
    {
        lock (SurveyorLock.GlobalLock)
        {
            if (request == null
            || request.HasZeroDisplacement
            || request.EndNode == null
            || !request.EndNode.TryGetPartition(out SolidChartPartition? targetPart)
            || targetPart == null)
            {
                return FlowFieldSurveyResult.Empty;
            }

            _request = request;

            ClearWorkingState();
            // Start from the end and move towards the start voxel
            _heap.Add(targetPart, pathCost: 0);
            ChartOwnerUtility.AddOwners(_chartKeys, targetPart.ChartOwners);

            if (!FloodPath())
            {
                ClearWorkingState();
                return FlowFieldSurveyResult.Empty;
            }

            SwiftDictionary<WorldVoxelIndex, FlowField> flowFields = GenerateFlowFields();
            string[] chartsUsed = _chartKeys.ToArray();
            FlowFieldSurveyResult result = FlowFieldSurveyResult.Create(flowFields, chartsUsed, request.RequestCacheKey);
            ClearWorkingState();
            return result;
        }
    }

    /// <summary>
    /// Executes the wavefront expansion (flood fill) phase of the flow field generation algorithm.
    /// Starts from the goal and expands outward until the start voxel is reached or search range is exceeded.
    /// </summary>
    /// <returns><c>true</c> if the start voxel is reached within the maximum range; otherwise <c>false</c>.</returns>
    private bool FloodPath()
    {
        FlowFieldPathRequest request = _request!;
        bool targetReached = false;

        int iterations = 0;
        int searchSize = request.MaxPathSearchRange;
        int maxFloodRange = 0;

        while (_heap.RemoveFirst(out SolidChartPartition? current)
            && current != null
            && iterations++ < searchSize)
        {
            int currentPathCost = GetPathCost(current);

            // Check if we found our way to the start voxel
            if (!targetReached)
            {
                if (current.Voxel == request.StartNode)
                {
                    maxFloodRange = currentPathCost + request.ExtraFloodRange;
                    targetReached = true;
                }

            }
            else if (currentPathCost >= maxFloodRange)
                break;

            AnalyzeNeighborDistance(current, currentPathCost);

            _heap.SetClosed(current);
        }

        return targetReached;
    }

    /// <summary>
    /// Evaluates each walkable neighbor of the current partition and assigns a heap cost if a shorter path is found.
    /// Ensures the wavefront expands in an optimal order.
    /// </summary>
    /// <param name="current">The current path partition being evaluated.</param>
    /// <param name="currentPathCost">The current flood distance stored for the partition.</param>
    private void AnalyzeNeighborDistance(SolidChartPartition current, int currentPathCost)
    {
        TryProcessDirection(current, SpatialAwareness.PerpendicularDirections, currentPathCost);
        TryProcessDirection(current, SpatialAwareness.DiagonalDirections, currentPathCost, true);
    }

    private void TryProcessDirection(
        SolidChartPartition current,
        SpatialDirection[] directions,
        int currentPathCost,
        bool checkEdges = false)
    {
        FlowFieldPathRequest request = _request!;
        SolidChartPartition?[]? neighbors = current.Neighbors;
        if (neighbors == null)
            return;

        foreach (SpatialDirection dir in directions)
        {
            SolidChartPartition? neighbor = neighbors[(int)dir];
            if (neighbor is null || _heap.IsClosed(neighbor) || neighbor.IsImpassable(request.UnitSize))
                continue;

            if (ExceedsMaxClimbHeight(current, neighbor))
                continue;

            if (checkEdges && !HasValidDiagonalLegs(current, dir))
                continue;

            int newCost = currentPathCost + 1;

            if (!_heap.Contains(neighbor))
            {
                _heap.Add(neighbor, newCost);
            }
            else if (GetPathCost(neighbor) > newCost)
            {
                _heap.UpdatePathCost(neighbor, newCost);
                _heap.SortUp(neighbor);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ExceedsMaxClimbHeight(SolidChartPartition current, SolidChartPartition neighbor)
    {
        FlowFieldPathRequest request = _request!;
        Fixed64 heightDifference = (current.VoxelPosition.y - neighbor.VoxelPosition.y).Abs();
        return heightDifference > request.MaxClimbHeight;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool HasValidDiagonalLegs(SolidChartPartition current, SpatialDirection diagonal)
    {
        (int dx, int dy, int dz) = SpatialAwareness.DirectionOffsets[(int)diagonal];

        if (dx != 0 && !IsLegClear(current, dx > 0 ? SpatialDirection.North : SpatialDirection.West))
            return false;

        if (dy != 0 && !IsLegClear(current, dy > 0 ? SpatialDirection.Above : SpatialDirection.Below))
            return false;

        if (dz != 0 && !IsLegClear(current, dz > 0 ? SpatialDirection.East : SpatialDirection.South))
            return false;

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool IsLegClear(SolidChartPartition current, SpatialDirection legDir)
    {
        FlowFieldPathRequest request = _request!;
        SolidChartPartition?[]? neighbors = current.Neighbors;
        if (neighbors == null)
            return false;

        SolidChartPartition? leg = neighbors[(int)legDir];
        return leg != null && _heap.IsClosed(leg) && !leg.IsImpassable(request.UnitSize);
    }

    /// <summary>
    /// Converts the results of the flood fill phase into directional flow fields pointing toward the goal.
    /// Each partition is assigned a direction vector blending shortest path and direct-to-goal direction.
    /// </summary>
    /// <returns>A dictionary of directional flow field data indexed by voxel spawn tokens.</returns>
    private SwiftDictionary<WorldVoxelIndex, FlowField> GenerateFlowFields()
    {
        FlowFieldPathRequest request = _request!;
        Voxel endNode = request.EndNode!;
        SwiftDictionary<WorldVoxelIndex, FlowField> result = new(_heap.TrackedCount);
        // Fixed64 totalDistance = Fixed64.One + _startDistanceMetric; // + 1 for end part

        foreach (SolidChartPartition current in _heap.EnumerateClosed())
        {
            FlowField currentFlow = new()
            {
                GlobalIndex = current.GlobalIndex,
                PathCost = GetPathCostTotal(current)
            };

            if (current.Voxel == endNode)
            {
                // Ensure end voxel is include, it shouldn't point anywhere
                currentFlow.IsGoal = true;
                result.Add(current.GlobalIndex, currentFlow);
                continue;
            }

            // Go through all neighbours and find the one with the lowest distance
            SolidChartPartition? minPartition = null;
            int minCost = int.MaxValue;
            SolidChartPartition?[]? neighbors = current.Neighbors;
            if (neighbors == null)
            {
                result.Add(current.GlobalIndex, currentFlow);
                ChartOwnerUtility.AddOwners(_chartKeys, current.ChartOwners);
                continue;
            }

            for (int i = 0; i < neighbors.Length; i++)
            {
                SolidChartPartition? nPart = neighbors[i];
                // check closed heap version to ensure neighbor was part of flood phase
                if (nPart == null || !_heap.IsClosed(nPart))
                    continue;

                if (ExceedsMaxClimbHeight(current, nPart))
                    continue;

                if (i > 6 && !HasValidDiagonalLegs(current, (SpatialDirection)i))
                    continue;

                int cost = GetPathCostTotal(nPart);
                if (cost < minCost)
                {
                    minPartition = nPart;
                    minCost = cost;
                }
            }

            // If we found a valid neighbour, point in its direction by applying distance-weighted blending
            if (minPartition != null)
            {
                Vector3d raw = minPartition.VoxelPosition - current.VoxelPosition;

                if (raw == Vector3d.Zero)
                    currentFlow.Direction = Vector3d.Zero;
                else
                    currentFlow.Direction = raw.Normalize();
            }

            result.Add(current.GlobalIndex, currentFlow);
            ChartOwnerUtility.AddOwners(_chartKeys, current.ChartOwners);
        }

        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetPathCost(SolidChartPartition partition)
    {
        return _heap.TryGetPathCost(partition, out int pathCost)
            ? pathCost
            : int.MaxValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetPathCostTotal(SolidChartPartition partition)
    {
        int pathCost = GetPathCost(partition);
        return pathCost == int.MaxValue
            ? int.MaxValue
            : pathCost + partition.PathCostModifier;
    }

    private void ClearWorkingState()
    {
        _heap.FastClear();
        _chartKeys.Clear();
    }

    /// <summary>
    /// Samples an interpolated flow direction from a given world position using bilinear interpolation.
    /// Helps agents move smoothly between grid cells.
    /// </summary>
    /// <param name="worldPosition">The world-space position to sample from.</param>
    /// <param name="fields">A dictionary of flow field data.</param>
    /// <returns>An interpolated directional vector.</returns>
    public static Vector3d SampleFlowVector(Vector3d worldPosition, SwiftDictionary<WorldVoxelIndex, FlowField> fields)
    {
        if (fields == null || fields.Count == 0)
            return Vector3d.Zero;

        // Get bottom-left corner of the square the agent is standing in
        Vector3d corner = new(
            FixedMath.Floor(worldPosition.x / TrailblazerWorldManager.VoxelSize) * TrailblazerWorldManager.VoxelSize,
            FixedMath.Floor(worldPosition.y / TrailblazerWorldManager.VoxelSize) * TrailblazerWorldManager.VoxelSize,
            FixedMath.Floor(worldPosition.z / TrailblazerWorldManager.VoxelSize) * TrailblazerWorldManager.VoxelSize
        );

        // Compute normalized offset in cell (0..1)
        Fixed64 dx = (worldPosition.x - corner.x) / TrailblazerWorldManager.VoxelSize;
        Fixed64 dz = (worldPosition.z - corner.z) / TrailblazerWorldManager.VoxelSize;

        // Sample the 4 surrounding voxel centers
        Vector3d bottomLeft = corner;
        Vector3d bottomRight = corner + new Vector3d(TrailblazerWorldManager.VoxelSize, Fixed64.Zero, Fixed64.Zero);
        Vector3d topLeft = corner + new Vector3d(Fixed64.Zero, Fixed64.Zero, TrailblazerWorldManager.VoxelSize);
        Vector3d topRight = corner + new Vector3d(TrailblazerWorldManager.VoxelSize, Fixed64.Zero, TrailblazerWorldManager.VoxelSize);

        // Get flow vectors
        Vector3d f00 = GetFlowDirection(bottomLeft, fields);
        Vector3d f10 = GetFlowDirection(bottomRight, fields);
        Vector3d f01 = GetFlowDirection(topLeft, fields);
        Vector3d f11 = GetFlowDirection(topRight, fields);

        // Bilinear interpolation
        Vector3d zHigh = f00 * (Fixed64.One - dx) + f10 * dx;
        Vector3d zLow = f01 * (Fixed64.One - dx) + f11 * dx;
        Vector3d blended = zHigh * (Fixed64.One - dz) + zLow * dz;

        blended.Normalize();
        return blended;
    }

    /// <summary>
    /// Attempts to locate the closest valid voxel from which to begin flow-based movement.
    /// Useful for finding an initial entry point to the flow field.
    /// </summary>
    /// <param name="origin">The world-space origin to search from.</param>
    /// <param name="fields">Flow field data indexed by voxel spawn token.</param>
    /// <param name="result">The closest valid voxel, if found.</param>
    /// <param name="range">Maximum range to search.</param>
    /// <returns><c>true</c> if a nearby flow field anchor is found; otherwise <c>false</c>.</returns>
    public static bool TryGetNearestFlowAnchor(
        Vector3d origin,
        SwiftDictionary<WorldVoxelIndex, FlowField> fields,
        Fixed64 range,
        out Voxel? result)
    {
        result = null;
        if (fields == null || fields.Count == 0)
            return false;

        Fixed64 minDistanceSq = range * range;
        bool found = false;

        foreach (FlowField flow in fields.Values)
        {
            if (!TrailblazerWorldManager.TryGetGridAndVoxel(flow.GlobalIndex, out _, out Voxel? flowVoxel)
                || flowVoxel == null)
                continue;

            Fixed64 distSq = Vector3d.SqrDistance(origin, flowVoxel.WorldPosition);
            if (distSq <= minDistanceSq)
            {
                result = flowVoxel;
                minDistanceSq = distSq;
                found = true;
            }
        }

        return found;
    }

    /// <summary>
    /// Retrieves the raw directional flow vector at the given world-space position, if available.
    /// </summary>
    /// <param name="position">The position to query within the flow field.</param>
    /// <param name="fields">Flow field data indexed by voxel index.</param>
    /// <returns>The direction vector, or <c>Vector3d.Zero</c> if no field exists.</returns>
    public static Vector3d GetFlowDirection(Vector3d position, SwiftDictionary<WorldVoxelIndex, FlowField> fields)
    {
        if (TrailblazerWorldManager.TryGetVoxel(position, out Voxel? voxel)
            && voxel != null)
        {
            if (fields.TryGetValue(voxel.WorldIndex, out FlowField field))
                return field.Direction;
        }
        return Vector3d.Zero;
    }

    public static FlowField GetFlowField(Vector3d position, SwiftDictionary<WorldVoxelIndex, FlowField> fields)
    {
        if (TrailblazerWorldManager.TryGetVoxel(position, out Voxel? voxel)
            && voxel != null)
        {
            if (fields.TryGetValue(voxel.WorldIndex, out FlowField field))
                return field;
        }
        return default;
    }
}

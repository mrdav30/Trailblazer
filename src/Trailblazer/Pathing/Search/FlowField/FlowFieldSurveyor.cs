//=======================================================================
// FlowFieldSurveyor.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

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
    private static readonly Vector3d[] _normalizedDirectionByNeighbor = CreateNormalizedDirectionLookup();

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

    private readonly SurveyorLock _scratchLock = new();

    private readonly PathHeap<SolidChartPartition> _heap = new();

    private readonly SwiftHashSet<string> _chartKeys = new();

    private readonly SwiftList<FlowFieldSamplingGrid> _samplingGrids = new();

    private readonly SwiftList<FlowFieldSamplingGridBuilder> _samplingGridBuilders = new();

    private FlowFieldPathRequest? _request;

    /// <summary>
    /// Attempts to create a shared flow field path from the start to the end voxel specified in the request.
    /// </summary>
    /// <param name="request">A flow field path request containing the start, end, and search parameters.</param>
    /// <returns>A dictionary of flow fields indexed by spawn token.</returns>
    public FlowFieldSurveyResult FindPath(FlowFieldPathRequest request)
    {
        lock (_scratchLock)
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
            FlowFieldSamplingGrid[] samplingGrids = _samplingGrids.Count == 0
                ? Array.Empty<FlowFieldSamplingGrid>()
                : _samplingGrids.ToArray();
            FlowFieldSurveyResult result = FlowFieldSurveyResult.Create(
                request.Context,
                flowFields,
                chartsUsed,
                request.RequestCacheKey,
                samplingGrids);
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
        WorldVoxelIndex startIndex = request.StartNode!.WorldIndex;
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
                if (current.WorldIndex == startIndex)
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
        TryProcessDirection(current, RectangularDirectionUtility.Perpendicular, currentPathCost);
        TryProcessDirection(current, RectangularDirectionUtility.Diagonal, currentPathCost, true);
    }

    private void TryProcessDirection(
        SolidChartPartition current,
        ReadOnlySpan<RectangularDirection> directions,
        int currentPathCost,
        bool checkEdges = false)
    {
        FlowFieldPathRequest request = _request!;
        SolidChartPartition?[]? neighbors = current.Neighbors;
        if (neighbors == null)
            return;

        foreach (RectangularDirection dir in directions)
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
        Fixed64 heightDifference = (current.VoxelPosition.Y - neighbor.VoxelPosition.Y).Abs();
        return heightDifference > request.MaxClimbHeight;
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
        WorldVoxelIndex endIndex = request.EndNode!.WorldIndex;
        PrepareSamplingGrids();

        SwiftDictionary<WorldVoxelIndex, FlowField> result = new(_heap.TrackedCount);
        // Fixed64 totalDistance = Fixed64.One + _startDistanceMetric; // + 1 for end part

        foreach (SolidChartPartition current in _heap.EnumerateClosed())
        {
            FlowFieldSamplingGrid samplingGrid = GetSamplingGrid(current.WorldIndex);

            FlowField currentFlow = new()
            {
                GlobalIndex = current.WorldIndex,
                PathCost = GetPathCostTotal(current)
            };

            if (current.WorldIndex == endIndex)
            {
                // Ensure end voxel is include, it shouldn't point anywhere
                currentFlow.IsGoal = true;
                AddFlowField(result, samplingGrid, current.WorldIndex, currentFlow);
                continue;
            }

            // Go through all neighbours and find the one with the lowest distance
            SolidChartPartition? minPartition = null;
            int minCost = int.MaxValue;
            int minDirectionIndex = -1;
            SolidChartPartition?[]? neighbors = current.Neighbors;
            if (neighbors == null)
            {
                AddFlowField(result, samplingGrid, current.WorldIndex, currentFlow);
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

                if (RectangularDirectionUtility.IsDiagonalNeighbor((RectangularDirection)i)
                    && !HasValidDiagonalLegs(current, (RectangularDirection)i))
                {
                    continue;
                }

                int cost = GetPathCostTotal(nPart);
                if (cost < minCost)
                {
                    minPartition = nPart;
                    minCost = cost;
                    minDirectionIndex = i;
                }
            }

            // If we found a valid neighbour, point in its direction by applying distance-weighted blending
            if (minPartition != null)
                currentFlow.Direction = _normalizedDirectionByNeighbor[minDirectionIndex];

            AddFlowField(result, samplingGrid, current.WorldIndex, currentFlow);
            ChartOwnerUtility.AddOwners(_chartKeys, current.ChartOwners);
        }

        return result;
    }

    private static void AddFlowField(
        SwiftDictionary<WorldVoxelIndex, FlowField> fields,
        FlowFieldSamplingGrid samplingGrid,
        WorldVoxelIndex index,
        FlowField field)
    {
        fields.Add(index, field);
        samplingGrid.AddDirection(index, field.Direction);
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
        _samplingGrids.Clear();
        _samplingGridBuilders.Clear();
    }

    private void PrepareSamplingGrids()
    {
        _samplingGridBuilders.Clear();
        _samplingGrids.Clear();

        foreach (SolidChartPartition current in _heap.EnumerateClosed())
        {
            FlowFieldSamplingGridBuilder builder = GetOrAddSamplingGridBuilder(current.WorldIndex, current.VoxelPosition);
            builder.Include(current.WorldIndex);
        }

        for (int i = 0; i < _samplingGridBuilders.Count; i++)
            _samplingGrids.Add(_samplingGridBuilders[i].Create(_request!.Context.VoxelSize));
    }

    private FlowFieldSamplingGridBuilder GetOrAddSamplingGridBuilder(WorldVoxelIndex index, Vector3d worldPosition)
    {
        for (int i = 0; i < _samplingGridBuilders.Count; i++)
        {
            if (_samplingGridBuilders[i].MatchesGrid(index))
                return _samplingGridBuilders[i];
        }

        FlowFieldSamplingGridBuilder builder = new(index, worldPosition, _request!.Context.VoxelSize);
        _samplingGridBuilders.Add(builder);
        return builder;
    }

    private FlowFieldSamplingGrid GetSamplingGrid(WorldVoxelIndex index)
    {
        for (int i = 0; i < _samplingGrids.Count; i++)
        {
            if (_samplingGrids[i].MatchesGrid(index))
                return _samplingGrids[i];
        }

        throw new InvalidOperationException("Flow-field sampling grid was not prepared for the closed partition.");
    }

    private static Vector3d[] CreateNormalizedDirectionLookup()
    {
        ReadOnlySpan<(int x, int y, int z)> offsets = RectangularDirectionUtility.Offsets;
        Vector3d[] directions = new Vector3d[offsets.Length];
        for (int i = 0; i < directions.Length; i++)
        {
            (int x, int y, int z) = offsets[i];
            directions[i] = new Vector3d((Fixed64)x, (Fixed64)y, (Fixed64)z).Normalized;
        }

        return directions;
    }

    /// <summary>
    /// Samples an interpolated flow direction from a survey result using direct index arithmetic.
    /// </summary>
    /// <param name="context">The world context to sample against.</param>
    /// <param name="worldPosition">The world-space position to sample from.</param>
    /// <param name="result">The flow-field survey result to sample.</param>
    /// <returns>An interpolated directional vector.</returns>
    public static Vector3d SampleFlowVector(
        TrailblazerWorldContext context,
        Vector3d worldPosition,
        FlowFieldSurveyResult result)
    {
        if (result == null || !result.HasPath || result.Fields == null)
            return Vector3d.Zero;

        PathRequestContextResolver.ThrowIfUnusable(context);
        ThrowIfResultOwnedByDifferentContext(result, context);

        return SampleFlowVector(context, worldPosition, result.Fields, result.SamplingGrids);
    }

    private static Vector3d SampleFlowVector(
        TrailblazerWorldContext context,
        Vector3d worldPosition,
        SwiftDictionary<WorldVoxelIndex, FlowField> fields,
        FlowFieldSamplingGrid[]? samplingGrids)
    {
        if (fields == null || fields.Count == 0)
            return Vector3d.Zero;

        Fixed64 voxelSize = context.VoxelSize;

        // Get bottom-left corner of the square the agent is standing in
        Vector3d corner = new(
            FixedMath.Floor(worldPosition.X / voxelSize) * voxelSize,
            FixedMath.Floor(worldPosition.Y / voxelSize) * voxelSize,
            FixedMath.Floor(worldPosition.Z / voxelSize) * voxelSize
        );

        // Compute normalized offset in cell (0..1)
        Fixed64 dx = (worldPosition.X - corner.X) / voxelSize;
        Fixed64 dz = (worldPosition.Z - corner.Z) / voxelSize;

        if (dx == Fixed64.Zero && dz == Fixed64.Zero)
            return GetFlowDirection(context, corner, fields, samplingGrids);

        // Sample the 4 surrounding voxel centers
        Vector3d bottomLeft = corner;
        Vector3d bottomRight = corner + new Vector3d(voxelSize, Fixed64.Zero, Fixed64.Zero);
        Vector3d topLeft = corner + new Vector3d(Fixed64.Zero, Fixed64.Zero, voxelSize);
        Vector3d topRight = corner + new Vector3d(voxelSize, Fixed64.Zero, voxelSize);

        // Get flow vectors
        Vector3d f00 = GetFlowDirection(context, bottomLeft, fields, samplingGrids);
        Vector3d f10 = GetFlowDirection(context, bottomRight, fields, samplingGrids);
        Vector3d f01 = GetFlowDirection(context, topLeft, fields, samplingGrids);
        Vector3d f11 = GetFlowDirection(context, topRight, fields, samplingGrids);

        // Bilinear interpolation
        Vector3d zHigh = f00 * (Fixed64.One - dx) + f10 * dx;
        Vector3d zLow = f01 * (Fixed64.One - dx) + f11 * dx;
        Vector3d blended = zHigh * (Fixed64.One - dz) + zLow * dz;

        blended.NormalizeInPlace();
        return blended;
    }

    /// <summary>
    /// Attempts to locate the closest valid voxel from which to begin flow-based movement.
    /// Useful for finding an initial entry point to the flow field.
    /// </summary>
    /// <param name="context">The world context to search against.</param>
    /// <param name="origin">The world-space origin to search from.</param>
    /// <param name="fields">Flow field data indexed by voxel spawn token.</param>
    /// <param name="result">The closest valid voxel, if found.</param>
    /// <param name="range">Maximum range to search.</param>
    /// <returns><c>true</c> if a nearby flow field anchor is found; otherwise <c>false</c>.</returns>
    public static bool TryGetNearestFlowAnchor(
        TrailblazerWorldContext context,
        Vector3d origin,
        SwiftDictionary<WorldVoxelIndex, FlowField> fields,
        Fixed64 range,
        out Voxel? result)
    {
        result = null;
        PathRequestContextResolver.ThrowIfUnusable(context);
        if (fields == null || fields.Count == 0)
            return false;

        Fixed64 minDistanceSq = range * range;
        bool found = false;

        foreach (FlowField flow in fields.Values)
        {
            if (!context.World.TryGetGridAndVoxel(flow.GlobalIndex, out _, out Voxel? flowVoxel)
                || flowVoxel == null)
                continue;

            Fixed64 distSq = Vector3d.DistanceSquared(origin, flowVoxel.WorldPosition);
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
    /// <param name="context">The world context to query against.</param>
    /// <param name="position">The position to query within the flow field.</param>
    /// <param name="fields">Flow field data indexed by voxel index.</param>
    /// <returns>The direction vector, or <c>Vector3d.Zero</c> if no field exists.</returns>
    public static Vector3d GetFlowDirection(
        TrailblazerWorldContext context,
        Vector3d position,
        SwiftDictionary<WorldVoxelIndex, FlowField> fields)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        if (context.World.TryGetVoxel(position, out Voxel? voxel)
            && voxel != null)
        {
            if (fields.TryGetValue(voxel.WorldIndex, out FlowField field))
                return field.Direction;
        }
        return Vector3d.Zero;
    }

    private static Vector3d GetFlowDirection(
        TrailblazerWorldContext context,
        Vector3d position,
        SwiftDictionary<WorldVoxelIndex, FlowField> fields,
        FlowFieldSamplingGrid[]? samplingGrids)
    {
        if (samplingGrids == null || samplingGrids.Length == 0)
            return GetFlowDirection(context, position, fields);

        for (int i = 0; i < samplingGrids.Length; i++)
        {
            if (samplingGrids[i].TryGetDirection(position, out Vector3d direction))
                return direction;
        }

        return Vector3d.Zero;
    }

    /// <summary>
    /// Retrieves the flow field associated with the specified world position, if available.
    /// </summary>
    /// <remarks>
    /// If the position does not correspond to a valid voxel or no flow field is found for the voxel,
    /// the method returns the default value for FlowField.
    /// </remarks>
    /// <param name="context">The world context to query against.</param>
    /// <param name="position">The world position for which to retrieve the corresponding flow field.</param>
    /// <param name="fields">A dictionary mapping world voxel indices to their associated flow fields. Must not be null.</param>
    /// <returns>
    /// The flow field associated with the specified position if found; otherwise, the default value for the FlowField type.
    /// </returns>
    public static FlowField GetFlowField(
        TrailblazerWorldContext context,
        Vector3d position,
        SwiftDictionary<WorldVoxelIndex, FlowField> fields)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        if (context.World.TryGetVoxel(position, out Voxel? voxel)
            && voxel != null)
        {
            if (fields.TryGetValue(voxel.WorldIndex, out FlowField field))
                return field;
        }
        return default;
    }

    private static void ThrowIfResultOwnedByDifferentContext(
        FlowFieldSurveyResult result,
        TrailblazerWorldContext expectedContext)
    {
        if (result.Context == null || ReferenceEquals(result.Context, expectedContext))
            return;

        throw new InvalidOperationException(
            "Flow field survey result belongs to a different owning TrailblazerWorldContext.");
    }

    private sealed class FlowFieldSamplingGridBuilder
    {
        private readonly WorldVoxelIndex _sampleIndex;
        private readonly Vector3d _originWorldPosition;
        private int _minX;
        private int _minY;
        private int _minZ;
        private int _maxX;
        private int _maxY;
        private int _maxZ;

        public int Count { get; private set; }

        public FlowFieldSamplingGridBuilder(
            WorldVoxelIndex sampleIndex,
            Vector3d sampleWorldPosition,
            Fixed64 voxelSize)
        {
            _sampleIndex = sampleIndex;
            VoxelIndex localIndex = sampleIndex.VoxelIndex;
            _originWorldPosition = new Vector3d(
                sampleWorldPosition.X - (voxelSize * (Fixed64)localIndex.x),
                sampleWorldPosition.Y - (voxelSize * (Fixed64)localIndex.y),
                sampleWorldPosition.Z - (voxelSize * (Fixed64)localIndex.z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MatchesGrid(WorldVoxelIndex index)
        {
            return index.WorldSpawnToken == _sampleIndex.WorldSpawnToken
                && index.GridIndex == _sampleIndex.GridIndex
                && index.GridSpawnToken == _sampleIndex.GridSpawnToken;
        }

        public void Include(WorldVoxelIndex index)
        {
            VoxelIndex localIndex = index.VoxelIndex;
            if (Count == 0)
            {
                _minX = _maxX = localIndex.x;
                _minY = _maxY = localIndex.y;
                _minZ = _maxZ = localIndex.z;
            }
            else
            {
                if (localIndex.x < _minX) _minX = localIndex.x;
                if (localIndex.y < _minY) _minY = localIndex.y;
                if (localIndex.z < _minZ) _minZ = localIndex.z;
                if (localIndex.x > _maxX) _maxX = localIndex.x;
                if (localIndex.y > _maxY) _maxY = localIndex.y;
                if (localIndex.z > _maxZ) _maxZ = localIndex.z;
            }

            Count++;
        }

        public FlowFieldSamplingGrid Create(Fixed64 voxelSize)
        {
            return new FlowFieldSamplingGrid(
                _sampleIndex,
                _originWorldPosition,
                voxelSize,
                _minX,
                _minY,
                _minZ,
                _maxX,
                _maxY,
                _maxZ,
                Count);
        }
    }
}

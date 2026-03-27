using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Trailblazer.Pathing;

internal struct VolumeVoxelMeta
{
    public int MovementCost;

    public GlobalVoxelIndex? NextTrailIndex;
}

/// <summary>
/// Executes chart-optional A* pathfinding through raw voxel volume.
/// </summary>
public sealed class VolumeSurveyor
{
    private static readonly Lazy<VolumeSurveyor> _instance =
        new(() => new VolumeSurveyor(), LazyThreadSafetyMode.ExecutionAndPublication);

    public static VolumeSurveyor Shared => _instance.Value;

    private readonly PathHeap<Voxel> _heap = new();

    private readonly SwiftDictionary<Voxel, VolumeVoxelMeta> _meta = new();

    private readonly SwiftList<Voxel> _rawPath = new();

    private readonly SwiftList<AStarWaypoint> _waypoints = new();

    private readonly SwiftHashSet<string> _chartKeys = new();

    private VolumePathRequest _request;

    public VolumeSurveyResult FindPath(VolumePathRequest request)
    {
        lock (SurveyorLock.GlobalLock)
        {
            if (request == null || request.HasZeroDisplacement || !request.HasValidEndpoints)
                return VolumeSurveyResult.Empty;

            _request = request;

            _heap.FastClear();
            _meta.Clear();
            _rawPath.FastClear();
            _waypoints.FastClear();
            _chartKeys.Clear();

            _meta[_request.StartNode] = new VolumeVoxelMeta();
            _heap.Add(_request.StartNode, 0);

            if (!TracePath())
                return VolumeSurveyResult.Empty;

            BuildRawPath();
            TrackRawPathChartOwners();
            BuildWaypoints();

            return _waypoints.Count > 0
                ? VolumeSurveyResult.Create(_waypoints.ToArray(), _chartKeys.ToArray(), request.RequestCacheKey)
                : VolumeSurveyResult.Empty;
        }
    }

    private bool TracePath()
    {
        int iterations = 0;
        int searchSize = _request.MaxPathSearchRange;

        while (_heap.RemoveFirst(out Voxel current) && iterations++ < searchSize)
        {
            if (current == _request.EndNode)
                return true;

            if (ProcessNeighbors(current))
                return true;

            _heap.SetClosed(current);
        }

        return false;
    }

    private bool ProcessNeighbors(Voxel current)
    {
        if (!_meta.TryGetValue(current, out VolumeVoxelMeta data))
            return false;

        if (TryProcessDirections(
            current,
            SpatialAwareness.PerpendicularDirections,
            data.MovementCost + AStarSurveyor.StraightCost))
        {
            return true;
        }

        if (TryProcessDirections(
            current,
            SpatialAwareness.DiagonalDirections,
            data.MovementCost + AStarSurveyor.DiagonalCost,
            checkEdges: true))
        {
            return true;
        }

        return false;
    }

    private bool TryProcessDirections(
        Voxel current,
        SpatialDirection[] directions,
        int movementCost,
        bool checkEdges = false)
    {
        foreach (SpatialDirection dir in directions)
        {
            if (!current.TryGetNeighborFromDirection(dir, out Voxel neighbor, useCache: true)
                || _heap.IsClosed(neighbor))
            {
                continue;
            }

            if (checkEdges && !HasValidDiagonalLegs(current, dir))
                continue;

            if (!CanTraverseVoxel(neighbor))
                continue;

            if (ProcessNeighbor(current, neighbor, movementCost))
                return true;
        }

        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool HasValidDiagonalLegs(Voxel current, SpatialDirection diagonal)
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
    private bool IsLegClear(Voxel current, SpatialDirection legDir)
    {
        return current.TryGetNeighborFromDirection(legDir, out Voxel leg, useCache: true)
            && CanTraverseVoxel(leg);
    }

    private bool ProcessNeighbor(Voxel current, Voxel neighbor, int movementCost)
    {
        int totalMovementCost = movementCost + GetTraversalCostModifier(neighbor);

        if (neighbor == _request.EndNode)
        {
            SetVoxelData(neighbor, current.GlobalIndex, totalMovementCost);
            return true;
        }

        int pathCost = CalculatePathCost(neighbor.WorldPosition, totalMovementCost);
        if (!_heap.Contains(neighbor))
        {
            SetVoxelData(neighbor, current.GlobalIndex, totalMovementCost);
            _heap.Add(neighbor, pathCost);
        }
        else if (_meta.TryGetValue(neighbor, out VolumeVoxelMeta neighborData)
            && neighborData.MovementCost > totalMovementCost)
        {
            SetVoxelData(neighbor, current.GlobalIndex, totalMovementCost);
            _heap.UpdatePathCost(neighbor, pathCost);
            _heap.SortUp(neighbor);
        }

        return false;
    }

    private void SetVoxelData(
        Voxel voxel,
        GlobalVoxelIndex nextTrailIndex,
        int movementCost)
    {
        _meta[voxel] = new VolumeVoxelMeta()
        {
            MovementCost = movementCost,
            NextTrailIndex = nextTrailIndex
        };
    }

    private void BuildRawPath()
    {
        Voxel current = _request.EndNode;
        while (current != _request.StartNode)
        {
            _rawPath.Insert(0, current);

            if (!_meta.TryGetValue(current, out VolumeVoxelMeta data)
                || !data.NextTrailIndex.HasValue)
                break;

            if (!GlobalGridManager.TryGetGridAndVoxel(data.NextTrailIndex.Value, out _, out Voxel nextTrailVoxel))
                break;

            current = nextTrailVoxel;
        }

        _rawPath.Insert(0, _request.StartNode);
    }

    private void BuildWaypoints()
    {
        if (_rawPath.Count == 0 || _rawPath[0] == _rawPath.FromEnd(1))
            return;

        _waypoints.EnsureCapacity(_rawPath.Count);

        Voxel start = _rawPath[0];
        _waypoints.Add(new AStarWaypoint()
        {
            Position = start.WorldPosition,
            PathCost = 0,
            GlobalIndex = start.GlobalIndex
        });

        Vector3d lastDirection = Vector3d.Zero;
        for (int i = 1; i < _rawPath.Count - 1; i++)
        {
            Vector3d direction = (_rawPath[i + 1].WorldPosition - _rawPath[i].WorldPosition).Normalize();
            if (!lastDirection.FuzzyEqual(direction))
            {
                _waypoints.Add(new AStarWaypoint()
                {
                    Position = _rawPath[i].WorldPosition,
                    PathCost = GetMovementCost(_rawPath[i]),
                    GlobalIndex = _rawPath[i].GlobalIndex
                });
            }

            lastDirection = direction;
        }

        Voxel end = _rawPath.FromEnd(1);
        _waypoints.Add(new AStarWaypoint()
        {
            Position = end.WorldPosition,
            PathCost = GetMovementCost(end),
            GlobalIndex = end.GlobalIndex,
            IsGoal = true
        });
    }

    private void TrackRawPathChartOwners()
    {
        for (int i = 0; i < _rawPath.Count; i++)
            AddVoxelChartOwners(_rawPath[i]);
    }

    private void AddVoxelChartOwners(Voxel voxel)
    {
        if (voxel == null)
            return;

        if (voxel.TryGetPartition(out SolidChartPartition pathPartition))
            ChartOwnerUtility.AddOwners(_chartKeys, pathPartition.ChartOwners);

        if (voxel.TryGetPartition(out VolumeChartPartition volumePartition))
            ChartOwnerUtility.AddOwners(_chartKeys, volumePartition.ChartOwners);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int CalculatePathCost(Vector3d currentVoxel, int movementCost)
    {
        return movementCost + AStarSurveyor.CalculateHeuristic(
            currentVoxel,
            _request.EndNode.WorldPosition,
            _request.Heuristic);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool CanTraverseVoxel(Voxel voxel)
    {
        if (voxel == _request.StartNode)
            return true;

        if (voxel == _request.EndNode && _request.AllowUnwalkableEndpoints)
            return VolumeMediumRules.Matches(voxel, _request.Medium);

        return VolumeVoxelFinder.IsTraversable(
            voxel,
            _request.UnitSize,
            _request.Medium);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetMovementCost(Voxel voxel)
    {
        return _meta.TryGetValue(voxel, out VolumeVoxelMeta data)
            ? data.MovementCost
            : int.MaxValue;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetTraversalCostModifier(Voxel voxel)
    {
        return voxel != null && voxel.TryGetPartition(out VolumeChartPartition volumePartition)
            ? volumePartition.PathCostModifier
            : 0;
    }
}

using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Trailblazer.Navigation;

namespace Trailblazer.Pathing;

internal struct AerialVoxelMeta
{
    public int MovementCost;

    public GlobalVoxelIndex? NextTrailIndex;
}

/// <summary>
/// Executes chart-optional 3D A* pathfinding for aerial travel using raw voxel connectivity.
/// </summary>
public sealed class AerialSurveyor
{
    private static readonly Lazy<AerialSurveyor> _instance =
        new(() => new AerialSurveyor(), LazyThreadSafetyMode.ExecutionAndPublication);

    public static AerialSurveyor Shared => _instance.Value;

    private readonly AerialPathHeap _heap = new();

    private readonly SwiftDictionary<Voxel, AerialVoxelMeta> _meta = new();

    private readonly SwiftList<Voxel> _rawPath = new();

    private readonly SwiftList<AStarWaypoint> _waypoints = new();

    private AerialPathRequest _request;

    public AerialSurveyResult FindPath(AerialPathRequest request)
    {
        lock (SurveyorLock.GlobalLock)
        {
            if (request == null || request.HasZeroDisplacement || !request.HasValidEndpoints)
                return AerialSurveyResult.Empty;

            _request = request;

            _heap.FastClear();
            _meta.Clear();
            _rawPath.FastClear();
            _waypoints.FastClear();

            _meta[_request.StartNode] = new AerialVoxelMeta();
            _heap.Add(_request.StartNode, 0);

            if (!TracePath())
                return AerialSurveyResult.Empty;

            BuildRawPath();
            BuildWaypoints();

            return _waypoints.Count > 0
                ? AerialSurveyResult.Create(_waypoints.ToArray(), request.RequestCacheKey)
                : AerialSurveyResult.Empty;
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
        if (!_meta.TryGetValue(current, out AerialVoxelMeta data))
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
        if (neighbor == _request.EndNode)
        {
            SetVoxelData(neighbor, current.GlobalIndex, movementCost);
            return true;
        }

        int pathCost = CalculatePathCost(neighbor.WorldPosition, movementCost);
        if (!_heap.Contains(neighbor))
        {
            SetVoxelData(neighbor, current.GlobalIndex, movementCost);
            _heap.Add(neighbor, pathCost);
        }
        else if (_meta.TryGetValue(neighbor, out AerialVoxelMeta neighborData)
            && neighborData.MovementCost > movementCost)
        {
            SetVoxelData(neighbor, current.GlobalIndex, movementCost);
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
        _meta[voxel] = new AerialVoxelMeta()
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

            if (!_meta.TryGetValue(current, out AerialVoxelMeta data)
                || !data.NextTrailIndex.HasValue)
            {
                break;
            }

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

        if (voxel == _request.EndNode && _request.AllowUnwalkable)
            return true;

        return AerialVoxelFinder.IsTraversable(voxel, _request.UnitSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int GetMovementCost(Voxel voxel)
    {
        return _meta.TryGetValue(voxel, out AerialVoxelMeta data)
            ? data.MovementCost
            : int.MaxValue;
    }
}

using System;
using FixedMathSharp;
using GridForge;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>
/// Caches conservative solid-partition connectivity snapshots for fast unreachable-route rejection.
/// </summary>
internal static class SolidPartitionReachability
{
    private static readonly object _lock = new();

    private static readonly SwiftDictionary<ReachabilitySnapshotKey, int> _snapshotIdsByKey = new();

    private static readonly SwiftDictionary<int, int> _snapshotVersionsById = new();

    private static int _nextSnapshotId;

    private static int _version;

    /// <summary>
    /// Clears cached connectivity snapshots after live solid topology changes.
    /// </summary>
    internal static void Invalidate()
    {
        lock (_lock)
        {
            unchecked { _version++; }
            _snapshotIdsByKey.Clear();
            _snapshotVersionsById.Clear();
            _nextSnapshotId = 0;
        }
    }

    /// <summary>
    /// Returns true only when the current solid-partition graph proves the request cannot reach its destination.
    /// </summary>
    internal static bool IsProvablyUnreachable(AStarPathRequest request)
    {
        if (request == null
            || request.AllowTraversalTransitions
            || request.AllowUnwalkableEndpoints
            || request.StartNode == null
            || request.EndNode == null
            || !request.StartNode.TryGetPartition(out SolidChartPartition? startPartition)
            || !request.EndNode.TryGetPartition(out SolidChartPartition? endPartition)
            || startPartition == null
            || endPartition == null
            || ReferenceEquals(startPartition, endPartition)
            || startPartition.Neighbors == null
            || endPartition.Neighbors == null)
        {
            return false;
        }

        ReachabilitySnapshotKey key = new(request.UnitSize, request.MaxClimbHeight);
        lock (_lock)
        {
            int snapshotId = EnsureSnapshot(key);
            return IsProvablyUnreachable(
                startPartition,
                endPartition,
                snapshotId,
                _version,
                request.MaxClimbHeight);
        }
    }

    private static bool IsProvablyUnreachable(
        SolidChartPartition startPartition,
        SolidChartPartition endPartition,
        int snapshotId,
        int version,
        Fixed64 maxClimbHeight)
    {
        if (!endPartition.TryGetReachabilityComponent(snapshotId, version, out int endComponent))
            return false;

        if (endComponent == 0)
            return true;

        if (!startPartition.TryGetReachabilityComponent(snapshotId, version, out int startComponent))
            return false;

        if (startComponent > 0)
            return startComponent != endComponent;

        // A* can expand from an oversized/low-clearance start voxel, but only into passable
        // neighboring partitions. If none of those neighbors are in the end component, no route exists.
        return !HasReachableNeighborInComponent(startPartition, endComponent, snapshotId, version, maxClimbHeight);
    }

    private static int EnsureSnapshot(ReachabilitySnapshotKey key)
    {
        if (!_snapshotIdsByKey.TryGetValue(key, out int snapshotId))
        {
            snapshotId = ++_nextSnapshotId;
            _snapshotIdsByKey[key] = snapshotId;
        }

        if (_snapshotVersionsById.TryGetValue(snapshotId, out int snapshotVersion)
            && snapshotVersion == _version)
        {
            return snapshotId;
        }

        BuildSnapshot(snapshotId, key.UnitSize, key.MaxClimbHeight, _version);
        _snapshotVersionsById[snapshotId] = _version;
        return snapshotId;
    }

    private static void BuildSnapshot(
        int snapshotId,
        Fixed64 unitSize,
        Fixed64 maxClimbHeight,
        int version)
    {
        SwiftDictionary<WorldVoxelIndex, SolidChartPartition> passablePartitions = new();
        SwiftList<SolidChartPartition> componentRoots = new();

        foreach (NavigationChart chart in PathManager.AllCharts)
        {
            if (chart == null || !chart.IsInitialized)
                continue;

            foreach ((Vector3d position, NavigationChartCell cell) in chart.GetAuthoredCells())
            {
                if (!cell.HasSolid
                    || !TrailblazerWorldManager.TryGetVoxel(position, out Voxel? voxel)
                    || voxel == null
                    || !voxel.TryGetPartition(out SolidChartPartition? partition)
                    || partition == null
                    || partition.Neighbors == null)
                {
                    continue;
                }

                partition.SetReachabilityComponent(snapshotId, version, 0);
                if (partition.IsImpassable(unitSize) || passablePartitions.ContainsKey(partition.WorldIndex))
                    continue;

                passablePartitions[partition.WorldIndex] = partition;
                componentRoots.Add(partition);
            }
        }

        AssignComponents(snapshotId, version, passablePartitions, componentRoots, maxClimbHeight);
    }

    private static void AssignComponents(
        int snapshotId,
        int version,
        SwiftDictionary<WorldVoxelIndex, SolidChartPartition> passablePartitions,
        SwiftList<SolidChartPartition> componentRoots,
        Fixed64 maxClimbHeight)
    {
        SwiftQueue<SolidChartPartition> queue = new();
        int componentId = 0;

        for (int i = 0; i < componentRoots.Count; i++)
        {
            SolidChartPartition root = componentRoots[i];
            if (!root.TryGetReachabilityComponent(snapshotId, version, out int existingComponent)
                || existingComponent != 0)
            {
                continue;
            }

            componentId++;
            root.SetReachabilityComponent(snapshotId, version, componentId);
            queue.Enqueue(root);

            while (queue.Count > 0)
            {
                SolidChartPartition current = queue.Dequeue();
                SolidChartPartition?[]? neighbors = current.Neighbors;
                if (neighbors == null)
                    continue;

                for (int neighborIndex = 0; neighborIndex < neighbors.Length; neighborIndex++)
                {
                    SolidChartPartition? neighbor = neighbors[neighborIndex];
                    if (neighbor == null
                        || !passablePartitions.ContainsKey(neighbor.WorldIndex)
                        || !neighbor.TryGetReachabilityComponent(snapshotId, version, out int neighborComponent)
                        || neighborComponent != 0
                        || !CanTraverse(current, neighbor, (SpatialDirection)neighborIndex, passablePartitions, maxClimbHeight))
                    {
                        continue;
                    }

                    neighbor.SetReachabilityComponent(snapshotId, version, componentId);
                    queue.Enqueue(neighbor);
                }
            }
        }
    }

    private static bool HasReachableNeighborInComponent(
        SolidChartPartition start,
        int targetComponent,
        int snapshotId,
        int version,
        Fixed64 maxClimbHeight)
    {
        SolidChartPartition?[]? neighbors = start.Neighbors;
        if (neighbors == null)
            return false;

        for (int neighborIndex = 0; neighborIndex < neighbors.Length; neighborIndex++)
        {
            SolidChartPartition? neighbor = neighbors[neighborIndex];
            if (neighbor == null
                || !neighbor.TryGetReachabilityComponent(snapshotId, version, out int neighborComponent)
                || neighborComponent != targetComponent
                || !CanTraverseFromMarkedStart(start, neighbor, (SpatialDirection)neighborIndex, snapshotId, version, maxClimbHeight))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    private static bool CanTraverse(
        SolidChartPartition current,
        SolidChartPartition neighbor,
        SpatialDirection direction,
        SwiftDictionary<WorldVoxelIndex, SolidChartPartition> passablePartitions,
        Fixed64 maxClimbHeight)
    {
        Fixed64 heightDifference = (current.VoxelPosition.y - neighbor.VoxelPosition.y).Abs();
        if (heightDifference > maxClimbHeight)
            return false;

        if (ContainsDirection(SpatialAwareness.PerpendicularDirections, direction))
            return true;

        return ContainsDirection(SpatialAwareness.DiagonalDirections, direction)
            && HasValidDiagonalLegs(current, direction, passablePartitions);
    }

    private static bool CanTraverseFromMarkedStart(
        SolidChartPartition current,
        SolidChartPartition neighbor,
        SpatialDirection direction,
        int snapshotId,
        int version,
        Fixed64 maxClimbHeight)
    {
        Fixed64 heightDifference = (current.VoxelPosition.y - neighbor.VoxelPosition.y).Abs();
        if (heightDifference > maxClimbHeight)
            return false;

        if (ContainsDirection(SpatialAwareness.PerpendicularDirections, direction))
            return true;

        return ContainsDirection(SpatialAwareness.DiagonalDirections, direction)
            && HasValidDiagonalLegsFromMarkedStart(current, direction, snapshotId, version);
    }

    private static bool HasValidDiagonalLegs(
        SolidChartPartition current,
        SpatialDirection diagonal,
        SwiftDictionary<WorldVoxelIndex, SolidChartPartition> passablePartitions)
    {
        (int dx, int dy, int dz) = SpatialAwareness.DirectionOffsets[(int)diagonal];

        if (dx != 0 && !IsLegClear(current, dx > 0 ? SpatialDirection.North : SpatialDirection.West, passablePartitions))
            return false;

        if (dy != 0 && !IsLegClear(current, dy > 0 ? SpatialDirection.Above : SpatialDirection.Below, passablePartitions))
            return false;

        if (dz != 0 && !IsLegClear(current, dz > 0 ? SpatialDirection.East : SpatialDirection.South, passablePartitions))
            return false;

        return true;
    }

    private static bool HasValidDiagonalLegsFromMarkedStart(
        SolidChartPartition current,
        SpatialDirection diagonal,
        int snapshotId,
        int version)
    {
        (int dx, int dy, int dz) = SpatialAwareness.DirectionOffsets[(int)diagonal];

        if (dx != 0 && !IsMarkedLegClear(current, dx > 0 ? SpatialDirection.North : SpatialDirection.West, snapshotId, version))
            return false;

        if (dy != 0 && !IsMarkedLegClear(current, dy > 0 ? SpatialDirection.Above : SpatialDirection.Below, snapshotId, version))
            return false;

        if (dz != 0 && !IsMarkedLegClear(current, dz > 0 ? SpatialDirection.East : SpatialDirection.South, snapshotId, version))
            return false;

        return true;
    }

    private static bool IsLegClear(
        SolidChartPartition current,
        SpatialDirection legDirection,
        SwiftDictionary<WorldVoxelIndex, SolidChartPartition> passablePartitions)
    {
        SolidChartPartition? leg = current.Neighbors?[(int)legDirection];
        return leg != null && passablePartitions.ContainsKey(leg.WorldIndex);
    }

    private static bool IsMarkedLegClear(
        SolidChartPartition current,
        SpatialDirection legDirection,
        int snapshotId,
        int version)
    {
        SolidChartPartition? leg = current.Neighbors?[(int)legDirection];
        return leg != null
            && leg.TryGetReachabilityComponent(snapshotId, version, out int componentId)
            && componentId > 0;
    }

    private static bool ContainsDirection(SpatialDirection[] directions, SpatialDirection direction)
    {
        for (int i = 0; i < directions.Length; i++)
        {
            if (directions[i] == direction)
                return true;
        }

        return false;
    }

    private readonly struct ReachabilitySnapshotKey : IEquatable<ReachabilitySnapshotKey>
    {
        internal ReachabilitySnapshotKey(Fixed64 unitSize, Fixed64 maxClimbHeight)
        {
            UnitSize = unitSize;
            MaxClimbHeight = maxClimbHeight;
        }

        internal Fixed64 UnitSize { get; }

        internal Fixed64 MaxClimbHeight { get; }

        public bool Equals(ReachabilitySnapshotKey other)
        {
            return UnitSize == other.UnitSize && MaxClimbHeight == other.MaxClimbHeight;
        }

        public override bool Equals(object? obj)
        {
            return obj is ReachabilitySnapshotKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + UnitSize.GetHashCode();
                hash = (hash * 31) + MaxClimbHeight.GetHashCode();
                return hash;
            }
        }
    }
}

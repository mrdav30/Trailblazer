using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>
/// Default-context facade for conservative solid-partition connectivity snapshots used for fast unreachable-route rejection.
/// </summary>
internal static class SolidPartitionReachability
{
    private static SolidPartitionReachabilityState State => PathManager.ActiveState.ReachabilityState;

    private static object _lock => State.Lock;

    private static SwiftDictionary<WorldVoxelIndex, SolidChartPartition> _passablePartitions =>
        State.PassablePartitions;

    private static SwiftList<SolidChartPartition> _componentRoots => State.ComponentRoots;

    private static SwiftQueue<SolidChartPartition> _componentQueue => State.ComponentQueue;

    private static ReachabilitySnapshotKey _activeSnapshotKey
    {
        get => State.ActiveSnapshotKey;
        set => State.ActiveSnapshotKey = value;
    }

    private static bool _hasActiveSnapshot
    {
        get => State.HasActiveSnapshot;
        set => State.HasActiveSnapshot = value;
    }

    private static int _activeSnapshotId
    {
        get => State.ActiveSnapshotId;
        set => State.ActiveSnapshotId = value;
    }

    private static int _activeSnapshotVersion
    {
        get => State.ActiveSnapshotVersion;
        set => State.ActiveSnapshotVersion = value;
    }

    private static long _snapshotBuildCount
    {
        get => State.SnapshotBuildCount;
        set => State.SnapshotBuildCount = value;
    }

    private static int _version
    {
        get => State.Version;
        set => State.Version = value;
    }

    /// <summary>
    /// Clears cached connectivity snapshots after live solid topology changes.
    /// </summary>
    internal static void Invalidate()
    {
        lock (_lock)
        {
            unchecked { _version++; }
            _hasActiveSnapshot = false;
            _activeSnapshotVersion = -1;
        }
    }

    /// <summary>
    /// Captures reachability snapshot state for benchmarks and regression tests.
    /// </summary>
    internal static SolidPartitionReachabilityStats CaptureStats()
    {
        lock (_lock)
        {
            return new SolidPartitionReachabilityStats(
                _hasActiveSnapshot ? 1 : 0,
                _version,
                _snapshotBuildCount,
                _passablePartitions.Capacity,
                _componentRoots.Capacity,
                _componentQueue.Capacity);
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
        if (!_hasActiveSnapshot || !_activeSnapshotKey.Equals(key))
        {
            _activeSnapshotKey = key;
            _activeSnapshotId = GetNextSnapshotId(_activeSnapshotId);
            _activeSnapshotVersion = -1;
            _hasActiveSnapshot = true;
        }

        if (_activeSnapshotVersion == _version)
            return _activeSnapshotId;

        BuildSnapshot(_activeSnapshotId, key.UnitSize, key.MaxClimbHeight, _version);
        _activeSnapshotVersion = _version;
        _snapshotBuildCount++;
        return _activeSnapshotId;
    }

    private static int GetNextSnapshotId(int current)
    {
        unchecked
        {
            int next = current + 1;
            return next == 0 ? 1 : next;
        }
    }

    private static void BuildSnapshot(
        int snapshotId,
        Fixed64 unitSize,
        Fixed64 maxClimbHeight,
        int version)
    {
        // Snapshot construction runs under the reachability lock, so these scratch containers
        // can be reused without exposing partition references after the build completes.
        _passablePartitions.Clear();
        _componentRoots.Clear();
        _componentQueue.Clear();

        try
        {
            GridWorld world = PathManager.ActiveState.World;
            foreach (NavigationChart chart in PathManager.AllCharts)
            {
                if (chart == null || !PathManager.IsChartInitialized(chart.Name))
                    continue;

                foreach ((Vector3d position, NavigationChartCell cell) in chart.GetAuthoredCells())
                {
                    if (!cell.HasSolid
                        || !world.TryGetVoxel(position, out Voxel? voxel)
                        || voxel == null
                        || !voxel.TryGetPartition(out SolidChartPartition? partition)
                        || partition == null
                        || partition.Neighbors == null)
                    {
                        continue;
                    }

                    partition.SetReachabilityComponent(snapshotId, version, 0);
                    if (partition.IsImpassable(unitSize) || _passablePartitions.ContainsKey(partition.WorldIndex))
                        continue;

                    _passablePartitions[partition.WorldIndex] = partition;
                    _componentRoots.Add(partition);
                }
            }

            AssignComponents(
                snapshotId,
                version,
                _passablePartitions,
                _componentRoots,
                _componentQueue,
                maxClimbHeight);
        }
        finally
        {
            _componentQueue.Clear();
            _componentRoots.Clear();
            _passablePartitions.Clear();
        }
    }

    private static void AssignComponents(
        int snapshotId,
        int version,
        SwiftDictionary<WorldVoxelIndex, SolidChartPartition> passablePartitions,
        SwiftList<SolidChartPartition> componentRoots,
        SwiftQueue<SolidChartPartition> queue,
        Fixed64 maxClimbHeight)
    {
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

        if (dx != 0 && !IsLegClear(current, DiagonalTraversalLegs.ForXOffset(dx), passablePartitions))
            return false;

        if (dy != 0 && !IsLegClear(current, DiagonalTraversalLegs.ForYOffset(dy), passablePartitions))
            return false;

        if (dz != 0 && !IsLegClear(current, DiagonalTraversalLegs.ForZOffset(dz), passablePartitions))
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

        if (dx != 0 && !IsMarkedLegClear(current, DiagonalTraversalLegs.ForXOffset(dx), snapshotId, version))
            return false;

        if (dy != 0 && !IsMarkedLegClear(current, DiagonalTraversalLegs.ForYOffset(dy), snapshotId, version))
            return false;

        if (dz != 0 && !IsMarkedLegClear(current, DiagonalTraversalLegs.ForZOffset(dz), snapshotId, version))
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

    /// <summary>
    /// Snapshot counters used by benchmarks and tests to verify reachability cache policy.
    /// </summary>
    internal readonly struct SolidPartitionReachabilityStats
    {
        internal SolidPartitionReachabilityStats(
            int activeSnapshotCount,
            int version,
            long snapshotBuildCount,
            int passablePartitionCapacity,
            int componentRootCapacity,
            int componentQueueCapacity)
        {
            ActiveSnapshotCount = activeSnapshotCount;
            Version = version;
            SnapshotBuildCount = snapshotBuildCount;
            PassablePartitionCapacity = passablePartitionCapacity;
            ComponentRootCapacity = componentRootCapacity;
            ComponentQueueCapacity = componentQueueCapacity;
        }

        /// <summary>
        /// Gets the number of snapshot keys currently retained by the reachability cache.
        /// </summary>
        internal int ActiveSnapshotCount { get; }

        /// <summary>
        /// Gets the current topology version tracked by the reachability cache.
        /// </summary>
        internal int Version { get; }

        /// <summary>
        /// Gets the number of connectivity snapshots built since process start.
        /// </summary>
        internal long SnapshotBuildCount { get; }

        /// <summary>
        /// Gets the retained capacity of the passable-partition scratch map.
        /// </summary>
        internal int PassablePartitionCapacity { get; }

        /// <summary>
        /// Gets the retained capacity of the component-root scratch list.
        /// </summary>
        internal int ComponentRootCapacity { get; }

        /// <summary>
        /// Gets the retained capacity of the component queue.
        /// </summary>
        internal int ComponentQueueCapacity { get; }
    }

}

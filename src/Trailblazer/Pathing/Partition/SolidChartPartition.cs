//=======================================================================
// SolidChartPartition.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;

namespace Trailblazer.Pathing;

/// <summary>
/// Represents a partition attached to a Voxel that provides additional data used during pathfinding,
/// such as clearance information, movement cost, and neighbor traversal helpers.
/// </summary>
public class SolidChartPartition : IVoxelPartition
{
    #region Constants

    /// <summary>
    /// Maximum clearance degree allowed for valid traversal.
    /// </summary>
    public static readonly byte DefaultDegreeCap = 8;

    private static readonly Lazy<SwiftQueuePool<(SolidChartPartition v, byte dist)>> _clearanceQueuePool =
        new(() => new SwiftQueuePool<(SolidChartPartition v, byte dist)>());

    internal static SwiftQueuePool<(SolidChartPartition v, byte dist)> ClearanceQueuePool => _clearanceQueuePool.Value;

    #endregion

    /// <summary>
    /// The world-scoped coordinate of the voxel this partition is attached to.
    /// </summary>
    public WorldVoxelIndex WorldIndex { get; private set; }

    internal PathingWorldState? OwnerState { get; private set; }

    /// <summary>
    /// Gets the voxel associated with this partition.
    /// </summary>
    public Voxel Voxel
    {
        get
        {
            if (TryGetGridAndVoxel(WorldIndex, out _, out Voxel? voxel)
                && voxel != null)
                return voxel;
            throw new InvalidOperationException($"Partition at {WorldIndex} is not attached to a valid voxel!");
        }
    }

    /// <summary>
    /// The world-space position of the voxel.
    /// </summary>
    public Vector3d VoxelPosition { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the current tile can be traversed.
    /// </summary>
    public bool IsWalkable { get; private set; }

    /// <summary>
    /// Indicates whether the voxel has been partitioned and is in use.
    /// </summary>
    public bool IsPartitioned { get; set; }

    /// <summary>
    /// A cost bias for this partition. Positive values make the partition less desirable.
    /// The public setter preserves caller-controlled adjustments,
    /// while chart-authored modifiers are aggregated separately.
    /// </summary>
    public int PathCostModifier
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _manualPathCostModifier + _chartPathCostModifier;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _manualPathCostModifier = value;
    }

    private int _manualPathCostModifier;

    private int _chartPathCostModifier;

    /// <summary>
    /// Gets the neighboring partitions adjacent to this partition.
    /// </summary>
    /// <remarks>
    /// Each element in the array represents a neighboring partition in a specific direction or position.
    /// The array may contain null values if a neighbor does not exist in that position.
    /// </remarks>
    public SolidChartPartition?[]? Neighbors { get; private set; }

    #region Clearance Properties

    /// <summary>
    /// The number of traversable connections until the nearest unwalkable voxel.
    /// </summary>
    private byte _clearanceRadiusInVoxels;

    /// <summary>
    /// Indicates whether the clearance degree has been computed and is valid.
    /// </summary>
    private bool _isClearanceValid;

    #endregion

    #region Chart Properties

    /// <summary>
    /// Maps that currently include this partition as part of their traversable space.
    /// </summary>
    public SwiftHashSet<string>? ChartOwners { get; private set; }

    /// <summary>
    /// The chart whose authored cell currently wins overlap resolution for this voxel.
    /// </summary>
    public string? EffectiveChartOwner { get; private set; }

    /// <summary>
    /// The authored chart flags from the winning effective cell currently applied to this live partition.
    /// </summary>
    public NavigationChartCellFlags ChartFlags { get; private set; }

    /// <summary>
    /// Returns true if any map currently references this partition.
    /// </summary>
    public bool HasAnyOwners => ChartOwners?.Count > 0;

    #endregion

    /// <summary>
    /// Sets the parent index for the current voxel in the world.
    /// </summary>
    /// <param name="parentIndex">The index to assign as the parent of the current voxel.</param>
    public void SetParentIndex(WorldVoxelIndex parentIndex) => WorldIndex = parentIndex;

    internal void SetOwner(PathingWorldState ownerState) => OwnerState = ownerState;

    /// <summary>
    /// Attaches a partition to a specified <see cref="Voxel"/>, updating its state and invoking initialization logic.
    /// </summary>
    /// <param name="voxel">The target voxel where the partition will be added.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnAddToVoxel(Voxel voxel)
    {
        voxel.OnObstacleAdded += HandleChange;
        voxel.OnObstacleRemoved += HandleChange;

        WorldIndex = voxel.WorldIndex;
        VoxelPosition = voxel.WorldPosition;

        IsWalkable = !voxel.IsBlocked;

        _clearanceRadiusInVoxels = DefaultDegreeCap;

        IsPartitioned = true;
    }

    /// <summary>
    /// Detaches a partition from a specified <see cref="Voxel"/>, resetting its state and invoking cleanup logic.
    /// </summary>
    /// <param name="voxel">The target voxel from which the partition will be removed.</param>
    /// <remarks>
    /// This will call <see cref="Reset"/> as an action on release
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnRemoveFromVoxel(Voxel voxel)
    {
        voxel.OnObstacleAdded -= HandleChange;
        voxel.OnObstacleRemoved -= HandleChange;

        PathingWorldState? ownerState = OwnerState;
        ownerState?.PartitionPool.Release(this);
    }

    /// <summary>
    /// Resets this partition's internal state, preparing it for reuse or reattachment.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Reset()
    {
        WorldIndex = default;
        OwnerState = null;

        _isClearanceValid = false;

        IsWalkable = false;

        PathCostModifier = 0;
        _chartPathCostModifier = 0;
        ChartFlags = NavigationChartCellFlags.None;

        Neighbors = null;

        _clearanceRadiusInVoxels = DefaultDegreeCap;

        ChartOwners?.Clear();
        EffectiveChartOwner = null;

        IsPartitioned = false;
    }

    /// <summary>
    /// Handles any obstacle changes on the associated voxel and invalidates clearance as needed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleChange(ObstacleEventInfo eventInfo)
    {
        // regardless of change type, we need to update clearance

        IsWalkable = eventInfo.VoxelIndex != default && eventInfo.ObstacleCount == 0;
        _clearanceRadiusInVoxels = DefaultDegreeCap;
        _isClearanceValid = false;
    }

    /// <summary>
    /// Populates the Neighbors array with references to adjacent SolidChartPartition instances based on the current WorldIndex.
    /// </summary>
    /// <remarks>
    /// If the grid or voxel corresponding to WorldIndex cannot be found, Neighbors is set to null.
    /// Each entry in the Neighbors array corresponds to a spatial direction;
    /// entries remain null if a neighbor is blocked or missing.
    /// </remarks>
    public void BindNeighbors()
    {
        Neighbors = new SolidChartPartition?[26];

        if (!TryGetGridAndVoxel(WorldIndex, out VoxelGrid? grid, out Voxel? voxel))
        {
            TrailblazerLogger.Channel.Warn($"Failed to find grid or voxel for WorldIndex {WorldIndex}. Neighbors will be null.");
            Neighbors = null;
            return;
        }

        VoxelGrid ownerGrid = grid!;
        SwiftList<Voxel> contactNeighbors = SwiftListPool<Voxel>.Shared.Rent();
        try
        {
            voxel!.GetNeighborsInto(
                ownerGrid,
                contactNeighbors,
                VoxelNeighborScope.SourceGrid | VoxelNeighborScope.SameTopologyGrids);

            for (int i = 0; i < contactNeighbors.Count; i++)
            {
                Voxel neighborVoxel = contactNeighbors[i];
                if (!neighborVoxel.TryGetPartition(out SolidChartPartition? neighborPart))
                    continue;

                Vector3d offset = neighborVoxel.WorldPosition - VoxelPosition;
                RectangularDirection direction = RectangularDirectionUtility.GetDirectionFromOffset((
                    Fixed64.Sign(offset.X),
                    Fixed64.Sign(offset.Y),
                    Fixed64.Sign(offset.Z)));
                if (direction == RectangularDirection.None)
                    continue;

                int directionIndex = (int)direction;
                SolidChartPartition? existing = Neighbors[directionIndex];
                if (existing == null
                    || (neighborVoxel.WorldIndex.GridIndex == ownerGrid.GridIndex
                        && existing.WorldIndex.GridIndex != ownerGrid.GridIndex))
                {
                    Neighbors[directionIndex] = neighborPart;
                }
            }
        }
        finally
        {
            SwiftListPool<Voxel>.Shared.Release(contactNeighbors);
        }
    }

    /// <summary>
    /// Returns the cached or recalculated clearance value to nearby obstacles.
    /// </summary>
    public byte GetNeighborClearance()
    {
        CheckClearance();
        return _clearanceRadiusInVoxels;
    }

    /// <summary>
    /// If this unit is too fat to fit.
    /// </summary>
    internal bool IsImpassable(Fixed64 unitSize)
    {
        if (unitSize <= Fixed64.Zero)
            return false;

        PathingWorldState ownerState = RequireOwnerState();
        if (!ownerState.World.TryGetGrid(WorldIndex, out VoxelGrid? ownerGrid))
            return true;

        // Request admission already validated the world-wide cubic metric invariant. The expansion hot path
        // reads only this partition's exact-generation owner instead of rescanning every active grid.
        Fixed64 voxelSize = ownerGrid!.Configuration.TopologyMetrics.CellWidth;
        if (unitSize <= voxelSize)
            return !IsWalkable;

        // Only evaluates local radial clearance from current voxel.
        // Does not account for directional corner blocking
        CheckClearance();

        // How many voxels wide our agent is, in cell terms
        int required = (unitSize / voxelSize).CeilToInt();
        // If there aren't at least that many free voxels around, it can't go

        return required > _clearanceRadiusInVoxels;
    }

    /// <summary>
    /// Validates or recalculates the clearance degree from nearby voxels.
    /// </summary>
    private void CheckClearance()
    {
        if (Neighbors == null)
            throw new InvalidOperationException("Must call BindNeighbors() before clearance.");

        if (_isClearanceValid)
            return;

        _isClearanceValid = true;

        if (!TryGetClearanceOrigin(out Voxel? origin))
        {
            _clearanceRadiusInVoxels = origin != null && origin.IsBlocked ? (byte)0 : DefaultDegreeCap;
            return;
        }

        _clearanceRadiusInVoxels = ComputeClearanceRadius();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool TryGetClearanceOrigin([MaybeNullWhen(false)] out Voxel origin)
    {
        return TryGetGridAndVoxel(WorldIndex, out _, out origin)
            && IsWalkable;
    }

    private bool TryGetGridAndVoxel(
        WorldVoxelIndex voxelIndex,
        out VoxelGrid? grid,
        out Voxel? voxel)
    {
        return RequireOwnerState().World.TryGetGridAndVoxel(voxelIndex, out grid, out voxel);
    }

    private PathingWorldState RequireOwnerState() =>
        OwnerState ?? throw new InvalidOperationException("Solid chart partition requires an owning pathing context.");

    private byte ComputeClearanceRadius()
    {
        // BFS from this voxel until we hit any blocked-or-missing neighbor
        byte best = DefaultDegreeCap;
        SwiftQueue<(SolidChartPartition v, byte dist)> q = ClearanceQueuePool.Rent();
        SwiftHashSet<SolidChartPartition> visited = PathManager.PartitionSetPool.Rent();

        try
        {
            q.Enqueue((this, 0));
            visited.Add(this);

            // stop BFS either when queue empty or we’ve already found best=1
            while (q.Count > 0 && best > 1)
            {
                (SolidChartPartition part, byte dist) = q.Dequeue();
                ExploreClearanceNeighbors(part, dist, visited, q, ref best);
            }

            // clamp to cap so you never return > DefaultDegreeCap
            return Math.Min(best, DefaultDegreeCap);
        }
        finally
        {
            ClearanceQueuePool.Release(q);
            PathManager.PartitionSetPool.Release(visited);
        }
    }

    private static void ExploreClearanceNeighbors(
        SolidChartPartition part,
        byte dist,
        SwiftHashSet<SolidChartPartition> visited,
        SwiftQueue<(SolidChartPartition v, byte dist)> queue,
        ref byte best)
    {
        SolidChartPartition?[]? neighbors = part.Neighbors;
        if (neighbors == null)
            return;

        for (int i = 0; i < neighbors.Length; i++)
        {
            byte nextDist = (byte)(dist + 1);
            SolidChartPartition? neighbor = neighbors[i];

            if (IsClearanceBoundary(i, neighbor))
            {
                best = Math.Min(best, nextDist);
                continue;
            }

            if (neighbor != null && ShouldExpandClearanceSearch(nextDist, best, neighbor, visited))
                queue.Enqueue((neighbor, nextDist));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsClearanceBoundary(int neighborIndex, SolidChartPartition? neighbor)
    {
        if (neighbor != null && neighbor.IsWalkable)
            return false;

        // skip above, below, or any above/below diagonals
        return neighborIndex != 4 && neighborIndex != 5 && neighborIndex < 10;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool ShouldExpandClearanceSearch(
        byte nextDist,
        byte best,
        SolidChartPartition? neighbor,
        SwiftHashSet<SolidChartPartition> visited)
    {
        return neighbor != null
            && nextDist < best
            && nextDist < DefaultDegreeCap
            && visited.Add(neighbor);
    }

    #region NavigationChart Management

    /// <summary>
    /// Applies the resolved overlap state for this voxel to the active solid partition.
    /// </summary>
    internal void ApplyAuthoredState(
        ResolvedChartVoxelState? state,
        string? effectiveChartOwner,
        NavigationChartCell effectiveCell)
    {
        ChartOwners ??= new SwiftHashSet<string>();
        ChartOwners.Clear();
        state?.AddChartOwnersTo(ChartOwners);

        EffectiveChartOwner = effectiveChartOwner;
        _chartPathCostModifier = effectiveCell.PathCostModifier;
        ChartFlags = effectiveCell.Flags;
    }

    /// <summary>
    /// Returns true if the partition is claimed by the given map name.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool BelongsTo(string mapName) => ChartOwners?.Contains(mapName) == true;

    #endregion

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        unchecked
        {
            VoxelIndex voxelIndex = WorldIndex.VoxelIndex;
            int hash = 17;
            hash = (hash * 31) + WorldIndex.WorldSpawnToken.GetHashCode();
            hash = (hash * 31) + WorldIndex.GridIndex;
            hash = (hash * 31) + WorldIndex.GridSpawnToken.GetHashCode();
            hash = (hash * 31) + voxelIndex.x;
            hash = (hash * 31) + voxelIndex.y;
            hash = (hash * 31) + voxelIndex.z;
            return hash;
        }
    }
}

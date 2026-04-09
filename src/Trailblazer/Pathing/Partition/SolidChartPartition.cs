using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;
using System;
using System.Runtime.CompilerServices;

#if DEBUG
using System.Diagnostics;
#endif

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
    /// The global coordinate of the voxel this partition is attached to.
    /// </summary>
    public GlobalVoxelIndex GlobalIndex { get; private set; }

    public Voxel Voxel
    {
        get
        {
            if (GlobalGridManager.TryGetGridAndVoxel(GlobalIndex, out _, out var voxel))
                return voxel;
            throw new InvalidOperationException($"Partition at {GlobalIndex} is not attached to a valid voxel!");
        }
    }

    /// <summary>
    /// The world-space position of the voxel.
    /// </summary>
    public Vector3d VoxelPosition { get; private set; }

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

#nullable enable
    public SolidChartPartition?[]? Neighbors { get; private set; }
#nullable disable

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
    public SwiftHashSet<string> ChartOwners { get; private set; }

    /// <summary>
    /// The chart whose authored cell currently wins overlap resolution for this voxel.
    /// </summary>
    public string EffectiveChartOwner { get; private set; }

    /// <summary>
    /// The authored chart flags from the winning effective cell currently applied to this live partition.
    /// </summary>
    public NavigationChartCellFlags ChartFlags { get; private set; }

    /// <summary>
    /// Returns true if any map currently references this partition.
    /// </summary>
    public bool HasAnyOwners => ChartOwners?.Count > 0;

    #endregion

    public void SetParentIndex(GlobalVoxelIndex parentIndex) => GlobalIndex = parentIndex;

    /// <summary>
    /// Attaches a partition to a specified <see cref="Voxel"/>, updating its state and invoking initialization logic.
    /// </summary>
    /// <param name="voxel">The target voxel where the partition will be added.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnAddToVoxel(Voxel voxel)
    {
        voxel.OnObstacleAdded += HandleChange;
        voxel.OnObstacleRemoved += HandleChange;

        GlobalIndex = voxel.GlobalIndex;
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

        PathManager.PartitionPool.Release(this);
    }

    /// <summary>
    /// Resets this partition's internal state, preparing it for reuse or reattachment.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Reset()
    {
        GlobalIndex = default;

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

    public void BindNeighbors()
    {
#nullable enable
        Neighbors = new SolidChartPartition?[26];
#nullable disable

        GlobalGridManager.TryGetGridAndVoxel(GlobalIndex, out _, out var voxel);

        if (voxel == null)
        {
#if DEBUG
            Debug.WriteLine($"Partition at {GlobalIndex} is not attached to a voxel!");
#endif
            Neighbors = null;
            return;
        }

        // for each of the 26 SpatialDirection values (except None)
        foreach (SpatialDirection dir in SpatialAwareness.AllDirections)
        {
            // use Voxel’s cached neighbor lookup
            if (voxel.TryGetNeighborFromDirection(dir, out var neighborVoxel, useCache: true)
             && neighborVoxel.TryGetPartition(out SolidChartPartition neighborPart))
            {
                Neighbors[(int)dir] = neighborPart;
            }
            // else leave null = “blocked or missing”
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

        // Only evaluates local radial clearance from current voxel. 
        // Does not account for directional corner blocking
        CheckClearance();

        // How many voxels wide our agent is, in cell terms
        int required = (unitSize / GlobalGridManager.VoxelSize).CeilToInt();
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

        if (_isClearanceValid) return;
        _isClearanceValid = true;

        if (!GlobalGridManager.TryGetGridAndVoxel(GlobalIndex, out _, out Voxel origin)
         || !IsWalkable)
        {
            _clearanceRadiusInVoxels = origin?.IsBlocked == true ? (byte)0 : DefaultDegreeCap;
            return;
        }

        // BFS from this voxel until we hit any blocked-or-missing neighbor
        byte best = DefaultDegreeCap;
        SwiftQueue<(SolidChartPartition v, byte dist)> q = ClearanceQueuePool.Rent();
        SwiftHashSet<SolidChartPartition> visited = PathManager.PartitionSetPool.Rent();

        q.Enqueue((this, 0));
        visited.Add(this);

        // stop BFS either when queue empty or we’ve already found best=1
        while (q.Count > 0 && best > 1)
        {
            (SolidChartPartition part, byte dist) = q.Dequeue();

            // any neighbor that’s missing or blocked → candidate = dist+1
            for (int i = 0; i < part.Neighbors.Length; i++)
            {
                byte nextDist = (byte)(dist + 1);
                SolidChartPartition nPart = part.Neighbors[i];

                // 1) missing or blocked → candidate radius = nextDist
                if (nPart == null || !nPart.IsWalkable)
                {
                    // skip above, below, or any above/below diagonals
                    if (i == 4 || i == 5 || i >= 10)
                        continue;

                    best = Math.Min(best, nextDist);
                    continue;
                }

                // 2) otherwise, keep exploring *only* up to your cap
                if (nextDist < best
                 && nextDist < DefaultDegreeCap
                 && visited.Add(nPart))
                {
                    q.Enqueue((nPart, nextDist));
                }
            }
        }

        // clamp to cap so you never return > DefaultDegreeCap
        _clearanceRadiusInVoxels = Math.Min(best, DefaultDegreeCap);

        ClearanceQueuePool.Release(q);
        PathManager.PartitionSetPool.Release(visited);
    }

    #region NavigationChart Management

    /// <summary>
    /// Applies the resolved overlap state for this voxel to the active solid partition.
    /// </summary>
    internal void ApplyAuthoredState(
        ResolvedChartVoxelState state,
        string effectiveChartOwner,
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

    public override int GetHashCode() => Voxel.GetHashCode();
}

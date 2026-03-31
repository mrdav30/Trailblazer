using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// Represents authored raw-volume traversal data attached to a voxel.
/// </summary>
public sealed class VolumeChartPartition : IVoxelPartition
{
    private int _manualPathCostModifier;

    private int _chartPathCostModifier;

    private TraversalMedia _volumeKinds;

    /// <summary>
    /// The global coordinate of the voxel this partition is attached to.
    /// </summary>
    public GlobalVoxelIndex GlobalIndex { get; private set; }

    /// <summary>
    /// The world-space position of the authored voxel.
    /// </summary>
    public Vector3d VoxelPosition { get; private set; }

    /// <summary>
    /// Indicates whether the voxel itself is currently unblocked.
    /// </summary>
    public bool IsWalkable { get; private set; }

    /// <summary>
    /// Charts that currently contribute authored volume data to this voxel.
    /// </summary>
    public SwiftHashSet<string> ChartOwners { get; private set; }

    /// <summary>
    /// Returns true if any chart currently contributes authored volume data to this voxel.
    /// </summary>
    public bool HasAnyOwners => ChartOwners?.Count > 0;

    /// <summary>
    /// The chart whose authored cell currently wins overlap resolution for this voxel.
    /// </summary>
    public string EffectiveChartOwner { get; private set; }

    /// <summary>
    /// Additional authored or caller-controlled path cost for this volume voxel.
    /// </summary>
    public int PathCostModifier
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _manualPathCostModifier + _chartPathCostModifier;
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => _manualPathCostModifier = value;
    }

    public void SetParentIndex(GlobalVoxelIndex parentIndex) => GlobalIndex = parentIndex;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnAddToVoxel(Voxel voxel)
    {
        voxel.OnObstacleChange += HandleChange;
        GlobalIndex = voxel.GlobalIndex;
        VoxelPosition = voxel.WorldPosition;
        IsWalkable = !voxel.IsBlocked;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnRemoveFromVoxel(Voxel voxel)
    {
        voxel.OnObstacleChange -= HandleChange;
        PathManager.VolumeChartPartitionPool.Release(this);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleChange(GridChange changeType, Voxel voxel)
    {
        IsWalkable = voxel != null && !voxel.IsBlocked;
    }

    /// <summary>
    /// Returns true if this partition currently supports the requested raw volume traversal medium.
    /// </summary>
    public bool SupportsMedium(TraversalMedium medium)
    {
        return medium switch
        {
            TraversalMedium.Gas => (_volumeKinds & TraversalMedia.Gas) != 0,
            TraversalMedium.Liquid => (_volumeKinds & TraversalMedia.Liquid) != 0,
            _ => false
        };
    }

    /// <summary>
    /// Returns true if this partition is claimed by the given chart name.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool BelongsTo(string chartName) => ChartOwners?.Contains(chartName) == true;

    /// <summary>
    /// Returns true if the requested unit size cannot fit through this voxel.
    /// </summary>
    internal bool IsImpassable(Fixed64 unitSize)
    {
        return !VolumeVoxelFinder.HasClearance(Voxel, unitSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Reset()
    {
        GlobalIndex = default;
        VoxelPosition = default;
        IsWalkable = false;
        PathCostModifier = 0;
        _chartPathCostModifier = 0;
        _volumeKinds = TraversalMedia.None;
        ChartOwners?.Clear();
        EffectiveChartOwner = null;
    }

    private Voxel Voxel
    {
        get
        {
            if (GlobalGridManager.TryGetGridAndVoxel(GlobalIndex, out _, out Voxel voxel))
                return voxel;

            throw new InvalidOperationException($"Volume partition at {GlobalIndex} is not attached to a valid voxel.");
        }
    }

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
        _volumeKinds = effectiveCell.TraversalKinds & TraversalMedia.AnyVolume;
    }
}

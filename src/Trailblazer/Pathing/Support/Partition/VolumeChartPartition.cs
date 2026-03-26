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
    private readonly SwiftHashSet<string> _chartOwners = new();

    private readonly SwiftDictionary<string, NavigationChartCell> _chartCells = new(4, StringComparer.Ordinal);

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
    /// Returns true if any chart currently contributes authored volume data to this voxel.
    /// </summary>
    public bool HasAnyOwners => _chartOwners.Count > 0;

    /// <summary>
    /// Charts that currently contribute authored volume data to this voxel.
    /// </summary>
    public SwiftHashSet<string> ChartOwners => _chartOwners;

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
    public bool BelongsTo(string chartName) => _chartOwners.Contains(chartName);

    /// <summary>
    /// Registers authored volume ownership for this voxel.
    /// </summary>
    public void AddOwner(string chartName, NavigationChartCell cell)
    {
        if (!cell.HasVolume)
            return;

        _chartOwners.Add(chartName);
        _chartCells[chartName] = cell;
        RefreshChartMetadata();
    }

    /// <summary>
    /// Removes authored volume ownership for the given chart name.
    /// </summary>
    public void RemoveOwner(string chartName)
    {
        _chartOwners.Remove(chartName);
        _chartCells.Remove(chartName);
        RefreshChartMetadata();
    }

    /// <summary>
    /// Returns true if the requested unit size cannot fit through this voxel.
    /// </summary>
    internal bool IsImpassable(Fixed64 unitSize)
    {
        return !RawVoxelFinder.HasClearance(Voxel, unitSize);
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
        _chartOwners.Clear();
        _chartCells.Clear();
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

    private void RefreshChartMetadata()
    {
        _chartPathCostModifier = 0;
        _volumeKinds = TraversalMedia.None;

        foreach (NavigationChartCell cell in _chartCells.Values)
        {
            _chartPathCostModifier += cell.PathCostModifier;
            _volumeKinds |= cell.TraversalKinds & TraversalMedia.AnyVolume;
        }
    }
}

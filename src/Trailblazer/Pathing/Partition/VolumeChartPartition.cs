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
    /// The world-scoped coordinate of the voxel this partition is attached to.
    /// </summary>
    public WorldVoxelIndex WorldIndex { get; private set; }

    internal PathingWorldState? OwnerState { get; private set; }

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
    public SwiftHashSet<string>? ChartOwners { get; private set; }

    /// <summary>
    /// Returns true if any chart currently contributes authored volume data to this voxel.
    /// </summary>
    public bool HasAnyOwners => ChartOwners?.Count > 0;

    /// <summary>
    /// The chart whose authored cell currently wins overlap resolution for this voxel.
    /// </summary>
    public string? EffectiveChartOwner { get; private set; }

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

    /// <summary>
    /// Sets the parent index for this voxel in the world structure.
    /// </summary>
    /// <param name="parentIndex">The index representing the parent voxel to assign. Must be a valid WorldVoxelIndex.</param>
    public void SetParentIndex(WorldVoxelIndex parentIndex) => WorldIndex = parentIndex;

    internal void SetOwner(PathingWorldState ownerState) => OwnerState = ownerState;

    /// <summary>
    /// Initializes the obstacle's state based on the specified voxel and subscribes to voxel change events.
    /// </summary>
    /// <remarks>
    /// This method updates the obstacle's world index, position, and walkability status to match the provided voxel. 
    /// It also attaches event handlers to respond to changes in the voxel's obstacle state.
    /// </remarks>
    /// <param name="voxel">The voxel to which the obstacle is being added. Cannot be null.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnAddToVoxel(Voxel voxel)
    {
        voxel.OnObstacleAdded += HandleChange;
        voxel.OnObstacleRemoved += HandleChange;

        WorldIndex = voxel.WorldIndex;
        VoxelPosition = voxel.WorldPosition;
        IsWalkable = !voxel.IsBlocked;
    }

    /// <summary>
    /// Handles cleanup when this object is removed from the specified voxel, including detaching event handlers and releasing resources.
    /// </summary>
    /// <remarks>
    /// After calling this method, the object should not be used with the specified voxel unless re-added. 
    /// This method also releases the object back to the partition pool for reuse.
    /// </remarks>
    /// <param name="voxel">The voxel from which this object is being removed. Cannot be null.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void OnRemoveFromVoxel(Voxel voxel)
    {
        voxel.OnObstacleAdded -= HandleChange;
        voxel.OnObstacleRemoved -= HandleChange;

        PathingWorldState? ownerState = OwnerState;
        if (ownerState != null)
            ownerState.VolumeChartPartitionPool.Release(this);
        else
            PathManager.VolumeChartPartitionPool.Release(this);
    }

    /// <summary>
    /// Updates the walkability state based on the provided obstacle event information.
    /// </summary>
    /// <remarks>
    /// Call this method when obstacle state changes to ensure the walkability property reflects the current environment.
    /// </remarks>
    /// <param name="eventInfo">
    /// The event data containing voxel index and obstacle count information used to determine walkability. Cannot be null.
    /// </param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void HandleChange(ObstacleEventInfo eventInfo)
    {
        IsWalkable = eventInfo.VoxelIndex != default && eventInfo.ObstacleCount == 0;
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
        PathingWorldState ownerState = OwnerState
            ?? throw new InvalidOperationException("Volume chart partition requires an owning pathing context.");
        return !VolumeVoxelFinder.HasClearance(ownerState.Context, Voxel, unitSize);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Reset()
    {
        WorldIndex = default;
        OwnerState = null;
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
            PathingWorldState? ownerState = OwnerState;
            Voxel? voxel = null;
            bool found = ownerState != null
                && ownerState.World.TryGetGridAndVoxel(WorldIndex, out _, out voxel);

            if (found
                && voxel != null)
                return voxel;

            throw new InvalidOperationException($"Volume partition at {WorldIndex} is not attached to a valid voxel.");
        }
    }

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
        _volumeKinds = effectiveCell.TraversalKinds & TraversalMedia.AnyVolume;
    }
}

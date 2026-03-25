using GridForge.Grids;
using GridForge.Spatial;
using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Trailblazer.Pathing;

/// <summary>
/// Holds authored and host-configured membership rules for raw volume traversal modes.
/// </summary>
/// <remarks>
/// Trailblazer can derive raw-volume membership from authored <see cref="VolumePartition"/> data
/// created during chart initialization. Hosts can also install supplemental water rules for
/// engine-specific partitioning or bespoke world logic.
/// </remarks>
public static class VolumeTraversalRules
{
    public delegate bool VoxelRule(Voxel voxel);

    private static VoxelRule _waterVoxelRule;

    private static int _registryVersion;

    /// <summary>
    /// Indicates whether a water-volume rule is currently configured.
    /// </summary>
    public static bool HasWaterVoxelRule => _waterVoxelRule != null;

    internal static int RegistryVersion => _registryVersion;

    /// <summary>
    /// Uses a host-defined voxel partition type to identify water voxels.
    /// </summary>
    public static void SetWaterVoxelPartition<TPartition>()
        where TPartition : class, IVoxelPartition
    {
        _waterVoxelRule = static voxel =>
            voxel != null
            && voxel.HasPartition<TPartition>();
        Interlocked.Increment(ref _registryVersion);
    }

    /// <summary>
    /// Sets a host-defined water voxel rule.
    /// </summary>
    public static void SetWaterVoxelRule(VoxelRule rule)
    {
        _waterVoxelRule = rule;
        Interlocked.Increment(ref _registryVersion);
    }

    /// <summary>
    /// Clears any previously configured water voxel rule.
    /// </summary>
    public static void ClearWaterVoxelRule()
    {
        _waterVoxelRule = null;
        Interlocked.Increment(ref _registryVersion);
    }

    internal static void Reset() => ClearWaterVoxelRule();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsConfigured(VolumeTraversalMode traversalMode)
    {
        return traversalMode switch
        {
            VolumeTraversalMode.Open => true,
            VolumeTraversalMode.Water => _waterVoxelRule != null || PathManager.HasAuthoredVolumeTraversal(VolumeTraversalMode.Water),
            _ => false
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool Matches(Voxel voxel, VolumeTraversalMode traversalMode)
    {
        if (voxel == null)
            return false;

        // TODO: add _openVoxelRule for host-defined open traversal rules, if needed
        bool hostWaterMatch = _waterVoxelRule?.Invoke(voxel) == true;
        bool hasAuthoredVolumePartition = voxel.TryGetPartition(out VolumePartition volumePartition);
        bool authoredOpenMatch = hasAuthoredVolumePartition
            && volumePartition.SupportsTraversal(VolumeTraversalMode.Open);
        bool authoredWaterMatch = hasAuthoredVolumePartition
            && volumePartition.SupportsTraversal(VolumeTraversalMode.Water);

        return traversalMode switch
        {
            VolumeTraversalMode.Open => hasAuthoredVolumePartition
                ? authoredOpenMatch
                : !PathManager.HasAuthoredVolumeTraversal(VolumeTraversalMode.Open),
            VolumeTraversalMode.Water => authoredWaterMatch || hostWaterMatch,
            _ => false
        };
    }
}

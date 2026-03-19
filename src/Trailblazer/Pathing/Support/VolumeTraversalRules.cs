using GridForge.Grids;
using GridForge.Spatial;
using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// Holds host-configured voxel rules for raw volume traversal modes.
/// </summary>
/// <remarks>
/// Trailblazer does not assign water or air voxels on its own. Hosts should configure
/// the relevant rules before requesting non-open volume traversal.
/// </remarks>
public static class VolumeTraversalRules
{
    public delegate bool VoxelRule(Voxel voxel);

    private static VoxelRule _waterVoxelRule;

    /// <summary>
    /// Indicates whether a water-volume rule is currently configured.
    /// </summary>
    public static bool HasWaterVoxelRule => _waterVoxelRule != null;

    /// <summary>
    /// Uses a host-defined voxel partition type to identify water voxels.
    /// </summary>
    public static void SetWaterVoxelPartition<TPartition>()
        where TPartition : class, IVoxelPartition
    {
        _waterVoxelRule = static voxel =>
            voxel != null
            && voxel.HasPartition<TPartition>();
    }

    /// <summary>
    /// Sets a host-defined water voxel rule.
    /// </summary>
    public static void SetWaterVoxelRule(VoxelRule rule)
    {
        _waterVoxelRule = rule;
    }

    /// <summary>
    /// Clears any previously configured water voxel rule.
    /// </summary>
    public static void ClearWaterVoxelRule()
    {
        _waterVoxelRule = null;
    }

    internal static void Reset() => ClearWaterVoxelRule();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsConfigured(VolumeTraversalMode traversalMode)
    {
        return traversalMode switch
        {
            VolumeTraversalMode.Open => true,
            VolumeTraversalMode.Water => _waterVoxelRule != null,
            _ => false
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool Matches(Voxel voxel, VolumeTraversalMode traversalMode)
    {
        return traversalMode switch
        {
            VolumeTraversalMode.Open => true,
            VolumeTraversalMode.Water => _waterVoxelRule?.Invoke(voxel) == true,
            _ => false
        };
    }
}

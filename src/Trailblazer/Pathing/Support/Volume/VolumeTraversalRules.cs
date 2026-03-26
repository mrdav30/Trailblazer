using GridForge.Grids;
using GridForge.Spatial;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Trailblazer.Pathing;

/// <summary>
/// Holds authored and host-configured membership rules for raw volume traversal modes.
/// </summary>
/// <remarks>
/// Trailblazer can derive raw-volume membership from authored <see cref="VolumePartition"/> data
/// created during chart initialization. Hosts can also install supplemental open-volume and
/// water-volume rules for engine-specific partitioning or bespoke world logic. These rules are
/// additive: they can extend medium membership on voxels that already belong to Trailblazer's
/// runtime traversal world, but they do not suppress authored membership.
/// </remarks>
public static class VolumeTraversalRules
{
    public delegate bool VoxelRule(Voxel voxel);

    private static VoxelRule _openVoxelRule;

    private static VoxelRule _waterVoxelRule;

    private static int _registryVersion;

    /// <summary>
    /// Indicates whether an open-volume rule is currently configured.
    /// </summary>
    public static bool HasOpenVoxelRule => _openVoxelRule != null;

    /// <summary>
    /// Indicates whether a water-volume rule is currently configured.
    /// </summary>
    public static bool HasWaterVoxelRule => _waterVoxelRule != null;

    internal static int RegistryVersion => _registryVersion;

    /// <summary>
    /// Uses a host-defined voxel partition type to add open-volume membership on eligible voxels.
    /// </summary>
    public static void SetOpenVoxelPartition<TPartition>()
        where TPartition : class, IVoxelPartition
    {
        SetOpenVoxelRule(static voxel =>
            voxel != null
            && voxel.HasPartition<TPartition>());
    }

    /// <summary>
    /// Sets a host-defined rule that adds open-volume membership on eligible voxels.
    /// </summary>
    public static void SetOpenVoxelRule(VoxelRule rule)
    {
        _openVoxelRule = rule;
        InvalidateRuleConfiguration();
    }

    /// <summary>
    /// Clears any previously configured open-volume voxel rule.
    /// </summary>
    public static void ClearOpenVoxelRule()
    {
        _openVoxelRule = null;
        InvalidateRuleConfiguration();
    }

    /// <summary>
    /// Uses a host-defined voxel partition type to add water-volume membership on eligible voxels.
    /// </summary>
    public static void SetWaterVoxelPartition<TPartition>()
        where TPartition : class, IVoxelPartition
    {
        SetWaterVoxelRule(static voxel =>
            voxel != null
            && voxel.HasPartition<TPartition>());
    }

    /// <summary>
    /// Sets a host-defined rule that adds water-volume membership on eligible voxels.
    /// </summary>
    public static void SetWaterVoxelRule(VoxelRule rule)
    {
        _waterVoxelRule = rule;
        InvalidateRuleConfiguration();
    }

    /// <summary>
    /// Clears any previously configured water voxel rule.
    /// </summary>
    public static void ClearWaterVoxelRule()
    {
        _waterVoxelRule = null;
        InvalidateRuleConfiguration();
    }

    internal static void Reset()
    {
        _openVoxelRule = null;
        _waterVoxelRule = null;
        InvalidateRuleConfiguration();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsConfigured(VolumeTraversalMode traversalMode)
    {
        return traversalMode switch
        {
            VolumeTraversalMode.Open => _openVoxelRule != null || PathManager.HasAuthoredVolumeTraversal(VolumeTraversalMode.Open),
            VolumeTraversalMode.Water => _waterVoxelRule != null || PathManager.HasAuthoredVolumeTraversal(VolumeTraversalMode.Water),
            _ => false
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool Matches(Voxel voxel, VolumeTraversalMode traversalMode)
    {
        if (voxel == null)
            return false;

        bool hasTrailblazerPartition = voxel.HasPartition<PathPartition>();
        bool hasAuthoredVolumePartition = voxel.TryGetPartition(out VolumePartition volumePartition);
        hasTrailblazerPartition |= hasAuthoredVolumePartition;
        if (!hasTrailblazerPartition)
            return false;

        bool hostOpenMatch = _openVoxelRule?.Invoke(voxel) == true;
        bool hostWaterMatch = _waterVoxelRule?.Invoke(voxel) == true;
        bool authoredOpenMatch = hasAuthoredVolumePartition
            && volumePartition.SupportsTraversal(VolumeTraversalMode.Open);
        bool authoredWaterMatch = hasAuthoredVolumePartition
            && volumePartition.SupportsTraversal(VolumeTraversalMode.Water);

        // Host rules only add medium membership; they do not suppress authored media.
        return traversalMode switch
        {
            VolumeTraversalMode.Open => authoredOpenMatch || hostOpenMatch,
            VolumeTraversalMode.Water => authoredWaterMatch || hostWaterMatch,
            _ => false
        };
    }

    private static void InvalidateRuleConfiguration()
    {
        Interlocked.Increment(ref _registryVersion);
        PathGuideFactory.InvalidateVolumeCache();
    }
}

using GridForge.Grids;
using GridForge.Spatial;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Trailblazer.Pathing;

/// <summary>
/// Holds authored and host-configured membership rules for raw volume traversal media.
/// </summary>
/// <remarks>
/// Trailblazer can derive raw-volume membership from authored <see cref="VolumeChartPartition"/> data
/// created during chart initialization. Hosts can also install supplemental gas and
/// liquid rules for engine-specific partitioning or bespoke world logic. These rules are
/// additive: they can extend medium membership on voxels that already belong to Trailblazer's
/// runtime traversal world, but they do not suppress authored membership.
/// </remarks>
public static class VolumeMediumRules
{
    /// <summary>
    /// Represents a method that defines a rule to evaluate a voxel and 
    /// determine whether it satisfies specific criteria.
    /// </summary>
    /// <param name="voxel">The voxel to evaluate against the rule.</param>
    /// <returns>true if the voxel meets the criteria defined by the rule; otherwise, false.</returns>
    public delegate bool VoxelRule(Voxel voxel);

    private static VoxelRule? _gasVoxelRule;

    private static VoxelRule? _liquidVoxelRule;

    private static int _registryVersion;

    /// <summary>
    /// Indicates whether a gas rule is currently configured.
    /// </summary>
    public static bool HasGasVoxelRule => _gasVoxelRule != null;

    /// <summary>
    /// Indicates whether a liquid rule is currently configured.
    /// </summary>
    public static bool HasLiquidVoxelRule => _liquidVoxelRule != null;

    internal static int RegistryVersion => _registryVersion;

    /// <summary>
    /// Uses a host-defined voxel partition type to add gas membership on eligible voxels.
    /// </summary>
    public static void SetGasVoxelPartition<TPartition>()
        where TPartition : class, IVoxelPartition
    {
        SetGasVoxelRule(static voxel =>
            voxel != null
            && voxel.HasPartition<TPartition>());
    }

    /// <summary>
    /// Sets a host-defined rule that adds gas membership on eligible voxels.
    /// </summary>
    public static void SetGasVoxelRule(VoxelRule rule)
    {
        _gasVoxelRule = rule;
        InvalidateRuleConfiguration();
    }

    /// <summary>
    /// Clears any previously configured gas voxel rule.
    /// </summary>
    public static void ClearGasVoxelRule()
    {
        _gasVoxelRule = null;
        InvalidateRuleConfiguration();
    }

    /// <summary>
    /// Uses a host-defined voxel partition type to add liquid membership on eligible voxels.
    /// </summary>
    public static void SetLiquidVoxelPartition<TPartition>()
        where TPartition : class, IVoxelPartition
    {
        SetLiquidVoxelRule(static voxel =>
            voxel != null
            && voxel.HasPartition<TPartition>());
    }

    /// <summary>
    /// Sets a host-defined rule that adds liquid membership on eligible voxels.
    /// </summary>
    public static void SetLiquidVoxelRule(VoxelRule rule)
    {
        _liquidVoxelRule = rule;
        InvalidateRuleConfiguration();
    }

    /// <summary>
    /// Clears any previously configured liquid voxel rule.
    /// </summary>
    public static void ClearLiquidVoxelRule()
    {
        _liquidVoxelRule = null;
        InvalidateRuleConfiguration();
    }

    internal static void Reset()
    {
        _gasVoxelRule = null;
        _liquidVoxelRule = null;
        InvalidateRuleConfiguration();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsConfigured(TraversalMedium medium)
    {
        return medium switch
        {
            TraversalMedium.Gas => _gasVoxelRule != null || PathManager.HasAuthoredVolumeMedium(TraversalMedium.Gas),
            TraversalMedium.Liquid => _liquidVoxelRule != null || PathManager.HasAuthoredVolumeMedium(TraversalMedium.Liquid),
            _ => false
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool Matches(Voxel voxel, TraversalMedium medium)
    {
        if (voxel == null)
            return false;

        bool hasTrailblazerPartition = voxel.HasPartition<SolidChartPartition>();
        bool hasAuthoredVolumeChartPartition = voxel.TryGetPartition(out VolumeChartPartition? volumeChartPartition)
            && volumeChartPartition != null;
        hasTrailblazerPartition |= hasAuthoredVolumeChartPartition;
        if (!hasTrailblazerPartition)
            return false;

        bool hostGasMatch = _gasVoxelRule?.Invoke(voxel) == true;
        bool hostLiquidMatch = _liquidVoxelRule?.Invoke(voxel) == true;
        bool authoredGasMatch = volumeChartPartition?.SupportsMedium(TraversalMedium.Gas) == true;
        bool authoredLiquidMatch = volumeChartPartition?.SupportsMedium(TraversalMedium.Liquid) == true;

        // Host rules only add medium membership; they do not suppress authored media.
        return medium switch
        {
            TraversalMedium.Gas => authoredGasMatch || hostGasMatch,
            TraversalMedium.Liquid => authoredLiquidMatch || hostLiquidMatch,
            _ => false
        };
    }

    private static void InvalidateRuleConfiguration()
    {
        Interlocked.Increment(ref _registryVersion);
        TraversalTransitionRegistry.RefreshManagedManualTransitions();
        PathGuideFactory.InvalidateVolumeCache();
    }
}

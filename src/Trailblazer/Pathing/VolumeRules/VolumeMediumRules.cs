//=======================================================================
// VolumeMediumRules.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Grids;
using GridForge.Spatial;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// Evaluates whether a host-defined voxel belongs to a raw-volume traversal medium.
/// </summary>
/// <param name="voxel">The voxel to evaluate.</param>
/// <returns>true when the voxel belongs to the configured medium; otherwise, false.</returns>
public delegate bool VolumeVoxelRule(Voxel voxel);

/// <summary>
/// Implements authored and host-configured membership rules for raw volume traversal media.
/// </summary>
internal static class VolumeMediumRules
{
    private static VolumeMediumRulesState State => PathManager.ActiveState.VolumeRulesState;

    /// <summary>
    /// Indicates whether a gas rule is currently configured.
    /// </summary>
    public static bool HasGasVoxelRule => State.GasVoxelRule != null;

    /// <summary>
    /// Indicates whether a liquid rule is currently configured.
    /// </summary>
    public static bool HasLiquidVoxelRule => State.LiquidVoxelRule != null;

    internal static int RegistryVersion => State.RegistryVersion;

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
    public static void SetGasVoxelRule(VolumeVoxelRule rule)
    {
        State.GasVoxelRule = rule;
        InvalidateRuleConfiguration();
    }

    /// <summary>
    /// Clears any previously configured gas voxel rule.
    /// </summary>
    public static void ClearGasVoxelRule()
    {
        State.GasVoxelRule = null;
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
    public static void SetLiquidVoxelRule(VolumeVoxelRule rule)
    {
        State.LiquidVoxelRule = rule;
        InvalidateRuleConfiguration();
    }

    /// <summary>
    /// Clears any previously configured liquid voxel rule.
    /// </summary>
    public static void ClearLiquidVoxelRule()
    {
        State.LiquidVoxelRule = null;
        InvalidateRuleConfiguration();
    }

    internal static void Reset()
    {
        if (!PathManager.TryGetActiveState(out _))
            return;

        State.GasVoxelRule = null;
        State.LiquidVoxelRule = null;
        InvalidateRuleConfiguration();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsConfigured(TraversalMedium medium)
    {
        return IsConfigured(PathManager.ActiveState, medium);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool IsConfigured(PathingWorldState pathingState, TraversalMedium medium)
    {
        VolumeMediumRulesState state = pathingState.VolumeRulesState;
        return medium switch
        {
            TraversalMedium.Gas => state.GasVoxelRule != null || PathManager.HasAuthoredVolumeMedium(pathingState, TraversalMedium.Gas),
            TraversalMedium.Liquid => state.LiquidVoxelRule != null || PathManager.HasAuthoredVolumeMedium(pathingState, TraversalMedium.Liquid),
            _ => false
        };
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool Matches(Voxel voxel, TraversalMedium medium)
    {
        return Matches(PathManager.ActiveState, voxel, medium);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static bool Matches(PathingWorldState pathingState, Voxel voxel, TraversalMedium medium)
    {
        if (voxel == null)
            return false;

        bool hasTrailblazerPartition = voxel.HasPartition<SolidChartPartition>();
        bool hasAuthoredVolumeChartPartition = voxel.TryGetPartition(out VolumeChartPartition? volumeChartPartition)
            && volumeChartPartition != null;
        hasTrailblazerPartition |= hasAuthoredVolumeChartPartition;
        if (!hasTrailblazerPartition)
            return false;

        VolumeMediumRulesState state = pathingState.VolumeRulesState;
        bool hostGasMatch = state.GasVoxelRule?.Invoke(voxel) == true;
        bool hostLiquidMatch = state.LiquidVoxelRule?.Invoke(voxel) == true;
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
        State.IncrementRegistryVersion();
        TraversalTransitionRegistry.RefreshManagedManualTransitions();
        PathGuideFactory.InvalidateVolumeCache();
    }
}

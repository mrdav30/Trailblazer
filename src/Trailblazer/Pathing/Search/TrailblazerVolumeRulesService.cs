using GridForge.Grids;
using GridForge.Spatial;
using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Context-owned API for host-configured raw-volume medium rules.
/// </summary>
public sealed class TrailblazerVolumeRulesService
{
    private readonly TrailblazerWorldContext _context;
    private readonly PathingWorldState _state;

    internal TrailblazerVolumeRulesService(TrailblazerWorldContext context, PathingWorldState state)
    {
        _context = context;
        _state = state;
    }

    /// <inheritdoc cref="VolumeMediumRules.HasGasVoxelRule"/>
    public bool HasGasVoxelRule
    {
        get
        {
            EnsureUsable();
            using (PathManager.EnterState(_state))
                return VolumeMediumRules.HasGasVoxelRule;
        }
    }

    /// <inheritdoc cref="VolumeMediumRules.HasLiquidVoxelRule"/>
    public bool HasLiquidVoxelRule
    {
        get
        {
            EnsureUsable();
            using (PathManager.EnterState(_state))
                return VolumeMediumRules.HasLiquidVoxelRule;
        }
    }

    internal int RegistryVersion
    {
        get
        {
            EnsureUsable();
            using (PathManager.EnterState(_state))
                return VolumeMediumRules.RegistryVersion;
        }
    }

    /// <inheritdoc cref="VolumeMediumRules.SetGasVoxelPartition{TPartition}"/>
    public void SetGasVoxelPartition<TPartition>()
        where TPartition : class, IVoxelPartition
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            VolumeMediumRules.SetGasVoxelPartition<TPartition>();
    }

    /// <inheritdoc cref="VolumeMediumRules.SetGasVoxelRule(VolumeMediumRules.VoxelRule)"/>
    public void SetGasVoxelRule(VolumeMediumRules.VoxelRule rule)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            VolumeMediumRules.SetGasVoxelRule(rule);
    }

    /// <inheritdoc cref="VolumeMediumRules.ClearGasVoxelRule"/>
    public void ClearGasVoxelRule()
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            VolumeMediumRules.ClearGasVoxelRule();
    }

    /// <inheritdoc cref="VolumeMediumRules.SetLiquidVoxelPartition{TPartition}"/>
    public void SetLiquidVoxelPartition<TPartition>()
        where TPartition : class, IVoxelPartition
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            VolumeMediumRules.SetLiquidVoxelPartition<TPartition>();
    }

    /// <inheritdoc cref="VolumeMediumRules.SetLiquidVoxelRule(VolumeMediumRules.VoxelRule)"/>
    public void SetLiquidVoxelRule(VolumeMediumRules.VoxelRule rule)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            VolumeMediumRules.SetLiquidVoxelRule(rule);
    }

    /// <inheritdoc cref="VolumeMediumRules.ClearLiquidVoxelRule"/>
    public void ClearLiquidVoxelRule()
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            VolumeMediumRules.ClearLiquidVoxelRule();
    }

    internal bool IsConfigured(TraversalMedium medium)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            return VolumeMediumRules.IsConfigured(medium);
    }

    internal bool Matches(Voxel voxel, TraversalMedium medium)
    {
        EnsureUsable();
        using (PathManager.EnterState(_state))
            return VolumeMediumRules.Matches(voxel, medium);
    }

    private void EnsureUsable()
    {
        if (_context.IsDisposed)
            throw new ObjectDisposedException(nameof(TrailblazerWorldContext));
        if (!_context.World.IsActive)
            throw new InvalidOperationException("TrailblazerVolumeRulesService is bound to an inactive GridWorld.");
    }
}

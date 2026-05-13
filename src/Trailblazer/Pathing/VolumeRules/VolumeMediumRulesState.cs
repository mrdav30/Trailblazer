using System.Threading;

namespace Trailblazer.Pathing;

/// <summary>
/// Stores host-provided raw-volume medium rules for one pathing context.
/// </summary>
internal sealed class VolumeMediumRulesState
{
    internal VolumeVoxelRule? GasVoxelRule;

    internal VolumeVoxelRule? LiquidVoxelRule;

    internal int RegistryVersion;

    internal void IncrementRegistryVersion()
    {
        Interlocked.Increment(ref RegistryVersion);
    }
}

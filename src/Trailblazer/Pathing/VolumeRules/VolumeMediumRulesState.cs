//=======================================================================
// VolumeMediumRulesState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

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

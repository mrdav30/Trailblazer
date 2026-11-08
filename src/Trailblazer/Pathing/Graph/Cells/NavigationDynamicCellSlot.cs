//=======================================================================
// NavigationDynamicCellSlot.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>Maps a dynamic navigation cell's voxel index to its runtime slot.</summary>
internal readonly struct NavigationDynamicCellSlot
{
    internal NavigationDynamicCellSlot(VoxelIndex index, int slot)
    {
        Index = index;
        Slot = slot;
    }

    internal VoxelIndex Index { get; }

    internal int Slot { get; }
}

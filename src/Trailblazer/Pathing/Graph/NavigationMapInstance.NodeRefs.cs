//=======================================================================
// NavigationMapInstance.NodeRefs.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Spatial;

namespace Trailblazer.Pathing;

internal sealed partial class NavigationMapInstance
{
    internal bool TryGetSlotIndex(int slot, out VoxelIndex index)
    {
        if ((uint)slot < (uint)Map.CellSpan.Length)
        {
            index = Map.CellSpan[slot].Index;
            return true;
        }

        return _dynamicSlotIndexes.TryGetValue(slot, out index);
    }
}

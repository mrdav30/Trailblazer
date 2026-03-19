using GridForge.Grids;
using GridForge.Spatial;

namespace Trailblazer.Tests;

internal sealed class TestWaterPartition : IVoxelPartition
{
    public GlobalVoxelIndex GlobalIndex { get; private set; }

    public void SetParentIndex(GlobalVoxelIndex parentVoxelIndex)
    {
        GlobalIndex = parentVoxelIndex;
    }

    public void OnAddToVoxel(Voxel voxel)
    {
    }

    public void OnRemoveFromVoxel(Voxel voxel)
    {
    }
}

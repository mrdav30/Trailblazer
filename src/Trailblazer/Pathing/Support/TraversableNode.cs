using GridForge.Grids;

namespace Trailblazer.Pathing
{
    public struct TraversableVoxel
    {
        public Voxel Voxel { get; set; }

        public PathPartition Partition { get; set; }

        public LinearDirection Direction { get; set; }
    }
}

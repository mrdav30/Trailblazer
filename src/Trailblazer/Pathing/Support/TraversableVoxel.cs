using GridForge.Grids;
using GridForge.Spatial;

namespace Trailblazer.Pathing
{
    public struct TraversableVoxel
    {
        public Voxel Voxel { get; set; }

        public PathPartition Partition { get; set; }

        public SpatialDirection Direction { get; set; }
    }
}

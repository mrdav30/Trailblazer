using GridForge.Grids;

namespace Trailblazer.Pathing
{
    public struct TraversableNode
    {
        public Node Node { get; set; }

        public PathPartition Partition { get; set; }

        public LinearDirection Direction { get; set; }
    }
}

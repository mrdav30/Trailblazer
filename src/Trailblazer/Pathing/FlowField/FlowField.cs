using FixedMathSharp;
using GridForge.Spatial;

namespace Trailblazer.Pathing
{
    public struct FlowField
    {
        public CoordinatesGlobal NodeCoordinates { get; set; }

        public Vector3d Direction { get; set; }

        public int DistanceToTarget { get; set; }
        
        public bool IsGoal { get; set; }
    }
}
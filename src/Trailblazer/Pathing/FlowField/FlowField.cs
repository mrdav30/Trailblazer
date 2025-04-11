using FixedMathSharp;
using GridForge.Spatial;

namespace Trailblazer.Pathing
{
    public struct FlowField
    {
        public CoordinatesGlobal NodeCoordinates { get; set; }

        public Vector3d Direction { get; set; }

        public bool HasLineOfSight { get; set; }
    }
}
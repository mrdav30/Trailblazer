using FixedMathSharp;

namespace Trailblazer.Pathing
{
    public interface IGuide
    {
        public bool HasPath { get; }
        
        public bool HasWaypoints { get; }

        void OnSetup();
        void RequestMovementPath(Vector3d from, Vector3d destination, Fixed64 size);
        Vector3d GetMovementDirection(Vector3d from);
        void MoveToNextWaypoint();


        void Reset();
    }
}

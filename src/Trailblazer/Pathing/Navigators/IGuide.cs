using FixedMathSharp;

namespace Trailblazer.Pathing
{
    public interface IGuide
    {
        public bool HasPath { get; }
        
        public bool MovingToWaypoint { get; }

        void OnSetup();
        void OnInitialize();
        void RequestMovementPath(Vector3d from, Vector3d destination, int size);
        Vector3d GetMovementDirection(Vector3d from, out Fixed64 distanceToMove);
        void CheckMovementStatus();


        void Reset();
    }
}

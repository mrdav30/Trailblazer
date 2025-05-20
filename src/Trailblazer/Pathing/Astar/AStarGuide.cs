using FixedMathSharp;
using SwiftCollections;

namespace Trailblazer.Pathing.Navigators
{
    public class AStarGuide : IGuide
    {
        public bool HasPath { get; private set; }

        private SwiftList<Vector3d> _myPath;

        private int _pathIndex;

        public Vector3d? Target { get; private set; }

        public bool HasWaypoints => HasPath && _pathIndex < _myPath.Count - 1 && _pathIndex > 0;

        public void OnSetup()
        {
            _myPath = new();
            HasPath = false;
            _pathIndex = -1;
        }

        public void RequestMovementPath(Vector3d from, Vector3d destination, Fixed64 unitSize)
        {
            AStarPathRequest pathRequest = new(from, destination, unitSize, (success, result) =>
            {
                HasPath = success;
                _myPath = result;
                _pathIndex = success ? 0 : -1;
                Target = success ? _myPath[_pathIndex] : null;
            });

            PathingManager.RequestPath(pathRequest);
        }

        public Vector3d GetMovementDirection(Vector3d from)
        {
            if (Target == null)
                return Vector3d.Zero;

            Vector3d direction = Target.Value - from;

            return direction;
        }

        public void MoveToNextWaypoint()
        {
            if (HasWaypoints)
                _pathIndex++;
        }

        public void Reset()
        {
            HasPath = false;
            _myPath.FastClear();
        }
    }
}

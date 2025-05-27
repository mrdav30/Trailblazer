using FixedMathSharp;
using SwiftCollections;

namespace Trailblazer.Pathing.Navigators
{
    public class AStarGuide : IGuide
    {
        public bool HasPath { get; private set; }

        private SwiftList<Vector3d> _myPath;

        private int _pathIndex;

        public bool HasWaypoints => HasPath && _myPath.Count > 0 && _pathIndex >= 0 && _pathIndex < _myPath.Count;

        public bool HasArrived => HasPath && _pathIndex == _myPath.Count - 1;

        public Vector3d Target => HasWaypoints ? _myPath[_pathIndex]
            : _myPath.Count > 0 ? _myPath.Last() : Vector3d.Zero;

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
            });

            PathingManager.RequestPath(pathRequest);
        }

        public Vector3d GetMovementDirection(Vector3d from)
        {
            if (!HasPath || _myPath.Count == 0 || _pathIndex < 0 || _pathIndex >= _myPath.Count)
                return Vector3d.Zero;

            return (Target - from).Normal;
        }

        public void MoveToNextWaypoint() => _pathIndex++;

        public bool TryGetNextWaypoint(out Vector3d waypoint)
        {
            if (HasWaypoints)
            {
                waypoint = _myPath[_pathIndex];
                return true;
            }

            waypoint = Vector3d.Zero;
            return false;
        }

        public void Reset()
        {
            HasPath = false;
            _myPath.FastClear();
            _pathIndex = -1;
        }
    }
}

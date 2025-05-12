using FixedMathSharp;
using SwiftCollections;
using System.Drawing;

namespace Trailblazer.Pathing.Navigators
{
    public class AStarGuide : IGuide
    {
        public bool HasPath { get; private set; }

        private SwiftList<Vector3d> _myPath;

        private int _pathIndex;

        public Vector3d? Target { get; private set; }

        public bool MovingToWaypoint => HasPath && _pathIndex < _myPath.Count - 1;

        public void OnSetup()
        {
            _myPath = new();
        }

        public void OnInitialize()
        {
            HasPath = false;
            _myPath.FastClear();
            _pathIndex = 0;
        }

        public void RequestMovementPath(Vector3d from, Vector3d destination, int size)
        {
            AStarPathRequest pathRequest = new(from, destination, size, (success, result) =>
            {
                HasPath = success;
                _myPath = result;
                _pathIndex = success ? 0 : -1;
                Target = success ? _myPath[_pathIndex] : null;
            });

            PathingManager.RequestPath(pathRequest);
        }

        public Vector3d GetMovementDirection(Vector3d from, out Fixed64 distanceToMove)
        {
            distanceToMove = Fixed64.Zero;
            if (Target == null)
                return Vector3d.Zero;

            Vector3d direction = Target.Value - from;

            return direction;
        }

        public void CheckMovementStatus()
        {
            if (MovingToWaypoint && _pathIndex >= 0)
                _pathIndex++;
        }

        public void Reset()
        {
            HasPath = false;
            _myPath.FastClear();
        }
    }
}

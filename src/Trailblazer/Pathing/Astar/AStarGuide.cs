using FixedMathSharp;
using SwiftCollections;

namespace Trailblazer.Pathing
{
    public class AStarGuide : IGuide
    {
        public bool PathFound => _path != null;

        public bool IsValid => PathFound && _path.Count > 0;

        public bool IsInUse { get; private set; }

        public int LastUsedFrame { get; private set; }

        private SwiftList<Vector3d> _path;

        public SwiftList<Vector3d> Path => _path;

        public int RequestHashKey { get; private set; }

        public bool HasWaypoints => IsValid;

        public bool Initialize(IPathRequest request)
        {
            if (request is not AStarPathRequest aStarRequest || !request.IsValid)
                return false;

            int requestHashKey = request.GetHashCode();
            if (RequestHashKey == requestHashKey && IsValid)
                return true; // Reuse existing path

            if (!AStarSurveyor.Shared.FindPath(aStarRequest, out SwiftList<Vector3d> foundPath))
                return false;

            _path = foundPath;
            RequestHashKey = requestHashKey;

            return true;
        }

        public void MarkInUse() => IsInUse = true;

        public bool HasArrived(int index)
        {
            return IsValid && index == _path.Count - 1;
        }

        public int GetIndex(Vector3d from)
        {
            for(int i = 0; i < _path.Count; i++)
            {
                if (from.Equals(_path[i]))
                    return i;
            }

            return -1;
        }

        public Vector3d GetMovementDirection(Vector3d from, int index)
        {
            if (!IsValid || index < 0 || index >= _path.Count)
                return Vector3d.Zero;

            Vector3d movementDirection = !IsValid ? _path.Last() : _path[index];
            if (movementDirection == Vector3d.Zero)
                return Vector3d.Zero;

            return (movementDirection - from).Normal;
        }

        public bool TryGetNextWaypoint(int index, out Vector3d waypoint)
        {
            if (!IsValid || index < 0 || index >= _path.Count)
            {
                waypoint = Vector3d.Zero;
                return false;
            }

            waypoint = _path[index];
            return true;
        }

        public void Release()
        {
            IsInUse = false;
            LastUsedFrame = TrailblazerManager.FrameCount;
        }

        public void Dispose()
        {
            IsInUse = false;
            LastUsedFrame = -1;
            _path = null;
            RequestHashKey = -1;
        }
    }
}

using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;

namespace Trailblazer.Pathing
{
    /// <summary>
    /// Provides steering direction based on a flow field vector grid.
    /// Suitable for group-based or gradient-following movement strategies.
    /// </summary>
    public class FlowFieldGuide : IGuide
    {
        public bool PathFound => _fields != null;

        public bool IsValid => PathFound && _fields.Count > 0;

        public bool IsInUse { get; private set; }

        public int LastUsedFrame { get; private set; }

        // key = node spawn token, value = vector flow field
        private SwiftDictionary<int, FlowField> _fields;

        private int _fieldSearchRange;

        public int RequestHashKey { get; private set; }

        public bool HasWaypoints => false;

        public bool Initialize(IPathRequest request)
        {
            if (request is not FlowFieldPathRequest flowFieldRequest)
                return false;

            int requestHashKey = request.RequestCacheKey;
            if (RequestHashKey == requestHashKey && IsValid)
            {
                // Make sure the start node is within the current fields collection
                if (_fields.ContainsKey(request.Start.SpawnToken))
                    return true;
            }

            int searchSize = request.MaxPathSearchRange ?? 0;
            if (searchSize <= 0)
            {
                // Retrieves the maximum length the path could possibly be
                if (!PathManager.GetMaxSearchSize(request.Start, request.End, out searchSize))
                    return false;

                flowFieldRequest.MaxPathSearchRange = searchSize;
            }

            if (!FlowFieldSurveyor.Shared.FindPath(flowFieldRequest, out SwiftDictionary<int, FlowField> foundFields))
                return false;

            _fields = foundFields;
            _fieldSearchRange = flowFieldRequest.FieldSearchRange;
            RequestHashKey = requestHashKey;

            return PathFound;
        }

        public void MarkInUse() => IsInUse = true;

        public bool HasArrived(int index)
        {
            if (!_fields.TryGetValue(index, out FlowField currentField))
                return false;

            return PathFound && currentField.IsGoal;
        }

        public int GetIndex(Vector3d from)
        {
            if (!PathFound
                || _fields == null
                || _fields.Count <= 0
                || !GlobalGridManager.TryGetGridAndNode(from, out _, out Node currentNode))
            {
                return -1;
            }

            if (_fields.ContainsKey(currentNode.SpawnToken))
                return currentNode.SpawnToken;

            if (!FlowFieldSurveyor.TryGetNearestFlowAnchor(from, _fields, out Node destination, _fieldSearchRange))
                return -1;

            return destination.SpawnToken;
        }

        public Vector3d GetMovementDirection(Vector3d from, int index)
        {
            if (!PathFound || _fields == null || _fields.Count <= 0)
                return Vector3d.Zero;

            if (_fields.ContainsKey(index))
            {
                Vector3d direction = FlowFieldSurveyor.SampleFlowVector(from, _fields);
                if (direction == Vector3d.Zero)
                    return Vector3d.Zero;

                return (direction - from).Normal;
            }

            return Vector3d.Zero;
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
            _fields = null;
            RequestHashKey = -1;
            _fieldSearchRange = 0;
        }
    }
}

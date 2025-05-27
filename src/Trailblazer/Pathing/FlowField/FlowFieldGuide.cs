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
        public bool HasPath { get; private set; }

        /// <summary>
        /// Indicates whether this guide uses waypoints. Always false for flow fields.
        /// </summary>
        public bool HasWaypoints => false;

        /// <summary>
        /// Not implemented. Flow fields do not use discrete waypoints for arrival logic.
        /// </summary>
        public bool HasArrived => HasPath && _currentField.IsGoal;

        public int SearchRange { get; set; } = FlowFieldPathRequest.DefaultSearchRange;

        // key = node spawn token, value = vector flow field
        private SwiftDictionary<int, FlowField> _fields;

        private FlowField _currentField;

        public void OnSetup()
        {
            _fields = new();
            HasPath = false;
        }

        public void RequestMovementPath(Vector3d from, Vector3d destination, Fixed64 unitSize)
        {
            FlowFieldPathRequest pathRequest = new(from, destination, unitSize, (success, result) =>
            {
                _fields = result;
                HasPath = success;
            });
            pathRequest.SearchRange = SearchRange;

            PathingManager.RequestPath(pathRequest);
        }

        public Vector3d GetMovementDirection(Vector3d from)
        {
            if (_fields == null || _fields.Count <= 0 || !GlobalGridManager.TryGetGridAndNode(from, out _, out Node currentNode))
                return Vector3d.Zero;

            Vector3d direction;
            if (_fields.TryGetValue(currentNode.SpawnToken, out _currentField))
                direction = FlowFieldSurveyor.SampleFlowVector(from, _fields);
            else
            {
                // Agent landed on a spot with no flow.
                // Try to course correct by finding the closest flow field to move towards
                if (!FlowFieldSurveyor.TryGetNearestFlowAnchor(from, _fields, out Vector3d destination, SearchRange))
                    return Vector3d.Zero;

                direction = destination - from;
            }

            return direction;
        }

        /// <summary>
        /// Unused for flow field logic. No discrete waypoint to advance to.
        /// </summary>
        public void MoveToNextWaypoint() { }

        /// <summary>
        /// Not implemented. Flow fields do not expose next waypoint positions.
        /// </summary>
        bool IGuide.TryGetNextWaypoint(out Vector3d waypoint)
        {
            waypoint = Vector3d.Zero;
            return false;
        }

        public void Reset()
        {
            _fields.Clear();
        }
    }
}

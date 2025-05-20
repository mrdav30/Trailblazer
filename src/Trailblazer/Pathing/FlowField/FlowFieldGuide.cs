using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;

namespace Trailblazer.Pathing
{
    public class FlowFieldGuide : IGuide
    {
        private static readonly Fixed64 DefaultSearchRange = Fixed64.One * 10;

        public bool HasPath { get; private set; }

        // key = world position, value = vector flow field
        private SwiftDictionary<int, FlowField> _fields;

        public bool HasWaypoints => false; // flow fields don't have waypoints...

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

            PathingManager.RequestPath(pathRequest);
        }

        public Vector3d GetMovementDirection(Vector3d from)
        {
            if (_fields == null || _fields.Count <= 0 || !GlobalGridManager.TryGetGridAndNode(from, out _, out Node currentNode))
                return Vector3d.Zero;

            Vector3d direction;
            if (_fields.TryGetValue(currentNode.SpawnToken, out _))
                direction = SampleFlowVector(from, _fields);
            else
            {
                // Agent landed on a spot with no flow.
                // Try to course correct by finding the closest flow field to move towards
                if (!TryGetNearestFlowAnchor(from, _fields, out Vector3d destination))
                    return Vector3d.Zero;

                direction = destination - from;
            }

            return direction;
        }

        public void MoveToNextWaypoint() { }

        /// <summary>
        /// Work out the force to apply to us based on the flow field grid squares we are on.
        /// we apply bilinear interpolation on the 4 grid squares nearest to us to work out our force.
        /// http://en.wikipedia.org/wiki/Bilinear_interpolation#Nonlinear
        /// </summary>
        public static Vector3d SampleFlowVector(Vector3d worldPosition, SwiftDictionary<int, FlowField> fields)
        {
            // Get bottom-left corner of the square the agent is standing in
            Vector3d corner = new Vector3d(
                FixedMath.Floor(worldPosition.x / GlobalGridManager.NodeSize) * GlobalGridManager.NodeSize,
                FixedMath.Floor(worldPosition.y / GlobalGridManager.NodeSize) * GlobalGridManager.NodeSize,
                FixedMath.Floor(worldPosition.z / GlobalGridManager.NodeSize) * GlobalGridManager.NodeSize
            );

            // Compute normalized offset in cell (0..1)
            Fixed64 dx = (worldPosition.x - corner.x) / GlobalGridManager.NodeSize;
            Fixed64 dz = (worldPosition.z - corner.z) / GlobalGridManager.NodeSize;

            // Sample the 4 surrounding node centers
            Vector3d bottomLeft = corner;
            Vector3d bottomRight = corner + new Vector3d(GlobalGridManager.NodeSize, Fixed64.Zero, Fixed64.Zero);
            Vector3d topLeft = corner + new Vector3d(Fixed64.Zero, Fixed64.Zero, GlobalGridManager.NodeSize);
            Vector3d topRight = corner + new Vector3d(GlobalGridManager.NodeSize, Fixed64.Zero, GlobalGridManager.NodeSize);

            // Get flow vectors
            Vector3d f00 = GetFlowVector(bottomLeft, fields);
            Vector3d f10 = GetFlowVector(bottomRight, fields);
            Vector3d f01 = GetFlowVector(topLeft, fields);
            Vector3d f11 = GetFlowVector(topRight, fields);

            // Bilinear interpolation
            Vector3d zHigh = f00 * (Fixed64.One - dx) + f10 * dx;
            Vector3d zLow = f01 * (Fixed64.One - dx) + f11 * dx;
            Vector3d blended = zHigh * (Fixed64.One - dz) + zLow * dz;

            blended.Normalize();
            return blended;
        }

        public static bool TryGetNearestFlowAnchor(
            Vector3d from,
            SwiftDictionary<int, FlowField> fields,
            out Vector3d closestTarget,
            Fixed64? maxRange = null)
        {
            closestTarget = Vector3d.Zero;
            Fixed64 range = maxRange ?? DefaultSearchRange;
            Fixed64 minDistanceSq = range * range;
            bool found = false;

            foreach (FlowField flow in fields.Values)
            {
                if (!GlobalGridManager.TryGetGridAndNode(flow.NodeCoordinates, out _, out Node flowNode))
                    continue;

                Fixed64 distSq = Vector3d.SqrDistance(from, flowNode.WorldPosition);
                if (distSq <= minDistanceSq)
                {
                    closestTarget = flowNode.WorldPosition;
                    minDistanceSq = distSq;
                    found = true;
                }
            }

            return found;
        }

        public static Vector3d GetFlowVector(Vector3d position, SwiftDictionary<int, FlowField> fields)
        {
            if (GlobalGridManager.TryGetGridAndNode(position, out _, out Node node))
            {
                if (fields.TryGetValue(node.SpawnToken, out FlowField field))
                    return field.Direction;
            }
            return Vector3d.Zero;
        }

        public void Reset()
        {
            _fields.Clear();
        }
    }
}

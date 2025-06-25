using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing
{
    public abstract class PathRequest : IPathRequest
    {
        protected Vector3d? _origin;
        public Vector3d Origin => _origin ?? default;

        protected Voxel _startNode;
        public Voxel StartNode => _startNode;

        protected Vector3d? _destination;
        public Vector3d Destination => _destination ?? default;

        protected Voxel _endNode;
        public Voxel EndNode => _endNode;

        public Fixed64 UnitSize { get; protected set; }

        public bool AllowUnwalkable { get; set; }

        public bool HasZeroDisplacement =>
            _startNode == null
            || _endNode == null
            || _startNode.SpawnToken == _endNode.SpawnToken;

        public int? MaxPathSearchRange { get; set; }

        public bool HasOrigin => _origin.HasValue && StartNode != null;

        public bool HasDestination => _destination.HasValue && EndNode != null;

        public bool HasValidEndpoints => HasOrigin && HasDestination;

        public bool IsValid => HasValidEndpoints && MaxPathSearchRange.HasValue;

        public int RequestCacheKey => GetHashCode();

        public bool TryPrepare(Vector3d origin, Vector3d destination, Fixed64 unitSize)
        {
            bool endPointsFound = VoxelFinder.TryGetPathEdgeVoxels(
                origin,
                destination,
                out Voxel startVoxel,
                out Voxel endVoxel,
                unitSize);
            if (!endPointsFound)
                return false;

            _origin = origin;
            _startNode = startVoxel;
            _destination = destination;
            _endNode = endVoxel;
            UnitSize = unitSize;

            Validate();

            return true;
        }

        // If path created without valid nodes, then set later, this must be called before processing the request
        public bool Validate()
        {
            if (IsValid) return true;

            if (!HasValidEndpoints) return false;

            if (!MaxPathSearchRange.HasValue
                && PathManager.GetMaxSearchSize(StartNode, EndNode, out int searchSize))
            {
                MaxPathSearchRange = searchSize;
            }

            return IsValid;
        }

        public bool TrySetOrigin(Vector3d origin, bool resetSearchRange = false)
        {
            if (!_destination.HasValue) return false;

            bool success = VoxelFinder.GetStartVoxel(
                origin,
                Destination,
                out Voxel newStartNode,
                AllowUnwalkable,
                UnitSize);

            if (!success) return false;

            // force reset if grid changed
            if (newStartNode.GridIndex != _startNode.GridIndex)
                resetSearchRange = true;

            _origin = origin;
            _startNode = newStartNode;

            if (resetSearchRange)
            {
                MaxPathSearchRange = null;
                Validate();
            }

            return true;
        }

        public bool TrySetDestination(Vector3d destination, bool resetSearchRange = false)
        {
            if (!_origin.HasValue) return false;

            bool success = VoxelFinder.GetEndVoxel(
                Origin,
                destination,
                out Voxel newEndNode,
                AllowUnwalkable,
                UnitSize);

            if (!success) return false;

            // force reset if grid changed
            if (newEndNode.GridIndex != _endNode.GridIndex)
                resetSearchRange = true;

            _destination = destination;
            _endNode = newEndNode;

            if (resetSearchRange)
            {
                MaxPathSearchRange = null;
                Validate();
            }

            return true;
        }

        public override abstract int GetHashCode();
    }
}

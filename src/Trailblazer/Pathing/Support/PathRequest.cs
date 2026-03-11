using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing;

public abstract class PathRequest : IPathRequest
{
    protected Voxel _startNode;
    public Voxel StartNode => _startNode;

    protected Voxel _endNode;
    public Voxel EndNode => _endNode;

    public Fixed64 UnitSize { get; protected set; }

    public bool AllowUnwalkable { get; set; }

    public bool HasZeroDisplacement =>
        _startNode == null
        || _endNode == null
        || _startNode.SpawnToken == _endNode.SpawnToken;

    public int? MaxPathSearchRange { get; set; }

    public bool HasOrigin => _startNode != null;

    public bool HasDestination => _endNode != null;

    public bool HasValidEndpoints => HasOrigin && HasDestination;

    public bool IsValid => HasValidEndpoints && MaxPathSearchRange.HasValue;

    public int RequestCacheKey => GetHashCode();

    public bool TryPrepare(
        Vector3d origin,
        Vector3d destination,
        Fixed64? unitSize = null)
    {
        bool success = VoxelFinder.TryGetPathEdgeVoxels(
            origin,
            destination,
            out Voxel startVoxel,
            out Voxel endVoxel,
            unitSize);

        // need to set these even if null incase the new size invalidates the request
        _startNode = startVoxel;
        _endNode = endVoxel;
        UnitSize = unitSize ?? GlobalGridManager.VoxelSize;

        if (!success)
            return false;

        Validate();

        return true;
    }

    public bool TrySetOrigin(Vector3d origin, bool resetSearchRange = false)
    {
        if (_endNode == null) return false;

        bool success = VoxelFinder.GetStartVoxel(
            origin,
            _endNode.WorldPosition,
            out Voxel newVoxel,
            AllowUnwalkable,
            UnitSize);

        if (!success) return false;

        if (_startNode != null)
        {
            // nothing to do here then
            if (newVoxel.SpawnToken == _startNode.SpawnToken)
                return true;

            // force reset if grid changed
            if (newVoxel.GridIndex != _startNode.GridIndex)
                resetSearchRange = true;
        }

        _startNode = newVoxel;

        if (resetSearchRange)
        {
            MaxPathSearchRange = null;
            Validate();
        }

        return true;
    }

    public bool TrySetDestination(Vector3d destination, bool resetSearchRange = false)
    {
        if (_startNode == null) return false;

        bool success = VoxelFinder.GetEndVoxel(
            _startNode.WorldPosition,
            destination,
            out Voxel newVoxel,
            AllowUnwalkable,
            UnitSize);

        if (!success) return false;

        if (_endNode != null)
        {
            // nothing to do here then
            if (newVoxel.SpawnToken == _endNode.SpawnToken)
                return true;

            // force reset if grid changed
            if (newVoxel.GridIndex != _endNode.GridIndex)
                resetSearchRange = true;
        }

        _endNode = newVoxel;

        if (resetSearchRange)
        {
            MaxPathSearchRange = null;
            Validate();
        }

        return true;
    }

    public bool TrySetUnitSize(Fixed64 unitSize)
    {
        // no change
        if (UnitSize == unitSize | !HasValidEndpoints) return false;

        return TryPrepare(_startNode.WorldPosition, _endNode.WorldPosition, unitSize);
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

    public override abstract int GetHashCode();
}

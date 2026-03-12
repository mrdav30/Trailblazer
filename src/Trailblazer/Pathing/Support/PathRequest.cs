using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing;

public abstract class PathRequest : IPathRequest
{
    public Vector3d Origin { get; protected set; }

    public Voxel StartNode { get; protected set; }

    public Vector3d TargetPosition { get; protected set; }

    public Voxel EndNode { get; protected set; }

    public Fixed64 UnitSize { get; protected set; }

    public bool AllowUnwalkable { get; set; }

    public bool HasZeroDisplacement =>
        StartNode == null
        || EndNode == null
        || StartNode.SpawnToken == EndNode.SpawnToken;

    public int? MaxPathSearchRange { get; set; }

    public bool HasOrigin => StartNode != null;

    public bool HasDestination => EndNode != null;

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
        StartNode = startVoxel;
        EndNode = endVoxel;
        UnitSize = unitSize ?? GlobalGridManager.VoxelSize;

        if (!success)
            return false;

        Validate();

        return true;
    }

    public bool TrySetOrigin(Vector3d origin, bool resetSearchRange = false)
    {
        if (EndNode == null) return false;

        bool success = VoxelFinder.GetStartVoxel(
            origin,
            EndNode.WorldPosition,
            out Voxel newVoxel,
            AllowUnwalkable,
            UnitSize);

        if (!success) return false;

        if (StartNode != null)
        {
            // nothing to do here then
            if (newVoxel.SpawnToken == StartNode.SpawnToken)
                return true;

            // force reset if grid changed
            if (newVoxel.GridIndex != StartNode.GridIndex)
                resetSearchRange = true;
        }

        StartNode = newVoxel;

        if (resetSearchRange)
        {
            MaxPathSearchRange = null;
            Validate();
        }

        return true;
    }

    public bool TrySetDestination(Vector3d destination, bool resetSearchRange = false)
    {
        if (StartNode == null) return false;

        bool success = VoxelFinder.GetEndVoxel(
            StartNode.WorldPosition,
            destination,
            out Voxel newVoxel,
            AllowUnwalkable,
            UnitSize);

        if (!success) return false;

        if (EndNode != null)
        {
            // nothing to do here then
            if (newVoxel.SpawnToken == EndNode.SpawnToken)
                return true;

            // force reset if grid changed
            if (newVoxel.GridIndex != EndNode.GridIndex)
                resetSearchRange = true;
        }

        EndNode = newVoxel;

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
        if (UnitSize == unitSize || !HasValidEndpoints) return false;

        return TryPrepare(StartNode.WorldPosition, EndNode.WorldPosition, unitSize);
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

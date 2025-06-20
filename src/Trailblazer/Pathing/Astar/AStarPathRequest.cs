using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System;

namespace Trailblazer.Pathing
{
    public enum HeuristicMethod
    {
        Manhattan,
        Octile,
        Euclidean
        //Chebyshev?
    }

    /// <summary>
    /// A pathfinding request used for A* trail generation, including options for climb height, heuristic weighting,
    /// and path smoothing. Implements value-based comparison and hashing for guide pooling.
    /// </summary>
    public struct AStarPathRequest : IPathRequest, IEquatable<AStarPathRequest>
    {
        public bool AllowUnwalkable { get; set; }

        public Voxel Start { get; set; }

        public Voxel End { get; set; }

        public readonly bool HasZeroDisplacement => Start == null || End == null || Start.SpawnToken == End.SpawnToken;

        public Fixed64 UnitSize { get; set; }

        public int? MaxPathSearchRange { get; set; }

        public readonly bool IsValid =>
            Start != null &&
            End != null &&
            MaxPathSearchRange.HasValue;

        /// <summary>
        /// The maximum Y-axis height delta a unit can step or climb per voxel.
        /// Voxels exceeding this are ignored even if walkable and adjacent.
        /// </summary>
        public Fixed64 MaxClimbHeight { get; set; }

        public HeuristicMethod Heuristic { get; set; }

        public readonly int RequestCacheKey => GetHashCode();

        public static AStarPathRequest CreateEmpty() => Create(null, null);

        public static AStarPathRequest Create(
            Vector3d start,
            Vector3d end,
            Fixed64? unitSize = null,
            HeuristicMethod heuristic = HeuristicMethod.Manhattan,
            bool allowUnwalkable = false)
        {
            if (!GlobalGridManager.TryGetVoxel(start, out Voxel startVoxel)
                || !GlobalGridManager.TryGetVoxel(end, out Voxel endVoxel))
            {
                return default;
            }

            return Create(startVoxel, endVoxel, unitSize, heuristic, allowUnwalkable);
        }

        public static AStarPathRequest Create(
            Voxel start, 
            Voxel end, 
            Fixed64? unitSize = null, 
            HeuristicMethod heuristic = HeuristicMethod.Manhattan, 
            bool allowUnwalkable = false)
        {
            AStarPathRequest request = new()
            {
                Start = start,
                End = end,
                UnitSize = unitSize ?? GlobalGridManager.VoxelSize,
                Heuristic = heuristic,
                AllowUnwalkable = allowUnwalkable,
                MaxClimbHeight = GlobalGridManager.VoxelSize,
                MaxPathSearchRange = null
            };

            if (request.Start != null && request.End != null)
                request.Validate();
            return request;
        }

        public bool Prepare(Vector3d origin, Vector3d target)
        {
            bool endPointsFound = VoxelFinder.TryGetPathEdgeVoxels(
                origin,
                target,
                out Voxel startVoxel,
                out Voxel endVoxel);
            if (!endPointsFound)
                return false;

            Start = startVoxel;
            End = endVoxel;

            return true;
        }

        // If path created without valid nodes, then set later, this must be called before processing the request
        public bool Validate()
        {
            if(Start == null || End == null) return false;

            if (!MaxPathSearchRange.HasValue
                && PathManager.GetMaxSearchSize(Start, End, out int searchSize))
            {
                MaxPathSearchRange = searchSize;
            }

            return IsValid;
        }

        public override readonly bool Equals(object obj) =>
            obj is AStarPathRequest other && Equals(other);

        public readonly bool Equals(AStarPathRequest other) => RequestCacheKey == other.RequestCacheKey;

        public override readonly int GetHashCode()
        {
            return (
                Start?.SpawnToken ?? 0,
                End?.SpawnToken ?? 0,
                UnitSize,
                AllowUnwalkable,
                Heuristic,
                MaxClimbHeight,
                MaxPathSearchRange ?? -1
            ).CombineHashCodes();
        }
    }
}

using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System;

namespace Trailblazer.Pathing
{
    /// <summary>
    /// A pathfinding request used for flow field generation. Contains configuration for 
    /// destination targeting, dynamic agent sizing, and walkability override. 
    /// Implements value-based equality for guide pooling.
    /// </summary>
    public struct FlowFieldPathRequest : IPathRequest, IEquatable<FlowFieldPathRequest>
    {
        public const int DefaultExtraFloodRange = 10;

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
        /// Limits how much extra distance the flood will expand after the target is reached.
        /// </summary>
        public int ExtraFloodRange { get; set; }

        public readonly int RequestCacheKey => GetHashCode();

        public static FlowFieldPathRequest CreateEmpty() => Create(null, null);

        public static FlowFieldPathRequest Create(
            Vector3d start,
            Vector3d end,
            Fixed64? unitSize = null,
            bool allowUnwalkable = false)
        {
            if (!GlobalGridManager.TryGetGridAndVoxel(start, out _, out Voxel startVoxel)
                || !GlobalGridManager.TryGetGridAndVoxel(end, out _, out Voxel endVoxel))
            {
                return default;
            }

            return Create(startVoxel, endVoxel, unitSize, allowUnwalkable);
        }

        public static FlowFieldPathRequest Create(
            Voxel start,
            Voxel end,
            Fixed64? unitSize = null,
            bool allowUnwalkable = false)
        {
            FlowFieldPathRequest request = new()
            {
                Start = start,
                End = end,
                UnitSize = unitSize ?? GlobalGridManager.VoxelSize,
                AllowUnwalkable = allowUnwalkable,
                ExtraFloodRange = DefaultExtraFloodRange,
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
            if (Start == null || End == null) return false;

            if (!MaxPathSearchRange.HasValue
                && PathManager.GetMaxSearchSize(Start, End, out int searchSize))
            {
                MaxPathSearchRange = searchSize;
            }

            return IsValid;
        }

        public override readonly bool Equals(object obj) =>
            obj is FlowFieldPathRequest other && Equals(other);

        public readonly bool Equals(FlowFieldPathRequest other) => RequestCacheKey == other.RequestCacheKey;

        public override readonly int GetHashCode()
        {
            // Note: For FlowFields we don't care about the start voxel (only that the FlowField contains it)
            return (
                End?.SpawnToken ?? 0,
                UnitSize,
                AllowUnwalkable,
                ExtraFloodRange,
                MaxPathSearchRange ?? -1
            ).CombineHashCodes();
        }
    }
}

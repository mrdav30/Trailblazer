using FixedMathSharp;
using GridForge.Grids;
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

        /// <summary>
        /// Limits how much extra distance the flood will expand after the target is reached.
        /// </summary>
        public int ExtraFloodRange { get; set; }

        public readonly int RequestCacheKey => GetHashCode();

        public static FlowFieldPathRequest CreateEmpty() => Create(null, null);

        public static FlowFieldPathRequest Create(
            Voxel start, 
            Voxel end, 
            Fixed64? unitSize = null,
            bool allowUnwalkable = false)
        {
            return new FlowFieldPathRequest()
            {
                Start = start,
                End = end,
                UnitSize = unitSize ?? GlobalGridManager.VoxelSize,
                AllowUnwalkable = allowUnwalkable,
                ExtraFloodRange = DefaultExtraFloodRange,
                MaxPathSearchRange = null
            };
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

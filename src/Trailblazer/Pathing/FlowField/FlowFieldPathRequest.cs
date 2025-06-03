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
        public const int DefaultSearchRange = 10;

        public bool AllowUnwalkable { get; set; }

        public Node Start { get; set; }

        public Node End { get; set; }

        public readonly bool HasZeroDisplacement => Start == null || End == null || Start.SpawnToken == End.SpawnToken;

        public Fixed64 UnitSize { get; set; }

        public int? MaxPathSearchRange { get; set; }

        public int FieldSearchRange { get; set; }

        public readonly int RequestCacheKey => GetHashCode();

        public static FlowFieldPathRequest CreateEmpty() => Create(null, null);

        public static FlowFieldPathRequest Create(
            Node start, 
            Node end, 
            Fixed64? unitSize = null,
            bool allowUnwalkable = false)
        {
            return new FlowFieldPathRequest()
            {

                Start = start,
                End = end,
                UnitSize = unitSize ?? GlobalGridManager.NodeSize,
                AllowUnwalkable = false,
                FieldSearchRange = DefaultSearchRange,
                MaxPathSearchRange = null
            };
        }

        public override readonly bool Equals(object obj) =>
            obj is FlowFieldPathRequest other && Equals(other);

        public readonly bool Equals(FlowFieldPathRequest other) => RequestCacheKey == other.RequestCacheKey;

        public override readonly int GetHashCode()
        {
            // Note: For FlowFields we don't care about the start node (only that the FlowField contains it)
            return (
                End?.SpawnToken ?? 0,
                UnitSize,
                AllowUnwalkable,
                FieldSearchRange,
                MaxPathSearchRange ?? -1
            ).CombineHashCodes();
        }
    }
}

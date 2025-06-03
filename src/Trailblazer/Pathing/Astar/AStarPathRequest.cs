using FixedMathSharp;
using GridForge.Grids;
using System;

namespace Trailblazer.Pathing
{
    /// <summary>
    /// A pathfinding request used for A* trail generation, including options for climb height, heuristic weighting,
    /// and path smoothing. Implements value-based comparison and hashing for guide pooling.
    /// </summary>
    public struct AStarPathRequest : IPathRequest, IEquatable<AStarPathRequest>
    {
        public bool AllowUnwalkable { get; set; }

        public Node Start { get; set; }

        public Node End { get; set; }

        public readonly bool HasZeroDisplacement => Start == null || End == null || Start.SpawnToken == End.SpawnToken;

        public Fixed64 UnitSize { get; set; }

        public int? MaxPathSearchRange { get; set; }

        /// <summary>
        /// The maximum Y-axis height delta a unit can step or climb per node.
        /// Nodes exceeding this are ignored even if walkable and adjacent.
        /// </summary>
        public Fixed64 MaxClimbHeight { get; set; }

        public HeuristicMethod Heuristic { get; set; }

        /// <summary>
        /// Indicates whether a smoothing algorithm like spline interpolation should be applied to the final path.
        /// </summary>
        public bool UseSplineSmoothing { get; set; }

        public readonly int RequestCacheKey => GetHashCode();

        public static AStarPathRequest CreateEmpty() => Create(null, null);

        public static AStarPathRequest Create(
            Node start, 
            Node end, 
            Fixed64? unitSize = null, 
            HeuristicMethod heuristic = HeuristicMethod.Manhattan, 
            bool allowUnwalkable = false)
        {
            return new AStarPathRequest
            {
                Start = start,
                End = end,
                UnitSize = unitSize ?? GlobalGridManager.NodeSize,
                Heuristic = heuristic,
                AllowUnwalkable = allowUnwalkable,
                MaxClimbHeight = GlobalGridManager.NodeSize,
                UseSplineSmoothing = false,
                MaxPathSearchRange = null
            };
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
                UseSplineSmoothing,
                MaxPathSearchRange ?? -1
            ).CombineHashCodes();
        }
    }
}

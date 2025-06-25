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
    public class FlowFieldPathRequest : PathRequest, IEquatable<FlowFieldPathRequest>
    {
        public const int DefaultExtraFloodRange = 10;
   
        /// <summary>
        /// Limits how much extra distance the flood will expand after the target is reached.
        /// </summary>
        public int ExtraFloodRange { get; set; }

        public static readonly FlowFieldPathRequest DefaultRequest = new()
        {
            _startNode = null,
            _endNode = null,
            UnitSize = GlobalGridManager.VoxelSize,
            AllowUnwalkable = false,
            ExtraFloodRange = DefaultExtraFloodRange,
            MaxPathSearchRange = null
        };

        public static FlowFieldPathRequest Create(Vector3d origin, Vector3d destination)
        {
            return Create(origin, destination, GlobalGridManager.VoxelSize);
        }

        public static FlowFieldPathRequest Create(
            Vector3d origin,
            Vector3d destination,
            Fixed64 unitSize,
            bool allowUnwalkable = false)
        {
            if (!VoxelFinder.TryGetPathEdgeVoxels(origin, destination, out Voxel startNode, out Voxel endNode, unitSize))
                return DefaultRequest;

            FlowFieldPathRequest request = new()
            {
                _startNode = startNode,
                _endNode = endNode,
                UnitSize = unitSize,
                AllowUnwalkable = allowUnwalkable,
                ExtraFloodRange = DefaultExtraFloodRange,
                MaxPathSearchRange = null
            };

            return request.Validate() ? request : DefaultRequest;
        }

        public override bool Equals(object obj) =>
            obj is FlowFieldPathRequest other && Equals(other);

        public bool Equals(FlowFieldPathRequest other) => RequestCacheKey == other.RequestCacheKey;

        public override int GetHashCode()
        {
            // Note: For FlowFields we don't care about the start voxel (only that the FlowField contains it)
            return (
                EndNode?.SpawnToken ?? 0,
                UnitSize,
                AllowUnwalkable,
                ExtraFloodRange,
                MaxPathSearchRange ?? -1
            ).CombineHashCodes();
        }
    }
}

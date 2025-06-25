using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System;
using System.Net;

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
    public class AStarPathRequest : PathRequest, IEquatable<AStarPathRequest>
    {      
        /// <summary>
        /// The maximum Y-axis height delta a unit can step or climb per voxel.
        /// Voxels exceeding this are ignored even if walkable and adjacent.
        /// </summary>
        public Fixed64 MaxClimbHeight { get; set; }

        public HeuristicMethod Heuristic { get; set; }

        public static readonly AStarPathRequest DefaultRequest = new()
        {
            _startNode = null,
            _endNode = null,
            UnitSize = GlobalGridManager.VoxelSize,
            Heuristic = HeuristicMethod.Manhattan,
            AllowUnwalkable = false,
            MaxClimbHeight = GlobalGridManager.VoxelSize,
            MaxPathSearchRange = null
        };

        public static AStarPathRequest Create(Vector3d origin,Vector3d destination)
        {
            return Create(origin, destination, GlobalGridManager.VoxelSize);
        }

        public static AStarPathRequest Create(
            Vector3d origin,
            Vector3d destination,
            Fixed64 unitSize,
            HeuristicMethod heuristic = HeuristicMethod.Manhattan,
            bool allowUnwalkable = false)
        {
            if (!VoxelFinder.TryGetPathEdgeVoxels(origin, destination, out Voxel startNode, out Voxel endNode, unitSize))
                return DefaultRequest;

            AStarPathRequest request = new()
            {
                _startNode = startNode,
                _endNode = endNode,
                UnitSize = unitSize,
                Heuristic = heuristic,
                AllowUnwalkable = allowUnwalkable,
                MaxClimbHeight = GlobalGridManager.VoxelSize,
                MaxPathSearchRange = null
            };

            return request.Validate() ? request : DefaultRequest;
        }
   
        public override bool Equals(object obj) =>
            obj is AStarPathRequest other && Equals(other);

        public bool Equals(AStarPathRequest other) => RequestCacheKey == other.RequestCacheKey;

        public override int GetHashCode()
        {
            return (
                StartNode?.SpawnToken ?? 0,
                EndNode?.SpawnToken ?? 0,
                UnitSize,
                AllowUnwalkable,
                Heuristic,
                MaxClimbHeight,
                MaxPathSearchRange ?? -1
            ).CombineHashCodes();
        }     
    }
}

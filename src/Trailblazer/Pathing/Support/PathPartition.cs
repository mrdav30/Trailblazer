using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Pool;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing
{
    /// <summary>
    /// Represents a partition attached to a Voxel that provides additional data used during pathfinding,
    /// such as clearance information, movement cost, and neighbor traversal helpers.
    /// </summary>
    public class PathPartition : IVoxelPartition
    {
        #region Constants

        /// <summary>
        /// Cost applied for straight (orthogonal) pathfinding moves.
        /// </summary>
        public const int StraightCost = 100;

        /// <summary>
        /// Cost applied for diagonal pathfinding moves.
        /// </summary>
        public const int DiagonalCost = 141;

        /// <summary>
        /// Default value indicating unlimited clearance in degrees.
        /// </summary>
        public static readonly byte DefaultDegree = byte.MaxValue;

        /// <summary>
        /// Maximum clearance degree allowed for valid traversal.
        /// </summary>
        public static readonly byte DefaultDegreeCap = 8;

        private static readonly Lazy<SwiftQueuePool<(PathPartition v, byte dist)>> _clearanceQueuePool =
            new(() => new SwiftQueuePool<(PathPartition v, byte dist)>());

        internal static SwiftQueuePool<(PathPartition v, byte dist)> ClearanceQueuePool => _clearanceQueuePool.Value;

        #endregion

        /// <summary>
        /// The global coordinate of the voxel this partition is attached to.
        /// </summary>
        public GlobalVoxelIndex GlobalIndex { get; set; }

        /// <summary>
        /// The spawn token that uniquely identifies this voxel.
        /// </summary>
        public int VoxelToken { get; private set; }

        /// <summary>
        /// The world-space position of the voxel.
        /// </summary>
        public Vector3d VoxelPosition { get; private set; }

        public bool IsWalkable { get; private set; }

        /// <summary>
        /// Indicates whether the voxel has been partitioned and is in use.
        /// </summary>
        public bool IsPartitioned { get; set; }

        /// <summary>
        /// The combined cost for use in pathfinding heap prioritization.
        /// </summary>
        [Transient]
        public int PathCost { get; set; } = int.MaxValue;

#nullable enable
        public PathPartition?[]? Neighbors { get; private set; }
#nullable disable

        #region Clearance Properties

        /// <summary>
        /// The number of traversable connections until the nearest unwalkable voxel.
        /// </summary>
        public byte ClearanceDegree { get; private set; }

        /// <summary>
        /// Indicates whether the clearance degree has been computed and is valid.
        /// </summary>
        public bool IsClearanceValid { get; private set; }

        #endregion

        #region Chart Properties

        /// <summary>
        /// Maps that currently include this partition as part of their traversable space.
        /// </summary>
        private readonly SwiftHashSet<string> _chartOwners = new();

        ///<inheritdoc cref="_chartOwners"/>
        public SwiftHashSet<string> ChartOwners => _chartOwners;

        /// <summary>
        /// Returns true if any map currently references this partition.
        /// </summary>
        public bool HasAnyOwners => _chartOwners.Count > 0;

        #endregion

        /// <summary>
        /// Called when this partition is attached to a voxel, initializing key references and state.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnAddToVoxel(Voxel voxel)
        {
            voxel.OnObstacleChange += HandleChange;

            GlobalIndex = voxel.GlobalIndex;
            VoxelToken = voxel.SpawnToken;
            VoxelPosition = voxel.WorldPosition;

            IsWalkable = !voxel.IsBlocked;

            ClearanceDegree = DefaultDegree;

            IsPartitioned = true;
        }

        /// This will call <see cref="Reset"/> as an action on release
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnRemoveFromVoxel(Voxel voxel)
        {
            voxel.OnObstacleChange -= HandleChange;
            PathManager.PartitionPool.Release(this);
        }

        public void BindNeighbors()
        {
#nullable enable
            Neighbors = new PathPartition?[26];
#nullable disable

            GlobalGridManager.TryGetGridAndVoxel(GlobalIndex, out _, out var voxel);

            // for each of the 26 LinearDirection values (except None)
            foreach (LinearDirection dir in PathManager.AllDirections)
            {
                // use Voxel’s cached neighbor lookup
                if (voxel.TryGetNeighborFromDirection(dir, out var neighborVoxel, useCache: true)
                 && neighborVoxel.TryGetPartition(out PathPartition neighborPart))
                {
                    Neighbors[(int)dir] = neighborPart;
                }
                // else leave null = “blocked or missing”
            }
        }

        /// <summary>
        /// Resets this partition's internal state, preparing it for reuse or reattachment.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Reset()
        {
            GlobalIndex = default;
            VoxelToken = 0;

            IsClearanceValid = false;

            IsWalkable = false;

            Neighbors = null;

            ClearanceDegree = DefaultDegree;

            _chartOwners.Clear();

            IsPartitioned = false;
        }

        /// <summary>
        /// Handles any obstacle changes on the associated voxel and invalidates clearance as needed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void HandleChange(GridChange changeType, Voxel voxel)
        {
            // regardless of change type, we need to update clearance

            IsWalkable = !voxel.IsBlocked;
            ClearanceDegree = DefaultDegree;
            IsClearanceValid = false;
        }

        /// <summary>
        /// Returns the cached or recalculated clearance value to nearby obstacles.
        /// </summary>
        public byte GetNeighborClearance()
        {
            CheckClearance();
            return ClearanceDegree;
        }

        /// <summary>
        /// If this unit is too fat to fit.
        /// </summary>
        internal bool IsImpassable(Fixed64 unitSize)
        {
            if (unitSize <= Fixed64.Zero)
                return false;

            CheckClearance();

            // How many voxels wide our agent is, in cell terms
            int required = (unitSize / GlobalGridManager.VoxelSize).CeilToInt();
            // If there aren't at least that many free voxels around, it can't go
            return required > ClearanceDegree;
        }

        /// <summary>
        /// Validates or recalculates the clearance degree from nearby voxels.
        /// </summary>
        private void CheckClearance()
        {
            if (Neighbors == null)
                throw new InvalidOperationException("Must call BindNeighbors() before clearance.");

            if (IsClearanceValid) return;
            IsClearanceValid = true;

            if (!GlobalGridManager.TryGetGridAndVoxel(GlobalIndex, out _, out Voxel origin)
             || !IsWalkable)
            {
                ClearanceDegree = origin?.IsBlocked == true ? (byte)0 : DefaultDegreeCap;
                return;
            }

            // BFS from this voxel until we hit any blocked-or-missing neighbor
            byte best = DefaultDegreeCap;
            SwiftQueue<(PathPartition v, byte dist)> q = ClearanceQueuePool.Rent();
            SwiftHashSet<PathPartition> visited = PathManager.PartitionSetPool.Rent();

            q.Enqueue((this, 0));
            visited.Add(this);

            // stop BFS either when queue empty or we’ve already found best=1
            while (q.Count > 0 && best > 1)
            {
                (PathPartition part, byte dist) = q.Dequeue();

                // any neighbor that’s missing or blocked → candidate = dist+1
                for (int i = 0; i < part.Neighbors.Length; i++)
                {
                    byte nextDist = (byte)(dist + 1);
                    PathPartition nPart = part.Neighbors[i];

                    // 1) missing or blocked → candidate radius = nextDist
                    if (nPart == null || !nPart.IsWalkable)
                    {
                        best = Math.Min(best, nextDist);
                        continue;
                    }

                    // 2) otherwise, keep exploring *only* up to your cap
                    if (nextDist < best
                     && nextDist < DefaultDegreeCap
                     && visited.Add(nPart))
                    {
                        q.Enqueue((nPart, nextDist));
                    }
                }
            }

            // clamp to cap so you never return > DefaultDegreeCap
            ClearanceDegree = (byte)Math.Min(best, DefaultDegreeCap);

            ClearanceQueuePool.Release(q);
            PathManager.PartitionSetPool.Release(visited);
        }

        #region TraversableNavMap Management

        /// <summary>
        /// Registers the map name as one that owns this partition.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddOwner(string mapName) => _chartOwners.Add(mapName);

        /// <summary>
        /// Removes the map name from those that reference this partition.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveOwner(string mapName) => _chartOwners.Remove(mapName);

        /// <summary>
        /// Returns true if the partition is claimed by the given map name.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool BelongsTo(string mapName) => _chartOwners.Contains(mapName);

        #endregion

        /// <summary>
        /// Calculates the heuristic cost for the current voxel based on the target voxel and the heuristic method used.
        /// This implementation takes into account the X, Y, and Z axes for pathfinding.
        /// </summary>
        public static int CalculateHeuristic(
            Vector3d currentVoxel,
            Vector3d targetVoxel,
            HeuristicMethod heuristicMethod)
        {
            Fixed64 heuristicCost = Fixed64.MAX_VALUE;

            // Calculate the absolute distance in each axis
            Vector3d dst = Vector3d.Abs(currentVoxel - targetVoxel);

            switch (heuristicMethod)
            {
                case HeuristicMethod.Manhattan:
                    // Sum the distances and multiply by 100 for the heuristic cost
                    heuristicCost = (dst.x + dst.y + dst.z) * StraightCost;
                    break;
                case HeuristicMethod.Octile:
                    // Find the max of the three distances
                    Fixed64 maxXY = FixedMath.Max(dst.x, dst.y);
                    Fixed64 max = FixedMath.Max(maxXY, dst.z);
                    // Calculate the heuristic cost using the max and sum of other distances
                    heuristicCost = (max * DiagonalCost) + ((dst.x + dst.y + dst.z - max - max) * StraightCost);
                    break;
                case HeuristicMethod.Euclidean:
                    // Calculate the squared distance and find the square root
                    Fixed64 d = dst.x * dst.x + dst.y * dst.y + dst.z * dst.z;
                    d = FixedMath.Sqrt(d);
                    // Multiply the result by 100 for the heuristic cost
                    heuristicCost = d * StraightCost;
                    break;
                default:
                    break;
            }

            return heuristicCost.CeilToInt();
        }

        public override int GetHashCode() => VoxelToken;
    }
}

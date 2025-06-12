using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using System;
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
        public static readonly Fixed64 DefaultDegree = Fixed64.MAX_VALUE;

        /// <summary>
        /// Maximum clearance degree allowed for valid traversal.
        /// </summary>
        public static readonly Fixed64 DefaultDegreeCap = (Fixed64)8;

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

        /// <summary>
        /// Indicates whether the voxel has been partitioned and is in use.
        /// </summary>
        public bool IsPartitioned { get; set; }

        /// <summary>
        /// The combined cost for use in pathfinding heap prioritization.
        /// </summary>
        [Transient]
        public int PathCost { get; set; }

        #region Clearance Properties

        /// <summary>
        /// The direction used when calculating neighbor clearance.
        /// </summary>
        public LinearDirection ClearanceDirection { get; private set; }

        /// <summary>
        /// The number of traversable connections until the nearest unwalkable voxel.
        /// </summary>
        public Fixed64 ClearanceDegree { get; private set; }

        /// <summary>
        /// Indicates whether the clearance degree has been computed and is valid.
        /// </summary>
        public bool IsClearanceValid { get; private set; }

        #endregion
       
        /// <summary>
        /// Maps that currently include this partition as part of their traversable space.
        /// </summary>
        private readonly SwiftHashSet<string> _mapOwners = new();

        ///<inheritdoc cref="_mapOwners"/>
        public SwiftHashSet<string> MapOwners => _mapOwners;

        /// <summary>
        /// Returns true if any map currently references this partition.
        /// </summary>
        public bool HasAnyOwners => _mapOwners.Count > 0;

        /// <summary>
        /// Called when this partition is attached to a voxel, initializing key references and state.
        /// </summary>
        public void OnAddToVoxel(Voxel voxel)
        {
            voxel.OnObstacleChange += HandleChange;

            GlobalIndex = voxel.GlobalIndex;
            VoxelToken = voxel.SpawnToken;
            VoxelPosition = voxel.WorldPosition;

            ClearanceDegree = Fixed64.MAX_VALUE;
            ClearanceDirection = LinearDirection.None;

            IsPartitioned = true;
        }

        /// This will call <see cref="Reset"/> as an action on release
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void OnRemoveFromVoxel(Voxel voxel)
        {
            voxel.OnObstacleChange -= HandleChange;
            PathManager.PartitionPool.Release(this);
        }

        /// <summary>
        /// Resets this partition's internal state, preparing it for reuse or reattachment.
        /// </summary>
        public void Reset()
        {
            GlobalIndex = default;
            VoxelToken = 0;

            IsClearanceValid = false;

            ClearanceDegree = DefaultDegree;
            ClearanceDirection = LinearDirection.None;

            _mapOwners.Clear();

            IsPartitioned = false;
        }

        /// <summary>
        /// Handles any obstacle changes on the associated voxel and invalidates clearance as needed.
        /// </summary>
        public void HandleChange(GridChange changeType, Voxel voxel)
        {
            // regardless of change type, we need to update clearance

            IsClearanceValid = false;
            CheckNeighborClearance();
        }

        /// <summary>
        /// If this unit is too fat to fit.
        /// </summary>
        internal bool Unpassable(Fixed64 size)
        {
            if (size <= Fixed64.Zero) return false;

            //  If there's an unwalkable within the size's number of connections, the unit cannot pass
            CheckNeighborClearance();
            return size > ClearanceDegree;
        }

        /// <summary>
        /// Returns the cached or recalculated clearance value to nearby obstacles.
        /// </summary>
        public Fixed64 GetNeighborClearance()
        {
            CheckNeighborClearance();
            return ClearanceDegree;
        }

        /// <summary>
        /// Validates or recalculates the clearance degree from nearby voxels.
        /// </summary>
        private void CheckNeighborClearance()
        {
            if (IsClearanceValid)
                return;

            if (!GlobalGridManager.TryGetGridAndVoxel(GlobalIndex, out _, out Voxel voxel))
            {
                Console.WriteLine($"Invalidate coordiante provided to setup partition: {GlobalIndex}");
                return;
            }

            if (voxel.IsBlocked)
            {
                ClearanceDegree = Fixed64.Zero;
                ClearanceDirection = LinearDirection.None;
                IsClearanceValid = true;
                return;
            }

            //  refresh source in case the map changed
            if (voxel.TryGetNeighborFromDirection(ClearanceDirection, out Voxel source)
                && source.TryGetPartition(out PathPartition sourcePartition))
            {
                Fixed64 prevSourceDegree = sourcePartition.ClearanceDegree;
                if (sourcePartition.ClearanceDegree < ClearanceDegree)
                {
                    sourcePartition.CheckNeighborClearance();

                    if (sourcePartition.ClearanceDegree != prevSourceDegree)
                    {
                        // Clearance from direction can no longer be trusted!
                        ClearanceDegree = DefaultDegree;
                        ClearanceDirection = LinearDirection.None;
                    }
                }
                else
                    ClearanceDegree = sourcePartition.ClearanceDegree + Fixed64.One;
            }

            //This method isn't always 100% accurate but after several updates, it will have a better picture of the map
            //TODO: Test this thoroughly and visualize
            foreach (LinearDirection direction in Enum.GetValues(typeof(LinearDirection)))
            {
                if (!voxel.TryGetNeighborFromDirection(direction, out Voxel neighbor)
                    || neighbor.IsBlocked
                    || !neighbor.TryGetPartition(out PathPartition neighborPartition))
                {
                    ClearanceDegree = Fixed64.One;
                    ClearanceDirection = direction;
                    break;
                }

                if (neighborPartition.ClearanceDegree < ClearanceDegree
                    && neighborPartition.ClearanceDegree < DefaultDegreeCap)
                {
                    //  Cap clearance to 8. Something larger than that won't work very well with pathfinding.
                    ClearanceDegree = neighborPartition.ClearanceDegree + Fixed64.One;
                    ClearanceDirection = direction;
                }
            }

            IsClearanceValid = true;
        }

        #region TraversableNavMap Management

        /// <summary>
        /// Registers the map name as one that owns this partition.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void AddOwner(string mapName) => _mapOwners.Add(mapName);

        /// <summary>
        /// Removes the map name from those that reference this partition.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RemoveOwner(string mapName) => _mapOwners.Remove(mapName);

        /// <summary>
        /// Returns true if the partition is claimed by the given map name.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool BelongsTo(string mapName) => _mapOwners.Contains(mapName);

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

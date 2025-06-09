using FixedMathSharp;
using GridForge.Grids;
using GridForge.Utility;
using SwiftCollections;

namespace Trailblazer.Pathing
{
    public static class VoxelFinder
    {
        // set to the highest height or width value of any game object
        private const int _maxTestDistance = 3;

        public static bool TryGetPathEdgeVoxels(Vector3d startPosition, Vector3d targetPosition, out Voxel startVoxel, out Voxel targetVoxel)
        {
            targetVoxel = default;
            if (!GlobalGridManager.TryGetGridAndVoxel(startPosition, out _, out startVoxel))
                return false;

            if (startVoxel.IsBlocked)
            {
                if (!TryGetClosestWalkableNeighbor(startVoxel, out Voxel closestNeighbor))
                    return false;
                startVoxel = closestNeighbor;
            }

            if (!GlobalGridManager.TryGetGridAndVoxel(targetPosition, out _, out targetVoxel))
                return false;

            if (targetVoxel.IsBlocked)
            {
                if (!TryGetClosestWalkableNeighbor(targetVoxel, out Voxel closestNeighbor))
                    return false;
                targetVoxel = closestNeighbor;
            }

            return true;
        }

        public static bool TryGetClosestWalkableNeighbor(Voxel voxel, out Voxel closestNeighbor)
        {
            closestNeighbor = null;

            foreach (TraversableVoxel neighbor in PathManager.WalkableStraightNeighborsOf(voxel))
            {
                closestNeighbor = neighbor.Voxel; // prefer straight neighbors since they cost less
                return true;
            }

            foreach (TraversableVoxel neighbor in PathManager.WalkableDiagonalNeighborsOf(voxel))
            {
                closestNeighbor = neighbor.Voxel;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Finds closest next-best-voxel also when destination is off invalid
        /// </summary>
        public static bool GetEndVoxel(Vector3d from, Vector3d destination, out Voxel destinationVoxel, bool allowUnwalkable = false)
        {
            if (!GlobalGridManager.TryGetGridAndVoxel(destination, out _, out destinationVoxel))
            {
                // If null, it is off the grid. Raycast back onto grid for closest viable voxel to the destination.
                foreach (GridVoxelSet gridVoxelSet in GridTracer.TraceLine(destination, from))
                {
                    foreach (Voxel voxel in gridVoxelSet.Voxels)
                    {
                        // A path is required if a voxel doesn't exist in the traced line
                        if (!allowUnwalkable && voxel.IsBlocked || !voxel.TryGetPartition<PathPartition>(out _))
                            continue;

                        destinationVoxel = voxel;
                        return true;
                    }
                }

                return false;
            }

            if (destinationVoxel.IsBlocked)
            {
                if (allowUnwalkable && TryGetClosestWalkableNeighbor(destinationVoxel, out _))
                    return true;

                return StarCast(destination, out destinationVoxel);
            }

            return true;
        }

        /// <summary>
        /// Finds closest next-best-voxel
        /// </summary>
        public static bool GetStartVoxel(Vector3d from, Vector3d destination, out Voxel startVoxel, bool allowUnwalkable = false)
        {
            if (!GlobalGridManager.TryGetGridAndVoxel(from, out _, out startVoxel))
            {
                // If null, it is off the grid. Raycast back onto grid for closest viable voxel to the destination.
                foreach (GridVoxelSet gridVoxelSet in GridTracer.TraceLine(from, destination))
                {
                    foreach (Voxel voxel in gridVoxelSet.Voxels)
                    {
                        // A path is required if a voxel doesn't exist in the traced line
                        if (!allowUnwalkable && voxel.IsBlocked || !voxel.TryGetPartition<PathPartition>(out _))
                            continue;

                        startVoxel = voxel;
                        return true;
                    }
                }

                return false;
            }

            if (startVoxel.IsBlocked)
            {
                if (allowUnwalkable && TryGetClosestWalkableNeighbor(startVoxel, out _))
                    return true;

                return StarCast(from, out startVoxel);
            }

            return true;
        }

        public static bool StarCast(Vector3d targetPosition, out Voxel returnVoxel)
        {
            returnVoxel = null;
            if (!GlobalGridManager.TryGetGrid(targetPosition, out VoxelGrid outGrid))
                return false; // no grid found at this position!

            AlternativeVoxelFinder.Instance.SetQuery(targetPosition, outGrid.BoundsMin, _maxTestDistance);

            if (!AlternativeVoxelFinder.Instance.GetVoxel(out returnVoxel))
                return false;

            return true;
        }

        public static bool GetClosestVoxelForSize(
            Vector3d from, 
            Vector3d destination, 
            Fixed64 pathingSize, 
            out Voxel returnVoxel, 
            bool allowUnwalkable = false)
        {
            if (GlobalGridManager.TryGetGridAndVoxel(from, out _, out returnVoxel)
                && (!returnVoxel.IsBlocked || allowUnwalkable)
                && returnVoxel.TryGetPartition(out PathPartition returnPartition)
                && !returnPartition.Unpassable(pathingSize))
            {
                return true;
            }

            foreach (GridVoxelSet gridVoxelSet in GridTracer.TraceLine(from, destination))
            {
                foreach (Voxel currentVoxel in gridVoxelSet.Voxels)
                {
                    // A path is required if a voxel doesn't exist in the traced line
                    if (!allowUnwalkable && currentVoxel.IsBlocked || !currentVoxel.TryGetPartition(out PathPartition currentPartition))
                        continue;

                    if (!currentPartition.Unpassable(pathingSize))
                    {
                        returnVoxel = currentVoxel;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
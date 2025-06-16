using FixedMathSharp;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing
{
    /// <summary>
    /// Represents a 3D navigable grid used for pathfinding. Provides utility methods for querying walkability 
    /// and converting world positions into discrete grid indices.
    /// </summary>
    [Serializable]
    public class NavigationChart
    {
        /// <summary>
        /// The name identifier for this navigation chart.
        /// </summary>
        public readonly string Name;

        /// <summary>
        /// The minimum world-space bounds of the grid. Determines the starting point for grid indexing.
        /// </summary>
        public readonly Vector3d MinBounds;

        /// <summary>
        /// The maximum world-space bounds of the grid, computed as MinBounds + grid size * Interval.
        /// Represents the exclusive upper bound of the grid.
        /// </summary>
        public readonly Vector3d MaxBounds;

        /// <summary>
        /// The distance between grid points along each axis.
        /// </summary>
        public readonly Fixed64 Interval;

        /// <summary>
        /// The number of cells along the X axis.
        /// </summary>
        public readonly int SizeX;

        /// <summary>
        /// The number of cells along the Y axis.
        /// </summary>
        public readonly int SizeY;

        /// <summary>
        /// The number of cells along the Z axis.
        /// </summary>
        public readonly int SizeZ;

        /// <summary>
        /// A flattened 3D boolean map representing walkable (true) or unwalkable (false) cells.
        /// The array is indexed using a custom row-major layout across Y, X, then Z.
        /// </summary>
        private readonly bool[] _map;

        /// <summary>
        /// Indicates whether this chart has been fully initialized and is ready for queries.
        /// </summary>
        public bool IsInitialized { get; internal set; }

        /// <summary>
        /// Creates a new navigation chart using a pre-flattened map array and spatial parameters.
        /// </summary>
        /// <param name="name">The chart's unique identifier.</param>
        /// <param name="map">A flattened boolean array representing walkable and non-walkable grid cells.</param>
        /// <param name="sizeX">Number of cells along the X axis.</param>
        /// <param name="sizeY">Number of cells along the Y axis.</param>
        /// <param name="sizeZ">Number of cells along the Z axis.</param>
        /// <param name="minBounds">The minimum world-space bounds of the grid.</param>
        /// <param name="maxBounds">The maximum world-space bounds of the grid.</param>
        /// <param name="interval">Distance between adjacent grid points.</param>
        public NavigationChart(
            string name,
            bool[] map,
            int sizeX,
            int sizeY,
            int sizeZ,
            Vector3d minBounds,
            Vector3d maxBounds,
            Fixed64 interval)
        {
            Name = name;
            _map = map;
            SizeX = sizeX;
            SizeY = sizeY;
            SizeZ = sizeZ;
            MinBounds = minBounds;
            MaxBounds = maxBounds;
            Interval = interval;
        }

        /// <summary>
        /// Converts 3D grid indices (x, y, z) into a 1D flattened index for accessing the internal map.
        /// </summary>
        /// <param name="x">The X index in the grid.</param>
        /// <param name="y">The Y index in the grid.</param>
        /// <param name="z">The Z index in the grid.</param>
        /// <returns>The flattened index corresponding to the provided 3D coordinates.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ToIndex(int x, int y, int z) => (y * SizeX * SizeZ) + (x * SizeZ) + z;

        /// <summary>
        /// Attempts to convert a world-space position into the grid's local indices.
        /// </summary>
        /// <param name="pos">The world-space position.</param>
        /// <param name="x">Resulting X index.</param>
        /// <param name="y">Resulting Y index.</param>
        /// <param name="z">Resulting Z index.</param>
        /// <returns>True if the position is within bounds; otherwise, false.</returns>
        public bool TryWorldToIndex(Vector3d pos, out int x, out int y, out int z)
        {
            x = (int)((pos.x - MinBounds.x) / Interval);
            y = (int)((pos.y - MinBounds.y) / Interval);
            z = (int)((pos.z - MinBounds.z) / Interval);

            bool valid = x >= 0 && x < SizeX &&
                         y >= 0 && y < SizeY &&
                         z >= 0 && z < SizeZ;

            if (!valid)
            {
                x = y = z = -1;
                return false;
            }

            return true;
        }

        /// <summary>
        /// Checks if the given world-space position corresponds to a walkable cell.
        /// </summary>
        /// <param name="worldPos">The position to query.</param>
        /// <returns>True if walkable; otherwise, false.</returns>
        public bool IsWalkable(Vector3d worldPos)
        {
            if (!TryWorldToIndex(worldPos, out int x, out int y, out int z))
                return false;

            return _map[ToIndex(x, y, z)];
        }

        /// <summary>
        /// Returns all walkable world positions within the chart.
        /// </summary>
        /// <returns>A collection of walkable Vector3d positions.</returns>
        public IEnumerable<Vector3d> GetWalkablePositions()
        {
            for (int y = 0; y < SizeY; y++)
                for (int x = 0; x < SizeX; x++)
                    for (int z = 0; z < SizeZ; z++)
                    {
                        if (_map[ToIndex(x, y, z)])
                        {
                            yield return new Vector3d(
                                MinBounds.x + x * Interval,
                                MinBounds.y + y * Interval,
                                MinBounds.z + z * Interval
                            );
                        }
                    }
        }

        /// <summary>
        /// Creates a navigation chart from a 3D boolean array representing walkable voxels.
        /// </summary>
        /// <param name="name">Name identifier for the chart.</param>
        /// <param name="sourceMap">3D map of walkable cells (true = walkable).</param>
        /// <param name="minBounds">The minimum world-space bounds of the grid.</param>
        /// <param name="interval">The spacing between each grid point.</param>
        /// <returns>A constructed NavigationChart instance.</returns>
        public static NavigationChart From3D(
            string name, 
            bool[,,] sourceMap, 
            Vector3d minBounds, 
            Fixed64 interval)
        {
            int sizeY = sourceMap.GetLength(0);
            int sizeX = sourceMap.GetLength(1);
            int sizeZ = sourceMap.GetLength(2);

            Vector3d maxBounds = minBounds + new Vector3d(
                sizeX * interval,
                sizeY * interval,
                sizeZ * interval
            );

            var flat = new bool[sizeX * sizeY * sizeZ];
            for (int y = 0; y < sizeY; y++)
                for (int x = 0; x < sizeX; x++)
                    for (int z = 0; z < sizeZ; z++)
                        flat[(y * sizeX * sizeZ) + (x * sizeZ) + z] = sourceMap[y, x, z];

            return new NavigationChart(name, flat, sizeX, sizeY, sizeZ, minBounds, maxBounds, interval);
        }
    }
}

using FixedMathSharp;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// TODO: add conversion to heightmap?
namespace Trailblazer.Pathing
{
    [Serializable]
    public class PathNavigationMap
    {
        public readonly string Name;

        public readonly Vector3d Origin;
        public readonly Fixed64 Interval;
        public readonly int SizeX, SizeY, SizeZ;

        private readonly bool[] _map;

        public bool IsInitialized { get; internal set; }

        public PathNavigationMap(string name, bool[] map, int sizeX, int sizeY, int sizeZ, Vector3d origin, Fixed64 interval)
        {
            Name = name;
            _map = map;
            SizeX = sizeX;
            SizeY = sizeY;
            SizeZ = sizeZ;
            Origin = origin;
            Interval = interval;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ToIndex(int x, int y, int z) => (y * SizeX * SizeZ) + (x * SizeZ) + z;

        public bool TryWorldToIndex(Vector3d pos, out int x, out int y, out int z)
        {
            x = (int)((pos.x - Origin.x) / Interval);
            y = (int)((pos.y - Origin.y) / Interval);
            z = (int)((pos.z - Origin.z) / Interval);

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

        public bool IsWalkable(Vector3d worldPos)
        {
            if (!TryWorldToIndex(worldPos, out int x, out int y, out int z))
                return false;

            return _map[ToIndex(x, y, z)];
        }

        public IEnumerable<Vector3d> GetWalkablePositions()
        {
            for (int y = 0; y < SizeY; y++)
                for (int x = 0; x < SizeX; x++)
                    for (int z = 0; z < SizeZ; z++)
                    {
                        if (_map[ToIndex(x, y, z)])
                        {
                            yield return new Vector3d(
                                Origin.x + x * Interval,
                                Origin.y + y * Interval,
                                Origin.z + z * Interval
                            );
                        }
                    }
        }

        public static PathNavigationMap From3D(string name, bool[,,] sourceMap, Vector3d origin, Fixed64 interval)
        {
            int sizeY = sourceMap.GetLength(0);
            int sizeX = sourceMap.GetLength(1);
            int sizeZ = sourceMap.GetLength(2);

            var flat = new bool[sizeX * sizeY * sizeZ];
            for (int y = 0; y < sizeY; y++)
                for (int x = 0; x < sizeX; x++)
                    for (int z = 0; z < sizeZ; z++)
                        flat[(y * sizeX * sizeZ) + (x * sizeZ) + z] = sourceMap[y, x, z];

            return new PathNavigationMap(name, flat, sizeX, sizeY, sizeZ, origin, interval);
        }
    }
}

using FixedMathSharp;
using GridForge.Configuration;
using System;

namespace Trailblazer.Benchmarks;

internal static class BenchmarkScenarioFactory
{
    public static GridConfiguration[] CreateTiledFlatGridConfigurations(
        int tilesX,
        int tilesZ,
        int extent,
        int scanCellSize = GridConfiguration.DefaultScanCellSize,
        bool overlapBoundaries = false,
        int originX = 0,
        int originZ = 0)
    {
        GridConfiguration[] configurations = new GridConfiguration[tilesX * tilesZ];
        int step = overlapBoundaries ? extent : extent + 1;
        int index = 0;

        for (int z = 0; z < tilesZ; z++)
        {
            for (int x = 0; x < tilesX; x++)
            {
                int minX = originX + x * step;
                int minZ = originZ + z * step;

                configurations[index++] = new GridConfiguration(
                    new Vector3d(minX, 0, minZ),
                    new Vector3d(minX + extent, 0, minZ + extent),
                    scanCellSize);
            }
        }

        return configurations;
    }

    public static BenchmarkOccupant[] CreateOccupants(
        int count,
        int width,
        int depth,
        int y = 0,
        int groupCount = 8,
        int originX = 0,
        int originZ = 0)
    {
        BenchmarkOccupant[] occupants = new BenchmarkOccupant[count];

        int index = 0;
        int groupId = 0;

        for (int z = 0; z < depth && index < count; z++)
        {
            for (int x = 0; x < width && index < count; x++)
            {
                occupants[index++] = new BenchmarkOccupant(
                    new Vector3d(originX + x, y, originZ + z),
                    (byte)(groupId++ % groupCount));
            }
        }

        if (index != count)
            throw new InvalidOperationException($"Only generated {index} of {count} requested occupants.");

        return occupants;
    }
}

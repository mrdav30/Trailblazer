using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Provides extension methods for debugging and visualizing the layout of a TraversalAuthoringMap.
/// </summary>
public static class TraversalAuthoringMapExtensions
{
    /// <summary>
    /// Prints the token layout of a specific XZ plane in the source map for debugging purposes.
    /// </summary>
    /// <param name="map">The traversal authoring map to print.</param>
    /// <param name="yLevel">The Y level of the XZ plane to print.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if the specified Y level is out of bounds.</exception>
    public static void PrintXZPlane(this TraversalAuthoringMap map, int yLevel)
    {
        int minY = 0;
        int maxYExclusive = map.SourceMap.GetLength(0);
        if (yLevel < minY || yLevel >= maxYExclusive)
            throw new ArgumentOutOfRangeException(nameof(yLevel));

        Console.WriteLine($"Token XZ Plane at Y={yLevel} for Traversal Authoring Map [{map.ChartName}]:");

        int sizeX = map.SourceMap.GetLength(1);
        int sizeZ = map.SourceMap.GetLength(2);
        for (int z = 0; z < sizeZ; z++)
        {
            for (int x = 0; x < sizeX; x++)
            {
                string token = map.SourceMap[yLevel, x, z];
                Console.Write(string.IsNullOrWhiteSpace(token) ? ". " : $"{token} ");
            }

            Console.WriteLine();
        }
    }
}
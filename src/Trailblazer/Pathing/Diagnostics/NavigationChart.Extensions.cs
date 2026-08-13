//=======================================================================
// NavigationChart.Extensions.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>
/// Provides extension methods for displaying information about walkable positions and XZ plane slices in a NavigationChart.
/// </summary>
/// <remarks>
/// These extension methods are intended to assist with debugging or visualizing navigation data by printing walkable positions
/// and 2D slices of the navigation chart to the console.
/// </remarks>
public static class NavigationChartExtensions
{
    /// <summary>
    /// Prints all walkable positions in the specified navigation chart to the console.
    /// </summary>
    /// <remarks>
    /// Each walkable position is printed in the format (x, y, z) under a header containing the chart's name.
    /// This method is intended for debugging or informational purposes and writes output directly to the standard console.
    /// </remarks>
    /// <param name="chart">The navigation chart from which to retrieve and display walkable positions. Cannot be null.</param>
    public static void PrintWalkablePositions(this NavigationChart chart)
    {
        Console.WriteLine($"Walkable Positions for Chart [{chart.Name}]:");

        foreach (Vector3d pos in chart.GetWalkablePositions())
            Console.WriteLine($"  ({pos.X}, {pos.Y}, {pos.Z})");
    }

    /// <summary>
    /// Prints a visual representation of the XZ plane at the specified Y level for the given navigation chart to the console.
    /// </summary>
    /// <remarks>
    /// Each cell in the output represents whether the corresponding position is walkable.
    /// Walkable positions are indicated by 'O', and non-walkable positions by '.'.
    /// This method is intended for debugging or visualization purposes.
    /// </remarks>
    /// <param name="chart">The navigation chart from which to extract and display the XZ plane.</param>
    /// <param name="yLevel">The Y coordinate at which to display the XZ plane.</param>
    public static void PrintXZPlane(this NavigationChart chart, int yLevel)
    {
        Console.WriteLine($"XZ Plane at Y={yLevel} for Chart [{chart.Name}]:");

        for (int z = (int)chart.MinBounds.Z; z < (int)chart.MaxBounds.Z; z++)
        {
            for (int x = (int)chart.MinBounds.X; x < (int)chart.MaxBounds.X; x++)
            {
                Vector3d pos = new(x, yLevel, z);
                bool walkable = chart.IsWalkable(pos);
                Console.Write(walkable ? "O " : ". ");
            }
            Console.WriteLine();
        }
    }
}

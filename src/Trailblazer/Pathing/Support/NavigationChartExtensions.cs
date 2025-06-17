using FixedMathSharp;
using System;

namespace Trailblazer.Pathing
{
    public static class NavigationChartExtensions
    {
        public static void PrintWalkablePositions(this NavigationChart chart)
        {
            Console.WriteLine($"Walkable Positions for Chart [{chart.Name}]:");

            foreach (Vector3d pos in chart.GetWalkablePositions())
                Console.WriteLine($"  ({pos.x}, {pos.y}, {pos.z})");
        }

        public static void PrintXZPlane(this NavigationChart chart, int yLevel)
        {
            Console.WriteLine($"XZ Plane at Y={yLevel} for Chart [{chart.Name}]:");

            for (int z = (int)chart.MinBounds.z; z < (int)chart.MaxBounds.z; z++)
            {
                for (int x = (int)chart.MinBounds.x; x < (int)chart.MaxBounds.x; x++)
                {
                    Vector3d pos = new(x, yLevel, z);
                    bool walkable = chart.IsWalkable(pos);
                    Console.Write(walkable ? "O " : ". ");
                }
                Console.WriteLine();
            }
        }
    }
}

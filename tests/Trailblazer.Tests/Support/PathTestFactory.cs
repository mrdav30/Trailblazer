using FixedMathSharp;
using GridForge.Grids;
using Trailblazer.Pathing;

namespace Trailblazer.Tests
{
    public static class PathTestFactory
    {
        public static NavigationChart RegisterSingleWalkablePoint(string mapName, Vector3d pos)
        {
            Vector3d origin = pos - new Vector3d(1, 1, 1);
            bool[,,] data = new bool[3, 3, 3];
            data[1, 1, 1] = true;

            var map = NavigationChart.From3D(mapName, data, origin, Fixed64.One);
            PathManager.Register(map);
            PathManager.InitializeMap(mapName);
            return map;
        }

        public static NavigationChart RegisterFromData(string name, bool[,,] data, Vector3d origin)
        {
            var map = NavigationChart.From3D(name, data, origin, GlobalGridManager.NodeSize);
            PathManager.Register(map);
            PathManager.InitializeMap(name);
            return map;
        }

        public static NavigationChart BuildSinglePointMap(string name, Vector3d worldPos)
        {
            // Convert a single world point into an aligned map
            Vector3d origin = worldPos - new Vector3d(1, 1, 1);
            bool[,,] data = new bool[3, 3, 3];
            data[1, 1, 1] = true;

            return NavigationChart.From3D(name, data, origin, Fixed64.One);
        }
    }
}

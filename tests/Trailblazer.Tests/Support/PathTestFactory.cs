using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System;
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
            PathingManager.Register(map);
            PathingManager.InitializeMap(mapName);
            return map;
        }

        public static NavigationChart RegisterFromData(string name, bool[,,] data, Vector3d origin)
        {
            var map = NavigationChart.From3D(name, data, origin, GlobalGridManager.NodeSize);
            PathingManager.Register(map);
            PathingManager.InitializeMap(name);
            return map;
        }

        public static AStarPathRequest CreateRequest(Vector3d from, Vector3d to, Fixed64? unitSize = null, Action<bool, SwiftList<Vector3d>>? onComplete = null)
        {
            onComplete ??= (_, __) => { }; // noop
            return new AStarPathRequest(from, to, unitSize ?? Fixed64.One, onComplete)
            {
                Heuristic = HeuristicMethod.Manhattan,
                MaxClimbHeight = Fixed64.One,
                UseSplineSmoothing = false
            };
        }
    }

}

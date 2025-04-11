using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using SwiftCollections.Pool;
using System.Collections.Generic;
using System.Diagnostics;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation
{
    public static class TraversableNavMapManager
    {
        private static readonly SwiftDictionary<string, TraversableNavMap> _loadedMaps = new();

        public static IEnumerable<TraversableNavMap> AllMaps => _loadedMaps.Values;

        internal static readonly SwiftObjectPool<PathPartition> PartitionPool = new(
            () => new PathPartition(),
            actionOnRelease: partition => partition.Reset()
        );

        public static void Register(TraversableNavMap map)
        {
            if (_loadedMaps.ContainsKey(map.Name))
            {
                Debug.WriteLine($"Map named {map.Name} already exists!");
                return;
            }

            _loadedMaps.Add(map.Name, map);
        }

        public static bool TryGet(string name, out TraversableNavMap map) => _loadedMaps.TryGetValue(name, out map);

        public static void InitializeAllMaps()
        {
            foreach (TraversableNavMap navMap in AllMaps)
                InitializeMap(navMap.Name);
        }

        public static void InitializeMap(string name)
        {
            if (!_loadedMaps.TryGetValue(name, out TraversableNavMap map))
            {
                Debug.WriteLine($"Map named {map.Name} is not registered!");
                return;
            }

            if (map.IsInitialized)
                return;

            foreach (Vector3d pos in map.GetWalkablePositions())
            {
                if (!GlobalGridManager.TryGetGridAndNode(pos, out _, out Node node))
                    continue;

                if (!node.TryGetPartition<PathPartition>(out PathPartition partition))
                {
                    partition = PartitionPool.Rent();
                    node.TryAddPartition(partition);
                }

                partition.AddOwner(map.Name);
            }

            map.IsInitialized = true;
        }

        public static void UnloadAllMaps()
        {
            foreach (TraversableNavMap navMap in AllMaps)
                Unload(navMap.Name);
        }

        public static void Unload(string name)
        {
            if (!_loadedMaps.TryGetValue(name, out TraversableNavMap map))
            {
                Debug.WriteLine($"Map named {map.Name} is not registered!");
                return;
            }

            if (!map.IsInitialized)
                return;

            foreach (Vector3d position in map.GetWalkablePositions())
            {
                if (!GlobalGridManager.TryGetGridAndNode(position, out _, out Node node))
                    continue;

                if (node.TryGetPartition(out PathPartition partition) && partition.BelongsTo(name))
                {
                    partition.RemoveOwner(name);
                    if (!partition.HasAnyOwners)
                        node.TryRemovePartition<PathPartition>();
                }
            }

            _loadedMaps.Remove(name);
        }

        public static void ClearAll() => _loadedMaps.Clear();
    }
}

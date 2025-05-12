using FixedMathSharp;
using GridForge.Grids;
using GridForge.Utility;
using SwiftCollections.Pool;
using SwiftCollections;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing
{
    public static class PathingManager
    {
        private static readonly SwiftDictionary<string, PathNavigationMap> _loadedMaps = new();

        public static IEnumerable<PathNavigationMap> AllMaps => _loadedMaps.Values;

        internal static readonly SwiftObjectPool<PathPartition> PartitionPool = new(
            () => new PathPartition(),
            actionOnRelease: partition => partition.Reset()
        );

        public static bool _processingLock;

        public static bool ValidatePathRequest(IPathRequest pathRequest)
        {
            if (!GlobalGridManager.TryGetGridAndNode(pathRequest.FromPosition, out Grid fromGrid, out Node fromNode))
            {
                Console.WriteLine("Unable to find a valid start node for {startPos}");
                return false;
            }

            if (fromNode.IsBlocked || !fromNode.TryGetPartition<PathPartition>(out _))
            {
                if (!NodeFinder.TryGetClosestWalkableNeighbor(fromNode, out Node closestNeighbor))
                    return false;
                fromNode = closestNeighbor;
            }

            if (!GlobalGridManager.TryGetGridAndNode(pathRequest.TargetPosition, out Grid targetGrid, out Node targetNode))
            {
                Console.WriteLine("Unable to find a valid end node for {targetPos}");
                return false;
            }

            if (targetNode.IsBlocked || !targetNode.TryGetPartition<PathPartition>(out _))
            {
                if (!NodeFinder.TryGetClosestWalkableNeighbor(targetNode, out Node closestNeighbor))
                    return false;
                targetNode = closestNeighbor;
            }

            int maxSearchSize = fromGrid.SpawnToken == targetGrid.SpawnToken ? fromGrid.Size : fromGrid.Size + targetGrid.Size;
            pathRequest.SetValidatedNodeRequest(fromNode, targetNode, maxSearchSize);

            return true;
        }

        public static void RequestPath(IPathRequest request)
        {
            if (_processingLock)
                return;

            if (!request.IsValidated && !ValidatePathRequest(request))
                return;

            _processingLock = true;

            request.FindPath();

            _processingLock = false;
        }

        public static bool NeedsPath(Vector3d startPos, Vector3d endPos, int unitSize, bool allowUnwalkable = false)
        {
            foreach (GridNodeSet gridNodeSet in GridTracer.TraceLine(startPos, endPos))
            {
                foreach (Node node in gridNodeSet.Nodes)
                {
                    // A path is required if a node doesn't exist in the traced line
                    if (!node.TryGetPartition(out PathPartition partition))
                        return true;

                    if (!allowUnwalkable && !node.IsBlocked && partition.Unpassable(unitSize))
                        return true;
                }
            }
            return false;
        }

        #region Navigation Map Management

        public static bool Register(PathNavigationMap map)
        {
            if (IsMapRegistered(map.Name))
            {
                Debug.WriteLine($"Map named {map.Name} already exists!");
                return false;
            }

            _loadedMaps.Add(map.Name, map);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool IsMapRegistered(string name) => _loadedMaps.ContainsKey(name);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetNavigationMap(string name, out PathNavigationMap map) => _loadedMaps.TryGetValue(name, out map);

        public static void InitializeAllMaps()
        {
            foreach (PathNavigationMap navMap in AllMaps)
                InitializeMap(navMap.Name);
        }

        public static void InitializeMap(string name)
        {
            if (!TryGetNavigationMap(name, out PathNavigationMap map))
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

                if (!node.TryGetPartition(out PathPartition partition))
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
            foreach (PathNavigationMap navMap in AllMaps)
                Unload(navMap.Name);
        }

        public static void Unload(string name)
        {
            if (!TryGetNavigationMap(name, out PathNavigationMap map))
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

        public static void ClearAll()
        {
            _loadedMaps.Clear();
            PathPartitionHeap.FastClear();
        }

        #endregion
    }
}

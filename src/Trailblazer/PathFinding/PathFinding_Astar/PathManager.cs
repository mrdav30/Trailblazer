//=======================================================================
// Copyright (c) 2019 David Oravsky
// Distributed under the MIT License.
// (See accompanying file LICENSE or copy at
// http://opensource.org/licenses/MIT)
//=======================================================================

using SwiftCollections;
using FixedMathSharp;
using SoulsClone.Settings;

using UnityEngine;
using System;
using GridForge.Grids;
using GridForge.Utility;

namespace Lockstep.Simulation.Pathfinding
{
    public static class PathManager
    {
        private static readonly int _initialSize = 8;
        private static SwiftQueue<PathFinder> _availablePathFinderPool;
        private static SwiftQueue<PathRequest> _pathRequestQueue;
        private static bool _isProcessingPath;

        // TODO: need a better solution than looping through each grid node
        // maybe use a combination of a navmesh (i.e. heightmap)?

        public static void Initialize()
        {
            _availablePathFinderPool = new SwiftQueue<PathFinder>(_initialSize);

            for (int i = 0; i < _initialSize; i++)
                _availablePathFinderPool.Enqueue(new PathFinder());

            _pathRequestQueue = new SwiftQueue<PathRequest>();

            foreach (GridForge.Grids.Grid grid in GlobalGridManager.ActiveGrids)
            {
                // TODO: a better way of adding partitions
                //foreach (GridNode node in grid._nodes)
                //{
                //    if (!CheckWalkableSurface(node))
                //        continue;

                //    PathPartition partition = new PathPartition();
                //    partition.Setup(node.GlobalCoordinate);
                //    node.AddPartition(PathPartition.Name, partition);
                //}
            }
        }

        public static void RequestPath(PathRequest request)
        {
            _pathRequestQueue.Enqueue(request);
            TryProcessNext();
        }

        private static void TryProcessNext()
        {
            if (!_isProcessingPath && _pathRequestQueue.Count > 0)
            {
                if (_availablePathFinderPool.Count == 0)
                {
                    // If no available PathFinders, create a new one and add it to the pool.
                    _availablePathFinderPool.Enqueue(new PathFinder());
                }

                PathFinder pathFinder = _availablePathFinderPool.Dequeue();
                PathRequest pathRequest = _pathRequestQueue.Dequeue();
                _isProcessingPath = true;

                pathFinder.Setup(pathRequest.StartPosition, pathRequest.TargetPosition, pathRequest.GridSize);
                pathFinder.FindPath(pathRequest);
            }
        }

        public static void FinishedProcessingPath(PathFinder pathFinder)
        {
            if (pathFinder != null)
                _availablePathFinderPool.Enqueue(pathFinder);

            _isProcessingPath = false;
            TryProcessNext();
        }

        public static bool NeedsPath(Vector3d startPos, Vector3d endPos, int unitSize)
        {
            foreach (GridNodeSet gridNodeSet in GridTracer.TraceLine(startPos, endPos))
            {
                foreach(Node node in gridNodeSet.Nodes)
                {
                    if (!node.TryGetPartition(out PathPartition partition))
                        return true;  // If a node doesn't exist in the traced line then we'll need a path

                    if (!node.IsBlocked && partition.Unpassable(unitSize))
                        return true;
                }
            }
            return false;
        }

        public static bool ValidatePathNodes(Vector3d startPos, Vector3d endPos, out Node startNode, out Node endNode)
        {
            startNode = default;
            endNode = default;
            if (!GlobalGridManager.TryGetGridAndNode(startPos, out GridForge.Grids.Grid startGrid, out startNode))
            {
                return false;
            }

            if (startNode.IsBlocked)
            {
                startNode = GetClosestWalkableNode(startNode);
                if (!startNode.IsAllocated)
                    return false;
            }

            if (!GlobalGridManager.TryGetGridAndNode(endPos, out GridForge.Grids.Grid endGrid, out endNode))
                return false;

            if (endNode.IsBlocked)
            {
                endNode = GetClosestWalkableNode(startNode);
                if (!endNode.IsAllocated)
                    return false;
            }

            return true;
        }

        // TODO: this needs to use the LS Raycast, not unity's!
        public static bool CheckWalkableSurface(Node node)
        {
            // Get the node's world position and move it up by a small offset
            Vector3 origin = node.WorldPosition.ToVector3();
            origin.y += 0.1f;

            // Cast a ray downwards from the top of the node
            if (Physics.Raycast(origin, Vector3.down, out RaycastHit _, 0.5f, SCSettings.IgnoreForGroundCheck))//IgnoreForGroundCheck belongs in grid settings
                return true;  // Check if the hit object is considered ground

            return false;
        }

        // this should probably use GridNode.WalkableNeighborsOf
        public static Node GetClosestWalkableNode(Node node)
        {
            Node result = default;
            SwiftList<WalkableNode> walkableNeighbors = PathPartition.WalkableNeighborsOf(node);
            for (int i = 0; i < walkableNeighbors.Count; i++)
            {
                result = walkableNeighbors[i].Node;
                if (walkableNeighbors[i].Cost == PathPartition.StraightCost)
                    break; // prefer straight neighbors since they cost less
            }
            return result;
        }

        //TODO: this needs to go into it's own editor utility
        //private void OnDrawGizmos()
        //{
        //    if (Application.isPlaying && ShowGrid)
        //    {
        //        if ((uint)GridIndex > GlobalGridManager.ActiveGrids.Count)
        //        {
        //            Console.WriteLine($"Index {GridIndex} is not available in the GlobalGridManager");
        //            return;
        //        }
        //    }
        //}
        //private void DrawPathfinding()
        //{
        //    GUIStyle labelStyle = new GUIStyle(GUI.skin.label)
        //    {
        //        fontSize = 12,
        //        fontStyle = FontStyle.Bold,
        //        normal = {
        //            textColor = Color.black,
        //            background =
        //            Texture2D.whiteTexture
        //        }
        //    };

        //    GlobalGridManager.GetGrid(GridIndex, out Grid grid);
        //    for (int x = 0; x < (CheckWidth == -1 ? grid.Width : CheckWidth); x++)
        //    {
        //        for (int y = 0; y < (CheckHeight == -1 ? grid.Height : CheckHeight); y++)
        //        {
        //            for (int z = 0; z < (CheckWidth == -1 ? grid.Width : CheckWidth); z++)
        //            {
        //                if (!grid.GetNode(new CoordinatesLocal(x, y, z), out Node node))
        //                    continue;

        //                //  Color depends on whether or not the node is walkable
        //                //  Red = Unwalkable, Green = Walkable
        //                if (node.IsBlocked)
        //                    Gizmos.color = new Color(1, 0, 0, 0.5f); //red
        //                else
        //                    Gizmos.color = new Color(0, 1, 0, 0.5f); //green

        //                Gizmos.DrawCube(node.WorldPosition.ToVector3(), _nodeScale);

        //                if (!node.GetPartition(PathPartition.Name, out PathPartition partition))
        //                    continue;

        //                if (ShowClearanceDegree)
        //                {
        //                    if (partition.ClearanceDegree != PathPartition.DefaultDegree)
        //                    {
        //                        UnityEditor.Handles.Label(node.WorldPosition.ToVector3((float)CheckHeight + _nodeScale.y) + Vector3.up * 0.2f, "d" + partition.ClearanceDegree.ToString(), labelStyle);
        //                    }
        //                }

        //                if (ShowCost && partition.TotalCost > Fixed64.Zero)
        //                {
        //                    UnityEditor.Handles.Label(node.WorldPosition.ToVector3((float)CheckHeight + _nodeScale.y) + Vector3.up * 0.2f, "c" + partition.TotalCost.ToString(), labelStyle);
        //                }
        //            }
        //        }
        //    }
        //}
    }
}
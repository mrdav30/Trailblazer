//=======================================================================
// Copyright (c) 2015 John Pan
// Distributed under the MIT License.
// (See accompanying file LICENSE or copy at
// http://opensource.org/licenses/MIT)
//=======================================================================

//Resources:
//Bresenham's Algorithm Implementation: ericw. (Source: http://ericw.ca/notes/bresenhams-line-algorithm-in-csharp.html)
//AStar Algorithm Template: Sebastian Lague (Source: https://www.youtube.com/watch?v=-L-WgKMFuhE)

using SwiftCollections;
using FixedMathSharp;
using System;
using GridForge.Grids;

namespace Lockstep.Simulation.Pathfinding
{
    public class PathFinder
    {
        public HeuristicMethod Heuristic { get; private set; } = HeuristicMethod.manhattan;

        // Fields
        private Node _startNode;
        private Node _endNode;
        private int _pathSize;
        private bool _destinationIsReached;
        private SwiftList<Node> _rawOutputPath;

        private const int maxIterations = 10000;
        private static readonly Fixed64 MaxHeightDifference = Fixed64.Half; // Change this value according to your game units and preferences

        public void Setup(Vector3d startPos, Vector3d targetPos, int pathSize)
        {
            _pathSize = pathSize;
            _destinationIsReached = false;

            _startNode = null;
            _endNode = null;

            if (GlobalGridManager.TryGetGridAndNode(startPos, out Grid startGrid, out _startNode))
            {
                Console.WriteLine("Unable to find a valid start node for {startPos}");
                return;
            }

            if (GlobalGridManager.TryGetGridAndNode(targetPos, out Grid endGrid, out _endNode))
            {
                _startNode = null;
                Console.WriteLine("Unable to find a valid end node for {targetPos}");
                return;
            }

            _rawOutputPath = new SwiftList<Node>();
            PathPartitionHeap.FastClear();

            if (_startNode.TryGetPartition(out PathPartition partition))
                PathPartitionHeap.Add(partition);
        }

        public void FindPath(PathRequest request)
        {
            if (!CanPath(out SwiftList<Vector3d> result))
                return;

            // Call the OnComplete callback with the resulting path
            request.OnComplete?.Invoke(result);

            // Signal the PathRequestManager that we're done processing this path
            PathManager.FinishedProcessingPath(this);
        }

        public bool CanPath(out SwiftList<Vector3d> path)
        {
            int iterations = 0;
            while (PathPartitionHeap.Count > 0 && iterations < maxIterations)
            {
                PathPartition currentPartition = PathPartitionHeap.RemoveFirst();

                if (!GlobalGridManager.TryGetGridAndNode(currentPartition.ParentCoordinate, out _, out Node currentNode))
                {
                    iterations++;
                    continue;
                }

                PathPartitionHeap.SetClosed(currentPartition);

                if (currentNode.SpawnToken == _endNode.SpawnToken)
                {
                    _destinationIsReached = true;
                    break;
                }

                AnalyzeNeighbors(currentNode);

                iterations++;
                if (iterations > maxIterations)
                    GlobalLogger.Log("Path is to long!", LogLevel.Info);
            }

            if (_destinationIsReached)
            {
                DestinationReached();
                path = SmoothPath();
                return true;
            }
            else
            {
                path = default;
                return false;
            }
        }

        private void AnalyzeNeighbors(Node currentNode)
        {
            if (!currentNode.TryGetPartition(out PathPartition partition))
                return;

            SwiftList<WalkableNode> neighbors = PathPartition.WalkableNeighborsOf(currentNode);
            for (int i = 0; i < neighbors.Count; i++)
            {
                Node neighbor = neighbors[i].Node;
                neighbor.TryGetPartition(out PathPartition neighborPartition);
                if (PathPartitionHeap.IsClosed(neighborPartition))
                    continue;

                Fixed64 heightDifference = (currentNode.WorldPosition.y - neighbor.WorldPosition.y).Abs();

                // Skip neighbors that have a height difference greater than the allowed maximum
                if (heightDifference > MaxHeightDifference)
                    continue;

                // Check if the neighbor node is unpassable for the actor
                if (neighborPartition.Unpassable(_pathSize))
                    continue;

                // Calculate the appropriate cost for diagonal or straight movement
                Fixed64 newMovementCostToNeighbor = partition.MovementCost + neighbors[i].Cost;
                if (!PathPartitionHeap.Contains(neighborPartition) || newMovementCostToNeighbor < neighborPartition.MovementCost)
                {
                    neighborPartition.MovementCost = newMovementCostToNeighbor;
                    (Fixed64 hCost, Fixed64 fCost) = PathPartition.CalculateHeuristic(
                        neighbor.WorldPosition,
                        _endNode.WorldPosition,
                        neighborPartition.MovementCost,
                        Heuristic);
                    neighborPartition.HeuristicCost = hCost;
                    neighborPartition.TotalCost = fCost;
                    neighborPartition.TrailNextCoordinate = currentNode.GlobalCoordinates;

                    if (!PathPartitionHeap.Contains(neighborPartition))
                        PathPartitionHeap.Add(neighborPartition);
                    else
                        PathPartitionHeap.SortUp(neighborPartition);
                }
            }
        }

        private void DestinationReached()
        {
            _rawOutputPath.Clear();
            Node currentNode = _endNode;

            while (currentNode.SpawnToken != _startNode.SpawnToken)
            {
                _rawOutputPath.Insert(0, currentNode);

                currentNode.TryGetPartition(out PathPartition partition);
                if (GlobalGridManager.TryGetGridAndNode(
                    partition.TrailNextCoordinate, 
                    out Grid nextTrailGrid, 
                    out Node nextTrailNode))
                {
                    currentNode = nextTrailNode;
                }
            }

            _rawOutputPath.Insert(0, _startNode);
        }

        public SwiftList<Vector3d> SmoothPath()
        {
            //  nodePath should include the start and end nodes
            SwiftList<Vector3d> outputVectorPath = new SwiftList<Vector3d>();
            int length = _rawOutputPath.Count - 1;

            //culling out unneccessary nodes
            Node StartNode = _rawOutputPath[0];
            // include flag to include start node if startnode != starting position?
            outputVectorPath.Add(StartNode.WorldPosition);
            Node oldNode = StartNode;
            Fixed64 oldX = Fixed64.Zero;
            Fixed64 oldZ = Fixed64.Zero;
            Fixed64 newX;
            Fixed64 newZ;
            for (int i = 1; i < length; i++)
            {
                Node node = _rawOutputPath[i];

                if (!node.TryGetPartition(out PathPartition partition))
                    continue;

                //Anyone who's somebody is near an unwalkable node
                bool important = partition.GetNeighborClearance() <= _pathSize + 1;
                if (!important)
                    continue;

                newX = node.WorldPosition.x - oldNode.WorldPosition.x;
                newZ = node.WorldPosition.z - oldNode.WorldPosition.z;
                if (newX <= Fixed64.One && newX >= -Fixed64.One
                    && newZ <= Fixed64.One && newZ >= -Fixed64.One)
                {
                    if (newX == oldX && newZ == oldZ)
                    {
                        if (oldX != Fixed64.Zero || oldZ != Fixed64.Zero)
                            outputVectorPath.RemoveAt(outputVectorPath.Count - 1);
                    }
                    else
                    {
                        oldX = newX;
                        oldZ = newZ;
                    }
                }
                else
                {
                    oldX = Fixed64.Zero;
                    oldZ = Fixed64.Zero;
                }

                outputVectorPath.Add(node.WorldPosition);

                oldNode = node;
            }

            outputVectorPath.Add(_endNode.WorldPosition);
            return outputVectorPath;
        }
    }
}
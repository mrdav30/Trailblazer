using FixedMathSharp;
using GridForge.Grids;
using GridForge.Utility;
using System;

namespace Trailblazer.Pathing
{
    public static class PathfindingManager
    {

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

            if (!GlobalGridManager.TryGetGridAndNode(pathRequest.TargetPosition, out Grid endGrid, out Node targetNode))
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

            int maxSearchSize = fromGrid.SpawnToken == endGrid.SpawnToken ? fromGrid.Size : fromGrid.Size + endGrid.Size;
            pathRequest.SetValidatedNodeRequest(fromNode, targetNode, maxSearchSize);

            return true;
        }

        public static void RequestPath(IPathRequest request)
        {
            if (_processingLock) return;

            if (!request.IsValidated && !ValidatePathRequest(request))
                return;

            _processingLock = true;

            request.FindPath();

            _processingLock = false;
        }

        public static bool NeedsPath(Vector3d startPos, Vector3d endPos, int unitSize)
        {
            foreach (GridNodeSet gridNodeSet in GridTracer.TraceLine(startPos, endPos))
            {
                foreach (Node node in gridNodeSet.Nodes)
                {
                    // A path is required if a node doesn't exist in the traced line
                    if (!node.TryGetPartition(out PathPartition partition))
                        return true;

                    if (!node.IsBlocked && partition.Unpassable(unitSize))
                        return true;
                }
            }
            return false;
        }       
    }
}

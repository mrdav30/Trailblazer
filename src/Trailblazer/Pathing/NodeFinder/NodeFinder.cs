using FixedMathSharp;
using GridForge.Grids;
using GridForge.Utility;
using SwiftCollections;

namespace Trailblazer.Pathing
{
    public static class NodeFinder
    {
        // set to the highest height or width value of any game object
        private const int _maxTestDistance = 3;

        public static bool TryGetPathEdgeNodes(Vector3d startPosition, Vector3d targetPosition, out Node startNode, out Node targetNode)
        {
            targetNode = default;
            if (!GlobalGridManager.TryGetGridAndNode(startPosition, out _, out startNode))
                return false;

            if (startNode.IsBlocked)
            {
                if (!TryGetClosestWalkableNeighbor(startNode, out Node closestNeighbor))
                    return false;
                startNode = closestNeighbor;
            }

            if (!GlobalGridManager.TryGetGridAndNode(targetPosition, out _, out targetNode))
                return false;

            if (targetNode.IsBlocked)
            {
                if (!TryGetClosestWalkableNeighbor(targetNode, out Node closestNeighbor))
                    return false;
                targetNode = closestNeighbor;
            }

            return true;
        }

        public static bool TryGetClosestWalkableNeighbor(Node node, out Node closestNeighbor)
        {
            closestNeighbor = null;

            foreach (TraversableNode neighbor in PathManager.WalkableStraightNeighborsOf(node))
            {
                closestNeighbor = neighbor.Node; // prefer straight neighbors since they cost less
                return true;
            }

            foreach (TraversableNode neighbor in PathManager.WalkableDiagonalNeighborsOf(node))
            {
                closestNeighbor = neighbor.Node;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Finds closest next-best-node also when destination is off invalid
        /// </summary>
        public static bool GetEndNode(Vector3d from, Vector3d destination, out Node destinationNode, bool allowUnwalkable = false)
        {
            if (!GlobalGridManager.TryGetGridAndNode(destination, out _, out destinationNode))
            {
                // If null, it is off the grid. Raycast back onto grid for closest viable node to the destination.
                foreach (GridNodeSet gridNodeSet in GridTracer.TraceLine(destination, from))
                {
                    foreach (Node node in gridNodeSet.Nodes)
                    {
                        // A path is required if a node doesn't exist in the traced line
                        if (!allowUnwalkable && node.IsBlocked || !node.TryGetPartition<PathPartition>(out _))
                            continue;

                        destinationNode = node;
                        return true;
                    }
                }

                return false;
            }

            if (destinationNode.IsBlocked)
            {
                if (allowUnwalkable && TryGetClosestWalkableNeighbor(destinationNode, out _))
                    return true;

                return StarCast(destination, out destinationNode);
            }

            return true;
        }

        /// <summary>
        /// Finds closest next-best-node
        /// </summary>
        public static bool GetStartNode(Vector3d from, Vector3d destination, out Node startNode, bool allowUnwalkable = false)
        {
            if (!GlobalGridManager.TryGetGridAndNode(from, out _, out startNode))
            {
                // If null, it is off the grid. Raycast back onto grid for closest viable node to the destination.
                foreach (GridNodeSet gridNodeSet in GridTracer.TraceLine(from, destination))
                {
                    foreach (Node node in gridNodeSet.Nodes)
                    {
                        // A path is required if a node doesn't exist in the traced line
                        if (!allowUnwalkable && node.IsBlocked || !node.TryGetPartition<PathPartition>(out _))
                            continue;

                        startNode = node;
                        return true;
                    }
                }

                return false;
            }

            if (startNode.IsBlocked)
            {
                if (allowUnwalkable && TryGetClosestWalkableNeighbor(startNode, out _))
                    return true;

                return StarCast(from, out startNode);
            }

            return true;
        }

        public static bool StarCast(Vector3d targetPosition, out Node returnNode)
        {
            returnNode = null;
            if (!GlobalGridManager.TryGetGrid(targetPosition, out Grid outGrid))
                return false; // no grid found at this position!

            AlternativeNodeFinder.Instance.SetQuery(targetPosition, outGrid.BoundsMin, _maxTestDistance);

            if (!AlternativeNodeFinder.Instance.GetNode(out returnNode))
                return false;

            return true;
        }

        public static bool GetClosestNodeForSize(
            Vector3d from, 
            Vector3d destination, 
            Fixed64 pathingSize, 
            out Node returnNode, 
            bool allowUnwalkable = false)
        {
            if (GlobalGridManager.TryGetGridAndNode(from, out _, out returnNode)
                && (!returnNode.IsBlocked || allowUnwalkable)
                && returnNode.TryGetPartition(out PathPartition returnPartition)
                && !returnPartition.Unpassable(pathingSize))
            {
                return true;
            }

            foreach (GridNodeSet gridNodeSet in GridTracer.TraceLine(from, destination))
            {
                foreach (Node currentNode in gridNodeSet.Nodes)
                {
                    // A path is required if a node doesn't exist in the traced line
                    if (!allowUnwalkable && currentNode.IsBlocked || !currentNode.TryGetPartition(out PathPartition currentPartition))
                        continue;

                    if (!currentPartition.Unpassable(pathingSize))
                    {
                        returnNode = currentNode;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
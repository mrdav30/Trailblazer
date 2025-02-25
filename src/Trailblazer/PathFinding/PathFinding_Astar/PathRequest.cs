using SwiftCollections;
using FixedMathSharp;
using System;

namespace Lockstep.Simulation.Pathfinding
{
    public struct PathRequest
    {
        private Vector3d _startPosition;
        public Vector3d StartPosition => _startPosition;
        private Vector3d _destinationPosition;
        public Vector3d TargetPosition => _destinationPosition;
        private int _gridSize;
        public int GridSize => _gridSize;
        public Action<SwiftList<Vector3d>> OnComplete { get; private set; }

        public PathRequest(Vector3d startPos, Vector3d targetPos, int pathSize, Action<SwiftList<Vector3d>> onComplete)
        {
            _startPosition = startPos;
            _destinationPosition = targetPos;
            _gridSize = pathSize;
            OnComplete = onComplete;
        }
    }
}

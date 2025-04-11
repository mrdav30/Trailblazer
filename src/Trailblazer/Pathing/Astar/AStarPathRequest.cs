using FixedMathSharp;
using SwiftCollections;
using System;

namespace Trailblazer.Pathing
{
    public class AStarPathRequest : PathRequest
    {
        public HeuristicMethod Heuristic { get; set; } = HeuristicMethod.Manhattan;

        // Change this value according to your game units and preferences
        public Fixed64 MaxHeightDifference { get; set; } = Fixed64.Half;

        public Action<bool, SwiftList<Vector3d>> OnComplete { get; private set; }

        public AStarPathRequest(Vector3d fromPosition, Vector3d targetPosition, int roverSize, Action<bool, SwiftList<Vector3d>> onComplete)
            : base(fromPosition, targetPosition, roverSize)
        {
            OnComplete = onComplete;
        }

        public override void FindPath()
        {
            if (!IsValidated) return;

            AStarPathFinder.Shared.FindPath(this);
        }
    }
}

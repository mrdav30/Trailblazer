using FixedMathSharp;
using SwiftCollections;
using System;

namespace Trailblazer.Pathing
{
    public class FlowFieldPathRequest : PathRequest
    {
        public Action<bool, SwiftDictionary<int, FlowField>> OnComplete { get; private set; }

        public FlowFieldPathRequest(Vector3d fromPosition, Vector3d targetPosition, int roverSize, Action<bool, SwiftDictionary<int, FlowField>> onComplete) 
            : base(fromPosition, targetPosition, roverSize)
        {
            OnComplete = onComplete;
        }

        public override void FindPath()
        {
            if (!IsValidated) return;

            FlowFieldPathfinder.Shared.FindPath(this);
        }
    }
}

using FixedMathSharp;
using SwiftCollections;
using System;

namespace Trailblazer.Pathing
{
    public class FlowFieldPathRequest : PathRequest
    {
        public const int DefaultSearchRange = 10;

        private int _searchRange = DefaultSearchRange;
        public int SearchRange
        {
            get => _searchRange;
            set
            {
                if (IsValidated) return;
                _searchRange = value;
            }
        }

        public Action<bool, SwiftDictionary<int, FlowField>> OnComplete { get; private set; }

        public FlowFieldPathRequest(
            Vector3d from, 
            Vector3d destination, 
            Fixed64 unitSize, 
            Action<bool, SwiftDictionary<int, FlowField>> onComplete) : base(from, destination, unitSize)
        {
            OnComplete = onComplete;
        }

        public override void FindPath()
        {
            if (!IsValidated) return;

            FlowFieldSurveyor.Shared.FindPath(this);
        }
    }
}

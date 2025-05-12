using FixedMathSharp;
using SwiftCollections;
using System;

namespace Trailblazer.Pathing
{
    public class FlowFieldPathRequest : PathRequest
    {

        private int _flowFieldSearchPadding = 10;
        public int FlowFieldSearchPadding
        {
            get => _flowFieldSearchPadding;
            set
            {
                if (IsValidated) return;
                _flowFieldSearchPadding = value;
            }
        }

        private bool _enableLineOfSight = true;
        public bool EnableLineOfSight
        {
            get => _enableLineOfSight;
            set
            {
                if (IsValidated) return;
                _enableLineOfSight = value;
            }
        }

        private int _lineOfSightMaxCost = 1000;
        public int LineOfSightMaxCost
        {
            get => _lineOfSightMaxCost;
            set
            {
                if (IsValidated) return;
                _lineOfSightMaxCost = value;
            }
        }

        public Action<bool, SwiftDictionary<int, FlowField>> OnComplete { get; private set; }

        public FlowFieldPathRequest(Vector3d fromPosition, Vector3d targetPosition, int roverSize, Action<bool, SwiftDictionary<int, FlowField>> onComplete) 
            : base(fromPosition, targetPosition, roverSize)
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

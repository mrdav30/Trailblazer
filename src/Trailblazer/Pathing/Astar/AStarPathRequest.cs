using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using System;

namespace Trailblazer.Pathing
{
    public class AStarPathRequest : PathRequest
    {
        private HeuristicMethod _heuristic = HeuristicMethod.Manhattan;
        public HeuristicMethod Heuristic
        {
            get => _heuristic;
            set
            {
                if (!IsValidated) _heuristic = value;
            }
        }

        /// <summary>
        /// The maximum Y-axis height delta a unit can step or climb per node.
        /// Nodes exceeding this are ignored even if walkable and adjacent.
        /// </summary>
        private Fixed64 _maxClimbHeight = GlobalGridManager.NodeSize;
        public Fixed64 MaxClimbHeight
        {
            get => _maxClimbHeight;
            set
            {
                if (!IsValidated) _maxClimbHeight = value;
            }
        }

        private bool _useSplineSmoothing = true;
        public bool UseSplineSmoothing
        {
            get => _useSplineSmoothing;
            set
            {
                if (!IsValidated) _useSplineSmoothing = value;
            }
        }

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

        public override void Reset()
        {
            base.Reset();

            _heuristic = HeuristicMethod.Manhattan;

            _maxClimbHeight = GlobalGridManager.NodeSize;

            _useSplineSmoothing = true;

            OnComplete = null;
        }
    }
}

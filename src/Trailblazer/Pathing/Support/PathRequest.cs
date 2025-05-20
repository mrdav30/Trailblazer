using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing
{
    public interface IPathRequest
    {
        Vector3d FromPosition { get; }

        Vector3d TargetPosition { get; }

        Fixed64 UnitSize { get; }

        bool IsValidated { get;}

        void SetValidatedNodeRequest(Node fromNode, Node targetNode, int? maxSearchSize);

        void FindPath();

        void Reset();
    }

    public abstract class PathRequest : IPathRequest
    {
        public const int DefaultMaxSearchSize = 1000;

        public Vector3d FromPosition { get; protected set; }

        public Vector3d TargetPosition { get; protected set; }

        public Fixed64 UnitSize { get; protected set; }

        public bool IsValidated { get; protected set; }

        public Node FromNode { get; protected set; }

        public Node TargetNode { get; protected set; }

        public int? _maxSearchSize;
        public int MaxSearchSize {
            get => _maxSearchSize ?? 0;
            set
            {
                if (IsValidated) return;
                _maxSearchSize = value;
            }
        }

        public PathRequest(Vector3d fromPosition, Vector3d targetPosition, Fixed64 unitSize)
        {
            FromPosition = fromPosition;
            TargetPosition = targetPosition;
            UnitSize = unitSize;
        }

        public virtual void SetValidatedNodeRequest(Node fromNode, Node targetNode, int? searchSize)
        {
            FromNode = fromNode;
            TargetNode = targetNode;
            MaxSearchSize = _maxSearchSize ?? (searchSize ?? DefaultMaxSearchSize);

            IsValidated = true;
        }

        public abstract void FindPath();

        public virtual void Reset()
        {
            IsValidated = false;

            FromNode = null;
            TargetNode = null;

            _maxSearchSize = null;
        }
    }
}

using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing
{
    public interface IPathRequest
    {
        Vector3d FromPosition { get; }

        Vector3d TargetPosition { get; }

        int RoverSize { get; }

        bool IsValidated { get;}

        void SetValidatedNodeRequest(Node fromNode, Node targetNode, int maxSearchSize);

        void FindPath();
    }

    public abstract class PathRequest : IPathRequest
    {
        public Vector3d FromPosition { get; protected set; }

        public Vector3d TargetPosition { get; protected set; }

        public int RoverSize { get; protected set; }

        public bool IsValidated { get; protected set; }

        public Node FromNode { get; protected set; }

        public Node TargetNode { get; protected set; }

        public int MaxSearchSize { get; protected set; }

        public PathRequest(Vector3d fromPosition, Vector3d targetPosition, int roverSize)
        {
            FromPosition = fromPosition;
            TargetPosition = targetPosition;
            RoverSize = roverSize;
        }

        public virtual void SetValidatedNodeRequest(Node fromNode, Node targetNode, int searchSize)
        {
            FromNode = fromNode;
            TargetNode = targetNode;
            MaxSearchSize = searchSize;

            IsValidated = true;
        }

        public abstract void FindPath();
    }
}

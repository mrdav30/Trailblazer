using FixedMathSharp;

namespace Trailblazer.Controllers
{
    public struct TraversalRequest
    {
        // The global direction we want the character to move in.
        public Vector3d MovementDirection { get;set; }
        public TraversalSpeed TraversalSpeed { get; set; }
        public bool IsRequestingJump { get; set; }
        public readonly bool IsMoving => MovementDirection != Vector3d.Zero && TraversalSpeed != TraversalSpeed.Idle;
    }
}

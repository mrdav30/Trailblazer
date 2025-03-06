using FixedMathSharp;

namespace Trailblazer.Controllers
{
    public struct TraversalState
    {
        public TraversalMedium ActiveTraversalMedium { get; set; }
        public TraversalMedium LastTraversalMedium { get; set; }

        public Vector3d GroundNormal { get; set; }
        public Vector3d LastGroundNormal { get; set; }

        public readonly bool StateChanged => ActiveTraversalMedium != LastTraversalMedium;

        public readonly bool IsGrounded => ActiveTraversalMedium == TraversalMedium.Ground;
        public readonly bool WasGrounded => LastTraversalMedium == TraversalMedium.Ground;

        public readonly bool IsInAir => ActiveTraversalMedium == TraversalMedium.Air;
        public readonly bool WasInAir => LastTraversalMedium == TraversalMedium.Air;

        public readonly bool IsInWater => ActiveTraversalMedium == TraversalMedium.Water;
        public readonly bool WasInWater => LastTraversalMedium == TraversalMedium.Water;

        /// <summary>
        /// The default movement state.
        /// </summary>
        public readonly static TraversalState DefaultTraversalState = new TraversalState 
        { 
            ActiveTraversalMedium = TraversalMedium.Unknown,
            LastTraversalMedium = TraversalMedium.Unknown,
            GroundNormal = Vector3d.Zero,
            LastGroundNormal = Vector3d.Zero
        };
    }

}

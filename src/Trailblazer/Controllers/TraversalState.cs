using FixedMathSharp;

namespace Trailblazer.Controllers
{
    public enum TraversalMedium
    {
        Ground = 0,
        Air = 1,
        Water = 2,
        Unknown = 99
    }

    public struct TraversalState
    {
        public TraversalMedium Medium;

        /// <summary>
        /// Stores the height of the current surface.
        /// </summary>
        public Fixed64 SurfaceLevel;

        public GroundState? Ground;

        /// <summary>
        /// The default movement state.
        /// </summary>
        public readonly static TraversalState DefaultTraversalState = new()
        {
            Medium = TraversalMedium.Unknown
        };

        public TraversalState(TraversalMedium medium, Fixed64 surfaceLevel) : this(medium, surfaceLevel, null) { }

        public TraversalState(TraversalMedium medium, Fixed64 surfaceLevel, GroundState? groundState)
        {
            Medium = medium;
            SurfaceLevel = surfaceLevel;
            Ground = groundState;
        }
    }
}

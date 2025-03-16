using FixedMathSharp;

namespace Trailblazer.Controllers
{
    public enum TraversalMedium
    {
        Unknown = 0,
        Ground = 1,
        Air = 2,
        Water = 3,
    }

    public struct TraversalCondition
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
        public readonly static TraversalCondition DefaultTraversalCondition = new()
        {
            Medium = TraversalMedium.Unknown
        };

        public TraversalCondition(TraversalMedium medium, Fixed64 surfaceLevel, GroundState? groundState = null)
        {
            Medium = medium;
            SurfaceLevel = surfaceLevel;
            Ground = groundState;
        }
    }
}

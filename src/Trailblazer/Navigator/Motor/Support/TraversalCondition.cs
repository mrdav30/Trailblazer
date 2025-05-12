using FixedMathSharp;

namespace Trailblazer.Navigator.Motor
{
    /// <summary>
    /// Represents the traversal state of a scout, including its movement medium and surface interactions.
    /// </summary>
    public class TraversalCondition
    {
        /// <summary>
        /// Defines the medium in which the scout is currently moving.
        /// </summary>
        public TraversalMedium Medium;

        /// <summary>
        /// Stores the height of the current surface, typically used for ground and water interactions.
        /// </summary>
        public Fixed64 SurfaceLevel;

        /// <summary>
        /// Stores the height of the ceiling above the scout, if applicable.
        /// Defaults to Fixed64.MAX_VALUE, meaning no ceiling.
        /// </summary>
        public Fixed64 CeilingLevel;

        /// <summary>
        /// Contains data about the ground state, if applicable.
        /// </summary>
        public GroundCondition? GroundState;

        /// <summary>
        /// Represents an empty traversal condition with default values.
        /// </summary>
        public readonly static TraversalCondition Empty = new();

        public TraversalCondition(
            TraversalMedium medium = TraversalMedium.Unknown,
            Fixed64? surfaceLevel = null,
            GroundCondition? surfaceCondition = null,
            Fixed64? ceilingLevel = null)
        {
            Medium = medium;
            SurfaceLevel = surfaceLevel ?? Fixed64.Zero;
            GroundState = surfaceCondition ?? null;
            CeilingLevel = ceilingLevel ?? Fixed64.MAX_VALUE;
        }
    }
}

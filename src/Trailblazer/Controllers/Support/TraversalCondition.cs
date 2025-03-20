using FixedMathSharp;

namespace Trailblazer.Controllers
{
    /// <summary>
    /// Represents the traversal state of a scout, including its movement medium and surface interactions.
    /// </summary>
    public struct TraversalCondition
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
        /// Contains data about the ground state, if applicable.
        /// </summary>
        public SurfaceCondition? SurfaceCondition;

        /// <summary>
        /// Stores the height of the ceiling above the scout, if applicable.
        /// Defaults to Fixed64.MAX_VALUE, meaning no ceiling.
        /// </summary>
        public Fixed64 CeilingLevel;

        /// <summary>
        /// Represents an empty traversal condition with default values.
        /// </summary>
        public readonly static TraversalCondition Empty = new()
        {
            CeilingLevel = Fixed64.MAX_VALUE
        };

        public TraversalCondition(TraversalMedium medium, Fixed64? surfaceLevel = null, SurfaceCondition? surfaceCondition = null, Fixed64? ceilingLevel = null)
        {
            Medium = medium;
            SurfaceLevel = surfaceLevel ?? Fixed64.Zero;
            SurfaceCondition = surfaceCondition ?? null;
            CeilingLevel = ceilingLevel ?? Fixed64.MAX_VALUE;
        }
    }
}

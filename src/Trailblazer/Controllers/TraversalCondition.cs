using FixedMathSharp;

namespace Trailblazer.Controllers
{
    /// <summary>
    /// Specifies the different movement mediums a scout can traverse through.
    /// </summary>
    public enum TraversalMedium
    {
        /// <summary>
        /// The scout's movement medium is unknown.
        /// </summary>
        Unknown = 0,

        /// <summary>
        /// The scout is traversing on the ground.
        /// </summary>
        Ground = 1,

        /// <summary>
        /// The scout is airborne.
        /// </summary>
        Air = 2,

        /// <summary>
        /// The scout is moving through water.
        /// </summary>
        Water = 3,
    }

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
        public GroundState? Ground;

        /// <summary>
        /// Represents an empty traversal condition with default values.
        /// </summary>
        public readonly static TraversalCondition Empty = new();
    }
}

using FixedMathSharp;
using System;

namespace Trailblazer.Navigation
{
    [Serializable]
    public struct TraversalRequest
    {
        /// <summary>
        /// Represents an empty movement request with default values.
        /// </summary>
        public static readonly TraversalRequest Empty = new();

        [Transient]
        public Vector3d Origin { get; set; }

        [Transient]
        public FixedQuaternion Rotation { get; set; }

        /// <summary>
        /// Normalized distance of movement
        /// </summary>
        [Transient]
        public Vector3d Direction { get; set; }

        /// <summary>
        /// The speed at which the scout wants to move.
        /// </summary>
        [Transient]
        public TrekRate Rate { get; set; }

        /// <summary>
        /// Indicates whether the scout is requesting to jump.
        /// </summary>
        [Transient]
        public bool IsRequestingJump { get; set; }
    }
}

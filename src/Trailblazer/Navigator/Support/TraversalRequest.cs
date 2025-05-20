using FixedMathSharp;
using System;

namespace Trailblazer.Navigation
{
    public enum TrailGuideParadigm
    {
        None,
        Astar,
        FlowField
    }

    [Serializable]
    public class TraversalRequest
    {
        [Transient]
        public Vector3d CurrentPosition { get; set; }

        [Transient]
        public FixedQuaternion CurrentRotation { get; set; }

        [Transient]
        public Fixed64 UnitSize { get; set; }

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

        /// <summary>
        /// Normalized distance of movement
        /// </summary>
        [Transient]
        public Vector3d Direction { get; set; }

        [Transient]
        public Vector3d? Destination { get; set; }

        [Transient]
        public TrailGuideParadigm TrailGuideRequest { get; set; }

        /// <summary>
        /// Represents an empty movement request with default values.
        /// </summary>
        public static readonly TraversalRequest Empty = new();

        public void Reset()
        {
            CurrentPosition = default;
            CurrentRotation = default;
            Direction = default;
            Rate = default;
            IsRequestingJump = false;
            TrailGuideRequest = default;
        }
    }
}

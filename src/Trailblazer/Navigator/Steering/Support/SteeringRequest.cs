using FixedMathSharp;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation
{
    public struct SteeringRequest
    {
        [Transient]
        public TrailGuideParadigm TrailGuideRequest { get; set; }

        [Transient]
        public Vector3d From { get; set; }

        [Transient]
        public Vector3d Destination { get; set; }

        [Transient]
        public Fixed64 UnitSize { get; set; }

        /// <summary>
        /// Represents an empty steering request with default values.
        /// </summary>
        public static readonly SteeringRequest Empty = new();
    }
}

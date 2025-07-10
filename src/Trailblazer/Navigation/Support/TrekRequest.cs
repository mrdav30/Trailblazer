using FixedMathSharp;
using System;
using System.Runtime.CompilerServices;
using Trailblazer.Navigation.Motor;

namespace Trailblazer.Navigation
{
    [Serializable]
    public class TrekRequest
    {
        public Vector3d Origin { get; set; }

        public FixedQuaternion Rotation { get; set; }

        /// <summary>
        /// Normalized distance of movement
        /// </summary>
        public Vector3d Direction { get; set; }

        /// <summary>
        /// The speed at which the scout wants to move.
        /// </summary>
        public TrekRate Rate { get; set; }

        /// <summary>
        /// Indicates whether the scout is requesting to jump.
        /// </summary>
        public bool IsRequestingJump { get; set; }

        public TrekRequest(
            Vector3d? origin = null,
            FixedQuaternion? rotation = null,
            Vector3d? direction = null,
            TrekRate rate = TrekRate.Stationary,
            bool requestingJump = false)
        {
            Origin = origin ?? Vector3d.Zero;
            Rotation = rotation ?? FixedQuaternion.Identity;
            Direction = direction ?? Vector3d.Zero;
            Rate = rate;
            IsRequestingJump = requestingJump;
        }

        /// <summary>
        /// Represents an empty movement request with default values.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static TrekRequest CreateEmpty() => new();

        public void Reset()
        {
            Origin = Vector3d.Zero;
            Rotation = FixedQuaternion.Identity;
            Direction = Vector3d.Zero;
            Rate = TrekRate.Stationary;
            IsRequestingJump = false;
        }
    }
}

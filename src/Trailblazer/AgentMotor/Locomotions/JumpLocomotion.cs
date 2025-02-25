using FixedMathSharp;
using Trailblazer.Utility.Coroutines;
using System.Collections.Generic;

namespace Trailblazer.AgentMotor.Locomotions
{
    // We will contain all the jumping related variables in one helper class for clarity.
    [System.Serializable]
    public class JumpLocomotion: ILocomotion
    {
        #region Constants

        public static readonly Fixed64 DefaultBaseJumpHeight = Fixed64.One;

        public static readonly Fixed64 DefaultExtraJumpHeight = (Fixed64)2f;

        public static readonly Fixed64 DefaultPerpendicularJumpAmount = (Fixed64)0.5f;

        public static readonly Fixed64 DefaultSteepPerpendicularJumpAmount = (Fixed64)0.5f;

        public static readonly Fixed64 DefaultCooldownTime = (Fixed64)0.2f;

        public static readonly Fixed64 DefaultAvoidGroundingAfterJumpTime = (Fixed64)0.05f;

        public static readonly Vector3d DefaultJumpDirection = Vector3d.Up;

        #endregion

        /// <summary>
        /// Can the character jump?
        /// </summary>
        public bool IsEnabled = true;

        /// <summary>
        /// Are we jumping?(Initiated with jump button and not grounded yet)
        /// To see if we are just in the air(initiated by jumping OR falling) see the grounded variable.
        /// </summary>
        public bool IsJumping { get; internal set; }

        public bool IsHoldingJump { get; internal set; }

        /// <summary>
        /// Set to false during cooldown
        /// </summary>
        public bool OnCooldown { get; internal set; }

        public Vector3d JumpDirection = DefaultJumpDirection;

        /// <summary>
        /// How high do we jump when pressing jump and letting go immediately
        /// </summary>
        public Fixed64 BaseJumpHeight = DefaultBaseJumpHeight;

        /// <summary>
        /// We add extraHeight units(meters) on top when holding the button down longer while jumping
        /// </summary>
        public Fixed64 ExtraJumpHeight = DefaultExtraJumpHeight;

        /// <summary>
        /// How much does the character jump out perpendicular to the surface on walkable surfaces?
        ///  0 means a fully vertical jump and 1 means fully perpendicular.
        /// </summary>
        public Fixed64 PerpendicularJumpAmount = DefaultPerpendicularJumpAmount;

        /// <summary>
        /// How much does the character jump out perpendicular to the surface on too steep surfaces?
        /// 0 means a fully vertical jump and 1 means fully perpendicular.
        /// </summary>
        public Fixed64 SteepPerpendicularJumpAmount = DefaultSteepPerpendicularJumpAmount;

        // TODO: try replacing this with lockstep framecount
        /// <summary>
        /// The time we jumped at (Used to determine for how long to apply extra jump power after jumping.)
        /// </summary>
        public int StartJumpFrame { get; internal set; }

        public Fixed64 CooldownTime = DefaultCooldownTime;

        public Fixed64 AvoidGroundingAfterJumpTime = DefaultAvoidGroundingAfterJumpTime;

        internal Vector3d GetGroundedJumpForce(Vector3d currentForce, Vector3d groundNormal, bool isToSteep)
        {
            // Calculate the jumping direction
            Fixed64 slerpAmount = isToSteep ? SteepPerpendicularJumpAmount : PerpendicularJumpAmount;
            JumpDirection = Vector3d.Slerp(Vector3d.Up, groundNormal, slerpAmount);

            // Apply the jumping force to the velocity. Cancel any vertical velocity first.
            currentForce.y = Fixed64.Zero;

            // From the jump height and gravity we deduce the upwards speed
            // for the character to reach at the apex.
            return currentForce += JumpDirection * FixedMath.Sqrt(BaseJumpHeight * 2);
        }

        internal Vector3d GetInAirJumpForce(Vector3d currentForce)
        {
            // When jumping up we don't apply gravity for some time when the user is holding the jump button.
            // This gives more control over jump height by pressing the button longer.
            if (!IsJumping && !IsHoldingJump)
                return Vector3d.Zero;

            // Calculate the duration that the extra jump force should have effect.
            // If we're still less than that duration after the jumping time, apply the force.
            int extraJumpLimit = StartJumpFrame + (int)(ExtraJumpHeight / FixedMath.Sqrt(BaseJumpHeight * 2));

            // Negate the gravity we just applied, except we push in jumpDir rather than jump upwards.
            if (TrailblazerSettings.FrameCount < extraJumpLimit)
                return currentForce += JumpDirection * TrailblazerSettings.FixedGravity * TrailblazerSettings.DeltaTime;

            return Vector3d.Zero;
        }
    }
}
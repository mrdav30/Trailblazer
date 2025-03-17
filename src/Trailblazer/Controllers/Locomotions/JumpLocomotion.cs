using FixedMathSharp;

namespace Trailblazer.Controllers.Locomotions
{
    /// <summary>
    /// Handles the scout’s jumping mechanics, including jump height, cooldown, and movement control while airborne.
    /// </summary>
    /// <remarks>
    /// This locomotion component determines how high the scout can jump, how much control they retain mid-air,  
    /// and enforces a cooldown period between consecutive jumps.
    /// </remarks>
    [System.Serializable]
    public class JumpLocomotion : ITransientLocomotion
    {
        #region Constants

        /// <summary>
        /// The default base jump height, representing how high the scout jumps when pressing and immediately releasing the jump button.
        /// </summary>
        public static readonly Fixed64 DefaultBaseJumpHeight = Fixed64.One;

        /// <summary>
        /// The additional height added when the jump button is held longer.
        /// </summary>
        public static readonly Fixed64 DefaultExtraJumpHeight = (Fixed64)2f;

        /// <summary>
        /// Determines how much the scout's jump direction is influenced by the slope of the surface they are on.
        /// </summary>
        public static readonly Fixed64 DefaultPerpendicularJumpAmount = (Fixed64)0.5f;

        /// <summary>
        /// Determines how much the scout's jump direction is influenced by steep slopes.
        /// </summary>
        public static readonly Fixed64 DefaultSteepPerpendicularJumpAmount = (Fixed64)0.5f;

        /// <summary>
        /// Determines how much movement input affects the scout while jumping.
        /// </summary>
        public static readonly Fixed64 DefaultJumpControlMultiplier = (Fixed64)0.375f; // 75% control when jumping

        /// <summary>
        /// The default cooldown time after a jump before the scout can jump again.
        /// </summary>
        public static readonly Fixed64 DefaultCooldownTime = (Fixed64)0.2f;

        /// <summary>
        /// The default duration after jumping during which ground checks should be ignored.
        /// </summary>
        public static readonly Fixed64 DefaultAvoidGroundingTimer = (Fixed64)0.05f;

        #endregion

        #region Configuration State

        /// <summary>
        /// Determines whether jumping is enabled.
        /// If disabled, the scout will be unable to jump.
        /// </summary>
        private bool _isEnabled = true;

        /// <summary>
        /// The cooldown time before another jump can be performed.
        /// </summary>
        public Fixed64 CooldownTime = DefaultCooldownTime;

        /// <summary>
        /// The duration after jumping where ground detection is temporarily disabled.
        /// </summary>
        public Fixed64 AvoidGroundingTimer = DefaultAvoidGroundingTimer;

        /// <summary>
        /// The base height the scout can jump when the button is pressed briefly.
        /// </summary>
        public Fixed64 BaseJumpHeight = DefaultBaseJumpHeight;

        /// <summary>
        /// Additional height gained when holding the jump button.
        /// </summary>
        public Fixed64 ExtraJumpHeight = DefaultExtraJumpHeight;

        /// <summary>
        /// Controls how much movement input affects the scout while jumping.
        /// Lower values reduce movement responsiveness.
        /// </summary>
        public Fixed64 JumpControlMultiplier = DefaultJumpControlMultiplier;

        /// <summary>
        /// Controls how much the scout jumps out perpendicular to the surface on walkable terrain.
        /// A value of 0 means fully vertical, and 1 means fully perpendicular.
        /// </summary>
        public Fixed64 PerpendicularJumpAmount = DefaultPerpendicularJumpAmount;

        /// <summary>
        /// Controls how much the scout jumps out perpendicular to the surface on steep terrain.
        /// A value of 0 means fully vertical, and 1 means fully perpendicular.
        /// </summary>
        public Fixed64 SteepPerpendicularJumpAmount = DefaultSteepPerpendicularJumpAmount;

        #endregion

        #region Transient State

        /// <inheritdoc cref="ILocomotion.IsEnabled"/>
        public bool IsEnabled
        {
            get => _isEnabled;
            set
            {
                _isEnabled = value;
                if (!_isEnabled)
                    ClearState();
            }
        }

        /// <summary>
        /// Indicates whether the scout is currently performing a jump.
        /// </summary>
        /// <remarks>
        /// This is true if the jump button was pressed and the scout is not grounded.
        /// </remarks>
        [Transient]
        public bool IsJumping { get; set; }

        /// <summary>
        /// Indicates whether the scout is holding the jump button.
        /// </summary>
        [Transient]
        public bool IsHoldingJump { get; set; }

        /// <summary>
        /// The simulation frame when the scout started jumping.
        /// </summary>
        [Transient]
        public int FrameStartJump { get; set; }

        /// <summary>
        /// The direction in which the scout jumped during the current frame.
        /// </summary>
        [Transient]
        public Vector3d FrameJumpDirection { get; set; }

        /// <summary>
        /// The elapsed time in the cooldown state.
        /// </summary>
        [Transient]
        public Fixed64 CooldownTimer { get; private set; }

        /// <summary>
        /// Indicates whether the scout is currently in a jump cooldown period.
        /// </summary>
        [Transient]
        public bool IsCoolingDown { get; private set; }

        #endregion

        /// <summary>
        /// Starts the jump cooldown period, preventing another jump until the cooldown expires.
        /// </summary>
        public void StartCooldown()
        {
            IsCoolingDown = true;
            CooldownTimer = Fixed64.Zero;
        }

        /// <summary>
        /// Updates the jump cooldown timer, resetting the jump state when the cooldown expires.
        /// </summary>
        public void UpdateCooldown()
        {
            if (!IsCoolingDown)
                return;
            CooldownTimer += TrailblazerManager.DeltaTime;
            if (CooldownTimer >= CooldownTime)
            {
                CooldownTimer = Fixed64.Zero;
                IsCoolingDown = false;
            }
        }

        /// <summary>
        /// Synchronizes the jump state with another <see cref="JumpLocomotion"/> instance.
        /// </summary>
        /// <param name="locomotion">The locomotion instance to sync with.</param>
        public void SyncState(ITransientLocomotion locomotion)
        {
            if (locomotion is not JumpLocomotion other) return;
            this.SyncTransientState(other);
        }

        /// <summary>
        /// Resets all jump-related properties, clearing the cooldown and jump state.
        /// </summary>
        public void ClearState()
        {
            this.ClearTransientState();
        }
    }
}
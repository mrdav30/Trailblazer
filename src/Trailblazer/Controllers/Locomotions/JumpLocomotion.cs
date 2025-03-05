using FixedMathSharp;

namespace Trailblazer.Controllers.Locomotions
{
    /// <summary>
    /// The state of the jump
    /// </summary>
    public enum JumpState
    {
        Ready,
        CoolingDown
    }

    /// <summary>
    /// A helper class for the jump locomotion.
    /// </summary>  
    [System.Serializable]
    public class JumpLocomotion: ITransientLocomotion
    {
        #region Constants

        /// <summary>
        /// The default base jump height.
        /// </summary>
        public static readonly Fixed64 DefaultBaseJumpHeight = Fixed64.One;

        /// <summary>
        /// The default extra jump height.
        /// </summary>
        public static readonly Fixed64 DefaultExtraJumpHeight = (Fixed64)2f;

        /// <summary>
        /// The default perpendicular jump amount.
        /// </summary>
        public static readonly Fixed64 DefaultPerpendicularJumpAmount = (Fixed64)0.5f;

        /// <summary>
        /// The default steep perpendicular jump amount.
        /// </summary>  
        public static readonly Fixed64 DefaultSteepPerpendicularJumpAmount = (Fixed64)0.5f;

        /// <summary>
        /// The default cooldown time.
        /// </summary>
        public static readonly Fixed64 DefaultCooldownTime = (Fixed64)0.2f;

        /// <summary>
        /// The default avoid grounding after jump time.
        /// </summary>
        public static readonly Fixed64 DefaultAvoidGroundingTimer = (Fixed64)0.05f;

        #endregion

        #region Configuration State

        /// <summary>
        /// Can the character jump?
        /// </summary>
        private bool _isEnabled = true;

        /// <summary>
        /// The time we jumped at (Used to determine for how long to apply extra jump power after jumping.)
        /// </summary>
        public Fixed64 CooldownTime = DefaultCooldownTime;

        public Fixed64 AvoidGroundingTimer = DefaultAvoidGroundingTimer;

        /// <summary>
        /// How high do we jump when pressing jump and letting go immediately
        /// </summary>
        public Fixed64 BaseJumpHeight = DefaultBaseJumpHeight;

        /// <summary>
        /// We add extraHeight units(meters) on top when holding the button down longer while jumping
        /// </summary>
        public Fixed64 ExtraJumpHeight = DefaultExtraJumpHeight;

        public Fixed64 JumpControlMultiplier = Fixed64.FromRaw(0x60000000L); // 75% control when jumping

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
        /// Are we jumping?(Initiated with jump button and not grounded yet)
        /// To see if we are just in the air(initiated by jumping OR falling) see the grounded variable.
        /// </summary>
        public bool IsJumping { get; set; }

        /// <summary>
        /// Are we holding the jump button?
        /// </summary>
        public bool IsHoldingJump { get; set; }

        /// <summary>
        /// The frame we started jumping at
        /// </summary>
        public int FrameStartJump { get; set; }

        /// <summary>
        /// The direction we jumped in the current frame
        /// </summary>
        public Vector3d FrameJumpDirection { get; set; }

        /// <summary>
        /// Set to false during cooldown
        /// </summary>
        public JumpState State { get; private set; }

        /// <summary>
        /// The number of seconds (in delta time) we are in the cooldown state
        /// </summary>
        public Fixed64 CooldownTimer { get; private set; }

        /// <summary>
        /// Is the jump on cooldown?
        /// </summary>
        public bool IsCoolingDown => State == JumpState.CoolingDown;

        #endregion

        /// <summary>
        /// Start the cooldown
        /// </summary>
        public void StartCooldown()
        {
            State = JumpState.CoolingDown;
            CooldownTimer = Fixed64.Zero;
        }

        /// <summary>
        /// Update the cooldown
        /// </summary>
        public void UpdateCooldown()
        {
            if (State == JumpState.Ready)
                return;
            CooldownTimer += TrailblazerManager.DeltaTime;
            if (CooldownTimer >= CooldownTime)
            {
                CooldownTimer = Fixed64.Zero;
                State = JumpState.Ready;
            }
        }

        public void SyncState(ITransientLocomotion locomotion)
        {
            if (locomotion is not JumpLocomotion other) return;

            IsJumping = other.IsJumping;
            IsHoldingJump = other.IsHoldingJump;
            State = other.State;
            CooldownTimer = other.CooldownTimer;
            FrameStartJump = other.FrameStartJump;
            FrameJumpDirection = other.FrameJumpDirection;
        }

        /// <inheritdoc cref="ITransientLocomotion.ClearState"/>
        public void ClearState()
        {
            IsJumping = false;
            IsHoldingJump = false;
            State = JumpState.Ready;
            FrameStartJump = 0;
            FrameJumpDirection = Vector3d.Zero;
            CooldownTimer = Fixed64.Zero;
        }
    }
}
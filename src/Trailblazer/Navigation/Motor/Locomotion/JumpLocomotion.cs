using FixedMathSharp;
using Trailblazer.Support;
using Trailblazer.Serialization;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Handles the scout’s jumping mechanics, including jump height, cooldown, and movement control while airborne.
/// </summary>
/// <remarks>
/// This locomotion component determines how high the scout can jump, how much control they retain mid-air,  
/// and enforces a cooldown period between consecutive jumps.
/// </remarks>
public class JumpLocomotion : ILocomotion
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
    /// Maximum number of consecutive jumps allowed (e.g., 2 = double jump).
    /// </summary>
    public int MaxJumpCount = 1;

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
                this.ClearTransientState();
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
    public Fixed64 JumpStartTime { get; set; }

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

    /// <summary>
    /// The current number of jumps performed since the last grounding.
    /// </summary>
    [Transient]
    public int JumpCount { get; private set; }

    /// <summary>
    /// Returns true if more jumps are allowed.
    /// </summary>
    public bool CanJump => JumpCount < MaxJumpCount && !IsCoolingDown;

    #endregion

    #region Methods

    /// <summary>
    /// Increments the jump counter.
    /// </summary>
    public void RegisterJump()
    {
        JumpCount++;
        IsJumping = true;
        IsHoldingJump = true;
        JumpStartTime = TrailblazerManager.TotalTime;

        // Start cooldown only if this was the last allowed jump
        if (JumpCount >= MaxJumpCount)
            StartCooldown();
    }

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
    /// Resets jump state upon grounding.
    /// </summary>
    public void ResetJumpCounter()
    {
        JumpCount = 0;
        IsJumping = false;
        IsHoldingJump = false;
    }

    #endregion

    /// <inheritdoc />
    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref _isEnabled, "isEnabled", true);
        RecordValues.Look(chronicler, ref MaxJumpCount, "maxJumpCount", 1);
        RecordValues.Look(chronicler, ref CooldownTime, "cooldownTime", DefaultCooldownTime);
        RecordValues.Look(chronicler, ref AvoidGroundingTimer, "avoidGroundingTimer", DefaultAvoidGroundingTimer);
        RecordValues.Look(chronicler, ref BaseJumpHeight, "baseJumpHeight", DefaultBaseJumpHeight);
        RecordValues.Look(chronicler, ref ExtraJumpHeight, "extraJumpHeight", DefaultExtraJumpHeight);
        RecordValues.Look(chronicler, ref JumpControlMultiplier, "jumpControlMultiplier", DefaultJumpControlMultiplier);
        RecordValues.Look(chronicler, ref PerpendicularJumpAmount, "perpendicularJumpAmount", DefaultPerpendicularJumpAmount);
        RecordValues.Look(chronicler, ref SteepPerpendicularJumpAmount, "steepPerpendicularJumpAmount", DefaultSteepPerpendicularJumpAmount);

        bool isJumping = IsJumping;
        bool isHoldingJump = IsHoldingJump;
        Fixed64 jumpStartTime = JumpStartTime;
        Vector3d frameJumpDirection = FrameJumpDirection;
        Fixed64 cooldownTimer = CooldownTimer;
        bool isCoolingDown = IsCoolingDown;
        int jumpCount = JumpCount;

        RecordValues.Look(chronicler, ref isJumping, "isJumping", false);
        RecordValues.Look(chronicler, ref isHoldingJump, "isHoldingJump", false);
        RecordValues.Look(chronicler, ref jumpStartTime, "jumpStartTime", Fixed64.Zero);
        RecordValues.Look(chronicler, ref frameJumpDirection, "frameJumpDirection", Vector3d.Zero);
        RecordValues.Look(chronicler, ref cooldownTimer, "cooldownTimer", Fixed64.Zero);
        RecordValues.Look(chronicler, ref isCoolingDown, "isCoolingDown", false);
        RecordValues.Look(chronicler, ref jumpCount, "jumpCount", 0);

        if (chronicler.Mode == SerializationMode.Loading)
        {
            IsJumping = isJumping;
            IsHoldingJump = isHoldingJump;
            JumpStartTime = jumpStartTime;
            FrameJumpDirection = frameJumpDirection;
            CooldownTimer = cooldownTimer;
            IsCoolingDown = isCoolingDown;
            JumpCount = jumpCount;

            if (!_isEnabled)
                this.ClearTransientState();
        }
    }
}

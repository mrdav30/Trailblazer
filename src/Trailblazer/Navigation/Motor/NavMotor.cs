using Chronicler;
using FixedMathSharp;
using SwiftCollections;
using System;
using System.Runtime.CompilerServices;

#if DEBUG
using System.Diagnostics;
#endif

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Controls character movement using an acceleration-based approach in a deterministic, lockstep simulation.
/// </summary>
/// <remarks>
/// This controller processes movement requests, applies forces such as gravity and platform adjustments, 
/// and finalizes traversal states for consistent movement across frames.
/// </remarks>
[Serializable]
public class NavMotor : IRecordable
{
    #region Fields & Properties

    /// <summary>
    /// Manages locomotion states and behaviors.
    /// </summary>
    public LocomotionHandler Handler = new();

    [NonSerialized]
    public NavMotorEvents Events = new();

    /// <summary>
    /// Optional host-owned resolver that supplies deterministic climb affordance snapshots.
    /// </summary>
    [NonSerialized]
    public IClimbAffordanceResolver? ClimbResolver;

    /// <summary>
    /// This stores the current <see cref="Navigator._frameCondition"/> for the frame.  
    /// </summary>
    /// <remarks>
    /// This is set on <see cref="OnInitialize"/>, can be explicitly synchronized before traversal
    /// through <see cref="SyncTraversalState(TrekCondition, bool)"/>, and is refreshed at the end of
    /// the frame in <see cref="FinalizeTraversal"/>.
    /// </remarks>
    public TransitState CurrentState { get; private set; } = null!;

    /// <summary>
    /// Indicates whether a traversal has started for the current frame and still requires finalization or abort.
    /// </summary>
    public bool TraversalInProgress { get; private set; }

    public bool IsInitialized { get; private set; }

    private PlatformLocomotion? PlatformModule => Handler.Platform;

    private JumpLocomotion? JumpModule => Handler.Jump;

    private SlideLocomotion? SlideModule => Handler.Slide;

    private SwimLocomotion? SwimModule => Handler.Swim;

    private FlyLocomotion? FlyModule => Handler.Fly;

    private ClimbLocomotion? ClimbModule => Handler.Climb;

    #region Cache

    /// <summary>
    /// Stores the slope angle for the current frame based on <see cref="TrekRequest.Direction"/>.
    /// </summary>
    public Fixed64 FrameSlopeAngle { get; private set; }

    /// <summary>
    /// Accumulates forces applied during the traversal phase before they are committed.
    /// </summary>
    private Vector3d _forceOutput;

    /// <summary>
    /// Records the simulation frame that opened the current traversal so stale traversal usage can be detected.
    /// </summary>
    private int _pendingTraversalFrame = -1;

    #endregion

    #region State Status

    /// <summary>
    /// Indicates if the traversal medium has changed since the last frame.
    /// </summary>
    public bool StateChanged => CurrentState.Medium != CurrentState.PreviousMedium
        && CurrentState.Medium != TraversalMedium.Unknown
        && CurrentState.PreviousMedium != TraversalMedium.Unknown;

    /// <summary>
    /// Determines if the navigator is currently on the ground.
    /// </summary>
    public bool IsOnSolid => CurrentState.Medium == TraversalMedium.Solid;

    /// <summary>
    /// Determines if the navigator was on the ground in the previous frame.
    /// </summary>
    public bool WasOnSolid => CurrentState.PreviousMedium == TraversalMedium.Solid;

    /// <summary>
    /// Determines if the navigator is currently in the air.
    /// </summary>
    public bool IsInGas => CurrentState.Medium == TraversalMedium.Gas;

    /// <summary>
    /// Determines if the navigator was in the air in the previous frame.
    /// </summary>
    public bool WasInGas => CurrentState.PreviousMedium == TraversalMedium.Gas;

    /// <summary>
    /// Determines if the navigator is currently in water.
    /// </summary>
    public bool IsInLiquid => CurrentState.Medium == TraversalMedium.Liquid;

    /// <summary>
    /// Determines if the navigator was in water in the previous frame.
    /// </summary>
    public bool WasInLiquid => CurrentState.PreviousMedium == TraversalMedium.Liquid;

    /// <summary>
    /// Determines if the navigator is currently under active flight control.
    /// </summary>
    public bool IsFlying => FlyModule?.IsFlying == true;

    /// <summary>
    /// Determines if the navigator is currently in a jump state.
    /// </summary>
    public bool IsJumping => JumpModule?.IsJumping == true;

    public bool IsFalling => Handler.Fall.IsFalling;

    /// <summary>
    /// Determines if the navigator is currently attached to a climb affordance.
    /// </summary>
    public bool IsClimbing => ClimbModule?.IsClimbing == true;

    /// <summary>
    /// Checks if the navigator is in a state where it is airborne but not actively jumping, flying, or falling.
    /// </summary>
    public bool InLimbo => !IsOnSolid && !IsInGas && !IsInLiquid
        || IsInGas && !IsFlying && !IsJumping && !IsFalling && !IsClimbing;

    #endregion

    #endregion

    #region Construct

    /// <summary>
    /// Creates a new <see cref="NavMotor"/> instance and initializes it with the provided navigator.
    /// </summary>
    /// <param name="initialCondition">The initial traversal condition of the navigator</param>
    /// <param name="profile">Optional locomotion composition profile. When omitted, the default profile is used.</param>
    /// <returns>A new instance of <see cref="NavMotor"/>.</returns>
    public static NavMotor CreateNew(TrekCondition initialCondition, LocomotionProfile? profile = null) => new(initialCondition, profile);

    // Parameterless constructor for serialization purposes. Not intended for direct use.
    private NavMotor() { }

    /// <summary>
    /// Initializes a new instance of the <see cref="NavMotor"/> class.
    /// </summary>
    /// <param name="condition">The initial traversal condition of the navigator</param>
    /// <param name="profile">Optional locomotion composition profile. When omitted, the default profile is used.</param>
    public NavMotor(TrekCondition condition, LocomotionProfile? profile = null) => OnInitialize(condition, profile);

    /// <summary>
    /// Prepares the controller by linking it to the given navigator and setting initial state values.
    /// </summary>
    /// <param name="condition">The initial traversal condition of the navigator</param>
    /// <param name="profile">Optional locomotion composition profile. When omitted, the default profile is used.</param>
    public void OnInitialize(TrekCondition condition, LocomotionProfile? profile = null)
    {
        Handler = profile != null ? new LocomotionHandler(profile) : new LocomotionHandler();
        CurrentState = new TransitState(condition);
        if (CurrentState.GroundState.HasValue && PlatformModule != null)
            PlatformModule.HandlePlatformChange(CurrentState.GroundState); // set the initial platform

        IsInitialized = true;
    }

    /// <summary>
    /// Replaces the currently installed locomotion profile.
    /// </summary>
    public void SetLocomotionProfile(LocomotionProfile profile)
    {
        SwiftThrowHelper.ThrowIfNull(profile, nameof(profile));

        if (TraversalInProgress)
            throw new InvalidOperationException("Cannot change locomotion composition while a traversal frame is in progress.");

        Handler.ApplyProfile(profile);

        if (CurrentState?.GroundState.HasValue == true && PlatformModule != null)
            PlatformModule.HandlePlatformChange(CurrentState.GroundState);
    }

    /// <summary>
    /// Reconfigures the currently installed locomotion profile from the active handler state.
    /// </summary>
    public void ConfigureLocomotions(Action<LocomotionProfileBuilder> configure)
    {
        SwiftThrowHelper.ThrowIfNull(configure, nameof(configure));

        var builder = LocomotionProfile.CreateBuilder(Handler);
        configure(builder);
        SetLocomotionProfile(builder.Build());
    }

    #endregion

    #region Phase 1 - Request Traversal

    /// <summary>
    /// Processes a movement request and applies necessary forces.
    /// </summary>
    /// <remarks>
    /// This method opens a traversal phase for the current frame so duplicate force accumulation is rejected
    /// until the host calls <see cref="FinalizeTraversal"/> or <see cref="AbortTraversalFrame"/>.
    /// Movement forces such as gravity, jump, and platform adjustments are applied.
    /// </remarks>
    /// <param name="request">The movement request containing desired movement parameters</param>
    /// <param name="velocityDelta">The resulting velocity change to apply to the navigator</param>
    /// <param name="positionDelta">The resulting position change from platform movement to apply to the navigator</param>
    /// <param name="rotationDelta">The resulting rotation change from platform movement to apply to the navigator</param>
    public bool TryTraversal(
        TrekRequest request,
        out Vector3d velocityDelta,
        out Vector3d positionDelta,
        out FixedQuaternion rotationDelta)
    {
        ResetTraversalOutputs(out velocityDelta, out positionDelta, out rotationDelta);
        if (!TryBeginTraversalFrame())
            return false;

        PrepareTraversalState(request);
        ResolveTraversalForces(request);

        velocityDelta = ResolveTraversalVelocityDelta();
        ResolvePlatformTraversal(request, ref positionDelta, ref rotationDelta);
        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void ResetTraversalOutputs(
        out Vector3d velocityDelta,
        out Vector3d positionDelta,
        out FixedQuaternion rotationDelta)
    {
        velocityDelta = Vector3d.Zero;
        positionDelta = Vector3d.Zero;
        rotationDelta = FixedQuaternion.Identity;
    }

    private bool TryBeginTraversalFrame()
    {
        if (!IsInitialized)
            return false;

        if (TraversalInProgress)
        {
            if (TrailblazerManager.FrameCount != _pendingTraversalFrame)
            {
                throw new InvalidOperationException(
                    $"NavMotor traversal from frame {_pendingTraversalFrame} was never finalized or aborted before frame {TrailblazerManager.FrameCount}. Call FinalizeTraversal(...) or AbortTraversalFrame() in the same frame that opened traversal.");
            }

            return false;
        }

        TraversalInProgress = true;
        _pendingTraversalFrame = TrailblazerManager.FrameCount;
        return true;
    }

    private void PrepareTraversalState(TrekRequest request)
    {
#if DEBUG
        Debug.WriteLine($"NavMotor State: " +
            $"Grounded={IsOnSolid}, " +
            $"InAir={IsInGas}, " +
            $"InWater={IsInLiquid}, " +
            $"Flying={IsFlying}, " +
            $"InLimbo={InLimbo}, " +
            $"Velocity={Handler.Move.FrameVelocity}");
#endif

        // Calculate the slope angle for the current frame based on the movement direction and surface normal.
        FrameSlopeAngle = CurrentState.GetSignedSlopeAngle(request.Direction);

        // Store the current velocity for manipulation.
        _forceOutput = Handler.Move.FrameVelocity;

        // Update platform velocity prior to applying jump force.
        PlatformModule?.UpdatePlatformVelocity();

        if (InLimbo)
            Handler.IsInControl = false;

        if (JumpModule?.IsCoolingDown == true)
            JumpModule.UpdateCooldown();

        UpdateClimbState(request);
        UpdateFlightState(request);
    }

    private void ResolveTraversalForces(TrekRequest request)
    {
        ComputeMovementForces(request);

        // Reset this before applying gravity.
        if (JumpModule != null && (!request.IsRequestingJump || !JumpModule.CanJump))
            JumpModule.IsHoldingJump = false;

        ApplyEnvironmentalForces();
        ApplyJumpForce(request);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector3d ResolveTraversalVelocityDelta()
    {
        return _forceOutput != Vector3d.Zero
            ? _forceOutput * TrailblazerManager.DeltaTime
            : Vector3d.Zero;
    }

    private void ResolvePlatformTraversal(
        TrekRequest request,
        ref Vector3d positionDelta,
        ref FixedQuaternion rotationDelta)
    {
        PlatformLocomotion? platformModule = PlatformModule;
        if (platformModule == null)
            return;

        // Do not apply platform movement if we just jumped or if the platform is not actively driving us.
        bool isMovingWithPlatform = platformModule.IsActive
            && !IsFlying
            && !IsClimbing
            && (IsOnSolid || platformModule.IsLockedToPlatform);
        if (!isMovingWithPlatform || IsJumping)
            return;

        platformModule.GetPlatformInfluence(
            request.FootPosition ?? request.Origin,
            request.Rotation,
            out positionDelta,
            out rotationDelta);
    }

    /// <summary>
    /// Computes the movement forces based on the traversal state and applies them to the navigator.
    /// </summary>
    /// <remarks>
    /// This method determines whether the navigator is in control, calculates velocity adjustments,
    /// and applies constraints such as slope resistance and airborne drag.
    /// </remarks>
    private void ComputeMovementForces(TrekRequest frameRequest)
    {
        UpdateControlState();

        if (InLimbo)
            return;

        ResetDownwardMomentumIfNeeded();

        Vector3d desiredVelocity = GetDesiredVelocity(frameRequest);

        if (TryApplyStationaryGroundFriction(frameRequest.Direction))
            return;

        ApplyDesiredVelocity(desiredVelocity);
    }

    /// <summary>
    /// Determines the desired velocity based on input direction, movement constraints, and traversal state.
    /// </summary>
    /// <returns>The computed velocity vector that the navigator should move toward.</returns>
    private Vector3d GetDesiredVelocity(TrekRequest frameRequest)
    {
        if (IsFlying)
            return GetFlightVelocity(frameRequest);

        if (ClimbModule?.IsMantling == true)
            return GetMantleVelocity(frameRequest.Origin);

        if (IsClimbing)
            return GetClimbVelocity(frameRequest);

        Vector3d result = GetControlledSurfaceVelocity(frameRequest);
        if (IsInLiquid)
            return ResolveLiquidVelocity(frameRequest, result);

        result = ApplyPlatformTransferVelocity(result);
        return ApplyGroundVelocityConstraints(result);
    }

    /// <summary>
    /// Computes the desired world-space velocity while controlled flight is active.
    /// </summary>
    private Vector3d GetFlightVelocity(TrekRequest frameRequest)
    {
        if (FlyModule?.IsEnabled != true || !FlyModule.CanFly || frameRequest.Rate == TrekRate.Stationary)
            return Vector3d.Zero;

        Fixed64 speedMultiplier = GetFlightSpeedMultiplier(frameRequest.Rate);
        if (speedMultiplier <= Fixed64.Zero)
            return Vector3d.Zero;

        Fixed3x3 transposedMatrix = frameRequest.Rotation.ToMatrix3x3();
        Vector3d desiredLocalDirection = Fixed3x3.InverseTransformDirection(transposedMatrix, frameRequest.Direction);
        Vector3d desiredLocalVelocity = Vector3d.Zero;

        Vector3d horizontalInput = new(desiredLocalDirection.x, Fixed64.Zero, desiredLocalDirection.z);
        Fixed64 horizontalMagnitude = FixedMath.Clamp01(horizontalInput.Magnitude);
        if (horizontalMagnitude > Fixed64.Zero)
            desiredLocalVelocity += horizontalInput.Normal * (FlyModule.MaxFlySpeed * speedMultiplier * horizontalMagnitude);

        Fixed64 verticalInput = FixedMath.Clamp(desiredLocalDirection.y, -Fixed64.One, Fixed64.One);
        if (verticalInput > Fixed64.Zero)
            desiredLocalVelocity.y = verticalInput * FlyModule.MaxAscendSpeed * speedMultiplier;
        else if (verticalInput < Fixed64.Zero)
            desiredLocalVelocity.y = verticalInput.Abs() * -FlyModule.MaxDescendSpeed * speedMultiplier;

        return Fixed3x3.TransformDirection(transposedMatrix, desiredLocalVelocity);
    }

    /// <summary>
    /// Computes the horizontal velocity based on movement input and current facing direction.
    /// </summary>
    /// <returns>The target horizontal velocity.</returns>
    private Vector3d GetHorizontalVelocity(TrekRequest frameRequest)
    {
        Fixed3x3 transposedMatrix = frameRequest.Rotation.ToMatrix3x3();
        Vector3d desiredLocalDirection = Fixed3x3.InverseTransformDirection(transposedMatrix, frameRequest.Direction);
        Fixed64 speed = MaxHoritzontalSpeedInDirection(desiredLocalDirection, frameRequest.Rate);

        speed *= Handler.Move.MoveSpeedMultiplier;

        // Modify max speed on slopes based on slope speed multiplier curve
        if (Handler.Move.ModifySpeedOnSlope && IsOnSlope(FrameSlopeAngle))
            speed *= Handler.Move.SlopeSpeedMultiplier.Evaluate(FrameSlopeAngle);

        return Fixed3x3.TransformDirection(transposedMatrix, desiredLocalDirection * speed);
    }

    /// <summary>
    /// Calculates the maximum allowable speed in a given movement direction.
    /// </summary>
    /// <param name="desiredMovementDirection">The movement direction to evaluate.</param>
    /// <param name="rate">The rate that dictates the maximum speed allowed.</param>
    /// <returns>The maximum speed possible in the specified direction.</returns>
    public Fixed64 MaxHoritzontalSpeedInDirection(Vector3d desiredMovementDirection, TrekRate rate)
    {
        if (desiredMovementDirection == Vector3d.Zero)
            return Fixed64.Zero;

        if (IsFlying)
            return GetFlightHorizontalSpeed(desiredMovementDirection, rate);

        if (IsInLiquid)
            return GetLiquidHorizontalSpeed(desiredMovementDirection);

        return GetGroundHorizontalSpeed(desiredMovementDirection, rate);
    }

    /// <summary>
    /// Retrieves the maximum acceleration value based on the navigator’s current traversal state.
    /// </summary>
    /// <returns>The acceleration limit depending on whether the navigator is grounded, airborne, or swimming.</returns>
    public Fixed64 GetMaxAcceleration()
    {
        if (CurrentState == null)
            throw new InvalidOperationException("NavMotor must be initialized before querying max acceleration.");

        if (IsInLiquid)
            return SwimModule?.IsEnabled == true && SwimModule.CanSwim
                ? SwimModule.MaxSwimAcceleration
                : Handler.Move.MaxAirAcceleration;

        if (IsClimbing)
            return ClimbModule?.IsEnabled == true && ClimbModule.CanClimb
                ? ClimbModule.MaxClimbAcceleration
                : Handler.Move.MaxAirAcceleration;

        if (IsFlying)
            return FlyModule?.IsEnabled == true && FlyModule.CanFly
                ? FlyModule.MaxFlyAcceleration
                : Handler.Move.MaxAirAcceleration;

        if (IsOnSolid) return Handler.Move.MaxGroundAcceleration;

        if (IsJumping || IsFalling || IsInGas)
            return Handler.Move.MaxAirAcceleration;

        throw new InvalidOperationException(
            $"Cannot resolve max acceleration while traversal medium is {CurrentState.Medium}. NavMotor requires a known traversal medium before movement forces are evaluated.");
    }

    /// <summary>
    /// Applies environmental forces such as gravity, water buoyancy, and downward force when grounded.
    /// </summary>
    private void ApplyEnvironmentalForces()
    {
        Fixed64 gravityStep = Handler.Forces.GravityForce * TrailblazerManager.DeltaTime;

        if (IsFlying)
        {
            Fixed64 gravityCompensation = FixedMath.Clamp(
                FlyModule?.GravityCompensation ?? Fixed64.Zero,
                Fixed64.Zero,
                Fixed64.One);
            _forceOutput.y -= gravityStep * (Fixed64.One - gravityCompensation);
            return;
        }

        if (IsClimbing)
        {
            Fixed64 gravityCompensation = FixedMath.Clamp(
                ClimbModule?.GravityCompensationWhileClimbing ?? Fixed64.Zero,
                Fixed64.Zero,
                Fixed64.One);
            _forceOutput.y -= gravityStep * (Fixed64.One - gravityCompensation);
            return;
        }

        if (IsOnSolid)
        {
            _forceOutput.y = FixedMath.Min(Fixed64.Zero, _forceOutput.y) - gravityStep;
            return;
        }

        if (IsInLiquid)
        {
            // Apply buoyancy if we can swim, otherwise apply gravity as normal.
            // Even if we can swim, we still apply gravity but reduce it based on the buoyancy factor to
            // create a more natural sinking effect when not actively swimming upwards.
            if (SwimModule?.IsEnabled == true)
                _forceOutput.y += gravityStep * (SwimModule.BuoyancyFactor - Fixed64.One);
            else
                _forceOutput.y = Handler.Move.FrameVelocity.y - gravityStep;

            return;
        }

        if (!IsInGas) return;

        _forceOutput.y = Handler.Move.FrameVelocity.y - gravityStep;

        // Ensure velocity does not exceed terminal fall speed
        Fixed64 terminalFallSpeed = Handler.Move.FrameVelocity.y + (_forceOutput.y * TrailblazerManager.DeltaTime);
        if (terminalFallSpeed < -Handler.Forces.TerminalVelocity)
            _forceOutput.y = -Handler.Forces.TerminalVelocity - Handler.Move.FrameVelocity.y;

        // When jumping up we don't apply gravity for some time when the user is holding the jump button.
        // This allows for more control over jump height by pressing the button longer.
        JumpLocomotion? jumpModule = JumpModule;
        if (IsJumping && jumpModule?.IsHoldingJump == true)
        {
            // Calculate the duration that the extra jump force should have effect.
            // If we're still less than that duration after the jumping time, apply the force.
            Fixed64 extraJumpLimit = (jumpModule.JumpStartTime + jumpModule.ExtraJumpHeight) / GetVerticalJumpSpeed();

            // Negate the gravity we just applied, except we push in jumpDir rather than jump upwards.
            if (TrailblazerManager.TotalTime <= extraJumpLimit)
                _forceOutput += jumpModule.FrameJumpDirection * gravityStep;
        }
    }

    /// <summary>
    /// Applies an instantaneous jump force to the navigator, considering platform inertia and jump physics.
    /// </summary>
    /// <remarks>
    /// This method validates jump conditions, determines the jump direction, and calculates the jump force.
    /// If the navigator is on a platform, its velocity is adjusted accordingly.
    /// </remarks>
    private void ApplyJumpForce(TrekRequest request)
    {
        if (!CanApplyJumpForce(request))
            return;

        Vector3d jumpForce = IsInLiquid
            ? GetWaterBreachJumpForce()
            : IsClimbing
                ? GetClimbDetachJumpForce()
                : GetGroundJumpForce();
        CommitJumpForce(jumpForce);
    }

    private bool CanApplyJumpForce(TrekRequest request)
    {
        if (!(JumpModule?.IsEnabled == true
            && Handler.IsInControl
            && request.IsRequestingJump))
        {
            return false;
        }

        if (IsFlying || IsFalling)
            return false;

        if (IsClimbing && !(ClimbModule?.ActiveAllowDetachJump ?? false))
            return false;

        if (IsInLiquid && !(SwimModule?.CanBreachWater ?? false))
            return false;

        if (!JumpModule.CanJump)
            return false;

        return request.CanAffordJump;
    }

    private Vector3d GetWaterBreachJumpForce()
    {
        JumpLocomotion? jumpModule = JumpModule;
        SwimLocomotion? swimModule = SwimModule;
        if (jumpModule == null || swimModule == null)
            return Vector3d.Zero;

        jumpModule.FrameJumpDirection = Vector3d.Up;
        Events.OnStartWaterBreach?.Invoke();
        return jumpModule.FrameJumpDirection * (GetVerticalJumpSpeed() * swimModule.BreachJumpMultiplier);
    }

    private Vector3d GetGroundJumpForce()
    {
        JumpLocomotion? jumpModule = JumpModule;
        if (jumpModule == null)
            return Vector3d.Zero;

        EnsureJumpDirectionInitialized();
        Events.OnStartJump?.Invoke(jumpModule.AvoidGroundingTimer);
        return jumpModule.FrameJumpDirection * GetVerticalJumpSpeed();
    }

    private Vector3d GetClimbDetachJumpForce()
    {
        JumpLocomotion? jumpModule = JumpModule;
        ClimbLocomotion? climbModule = ClimbModule;
        if (jumpModule == null || climbModule == null)
            return Vector3d.Zero;

        Vector3d upward = climbModule.AttachedUpDirection != Vector3d.Zero
            ? climbModule.AttachedUpDirection.Normal
            : Vector3d.Up;
        Vector3d outward = climbModule.AttachedSurfaceNormal != Vector3d.Zero
            ? climbModule.AttachedSurfaceNormal.Normal
            : Vector3d.Backward;
        jumpModule.FrameJumpDirection = Vector3d.Slerp(upward, outward, jumpModule.PerpendicularJumpAmount);
        Events.OnStartJump?.Invoke(jumpModule.AvoidGroundingTimer);
        return jumpModule.FrameJumpDirection * GetVerticalJumpSpeed();
    }

    private void EnsureJumpDirectionInitialized()
    {
        JumpLocomotion? jumpModule = JumpModule;
        if (jumpModule == null || jumpModule.IsJumping)
            return;

        Fixed64 slerpAmount = IsTooSteep(FrameSlopeAngle)
            ? jumpModule.SteepPerpendicularJumpAmount
            : jumpModule.PerpendicularJumpAmount;

        jumpModule.FrameJumpDirection = Vector3d.Slerp(
            Vector3d.Up,
            CurrentState.SurfaceNormal,
            slerpAmount);
    }

    private void CommitJumpForce(Vector3d jumpForce)
    {
        if (IsClimbing)
            StopClimb(wasForced: false);

        JumpLocomotion? jumpModule = JumpModule;
        if (jumpModule == null)
            return;

        jumpModule.RegisterJump();

        // Remove any existing downward force before the jump impulse is applied.
        _forceOutput.y = FixedMath.Max(Fixed64.Zero, _forceOutput.y);
        _forceOutput += jumpForce;
    }

    #endregion

    #region Phase 2 - Finalize 

    /// <summary>
    /// Finalizes traversal state updates and prepares the navigator for the next simulation frame.
    /// </summary>
    /// <remarks>
    /// This method updates the frame velocity, applies necessary adjustments based on traversal state changes,
    /// and processes platform movement or environmental effects as needed.
    /// </remarks>
    public void FinalizeTraversal(
        Vector3d newPosition,
        Vector3d lastPosition,
        FixedQuaternion newRotation,
        TrekCondition conditonRefresh,
        Vector3d? newFootPosition = null)
    {
        if (!ShouldFinalizeTraversal())
            return;

        ValidatePendingTraversalFrame();
        RefreshTraversalState(newPosition, lastPosition, conditonRefresh);
        HandleTraversalTransitions();
        HandleClimbState(newPosition);
        HandleSwimState(newPosition);
        HandleFlightState();
        HandleFallState(newPosition);
        FinalizePlatformMovement(newFootPosition ?? newPosition, newRotation);
        AbortTraversalFrame();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ShouldFinalizeTraversal()
    {
        return IsInitialized && TraversalInProgress;
    }

    private void ValidatePendingTraversalFrame()
    {
        if (TrailblazerManager.FrameCount == _pendingTraversalFrame)
            return;

        throw new InvalidOperationException(
            $"NavMotor traversal opened on frame {_pendingTraversalFrame} cannot be finalized on frame {TrailblazerManager.FrameCount}. Call AbortTraversalFrame() to discard stale traversal state before starting a new frame.");
    }

    private void RefreshTraversalState(
        Vector3d newPosition,
        Vector3d lastPosition,
        TrekCondition conditonRefresh)
    {
        Handler.Move.FrameVelocity = (newPosition - lastPosition) * TrailblazerManager.InvDeltaTime;

        CurrentState.Update(conditonRefresh, CurrentState.ToTrekCondition());

        PlatformModule?.HandlePlatformChange(CurrentState.GroundState);
        HandlePlatformTransitions();

        // Ceiling check runs last so platform inertia inherited this frame cannot bypass the clamp.
        CheckJumpStatus(newPosition);
    }

    private void HandleTraversalTransitions()
    {
        if (WasInGas && !IsInGas)
            HandleGasExitTransition();

        if (SwimModule?.IsEnabled == true && !IsInLiquid && WasInLiquid)
            Handler.ClearTransientState<SwimLocomotion>();
    }

    private void HandleClimbState(Vector3d position)
    {
        ClimbLocomotion? climbModule = ClimbModule;
        if (climbModule?.IsEnabled != true)
            return;

        if (climbModule.IsMantling)
        {
            if (IsInLiquid || CurrentState.Medium == TraversalMedium.Unknown)
            {
                StopClimb(wasForced: true);
                return;
            }

            if (IsOnSolid)
            {
                CompleteMantle();
                return;
            }

            if (!CanContinueActiveMantle())
            {
                StopClimb(wasForced: true);
                return;
            }

            if (HasReachedMantleTarget(position))
                CompleteMantle();

            return;
        }

        if ((IsInLiquid || CurrentState.Medium == TraversalMedium.Unknown) && climbModule.IsClimbing)
            StopClimb(wasForced: false);
    }

    private void HandleGasExitTransition()
    {
        if (IsJumping)
        {
            JumpLocomotion? jumpModule = JumpModule;
            if (jumpModule == null)
                return;

            // Reset cooldown on landing.
            jumpModule.ResetJumpCounter();

            if (IsInLiquid)
                Events.OnStopWaterBreach?.Invoke();
            else
                Events.OnStopJump?.Invoke();

            return;
        }

        if (!IsInLiquid)
            Events.OnLandedFall?.Invoke();
    }

    private void FinalizePlatformMovement(Vector3d position, FixedQuaternion rotation)
    {
        if (PlatformModule?.IsActive != true || (!IsOnSolid && !PlatformModule.IsLockedToPlatform))
            return;

        PlatformModule.HandlePlatformMovement(position, rotation);
    }

    private void CheckJumpStatus(Vector3d position)
    {
        // Make sure we aren't hitting the ceiling
        if (Handler.Move.FrameVelocity.y <= Fixed64.Zero || CurrentState.CeilingLevel == Fixed64.MAX_VALUE)
            return;

        if (position.y <= CurrentState.CeilingLevel) return;

        Handler.Move.FrameVelocity = new(
            Handler.Move.FrameVelocity.x,
            Fixed64.Zero,
            Handler.Move.FrameVelocity.z);

        if (JumpModule != null)
        {
            JumpModule.IsJumping = false;
            JumpModule.IsHoldingJump = false;
        }
    }

    /// <summary>
    /// Handles platform updates by applying inertia and adjusting velocity based on platform motion.
    /// </summary>
    /// <remarks>
    /// This method ensures that velocity transitions smoothly when stepping onto or off moving platforms.
    /// </remarks>
    private void HandlePlatformTransitions()
    {
        // Don't process platform state when in water
        if (PlatformModule?.IsEnabled != true || IsInLiquid)
            return;

        bool isReleasing = false;
        if (PlatformModule.IsHoldingPlatform)
            isReleasing = PlatformModule.TickHoldOnPlatform();

        if (isReleasing)
        {
            Handler.Move.FrameVelocity -= PlatformModule.PlatformVelocity;
            return;
        }

        if (!PlatformModule.InteriaApplied) return;

        if (WasOnSolid && IsInGas)
        {
            // Scout just left the ground, so it inherits platform inertia into its new velocity.
            PlatformModule.FramePlatformVelocity = PlatformModule.PlatformVelocity;
            Handler.Move.FrameVelocity += PlatformModule.PlatformVelocity;
            return;
        }

        if (WasInGas && IsOnSolid)
        {
            if (PlatformModule.IsNewPlatform)
                // If navigator landed on a new platform, we have to wait for two frames
                // before we know the new velocity of the platform under the navigator
                PlatformModule.SetHoldPlatform(PlatformModule.ActivePlatform);
            else
                // If the platform isn’t new, we assume the navigator landed back on the same platform
                // and subtract platform velocity to prevent doubling the effect.
                Handler.Move.FrameVelocity -= PlatformModule.PlatformVelocity;
        }
    }

    /// <summary>
    /// Manages the navigator's state when entering, exiting, or moving within water.
    /// </summary>
    /// <remarks>
    /// This method updates swim-related properties, tracks dive time, and triggers drowning events if necessary.
    /// </remarks>
    private void HandleSwimState(Vector3d position)
    {
        if (!IsInLiquid)
        {
            if (SwimModule?.IsEnabled == true && WasInLiquid)
                Handler.ClearTransientState<SwimLocomotion>();

            return;
        }

        // Clear the transient state when entering water for the first time
        if (!WasInLiquid)
            Handler.ClearAllTransientState();

        if (SwimModule?.IsEnabled == true)
        {
            SwimModule.IsSwimming = SwimModule.CanSwim;
            SwimModule.IsDiving = position.y < CurrentState.SurfaceLevel;

            SwimModule.UpdateDiveTime();

            if (IsInLiquid && SwimModule.IsDrowning)
                Events.OnDrowning?.Invoke(SwimModule.UnderwaterTimer);
        }
    }

    /// <summary>
    /// Clears or preserves flight state based on the refreshed traversal medium.
    /// </summary>
    private void HandleFlightState()
    {
        if (FlyModule?.IsEnabled != true)
            return;

        if (!IsInGas || IsClimbing)
        {
            if (FlyModule.IsFlying)
                Handler.ClearTransientState<FlyLocomotion>();

            return;
        }

        if (FlyModule.IsFlying && IsFalling)
            Handler.ClearTransientState<FallLocomotion>();
    }

    /// <summary>
    /// Processes the navigator’s fall state, tracking fall height and triggering landing events when appropriate.
    /// </summary>
    /// <remarks>
    /// This method determines when a navigator starts falling, updates fall height, and detects when a safe landing occurs.
    /// </remarks>
    private void HandleFallState(Vector3d position)
    {
        if (!Handler.Fall.IsEnabled) return;

        if (ShouldClearActiveFallState())
        {
            ClearFallState();
            return;
        }

        if (IsFalling)
        {
            UpdateActiveFallState(position);
            return;
        }

        TryStartFall(position);
    }

    /// <summary>
    /// Updates the active flight state from the current frame request.
    /// </summary>
    private void UpdateFlightState(TrekRequest request)
    {
        if (FlyModule?.IsEnabled != true)
            return;

        bool shouldFly = request.IsRequestingFlight
            && FlyModule.CanFly
            && !IsClimbing
            && !IsInLiquid
            && CurrentState.Medium != TraversalMedium.Unknown;

        if (!shouldFly)
        {
            FlyModule.IsFlying = false;
            return;
        }

        FlyModule.IsFlying = true;

        if (IsFalling)
            Handler.ClearTransientState<FallLocomotion>();

        if (JumpModule != null)
        {
            JumpModule.IsJumping = false;
            JumpModule.IsHoldingJump = false;
        }

        if (SlideModule != null)
            SlideModule.IsSliding = false;
    }

    /// <summary>
    /// Scales configured flight speeds by the requested traversal rate.
    /// </summary>
    private Fixed64 GetFlightSpeedMultiplier(TrekRate rate)
    {
        if (rate == TrekRate.Stationary)
            return Fixed64.Zero;

        Fixed64 maxFastSpeed = Handler.Move.MaxFastSpeed;
        if (rate == TrekRate.Fast || maxFastSpeed <= Fixed64.Zero)
            return Fixed64.One;

        return FixedMath.Clamp(GetScaledFlightSpeedMultiplier(rate, maxFastSpeed), Fixed64.Zero, Fixed64.One);
    }

    private void UpdateControlState()
    {
        if (IsClimbing)
        {
            SetSlidingState(false);
            Handler.IsInControl = true;
            return;
        }

        if (IsOnSolid)
        {
            if (IsFlying)
            {
                SetSlidingState(false);
                Handler.IsInControl = true;
            }
            else
            {
                bool isTooSteep = IsTooSteep(FrameSlopeAngle);
                SetSlidingState(SlideModule?.IsEnabled == true && isTooSteep);
                Handler.IsInControl = !isTooSteep;
            }

            return;
        }

        Handler.IsInControl = IsFlying || !InLimbo;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SetSlidingState(bool isSliding)
    {
        if (SlideModule != null)
            SlideModule.IsSliding = isSliding;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ResetDownwardMomentumIfNeeded()
    {
        if (IsClimbing)
        {
            _forceOutput = Vector3d.Zero;
            return;
        }

        if ((!IsOnSolid || WasInGas) && !IsFlying)
            _forceOutput.y = Fixed64.Zero;
    }

    private Vector3d GetClimbVelocity(TrekRequest frameRequest)
    {
        if (ClimbModule?.IsEnabled != true
            || !ClimbModule.CanClimb
            || frameRequest.Rate == TrekRate.Stationary)
        {
            return Vector3d.Zero;
        }

        Vector3d upAxis = ClimbModule.AttachedUpDirection != Vector3d.Zero
            ? ClimbModule.AttachedUpDirection.Normal
            : Vector3d.Up;
        Vector3d outwardNormal = ClimbModule.AttachedSurfaceNormal != Vector3d.Zero
            ? ClimbModule.AttachedSurfaceNormal.Normal
            : Vector3d.Backward;
        Vector3d lateralAxis = Vector3d.Cross(upAxis, outwardNormal);
        if (lateralAxis == Vector3d.Zero)
            lateralAxis = Vector3d.Cross(Vector3d.Up, outwardNormal);
        if (lateralAxis == Vector3d.Zero)
            lateralAxis = Vector3d.Right;
        lateralAxis = lateralAxis.Normal;

        Fixed64 verticalAmount = Vector3d.Dot(frameRequest.Direction, upAxis);
        if (!ClimbModule.ActiveAllowDescent && verticalAmount < Fixed64.Zero)
            verticalAmount = Fixed64.Zero;

        Fixed64 lateralAmount = ClimbModule.ActiveAllowLateralTraverse
            ? Vector3d.Dot(frameRequest.Direction, lateralAxis)
            : Fixed64.Zero;

        Vector3d climbDirection = (upAxis * verticalAmount) + (lateralAxis * lateralAmount);
        Fixed64 inputMagnitude = FixedMath.Clamp01(climbDirection.Magnitude);
        if (inputMagnitude <= Fixed64.Zero)
            return Vector3d.Zero;

        return climbDirection.Normal * (ClimbModule.MaxClimbSpeed * inputMagnitude);
    }

    private bool TryApplyStationaryGroundFriction(Vector3d desiredDirection)
    {
        if (!IsOnSolid || IsFlying || desiredDirection != Vector3d.Zero)
            return false;

        Fixed64 friction = CurrentState.GroundState?.SurfaceFriction ?? Fixed64.Zero;
        if (friction <= Fixed64.Zero || _forceOutput == Vector3d.Zero)
            return false;

        _forceOutput *= Fixed64.One - friction;
        return true;
    }

    private void ApplyDesiredVelocity(Vector3d desiredVelocity)
    {
        if (desiredVelocity == _forceOutput)
            return;

        Fixed64 maxVelocityChange = GetMaxAcceleration() * TrailblazerManager.DeltaTime;
        Vector3d velocityChange = (desiredVelocity - _forceOutput).ClampMagnitude(maxVelocityChange);
        if (!IsOnSolid && !Handler.IsInControl)
            return;

        _forceOutput += velocityChange;
        if (IsOnSolid && !IsFlying)
            _forceOutput.y = FixedMath.Min(_forceOutput.y, Fixed64.Zero);
    }

    private Vector3d GetControlledSurfaceVelocity(TrekRequest frameRequest)
    {
        if (SlideModule?.IsSliding == true)
            return GetSlidingVelocity(frameRequest);

        return Handler.IsInControl && frameRequest.Rate != TrekRate.Stationary
            ? GetHorizontalVelocity(frameRequest)
            : Vector3d.Zero;
    }

    private Vector3d GetSlidingVelocity(TrekRequest frameRequest)
    {
        SlideLocomotion? slideModule = SlideModule;
        if (slideModule == null)
            return Vector3d.Zero;

        Vector3d slideDirection = new Vector3d(
            CurrentState.SurfaceNormal.x,
            Fixed64.Zero,
            CurrentState.SurfaceNormal.z).Normal;
        Vector3d projectedMoveDir = Vector3d.Project(frameRequest.Direction, slideDirection);
        Vector3d speedContribution = projectedMoveDir * slideModule.SpeedControl;
        Vector3d sidewaysContribution = (frameRequest.Direction - projectedMoveDir) * slideModule.SidewaysControl;
        return slideDirection + ((speedContribution + sidewaysContribution) * slideModule.SlidingSpeed);
    }

    private Vector3d ResolveLiquidVelocity(TrekRequest frameRequest, Vector3d desiredVelocity)
    {
        if (SwimModule?.IsEnabled != true || !SwimModule.CanSwim)
            desiredVelocity = Vector3d.Zero;

        if (SwimModule?.IsEnabled == true && frameRequest.Direction.y != Fixed64.Zero)
            desiredVelocity.y = frameRequest.Direction.y * SwimModule.MaxSwimSpeed;

        if (desiredVelocity != Vector3d.Zero)
            desiredVelocity *= FixedMath.Clamp01(Fixed64.One - Handler.Move.WaterDragFactor);

        return desiredVelocity;
    }

    private Vector3d ApplyPlatformTransferVelocity(Vector3d desiredVelocity)
    {
        if (PlatformModule?.IsEnabled == true
            && PlatformModule.MovementTransfer == MotionTransfer.PermaTransfer)
        {
            desiredVelocity += PlatformModule.FramePlatformVelocity;
            desiredVelocity.y = Fixed64.Zero;
        }

        return desiredVelocity;
    }

    private Vector3d ApplyGroundVelocityConstraints(Vector3d desiredVelocity)
    {
        if (!IsOnSolid || desiredVelocity == Vector3d.Zero)
            return desiredVelocity;

        Fixed64 surfaceFriction = CurrentState.GroundState?.SurfaceFriction ?? Fixed64.Zero;
        desiredVelocity *= Fixed64.One - surfaceFriction;

        // Flat or host-defined "solid but no sampled normal" surfaces should preserve the raw ground vector.
        // Sliding also already produces a slope-aware direction, so re-projecting it here distorts the result.
        if (CurrentState.SurfaceNormal == Vector3d.Zero
            || CurrentState.SlopeAngle == Fixed64.Zero
            || SlideModule?.IsSliding == true)
        {
            return desiredVelocity;
        }

        Vector3d sideways = Vector3d.Cross(Vector3d.Up, desiredVelocity);
        Vector3d adjustedVelocity = Vector3d.Cross(sideways, CurrentState.SurfaceNormal).Normal * desiredVelocity.Magnitude;
        if (Fixed64.Sign(adjustedVelocity.y) != Fixed64.Sign(FrameSlopeAngle))
            adjustedVelocity.y *= -1;

        return adjustedVelocity;
    }

    private Fixed64 GetFlightHorizontalSpeed(Vector3d desiredMovementDirection, TrekRate rate)
    {
        if (FlyModule?.IsEnabled != true || !FlyModule.CanFly)
            return Fixed64.Zero;

        Fixed64 horizontalMagnitude = FixedMath.Clamp01(new Vector3d(
            desiredMovementDirection.x,
            Fixed64.Zero,
            desiredMovementDirection.z).Magnitude);
        return horizontalMagnitude * FlyModule.MaxFlySpeed * GetFlightSpeedMultiplier(rate);
    }

    private Fixed64 GetLiquidHorizontalSpeed(Vector3d desiredMovementDirection)
    {
        if (SwimModule?.IsEnabled != true || !SwimModule.CanSwim)
            return Fixed64.Zero;

        Fixed64 ellipseMultiplier = SwimModule.MaxSwimSpeed / SwimModule.MaxSwimSidewaysSpeed;
        if (ellipseMultiplier <= Fixed64.Zero)
            return Fixed64.Zero;

        return GetEllipticalHorizontalSpeed(
            desiredMovementDirection,
            ellipseMultiplier,
            SwimModule.MaxSwimSidewaysSpeed,
            Fixed64.One);
    }

    private Fixed64 GetGroundHorizontalSpeed(Vector3d desiredMovementDirection, TrekRate rate)
    {
        Fixed64 maxSpeed = GetGroundDirectionalMaxSpeed(desiredMovementDirection, rate);
        Fixed64 ellipseMultiplier = maxSpeed / Handler.Move.MaxSidewaysSpeed;
        if (ellipseMultiplier <= Fixed64.Zero)
            return Fixed64.Zero;

        return GetEllipticalHorizontalSpeed(
            desiredMovementDirection,
            ellipseMultiplier,
            Handler.Move.MaxSidewaysSpeed,
            GetAirborneControlMultiplier());
    }

    private Fixed64 GetGroundDirectionalMaxSpeed(Vector3d desiredMovementDirection, TrekRate rate)
    {
        if (desiredMovementDirection.z < Fixed64.Zero)
            return Handler.Move.MaxBackwardsSpeed;

        return rate switch
        {
            TrekRate.Slow => Handler.Move.MaxSlowSpeed,
            TrekRate.Moderate => Handler.Move.MaxModerateSpeed,
            TrekRate.Fast => Handler.Move.MaxFastSpeed,
            _ => Fixed64.Zero
        };
    }

    private static Fixed64 GetEllipticalHorizontalSpeed(
        Vector3d desiredMovementDirection,
        Fixed64 zAxisEllipseMultiplier,
        Fixed64 sidewaysSpeed,
        Fixed64 controlMultiplier)
    {
        Vector3d normalized = new Vector3d(
            desiredMovementDirection.x,
            Fixed64.Zero,
            desiredMovementDirection.z / zAxisEllipseMultiplier).Normal;
        Fixed64 length = new Vector3d(
            normalized.x,
            Fixed64.Zero,
            normalized.z * zAxisEllipseMultiplier).Magnitude;
        return length * sidewaysSpeed * controlMultiplier;
    }

    private Fixed64 GetAirborneControlMultiplier()
    {
        if (IsOnSolid)
            return Fixed64.One;

        if (IsJumping)
            return JumpModule?.JumpControlMultiplier ?? Fixed64.One;

        if (IsFalling)
            return Handler.Fall.FallControlMultiplier;

        return Fixed64.One;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ShouldClearActiveFallState() => IsInLiquid || IsFlying || IsClimbing;

    private void ClearFallState()
    {
        if (IsFalling)
            Handler.ClearTransientState<FallLocomotion>();
    }

    private void UpdateActiveFallState(Vector3d position)
    {
        if (position.y > Handler.Fall.FallStart)
            Handler.Fall.FallStart = position.y;

        if (!IsInGas && !IsTooSteep(FrameSlopeAngle))
        {
            Handler.Fall.IsFalling = false;
            Handler.Fall.FallEnd = position.y;

            if (Handler.Fall.FallHeight > Fixed64.Zero)
                Events.OnStopFall?.Invoke(Handler.Fall.FallHeight);

            Handler.ClearTransientState<FallLocomotion>();
            return;
        }

        Fixed64 currentFallHeight = (Handler.Fall.FallStart - position.y).Abs();
        if (currentFallHeight > Handler.Fall.MaxFallHeight)
            Events?.OnMaxFallHeightReached?.Invoke();
    }

    private void TryStartFall(Vector3d position)
    {
        bool isSlidingTooSteep = IsTooSteep(FrameSlopeAngle);
        if (!(IsInGas || isSlidingTooSteep) || _forceOutput.y >= Fixed64.Zero)
            return;

        Handler.Fall.IsFalling = true;
        Handler.Fall.FallStart = position.y;

        if (JumpModule != null && JumpModule.JumpCount > 0 && !JumpModule.IsCoolingDown)
            JumpModule.StartCooldown();

        Events?.OnStartFall?.Invoke();
    }

    private Fixed64 GetScaledFlightSpeedMultiplier(TrekRate rate, Fixed64 maxFastSpeed) => rate switch
    {
        TrekRate.Slow => Handler.Move.MaxSlowSpeed / maxFastSpeed,
        TrekRate.Moderate => Handler.Move.MaxModerateSpeed / maxFastSpeed,
        // Fast never reaches this helper because GetFlightSpeedMultiplier(...) short-circuits it to one.
        _ => Fixed64.Zero
    };

    private void UpdateClimbState(TrekRequest request)
    {
        if (ClimbModule?.IsEnabled != true)
            return;

        if (ClimbModule.IsMantling)
            return;

        bool canAttemptClimb = request.IsRequestingClimb
            && ClimbModule.CanClimb
            && !IsInLiquid
            && CurrentState.Medium != TraversalMedium.Unknown;
        if (!canAttemptClimb)
        {
            if (ClimbModule.IsClimbing)
                StopClimb(wasForced: false);

            return;
        }

        if (ClimbResolver == null
            || !ClimbResolver.TryResolveClimbAffordance(request, CurrentState, out ClimbAffordanceSnapshot snapshot))
        {
            if (ClimbModule.IsClimbing)
                StopClimb(wasForced: true);

            return;
        }

        if (ClimbModule.IsClimbing)
        {
            bool canContinue = snapshot.CanContinueClimb
                && IsCompatibleClimbAffordance(snapshot);
            if (!canContinue)
            {
                StopClimb(wasForced: true);
                return;
            }

            ClimbModule.ApplyClimbSnapshot(snapshot);

            if (ShouldStartMantle(request, snapshot))
                StartMantle(snapshot);

            return;
        }

        bool canStart = snapshot.CanStartClimb;
        if (!canStart)
            return;

        StartClimb(snapshot);
    }

    private void StartClimb(ClimbAffordanceSnapshot snapshot)
    {
        ClimbLocomotion? climbModule = ClimbModule;
        if (climbModule == null)
            return;

        climbModule.ApplyClimbSnapshot(snapshot);
        climbModule.IsClimbing = true;
        climbModule.IsMantling = false;

        if (IsFalling)
            Handler.ClearTransientState<FallLocomotion>();

        if (JumpModule != null)
        {
            JumpModule.IsJumping = false;
            JumpModule.IsHoldingJump = false;
        }

        if (FlyModule != null)
            FlyModule.IsFlying = false;

        if (SlideModule != null)
            SlideModule.IsSliding = false;

        Events.OnStartClimb?.Invoke(snapshot);
    }

    private void StartMantle(ClimbAffordanceSnapshot snapshot)
    {
        ClimbLocomotion? climbModule = ClimbModule;
        if (climbModule == null)
            return;

        climbModule.ApplyClimbSnapshot(snapshot);
        climbModule.IsMantling = true;
        Events.OnStartMantle?.Invoke();
    }

    private void StopClimb(bool wasForced)
    {
        if (ClimbModule?.IsClimbing != true)
            return;

        Handler.ClearTransientState<ClimbLocomotion>();

        if (wasForced)
            Events.OnClimbSlip?.Invoke();

        Events.OnStopClimb?.Invoke();
    }

    private bool IsCompatibleClimbAffordance(ClimbAffordanceSnapshot snapshot)
    {
        ClimbLocomotion? climbModule = ClimbModule;
        if (climbModule == null)
            return false;

        if (climbModule.AttachmentId.HasValue && snapshot.AffordanceId.HasValue)
            return climbModule.AttachmentId.Value == snapshot.AffordanceId.Value;

        if (snapshot.Kind != climbModule.ActiveClimbKind)
            return false;

        if (!HasCompatibleClimbAxes(snapshot))
            return false;

        Fixed64 tolerance = GetClimbContinuityTolerance();
        return (snapshot.AttachmentPoint - climbModule.AttachmentPoint).SqrMagnitude <= tolerance * tolerance;
    }

    private bool HasCompatibleClimbAxes(ClimbAffordanceSnapshot snapshot)
    {
        ClimbLocomotion? climbModule = ClimbModule;
        if (climbModule == null)
            return false;

        if (climbModule.AttachedSurfaceNormal != Vector3d.Zero
            && snapshot.SurfaceNormal != Vector3d.Zero
            && Vector3d.Dot(climbModule.AttachedSurfaceNormal.Normal, snapshot.SurfaceNormal.Normal) <= Fixed64.Zero)
        {
            return false;
        }

        if (climbModule.AttachedUpDirection != Vector3d.Zero
            && snapshot.UpDirection != Vector3d.Zero
            && Vector3d.Dot(climbModule.AttachedUpDirection.Normal, snapshot.UpDirection.Normal) <= Fixed64.Zero)
        {
            return false;
        }

        return true;
    }

    private Fixed64 GetClimbContinuityTolerance()
    {
        ClimbLocomotion? climbModule = ClimbModule;
        if (climbModule == null)
            return Fixed64.Zero;

        Fixed64 frameTravelAllowance = climbModule.MaxClimbSpeed * TrailblazerManager.DeltaTime;
        return climbModule.ClimbStartTolerance + frameTravelAllowance;
    }

    private bool ShouldStartMantle(TrekRequest request, ClimbAffordanceSnapshot snapshot)
    {
        ClimbLocomotion? climbModule = ClimbModule;
        if (climbModule == null)
            return false;

        if (snapshot.Kind != ClimbAffordanceKind.Ledge
            || !climbModule.ActiveAllowMantle
            || !climbModule.MantleTargetPosition.HasValue
            || request.Rate == TrekRate.Stationary)
        {
            return false;
        }

        Vector3d upAxis = climbModule.AttachedUpDirection != Vector3d.Zero
            ? climbModule.AttachedUpDirection.Normal
            : Vector3d.Up;
        return Vector3d.Dot(request.Direction, upAxis) > Fixed64.Zero;
    }

    private Vector3d GetMantleVelocity(Vector3d origin)
    {
        ClimbLocomotion? climbModule = ClimbModule;
        if (climbModule?.MantleTargetPosition.HasValue != true)
            return Vector3d.Zero;

        Vector3d mantleTargetPosition = climbModule.MantleTargetPosition.GetValueOrDefault();
        Vector3d toTarget = mantleTargetPosition - origin;
        if (toTarget == Vector3d.Zero)
            return Vector3d.Zero;

        Fixed64 distance = toTarget.Magnitude;
        if (distance <= climbModule.ClimbStartTolerance)
            return Vector3d.Zero;

        return toTarget.Normal * climbModule.MaxClimbSpeed;
    }

    private bool CanContinueActiveMantle()
    {
        ClimbLocomotion? climbModule = ClimbModule;
        if (climbModule == null || !climbModule.ValidateActiveMantleWithHost)
            return true;

        if (ClimbResolver is not IActiveMantleValidator validator)
            return true;

        return validator.TryValidateActiveMantle(
                CurrentState,
                climbModule.CreateActiveMantleState(),
                out MantleValidationSnapshot snapshot)
            && snapshot.CanContinueMantle;
    }

    private bool HasReachedMantleTarget(Vector3d position)
    {
        ClimbLocomotion? climbModule = ClimbModule;
        if (climbModule?.MantleTargetPosition.HasValue != true)
            return false;

        Fixed64 tolerance = climbModule.ClimbStartTolerance;
        Vector3d mantleTargetPosition = climbModule.MantleTargetPosition.GetValueOrDefault();
        return (mantleTargetPosition - position).SqrMagnitude <= tolerance * tolerance;
    }

    private void CompleteMantle()
    {
        StopClimb(wasForced: false);
    }

    #endregion

    #region Utility

    /// <summary>
    /// Computes the vertical jump speed required to reach the desired jump height (apex).
    /// </summary>
    /// <returns>The initial vertical velocity needed for the jump.</returns>
    public Fixed64 GetVerticalJumpSpeed() => JumpModule == null
        ? Fixed64.Zero
        : FixedMath.Sqrt(2 * JumpModule.BaseJumpHeight * Handler.Forces.GravityForce);

    /// <summary>
    /// Determines whether the current surface is too steep for normal movement.
    /// </summary>
    /// <returns>True if the slope exceeds the allowable incline; otherwise, false.</returns>
    public bool IsTooSteep(Fixed64 angle)
    {
        if (!IsOnSolid) return false;

        Fixed64 absAngle = FixedMath.Abs(angle); // Handle both positive (uphill) and negative (downhill) slopes
        return absAngle > Handler.Move.SlopeLimit - Fixed64.Epsilon;
    }

    /// <summary>
    /// Checks if the navigator is on a sloped surface that is not considered too steep.
    /// </summary>
    /// <returns>True if the navigator is on a valid slope; otherwise, false.</returns>
    public bool IsOnSlope(Fixed64 angle)
    {
        if (!IsOnSolid) return false;

        Fixed64 absAngle = FixedMath.Abs(angle); // Account for downhill slopes too
        return absAngle > Fixed64.One && absAngle <= Handler.Move.SlopeLimit + Fixed64.Epsilon;
    }

    /// <summary>
    /// Manually sets the navigator’s velocity, overriding the computed velocity for the next frame.
    /// </summary>
    /// <param name="velocity">The new velocity to assign to the navigator.</param>
    public void SetVelocity(Vector3d velocity)
    {
        Handler.Move.FrameVelocity = velocity;
    }

    /// <summary>
    /// Pushes a traversal snapshot into the motor before the next traversal phase begins.
    /// </summary>
    /// <remarks>
    /// This is the explicit pre-traversal sync seam for hosts that learn about medium or surface
    /// changes before the next call to <see cref="TryTraversal(TrekRequest, out Vector3d, out Vector3d, out FixedQuaternion)"/>.
    /// </remarks>
    public void SyncTraversalState(TrekCondition newCondition, bool isInitializing = false)
    {
        if (isInitializing)
        {
            // Don't set the previous state as an empty state
            CurrentState.Update(newCondition, newCondition);
            return;
        }

        TrekCondition previousCondition = CurrentState.ToTrekCondition();
        CurrentState.Update(newCondition, previousCondition);
    }

    /// <summary>
    /// Clears the current traversal-finalization requirement without reconciling frame results.
    /// </summary>
    /// <remarks>
    /// This is an explicit recovery escape hatch for hosts that must discard an in-progress traversal.
    /// It clears traversal bookkeeping only and does not roll back locomotion state changes that already occurred.
    /// </remarks>
    public void AbortTraversalFrame()
    {
        TraversalInProgress = false;
        _pendingTraversalFrame = -1;
        FrameSlopeAngle = Fixed64.Zero;
        _forceOutput = Vector3d.Zero;
    }

    #endregion

    #region Serialization

    /// <inheritdoc />
    public void RecordData(IChronicler chronicler)
    {
        LocomotionHandler handler = Handler;
        TrekCondition currentCondition = CurrentState?.ToTrekCondition() ?? new TrekCondition();
        TrekCondition? previousCondition = CurrentState?.PreviousState;
        bool isInitialized = IsInitialized;

        RecordDeep.Look(chronicler, ref handler, "handler");
        RecordValues.Look(chronicler, ref currentCondition, "currentCondition");
        RecordValues.Look(chronicler, ref previousCondition, "previousCondition");
        RecordValues.Look(chronicler, ref isInitialized, "isInitialized", false);

        if (chronicler.Mode == SerializationMode.Loading)
        {
            Handler = handler;
            CurrentState ??= new(currentCondition, previousCondition);
            CurrentState.Update(currentCondition, previousCondition);
            IsInitialized = isInitialized;
            AbortTraversalFrame();
        }
    }

    #endregion
}

using FixedMathSharp;
using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Navigation.Motor;

public partial class NavMotor
{
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
    /// <param name="velocityDelta">The resulting velocity change to apply to the object</param>
    /// <param name="positionDelta">The resulting position change from platform movement to apply to the object</param>
    /// <param name="rotationDelta">The resulting rotation change from platform movement to apply to the object</param>
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
            if (FrameCount != _pendingTraversalFrame)
            {
                throw new InvalidOperationException(
                    $"NavMotor traversal from frame {_pendingTraversalFrame} was never finalized or aborted before frame {FrameCount}. Call FinalizeTraversal(...) or AbortTraversalFrame() in the same frame that opened traversal.");
            }

            return false;
        }

        TraversalInProgress = true;
        _pendingTraversalFrame = FrameCount;
        return true;
    }

    private void PrepareTraversalState(TrekRequest request)
    {
        TrailblazerLogger.DebugChannel.Info(
            $"NavMotor State: Grounded={IsOnSolid}, InAir={IsInGas}, InWater={IsInLiquid}, Flying={IsFlying}, InLimbo={InLimbo}, Velocity={Handler.Move.FrameVelocity}");

        // Calculate the slope angle for the current frame based on the movement direction and surface normal.
        FrameSlopeAngle = CurrentState.GetSignedSlopeAngle(request.Direction);

        // Store the current velocity for manipulation.
        _forceOutput = Handler.Move.FrameVelocity;

        // Update platform velocity prior to applying jump force.
        PlatformModule.UpdatePlatformVelocity();

        if (InLimbo)
            Handler.IsInControl = false;

        if (JumpModule?.IsCoolingDown == true)
            JumpModule.UpdateCooldown();

        UpdateClimbState(request);
        UpdateFlightState(request);
        UpdateSwimState(request);
    }

    #region Traversal Force Resolution

    private void ResolveTraversalForces(TrekRequest request)
    {
        ComputeMovementForces(request);

        // Reset this before applying gravity.
        if (JumpModule != null && (!request.IsRequestingJump || !JumpModule.CanJump))
            JumpModule.IsHoldingJump = false;

        ApplyEnvironmentalForces();
        ApplyJumpForce(request);
    }

    /// <summary>
    /// Computes the movement forces based on the traversal state and applies them to the object.
    /// </summary>
    /// <remarks>
    /// This method determines whether the object is in control, calculates velocity adjustments,
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

    /// <summary>
    /// Determines the desired velocity based on input direction, movement constraints, and traversal state.
    /// </summary>
    /// <returns>The computed velocity vector that the object should move toward.</returns>
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

    private Fixed64 GetFlightSpeedMultiplier(TrekRate rate)
    {
        if (rate == TrekRate.Stationary)
            return Fixed64.Zero;

        Fixed64 maxFastSpeed = Handler.Move.MaxFastSpeed;
        if (rate == TrekRate.Fast || maxFastSpeed <= Fixed64.Zero)
            return Fixed64.One;

        return FixedMath.Clamp(GetScaledFlightSpeedMultiplier(rate, maxFastSpeed), Fixed64.Zero, Fixed64.One);
    }

    private Fixed64 GetLiquidHorizontalSpeed(Vector3d desiredMovementDirection)
    {
        if (WaterModule?.IsEnabled != true
            || !WaterModule.CanSwim
            || !WaterModule.IsSwimming)
        {
            return Fixed64.Zero;
        }

        Fixed64 ellipseMultiplier = WaterModule.MaxSwimSpeed / WaterModule.MaxSwimSidewaysSpeed;
        if (ellipseMultiplier <= Fixed64.Zero)
            return Fixed64.Zero;

        return GetEllipticalHorizontalSpeed(
            desiredMovementDirection,
            ellipseMultiplier,
            WaterModule.MaxSwimSidewaysSpeed,
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

    private Vector3d ResolveLiquidVelocity(TrekRequest frameRequest, Vector3d desiredVelocity)
    {
        if (WaterModule?.IsEnabled != true
            || !WaterModule.CanSwim
            || !WaterModule.IsSwimming)
        {
            desiredVelocity = Vector3d.Zero;
        }

        if (WaterModule?.IsSwimming == true && frameRequest.Direction.y != Fixed64.Zero)
            desiredVelocity.y = frameRequest.Direction.y * WaterModule.MaxSwimSpeed;

        if (desiredVelocity != Vector3d.Zero)
            desiredVelocity *= FixedMath.Clamp01(Fixed64.One - Handler.Move.WaterDragFactor);

        return desiredVelocity;
    }

    private Vector3d ApplyPlatformTransferVelocity(Vector3d desiredVelocity)
    {
        PlatformLocomotion platformModule = PlatformModule;
        if (platformModule.IsEnabled
            && platformModule.MovementTransfer == MotionTransfer.PermaTransfer)
        {
            desiredVelocity += platformModule.FramePlatformVelocity;
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

        Fixed64 maxVelocityChange = GetMaxAcceleration() * DeltaTime;
        Vector3d velocityChange = (desiredVelocity - _forceOutput).ClampMagnitude(maxVelocityChange);
        if (!IsOnSolid && !Handler.IsInControl)
            return;

        _forceOutput += velocityChange;
        if (IsOnSolid && !IsFlying)
            _forceOutput.y = FixedMath.Min(_forceOutput.y, Fixed64.Zero);
    }

    /// <summary>
    /// Retrieves the maximum acceleration value based on the object’s current traversal state.
    /// </summary>
    /// <returns>The acceleration limit depending on whether the object is grounded, airborne, or swimming.</returns>
    public Fixed64 GetMaxAcceleration()
    {
        if (CurrentState == null)
            throw new InvalidOperationException("NavMotor must be initialized before querying max acceleration.");

        if (IsInLiquid)
            return WaterModule?.IsEnabled == true
                && WaterModule.CanSwim
                && WaterModule.IsSwimming
                ? WaterModule.MaxSwimAcceleration
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

    private void ApplyEnvironmentalForces()
    {
        Fixed64 gravityStep = Handler.Forces.GravityForce * DeltaTime;

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
            if (WaterModule?.IsEnabled == true)
                _forceOutput.y += gravityStep * (WaterModule.BuoyancyFactor - Fixed64.One);
            else
                _forceOutput.y = Handler.Move.FrameVelocity.y - gravityStep;

            return;
        }

        if (!IsInGas) return;

        _forceOutput.y = Handler.Move.FrameVelocity.y - gravityStep;

        // Ensure velocity does not exceed terminal fall speed
        Fixed64 terminalFallSpeed = Handler.Move.FrameVelocity.y + (_forceOutput.y * DeltaTime);
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
            if (TotalTime <= extraJumpLimit)
                _forceOutput += jumpModule.FrameJumpDirection * gravityStep;
        }
    }

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

        if (IsInLiquid && !(WaterModule?.CanBreachWater ?? false))
            return false;

        if (!JumpModule.CanJump)
            return false;

        return request.CanAffordJump;
    }

    private Vector3d GetWaterBreachJumpForce()
    {
        JumpLocomotion? jumpModule = JumpModule;
        WaterLocomotion? waterModule = WaterModule;
        if (jumpModule == null || waterModule == null)
            return Vector3d.Zero;

        jumpModule.FrameJumpDirection = Vector3d.Up;
        Events.OnStartWaterBreach?.Invoke();
        return jumpModule.FrameJumpDirection * (GetVerticalJumpSpeed() * waterModule.BreachJumpMultiplier);
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

        if (ClimbModule.ClimbResolver == null
            || !ClimbModule.ClimbResolver.TryResolveClimbAffordance(request, CurrentState, out ClimbAffordanceSnapshot snapshot))
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

        Fixed64 frameTravelAllowance = climbModule.MaxClimbSpeed * DeltaTime;
        return climbModule.ClimbStartTolerance + frameTravelAllowance;
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

    private void StartMantle(ClimbAffordanceSnapshot snapshot)
    {
        ClimbLocomotion? climbModule = ClimbModule;
        if (climbModule == null)
            return;

        climbModule.ApplyClimbSnapshot(snapshot);
        climbModule.IsMantling = true;
        Events.OnStartMantle?.Invoke();
    }

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

    private void UpdateSwimState(TrekRequest request)
    {
        WaterLocomotion? waterModule = WaterModule;
        if (waterModule?.IsEnabled != true)
            return;

        waterModule.RequestedSwimThisTraversal = request.IsRequestingSwim;
        waterModule.IsSwimming = IsInLiquid
            && waterModule.CanSwim
            && request.IsRequestingSwim;
    }

    #endregion

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private Vector3d ResolveTraversalVelocityDelta()
    {
        return _forceOutput != Vector3d.Zero
            ? _forceOutput * DeltaTime
            : Vector3d.Zero;
    }

    private void ResolvePlatformTraversal(
        TrekRequest request,
        ref Vector3d positionDelta,
        ref FixedQuaternion rotationDelta)
    {
        PlatformLocomotion platformModule = PlatformModule;

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

    #endregion
}

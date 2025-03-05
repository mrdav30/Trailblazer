using FixedMathSharp;
using System;
using System.Diagnostics;
using Trailblazer.Controllers.Locomotions;

namespace Trailblazer.Controllers
{
    /// <summary>
    /// The mode of the motor.
    /// </summary>
    public enum ControllerMode
    {
        /// <summary>   
        /// Standard force application.
        /// </summary>
        ForceBased = 0,
        /// <summary>
        /// Calculates and outputs expected position updates.
        /// </summary>
        PositionBased = 1
    }

    /// <summary>
    /// The type of movement input.
    /// </summary>
    public enum MoveInput
    {
        Idle = 0,
        Walk = 1, // walk
        Jog = 2, // jog
        Sprint = 3, // run
        Unlocked = 99
    }

    // Deterministic, lockstep, impulse-based agent controller
    // Each frame applies an impulse that directly modifies velocity,
    // rather than continuously accumulating acceleration.
    [Serializable]
    public class ScoutController
    {
        #region Fields

        public bool DebugMode;

        /// <summary>
        /// Contains all locomotion states for the scout.
        /// </summary>
        public LocomotionData LocomotionState = new();

        /// <summary>
        /// The mode of the motor.
        /// </summary>
        public ControllerMode Mode;

        [NonSerialized]
        private IScout _hostScout;

        [NonSerialized]
        private MovementData _movementState = MovementData.DefaultMovementState;

        public MovementData MovementState { get => _movementState; }

        /// <summary>
        /// Whether the motor is locked.
        /// </summary>
        public bool IsMotorLocked { get; private set; }

        /// <inheritdoc cref="TrailblazerManager.GravityForce"/>
        public Fixed64 Gravity { get; set; } = TrailblazerManager.GravityForce;

        public bool IsPlatformMovementApplied => _movementState.IsGrounded && LocomotionState.Platform.IsOnPlatform || LocomotionState.Platform.IsLockedToPlatform;

        #region Cache

        [NonSerialized]
        private TraversalData _cachedTraversalState;

        [NonSerialized]
        private Vector3d _cachedMoveDirection;

        [NonSerialized]
        private MoveInput _cachedMovementInput;

        [NonSerialized]
        private Vector3d _cachedForce;

        #endregion

        #endregion

        #region Lifecycle

        public static ScoutController CreateNew(IScout scout)
        {
            ScoutController controller = new ScoutController();
            controller.Initialize(scout);
            return controller;
        }

        public void Initialize(IScout scout) => _hostScout = scout;

        /// <summary>
        /// Call once every simulation frame (i.e. FixedUpdate)
        /// </summary>
        public void Simulate(
            Vector3d direction, // The current global direction we want the character to move in.
            MoveInput movementInput,
            bool hasJumpRequest = false)
        {
            if (_hostScout == null) return;

            if (IsMotorLocked)
                return;

            IsMotorLocked = true;

            if (DebugMode)
                Debug.WriteLine($"AgentMotor State: " +
                    $"Grounded={_movementState.IsGrounded}, " +
                    $"InAir={_movementState.IsInAir}, " +
                    $"Velocity={_hostScout.LinearVelocity}");

            _cachedMovementInput = movementInput;
            _cachedMoveDirection = LocomotionState.CanControl ? direction : Vector3d.Zero;

            // Reset the target impulse to prevent accumulation
            _cachedForce = Vector3d.Zero;

            CheckGroundingState();

            HandleMovementTransitions();

            // Apply movement force only if we have control, must calculate before platform movement
            if (LocomotionState.CanControl)
                ComputeMovementForces();

            // Register any additional movement after applying platform movement
            if (LocomotionState.Platform.IsEnabled)
                HandlePlatformUpdates();

            ApplyGravityImpulse();

            if (LocomotionState.CanControl && LocomotionState.Jump.IsEnabled && hasJumpRequest)
                ApplyJumpImpulse();
            else
                LocomotionState.Jump.IsHoldingJump = false;

            if (!LocomotionState.Jump.IsJumping)
            {
                if (_movementState.IsInWater)
                    _cachedForce.y = _cachedMoveDirection.y;

                if (_movementState.IsGrounded)
                    _cachedForce.y = Fixed64.Zero; // Prevent unwanted vertical movement
            }

            // Apply the force
            if (_cachedForce != Vector3d.Zero)
            {
                if (Mode == ControllerMode.ForceBased)
                    _hostScout.Events?.OnAddLinearImpulse?.Invoke(_cachedForce);
                else if (Mode == ControllerMode.PositionBased)
                    _hostScout.Events?.OnAddPositionDelta?.Invoke(_cachedForce * TrailblazerManager.DeltaTime);
            }

            // Reset before returning
            Reset();
        }

        public void Reset()
        {
            _cachedMovementInput = MoveInput.Idle;
            _cachedMoveDirection = Vector3d.Zero;
            _cachedForce = Vector3d.Zero;
            _cachedTraversalState = TraversalData.DefaultTraversalState;
        }

        #endregion

        #region Movement Processing

        private void CheckGroundingState()
        {
            _hostScout.GetTraversalState(out _cachedTraversalState);

            _movementState.LastTraversalMedium = _movementState.ActiveTraversalMedium;
            _movementState.ActiveTraversalMedium = _cachedTraversalState.Medium;

            if (!_movementState.IsGrounded)  // Left the ground
            {
                _movementState.LastGroundNormal = Vector3d.Zero;
                _movementState.GroundNormal = Vector3d.Zero;
            }
            else
            {
                _movementState.LastGroundNormal = _movementState.GroundNormal;
                _movementState.GroundNormal = _cachedTraversalState.GroundNormal;
            }
        }

        private void HandleMovementTransitions()
        {
            if (LocomotionState.Jump.IsEnabled && LocomotionState.Jump.IsCoolingDown)
                LocomotionState.Jump.UpdateCooldown();

            // If we hit a new platform, reset platform state
            if (!_movementState.IsInWater)
                DidPlatformChange();

            if (_movementState.WasInAir && !_movementState.IsInAir) // Landed
            {
                if (LocomotionState.Platform.IsInteriaApplied)
                    LocomotionState.Platform.FrameVelocity *= Fixed64.Half; // Preserve some horizontal

                if (LocomotionState.Jump.IsJumping)
                {
                    // Reset cooldown on landing
                    LocomotionState.Jump.ClearState();

                    if (_movementState.IsInWater)
                        _hostScout.Events?.OnStopWaterBreach?.Invoke();
                    else
                        _hostScout.Events?.OnStopJump?.Invoke();
                }

                if (!_movementState.IsInWater)
                    _hostScout.Events?.OnLandedFall?.Invoke();
            }

            // Transitioning into water
            if (_movementState.IsInWater)
            {
                // Clear the transient state when entiring water for the first time
                if (!_movementState.WasInWater)
                    LocomotionState.ClearStateAll();

                if (LocomotionState.Swim.IsEnabled)
                {
                    LocomotionState.Swim.IsSwimming = true;
                    LocomotionState.Swim.IsDiving = _hostScout.WorldPosition.y < _cachedTraversalState.SurfaceLevel;

                    LocomotionState.Swim.UpdateDiveTime();

                    if (_movementState.IsInWater && LocomotionState.Swim.IsDrowning)
                        _hostScout.Events?.OnDrowning?.Invoke(LocomotionState.Swim.UnderwaterTimer);
                }

                return;
            }

            if (!_movementState.IsInWater)
            {
                // Transitioning from water to land
                if (_movementState.WasInWater && LocomotionState.Swim.IsEnabled)
                    LocomotionState.Swim.ClearState();

                if (_movementState.IsGrounded && LocomotionState.Slide.IsEnabled)
                {
                    bool isSliding = IsTooSteep();
                    if (!isSliding && LocomotionState.Slide.IsSliding)
                        LocomotionState.CanControl = true; // reset control

                    LocomotionState.Slide.IsSliding = isSliding;
                }


                if (LocomotionState.Fall.IsEnabled)
                    UpdateFallingState();
            }
        }

        private bool DidPlatformChange()
        {
            if (!LocomotionState.Platform.IsEnabled
                || LocomotionState.Platform.ActivePlatform == _cachedTraversalState.HitObject)
                return LocomotionState.Platform.IsNewPlatform = false;

            if (LocomotionState.Platform.ActivePlatform == null
                || LocomotionState.Platform.ActivePlatform != _cachedTraversalState.HitObject)
            {
                LocomotionState.Platform.LastMatrix = LocomotionState.Platform.ActivePlatform == null
                    ? _cachedTraversalState.GroundMatrix
                    : LocomotionState.Platform.ActiveMatrix;
                LocomotionState.Platform.ActiveMatrix = _cachedTraversalState.GroundMatrix;
                LocomotionState.Platform.ActivePlatform = _cachedTraversalState.HitObject;
                LocomotionState.Platform.FrameVelocity = Vector3d.Zero;
                LocomotionState.Platform.ActiveVelocity = Vector3d.Zero;
                return LocomotionState.Platform.IsNewPlatform = true;
            }

            return LocomotionState.Platform.IsNewPlatform = false;
        }

        private void UpdateFallingState()
        {
            if (LocomotionState.Fall.IsFalling)
            {
                // Make sure we didn't somehow get above the initial start point
                if (_hostScout.WorldPosition.y > LocomotionState.Fall.FallStart)
                    LocomotionState.Fall.FallStart = _hostScout.WorldPosition.y;

                if (!_movementState.IsInAir && !LocomotionState.Slide.IsSliding)
                {
                    // scout landed after falling
                    LocomotionState.Fall.IsFalling = false;
                    LocomotionState.Fall.FallEnd = _hostScout.WorldPosition.y;

                    if (LocomotionState.Fall.FallHeight > Fixed64.Zero)
                        _hostScout.Events?.OnStopFall?.Invoke(LocomotionState.Fall.FallHeight);

                    return;
                }

                Fixed64 fallHeight = (LocomotionState.Fall.FallStart - _hostScout.WorldPosition.y).Abs();
                if (fallHeight > LocomotionState.Fall.MaxFallHeight)
                    _hostScout.Events?.OnMaxFallHeightReached?.Invoke();

                return;
            }

            // check if we are currently falling with a small threshold
            if ((_movementState.IsInAir || LocomotionState.Slide.IsSliding) && _hostScout.LinearVelocity.y < -Fixed64.FromRaw(0x00010000L))
            {
                // scout started falling
                LocomotionState.Fall.IsFalling = true;
                LocomotionState.Fall.FallStart = _hostScout.WorldPosition.y;
                _hostScout.Events?.OnStartFall?.Invoke();
            }
        }

        private void HandlePlatformUpdates()
        {
            // Don't process platform state when in water
            if (_movementState.IsInWater)
                return;

            // Subtract only horizontal velocity, preserving vertical momentum
            Vector3d adjustedPlatformVelocity = LocomotionState.Platform.ActiveVelocity;
            adjustedPlatformVelocity.y = Fixed64.Zero; // Keep vertical momentum

            // If scout landed on a new platform, we have to wait for two frames
            // before we know the new velocity of the platform under the scout
            if (LocomotionState.Platform.IsHoldingPlatform)
            {
                bool release = LocomotionState.Platform.UpdateHoldOnPlatform();
                if (release && _movementState.IsGrounded)
                    _cachedForce -= adjustedPlatformVelocity;
            }

            if (LocomotionState.Platform.IsInteriaApplied)
            {
                if (!_movementState.WasInAir && _movementState.IsInAir)
                {
                    LocomotionState.Platform.FrameVelocity = LocomotionState.Platform.ActiveVelocity;
                    _cachedForce += LocomotionState.Platform.FrameVelocity;  // Apply inertia from platform
                }

                if (!_movementState.WasGrounded && _movementState.IsGrounded)
                {
                    if (LocomotionState.Platform.IsNewPlatform)
                        LocomotionState.Platform.SetHoldPlatform(_cachedTraversalState.HitObject);
                    else
                        _cachedForce -= adjustedPlatformVelocity;
                }
            }

            if (LocomotionState.Platform.ActivePlatform != null)
                UpdatePlatformVelocity();
            else
                LocomotionState.Platform.ActiveVelocity = Vector3d.Zero;

            if (IsPlatformMovementApplied)
            {
                UpdatePlatformMovement();
                ApplyPlatformMovement();
            }

            LocomotionState.Platform.LastMatrix = LocomotionState.Platform.ActiveMatrix;
        }

        private void UpdatePlatformMovement()
        {
            Vector3d footPosition = _hostScout.GetFootPosition();
            footPosition.y += LocomotionState.Platform.HeightAdjust;
            LocomotionState.Platform.ActiveGlobalPoint = footPosition;
            LocomotionState.Platform.ActiveLocalPoint = Fixed4x4.InverseTransformPoint(
                LocomotionState.Platform.ActiveMatrix,
                LocomotionState.Platform.ActiveGlobalPoint);

            LocomotionState.Platform.ActiveGlobalRotation = _hostScout.VisualRotation;
            LocomotionState.Platform.ActiveLocalRotation = LocomotionState.Platform.ActiveMatrix.Rotation.Inverse() * LocomotionState.Platform.ActiveGlobalRotation;
        }

        private void UpdatePlatformVelocity()
        {
            if (!LocomotionState.Platform.IsNewPlatform)
            {
                Vector3d currentPoint = LocomotionState.Platform.ActiveMatrix.TransformPoint(LocomotionState.Platform.ActiveLocalPoint);
                Vector3d previousPoint = LocomotionState.Platform.LastMatrix.TransformPoint(LocomotionState.Platform.ActiveLocalPoint);
                LocomotionState.Platform.ActiveVelocity = (currentPoint - previousPoint) / TrailblazerManager.DeltaTime;
            }
        }

        private void ApplyPlatformMovement()
        {
            if (!_movementState.IsGrounded || LocomotionState.Platform.ActiveVelocity == Vector3d.Zero)
                return;

            FixedQuaternion targetRotation = LocomotionState.Platform.ActiveMatrix.Rotation * LocomotionState.Platform.ActiveLocalRotation;

            if (Mode == ControllerMode.PositionBased)
            {
                // Apply platform rotation FIRST
                if (targetRotation != FixedQuaternion.Identity)
                    _hostScout.Events?.OnSetRotation?.Invoke(targetRotation);

                Vector3d moveDistance = LocomotionState.Platform.ActiveVelocity * TrailblazerManager.DeltaTime;
                // THEN apply platform movement
                if (moveDistance != Vector3d.Zero)
                    _hostScout.Events?.OnAddPositionDelta?.Invoke(moveDistance);
            }
            else if (Mode == ControllerMode.ForceBased)
            {
                if (targetRotation != FixedQuaternion.Identity)
                {
                    // --- ANGULAR FORCE CALCULATION ---
                    // Compute angular impulse required to match platform rotation
                    Vector3d requiredAngularVelocity = targetRotation.ToAngularVelocity(_hostScout.VisualRotation, TrailblazerManager.DeltaTime);

                    // Apply torque to match platform rotation
                    _hostScout.Events?.OnAddAngularImpulse?.Invoke(requiredAngularVelocity);
                }

                if (LocomotionState.Platform.ActiveVelocity != Vector3d.Zero)
                {
                    // --- LINEAR FORCE CALCULATION ---
                    // Convert movement into required linear impulse
                    Vector3d requiredLinearVelocity = LocomotionState.Platform.ActiveVelocity - _hostScout.LinearVelocity;

                    // Apply impulse for linear movement
                    _hostScout.Events?.OnAddLinearImpulse?.Invoke(requiredLinearVelocity);
                }
            }
        }

        /// <summary>
        /// Find desired velocity
        /// </summary>
        private void ComputeMovementForces()
        {
            Vector3d velocityChange = GetDesiredVelocity() - _hostScout.LinearVelocity;
            Fixed64 maxVelocityChange = GetMaxVelocity() * TrailblazerManager.DeltaTime;
            // Clamp the impulse
            _cachedForce = velocityChange.SqrMagnitude > maxVelocityChange * maxVelocityChange
                    ? velocityChange.Normal * maxVelocityChange
                    : velocityChange;
        }

        private Vector3d GetDesiredVelocity()
        {
            Vector3d result;
            if (LocomotionState.Slide.IsSliding)
            {
                // The direction we're sliding in
                result = new Vector3d(_movementState.GroundNormal.x, Fixed64.Zero, _movementState.GroundNormal.z).Normal;
                // Find the input movement direction projected onto the sliding direction
                Vector3d projectedMoveDir = Vector3d.Project(_cachedMoveDirection, result);
                // Add the sliding direction, the speed control, and the sideways control vectors
                result = result + projectedMoveDir
                    * LocomotionState.Slide.SpeedControl + (_cachedMoveDirection - projectedMoveDir)
                    * LocomotionState.Slide.SidewaysControl;
                // Multiply with the sliding speed

                Fixed64 adjustedSlideSpeed = LocomotionState.Slide.SlidingSpeed
                    * (Fixed64.One - LocomotionState.Move.SurfaceFriction);
                result *= adjustedSlideSpeed;

                LocomotionState.CanControl = false;
            }
            else
            {
                result = GetHorizontalVelocity();

                // Ensure that the desired movement of the scout aligns with the surface they are on
                // i.e., the scout doesn't try to move into the ground when the ground is sloping upwards
                if (_movementState.IsGrounded)
                    result = Vector3d.ProjectOnPlane(result, _movementState.GroundNormal);

                if (_movementState.IsInWater) // Ensure smoother stops in water instead of abrupt halts
                    return result * (Fixed64.One - LocomotionState.Swim.WaterDragFactor);

                LocomotionState.CanControl = true;
            }

            if (IsPlatformMovementApplied)
            {
                result += LocomotionState.Platform.FrameVelocity;
                result.y = Fixed64.Zero;
            }

            // Ensures scout does not "digging into" the ground when moving over a bump
            if (_movementState.IsGrounded)
            {
                Vector3d sideways = Vector3d.Cross(Vector3d.Up, result);
                result = Vector3d.Cross(sideways, _cachedTraversalState.GroundNormal).Normal * result.Magnitude;
            }

            return result;
        }

        private Vector3d GetHorizontalVelocity()
        {
            Fixed3x3 transposedMatrix = _hostScout.VisualRotation.ToMatrix3x3();
            Vector3d desiredLocalDirection = Fixed3x3.InverseTransformDirection(transposedMatrix, _cachedMoveDirection);
            Fixed64 speed = MaxSpeedInDirection(desiredLocalDirection);

            speed *= LocomotionState.Move.MoveSpeedMultiplier;

            if (IsOnSlope())
            {
                // Modify max speed on slopes based on slope speed multiplier curve
                Fixed64 movementSlopeAngle = FixedMath.Asin(_movementState.GroundNormal.y.ClampOne()).ToDegree();
                speed *= LocomotionState.Move.SlopeSpeedMultiplier.Evaluate(movementSlopeAngle);
            }

            return transposedMatrix * (desiredLocalDirection * speed);
        }

        /// <summary>
        /// Project a direction onto elliptical quater segments based on forward, sideways, and backwards speed.
        /// </summary>
        /// <returns>Returns the length of the resulting vector.</returns>
        private Fixed64 MaxSpeedInDirection(Vector3d desiredMovementDirection)
        {
            if (desiredMovementDirection == Vector3d.Zero)
                return Fixed64.Zero;

            Fixed64 maxSpeed = Fixed64.Zero;
            if (desiredMovementDirection.z < Fixed64.Zero)
                maxSpeed = LocomotionState.Move.MaxBackwardsSpeed;
            else
            {
                switch (_cachedMovementInput)
                {
                    case MoveInput.Walk:
                        maxSpeed = LocomotionState.Move.MaxWalkSpeed;
                        break;
                    case MoveInput.Jog:
                        maxSpeed = LocomotionState.Move.MaxJogSpeed;
                        break;
                    case MoveInput.Sprint:
                        maxSpeed = LocomotionState.Move.MaxSprintSpeed;
                        break;
                }
            }

            Vector3d temp;
            Fixed64 zAxisEllipseMultiplier;
            if (_movementState.IsInWater)
            {
                zAxisEllipseMultiplier = LocomotionState.Swim.MaxSwimSpeed / LocomotionState.Swim.MaxSwimSidewaysSpeed;
                temp = new Vector3d(
                    desiredMovementDirection.x,
                    Fixed64.Zero,
                    desiredMovementDirection.z / zAxisEllipseMultiplier).Normal;
            }
            else
            {
                zAxisEllipseMultiplier = maxSpeed / LocomotionState.Move.MaxSidewaysSpeed;
                if (zAxisEllipseMultiplier <= Fixed64.Zero)
                    return Fixed64.Zero;

                temp = new Vector3d(
                    desiredMovementDirection.x,
                    Fixed64.Zero,
                    desiredMovementDirection.z / zAxisEllipseMultiplier).Normal;
            }

            Fixed64 length = new Vector3d(temp.x, Fixed64.Zero, temp.z * zAxisEllipseMultiplier).Magnitude;
            return length * (_movementState.IsInWater
                ? LocomotionState.Swim.MaxSwimSpeed
                : LocomotionState.Move.MaxSidewaysSpeed);
        }

        private Fixed64 GetMaxVelocity()
        {
            if (_movementState.IsInWater)
                return LocomotionState.Swim.MaxVelocity;

            if (_movementState.IsGrounded)
                return LocomotionState.Move.MaxGroundAcceleration;

            if (LocomotionState.Jump.IsJumping)
                return LocomotionState.Move.MaxAirAcceleration * LocomotionState.Jump.JumpControlMultiplier;

            if (LocomotionState.Fall.IsFalling)
                return LocomotionState.Move.MaxAirAcceleration * LocomotionState.Fall.FallControlMultiplier;

            return Fixed64.One;
        }

        private void ApplyGravityImpulse()
        {
            if (_movementState.IsGrounded)
            {
                // Only apply gravity if no other system set Y force
                if (_cachedForce.y == Fixed64.Zero)
                    _cachedForce.y = FixedMath.Min(Fixed64.Zero, _cachedForce.y - Gravity);
                return;
            }

            if (_movementState.IsInAir)
            {
                // Convert gravity acceleration to a velocity vector
                _cachedForce.y = _hostScout.LinearVelocity.y - Gravity;

                // When jumping up we don't apply gravity for some time when the user is holding the jump button.
                // This gives more control over jump height by pressing the button longer.
                if (LocomotionState.Jump.IsJumping && LocomotionState.Jump.IsHoldingJump)
                {
                    // Calculate the duration that the extra jump force should have effect.
                    // If we're still less than that duration after the jumping time, apply the force.
                    int extraJumpLimit = LocomotionState.Jump.FrameStartJump
                        + (int)(LocomotionState.Jump.ExtraJumpHeight
                            / FixedMath.Sqrt(LocomotionState.Jump.BaseJumpHeight * 2));

                    // Negate the gravity we just applied, except we push in jumpDir rather than jump upwards.
                    if (TrailblazerManager.FrameCount < extraJumpLimit)
                        _cachedForce += LocomotionState.Jump.FrameJumpDirection * Gravity;
                }

                // Make sure we don't fall any faster than maxFallSpeed. This gives our character a terminal velocity.
                _cachedForce.y = FixedMath.Max(_cachedForce.y, -TrailblazerManager.MaxFallSpeed);
            }
        }

        private void ApplyJumpImpulse()
        {
            if (_movementState.IsInAir
                || _movementState.IsInWater && !LocomotionState.Swim.CanBreachWater
                || LocomotionState.Jump.IsCoolingDown
                || _hostScout.Events?.CanAffordJump?.Invoke() == false)
            {
                return;
            }

            Vector3d jumpImpulse;
            if (_movementState.IsInWater)
            {
                LocomotionState.Jump.FrameJumpDirection = Vector3d.Up;
                jumpImpulse = LocomotionState.Jump.FrameJumpDirection * LocomotionState.Swim.BuoyantForce;
                _hostScout.Events?.OnStartWaterBreach?.Invoke();
            }
            else
            {
                // Calculate the jumping direction
                Fixed64 slerpAmount = IsTooSteep()
                    ? LocomotionState.Jump.SteepPerpendicularJumpAmount
                    : LocomotionState.Jump.PerpendicularJumpAmount;

                // Store jump direction the first time we jump
                if (!LocomotionState.Jump.IsJumping)
                    LocomotionState.Jump.FrameJumpDirection = Vector3d.Slerp(Vector3d.Up, _movementState.GroundNormal, slerpAmount);

                // From the jump height and gravity we deduce the upwards speed
                // for the character to reach at the apex.
                jumpImpulse = LocomotionState.Jump.FrameJumpDirection * FixedMath.Sqrt(LocomotionState.Jump.BaseJumpHeight * 2);

                // Apply inertia from platform
                if (LocomotionState.Platform.IsInteriaApplied)
                {
                    LocomotionState.Platform.FrameVelocity = LocomotionState.Platform.ActiveVelocity;
                    jumpImpulse += LocomotionState.Platform.ActiveVelocity;
                }

                _hostScout.Events?.OnStartJump?.Invoke();
                _hostScout.Events?.OnSkipGroundingCheckTimer?.Invoke(LocomotionState.Jump.AvoidGroundingTimer);
            }

            // If we aren't in air, trigger a new jump then...
            LocomotionState.Jump.IsJumping = true;
            LocomotionState.Jump.IsHoldingJump = true;
            LocomotionState.Jump.FrameStartJump = TrailblazerManager.FrameCount;

            LocomotionState.Jump.StartCooldown();

            _cachedForce += jumpImpulse;
        }

        #endregion

        #region Utility

        public void SetMotorLock(bool status) => IsMotorLocked = status;

        public bool IsTooSteep()
        {
            Fixed64 angle = Vector3d.Angle(Vector3d.Up, _movementState.GroundNormal);
            return angle > LocomotionState.Slide.SlopeLimit;
        }

        public bool IsOnSlope()
        {
            if (!_movementState.IsGrounded) return false;
            Fixed64 angle = Vector3d.Angle(Vector3d.Up, _movementState.GroundNormal);
            return angle > Fixed64.One && angle <= LocomotionState.Slide.SlopeLimit;
        }

        #endregion
    }
}

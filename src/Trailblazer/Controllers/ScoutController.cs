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
        public LocomotionState Locomotion = new();

        /// <summary>
        /// The mode of the motor.
        /// </summary>
        public ControllerMode Mode;

        [NonSerialized]
        private IScout _hostScout;

        [NonSerialized]
        private TraversalState _traversal = TraversalState.DefaultTraversalState;

        public TraversalState Traversal { get => _traversal; }

        /// <summary>
        /// Whether the motor is locked.
        /// </summary>
        public bool IsMotorLocked { get; private set; }

        /// <inheritdoc cref="TrailblazerManager.GravityForce"/>
        public Fixed64 Gravity { get; set; } = TrailblazerManager.GravityForce;

        public bool IsPlatformMovementApplied => _traversal.IsGrounded && Locomotion.Platform.IsOnPlatform || Locomotion.Platform.IsLockedToPlatform;

        public bool InLimbo => _traversal.IsInAir && !Locomotion.Jump.IsJumping && !Locomotion.Fall.IsFalling;

        #region Cache

        [NonSerialized]
        private MovementState _cachedMovementState;

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
                    $"Grounded={_traversal.IsGrounded}, " +
                    $"InAir={_traversal.IsInAir}, " +
                    $"Velocity={_hostScout.LinearVelocity}");

            _cachedMovementInput = movementInput;
            _cachedMoveDirection = Locomotion.IsInControl ? direction : Vector3d.Zero;

            // Reset the target impulse to prevent accumulation
            _cachedForce = Vector3d.Zero;

            CheckGroundingState();

            HandleMovementTransitions();

            ComputeMovementForces();

            // Register any additional movement after applying platform movement
            if (Locomotion.Platform.IsEnabled)
                HandlePlatformUpdates();

            if (!hasJumpRequest)
                Locomotion.Jump.IsHoldingJump = false;

            // TODO: for water, handle bouyant force
            if (!Locomotion.Swim.IsSwimming)
                ApplyGroundOrGravityImpulse();

            if (Locomotion.Jump.IsEnabled && Locomotion.IsInControl)
                ApplyJumpImpulse();

            if (Locomotion.Fall.IsEnabled && !Locomotion.Swim.IsSwimming)
                HandleFallState();

            // Corrects the Y axis if swimming or no gravity
            if (Locomotion.Swim.IsSwimming)
                _cachedForce.y = _cachedMoveDirection.y;

            // Apply the force
            if (_cachedForce != Vector3d.Zero)
            {
                if (Mode == ControllerMode.ForceBased)
                    _hostScout.Events?.OnAddLinearForce?.Invoke(_cachedForce);
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
            _cachedMovementState = MovementState.DefaultMovementState;
        }

        #endregion

        #region Movement Processing

        private void CheckGroundingState()
        {
            _hostScout.GetMovementState(out _cachedMovementState);

            _traversal.LastTraversalMedium = _traversal.ActiveTraversalMedium;
            _traversal.ActiveTraversalMedium = _cachedMovementState.Medium;

            if (!_traversal.IsGrounded)  // Left the ground
            {
                _traversal.LastGroundNormal = Vector3d.Zero;
                _traversal.GroundNormal = Vector3d.Zero;
            }
            else
            {
                _traversal.LastGroundNormal = _traversal.GroundNormal;
                _traversal.GroundNormal = _cachedMovementState.GroundNormal;
            }
        }

        private void HandleMovementTransitions()
        {
            if (InLimbo)
            {
                // In limbo, prevent any further processing until control is given back
                Locomotion.IsInControl = false;
                return;
            }

            if (Locomotion.Jump.IsCoolingDown)
                Locomotion.Jump.UpdateCooldown();

            // If we hit a new platform, reset platform state
            if (!Locomotion.Swim.IsSwimming)
                DidPlatformChange();

            if (_traversal.WasInAir && !_traversal.IsInAir) // Landed
            {
                if (Locomotion.Platform.IsInteriaApplied)
                    Locomotion.Platform.FrameVelocity *= Fixed64.Half; // Preserve some horizontal

                if (Locomotion.Jump.IsJumping)
                {
                    // Reset cooldown on landing
                    Locomotion.Jump.ClearState();

                    if (_traversal.IsInWater)
                        _hostScout.Events?.OnStopWaterBreach?.Invoke();
                    else
                        _hostScout.Events?.OnStopJump?.Invoke();
                }

                if (!_traversal.IsInWater)
                    _hostScout.Events?.OnLandedFall?.Invoke();
            }

            // Transitioning into water
            if (_traversal.IsInWater)
            {
                // Clear the transient state when entiring water for the first time
                if (!_traversal.WasInWater)
                    Locomotion.ClearStateAll();

                if (Locomotion.Swim.IsEnabled)
                {
                    Locomotion.Swim.IsSwimming = true;
                    Locomotion.Swim.IsDiving = _hostScout.WorldPosition.y < _cachedMovementState.SurfaceLevel;

                    Locomotion.Swim.UpdateDiveTime();

                    if (_traversal.IsInWater && Locomotion.Swim.IsDrowning)
                        _hostScout.Events?.OnDrowning?.Invoke(Locomotion.Swim.UnderwaterTimer);
                }

                return;
            }

            // Transitioning from water to land
            if (_traversal.WasInWater && Locomotion.Swim.IsSwimming)
                Locomotion.Swim.ClearState();

            if (_traversal.IsGrounded && Locomotion.Slide.IsEnabled)
            {
                bool isSliding = IsTooSteep();
                if (!isSliding && Locomotion.Slide.IsSliding)
                    Locomotion.IsInControl = true; // reset control

                Locomotion.Slide.IsSliding = isSliding;
            }
        }

        private bool DidPlatformChange()
        {
            if (!Locomotion.Platform.IsEnabled
                || Locomotion.Platform.ActivePlatform == _cachedMovementState.HitObject)
                return Locomotion.Platform.IsNewPlatform = false;

            if (Locomotion.Platform.ActivePlatform == null
                || Locomotion.Platform.ActivePlatform != _cachedMovementState.HitObject)
            {
                Locomotion.Platform.LastMatrix = Locomotion.Platform.ActivePlatform == null
                    ? _cachedMovementState.GroundMatrix
                    : Locomotion.Platform.ActiveMatrix;
                Locomotion.Platform.ActiveMatrix = _cachedMovementState.GroundMatrix;
                Locomotion.Platform.ActivePlatform = _cachedMovementState.HitObject;
                Locomotion.Platform.FrameVelocity = Vector3d.Zero;
                Locomotion.Platform.ActiveVelocity = Vector3d.Zero;
                return Locomotion.Platform.IsNewPlatform = true;
            }

            return Locomotion.Platform.IsNewPlatform = false;
        }

        private void HandleFallState()
        {
            if (Locomotion.Fall.IsFalling)
            {
                // Make sure we didn't somehow get above the initial start point
                if (_hostScout.WorldPosition.y > Locomotion.Fall.FallStart)
                    Locomotion.Fall.FallStart = _hostScout.WorldPosition.y;

                if (!_traversal.IsInAir && !Locomotion.Slide.IsSliding)
                {
                    // scout landed after falling
                    Locomotion.Fall.IsFalling = false;
                    Locomotion.Fall.FallEnd = _hostScout.WorldPosition.y;

                    if (Locomotion.Fall.FallHeight > Fixed64.Zero)
                        _hostScout.Events?.OnStopFall?.Invoke(Locomotion.Fall.FallHeight);

                    return;
                }

                Fixed64 fallHeight = (Locomotion.Fall.FallStart - _hostScout.WorldPosition.y).Abs();
                if (fallHeight > Locomotion.Fall.MaxFallHeight)
                    _hostScout.Events?.OnMaxFallHeightReached?.Invoke();

                return;
            }

            // check if we are currently falling with a small threshold
            if ((_traversal.IsInAir || Locomotion.Slide.IsSliding) && _cachedForce.y < -Fixed64.FromRaw(0x00010000L))
            {
                // scout started falling
                Locomotion.Fall.IsFalling = true;
                Locomotion.Fall.FallStart = _hostScout.WorldPosition.y;
                _hostScout.Events?.OnStartFall?.Invoke();
            }
        }

        private void HandlePlatformUpdates()
        {
            // Don't process platform state when in water
            if (_traversal.IsInWater)
                return;

            // Subtract only horizontal velocity, preserving vertical momentum
            Vector3d adjustedPlatformVelocity = Locomotion.Platform.ActiveVelocity;
            adjustedPlatformVelocity.y = Fixed64.Zero; // Keep vertical momentum

            // If scout landed on a new platform, we have to wait for two frames
            // before we know the new velocity of the platform under the scout
            if (Locomotion.Platform.IsHoldingPlatform)
            {
                bool release = Locomotion.Platform.UpdateHoldOnPlatform();
                if (release && _traversal.IsGrounded)
                    _cachedForce -= adjustedPlatformVelocity;
            }

            if (Locomotion.Platform.IsInteriaApplied)
            {
                if (!_traversal.WasInAir && _traversal.IsInAir)
                {
                    Locomotion.Platform.FrameVelocity = Locomotion.Platform.ActiveVelocity;
                    _cachedForce += Locomotion.Platform.FrameVelocity;  // Apply inertia from platform
                }

                if (!_traversal.WasGrounded && _traversal.IsGrounded)
                {
                    if (Locomotion.Platform.IsNewPlatform)
                        Locomotion.Platform.SetHoldPlatform(_cachedMovementState.HitObject);
                    else
                        _cachedForce -= adjustedPlatformVelocity;
                }
            }

            if (Locomotion.Platform.ActivePlatform != null)
                UpdatePlatformVelocity();
            else
                Locomotion.Platform.ActiveVelocity = Vector3d.Zero;

            if (IsPlatformMovementApplied)
            {
                UpdatePlatformMovement();
                ApplyPlatformMovement();
            }

            Locomotion.Platform.LastMatrix = Locomotion.Platform.ActiveMatrix;
        }

        private void UpdatePlatformMovement()
        {
            Vector3d footPosition = _hostScout.GetFootPosition();
            footPosition.y += Locomotion.Platform.HeightAdjust;
            Locomotion.Platform.ActiveGlobalPoint = footPosition;
            Locomotion.Platform.ActiveLocalPoint = Fixed4x4.InverseTransformPoint(
                Locomotion.Platform.ActiveMatrix,
                Locomotion.Platform.ActiveGlobalPoint);

            Locomotion.Platform.ActiveGlobalRotation = _hostScout.VisualRotation;
            Locomotion.Platform.ActiveLocalRotation = Locomotion.Platform.ActiveMatrix.Rotation.Inverse() * Locomotion.Platform.ActiveGlobalRotation;
        }

        private void UpdatePlatformVelocity()
        {
            if (!Locomotion.Platform.IsNewPlatform)
            {
                Vector3d currentPoint = Locomotion.Platform.ActiveMatrix.TransformPoint(Locomotion.Platform.ActiveLocalPoint);
                Vector3d previousPoint = Locomotion.Platform.LastMatrix.TransformPoint(Locomotion.Platform.ActiveLocalPoint);
                Locomotion.Platform.ActiveVelocity = (currentPoint - previousPoint) / TrailblazerManager.DeltaTime;
            }
        }

        private void ApplyPlatformMovement()
        {
            if (!_traversal.IsGrounded || Locomotion.Platform.ActiveVelocity == Vector3d.Zero)
                return;

            FixedQuaternion targetRotation = Locomotion.Platform.ActiveMatrix.Rotation * Locomotion.Platform.ActiveLocalRotation;

            // Apply platform rotation FIRST
            // THEN apply platform movement
            if (Mode == ControllerMode.PositionBased)
            {

                if (targetRotation != FixedQuaternion.Identity)
                    _hostScout.Events?.OnSetRotation?.Invoke(targetRotation);

                Vector3d moveDistance = Locomotion.Platform.ActiveVelocity * TrailblazerManager.DeltaTime;
                if (moveDistance != Vector3d.Zero)
                    _hostScout.Events?.OnAddPositionDelta?.Invoke(moveDistance);
            }
            else if (Mode == ControllerMode.ForceBased)
            {
                if (targetRotation != FixedQuaternion.Identity)
                {
                    // --- ANGULAR FORCE CALCULATION ---
                    Vector3d requiredAngularVelocity = targetRotation.ToAngularVelocity(_hostScout.VisualRotation, TrailblazerManager.DeltaTime);
                    _hostScout.Events?.OnAddAngularForce?.Invoke(requiredAngularVelocity);
                }

                if (Locomotion.Platform.ActiveVelocity != Vector3d.Zero)
                {
                    // --- LINEAR FORCE CALCULATION ---
                    Vector3d requiredLinearVelocity = Locomotion.Platform.ActiveVelocity - _hostScout.LinearVelocity;
                    _hostScout.Events?.OnAddLinearForce?.Invoke(requiredLinearVelocity);
                }
            }
        }

        /// <summary>
        /// Find desired velocity
        /// </summary>
        private void ComputeMovementForces()
        {
            Fixed64 maxVelocityChange = GetMaxAcceleration() * TrailblazerManager.DeltaTime;
            Vector3d velocityChange = (GetDesiredVelocity() - _hostScout.LinearVelocity).ClampMagnitude(maxVelocityChange);

            if (Locomotion.Fall.IsFalling)
                velocityChange.y = Fixed64.Zero; // let gravity handle any downward momentum

            if (_traversal.IsGrounded || Locomotion.IsInControl)
                _cachedForce += velocityChange;

            // When going uphill, the IScout will automatically move up by the needed amount.
            // Not moving it upwards manually prevent risk of lifting off from the ground.
            // When going downhill, DO move down manually, as gravity is not enough on steep hills.
            if (_traversal.IsGrounded)
                _cachedForce.y = FixedMath.Min(_cachedForce.y, Fixed64.Zero);
        }

        private Vector3d GetDesiredVelocity()
        {
            Vector3d result;
            if (Locomotion.Slide.IsSliding)
            {
                // The direction we're sliding in
                result = new Vector3d(_traversal.GroundNormal.x, Fixed64.Zero, _traversal.GroundNormal.z).Normal;
                // Find the input movement direction projected onto the sliding direction
                Vector3d projectedMoveDir = Vector3d.Project(_cachedMoveDirection, result);
                // Add the sliding direction, the speed control, and the sideways control vectors
                result = result + projectedMoveDir
                    * Locomotion.Slide.SpeedControl + (_cachedMoveDirection - projectedMoveDir)
                    * Locomotion.Slide.SidewaysControl;
                // Multiply with the sliding speed

                Fixed64 adjustedSlideSpeed = Locomotion.Slide.SlidingSpeed
                    * (Fixed64.One - Locomotion.Move.SurfaceFriction);
                result *= adjustedSlideSpeed;

                Locomotion.IsInControl = false;
            }
            else
            {
                result = GetHorizontalVelocity();

                // Ensure that the desired movement of the scout aligns with the surface they are on
                // i.e., the scout doesn't try to move into the ground when the ground is sloping upwards
                if (_traversal.IsGrounded)
                    result = Vector3d.ProjectOnPlane(result, _traversal.GroundNormal);

                if (!InLimbo) // see if IScout gets control back
                    Locomotion.IsInControl = true;
                else
                    return Vector3d.Zero;

                if (_traversal.IsInWater) // Ensure smoother stops in water instead of abrupt halts
                    return result * (Fixed64.One - Locomotion.Swim.WaterDragFactor);
            }

            if (IsPlatformMovementApplied)
            {
                result += Locomotion.Platform.FrameVelocity;
                result.y = Fixed64.Zero;
            }

            // Ensures scout does not "digging into" the ground when moving over a bump
            if (_traversal.IsGrounded)
            {
                Vector3d sideways = Vector3d.Cross(Vector3d.Up, result);
                result = Vector3d.Cross(sideways, _cachedMovementState.GroundNormal).Normal * result.Magnitude;
            }

            return result;
        }

        private Vector3d GetHorizontalVelocity()
        {
            Fixed3x3 transposedMatrix = _hostScout.VisualRotation.ToMatrix3x3();
            Vector3d desiredLocalDirection = Fixed3x3.InverseTransformDirection(transposedMatrix, _cachedMoveDirection);
            Fixed64 speed = MaxSpeedInDirection(desiredLocalDirection);

            speed *= Locomotion.Move.MoveSpeedMultiplier;

            if (IsOnSlope())
            {
                // Modify max speed on slopes based on slope speed multiplier curve
                Fixed64 movementSlopeAngle = FixedMath.Asin(_traversal.GroundNormal.y.ClampOne()).ToDegree();
                speed *= Locomotion.Move.SlopeSpeedMultiplier.Evaluate(movementSlopeAngle);
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
                maxSpeed = Locomotion.Move.MaxBackwardsSpeed;
            else
            {
                switch (_cachedMovementInput)
                {
                    case MoveInput.Walk:
                        maxSpeed = Locomotion.Move.MaxWalkSpeed;
                        break;
                    case MoveInput.Jog:
                        maxSpeed = Locomotion.Move.MaxJogSpeed;
                        break;
                    case MoveInput.Sprint:
                        maxSpeed = Locomotion.Move.MaxSprintSpeed;
                        break;
                }
            }

            Vector3d temp;
            Fixed64 zAxisEllipseMultiplier;
            if (_traversal.IsInWater)
            {
                zAxisEllipseMultiplier = Locomotion.Swim.MaxSwimSpeed / Locomotion.Swim.MaxSwimSidewaysSpeed;
                temp = new Vector3d(
                    desiredMovementDirection.x,
                    Fixed64.Zero,
                    desiredMovementDirection.z / zAxisEllipseMultiplier).Normal;
            }
            else
            {
                zAxisEllipseMultiplier = maxSpeed / Locomotion.Move.MaxSidewaysSpeed;
                if (zAxisEllipseMultiplier <= Fixed64.Zero)
                    return Fixed64.Zero;

                temp = new Vector3d(
                    desiredMovementDirection.x,
                    Fixed64.Zero,
                    desiredMovementDirection.z / zAxisEllipseMultiplier).Normal;
            }

            Fixed64 length = new Vector3d(temp.x, Fixed64.Zero, temp.z * zAxisEllipseMultiplier).Magnitude;
            return length * (_traversal.IsInWater
                ? Locomotion.Swim.MaxSwimSpeed
                : Locomotion.Move.MaxSidewaysSpeed);
        }

        private Fixed64 GetMaxAcceleration()
        {
            if (Locomotion.Swim.IsSwimming)
                return Locomotion.Swim.MaxVelocity;

            if (_traversal.IsGrounded)
                return Locomotion.Move.MaxGroundAcceleration;

            if (Locomotion.Jump.IsJumping)
                return Locomotion.Move.MaxAirAcceleration * Locomotion.Jump.JumpControlMultiplier;

            if (Locomotion.Fall.IsFalling)
                return Locomotion.Move.MaxAirAcceleration * Locomotion.Fall.FallControlMultiplier;

            if (_traversal.IsInAir)
                return Locomotion.Move.MaxAirAcceleration;

            return Fixed64.MaxValue; // fallback, should never be hit
        }

        private void ApplyGroundOrGravityImpulse()
        {
            if (_traversal.IsGrounded)
            {
                // Actively cancel any existing vertical momentum by simulating the normal force of the ground.
                if (_hostScout.LinearVelocity.y < Fixed64.Zero)
                    _cachedForce.y = -_hostScout.LinearVelocity.y;
                else
                    _cachedForce.y = Fixed64.Zero;

                return;
            }

            if (_traversal.IsInAir)
            {
                // Apply gravity impulse correctly, considering velocity-to-force conversion
                // TODO: if we want to support moving while in air, does this need to skip?
                _cachedForce.y -= Gravity;

                // Prevent excessive falling speed (terminal velocity)
                _cachedForce.y = FixedMath.Max(_cachedForce.y, -TrailblazerManager.TerminalFallVelocity);

                // When jumping up we don't apply gravity for some time when the user is holding the jump button.
                // This gives more control over jump height by pressing the button longer.
                if (Locomotion.Jump.IsJumping && Locomotion.Jump.IsHoldingJump)
                {
                    // Calculate the duration that the extra jump force should have effect.
                    // If we're still less than that duration after the jumping time, apply the force.
                    int extraJumpLimit = (int)(Locomotion.Jump.FrameStartJump + Locomotion.Jump.ExtraJumpHeight / GetVerticalJumpSpeed());

                    // Negate the gravity we just applied, except we push in jumpDir rather than jump upwards.
                    if (TrailblazerManager.FrameCount < extraJumpLimit)
                        _cachedForce += Locomotion.Jump.FrameJumpDirection * Gravity * TrailblazerManager.DeltaTime;
                }
            }
        }

        private void ApplyJumpImpulse()
        {
            if (_traversal.IsInAir
                || _traversal.IsInWater && !Locomotion.Swim.CanBreachWater
                || Locomotion.Jump.IsCoolingDown
                || _hostScout.Events?.CanAffordJump?.Invoke() == false)
            {
                return;
            }

            Vector3d jumpForce;
            if (_traversal.IsInWater)
            {
                Locomotion.Jump.FrameJumpDirection = Vector3d.Up;
                jumpForce = Locomotion.Jump.FrameJumpDirection * Locomotion.Swim.BuoyantForce;
                _hostScout.Events?.OnStartWaterBreach?.Invoke();
            }
            else
            {
                // Calculate the jumping direction
                Fixed64 slerpAmount = IsTooSteep()
                    ? Locomotion.Jump.SteepPerpendicularJumpAmount
                    : Locomotion.Jump.PerpendicularJumpAmount;

                // Store jump direction the first time we jump
                if (!Locomotion.Jump.IsJumping)
                    Locomotion.Jump.FrameJumpDirection = Vector3d.Slerp(Vector3d.Up, _traversal.GroundNormal, slerpAmount);

                jumpForce = Locomotion.Jump.FrameJumpDirection * (GetVerticalJumpSpeed() / TrailblazerManager.DeltaTime);

                // Apply inertia from platform and store velocity for landing momentum
                if (Locomotion.Platform.IsInteriaApplied)
                {
                    Locomotion.Platform.FrameVelocity = Locomotion.Platform.ActiveVelocity;
                    jumpForce += Locomotion.Platform.ActiveVelocity;
                }

                _hostScout.Events?.OnStartJump?.Invoke();
                _hostScout.Events?.OnSkipGroundingCheckTimer?.Invoke(Locomotion.Jump.AvoidGroundingTimer);
            }

            // If we aren't in air, trigger a new jump then...
            Locomotion.Jump.IsJumping = true;
            Locomotion.Jump.IsHoldingJump = true;
            Locomotion.Jump.FrameStartJump = TrailblazerManager.FrameCount;

            Locomotion.Jump.StartCooldown();

            // Remove any existing downward force
            _cachedForce.y = FixedMath.Max(Fixed64.Zero, _cachedForce.y);
            _cachedForce += jumpForce;
        }

        /// <summary>
        /// From the jump height and gravity we deduce the upwards speed for the character to reach at the apex.
        /// </summary>
        /// <returns></returns>
        private Fixed64 GetVerticalJumpSpeed() => FixedMath.Sqrt(2 * Locomotion.Jump.BaseJumpHeight * Gravity);

        #endregion

        #region Utility

        public void SetMotorLock(bool status) => IsMotorLocked = status;

        public bool IsTooSteep()
        {
            Fixed64 angle = Vector3d.Angle(Vector3d.Up, _traversal.GroundNormal);
            return angle > Locomotion.Slide.SlopeLimit - Fixed64.Epsilon;
        }

        public bool IsOnSlope()
        {
            if (!_traversal.IsGrounded) return false;
            Fixed64 angle = Vector3d.Angle(Vector3d.Up, _traversal.GroundNormal);
            return angle > Fixed64.One && angle <= Locomotion.Slide.SlopeLimit + Fixed64.Epsilon;
        }

        #endregion
    }
}

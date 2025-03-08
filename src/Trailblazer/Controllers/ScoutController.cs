using FixedMathSharp;
using System;
using System.Diagnostics;
using System.Numerics;
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
        Force = 0,
        /// <summary>
        /// Calculates and outputs expected position updates.
        /// </summary>
        PositionDelta = 1
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

        public TraversalMedium ActiveMedium { get; set; }

        public TraversalMedium LastMedium { get; set; }

        public Fixed64 SurfaceLevel { get; set; }

        public Fixed64 LastSurfaceLevel { get; set; }

        public Vector3d GroundNormal { get; set; }

        public Vector3d LastGroundNormal { get; set; }

        public bool StateChanged => ActiveMedium != LastMedium
            && ActiveMedium != TraversalMedium.Unknown
            && LastMedium != TraversalMedium.Unknown;

        public bool IsGrounded => ActiveMedium == TraversalMedium.Ground;
        public bool WasGrounded => LastMedium == TraversalMedium.Ground;

        public bool IsInAir => ActiveMedium == TraversalMedium.Air;
        public bool WasInAir => LastMedium == TraversalMedium.Air;

        public bool IsInWater => ActiveMedium == TraversalMedium.Water;
        public bool WasInWater => LastMedium == TraversalMedium.Water;

        public bool InLimbo => IsInAir && !Locomotion.Jump.IsJumping && !Locomotion.Fall.IsFalling;

        /// <summary>
        /// Whether the motor is locked.
        /// </summary>
        public bool IsMotorLocked { get; private set; }

        /// <inheritdoc cref="TrailblazerManager.GravityForce"/>
        public Fixed64 Gravity { get; set; } = TrailblazerManager.GravityForce;

        #region Cache

        [NonSerialized]
        private Vector3d _frameMoveDirection;

        [NonSerialized]
        private MoveInput _frameMoveInput;

        [NonSerialized]
        private Vector3d _accelerationDelta;

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
                    $"Grounded={IsGrounded}, " +
                    $"InAir={IsInAir}, " +
                    $"Velocity={_hostScout.LinearVelocity}");

            _frameMoveInput = movementInput;
            _frameMoveDirection = Locomotion.IsInControl ? direction : Vector3d.Zero;

            // Reset the target impulse to prevent accumulation
            _accelerationDelta = Vector3d.Zero;

            CheckTraversalState();

            HandleMovementTransitions();

            ComputeMovementForces();

            // Register any additional movement after applying platform movement
            if (Locomotion.Platform.IsEnabled)
                HandlePlatformUpdates();

            if (!hasJumpRequest) // reset this before applying gravity
                Locomotion.Jump.IsHoldingJump = false;

            // TODO: for water, handle bouyant force
            if (!Locomotion.Swim.IsSwimming)
                ApplyGroundOrGravityImpulse();

            if (Locomotion.Jump.IsEnabled && Locomotion.IsInControl && hasJumpRequest)
                ApplyJumpImpulse();

            if (Locomotion.Fall.IsEnabled && !Locomotion.Swim.IsSwimming)
                HandleFallState();

            // Corrects the Y axis if swimming or no gravity
            if (Locomotion.Swim.IsSwimming)
                _accelerationDelta.y = _frameMoveDirection.y;

            // Apply the force
            if (_accelerationDelta != Vector3d.Zero)
            {
                if (Mode == ControllerMode.Force)
                    _hostScout.Events?.OnAddLinearForce?.Invoke(_accelerationDelta);
                else if (Mode == ControllerMode.PositionDelta)
                    _hostScout.Events?.OnAddPositionDelta?.Invoke(_accelerationDelta * TrailblazerManager.DeltaTime);
            }

            // Reset before returning
            Reset();
        }

        public void Reset()
        {
            _frameMoveInput = MoveInput.Idle;
            _frameMoveDirection = Vector3d.Zero;
            _accelerationDelta = Vector3d.Zero;
        }

        #endregion

        #region Movement Processing

        private void CheckTraversalState()
        {
            _hostScout.GetTraversalState(out TraversalState traversalState);

            LastMedium = ActiveMedium;
            ActiveMedium = traversalState.Medium;

            SurfaceLevel = traversalState.SurfaceLevel;

            if (!IsGrounded)  // Left the ground
            {
                LastGroundNormal = Vector3d.Zero;
                GroundNormal = Vector3d.Zero;
            }
            else
            {
                LastGroundNormal = GroundNormal;
                GroundNormal = traversalState.Ground?.GroundNormal ?? Vector3d.Zero;

                // If we hit a new platform, reset platform state
                if (traversalState.Ground.HasValue)
                    DidPlatformChange(traversalState.Ground.Value);
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

            if (WasInAir && !IsInAir) // Landed
            {
                if (Locomotion.Platform.IsInteriaApplied)
                    Locomotion.Platform.FrameVelocity *= Fixed64.Half; // Preserve some horizontal

                if (Locomotion.Jump.IsJumping)
                {
                    // Reset cooldown on landing
                    Locomotion.Jump.ClearState();

                    if (IsInWater)
                        _hostScout.Events?.OnStopWaterBreach?.Invoke();
                    else
                        _hostScout.Events?.OnStopJump?.Invoke();
                }

                if (!IsInWater)
                    _hostScout.Events?.OnLandedFall?.Invoke();
            }

            // Transitioning into water
            if (IsInWater)
            {
                // Clear the transient state when entiring water for the first time
                if (!WasInWater)
                    Locomotion.ClearStateAll();

                if (Locomotion.Swim.IsEnabled)
                {
                    Locomotion.Swim.IsSwimming = true;
                    Locomotion.Swim.IsDiving = _hostScout.WorldPosition.y < SurfaceLevel;

                    Locomotion.Swim.UpdateDiveTime();

                    if (IsInWater && Locomotion.Swim.IsDrowning)
                        _hostScout.Events?.OnDrowning?.Invoke(Locomotion.Swim.UnderwaterTimer);
                }

                return;
            }

            // Transitioning from water to land
            if (WasInWater && Locomotion.Swim.IsSwimming)
                Locomotion.Swim.ClearState();

            if (IsGrounded && Locomotion.Slide.IsEnabled)
            {
                bool isSliding = IsTooSteep();
                if (!isSliding && Locomotion.Slide.IsSliding)
                    Locomotion.IsInControl = true; // reset control

                Locomotion.Slide.IsSliding = isSliding;
            }
        }

        private bool DidPlatformChange(GroundState groundState)
        {
            if (!Locomotion.Platform.IsEnabled
                || Locomotion.Platform.ActivePlatform == groundState.HitObject)
                return Locomotion.Platform.IsNewPlatform = false;

            if (Locomotion.Platform.ActivePlatform == null
                || Locomotion.Platform.ActivePlatform != groundState.HitObject)
            {
                Fixed4x4 newGroundMatrix = groundState.GroundMatrix ?? Fixed4x4.Identity;

                Locomotion.Platform.LastMatrix = Locomotion.Platform.ActivePlatform == null
                    ? newGroundMatrix
                    : Locomotion.Platform.ActiveMatrix;
                Locomotion.Platform.ActiveMatrix = newGroundMatrix;
                Locomotion.Platform.ActivePlatform = groundState.HitObject;
                Locomotion.Platform.FrameVelocity = Vector3d.Zero;
                Locomotion.Platform.Velocity = Vector3d.Zero;
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

                if (!IsInAir && !Locomotion.Slide.IsSliding)
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
            if ((IsInAir || Locomotion.Slide.IsSliding) && _accelerationDelta.y < -Fixed64.FromRaw(0x00010000L))
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
            if (IsInWater)
                return;

            // Subtract only horizontal velocity, preserving vertical momentum
            Vector3d adjustedPlatformAcceleration = Locomotion.Platform.Velocity / TrailblazerManager.DeltaTime;
            adjustedPlatformAcceleration.y = Fixed64.Zero; // Keep vertical momentum

            // If scout landed on a new platform, we have to wait for two frames
            // before we know the new velocity of the platform under the scout
            if (Locomotion.Platform.IsHoldingPlatform)
            {
                bool release = Locomotion.Platform.UpdateHoldOnPlatform();
                if (release && IsGrounded)
                    _accelerationDelta -= adjustedPlatformAcceleration;
            }

            if (Locomotion.Platform.IsInteriaApplied)
            {
                if (!WasInAir && IsInAir)
                {
                    Locomotion.Platform.FrameVelocity = Locomotion.Platform.Velocity;
                    // Apply inertia from platform
                    _accelerationDelta += Locomotion.Platform.FrameVelocity / TrailblazerManager.DeltaTime;
                }

                if (!WasGrounded && IsGrounded)
                {
                    if (Locomotion.Platform.IsNewPlatform)
                        Locomotion.Platform.SetHoldPlatform(Locomotion.Platform.ActivePlatform);
                    else
                        _accelerationDelta -= adjustedPlatformAcceleration;
                }
            }

            if (Locomotion.Platform.ActivePlatform != null)
                UpdatePlatformVelocity();
            else
                Locomotion.Platform.Velocity = Vector3d.Zero;

            if (IsGrounded && Locomotion.Platform.IsOnPlatform || Locomotion.Platform.IsLockedToPlatform)
            {
                if (IsGrounded)
                    ApplyPlatformMovement();
                UpdatePlatformMovement();
            }

            Locomotion.Platform.LastMatrix = Locomotion.Platform.ActiveMatrix;
            Locomotion.Platform.IsNewPlatform = false;
        }

        private void UpdatePlatformMovement()
        {
            Vector3d footPosition = _hostScout.GetFootPosition();
            footPosition.y += Locomotion.Platform.HeightAdjust;
            Locomotion.Platform.ScoutGlobalPoint = footPosition;
            Locomotion.Platform.ScoutLocalPoint = Fixed4x4.InverseTransformPoint(
                Locomotion.Platform.ActiveMatrix,
                Locomotion.Platform.ScoutGlobalPoint);

            Locomotion.Platform.ScoutGlobalRotation = _hostScout.VisualRotation;
            Locomotion.Platform.ScoutLocalRotation = Locomotion.Platform.ActiveMatrix.Rotation.Inverse() * Locomotion.Platform.ScoutGlobalRotation;
        }

        private void UpdatePlatformVelocity()
        {
            if (!Locomotion.Platform.IsNewPlatform)
            {
                Vector3d currentPoint = Locomotion.Platform.ActiveMatrix.TransformPoint(Locomotion.Platform.ScoutLocalPoint);
                Vector3d previousPoint = Locomotion.Platform.LastMatrix.TransformPoint(Locomotion.Platform.ScoutLocalPoint);
                Locomotion.Platform.Velocity = (currentPoint - previousPoint) / TrailblazerManager.DeltaTime;
            }
        }

        private void ApplyPlatformMovement()
        {
            switch (Mode)
            {
                case ControllerMode.Force:
                    ApplyPlatformMovementWithForce();
                    break;
                case ControllerMode.PositionDelta:
                    ApplyPlatformMovementWithPosition();
                    break;
                default:
                    break;
            }
        }

        private void ApplyPlatformMovementWithPosition()
        {
            // Apply platform rotation first THEN apply platform movement
            FixedQuaternion targetRotation = Locomotion.Platform.ActiveMatrix.Rotation * Locomotion.Platform.ScoutLocalRotation;
            if (targetRotation != FixedQuaternion.Identity)
            {
                FixedQuaternion rotationDiff = targetRotation * Locomotion.Platform.ScoutGlobalRotation.Inverse();
                _hostScout.Events?.OnAddRotationDelta?.Invoke(rotationDiff);
            }

            Vector3d newGlobalPoint = Locomotion.Platform.ActiveMatrix.TransformPoint(Locomotion.Platform.ScoutLocalPoint);
            Vector3d moveDistance = newGlobalPoint - Locomotion.Platform.ScoutGlobalPoint;
            if (moveDistance != Vector3d.Zero)
                _hostScout.Events?.OnAddPositionDelta?.Invoke(moveDistance);
        }

        private void ApplyPlatformMovementWithForce()
        {
            // Apply platform rotation first THEN apply platform movement
            FixedQuaternion targetRotation = Locomotion.Platform.ActiveMatrix.Rotation * Locomotion.Platform.ScoutLocalRotation;
            if (targetRotation != FixedQuaternion.Identity)
            {
                Vector3d requiredAngularVelocity = targetRotation.ToAngularVelocity(
                    _hostScout.VisualRotation,
                    TrailblazerManager.DeltaTime
                );
                _hostScout.Events?.OnAddAngularForce?.Invoke(requiredAngularVelocity);
            }

            if (Locomotion.Platform.Velocity != Vector3d.Zero)
            {
                Vector3d requiredAcceleration = (Locomotion.Platform.Velocity - _hostScout.LinearVelocity) / TrailblazerManager.DeltaTime;
                _hostScout.Events?.OnAddLinearForce?.Invoke(requiredAcceleration);
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
                velocityChange.y = Fixed64.Zero; // TODO: this should get added to gravity

            if (velocityChange == Vector3d.Zero)
                return;

            Vector3d accelerationChange = velocityChange / TrailblazerManager.DeltaTime;

            if (IsGrounded || Locomotion.IsInControl)
                _accelerationDelta += accelerationChange;

            // When going uphill, the IScout will automatically move up by the needed amount.
            // Not moving it upwards manually prevent risk of lifting off from the ground.
            // When going downhill, DO move down manually, as gravity is not enough on steep hills.
            if (IsGrounded)
                _accelerationDelta.y = FixedMath.Min(_accelerationDelta.y, Fixed64.Zero);
        }

        private Vector3d GetDesiredVelocity()
        {
            Vector3d result;
            if (Locomotion.Slide.IsSliding)
            {
                Locomotion.IsInControl = false;

                // The direction we're sliding in
                result = new Vector3d(GroundNormal.x, Fixed64.Zero, GroundNormal.z).Normal;
                // Find the input movement direction projected onto the sliding direction
                Vector3d projectedMoveDir = Vector3d.Project(_frameMoveDirection, result);
                // Add the sliding direction, the speed control, and the sideways control vectors
                result = result + projectedMoveDir
                    * Locomotion.Slide.SpeedControl + (_frameMoveDirection - projectedMoveDir)
                    * Locomotion.Slide.SidewaysControl;
                // Multiply with the sliding speed

                Fixed64 adjustedSlideSpeed = Locomotion.Slide.SlidingSpeed
                    * (Fixed64.One - Locomotion.Move.SurfaceFriction);
                result *= adjustedSlideSpeed;
            }
            else
            {
                Locomotion.IsInControl = !InLimbo; // see if IScout gets control back

                if (InLimbo || _frameMoveInput == MoveInput.Idle)
                    return Vector3d.Zero;

                result = GetHorizontalVelocity();

                // Ensure that the desired movement of the scout aligns with the surface they are on
                // i.e., the scout doesn't try to move into the ground when the ground is sloping upwards
                if (IsGrounded)
                    result = Vector3d.ProjectOnPlane(result, GroundNormal);

                // Ensure smoother stops in water instead of abrupt halts
                if (IsInWater)
                    return result * (Fixed64.One - Locomotion.Swim.WaterDragFactor);
            }

            if (IsGrounded && Locomotion.Platform.IsOnPlatform || Locomotion.Platform.IsLockedToPlatform)
            {
                result += Locomotion.Platform.FrameVelocity;
                // any upward/downward momentum will be handled by the platform
                result.y = Fixed64.Zero;
            }

            // Ensures scout does not "digging into" the ground when moving over a bump
            if (IsGrounded && result != Vector3d.Zero)
            {
                Vector3d sideways = Vector3d.Cross(Vector3d.Up, result);
                result = Vector3d.Cross(sideways, GroundNormal).Normal * result.Magnitude;
            }

            return result;
        }

        private Vector3d GetHorizontalVelocity()
        {
            Fixed3x3 transposedMatrix = _hostScout.VisualRotation.ToMatrix3x3();
            Vector3d desiredLocalDirection = Fixed3x3.InverseTransformDirection(transposedMatrix, _frameMoveDirection);
            Fixed64 speed = MaxSpeedInDirection(desiredLocalDirection);

            speed *= Locomotion.Move.MoveSpeedMultiplier;

            if (IsOnSlope())
            {
                // Modify max speed on slopes based on slope speed multiplier curve
                Fixed64 movementSlopeAngle = FixedMath.Asin(GroundNormal.y.ClampOne()).ToDegree();
                speed *= Locomotion.Move.SlopeSpeedMultiplier.Evaluate(movementSlopeAngle);
            }

            return transposedMatrix * (desiredLocalDirection * speed);
        }

        /// <summary>
        /// Project a direction onto elliptical quater segments based on forward, sideways, and backwards speed.
        /// </summary>
        /// <returns>Returns the length of the resulting vector.</returns>
        public Fixed64 MaxSpeedInDirection(Vector3d desiredMovementDirection)
        {
            if (desiredMovementDirection == Vector3d.Zero)
                return Fixed64.Zero;

            Fixed64 maxSpeed = Fixed64.Zero;
            if (desiredMovementDirection.z < Fixed64.Zero)
                maxSpeed = Locomotion.Move.MaxBackwardsSpeed;
            else
            {
                switch (_frameMoveInput)
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
            if (IsInWater)
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
            return length * (IsInWater
                ? Locomotion.Swim.MaxSwimSpeed
                : Locomotion.Move.MaxSidewaysSpeed);
        }

        private Fixed64 GetMaxAcceleration()
        {
            if (Locomotion.Swim.IsSwimming)
                return Locomotion.Swim.MaxVelocity;

            if (IsGrounded)
                return Locomotion.Move.MaxGroundAcceleration;

            if (Locomotion.Jump.IsJumping)
                return Locomotion.Move.MaxAirAcceleration * Locomotion.Jump.JumpControlMultiplier;

            if (Locomotion.Fall.IsFalling)
                return Locomotion.Move.MaxAirAcceleration * Locomotion.Fall.FallControlMultiplier;

            if (IsInAir)
                return Locomotion.Move.MaxAirAcceleration;

            return Fixed64.MaxValue; // fallback, should never be hit
        }

        private void ApplyGroundOrGravityImpulse()
        {
            if (IsGrounded)
            {
                // Actively cancel any existing downward momentum by simulating the normal force of the ground.
                if (_hostScout.LinearVelocity.y < Fixed64.Zero)
                    _accelerationDelta.y = -_hostScout.LinearVelocity.y / TrailblazerManager.DeltaTime;
                else
                    _accelerationDelta.y = Fixed64.Zero;

                return;
            }

            if (IsInAir)
            {
                // Apply gravity impulse correctly, considering velocity-to-force conversion
                // TODO: if we want to support moving while in air, does this need to skip?
                _accelerationDelta.y -= Gravity;

                // Prevent excessive falling speed (terminal velocity)
                _accelerationDelta.y = FixedMath.Max(_accelerationDelta.y, -TrailblazerManager.TerminalFallVelocity);

                // When jumping up we don't apply gravity for some time when the user is holding the jump button.
                // This gives more control over jump height by pressing the button longer.
                if (Locomotion.Jump.IsJumping && Locomotion.Jump.IsHoldingJump)
                {
                    // Calculate the duration that the extra jump force should have effect.
                    // If we're still less than that duration after the jumping time, apply the force.
                    int extraJumpLimit = (int)(Locomotion.Jump.FrameStartJump + Locomotion.Jump.ExtraJumpHeight / GetVerticalJumpSpeed());

                    // Negate the gravity we just applied, except we push in jumpDir rather than jump upwards.
                    if (TrailblazerManager.FrameCount < extraJumpLimit)
                        _accelerationDelta += Locomotion.Jump.FrameJumpDirection * Gravity;
                }
            }
        }

        private void ApplyJumpImpulse()
        {
            if (IsInAir
                || IsInWater && !Locomotion.Swim.CanBreachWater
                || Locomotion.Jump.IsCoolingDown
                || _hostScout.Events?.CanAffordJump?.Invoke() == false)
            {
                return;
            }

            Vector3d jumpForce;
            if (IsInWater)
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
                    Locomotion.Jump.FrameJumpDirection = Vector3d.Slerp(Vector3d.Up, GroundNormal, slerpAmount);

                jumpForce = Locomotion.Jump.FrameJumpDirection * GetVerticalJumpSpeed();

                // Apply inertia from platform and store velocity for landing momentum
                if (Locomotion.Platform.IsInteriaApplied)
                {
                    Locomotion.Platform.FrameVelocity = Locomotion.Platform.Velocity;
                    jumpForce += Locomotion.Platform.Velocity / TrailblazerManager.DeltaTime;
                }

                _hostScout.Events?.OnStartJump?.Invoke(Locomotion.Jump.AvoidGroundingTimer);
            }

            // If we aren't in air, trigger a new jump then...
            Locomotion.Jump.IsJumping = true;
            Locomotion.Jump.IsHoldingJump = true;
            Locomotion.Jump.FrameStartJump = TrailblazerManager.FrameCount;

            Locomotion.Jump.StartCooldown();

            // Remove any existing downward force
            _accelerationDelta.y = FixedMath.Max(Fixed64.Zero, _accelerationDelta.y);
            _accelerationDelta += jumpForce;
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
            Fixed64 angle = Vector3d.Angle(Vector3d.Up, GroundNormal);
            return angle > Locomotion.Slide.SlopeLimit - Fixed64.Epsilon;
        }

        public bool IsOnSlope()
        {
            if (!IsGrounded) return false;
            Fixed64 angle = Vector3d.Angle(Vector3d.Up, GroundNormal);
            return angle > Fixed64.One && angle <= Locomotion.Slide.SlopeLimit + Fixed64.Epsilon;
        }

        #endregion
    }
}

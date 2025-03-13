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
    public enum OutputMode
    {
        /// <summary>   
        /// Standard force application.
        /// </summary>
        Force = 0,
        /// <summary>
        /// Calculates and outputs expected position updates.
        /// </summary>
        Position = 1
    }

    /// <summary>
    /// The type of movement input.
    /// </summary>
    public enum TraversalSpeed
    {
        Idle = 0,
        Walk = 1,
        Jog = 2,
        Sprint = 3,
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
        public LocomotionMotor Locomotions = new();

        /// <summary>
        /// The mode of the motor.
        /// </summary>
        public OutputMode Mode;

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

        public bool InLimbo => IsInAir && !Locomotions.Jump.IsJumping && !Locomotions.Fall.IsFalling;

        /// <summary>
        /// Whether the motor is locked.
        /// </summary>
        public bool IsControllerLocked { get; private set; }

        /// <inheritdoc cref="TrailblazerManager.GravityForce"/>
        public Fixed64 Gravity { get; set; } = TrailblazerManager.GravityForce;

        #region Cache

        [NonSerialized]
        private TraversalRequest _frameTraversalRequest;

        [NonSerialized]
        private Vector3d _forceOutput;

        #endregion

        #endregion

        #region Lifecycle

        public static ScoutController CreateNew(IScout scout)
        {
            ScoutController controller = new();
            controller.Initialize(scout);
            return controller;
        }

        public void Initialize(IScout scout) => _hostScout = scout;

        public void Simulate(Vector3d movementDirection, TraversalSpeed traversalSpeed, bool isRequestingJump = false)
        {
            Simulate(new TraversalRequest
            {
                MovementDirection = movementDirection,
                TraversalSpeed = traversalSpeed,
                IsRequestingJump = isRequestingJump
            });
        }
        
        /// <summary>
        /// Call once every simulation frame (i.e. FixedUpdate)
        /// </summary>
        /// <remarks>
        /// Controller will lock to prevent further accumulation of forces until unlocked for the next frame
        /// </remarks>
        public void Simulate(TraversalRequest traversalRequest)
        {
            if (_hostScout == null) return;

            if (IsControllerLocked)
                return;

            IsControllerLocked = true;

            if (DebugMode)
                Debug.WriteLine($"AgentMotor State: " +
                    $"Grounded={IsGrounded}, " +
                    $"InAir={IsInAir}, " +
                    $"Velocity={_hostScout.LinearVelocity}");

            _frameTraversalRequest = traversalRequest;

            // Reset the target impulse to prevent accumulation
            _forceOutput = Vector3d.Zero;

            CheckTraversalState();

            HandleMovementTransitions();

            ComputeMovementForces();

            // Register any additional movement after applying platform movement
            if (Locomotions.Platform.IsEnabled)
                HandlePlatformUpdates();

            if (!_frameTraversalRequest.IsRequestingJump) // reset this before applying gravity
                Locomotions.Jump.IsHoldingJump = false;

            // Include forces from gravity, water, and ground.
            ApplyEnvironmentalForces();

            if (Locomotions.Jump.IsEnabled && Locomotions.IsInControl && _frameTraversalRequest.IsRequestingJump)
                ApplyJumpForce();

            if (Locomotions.Fall.IsEnabled && !Locomotions.Swim.IsSwimming)
                HandleFallState();

            ApplyScoutMovement();

            // Reset before returning
            Reset();
        }

        public void Reset()
        {
            _frameTraversalRequest = default;
            _forceOutput = Vector3d.Zero;
        }

        #endregion

        #region Movement Processing

        private void CheckTraversalState()
        {
            _hostScout.GetTraversalState(out TraversalState traversalState);

            LastMedium = ActiveMedium;
            ActiveMedium = traversalState.Medium;

            LastSurfaceLevel = SurfaceLevel;
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
                if (Locomotions.Platform.IsEnabled)
                {
                    Locomotions.Platform.MovementTransfer = traversalState.Ground?.MovementTransfer ?? MovementTransferState.None;
                    if (DidPlatformChange(traversalState.Ground))
                    {
                        Fixed4x4 newGroundMatrix = traversalState.Ground?.GroundMatrix ?? Fixed4x4.Identity;

                        Locomotions.Platform.LastTransform = Locomotions.Platform.ActivePlatform == null
                            ? newGroundMatrix
                            : Locomotions.Platform.ActiveTransform;
                        Locomotions.Platform.ActiveTransform = newGroundMatrix;
                        Locomotions.Platform.ActivePlatform = traversalState.Ground?.HitObject;
                        Locomotions.Platform.ActiveVelocity = Vector3d.Zero;
                    }
                }
            }
        }
        private bool DidPlatformChange(GroundState? groundState)
        {
            if (Locomotions.Platform.ActivePlatform == groundState?.HitObject)
                return Locomotions.Platform.IsNewPlatform = false;

            if (Locomotions.Platform.ActivePlatform == null || Locomotions.Platform.ActivePlatform != groundState?.HitObject)
                return Locomotions.Platform.IsNewPlatform = true;

            return Locomotions.Platform.IsNewPlatform = false;
        }

        private void HandleMovementTransitions()
        {
            if (InLimbo)
            {
                // In limbo, prevent any further processing until control is given back
                Locomotions.IsInControl = false;
                return;
            }

            if (Locomotions.Jump.IsCoolingDown)
                Locomotions.Jump.UpdateCooldown();

            if (WasInAir && !IsInAir) // Landed
            {
                if (Locomotions.Platform.IsPlatformInteriaApplied)
                    Locomotions.Platform.FrameForce = Vector3d.Zero; // Preserve some horizontal

                if (Locomotions.Jump.IsJumping)
                {
                    // Reset cooldown on landing
                    Locomotions.Jump.ClearState();

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
                    Locomotions.ClearStateAll();

                if (Locomotions.Swim.IsEnabled)
                {
                    Locomotions.Swim.IsSwimming = true;
                    Locomotions.Swim.IsDiving = _hostScout.WorldPosition.y < SurfaceLevel;

                    Locomotions.Swim.UpdateDiveTime();

                    if (IsInWater && Locomotions.Swim.IsDrowning)
                        _hostScout.Events?.OnDrowning?.Invoke(Locomotions.Swim.UnderwaterTimer);
                }

                return;
            }

            // Transitioning from water to land
            if (WasInWater && Locomotions.Swim.IsSwimming)
                Locomotions.Swim.ClearState();

            if (IsGrounded && Locomotions.Slide.IsEnabled)
            {
                bool isSliding = IsTooSteep();
                if (!isSliding && Locomotions.Slide.IsSliding)
                    Locomotions.IsInControl = true; // reset control

                Locomotions.Slide.IsSliding = isSliding;
            }
        }

        private void HandleFallState()
        {
            if (Locomotions.Fall.IsFalling)
            {
                // Make sure we didn't somehow get above the initial start point
                if (_hostScout.WorldPosition.y > Locomotions.Fall.FallStart)
                    Locomotions.Fall.FallStart = _hostScout.WorldPosition.y;

                if (!IsInAir && !Locomotions.Slide.IsSliding)
                {
                    // scout landed after falling
                    Locomotions.Fall.IsFalling = false;
                    Locomotions.Fall.FallEnd = _hostScout.WorldPosition.y;

                    if (Locomotions.Fall.FallHeight > Fixed64.Zero)
                        _hostScout.Events?.OnStopFall?.Invoke(Locomotions.Fall.FallHeight);

                    return;
                }

                Fixed64 fallHeight = (Locomotions.Fall.FallStart - _hostScout.WorldPosition.y).Abs();
                if (fallHeight > Locomotions.Fall.MaxFallHeight)
                    _hostScout.Events?.OnMaxFallHeightReached?.Invoke();

                return;
            }

            // check if we are currently falling with a small threshold
            if ((IsInAir || Locomotions.Slide.IsSliding) && _forceOutput.y < -Fixed64.FromRaw(0x00010000L))
            {
                // scout started falling
                Locomotions.Fall.IsFalling = true;
                Locomotions.Fall.FallStart = _hostScout.WorldPosition.y;
                _hostScout.Events?.OnStartFall?.Invoke();
            }
        }

        private void HandlePlatformUpdates()
        {
            // Don't process platform state when in water
            if (IsInWater)
                return;

            // Convert platforms velocity into instantaneous velocity shift
            Vector3d adjustedPlatformForce = Locomotions.Platform.ActiveVelocity / TrailblazerManager.DeltaTime;
            adjustedPlatformForce.y = Fixed64.Zero; // preserve vertical momentum

            // If scout landed on a new platform, we have to wait for two frames
            // before we know the new velocity of the platform under the scout
            if (Locomotions.Platform.IsHoldingPlatform)
            {
                bool release = Locomotions.Platform.UpdateHoldOnPlatform();
                if (release && IsGrounded)
                    _forceOutput -= adjustedPlatformForce;
            }

            if (Locomotions.Platform.IsPlatformInteriaApplied)
            {
                if (!WasInAir && IsInAir)
                {
                    Locomotions.Platform.FrameForce = adjustedPlatformForce;
                    // Apply inertia from platform
                    _forceOutput += Locomotions.Platform.FrameForce;
                }

                if (!WasGrounded && IsGrounded)
                {
                    if (Locomotions.Platform.IsNewPlatform)
                        Locomotions.Platform.SetHoldPlatform(Locomotions.Platform.ActivePlatform);
                    else
                        _forceOutput -= adjustedPlatformForce;
                }
            }

            if (Locomotions.Platform.ActivePlatform != null)
                UpdatePlatformVelocity();
            else
                Locomotions.Platform.ActiveVelocity = Vector3d.Zero;

            if (IsGrounded && Locomotions.Platform.IsOnPlatform || Locomotions.Platform.IsLockedToPlatform)
            {
                if (IsGrounded)
                    ApplyPlatformMovement();
                UpdatePlatformMovement();
            }

            Locomotions.Platform.LastTransform = Locomotions.Platform.ActiveTransform;
            Locomotions.Platform.IsNewPlatform = false;
        }

        private void UpdatePlatformVelocity()
        {
            if (!Locomotions.Platform.IsNewPlatform)
            {
                Vector3d currentPoint = Locomotions.Platform.ActiveTransform.TransformPoint(Locomotions.Platform.ScoutLocalPoint);
                Vector3d previousPoint = Locomotions.Platform.LastTransform.TransformPoint(Locomotions.Platform.ScoutLocalPoint);

                // Store platform velocity to use as a canceling force
                Locomotions.Platform.ActiveVelocity = (currentPoint - previousPoint) / TrailblazerManager.DeltaTime;
            }
        }

        // platform movement should be treated as an external influence rather than an acceleration-based movement.
        // The Scout should inherit platform motion directly – since it's not "pushing" itself but rather being carried along.
        private void ApplyPlatformMovement()
        {
            // Apply platform rotation first THEN apply platform movement
            FixedQuaternion targetRotation = Locomotions.Platform.ActiveTransform.Rotation * Locomotions.Platform.ScoutLocalRotation;
            if (targetRotation != FixedQuaternion.Identity)
            {
                FixedQuaternion rotationDiff = targetRotation * Locomotions.Platform.ScoutGlobalRotation.Inverse();
                _hostScout.Events?.OnAddRotationDelta?.Invoke(rotationDiff);
            }

            Vector3d newGlobalPoint = Locomotions.Platform.ActiveTransform.TransformPoint(Locomotions.Platform.ScoutLocalPoint);
            Vector3d moveDistance = newGlobalPoint - Locomotions.Platform.ScoutGlobalPoint;
            if (moveDistance != Vector3d.Zero)
                _hostScout.Events?.OnAddPositionDelta?.Invoke(moveDistance);
        }

        private void UpdatePlatformMovement()
        {
            Vector3d footPosition = _hostScout.GetFootPosition();
            footPosition.y += Locomotions.Platform.HeightAdjust;
            Locomotions.Platform.ScoutGlobalPoint = footPosition;
            Locomotions.Platform.ScoutLocalPoint = Fixed4x4.InverseTransformPoint(
                Locomotions.Platform.ActiveTransform,
                Locomotions.Platform.ScoutGlobalPoint);

            Locomotions.Platform.ScoutGlobalRotation = _hostScout.VisualRotation;
            Locomotions.Platform.ScoutLocalRotation = Locomotions.Platform.ActiveTransform.Rotation.Inverse() * Locomotions.Platform.ScoutGlobalRotation;
        }

        /// <summary>
        /// Calculate the desired velocity to output as an acceleration delta
        /// </summary>
        private void ComputeMovementForces()
        {
            Fixed64 maxVelocityChange = GetMaxAcceleration() * TrailblazerManager.DeltaTime;
            Vector3d velocityChange = (GetDesiredVelocity() - _hostScout.LinearVelocity).ClampMagnitude(maxVelocityChange);

            if (Locomotions.Fall.IsFalling)
                velocityChange.y = Fixed64.Zero; // TODO: this should get added to gravity

            if (velocityChange == Vector3d.Zero)
                return;

            Vector3d accelerationChange = velocityChange / TrailblazerManager.DeltaTime;

            if (IsGrounded || Locomotions.IsInControl)
                _forceOutput += accelerationChange;

            // When going uphill, the IScout will automatically move up by the needed amount.
            // Not moving it upwards manually prevent risk of lifting off from the ground.
            // When going downhill, DO move down manually, as gravity is not enough on steep hills.
            if (IsGrounded)
                _forceOutput.y = FixedMath.Min(_forceOutput.y, Fixed64.Zero);
        }

        private Vector3d GetDesiredVelocity()
        {
            Vector3d result;
            if (Locomotions.Slide.IsSliding)
            {
                Locomotions.IsInControl = false;

                // The direction we're sliding in
                result = new Vector3d(GroundNormal.x, Fixed64.Zero, GroundNormal.z).Normal;
                // Find the input movement direction projected onto the sliding direction
                Vector3d projectedMoveDir = Vector3d.Project(_frameTraversalRequest.MovementDirection, result);
                // Add the sliding direction, the speed control, and the sideways control vectors
                result = result + projectedMoveDir
                    * Locomotions.Slide.SpeedControl + (_frameTraversalRequest.MovementDirection - projectedMoveDir)
                    * Locomotions.Slide.SidewaysControl;
                // Multiply with the sliding speed

                Fixed64 adjustedSlideSpeed = Locomotions.Slide.SlidingSpeed
                    * (Fixed64.One - Locomotions.Move.SurfaceFriction);
                result *= adjustedSlideSpeed;
            }
            else
            {
                Locomotions.IsInControl = !InLimbo; // see if IScout gets control back

                if (InLimbo || _frameTraversalRequest.TraversalSpeed == TraversalSpeed.Idle)
                    return Vector3d.Zero;

                result = GetHorizontalVelocity();

                // Ensure that the desired movement of the scout aligns with the surface they are on
                // i.e., the scout doesn't try to move into the ground when the ground is sloping upwards
                if (IsGrounded)
                    result = Vector3d.ProjectOnPlane(result, GroundNormal);

                // Ensure smoother stops in water instead of abrupt halts
                if (IsInWater)
                    return result * (Fixed64.One - Locomotions.Swim.WaterDragFactor);
            }

            if (Locomotions.Platform.IsLockedToPlatform)
            {
                result += Locomotions.Platform.FrameForce;
                // any upward/downward momentum will be handled by the platform or gravity
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
            Vector3d desiredLocalDirection = Fixed3x3.InverseTransformDirection(transposedMatrix, _frameTraversalRequest.MovementDirection);
            Fixed64 speed = MaxSpeedInDirection(desiredLocalDirection);

            speed *= Locomotions.Move.MoveSpeedMultiplier;

            if (Locomotions.Move.ModifySpeedOnSlope && IsOnSlope())
            {
                // Modify max speed on slopes based on slope speed multiplier curve
                Fixed64 movementSlopeAngle = FixedMath.Asin(GroundNormal.y.ClampOne()).ToDegree();
                speed *= Locomotions.Move.SlopeSpeedMultiplier.Evaluate(movementSlopeAngle);
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

            Vector3d temp;
            Fixed64 zAxisEllipseMultiplier;
            if (IsInWater)
            {
                zAxisEllipseMultiplier = Locomotions.Swim.MaxSwimSpeed / Locomotions.Swim.MaxSwimSidewaysSpeed;
                temp = new Vector3d(
                    desiredMovementDirection.x,
                    Fixed64.Zero,
                    desiredMovementDirection.z / zAxisEllipseMultiplier).Normal;
            }
            else
            {
                Fixed64 maxSpeed = Fixed64.Zero;
                if (desiredMovementDirection.z < Fixed64.Zero)
                    maxSpeed = Locomotions.Move.MaxBackwardsSpeed;
                else
                {
                    switch (_frameTraversalRequest.TraversalSpeed)
                    {
                        case TraversalSpeed.Walk:
                            maxSpeed = Locomotions.Move.MaxWalkSpeed;
                            break;
                        case TraversalSpeed.Jog:
                            maxSpeed = Locomotions.Move.MaxJogSpeed;
                            break;
                        case TraversalSpeed.Sprint:
                            maxSpeed = Locomotions.Move.MaxSprintSpeed;
                            break;
                    }
                }

                zAxisEllipseMultiplier = maxSpeed / Locomotions.Move.MaxSidewaysSpeed;
                if (zAxisEllipseMultiplier <= Fixed64.Zero)
                    return Fixed64.Zero;

                temp = new Vector3d(
                    desiredMovementDirection.x,
                    Fixed64.Zero,
                    desiredMovementDirection.z / zAxisEllipseMultiplier).Normal;
            }

            Fixed64 length = new Vector3d(temp.x, Fixed64.Zero, temp.z * zAxisEllipseMultiplier).Magnitude;
            return length * (IsInWater
                ? Locomotions.Swim.MaxSwimSidewaysSpeed
                : Locomotions.Move.MaxSidewaysSpeed);
        }

        public Fixed64 GetMaxAcceleration()
        {
            if (Locomotions.Swim.IsSwimming)
                return Locomotions.Swim.MaxSwimAcceleration;

            if (IsGrounded)
                return Locomotions.Move.MaxGroundAcceleration;

            if (Locomotions.Jump.IsJumping)
                return Locomotions.Move.MaxAirAcceleration * Locomotions.Jump.JumpControlMultiplier;

            if (Locomotions.Fall.IsFalling)
                return Locomotions.Move.MaxAirAcceleration * Locomotions.Fall.FallControlMultiplier;

            if (IsInAir)
                return Locomotions.Move.MaxAirAcceleration;

            return Fixed64.MAX_VALUE; // fallback, should never be hit
        }

        private void ApplyEnvironmentalForces()
        {
            if (IsGrounded)
            {
                // Actively cancel any existing downward momentum by simulating the normal force of the ground.
                if (_hostScout.LinearVelocity.y <= Fixed64.Zero)
                    _forceOutput.y = -_hostScout.LinearVelocity.y / TrailblazerManager.DeltaTime;
                else
                    _forceOutput.y = Fixed64.Zero;

                return;
            }

            if (IsInWater)
            {
                Fixed64 netBuoyancyForce = Gravity * Locomotions.Swim.BuoyancyFactor;


                // If buoyancy cancels gravity completely, scout should neither sink nor rise
                if (Locomotions.Swim.BuoyancyFactor == Fixed64.One)
                {
                    _forceOutput.y = Fixed64.Zero;
                    return;
                }

                // If buoyancy is higher than gravity, scout should float up
                if (Locomotions.Swim.BuoyancyFactor > Fixed64.One)
                {
                    _forceOutput.y += netBuoyancyForce;
                    _forceOutput.y += (Locomotions.Swim.BuoyantForce - Gravity);
                }

                // If buoyancy is less than gravity, scout should sink down
                if (Locomotions.Swim.BuoyancyFactor < Fixed64.One)
                {
                    _forceOutput.y -= netBuoyancyForce;
                    _forceOutput.y -= (Locomotions.Swim.BuoyantForce - Gravity);
                }

                return;
            }

            if (IsInAir)
            {
                // TODO: if we want to support moving while in air, does this need to skip?
                // or cancel it out via another force?
                _forceOutput.y -= Gravity;

                Fixed64 newVelocityY = _hostScout.LinearVelocity.y + (_forceOutput.y * TrailblazerManager.DeltaTime);
                // Ensure velocity does not exceed terminal fall speed
                if (newVelocityY < -TrailblazerManager.TerminalFallVelocity)
                    _forceOutput.y = (-TrailblazerManager.TerminalFallVelocity - _hostScout.LinearVelocity.y) / TrailblazerManager.DeltaTime;

                // When jumping up we don't apply gravity for some time when the user is holding the jump button.
                // This allows for more control over jump height by pressing the button longer.
                if (Locomotions.Jump.IsJumping && Locomotions.Jump.IsHoldingJump)
                {
                    // Calculate the duration that the extra jump force should have effect.
                    // If we're still less than that duration after the jumping time, apply the force.
                    int extraJumpLimit = (int)(Locomotions.Jump.FrameStartJump + Locomotions.Jump.ExtraJumpHeight / GetVerticalJumpSpeed());

                    // Negate the gravity we just applied, except we push in jumpDir rather than jump upwards.
                    if (TrailblazerManager.FrameCount < extraJumpLimit)
                        _forceOutput += Locomotions.Jump.FrameJumpDirection * Gravity;
                }
            }
        }

        private void ApplyJumpForce()
        {
            if (IsInAir
                || IsInWater && !Locomotions.Swim.CanBreachWater
                || Locomotions.Jump.IsCoolingDown
                || _hostScout.Events?.CanAffordJump?.Invoke() == false)
            {
                return;
            }

            Vector3d jumpForce;
            if (IsInWater)
            {
                Locomotions.Jump.FrameJumpDirection = Vector3d.Up;
                jumpForce = Locomotions.Jump.FrameJumpDirection * Locomotions.Swim.BuoyantForce;
                _hostScout.Events?.OnStartWaterBreach?.Invoke();
            }
            else
            {
                // Calculate the jumping direction
                Fixed64 slerpAmount = IsTooSteep()
                    ? Locomotions.Jump.SteepPerpendicularJumpAmount
                    : Locomotions.Jump.PerpendicularJumpAmount;

                // Store jump direction the first time we jump
                if (!Locomotions.Jump.IsJumping)
                    Locomotions.Jump.FrameJumpDirection = Vector3d.Slerp(Vector3d.Up, GroundNormal, slerpAmount);

                jumpForce = Locomotions.Jump.FrameJumpDirection * GetVerticalJumpSpeed();

                // Apply inertia from platform and store platform force for landing momentum
                if (Locomotions.Platform.IsPlatformInteriaApplied)
                {
                    // Apply platform velocity changes as an instantaneous velocity shift
                    Locomotions.Platform.FrameForce = Locomotions.Platform.ActiveVelocity / TrailblazerManager.DeltaTime;
                    jumpForce += Locomotions.Platform.FrameForce;
                }

                _hostScout.Events?.OnStartJump?.Invoke(Locomotions.Jump.AvoidGroundingTimer);
            }

            // If we aren't in air, trigger a new jump then...
            Locomotions.Jump.IsJumping = true;
            Locomotions.Jump.IsHoldingJump = true;
            Locomotions.Jump.FrameStartJump = TrailblazerManager.FrameCount;

            Locomotions.Jump.StartCooldown();

            // Remove any existing downward force
            _forceOutput.y = FixedMath.Max(Fixed64.Zero, _forceOutput.y);
            _forceOutput += jumpForce;
        }

        /// <summary>
        /// From the jump height and gravity we deduce the upwards speed for the character to reach at the apex.
        /// </summary>
        /// <returns></returns>
        private Fixed64 GetVerticalJumpSpeed() => FixedMath.Sqrt(2 * Locomotions.Jump.BaseJumpHeight * Gravity);

        private void ApplyScoutMovement()
        {
            // Corrects the Y axis if swimming or no gravity
            if (Locomotions.Swim.IsSwimming && _frameTraversalRequest.IsMoving)
                _forceOutput.y = _frameTraversalRequest.MovementDirection.y;

            // Apply the force
            if (_forceOutput != Vector3d.Zero)
            {
                if (Mode == OutputMode.Force)
                    _hostScout.Events?.OnAddLinearForce?.Invoke(_forceOutput);
                else if (Mode == OutputMode.Position)
                {
                    Vector3d velocityDelta = _forceOutput * TrailblazerManager.DeltaTime;
                    _hostScout.Events?.OnAddPositionDelta?.Invoke(velocityDelta * TrailblazerManager.DeltaTime);
                }
            }
        }

        #endregion

        #region Utility

        public void SetMotorLock(bool status) => IsControllerLocked = status;

        public bool IsTooSteep()
        {
            Fixed64 angle = Vector3d.Angle(Vector3d.Up, GroundNormal);
            return angle > Locomotions.Slide.SlopeLimit - Fixed64.Epsilon;
        }

        public bool IsOnSlope()
        {
            if (!IsGrounded) return false;
            Fixed64 angle = Vector3d.Angle(Vector3d.Up, GroundNormal);
            return angle > Fixed64.One && angle <= Locomotions.Slide.SlopeLimit + Fixed64.Epsilon;
        }

        #endregion
    }
}

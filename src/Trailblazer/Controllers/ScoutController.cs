using FixedMathSharp;
using System;
using System.Diagnostics;
using Trailblazer.Controllers.Locomotions;

namespace Trailblazer.Controllers
{
    /// <summary>
    /// Controls character movement using an acceleration-based approach in a deterministic, lockstep simulation.
    /// </summary>
    /// <remarks>
    /// This controller processes movement requests, applies forces such as gravity and platform adjustments, 
    /// and finalizes traversal states for consistent movement across frames.
    /// </remarks>
    [Serializable]
    public class ScoutController
    {
        #region Fields & Properties

        /// <summary>
        /// Enables debug output for state logging.
        /// </summary>
        public bool DebugMode;

        /// <summary>
        /// Manages locomotion states and behaviors.
        /// </summary>
        public LocomotionHandler Locomotions = new();

        [NonSerialized]
        private IScout _hostScout;

        [NonSerialized]
        private TraversalState _currentState;

        public TraversalState CurrentState => _currentState;

        /// <summary>
        /// Indicates whether the controller is locked for the current frame to prevent multiple force applications.
        /// </summary>
        public bool IsFrameLocked { get; private set; }

        #region Cache

        /// <summary>
        /// Stores the movement request for the current frame.
        /// </summary>
        [NonSerialized]
        private TraversalRequest _frameTraversalRequest;

        /// <summary>
        /// Accumulates forces applied during the traversal phase before they are committed.
        /// </summary>
        [NonSerialized]
        private Vector3d _forceOutput;

        #endregion

        #region State Status

        /// <summary>
        /// Indicates if the traversal medium has changed since the last frame.
        /// </summary>
        public bool StateChanged => _currentState.Medium != _currentState.PreviousState?.Medium
            && _currentState.Medium != TraversalMedium.Unknown
            && _currentState.PreviousState?.Medium != TraversalMedium.Unknown;

        /// <summary>
        /// Determines if the scout is currently on the ground.
        /// </summary>
        public bool IsGrounded => _currentState.Medium == TraversalMedium.Ground;

        /// <summary>
        /// Determines if the scout was on the ground in the previous frame.
        /// </summary>
        public bool WasGrounded => _currentState.PreviousState?.Medium == TraversalMedium.Ground;

        /// <summary>
        /// Determines if the scout is currently in the air.
        /// </summary>
        public bool IsInAir => _currentState.Medium == TraversalMedium.Air;

        /// <summary>
        /// Determines if the scout was in the air in the previous frame.
        /// </summary>
        public bool WasInAir => _currentState.PreviousState?.Medium == TraversalMedium.Air;

        /// <summary>
        /// Determines if the scout is currently in water.
        /// </summary>
        public bool IsInWater => _currentState.Medium == TraversalMedium.Water;

        /// <summary>
        /// Determines if the scout was in water in the previous frame.
        /// </summary>
        public bool WasInWater => _currentState.PreviousState?.Medium == TraversalMedium.Water;

        /// <summary>
        /// Checks if the scout is in a state where it is airborne but not actively jumping or falling.
        /// </summary>
        public bool InLimbo => IsInAir && !Locomotions.Jump.IsJumping && !Locomotions.Fall.IsFalling;

        /// <summary>
        /// Indicates whether the scout is currently standing on a platform.
        /// </summary>
        public bool IsOnPlatform => Locomotions.Platform.IsEnabled && Locomotions.Platform.ActivePlatform != null;

        /// <summary>
        /// Indicates whether platform inertia (initial velocity transfer) has been applied.
        /// </summary>
        public bool IsPlatformInteriaApplied => Locomotions.Platform.IsEnabled
            && (Locomotions.Platform.MovementTransfer == MotionTransfer.InitTransfer || Locomotions.Platform.MovementTransfer == MotionTransfer.PermaTransfer);

        /// <summary>
        /// Indicates whether the scout is locked to a platform and will move with it.
        /// </summary>
        public bool IsMovingWithPlatform => IsOnPlatform && (IsGrounded || Locomotions.Platform.MovementTransfer == MotionTransfer.PermaLocked);

        #endregion

        #endregion

        #region Construct

        /// <summary>
        /// Creates a new <see cref="ScoutController"/> instance and initializes it with the provided scout.
        /// </summary>
        /// <param name="scout">The scout entity that this controller will manage.</param>
        /// <param name="initialCondition">The initial traversal condition of the scout</param>
        /// <returns>A new instance of <see cref="ScoutController"/>.</returns>
        public static ScoutController CreateNew(IScout scout, TraversalCondition initialCondition) => new(scout, initialCondition);

        /// <summary>
        /// Initializes a new instance of the <see cref="ScoutController"/> class.
        /// </summary>
        /// <param name="scout">The scout entity that this controller will manage.</param>
        /// <param name="initialCondition">The initial traversal condition of the scout</param>
        public ScoutController(IScout scout, TraversalCondition initialCondition) => Initialize(scout, initialCondition);

        /// <summary>
        /// Prepares the controller by linking it to the given scout and setting initial state values.
        /// </summary>
        /// <param name="scout">The scout entity that this controller will manage.</param>
        /// <param name="initialCondition">The initial traversal condition of the scout</param>
        public void Initialize(IScout scout, TraversalCondition initialCondition)
        {
            _hostScout = scout;
            _currentState = new TraversalState(initialCondition);
            if (_currentState.SurfaceState.HasValue)
                HandlePlatformChange(); // set the initial platform
            Locomotions.Move.CurrentPosition = _hostScout.WorldPosition;
            Locomotions.Move.LastPosition = Locomotions.Move.CurrentPosition;
        }

        #endregion

        #region Phase 1 - Request Traversal

        /// <summary>
        /// Requests movement input for the current simulation frame.
        /// </summary>
        /// <param name="movementDirection">The direction of movement, represented as a unit vector.</param>
        /// <param name="traversalSpeed">The speed category of the movement (e.g., walk, jog, sprint).</param>
        /// <param name="isRequestingJump">Whether the scout is attempting to jump.</param>
        public void Traverse(Vector3d movementDirection, TraversalSpeed traversalSpeed, bool isRequestingJump = false)
        {
            Traverse(new TraversalRequest
            {
                MovementDirection = movementDirection,
                TraversalSpeed = traversalSpeed,
                IsRequestingJump = isRequestingJump
            });
        }

        /// <summary>
        /// Processes a movement request and applies necessary forces.
        /// </summary>
        /// <remarks>
        /// This method locks the controller for the current frame to prevent duplicate force accumulation.  
        /// Movement forces such as gravity, jump, and platform adjustments are applied.
        /// </remarks>
        /// <param name="traversalRequest">The movement request containing direction, speed, and jump state.</param>
        public void Traverse(TraversalRequest traversalRequest)
        {
            if (_hostScout == null) return;

            if (IsFrameLocked)
                return;

            IsFrameLocked = true;

            if (DebugMode)
                Debug.WriteLine($"AgentMotor State: " +
                    $"Grounded={IsGrounded}, " +
                    $"InAir={IsInAir}, " +
                    $"Velocity={Locomotions.Move.CurrentVelocity}");

            _frameTraversalRequest = traversalRequest;

            // Store the current velocity for manipulation
            _forceOutput = Locomotions.Move.CurrentVelocity;

            // Update platform velocity prior to applying jump force
            UpdatePlatformVelocity();

            // In limbo, prevent any further processing until control is given back
            if (InLimbo)
                Locomotions.IsInControl = false;

            if (Locomotions.Jump.IsCoolingDown)
                Locomotions.Jump.UpdateCooldown();

            ComputeMovementForces();

            if (!_frameTraversalRequest.IsRequestingJump) // Reset this before applying gravity
                Locomotions.Jump.IsHoldingJump = false;

            // Apply external forces such as gravity, water drag, and friction.
            ApplyEnvironmentalForces();

            ApplyJumpForce();

            // Save last position before platform movement is applied for velocity calculation.
            Locomotions.Move.LastPosition = _hostScout.WorldPosition;

            ApplyPlatformMovement();

            // Apply the computed force
            if (_forceOutput != Vector3d.Zero)
                _hostScout.Events?.OnAddLinearForce?.Invoke(_forceOutput * TrailblazerManager.DeltaTime);

            _frameTraversalRequest = default;
        }

        /// <summary>
        /// Updates the platform velocity based on movement from the last frame.
        /// </summary>
        private void UpdatePlatformVelocity()
        {
            if (!Locomotions.Platform.IsEnabled) return;

            if (Locomotions.Platform.ActivePlatform != null)
            {
                if (!Locomotions.Platform.IsNewPlatform)
                {
                    Vector3d currentPoint = Locomotions.Platform.ActiveTransform.TransformPoint(Locomotions.Platform.ScoutLocalPoint);
                    Vector3d previousPoint = Locomotions.Platform.LastTransform.TransformPoint(Locomotions.Platform.ScoutLocalPoint);

                    // Store platform velocity to use as a canceling force
                    Locomotions.Platform.PlatformVelocity = (currentPoint - previousPoint) / TrailblazerManager.DeltaTime;
                }

                Locomotions.Platform.LastTransform = Locomotions.Platform.ActiveTransform;
                Locomotions.Platform.IsNewPlatform = false;
            }
            else
                Locomotions.Platform.PlatformVelocity = Vector3d.Zero;
        }

        /// <summary>
        /// Computes the movement forces based on the traversal state and applies them to the scout.
        /// </summary>
        /// <remarks>
        /// This method determines whether the scout is in control, calculates velocity adjustments,
        /// and applies constraints such as slope resistance and airborne drag.
        /// </remarks>
        private void ComputeMovementForces()
        {
            // Check if scout is in control
            if (IsGrounded)
            {
                bool isSliding = false;
                if (Locomotions.Slide.IsEnabled)
                {
                    isSliding = IsTooSteep(_currentState.SlopeAngle);
                    Locomotions.Slide.IsSliding = isSliding;
                }
                Locomotions.IsInControl = !isSliding; // prevent control if sliding
            }
            else
                Locomotions.IsInControl = !InLimbo;

            if (InLimbo)
                return;

            // remove any downward current downward momentum if we aren't grounded or just landed
            if (!IsGrounded || IsGrounded && !WasGrounded)
                _forceOutput.y = Fixed64.Zero;

            Vector3d desiredVelocity = GetDesiredVelocity();

            if (desiredVelocity == _forceOutput)
                return;

            Fixed64 maxVelocityChange = GetMaxAcceleration() * TrailblazerManager.DeltaTime;
            Vector3d velocityChange = (desiredVelocity - _forceOutput).ClampMagnitude(maxVelocityChange);

            // if we're on the ground and don't have control we do apply it - it will correspond to friction.
            // if we're in the air or water and don't have control, don't apply any velocity change at all.
            if (!IsGrounded && !Locomotions.IsInControl)
                return;

            _forceOutput += velocityChange;

            // When going uphill, the IScout will automatically move up by the needed amount.
            // Not moving it upwards manually prevent risk of lifting off from the ground.
            // When going downhill, DO move down manually, as gravity is not enough on steep hills.
            if (IsGrounded)
                _forceOutput.y = FixedMath.Min(_forceOutput.y, Fixed64.Zero);
        }

        /// <summary>
        /// Determines the desired velocity based on input direction, movement constraints, and traversal state.
        /// </summary>
        /// <returns>The computed velocity vector that the scout should move toward.</returns>
        private Vector3d GetDesiredVelocity()
        {
            Vector3d result = Vector3d.Zero;
            if (Locomotions.Slide.IsSliding)
            {
                // The direction we're sliding in
                result = new Vector3d(_currentState.SurfaceNormal.x, Fixed64.Zero, _currentState.SurfaceNormal.z).Normal;
                // Find the input movement direction projected onto the sliding direction
                Vector3d projectedMoveDir = Vector3d.Project(_frameTraversalRequest.MovementDirection, result);
                // Add the sliding direction, the speed control, and the sideways control vectors
                result = result + projectedMoveDir
                    * Locomotions.Slide.SpeedControl + (_frameTraversalRequest.MovementDirection - projectedMoveDir)
                    * Locomotions.Slide.SidewaysControl;
                // Multiply with the sliding speed

                Fixed64 adjustedSlideSpeed = Locomotions.Slide.SlidingSpeed * (Fixed64.One - Locomotions.Move.SurfaceFriction);
                result *= adjustedSlideSpeed;
            }
            else if (Locomotions.IsInControl && _frameTraversalRequest.TraversalSpeed != TraversalSpeed.Stationary)
                result = GetHorizontalVelocity();

            // Ensure smoother stops in water instead of abrupt halts
            if (IsInWater)
            {
                // Calculates the maximum allowable vertical swimming speed
                if (_frameTraversalRequest.MovementDirection.y != Fixed64.Zero)
                    result.y = _frameTraversalRequest.MovementDirection.y * Locomotions.Swim.MaxSwimSpeed;

                // Apply drag resistance (reduces speed as it increases)
                if (result != Vector3d.Zero)
                    result *= FixedMath.Clamp01(Fixed64.One - Locomotions.Swim.WaterDragFactor);

                return result;
            }

            if (Locomotions.Platform.IsEnabled && Locomotions.Platform.MovementTransfer == MotionTransfer.PermaTransfer)
            {
                result += Locomotions.Platform.FramePlatformVelocity;
                result.y = Fixed64.Zero;
            }

            if (!IsGrounded || result == Vector3d.Zero)
                return result;

            // Ensure that the desired movement of the scout aligns with the surface they are on
            // i.e., ensures scout does not "digging into" the ground when moving over a bump
            Vector3d sideways = Vector3d.Cross(Vector3d.Up, result);
            Vector3d adjustedVelocity = Vector3d.Cross(sideways, _currentState.SurfaceNormal).Normal * result.Magnitude;

            // Ensure downward movement on downhill slopes & upward movement on uphill slopes
            if (_currentState.SlopeAngle != Fixed64.Zero && Fixed64.Sign(adjustedVelocity.y) != Fixed64.Sign(_currentState.SlopeAngle))
                adjustedVelocity.y *= -1;

            // Prevent excessive redirection on very steep terrain
            if (Vector3d.Angle(result, adjustedVelocity) < Locomotions.Slide.SlopeLimit)
                result = adjustedVelocity;

            return result;
        }

        /// <summary>
        /// Computes the horizontal velocity based on movement input and current facing direction.
        /// </summary>
        /// <returns>The target horizontal velocity.</returns>
        private Vector3d GetHorizontalVelocity()
        {
            Fixed3x3 transposedMatrix = _hostScout.VisualRotation.ToMatrix3x3();
            Vector3d desiredLocalDirection = Fixed3x3.InverseTransformDirection(transposedMatrix, _frameTraversalRequest.MovementDirection);
            Fixed64 speed = MaxHoritzontalSpeedInDirection(desiredLocalDirection);

            speed *= Locomotions.Move.MoveSpeedMultiplier;

            // Modify max speed on slopes based on slope speed multiplier curve
            if (Locomotions.Move.ModifySpeedOnSlope && IsOnSlope(_currentState.SlopeAngle))
                speed *= Locomotions.Move.SlopeSpeedMultiplier.Evaluate(_currentState.SlopeAngle);

            return Fixed3x3.TransformDirection(transposedMatrix, desiredLocalDirection * speed);
        }

        /// <summary>
        /// Calculates the maximum allowable speed in a given movement direction.
        /// </summary>
        /// <param name="desiredMovementDirection">The movement direction to evaluate.</param>
        /// <returns>The maximum speed possible in the specified direction.</returns>
        public Fixed64 MaxHoritzontalSpeedInDirection(Vector3d desiredMovementDirection)
        {
            if (desiredMovementDirection == Vector3d.Zero)
                return Fixed64.Zero;

            Vector3d temp;
            Fixed64 zAxisEllipseMultiplier;
            if (IsInWater)
            {
                zAxisEllipseMultiplier = Locomotions.Swim.MaxSwimSpeed / Locomotions.Swim.MaxSwimSidewaysSpeed;
                if (zAxisEllipseMultiplier <= Fixed64.Zero)
                    return Fixed64.Zero;

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
                        case TraversalSpeed.Slow:
                            maxSpeed = Locomotions.Move.MaxSlowSpeed;
                            break;
                        case TraversalSpeed.Moderate:
                            maxSpeed = Locomotions.Move.MaxModerateSpeed;
                            break;
                        case TraversalSpeed.Fast:
                            maxSpeed = Locomotions.Move.MaxFastSpeed;
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

        /// <summary>
        /// Retrieves the maximum acceleration value based on the scout’s current traversal state.
        /// </summary>
        /// <returns>The acceleration limit depending on whether the scout is grounded, airborne, or swimming.</returns>
        public Fixed64 GetMaxAcceleration()
        {
            if (IsInWater)
                return Locomotions.Swim.MaxSwimAcceleration;

            if (IsGrounded)
                return Locomotions.Move.MaxGroundAcceleration;

            if (Locomotions.Jump.IsJumping)
                return Locomotions.Move.MaxAirAcceleration;// * Locomotions.Jump.JumpControlMultiplier;

            if (Locomotions.Fall.IsFalling)
                return Locomotions.Move.MaxAirAcceleration;// * Locomotions.Fall.FallControlMultiplier;

            if (IsInAir)
                return Locomotions.Move.MaxAirAcceleration;

            return Fixed64.MAX_VALUE; // fallback, should never be hit
        }

        /// <summary>
        /// Applies environmental forces such as gravity, water buoyancy, and downward force when grounded.
        /// </summary>
        private void ApplyEnvironmentalForces()
        {
            Fixed64 gravityStep = Locomotions.Move.GravityForce * TrailblazerManager.DeltaTime;

            if (IsGrounded)
            {
                _forceOutput.y = FixedMath.Min(Fixed64.Zero, _forceOutput.y) - gravityStep;
                return;
            }

            if (IsInWater)
            {
                // If buoyancy cancels gravity completely, scout should neither sink nor rise
                if (Locomotions.Swim.BuoyancyFactor == Fixed64.One)
                    return;

                // netBuoyancyForce
                _forceOutput.y += gravityStep * Locomotions.Swim.BuoyancyFactor;

                // If buoyancy is higher than gravity, scout should float up
                if (Locomotions.Swim.BuoyancyFactor > Fixed64.One)
                    _forceOutput.y += (Locomotions.Swim.BuoyantForce - gravityStep);

                // If buoyancy is less than gravity, scout should sink down
                if (Locomotions.Swim.BuoyancyFactor < Fixed64.One)
                    _forceOutput.y -= (Locomotions.Swim.BuoyantForce - gravityStep);

                return;
            }

            if (!IsInAir) return;

            // TODO: if we want to support moving while in air, does this need to skip?
            // or cancel it out via another force?
            _forceOutput.y = Locomotions.Move.CurrentVelocity.y - gravityStep;

            // Ensure velocity does not exceed terminal fall speed
            if (Locomotions.Move.CurrentVelocity.y + _forceOutput.y < -Locomotions.Move.TerminalVelocity)
                _forceOutput.y = -Locomotions.Move.TerminalVelocity - Locomotions.Move.CurrentVelocity.y;

            // When jumping up we don't apply gravity for some time when the user is holding the jump button.
            // This allows for more control over jump height by pressing the button longer.
            if (Locomotions.Jump.IsJumping && Locomotions.Jump.IsHoldingJump)
            {
                // Calculate the duration that the extra jump force should have effect.
                // If we're still less than that duration after the jumping time, apply the force.
                Fixed64 extraJumpLimit = (Locomotions.Jump.JumpStartTime + Locomotions.Jump.ExtraJumpHeight) / GetVerticalJumpSpeed();

                // Negate the gravity we just applied, except we push in jumpDir rather than jump upwards.
                if (TrailblazerManager.TotalTime <= extraJumpLimit)
                    _forceOutput += Locomotions.Jump.FrameJumpDirection * gravityStep;
            }
        }

        /// <summary>
        /// Applies an instantaneous jump force to the scout, considering platform inertia and jump physics.
        /// </summary>
        /// <remarks>
        /// This method validates jump conditions, determines the jump direction, and calculates the jump force.
        /// If the scout is on a platform, its velocity is adjusted accordingly.
        /// </remarks>
        private void ApplyJumpForce()
        {
            if (!(Locomotions.Jump.IsEnabled && Locomotions.IsInControl && _frameTraversalRequest.IsRequestingJump)) return;

            if (IsInAir || IsInWater && !Locomotions.Swim.CanBreachWater || Locomotions.Jump.IsCoolingDown) return;

            if (_hostScout.Events?.CanAffordJump?.Invoke() == false) return;

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
                Fixed64 slerpAmount = IsTooSteep(_currentState.SlopeAngle)
                    ? Locomotions.Jump.SteepPerpendicularJumpAmount
                    : Locomotions.Jump.PerpendicularJumpAmount;

                // Store jump direction the first time we jump
                if (!Locomotions.Jump.IsJumping)
                    Locomotions.Jump.FrameJumpDirection = Vector3d.Slerp(Vector3d.Up, _currentState.SurfaceNormal, slerpAmount);

                jumpForce = Locomotions.Jump.FrameJumpDirection * GetVerticalJumpSpeed();

                _hostScout.Events?.OnStartJump?.Invoke(Locomotions.Jump.AvoidGroundingTimer);
            }

            // If we aren't in air, trigger a new jump then...
            Locomotions.Jump.IsJumping = true;
            Locomotions.Jump.IsHoldingJump = true;
            Locomotions.Jump.JumpStartTime = TrailblazerManager.TotalTime;

            Locomotions.Jump.StartCooldown();

            // Remove any existing downward force
            _forceOutput.y = FixedMath.Max(Fixed64.Zero, _forceOutput.y);
            _forceOutput += jumpForce;
        }

        /// <summary>
        /// Applies movement adjustments due to platform motion, ensuring the scout inherits platform movement correctly.
        /// </summary>
        /// <remarks>
        /// This method updates the scout’s position and rotation based on the platform’s transform,
        /// preventing unwanted movement shifts when transitioning between platforms.
        /// </remarks>
        private void ApplyPlatformMovement()
        {
            if (!IsMovingWithPlatform) return;

            //  Do NOT apply movement if we just jumped — velocity was already injected
            if (Locomotions.Jump.IsJumping) return;

            // Apply platform rotation first THEN apply platform movement
            FixedQuaternion targetRotation = Locomotions.Platform.ActiveTransform.Rotation * Locomotions.Platform.ScoutLocalRotation;
            if (targetRotation != FixedQuaternion.Identity)
            {
                FixedQuaternion rotationDiff = targetRotation * Locomotions.Platform.ScoutGlobalRotation.Inverse();
                _hostScout.Events?.OnAddPlatformRotationDelta?.Invoke(rotationDiff);
            }

            Vector3d newGlobalPoint = Locomotions.Platform.ActiveTransform.TransformPoint(Locomotions.Platform.ScoutLocalPoint);
            Vector3d moveDistance = newGlobalPoint - Locomotions.Platform.ScoutGlobalPoint;
            if (moveDistance != Vector3d.Zero)
            {
                _hostScout.Events?.OnAddPlatformPositionDelta?.Invoke(moveDistance);
                Locomotions.Move.LastPosition += moveDistance; // shift last position so it doesn't alter scout's velocity
            }
        }

        #endregion

        #region Phase 2 - Finalize 

        /// <summary>
        /// Finalizes traversal state updates and prepares the scout for the next simulation frame.
        /// </summary>
        /// <remarks>
        /// This method updates the scout's velocity, applies necessary adjustments based on traversal state changes,
        /// and processes platform movement or environmental effects as needed.
        /// </remarks>
        public void FinishFrameTraversal(TraversalCondition condition)
        {
            if (!IsFrameLocked) return;

            Locomotions.Move.CurrentPosition = _hostScout.WorldPosition;

            Locomotions.Move.LastVelocity = Locomotions.Move.CurrentVelocity;
            Locomotions.Move.CurrentVelocity = (Locomotions.Move.CurrentPosition - Locomotions.Move.LastPosition) / TrailblazerManager.DeltaTime;

            _currentState.Update(condition, _currentState.ToTraversalCondition());

            // Make sure we aren't hitting the ceiling
            if (Locomotions.Move.CurrentVelocity.y > Fixed64.Zero && _currentState.CeilingLevel != Fixed64.MAX_VALUE)
            {
                if (_hostScout.WorldPosition.y > _currentState.CeilingLevel)
                {
                    Locomotions.Move.CurrentVelocity = new Vector3d(Locomotions.Move.CurrentVelocity.x, Fixed64.Zero, Locomotions.Move.CurrentVelocity.z);
                    Locomotions.Jump.IsJumping = false;
                    Locomotions.Jump.IsHoldingJump = false;
                }
            }

            HandlePlatformChange();

            HandlePlatformTransitions();

            HandleMovementTransitions();

            HandleSwimState();

            HandleFallState();

            HandlePlatformMovement();

            IsFrameLocked = false;
        }

        private void HandlePlatformChange()
        {
            // If we hit a new platform, reset platform state
            if (!Locomotions.Platform.IsEnabled)
                return;

            // Clear it to avoid double-applying next frame
            Locomotions.Platform.FramePlatformVelocity = Vector3d.Zero;
            Locomotions.Platform.MovementTransfer = _currentState.SurfaceState?.MotionTransferState ?? MotionTransfer.None;

            if (!DidPlatformChange(_currentState.SurfaceState))
                return;

            Fixed4x4 newPlatformMatrix = _currentState.SurfaceState?.SurfaceMatrix ?? Fixed4x4.Identity;

            Locomotions.Platform.LastTransform = Locomotions.Platform.ActivePlatform == null
                ? newPlatformMatrix
                : Locomotions.Platform.ActiveTransform;
            Locomotions.Platform.ActiveTransform = newPlatformMatrix;
            Locomotions.Platform.ActivePlatform = _currentState.SurfaceState?.SurfaceObject ?? null;

            Locomotions.Platform.IsNewPlatform = true;
        }

        /// <summary>
        /// Determines if the scout has transitioned onto a different platform.
        /// </summary>
        /// <param name="surfaceCondition">The current ground state of the scout.</param>
        /// <returns>True if the scout is on a new platform; otherwise, false.</returns>
        private bool DidPlatformChange(SurfaceCondition? surfaceCondition)
        {
            if (Locomotions.Platform.ActivePlatform == surfaceCondition?.SurfaceObject)
                return false;

            if (Locomotions.Platform.ActivePlatform == null || Locomotions.Platform.ActivePlatform != surfaceCondition?.SurfaceObject)
                return true;

            return false;
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
            if (!Locomotions.Platform.IsEnabled || IsInWater)
                return;

            bool isReleasing = false;
            if (Locomotions.Platform.IsHoldingPlatform)
                isReleasing = Locomotions.Platform.CanReleaseHoldOnPlatform();

            if (isReleasing)
            {
                Locomotions.Move.CurrentVelocity -= Locomotions.Platform.PlatformVelocity;
                return;
            }

            if (!IsPlatformInteriaApplied) return;

            if (WasGrounded && IsInAir)
            {
                // Scout just left the ground, so it inherits platform inertia into its new velocity.
                Locomotions.Platform.FramePlatformVelocity = Locomotions.Platform.PlatformVelocity;
                Locomotions.Move.CurrentVelocity += Locomotions.Platform.PlatformVelocity;
                return;
            }

            if (WasInAir && IsGrounded)
            {
                if (Locomotions.Platform.IsNewPlatform)
                    // If scout landed on a new platform, we have to wait for two frames
                    // before we know the new velocity of the platform under the scout
                    Locomotions.Platform.SetHoldPlatform(Locomotions.Platform.ActivePlatform);
                else
                    // If the platform isn’t new, we assume the scout landed back on the same platform
                    // and subtract platform velocity to prevent doubling the effect.
                    Locomotions.Move.CurrentVelocity -= Locomotions.Platform.PlatformVelocity;
            }
        }

        /// <summary>
        /// Handles movement state transitions, including landing after a fall, jumping, or entering/exiting water.
        /// </summary>
        /// <remarks>
        /// This method ensures proper event notifications for transitions such as falling, landing, and jump termination.
        /// </remarks>
        private void HandleMovementTransitions()
        {
            // Trasitioning to either ground or water
            if (WasInAir && !IsInAir)
            {
                if (Locomotions.Jump.IsJumping)
                {
                    // Reset cooldown on landing
                    Locomotions.Jump.IsJumping = false;

                    if (IsInWater)
                        _hostScout.Events?.OnStopWaterBreach?.Invoke();
                    else
                        _hostScout.Events?.OnStopJump?.Invoke();

                    return;
                }

                if (!IsInWater)
                    _hostScout.Events?.OnLandedFall?.Invoke();
            }

            // Transitioning out of water
            if (Locomotions.Swim.IsSwimming && !IsInWater && WasInWater)
                Locomotions.Swim.ClearState();
        }

        /// <summary>
        /// Manages the scout's state when entering, exiting, or moving within water.
        /// </summary>
        /// <remarks>
        /// This method updates swim-related properties, tracks dive time, and triggers drowning events if necessary.
        /// </remarks>
        private void HandleSwimState()
        {
            if (!IsInWater) return;

            // Clear the transient state when entiring water for the first time
            if (!WasInWater)
                Locomotions.ClearStateAll();

            if (Locomotions.Swim.IsEnabled)
            {
                Locomotions.Swim.IsSwimming = true;
                Locomotions.Swim.IsDiving = _hostScout.WorldPosition.y < _currentState.SurfaceLevel;

                Locomotions.Swim.UpdateDiveTime();

                if (IsInWater && Locomotions.Swim.IsDrowning)
                    _hostScout.Events?.OnDrowning?.Invoke(Locomotions.Swim.UnderwaterTimer);
            }
        }

        /// <summary>
        /// Processes the scout’s fall state, tracking fall height and triggering landing events when appropriate.
        /// </summary>
        /// <remarks>
        /// This method determines when a scout starts falling, updates fall height, and detects when a safe landing occurs.
        /// </remarks>
        private void HandleFallState()
        {
            if (!Locomotions.Fall.IsEnabled || Locomotions.Swim.IsSwimming) return;

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

            // Check if the scout is in freefall (not simply moving downhill)
            bool isInFreefall = (IsInAir || Locomotions.Slide.IsSliding) && Locomotions.Move.CurrentVelocity.y < Fixed64.Zero;

            // Ensure we don't trigger falling when moving naturally down a slope
            if (isInFreefall && (_currentState.SlopeAngle >= -Locomotions.Slide.SlopeLimit))
            {
                // scout started falling
                Locomotions.Fall.IsFalling = true;
                Locomotions.Fall.FallStart = _hostScout.WorldPosition.y;
                _hostScout.Events?.OnStartFall?.Invoke();
            }
        }

        /// <summary>
        /// Updates platform movement by synchronizing the scout's position and rotation with the platform it is standing on.
        /// </summary>
        /// <remarks>
        /// This method prevents unwanted movement shifts when transitioning between platforms, ensuring smooth locomotion.
        /// </remarks>
        private void HandlePlatformMovement()
        {
            if (!IsMovingWithPlatform) return;

            Vector3d footPosition = _hostScout.GetFootPosition();
            footPosition.y += Locomotions.Platform.HeightAdjust;
            Locomotions.Platform.ScoutGlobalPoint = footPosition;
            Locomotions.Platform.ScoutLocalPoint = Fixed4x4.InverseTransformPoint(
                Locomotions.Platform.ActiveTransform,
                Locomotions.Platform.ScoutGlobalPoint);

            Locomotions.Platform.ScoutGlobalRotation = _hostScout.VisualRotation;
            Locomotions.Platform.ScoutLocalRotation = Locomotions.Platform.ActiveTransform.Rotation.Inverse() * Locomotions.Platform.ScoutGlobalRotation;
        }

        #endregion

        #region Utility

        /// <summary>
        /// Computes the vertical jump speed required to reach the desired jump height (apex).
        /// </summary>
        /// <returns>The initial vertical velocity needed for the jump.</returns>
        private Fixed64 GetVerticalJumpSpeed() => FixedMath.Sqrt(2 * Locomotions.Jump.BaseJumpHeight * Locomotions.Move.GravityForce);

        /// <summary>
        /// Determines whether the current surface is too steep for normal movement.
        /// </summary>
        /// <returns>True if the slope exceeds the allowable incline; otherwise, false.</returns>
        public bool IsTooSteep(Fixed64 angle)
        {
            if (!IsGrounded) return false;

            Fixed64 absAngle = FixedMath.Abs(angle); // Handle both positive (uphill) and negative (downhill) slopes
            return absAngle > Locomotions.Slide.SlopeLimit - Fixed64.Epsilon;
        }

        /// <summary>
        /// Checks if the scout is on a sloped surface that is not considered too steep.
        /// </summary>
        /// <returns>True if the scout is on a valid slope; otherwise, false.</returns>
        public bool IsOnSlope(Fixed64 angle)
        {
            if (!IsGrounded) return false;

            Fixed64 absAngle = FixedMath.Abs(angle); // Account for downhill slopes too
            return absAngle > Fixed64.One && absAngle <= Locomotions.Slide.SlopeLimit + Fixed64.Epsilon;
        }

        /// <summary>
        /// Manually sets the scout’s velocity, overriding the computed velocity for the next frame.
        /// </summary>
        /// <param name="velocity">The new velocity to assign to the scout.</param>
        public void SetVelocity(Vector3d velocity)
        {
            Locomotions.Move.CurrentVelocity = velocity;
        }

        public void UpdateTraversal(TraversalCondition newCondition)
        {
            _currentState.Update(newCondition, _currentState.ToTraversalCondition());
        }

        #endregion
    }
}

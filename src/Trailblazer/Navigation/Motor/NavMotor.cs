using FixedMathSharp;
using SwiftCollections;
using System;
using System.Diagnostics;

namespace Trailblazer.Navigation.Motor
{
    /// <summary>
    /// Controls character movement using an acceleration-based approach in a deterministic, lockstep simulation.
    /// </summary>
    /// <remarks>
    /// This controller processes movement requests, applies forces such as gravity and platform adjustments, 
    /// and finalizes traversal states for consistent movement across frames.
    /// </remarks>
    [Serializable]
    public class NavMotor
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
        public NavMotorEvents Events = new();

        /// <summary>
        /// This stores the current <see cref="Navigator.FrameCondition"/> for the frame.  
        /// </summary>
        /// <remarks>
        /// This is only set on <see cref="OnInitialize"/>,
        /// and updated at the end of the frame in <see cref="FinalizeTraversal"/>
        /// </remarks>
        public TransitState CurrentState { get; private set; }

        /// <summary>
        /// Indicates whether the controller is locked for the current frame to prevent multiple force applications.
        /// </summary>
        public bool IsFrameLocked { get; private set; }

        public bool IsInitialized { get; private set; }

        #region Cache

        /// <summary>
        /// Stores the movement request for the current frame.
        /// </summary>
        private TrekRequest _frameRequest;

        /// <summary>
        /// Stores the slope angle for the current frame based on <see cref="TrekRequest.Direction"/>.
        /// </summary>
        public Fixed64 FrameSlopeAngle { get; private set; }

        /// <summary>
        /// Accumulates forces applied during the traversal phase before they are committed.
        /// </summary>
        private Vector3d _forceOutput;

        #endregion

        #region State Status

        /// <summary>
        /// Indicates if the traversal medium has changed since the last frame.
        /// </summary>
        public bool StateChanged => CurrentState.Medium != CurrentState.PreviousState?.Medium
            && CurrentState.Medium != TraversalMedium.Unknown
            && CurrentState.PreviousState?.Medium != TraversalMedium.Unknown;

        /// <summary>
        /// Determines if the navigator is currently on the ground.
        /// </summary>
        public bool IsGrounded => CurrentState.Medium == TraversalMedium.Ground;

        /// <summary>
        /// Determines if the navigator was on the ground in the previous frame.
        /// </summary>
        public bool WasGrounded => CurrentState.PreviousState?.Medium == TraversalMedium.Ground;

        /// <summary>
        /// Determines if the navigator is currently in the air.
        /// </summary>
        public bool IsInAir => CurrentState.Medium == TraversalMedium.Air;

        /// <summary>
        /// Determines if the navigator was in the air in the previous frame.
        /// </summary>
        public bool WasInAir => CurrentState.PreviousState?.Medium == TraversalMedium.Air;

        /// <summary>
        /// Determines if the navigator is currently in water.
        /// </summary>
        public bool IsInWater => CurrentState.Medium == TraversalMedium.Water;

        /// <summary>
        /// Determines if the navigator was in water in the previous frame.
        /// </summary>
        public bool WasInWater => CurrentState.PreviousState?.Medium == TraversalMedium.Water;

        /// <summary>
        /// Checks if the navigator is in a state where it is airborne but not actively jumping or falling.
        /// </summary>
        public bool InLimbo => !IsGrounded && !IsInAir && !IsInWater
            || IsInAir && !Locomotions.Jump.IsJumping && !Locomotions.Fall.IsFalling;

        /// <summary>
        /// Indicates whether the navigator is currently standing on a platform.
        /// </summary>
        public bool IsOnPlatform => Locomotions.Platform.IsEnabled && Locomotions.Platform.ActivePlatform != null;

        /// <summary>
        /// Indicates whether platform inertia (initial velocity transfer) has been applied.
        /// </summary>
        public bool IsPlatformInteriaApplied => Locomotions.Platform.IsEnabled
            && (Locomotions.Platform.MovementTransfer == MotionTransfer.InitTransfer || Locomotions.Platform.MovementTransfer == MotionTransfer.PermaTransfer);

        /// <summary>
        /// Indicates whether the navigator is locked to a platform and will move with it.
        /// </summary>
        public bool IsMovingWithPlatform => IsOnPlatform && (IsGrounded || Locomotions.Platform.MovementTransfer == MotionTransfer.PermaLocked);

        #endregion

        #endregion

        #region Construct

        /// <summary>
        /// Creates a new <see cref="NavMotor"/> instance and initializes it with the provided navigator.
        /// </summary>
        /// <param name="startingPosition">The position of the navigator entity that this controller will manage.</param>
        /// <param name="initialCondition">The initial traversal condition of the navigator</param>
        /// <returns>A new instance of <see cref="NavMotor"/>.</returns>
        public static NavMotor CreateNew(Vector3d startingPosition, TrekCondition initialCondition) =>
            new(startingPosition, initialCondition);

        /// <summary>
        /// Initializes a new, empty instance of the <see cref="NavMotor"/> class.
        /// </summary>
        public NavMotor() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="NavMotor"/> class.
        /// </summary>
        /// <param name="startingPosition">The position of the navigator entity that this controller will manage.</param>
        /// <param name="initialCondition">The initial traversal condition of the navigator</param>
        public NavMotor(Vector3d startingPosition, TrekCondition initialCondition) =>
            OnInitialize(startingPosition, initialCondition);

        /// <summary>
        /// Prepares the controller by linking it to the given navigator and setting initial state values.
        /// </summary>
        /// <param name="startingPosition">The position of the navigator entity that this controller will manage.</param>
        /// <param name="initialCondition">The initial traversal condition of the navigator</param>
        public void OnInitialize(Vector3d startingPosition, TrekCondition initialCondition)
        {
            CurrentState = new TransitState(initialCondition);
            if (CurrentState.GroundState.HasValue)
                HandlePlatformChange(); // set the initial platform

            IsInitialized = true;
        }

        #endregion

        #region Phase 1 - Request Traversal

        /// <summary>
        /// Processes a movement request and applies necessary forces.
        /// </summary>
        /// <remarks>
        /// This method locks the controller for the current frame to prevent duplicate force accumulation.  
        /// Movement forces such as gravity, jump, and platform adjustments are applied.
        /// </remarks>
        /// <param name="navigator">The navigator this controller manages</param>
        public void Traverse(IMotor navigator)
        {
            if (!IsInitialized) return;

            if (IsFrameLocked)
                return;

            IsFrameLocked = true;

            if (DebugMode)
                Debug.WriteLine($"AgentMotor State: " +
                    $"Grounded={IsGrounded}, " +
                    $"InAir={IsInAir}, " +
                    $"Velocity={Locomotions.Move.FrameVelocity}");

            _frameRequest = navigator.FrameRequest;
            FrameSlopeAngle = CurrentState.GetSignedSlopeAngle(_frameRequest.Direction);

            // Store the current velocity for manipulation
            _forceOutput = Locomotions.Move.FrameVelocity;

            // Update platform velocity prior to applying jump force
            UpdatePlatformVelocity();

            // In limbo, prevent any further processing until control is given back
            if (InLimbo)
                Locomotions.IsInControl = false;

            if (Locomotions.Jump.IsCoolingDown)
                Locomotions.Jump.UpdateCooldown();

            ComputeMovementForces();

            // Reset this before applying gravity
            if (!_frameRequest.IsRequestingJump || !Locomotions.Jump.CanJump)
                Locomotions.Jump.IsHoldingJump = false;

            // Apply external forces such as gravity, water drag, and friction.
            ApplyEnvironmentalForces();

            ApplyJumpForce();

            ApplyPlatformMovement(navigator);

            // Apply the computed force
            if (_forceOutput != Vector3d.Zero)
            {
                Vector3d velDelta = _forceOutput * TrailblazerManager.DeltaTime;
                Events.OnAddVelocityDelta?.Invoke(velDelta);
                navigator.AddVelocityDelta(velDelta);
            }

            _frameRequest = default;
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
                    Locomotions.Platform.PlatformVelocity = (currentPoint - previousPoint) * TrailblazerManager.InvDeltaTime;
                }

                Locomotions.Platform.LastTransform = Locomotions.Platform.ActiveTransform;
                Locomotions.Platform.IsNewPlatform = false;
            }
            else
                Locomotions.Platform.PlatformVelocity = Vector3d.Zero;
        }

        /// <summary>
        /// Computes the movement forces based on the traversal state and applies them to the navigator.
        /// </summary>
        /// <remarks>
        /// This method determines whether the navigator is in control, calculates velocity adjustments,
        /// and applies constraints such as slope resistance and airborne drag.
        /// </remarks>
        private void ComputeMovementForces()
        {
            // Check if navigator is in control
            if (IsGrounded)
            {
                bool isSliding = false;
                if (Locomotions.Slide.IsEnabled)
                {
                    isSliding = IsTooSteep(FrameSlopeAngle);
                    Locomotions.Slide.IsSliding = isSliding;
                }
                Locomotions.IsInControl = !isSliding; // prevent control if sliding
            }
            else
                Locomotions.IsInControl = !InLimbo;

            if (InLimbo)
                return;

            // remove any downward current downward momentum if we aren't grounded or just landed
            if (!IsGrounded || IsGrounded && WasInAir)
                _forceOutput.y = Fixed64.Zero;

            Vector3d desiredVelocity = GetDesiredVelocity();

            // Apply Friction (resistance to motion)
            if (IsGrounded && _frameRequest.Direction == Vector3d.Zero)
            {
                Fixed64 friction = CurrentState.GroundState?.SurfaceFriction ?? Fixed64.Zero;
                if (friction > Fixed64.Zero && _forceOutput != Vector3d.Zero)
                {
                    // Decay force over time based on surface friction
                    _forceOutput *= Fixed64.One - friction;

                    // Apply no additional acceleration — skip the rest of the method
                    return;
                }
            }

            // Skip force update if already at desired velocity
            if (desiredVelocity == _forceOutput)
                return;

            Fixed64 maxVelocityChange = GetMaxAcceleration() * TrailblazerManager.DeltaTime;
            Vector3d velocityChange = (desiredVelocity - _forceOutput).ClampMagnitude(maxVelocityChange);

            // Don't apply velocity changes in air unless controlled
            if (!IsGrounded && !Locomotions.IsInControl)
                return;

            _forceOutput += velocityChange;

            // Uphill / Downhill velocity Y clamping
            if (IsGrounded)
                _forceOutput.y = FixedMath.Min(_forceOutput.y, Fixed64.Zero);
        }

        /// <summary>
        /// Determines the desired velocity based on input direction, movement constraints, and traversal state.
        /// </summary>
        /// <returns>The computed velocity vector that the navigator should move toward.</returns>
        private Vector3d GetDesiredVelocity()
        {
            Vector3d result = Vector3d.Zero;
            if (Locomotions.Slide.IsSliding)
            {
                // The direction we're sliding in
                result = new Vector3d(CurrentState.SurfaceNormal.x, Fixed64.Zero, CurrentState.SurfaceNormal.z).Normal;
                // Find the input movement direction projected onto the sliding direction
                Vector3d projectedMoveDir = Vector3d.Project(_frameRequest.Direction, result);

                // Add the sliding direction, the speed control, and the sideways control vectors
                Vector3d speedContribution = projectedMoveDir * Locomotions.Slide.SpeedControl;
                Vector3d sidewaysContribution = (_frameRequest.Direction - projectedMoveDir) * Locomotions.Slide.SidewaysControl;

                // Multiply with the sliding speed
                result += (speedContribution + sidewaysContribution) * Locomotions.Slide.SlidingSpeed;
            }
            else if (Locomotions.IsInControl && _frameRequest.Rate != TrekRate.Stationary)
                result = GetHorizontalVelocity();

            // Ensure smoother stops in water instead of abrupt halts
            if (IsInWater)
            {
                // Calculates the maximum allowable vertical swimming speed
                if (_frameRequest.Direction.y != Fixed64.Zero)
                    result.y = _frameRequest.Direction.y * Locomotions.Swim.MaxSwimSpeed;

                // Apply drag resistance (reduces speed as it increases)
                if (result != Vector3d.Zero)
                    result *= FixedMath.Clamp01(Fixed64.One - Locomotions.Swim.WaterDragFactor);

                return result;
            }

            if (Locomotions.Platform.IsEnabled
                && Locomotions.Platform.MovementTransfer == MotionTransfer.PermaTransfer)
            {
                result += Locomotions.Platform.FramePlatformVelocity;
                result.y = Fixed64.Zero;
            }

            if (!IsGrounded || result == Vector3d.Zero)
                return result;

            // Apply friction (resistance to control)
            result *= Fixed64.One - CurrentState.GroundState?.SurfaceFriction ?? Fixed64.Zero;

            // Ensure that the desired movement of the navigator aligns with the surface they are on
            // i.e., ensures navigator does not "digging into" the ground when moving over a bump
            Vector3d sideways = Vector3d.Cross(Vector3d.Up, result);
            Vector3d adjustedVelocity = Vector3d.Cross(sideways, CurrentState.SurfaceNormal).Normal * result.Magnitude;

            // Ensure downward movement on downhill slopes & upward movement on uphill slopes
            if (Fixed64.Sign(adjustedVelocity.y) != Fixed64.Sign(FrameSlopeAngle))
                adjustedVelocity.y *= -1;

            return result;
        }

        /// <summary>
        /// Computes the horizontal velocity based on movement input and current facing direction.
        /// </summary>
        /// <returns>The target horizontal velocity.</returns>
        private Vector3d GetHorizontalVelocity()
        {
            Fixed3x3 transposedMatrix = _frameRequest.Rotation.ToMatrix3x3();
            Vector3d desiredLocalDirection = Fixed3x3.InverseTransformDirection(transposedMatrix, _frameRequest.Direction);
            Fixed64 speed = MaxHoritzontalSpeedInDirection(desiredLocalDirection, _frameRequest.Rate);

            speed *= Locomotions.Move.MoveSpeedMultiplier;

            // Modify max speed on slopes based on slope speed multiplier curve
            if (Locomotions.Move.ModifySpeedOnSlope && IsOnSlope(FrameSlopeAngle))
                speed *= Locomotions.Move.SlopeSpeedMultiplier.Evaluate(FrameSlopeAngle);

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
                    switch (rate)
                    {
                        case TrekRate.Slow:
                            maxSpeed = Locomotions.Move.MaxSlowSpeed;
                            break;
                        case TrekRate.Moderate:
                            maxSpeed = Locomotions.Move.MaxModerateSpeed;
                            break;
                        case TrekRate.Fast:
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
            Fixed64 baseSpeed = length * (IsInWater
                ? Locomotions.Swim.MaxSwimSidewaysSpeed
                : Locomotions.Move.MaxSidewaysSpeed);

            if (IsGrounded)
                return baseSpeed;

            // Apply reduced control when jumping or falling
            Fixed64 controlMultiplier = Fixed64.One;

            if (Locomotions.Jump.IsJumping && !IsGrounded)
                controlMultiplier = Locomotions.Jump.JumpControlMultiplier;
            else if (Locomotions.Fall.IsFalling && !IsGrounded)
                controlMultiplier = Locomotions.Fall.FallControlMultiplier;

            return baseSpeed * controlMultiplier;
        }

        /// <summary>
        /// Retrieves the maximum acceleration value based on the navigator’s current traversal state.
        /// </summary>
        /// <returns>The acceleration limit depending on whether the navigator is grounded, airborne, or swimming.</returns>
        public Fixed64 GetMaxAcceleration()
        {
            if (IsInWater) return Locomotions.Swim.MaxSwimAcceleration;

            if (IsGrounded) return Locomotions.Move.MaxGroundAcceleration;

            if (Locomotions.Jump.IsJumping
                || Locomotions.Fall.IsFalling
                || IsInAir) return Locomotions.Move.MaxAirAcceleration;

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
                // Apply net buoyant force relative to gravity
                _forceOutput.y += gravityStep * (Locomotions.Swim.BuoyancyFactor - Fixed64.One);

                return;
            }

            if (!IsInAir) return;

            _forceOutput.y = Locomotions.Move.FrameVelocity.y - gravityStep;

            // Ensure velocity does not exceed terminal fall speed
            Fixed64 terminalFallSpeed = Locomotions.Move.FrameVelocity.y
                + (_forceOutput.y * TrailblazerManager.DeltaTime);
            if (terminalFallSpeed < -Locomotions.Move.TerminalVelocity)
                _forceOutput.y = -Locomotions.Move.TerminalVelocity - Locomotions.Move.FrameVelocity.y;

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
        /// Applies an instantaneous jump force to the navigator, considering platform inertia and jump physics.
        /// </summary>
        /// <remarks>
        /// This method validates jump conditions, determines the jump direction, and calculates the jump force.
        /// If the navigator is on a platform, its velocity is adjusted accordingly.
        /// </remarks>
        private void ApplyJumpForce()
        {
            if (!(Locomotions.Jump.IsEnabled
                && Locomotions.IsInControl
                && _frameRequest.IsRequestingJump)) return;

            // Prevent jumping while in active fall state (e.g., walking off a ledge)
            if (Locomotions.Fall.IsFalling)
                return;

            if (IsInWater && !Locomotions.Swim.CanBreachWater)
                return;

            if (!Locomotions.Jump.CanJump)
                return;

            if (Events.CanAffordJump?.Invoke() == false)
                return;

            Vector3d jumpForce;
            if (IsInWater)
            {
                Locomotions.Jump.FrameJumpDirection = Vector3d.Up;
                jumpForce = Locomotions.Jump.FrameJumpDirection * (GetVerticalJumpSpeed() * Locomotions.Swim.BreachJumpMultiplier);
                Events.OnStartWaterBreach?.Invoke();
            }
            else
            {
                // Store jump direction the first time we jump
                if (!Locomotions.Jump.IsJumping)
                {
                    // Calculate the jumping direction
                    Fixed64 slerpAmount = IsTooSteep(FrameSlopeAngle)
                    ? Locomotions.Jump.SteepPerpendicularJumpAmount
                    : Locomotions.Jump.PerpendicularJumpAmount;

                    Locomotions.Jump.FrameJumpDirection = Vector3d.Slerp(
                        Vector3d.Up,
                        CurrentState.SurfaceNormal,
                        slerpAmount);
                }

                jumpForce = Locomotions.Jump.FrameJumpDirection * GetVerticalJumpSpeed();

                Events.OnStartJump?.Invoke(Locomotions.Jump.AvoidGroundingTimer);
            }

            // If we aren't in air, trigger a new jump then...
            Locomotions.Jump.RegisterJump();

            // Remove any existing downward force
            _forceOutput.y = FixedMath.Max(Fixed64.Zero, _forceOutput.y);
            _forceOutput += jumpForce;
        }

        /// <summary>
        /// Applies movement adjustments due to platform motion, ensuring the navigator inherits platform movement correctly.
        /// </summary>
        /// <remarks>
        /// This method updates the navigator’s position and rotation based on the platform’s transform,
        /// preventing unwanted movement shifts when transitioning between platforms.
        /// </remarks>
        private void ApplyPlatformMovement(IMotor navigator)
        {
            if (!IsMovingWithPlatform) return;

            //  Do NOT apply movement if we just jumped — velocity was already injected
            if (Locomotions.Jump.IsJumping) return;

            // Apply platform rotation first THEN apply platform movement
            FixedQuaternion targetRotation = Locomotions.Platform.ActiveTransform.Rotation * Locomotions.Platform.ScoutLocalRotation;
            if (targetRotation != FixedQuaternion.Identity)
            {
                FixedQuaternion rotDelta = targetRotation * Locomotions.Platform.ScoutGlobalRotation.Inverse();
                Events.OnAddRotationDelta?.Invoke(rotDelta);
                navigator.AddRotationDelta(rotDelta);
            }

            Vector3d newGlobalPoint = Locomotions.Platform.ActiveTransform.TransformPoint(Locomotions.Platform.ScoutLocalPoint);
            Vector3d posDelta = newGlobalPoint - Locomotions.Platform.ScoutGlobalPoint;
            if (posDelta != Vector3d.Zero)
            {
                Events.OnAddPositionDelta?.Invoke(posDelta);
                navigator.AddPositionDelta(posDelta);
            }
        }

        #endregion

        #region Phase 2 - Finalize 

        /// <summary>
        /// Finalizes traversal state updates and prepares the navigator for the next simulation frame.
        /// </summary>
        /// <remarks>
        /// This method updates the navigator's velocity, applies necessary adjustments based on traversal state changes,
        /// and processes platform movement or environmental effects as needed.
        /// </remarks>
        public void FinalizeTraversal(IMotor navigator)
        {
            if (!IsInitialized || !IsFrameLocked) return;

            Locomotions.Move.FrameVelocity = (navigator.Position - navigator.LastPosition)
                * TrailblazerManager.InvDeltaTime;

            CurrentState.Update(navigator.FrameCondition, CurrentState.ToTrekCondition());

            CheckJumpStatus(navigator.Position);

            HandlePlatformChange();

            HandlePlatformTransitions();

            HandleMovementTransitions();

            HandleSwimState(navigator.Position);

            HandleFallState(navigator.Position);

            HandlePlatformMovement(navigator.GetFootPosition(), navigator.Rotation);

            IsFrameLocked = false;
        }

        private void CheckJumpStatus(Vector3d position)
        {
            // Make sure we aren't hitting the ceiling
            if (Locomotions.Move.FrameVelocity.y > Fixed64.Zero
                && CurrentState.CeilingLevel != Fixed64.MAX_VALUE)
            {
                if (position.y > CurrentState.CeilingLevel)
                {
                    Locomotions.Move.FrameVelocity = new(
                        Locomotions.Move.FrameVelocity.x,
                        Fixed64.Zero,
                        Locomotions.Move.FrameVelocity.z);
                    Locomotions.Jump.IsJumping = false;
                    Locomotions.Jump.IsHoldingJump = false;
                }
            }
        }

        private void HandlePlatformChange()
        {
            // If we hit a new platform, reset platform state
            if (!Locomotions.Platform.IsEnabled)
                return;

            // Clear it to avoid double-applying next frame
            Locomotions.Platform.FramePlatformVelocity = Vector3d.Zero;
            Locomotions.Platform.MovementTransfer = CurrentState.GroundState?.MotionTransferState ?? MotionTransfer.None;

            if (!DidPlatformChange(CurrentState.GroundState))
                return;

            Fixed4x4 newPlatformMatrix = CurrentState.GroundState?.GroundMatrix ?? Fixed4x4.Identity;

            Locomotions.Platform.LastTransform = Locomotions.Platform.ActivePlatform == null
                ? newPlatformMatrix
                : Locomotions.Platform.ActiveTransform;
            Locomotions.Platform.ActiveTransform = newPlatformMatrix;
            Locomotions.Platform.ActivePlatform = CurrentState.GroundState?.BaseObject ?? null;

            Locomotions.Platform.IsNewPlatform = true;
        }

        /// <summary>
        /// Determines if the navigator has transitioned onto a different platform.
        /// </summary>
        /// <param name="surfaceCondition">The current ground state of the navigator.</param>
        /// <returns>True if the navigator is on a new platform; otherwise, false.</returns>
        private bool DidPlatformChange(GroundCondition? surfaceCondition)
        {
            if (Locomotions.Platform.ActivePlatform == surfaceCondition?.BaseObject)
                return false;

            if (Locomotions.Platform.ActivePlatform == null || Locomotions.Platform.ActivePlatform != surfaceCondition?.BaseObject)
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
                Locomotions.Move.FrameVelocity -= Locomotions.Platform.PlatformVelocity;
                return;
            }

            if (!IsPlatformInteriaApplied) return;

            if (WasGrounded && IsInAir)
            {
                // Scout just left the ground, so it inherits platform inertia into its new velocity.
                Locomotions.Platform.FramePlatformVelocity = Locomotions.Platform.PlatformVelocity;
                Locomotions.Move.FrameVelocity += Locomotions.Platform.PlatformVelocity;
                return;
            }

            if (WasInAir && IsGrounded)
            {
                if (Locomotions.Platform.IsNewPlatform)
                    // If navigator landed on a new platform, we have to wait for two frames
                    // before we know the new velocity of the platform under the navigator
                    Locomotions.Platform.SetHoldPlatform(Locomotions.Platform.ActivePlatform);
                else
                    // If the platform isn’t new, we assume the navigator landed back on the same platform
                    // and subtract platform velocity to prevent doubling the effect.
                    Locomotions.Move.FrameVelocity -= Locomotions.Platform.PlatformVelocity;
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
                    Locomotions.Jump.ResetJumpCounter();

                    if (IsInWater)
                        Events.OnStopWaterBreach?.Invoke();
                    else
                        Events.OnStopJump?.Invoke();

                    return;
                }

                if (!IsInWater)
                    Events.OnLandedFall?.Invoke();
            }

            // Transitioning out of water
            if (Locomotions.Swim.IsEnabled && !IsInWater && WasInWater)
                Locomotions.Swim.ClearState();
        }

        /// <summary>
        /// Manages the navigator's state when entering, exiting, or moving within water.
        /// </summary>
        /// <remarks>
        /// This method updates swim-related properties, tracks dive time, and triggers drowning events if necessary.
        /// </remarks>
        private void HandleSwimState(Vector3d position)
        {
            if (!IsInWater) return;

            // Clear the transient state when entiring water for the first time
            if (!WasInWater)
                Locomotions.ClearStateAll();

            if (Locomotions.Swim.IsEnabled)
            {
                Locomotions.Swim.IsSwimming = Locomotions.Swim.CanSwim;
                Locomotions.Swim.IsDiving = position.y < CurrentState.SurfaceLevel;

                Locomotions.Swim.UpdateDiveTime();

                if (IsInWater && Locomotions.Swim.IsDrowning)
                    Events.OnDrowning?.Invoke(Locomotions.Swim.UnderwaterTimer);
            }
        }

        /// <summary>
        /// Processes the navigator’s fall state, tracking fall height and triggering landing events when appropriate.
        /// </summary>
        /// <remarks>
        /// This method determines when a navigator starts falling, updates fall height, and detects when a safe landing occurs.
        /// </remarks>
        private void HandleFallState(Vector3d position)
        {
            if (!Locomotions.Fall.IsEnabled) return;

            if (IsInWater)
            {
                Locomotions.Fall.IsFalling = false;
                return;
            };

            if (Locomotions.Fall.IsFalling)
            {
                // Make sure we didn't somehow get above the initial start point
                if (position.y > Locomotions.Fall.FallStart)
                    Locomotions.Fall.FallStart = position.y;

                if (!IsInAir && !Locomotions.Slide.IsSliding)
                {
                    // navigator landed after falling
                    Locomotions.Fall.IsFalling = false;
                    Locomotions.Fall.FallEnd = position.y;

                    if (Locomotions.Fall.FallHeight > Fixed64.Zero)
                        Events.OnStopFall?.Invoke(Locomotions.Fall.FallHeight);

                    return;
                }

                Fixed64 fallHeight = (Locomotions.Fall.FallStart - position.y).Abs();
                if (fallHeight > Locomotions.Fall.MaxFallHeight)
                    Events?.OnMaxFallHeightReached?.Invoke();

                return;
            }

            // Ensure we don't trigger falling when moving naturally down a slope
            bool isSlidingTooSleep = Locomotions.Slide.IsSliding
                && FrameSlopeAngle.Abs() > Locomotions.Slide.SlopeLimit;

            // Check if the navigator is in freefall (not simply moving downhill)
            if ((IsInAir || isSlidingTooSleep) && _forceOutput.y < Fixed64.Zero)
            {
                // navigator started falling
                Locomotions.Fall.IsFalling = true;
                Locomotions.Fall.FallStart = position.y;

                // prevent mid-fall jump abuse
                if (Locomotions.Jump.JumpCount > 0 && !Locomotions.Jump.IsCoolingDown)
                    Locomotions.Jump.StartCooldown();

                Events?.OnStartFall?.Invoke();
            }
        }

        /// <summary>
        /// Updates platform movement by synchronizing the navigator's position and rotation with the platform it is standing on.
        /// </summary>
        /// <remarks>
        /// This method prevents unwanted movement shifts when transitioning between platforms, ensuring smooth locomotion.
        /// </remarks>
        private void HandlePlatformMovement(Vector3d footPosition, FixedQuaternion rotation)
        {
            if (!IsMovingWithPlatform) return;

            footPosition.y += Locomotions.Platform.HeightAdjust;
            Locomotions.Platform.ScoutGlobalPoint = footPosition;
            Locomotions.Platform.ScoutLocalPoint = Fixed4x4.InverseTransformPoint(
                Locomotions.Platform.ActiveTransform,
                Locomotions.Platform.ScoutGlobalPoint);

            Locomotions.Platform.ScoutGlobalRotation = rotation;
            Locomotions.Platform.ScoutLocalRotation = Locomotions.Platform.ActiveTransform.Rotation.Inverse() * Locomotions.Platform.ScoutGlobalRotation;
        }

        #endregion

        #region Utility

        /// <summary>
        /// Computes the vertical jump speed required to reach the desired jump height (apex).
        /// </summary>
        /// <returns>The initial vertical velocity needed for the jump.</returns>
        public Fixed64 GetVerticalJumpSpeed() => FixedMath.Sqrt(2 * Locomotions.Jump.BaseJumpHeight * Locomotions.Move.GravityForce);

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
        /// Checks if the navigator is on a sloped surface that is not considered too steep.
        /// </summary>
        /// <returns>True if the navigator is on a valid slope; otherwise, false.</returns>
        public bool IsOnSlope(Fixed64 angle)
        {
            if (!IsGrounded) return false;

            Fixed64 absAngle = FixedMath.Abs(angle); // Account for downhill slopes too
            return absAngle > Fixed64.One && absAngle <= Locomotions.Slide.SlopeLimit + Fixed64.Epsilon;
        }

        /// <summary>
        /// Manually sets the navigator’s velocity, overriding the computed velocity for the next frame.
        /// </summary>
        /// <param name="velocity">The new velocity to assign to the navigator.</param>
        public void SetVelocity(Vector3d velocity)
        {
            Locomotions.Move.FrameVelocity = velocity;
        }

        public void UpdateTraversal(TrekCondition newCondition, bool isInitializing = false)
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

        #endregion
    }
}

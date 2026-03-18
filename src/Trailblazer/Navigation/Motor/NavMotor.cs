using FixedMathSharp;
using System;
using System.Diagnostics;
using Trailblazer.Serialization;

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
        /// This stores the current <see cref="Navigator._frameCondition"/> for the frame.  
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
            || IsInAir && !Handler.Jump.IsJumping && !Handler.Fall.IsFalling;

        #endregion

        #endregion

        #region Construct

        /// <summary>
        /// Creates a new <see cref="NavMotor"/> instance and initializes it with the provided navigator.
        /// </summary>
        /// <param name="initialCondition">The initial traversal condition of the navigator</param>
        /// <returns>A new instance of <see cref="NavMotor"/>.</returns>
        public static NavMotor CreateNew(TrekCondition initialCondition) => new(initialCondition);

        private NavMotor() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="NavMotor"/> class.
        /// </summary>
        /// <param name="condition">The initial traversal condition of the navigator</param>
        public NavMotor(TrekCondition condition) => OnInitialize(condition);

        /// <summary>
        /// Prepares the controller by linking it to the given navigator and setting initial state values.
        /// </summary>
        /// <param name="condition">The initial traversal condition of the navigator</param>
        public void OnInitialize(TrekCondition condition)
        {
            CurrentState = new TransitState(condition);
            if (CurrentState.GroundState.HasValue)
                Handler.Platform.HandlePlatformChange(CurrentState.GroundState); // set the initial platform

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

            velocityDelta = Vector3d.Zero;
            positionDelta = Vector3d.Zero;
            rotationDelta = FixedQuaternion.Identity;


            if (!IsInitialized) return false;

            if (IsFrameLocked)
                return false;

            IsFrameLocked = true;

#if DEBUG
            Debug.WriteLine($"NavMotor State: " +
                $"Grounded={IsGrounded}, " +
                $"InAir={IsInAir}, " +
                $"Velocity={Handler.Move.FrameVelocity}");
#endif

            // Calculate the slope angle for the current frame based on the movement direction and surface normal.
            FrameSlopeAngle = CurrentState.GetSignedSlopeAngle(request.Direction);

            // Store the current velocity for manipulation
            _forceOutput = Handler.Move.FrameVelocity;

            // Update platform velocity prior to applying jump force
            Handler.Platform.UpdatePlatformVelocity();

            // In limbo, prevent any further processing until control is given back
            if (InLimbo)
                Handler.IsInControl = false;

            if (Handler.Jump.IsCoolingDown)
                Handler.Jump.UpdateCooldown();

            ComputeMovementForces(request);

            // Reset this before applying gravity
            if (!request.IsRequestingJump || !Handler.Jump.CanJump)
                Handler.Jump.IsHoldingJump = false;

            // Apply external forces such as gravity, water drag, and friction.
            ApplyEnvironmentalForces();

            ApplyJumpForce(request.IsRequestingJump);

            // Apply the computed force
            velocityDelta = _forceOutput != Vector3d.Zero
                ? _forceOutput * TrailblazerManager.DeltaTime
                : Vector3d.Zero;

            //  Do NOT apply movement if we just jumped — velocity was already injected
            bool isMovingWithPlatform = Handler.Platform.IsActive && (IsGrounded || Handler.Platform.IsLockedToPlatform);
            if (isMovingWithPlatform && !Handler.Jump.IsJumping)
                Handler.Platform.GetPlatformInfluence(request.FootPosition ?? request.Origin,
                    request.Rotation,
                    out positionDelta,
                    out rotationDelta);

            return true;
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
            // Check if navigator is in control
            if (IsGrounded)
            {
                bool isSliding = false;
                if (Handler.Slide.IsEnabled)
                {
                    isSliding = IsTooSteep(FrameSlopeAngle);
                    Handler.Slide.IsSliding = isSliding;
                }
                Handler.IsInControl = !isSliding; // prevent control if sliding
            }
            else
                Handler.IsInControl = !InLimbo;

            if (InLimbo)
                return;

            // remove any downward current downward momentum if we aren't grounded or just landed
            if (!IsGrounded || IsGrounded && WasInAir)
                _forceOutput.y = Fixed64.Zero;

            Vector3d desiredVelocity = GetDesiredVelocity(frameRequest);

            // Apply Friction (resistance to motion)
            if (IsGrounded && frameRequest.Direction == Vector3d.Zero)
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
            if (!IsGrounded && !Handler.IsInControl)
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
        private Vector3d GetDesiredVelocity(TrekRequest frameRequest)
        {
            Vector3d result = Vector3d.Zero;
            if (Handler.Slide.IsSliding)
            {
                // The direction we're sliding in
                result = new Vector3d(CurrentState.SurfaceNormal.x, Fixed64.Zero, CurrentState.SurfaceNormal.z).Normal;
                // Find the input movement direction projected onto the sliding direction
                Vector3d projectedMoveDir = Vector3d.Project(frameRequest.Direction, result);

                // Add the sliding direction, the speed control, and the sideways control vectors
                Vector3d speedContribution = projectedMoveDir * Handler.Slide.SpeedControl;
                Vector3d sidewaysContribution = (frameRequest.Direction - projectedMoveDir) * Handler.Slide.SidewaysControl;

                // Multiply with the sliding speed
                result += (speedContribution + sidewaysContribution) * Handler.Slide.SlidingSpeed;
            }
            else if (Handler.IsInControl && frameRequest.Rate != TrekRate.Stationary)
                result = GetHorizontalVelocity(frameRequest);

            // Ensure smoother stops in water instead of abrupt halts
            if (IsInWater)
            {
                // Calculates the maximum allowable vertical swimming speed
                if (frameRequest.Direction.y != Fixed64.Zero)
                    result.y = frameRequest.Direction.y * Handler.Swim.MaxSwimSpeed;

                // Apply drag resistance (reduces speed as it increases)
                if (result != Vector3d.Zero)
                    result *= FixedMath.Clamp01(Fixed64.One - Handler.Swim.WaterDragFactor);

                return result;
            }

            if (Handler.Platform.IsEnabled
                && Handler.Platform.MovementTransfer == MotionTransfer.PermaTransfer)
            {
                result += Handler.Platform.FramePlatformVelocity;
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

            Vector3d temp;
            Fixed64 zAxisEllipseMultiplier;
            if (IsInWater)
            {
                zAxisEllipseMultiplier = Handler.Swim.MaxSwimSpeed / Handler.Swim.MaxSwimSidewaysSpeed;
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
                    maxSpeed = Handler.Move.MaxBackwardsSpeed;
                else
                {
                    switch (rate)
                    {
                        case TrekRate.Slow:
                            maxSpeed = Handler.Move.MaxSlowSpeed;
                            break;
                        case TrekRate.Moderate:
                            maxSpeed = Handler.Move.MaxModerateSpeed;
                            break;
                        case TrekRate.Fast:
                            maxSpeed = Handler.Move.MaxFastSpeed;
                            break;
                    }
                }

                zAxisEllipseMultiplier = maxSpeed / Handler.Move.MaxSidewaysSpeed;
                if (zAxisEllipseMultiplier <= Fixed64.Zero)
                    return Fixed64.Zero;

                temp = new Vector3d(
                    desiredMovementDirection.x,
                    Fixed64.Zero,
                    desiredMovementDirection.z / zAxisEllipseMultiplier).Normal;
            }

            Fixed64 length = new Vector3d(temp.x, Fixed64.Zero, temp.z * zAxisEllipseMultiplier).Magnitude;
            Fixed64 baseSpeed = length * (IsInWater
                ? Handler.Swim.MaxSwimSidewaysSpeed
                : Handler.Move.MaxSidewaysSpeed);

            if (IsGrounded)
                return baseSpeed;

            // Apply reduced control when jumping or falling
            Fixed64 controlMultiplier = Fixed64.One;

            if (Handler.Jump.IsJumping && !IsGrounded)
                controlMultiplier = Handler.Jump.JumpControlMultiplier;
            else if (Handler.Fall.IsFalling && !IsGrounded)
                controlMultiplier = Handler.Fall.FallControlMultiplier;

            return baseSpeed * controlMultiplier;
        }

        /// <summary>
        /// Retrieves the maximum acceleration value based on the navigator’s current traversal state.
        /// </summary>
        /// <returns>The acceleration limit depending on whether the navigator is grounded, airborne, or swimming.</returns>
        public Fixed64 GetMaxAcceleration()
        {
            if (IsInWater) return Handler.Swim.MaxSwimAcceleration;

            if (IsGrounded) return Handler.Move.MaxGroundAcceleration;

            if (Handler.Jump.IsJumping
                || Handler.Fall.IsFalling
                || IsInAir) return Handler.Move.MaxAirAcceleration;

            return Fixed64.MAX_VALUE; // fallback, should never be hit
        }

        /// <summary>
        /// Applies environmental forces such as gravity, water buoyancy, and downward force when grounded.
        /// </summary>
        private void ApplyEnvironmentalForces()
        {
            Fixed64 gravityStep = Handler.Move.GravityForce * TrailblazerManager.DeltaTime;

            if (IsGrounded)
            {
                _forceOutput.y = FixedMath.Min(Fixed64.Zero, _forceOutput.y) - gravityStep;
                return;
            }

            if (IsInWater)
            {
                // Apply net buoyant force relative to gravity
                _forceOutput.y += gravityStep * (Handler.Swim.BuoyancyFactor - Fixed64.One);

                return;
            }

            if (!IsInAir) return;

            _forceOutput.y = Handler.Move.FrameVelocity.y - gravityStep;

            // Ensure velocity does not exceed terminal fall speed
            Fixed64 terminalFallSpeed = Handler.Move.FrameVelocity.y + (_forceOutput.y * TrailblazerManager.DeltaTime);
            if (terminalFallSpeed < -Handler.Move.TerminalVelocity)
                _forceOutput.y = -Handler.Move.TerminalVelocity - Handler.Move.FrameVelocity.y;

            // When jumping up we don't apply gravity for some time when the user is holding the jump button.
            // This allows for more control over jump height by pressing the button longer.
            if (Handler.Jump.IsJumping && Handler.Jump.IsHoldingJump)
            {
                // Calculate the duration that the extra jump force should have effect.
                // If we're still less than that duration after the jumping time, apply the force.
                Fixed64 extraJumpLimit = (Handler.Jump.JumpStartTime + Handler.Jump.ExtraJumpHeight) / GetVerticalJumpSpeed();

                // Negate the gravity we just applied, except we push in jumpDir rather than jump upwards.
                if (TrailblazerManager.TotalTime <= extraJumpLimit)
                    _forceOutput += Handler.Jump.FrameJumpDirection * gravityStep;
            }
        }

        /// <summary>
        /// Applies an instantaneous jump force to the navigator, considering platform inertia and jump physics.
        /// </summary>
        /// <remarks>
        /// This method validates jump conditions, determines the jump direction, and calculates the jump force.
        /// If the navigator is on a platform, its velocity is adjusted accordingly.
        /// </remarks>
        private void ApplyJumpForce(bool requestJump)
        {
            if (!(Handler.Jump.IsEnabled
                && Handler.IsInControl
                && requestJump)) return;

            // Prevent jumping while in active fall state (e.g., walking off a ledge)
            if (Handler.Fall.IsFalling)
                return;

            if (IsInWater && !Handler.Swim.CanBreachWater)
                return;

            if (!Handler.Jump.CanJump)
                return;

            if (Events.CanAffordJump?.Invoke() == false)
                return;

            Vector3d jumpForce;
            if (IsInWater)
            {
                Handler.Jump.FrameJumpDirection = Vector3d.Up;
                jumpForce = Handler.Jump.FrameJumpDirection * (GetVerticalJumpSpeed() * Handler.Swim.BreachJumpMultiplier);
                Events.OnStartWaterBreach?.Invoke();
            }
            else
            {
                // Store jump direction the first time we jump
                if (!Handler.Jump.IsJumping)
                {
                    // Calculate the jumping direction
                    Fixed64 slerpAmount = IsTooSteep(FrameSlopeAngle)
                    ? Handler.Jump.SteepPerpendicularJumpAmount
                    : Handler.Jump.PerpendicularJumpAmount;

                    Handler.Jump.FrameJumpDirection = Vector3d.Slerp(
                        Vector3d.Up,
                        CurrentState.SurfaceNormal,
                        slerpAmount);
                }

                jumpForce = Handler.Jump.FrameJumpDirection * GetVerticalJumpSpeed();

                Events.OnStartJump?.Invoke(Handler.Jump.AvoidGroundingTimer);
            }

            // If we aren't in air, trigger a new jump then...
            Handler.Jump.RegisterJump();

            // Remove any existing downward force
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
            if (!IsInitialized || !IsFrameLocked) return;

            Handler.Move.FrameVelocity = (newPosition - lastPosition) * TrailblazerManager.InvDeltaTime;

            CurrentState.Update(conditonRefresh, CurrentState.ToTrekCondition());

            CheckJumpStatus(newPosition);

            Handler.Platform.HandlePlatformChange(CurrentState.GroundState);

            HandlePlatformTransitions();

            #region Movement State Transitions

            // Trasitioning to either ground or water
            if (WasInAir && !IsInAir)
            {
                if (Handler.Jump.IsJumping)
                {
                    // Reset cooldown on landing
                    Handler.Jump.ResetJumpCounter();

                    if (IsInWater)
                        Events.OnStopWaterBreach?.Invoke();
                    else
                        Events.OnStopJump?.Invoke();
                }
                else if (!IsInWater)
                    Events.OnLandedFall?.Invoke();
            }

            // Transitioning out of water
            if (Handler.Swim.IsEnabled && !IsInWater && WasInWater)
                Handler.ClearState<SwimLocomotion>();

            #endregion

            HandleSwimState(newPosition);

            HandleFallState(newPosition);

            if (Handler.Platform.IsActive && (IsGrounded || Handler.Platform.IsLockedToPlatform))
                Handler.Platform.HandlePlatformMovement(newFootPosition ?? newPosition, newRotation);

            IsFrameLocked = false;
        }

        // TODO: make sure we can't ever go past ceiling level by any means, including external forces or platform movement
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
            Handler.Jump.IsJumping = false;
            Handler.Jump.IsHoldingJump = false;
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
            if (!Handler.Platform.IsEnabled || IsInWater)
                return;

            bool isReleasing = false;
            if (Handler.Platform.IsHoldingPlatform)
                isReleasing = Handler.Platform.TickHoldOnPlatform();

            if (isReleasing)
            {
                Handler.Move.FrameVelocity -= Handler.Platform.PlatformVelocity;
                return;
            }

            if (!Handler.Platform.InteriaApplied) return;

            if (WasGrounded && IsInAir)
            {
                // Scout just left the ground, so it inherits platform inertia into its new velocity.
                Handler.Platform.FramePlatformVelocity = Handler.Platform.PlatformVelocity;
                Handler.Move.FrameVelocity += Handler.Platform.PlatformVelocity;
                return;
            }

            if (WasInAir && IsGrounded)
            {
                if (Handler.Platform.IsNewPlatform)
                    // If navigator landed on a new platform, we have to wait for two frames
                    // before we know the new velocity of the platform under the navigator
                    Handler.Platform.SetHoldPlatform(Handler.Platform.ActivePlatform);
                else
                    // If the platform isn’t new, we assume the navigator landed back on the same platform
                    // and subtract platform velocity to prevent doubling the effect.
                    Handler.Move.FrameVelocity -= Handler.Platform.PlatformVelocity;
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
            if (!IsInWater)
            {
                if (Handler.Swim.IsEnabled && WasInWater)
                    Handler.ClearState<SwimLocomotion>();

                return;
            }

            // Clear the transient state when entering water for the first time
            if (!WasInWater)
                Handler.ClearStateAll();

            if (Handler.Swim.IsEnabled)
            {
                Handler.Swim.IsSwimming = Handler.Swim.CanSwim;
                Handler.Swim.IsDiving = position.y < CurrentState.SurfaceLevel;

                Handler.Swim.UpdateDiveTime();

                if (IsInWater && Handler.Swim.IsDrowning)
                    Events.OnDrowning?.Invoke(Handler.Swim.UnderwaterTimer);
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
            if (!Handler.Fall.IsEnabled) return;

            if (IsInWater)
            {
                if (Handler.Fall.IsFalling)
                    Handler.ClearState<FallLocomotion>();
                return;
            }

            if (Handler.Fall.IsFalling)
            {
                // Make sure we didn't somehow get above the initial start point
                if (position.y > Handler.Fall.FallStart)
                    Handler.Fall.FallStart = position.y;

                if (!IsInAir && !Handler.Slide.IsSliding)
                {
                    // navigator landed after falling
                    Handler.Fall.IsFalling = false;
                    Handler.Fall.FallEnd = position.y;

                    if (Handler.Fall.FallHeight > Fixed64.Zero)
                        Events.OnStopFall?.Invoke(Handler.Fall.FallHeight);

                    // Clear fall state after landing to reset max height and other properties for the next fall
                    Handler.ClearState<FallLocomotion>();

                    return;
                }

                Fixed64 currentFallHeight = (Handler.Fall.FallStart - position.y).Abs();
                if (currentFallHeight > Handler.Fall.MaxFallHeight)
                    Events?.OnMaxFallHeightReached?.Invoke();

                return;
            }

            // Ensure we don't trigger falling when moving naturally down a slope
            bool isSlidingTooSleep = Handler.Slide.IsSliding
                && FrameSlopeAngle.Abs() > Handler.Slide.SlopeLimit;

            // Check if the navigator is in freefall (not simply moving downhill)
            if ((IsInAir || isSlidingTooSleep) && _forceOutput.y < Fixed64.Zero)
            {
                // navigator started falling
                Handler.Fall.IsFalling = true;
                Handler.Fall.FallStart = position.y;

                // prevent mid-fall jump abuse
                if (Handler.Jump.JumpCount > 0 && !Handler.Jump.IsCoolingDown)
                    Handler.Jump.StartCooldown();

                Events?.OnStartFall?.Invoke();
            }
        }

        #endregion

        #region Utility

        /// <summary>
        /// Computes the vertical jump speed required to reach the desired jump height (apex).
        /// </summary>
        /// <returns>The initial vertical velocity needed for the jump.</returns>
        public Fixed64 GetVerticalJumpSpeed() => FixedMath.Sqrt(2 * Handler.Jump.BaseJumpHeight * Handler.Move.GravityForce);

        /// <summary>
        /// Determines whether the current surface is too steep for normal movement.
        /// </summary>
        /// <returns>True if the slope exceeds the allowable incline; otherwise, false.</returns>
        public bool IsTooSteep(Fixed64 angle)
        {
            if (!IsGrounded) return false;

            Fixed64 absAngle = FixedMath.Abs(angle); // Handle both positive (uphill) and negative (downhill) slopes
            return absAngle > Handler.Slide.SlopeLimit - Fixed64.Epsilon;
        }

        /// <summary>
        /// Checks if the navigator is on a sloped surface that is not considered too steep.
        /// </summary>
        /// <returns>True if the navigator is on a valid slope; otherwise, false.</returns>
        public bool IsOnSlope(Fixed64 angle)
        {
            if (!IsGrounded) return false;

            Fixed64 absAngle = FixedMath.Abs(angle); // Account for downhill slopes too
            return absAngle > Fixed64.One && absAngle <= Handler.Slide.SlopeLimit + Fixed64.Epsilon;
        }

        /// <summary>
        /// Manually sets the navigator’s velocity, overriding the computed velocity for the next frame.
        /// </summary>
        /// <param name="velocity">The new velocity to assign to the navigator.</param>
        public void SetVelocity(Vector3d velocity)
        {
            Handler.Move.FrameVelocity = velocity;
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

        /// <inheritdoc />
        public void RecordData(IChronicler chronicler)
        {
            LocomotionHandler handler = Handler;
            TrekCondition currentCondition = CurrentState?.ToTrekCondition() ?? new TrekCondition();
            TrekCondition? previousCondition = CurrentState?.PreviousState;
            bool isInitialized = IsInitialized;

            RecordDeep.Look(chronicler, ref handler, "handler");
            RecordValues.Look(chronicler, ref currentCondition, "currentCondition", currentCondition);
            RecordValues.Look(chronicler, ref previousCondition, "previousCondition", previousCondition);
            RecordValues.Look(chronicler, ref isInitialized, "isInitialized", isInitialized);

            if (chronicler.Mode == SerializationMode.Loading)
            {
                Handler = handler;
                CurrentState ??= new(currentCondition, previousCondition);
                CurrentState.Update(currentCondition, previousCondition);
                IsInitialized = isInitialized;
                IsFrameLocked = false;
                FrameSlopeAngle = Fixed64.Zero;
                _forceOutput = Vector3d.Zero;
            }
        }

        #endregion
    }
}

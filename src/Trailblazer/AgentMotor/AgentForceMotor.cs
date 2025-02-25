using FixedMathSharp;
using System.Collections.Generic;
using System.Diagnostics;
using Trailblazer.AgentMotor.Locomotions;
using Trailblazer.Utility.Coroutines;

namespace Trailblazer.AgentMotor
{
    [System.Serializable]
    public partial class AgentForceMotor
    {
        #region Constants

        public static readonly Fixed64 VelocityEpsilon = Fixed64.FromRaw(0x418938L); //0.001f;

        public static readonly Fixed64 DefaultMaxWalkSpeed = (Fixed64)0.1d;

        public static readonly Fixed64 DefaultMaxJogSpeed = (Fixed64)0.25d;

        public static readonly Fixed64 DefaultMaxSprintSpeed = (Fixed64)0.5d;

        public static readonly Fixed64 DefaultMaxSidewaysSpeed = (Fixed64)0.15d;

        public static readonly Fixed64 DefaultMaxBackwardsSpeed = (Fixed64)0.15d;

        public static readonly Fixed64 DefaultMaxGroundAcceleration = (Fixed64)30;

        public static readonly Fixed64 DefaultMaxAirAcceleration = (Fixed64)20;

        public static readonly FixedCurve DefaultAnimationCurve = new FixedCurve(FixedCurveMode.Linear,
                new FixedCurveKey(-90, 1), // Full downward slope
                new FixedCurveKey(0, 1), // Flat ground
                new FixedCurveKey(90, 0) // Full upward slope
            );

        #endregion

        #region Fields

        public bool DebugMode;

        /// <summary>
        /// Does this script currently respond to input?
        /// </summary>
        private bool _canControl = true;

        /// <summary>
        /// This keeps track of our current velocity while we're not grounded
        /// </summary>
        public Vector3d InAirVelocityCache = Vector3d.Zero;

        private Vector3d _targetForce;

        private bool _moved;

        private bool _groundedStateChanged;

        private Vector3d _groundNormal;

        private Vector3d _lastGroundNormal;

        private bool _isInAir;

        public PlatformLocomotion Platform = new();

        public JumpLocomotion Jumping = new();

        public FallLocomotion Falling = new();

        public SlideLocomotion Sliding = new();

        public SwimLocomotion Swimming = new();

        /// <summary>
        /// The maximum horizontal speed when moving
        /// </summary>
        public Fixed64 MaxWalkSpeed = DefaultMaxWalkSpeed;

        public Fixed64 MaxJogSpeed = DefaultMaxJogSpeed;

        public Fixed64 MaxSprintSpeed = DefaultMaxSprintSpeed;

        public Fixed64 MaxSidewaysSpeed = DefaultMaxSidewaysSpeed;

        public Fixed64 MaxBackwardsSpeed = DefaultMaxBackwardsSpeed;

        /// <summary>
        /// How fast does the character change speeds?  Higher is faster.
        /// </summary>
        public Fixed64 MaxGroundAcceleration = DefaultMaxGroundAcceleration;

        public Fixed64 MaxAirAcceleration = DefaultMaxAirAcceleration;

        public Fixed64 MoveSpeedMultiplier;

        /// <summary>
        /// Curve for multiplying speed based on slope(negative = downwards)
        /// </summary>
        public FixedCurve SlopeSpeedMultiplier = DefaultAnimationCurve;

        public Fixed64 WaterDragFactor = Fixed64.FromRaw(0x10000000L); // ~0.0625

        #endregion

        #region Properties

        public IDrive Driver { get; set; }

        /// <summary>
        /// The current global direction we want the character to move in.
        /// </summary>
        public Vector3d InputMoveDirection { get; private set; }

        public SpeedState CurrentState { get; private set; }

        public bool IsRequestingJump { get; private set; }

        public Vector3d TargetForce { get => _targetForce; internal set => value = _targetForce; }

        #endregion

        #region Init (Constructor) 

        public void Init(IDrive driver)
        {
            Driver = driver ?? throw new System.ArgumentNullException(nameof(driver));
            driver.OnDidMove += () => _moved = true;
        }

        #endregion

        #region Update Lifecycle

        /// <summary>
        /// Call once every simulation frame (i.e. FixedUpdate)
        /// </summary>
        public void Simulate(Vector3d direction, MovementInput movementInput, bool jumpInput = false)
        {
            if (DebugMode)
                Debug.WriteLine($"AgentMotor State: Grounded={Driver.GroundData.IsGrounded}, InAir={_isInAir}, Velocity={Driver.BodyData.LinearVelocity}");

            // Reset the target force to prevent accumulation
            _targetForce = Vector3d.Zero;
            SetMovementState(movementInput, jumpInput);
            InputMoveDirection = _canControl ? direction : Vector3d.Zero;

            if (_moved)
                HandleMovementTransitions();

            if (_groundedStateChanged && Platform.IsApplyingInititalVelocity)
            {
                if (_isInAir)
                {
                    InAirVelocityCache = Platform.PlatformVelocity;
                    _targetForce += InAirVelocityCache;
                }
                else
                {
                    // If driver landed on a new platform, we have to wait for two frames
                    // before we know the new velocity of the platform under the driver
                    CoroutineManager.StartCoroutine(SubtractNewPlatformVelocity(this));
                }
            }

            if (Platform.IsOnPlatform)
                Platform.UpdatePlatformVelocity();

            // If we're in the air and don't have control, don't apply any velocity change at all.
            if (Driver.GroundData.IsGrounded || _canControl)
                _targetForce = ComputeMovementForces();
            _targetForce = ApplyJumpForce(_targetForce);

            // TODO: Check if this needs to apply after jump force
            // so we register the jump before applying platform movement
            if (Driver.GroundData.IsGrounded && Platform.IsPlatformVelocityEnforced)
                UpdatePlatformMovement();

            if (Swimming.IsEnabled && Swimming.IsSwimming)
                _targetForce.y = InputMoveDirection.y;

            Driver?.OnAddForce?.Invoke(TargetForce);

            _groundedStateChanged = false;
            Platform.IsNewPlatform = false;
        }

        #endregion

        #region Movement Processing

        private void SetMovementState(MovementInput movementInput, bool hasJumpInput = false)
        {
            CurrentState = movementInput switch
            {
                MovementInput.Walk => SpeedState.Normal,
                MovementInput.Jog => SpeedState.Fast,
                MovementInput.Run => SpeedState.Faster,
                _ => SpeedState.None,
            };
            IsRequestingJump = hasJumpInput;
        }

        public void ToggleSwimingStatus(bool status)
        {
            if (Swimming.IsEnabled)
            {
                if (status != Swimming.IsSwimming)
                {
                    Swimming.IsSwimming = status;
                    Swimming.IsDrowning = false;
                    Swimming.IsDiving = false;
                    Swimming.UnderwaterTimer = Fixed64.Zero;
                }
            }
        }

        private void HandleMovementTransitions()
        {
            // If we hit a new platform while in-air, reset velocity
            if (Platform.IsEnabled && Platform.DidPlatformChanged(Driver.GroundData.GroundMatrix))
                InAirVelocityCache = Vector3d.Zero;

            if (Swimming.IsSwimming)
            {
                if (Driver.GroundData.IsGrounded && _isInAir && Jumping.IsJumping)
                {
                    Jumping.StartJumpFrame = 0;
                    Jumping.IsJumping = false;

                    // TODO: should these OnStop* actions actually be a callback assigned to the OnStart* actions?
                    Driver.OnStopWaterBreach?.Invoke();
                }
            }
            else
                CheckGroundingState();

            if (Falling.IsEnabled)
            {
                if (Swimming.IsSwimming || Jumping.IsJumping)
                {
                    // TODO: can't be falling if your swimming, maybe reset here if we were falling?
                    Falling.IsFalling = false;
                    return;
                }

                UpdateFallingState();
            }

            if (Swimming.IsEnabled)
                UpdateSwimState();

            if (Driver.GroundData.IsGrounded && Platform.IsPlatformVelocityEnforced)
                UpdatePlatformState();

            _moved = false;
        }

        private void CheckGroundingState()
        {
            bool wasInAir = _isInAir;
            _isInAir = !Driver.GroundData.IsGrounded;

            if (wasInAir && !_isInAir) // Landed
            {
                _groundedStateChanged = true;
                InAirVelocityCache *= Fixed64.Half; // Preserve some horizontal

                if (Jumping.IsJumping)
                {
                    Jumping.IsJumping = false;
                    Jumping.StartJumpFrame = 0;
                    Jumping.OnCooldown = false; // Reset cooldown on landing
                    Driver.OnStopJump();
                }
            }
            else if (!wasInAir && _isInAir) // Left the ground
            {
                _groundedStateChanged = true;
                _groundNormal = Vector3d.Zero;
                _lastGroundNormal = Vector3d.Up;
                return;
            }

            if (_isInAir) return;

            _groundNormal = _lastGroundNormal != Vector3d.Up
                ? Vector3d.Lerp(_lastGroundNormal, Driver.GroundData.GroundNormal, Fixed64.FromRaw(0x40000000L))
                : Driver.GroundData.GroundNormal;

            _lastGroundNormal = Driver.GroundData.GroundNormal;
        }

        private void UpdateFallingState()
        {
            bool isSliding = IsSliding();
            if ((!Driver.GroundData.IsGrounded || isSliding)
                && !Falling.IsFalling && Driver.BodyData.LinearVelocity.y < Fixed64.Zero)
            {
                Falling.IsFalling = true;
                Falling.FallStart = Driver.BodyData.WorldPosition.y;
                Driver.OnStartFall?.Invoke();
            }
            else if (Driver.GroundData.IsGrounded && !isSliding && Falling.IsFalling)
            {
                Falling.IsFalling = false;
                Falling.FallEnd = Driver.BodyData.WorldPosition.y;
                if (Falling.FallHeight > Fixed64.Zero)
                    Driver.OnStopFall?.Invoke(Falling.FallHeight);
            }
            else if (Driver.BodyData.WorldPosition.y > Falling.FallStart)
                Falling.FallStart = Driver.BodyData.WorldPosition.y;

            if (Falling.IsFalling && (Falling.FallStart - Driver.BodyData.WorldPosition.y).Abs() > Falling.MaxFallHeight)
                Driver.OnMaxFallHeightReached?.Invoke();
        }

        public void UpdateSwimState()
        {
            if (Swimming.IsDiving)
                Swimming.UnderwaterTimer += TrailblazerSettings.DeltaTime;
            else if (!Swimming.IsDiving && Swimming.UnderwaterTimer != Fixed64.Zero)
            {
                Swimming.UnderwaterTimer -= TrailblazerSettings.DeltaTime * Swimming.BreathRegenerateIncrement;
                if (Swimming.UnderwaterTimer < Fixed64.Zero)
                    Swimming.UnderwaterTimer = Fixed64.Zero;
            }

            Swimming.IsDrowning = Swimming.IsDrowningStatus;
            if (Swimming.IsDrowning && Swimming.IsDiving)
                Driver.OnDrowning?.Invoke(Swimming.UnderwaterTimer);
        }

        public void UpdatePlatformState()
        {
            // Use the center of the lower half sphere of a capsule as reference point.
            // This works best when the driver is standing on moving tilting platforms.
            Fixed64 adjustedHeight = Driver.ColliderData.ScaledSize.y * PlatformLocomotion.GlobalHeightAdjust;
            Platform.ActiveGlobalPoint = Driver.BodyData.WorldPosition + Vector3d.Up
                * (Driver.ColliderData.Center.y - adjustedHeight + Driver.ColliderData.Radius);

            Platform.ActiveLocalPoint = Fixed4x4.InverseTransformPoint(Platform.CurrentPlatformMatrix, Platform.ActiveGlobalPoint);

            // Support moving platform rotation as well:
            Platform.ActiveGlobalRotation = Driver.BodyData.VisualRotation;
            Platform.ActiveLocalRotation = Platform.PlatformRotation.Inverse() * Platform.ActiveGlobalRotation;
        }

        /// <summary>
        /// Find desired velocity
        /// </summary>
        private Vector3d ComputeMovementForces()
        {
            Vector3d velocityChange = GetDesiredVelocity() - Driver.BodyData.LinearVelocity;
            Fixed64 maxVelocityChange = GetMaxVelocity() * TrailblazerSettings.DeltaTime;
            Vector3d clampedVelocityChange = velocityChange.SqrMagnitude > maxVelocityChange * maxVelocityChange
                    ? velocityChange.Normal * maxVelocityChange
                    : velocityChange;
            return (clampedVelocityChange * Driver.BodyData.Mass) / TrailblazerSettings.DeltaTime;
        }

        public Vector3d GetDesiredVelocity()
        {
            Vector3d result;
            if (Driver.GroundData.IsGrounded && Sliding.IsEnabled && IsTooSteep())
            {
                // The direction we're sliding in
                result = new Vector3d(_groundNormal.x, Fixed64.Zero, _groundNormal.z).Normal;
                // Find the input movement direction projected onto the sliding direction
                Vector3d projectedMoveDir = Vector3d.Project(InputMoveDirection, result);
                // Add the sliding direction, the speed control, and the sideways control vectors
                result = result + projectedMoveDir
                    * Sliding.SpeedControl + (InputMoveDirection - projectedMoveDir)
                    * Sliding.SidewaysControl;
                // Multiply with the sliding speed
                result *= Sliding.AdjustedSlidingSpeed;

                _canControl = false;
            }
            else
            {
                result = GetHorizontalVelocity();

                // Ensure that the desired movement of the driver aligns with the surface they are on
                // i.e., the driver doesn't try to move into the ground when the ground is sloping upwards
                if (Driver.GroundData.IsGrounded)
                    result = Vector3d.ProjectOnPlane(result, _groundNormal);

                _canControl = true;

                if (Swimming.IsEnabled && Swimming.IsSwimming)
                {
                    // Ensures smoother stops in water instead of abrupt halts
                    return Swimming.ApplyWaterDrag(result);
                }
            }

            if (Platform.IsPlatformVelocityEnforced)
            {
                result += InAirVelocityCache;
                result.y = Fixed64.Zero;
            }

            if (Driver.GroundData.IsGrounded)
                // Ensures driver does not "digging into" the ground when moving over a bump
                result = AdjustGroundVelocityToNormal(result, _groundNormal);

            return result;
        }

        private Vector3d GetHorizontalVelocity()
        {
            Fixed3x3 transposedMatrix = Driver.BodyData.VisualRotation.ToMatrix3x3();
            Vector3d desiredLocalDirection = Fixed3x3.InverseTransformDirection(transposedMatrix, InputMoveDirection);
            Fixed64 speed = MaxSpeedInDirection(desiredLocalDirection);

            speed *= MoveSpeedMultiplier;

            if (Driver.GroundData.IsGrounded)
            {
                // Modify max speed on slopes based on slope speed multiplier curve
                Fixed64 movementSlopeAngle = FixedMath.Asin(Driver.BodyData.LinearVelocity.Normal.y.ClampOne()).ToDegree();
                speed *= SlopeSpeedMultiplier.Evaluate(movementSlopeAngle);
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

            if (Swimming.IsEnabled && Swimming.IsSwimming)
                return Swimming.MaxSwimSpeedInDirection(desiredMovementDirection);

            Fixed64 maxSpeed = Fixed64.Zero;
            if (desiredMovementDirection.z < Fixed64.Zero)
                maxSpeed = MaxBackwardsSpeed;
            else
            {
                switch (CurrentState)
                {
                    case SpeedState.Normal:
                        maxSpeed = MaxWalkSpeed;
                        break;
                    case SpeedState.Fast:
                        maxSpeed = MaxJogSpeed;
                        break;
                    case SpeedState.Faster:
                        maxSpeed = MaxSprintSpeed;
                        break;
                }
            }

            Fixed64 zAxisEllipseMultiplier = maxSpeed / MaxSidewaysSpeed;
            if (zAxisEllipseMultiplier <= Fixed64.Zero)
                return Fixed64.Zero;

            Vector3d temp = new Vector3d(
                desiredMovementDirection.x,
                Fixed64.Zero,
                desiredMovementDirection.z / zAxisEllipseMultiplier).Normal;
            Fixed64 length = new Vector3d(temp.x, Fixed64.Zero, temp.z * zAxisEllipseMultiplier).Magnitude
                * MaxSidewaysSpeed;
            return length;
        }

        public static Vector3d AdjustGroundVelocityToNormal(Vector3d hVelocity, Vector3d groundNormal)
        {
            Vector3d sideways = Vector3d.Cross(Vector3d.Up, hVelocity);
            return Vector3d.Cross(sideways, groundNormal).Normal * hVelocity.Magnitude;
        }

        private Fixed64 GetMaxVelocity()
        {
            if (Swimming.IsEnabled && Swimming.IsSwimming)
                return Swimming.MaxVelocity;

            if (Driver.GroundData.IsGrounded)
                return MaxGroundAcceleration;

            if (Jumping.IsJumping)
                return MaxAirAcceleration * Fixed64.FromRaw(0x60000000L); // 75% control when jumping

            if (_isInAir)
                return MaxAirAcceleration * Fixed64.FromRaw(0x30000000L); // 50% control when falling

            return Fixed64.Zero;
        }

        private Vector3d ApplyJumpForce(Vector3d currentForce)
        {
            if (!_canControl || !IsRequestingJump || !Jumping.IsEnabled || Jumping.OnCooldown
                || !Driver.CanAffordToJump())
            {
                // TODO: We need to reset state here...or should we be doing this somewhere else?
                Jumping.IsJumping = false;
                Jumping.IsHoldingJump = false;
                return currentForce;
            }

            if (Swimming.IsSwimming)
            {
                // Trigger a water breach
                Jumping.IsJumping = true;
                Jumping.StartJumpFrame = TrailblazerSettings.FrameCount;
                CoroutineManager.StartCoroutine(StartJumpCooldown(this));
                Jumping.IsHoldingJump = true;

                currentForce.y += Swimming.BuoyantForce; // Apply buoyant jump force

                // TODO: similliar to jump...maybe we should pass in a call back for when breaching is done?
                Driver.OnStartWaterBreach?.Invoke();
                return currentForce;
            }

            if (_isInAir)
                return Jumping.GetInAirJumpForce(TargetForce); // override any previous force if we're jumping in-air
            else if (!Driver.GroundData.IsGrounded)
                return currentForce;

            // Trigger a new jump then...
            Jumping.IsJumping = true;
            Jumping.StartJumpFrame = TrailblazerSettings.FrameCount;
            CoroutineManager.StartCoroutine(StartJumpCooldown(this));
            Jumping.IsHoldingJump = true;

            currentForce = Jumping.GetGroundedJumpForce(TargetForce, _groundNormal, IsTooSteep());

            // Apply inertia from platform
            if (Platform.IsApplyingInititalVelocity)
            {
                InAirVelocityCache = Platform.PlatformVelocity;
                currentForce += Platform.PlatformVelocity;
            }

            Driver.OnStartJump?.Invoke();
            Driver.OnSkipGroundingCheckTimer?.Invoke(Jumping.AvoidGroundingAfterJumpTime);

            return currentForce;
        }

        private void UpdatePlatformMovement()
        {
            Vector3d newWorldPoint = Fixed4x4.TransformPoint(Platform.CurrentPlatformMatrix, Platform.ActiveLocalPoint);
            Vector3d moveDistance = newWorldPoint - Platform.ActiveGlobalPoint;
            if (moveDistance != Vector3d.Zero)
                Driver.OnSetPosition?.Invoke(Driver.BodyData.WorldPosition + moveDistance);

            // Support moving platform rotation
            FixedQuaternion targetRotation = Platform.PlatformRotation * Platform.ActiveLocalRotation;
            FixedQuaternion newWorldRotation = FixedQuaternion.Lerp(Driver.BodyData.VisualRotation, targetRotation, Fixed64.FromRaw(0x40000000L));
            if (newWorldRotation != FixedQuaternion.Identity)
                Driver.OnSetRotation?.Invoke(newWorldRotation);
        }

        public bool IsSliding()
        {
            return Sliding.IsEnabled && Driver.GroundData.IsGrounded && IsTooSteep();
        }

        public bool IsTooSteep()
        {
            Fixed64 angle = Vector3d.Angle(Vector3d.Up, _groundNormal);
            return angle > Sliding.SlopeLimit;
        }

        #endregion

        #region Coroutines 

        private static IEnumerator<LockedYieldInstruction> StartJumpCooldown(AgentForceMotor agentForceMotor)
        {
            agentForceMotor.Jumping.OnCooldown = false;
            yield return new WaitForRealSeconds(agentForceMotor.Jumping.CooldownTime);
            agentForceMotor.Jumping.OnCooldown = true;
        }

        /// <summary>
        /// When landing, subtract the velocity of the new platform from the drivers's velocity
        /// since movement on the platform is relative to the movement of the ground.
        /// </summary>
        private static IEnumerator<LockedYieldInstruction> SubtractNewPlatformVelocity(AgentForceMotor agentMotor)
        {
            Fixed4x4 platform = agentMotor.Platform.CurrentPlatformMatrix;
            yield return new WaitForNextSimulate();
            yield return new WaitForNextSimulate();
            if (agentMotor.Driver.GroundData.IsGrounded && platform == agentMotor.Platform.CurrentPlatformMatrix)
                yield break;
            agentMotor.TargetForce -= agentMotor.Platform.PlatformVelocity;
        }


        #endregion
    }
}

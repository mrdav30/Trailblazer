//=======================================================================
// NavMotor.Finalization.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using FixedMathSharp;

namespace Trailblazer.Navigation.Motor;

public partial class NavMotor
{
    #region Finalize

    /// <summary>
    /// Finalizes traversal state updates and prepares the object for the next simulation frame.
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
        if (FrameCount == _pendingTraversalFrame)
            return;

        throw new InvalidOperationException(
            $"NavMotor traversal opened on frame {_pendingTraversalFrame} cannot be finalized on frame {FrameCount}. Call AbortTraversalFrame() to discard stale traversal state before starting a new frame.");
    }

    private void RefreshTraversalState(
        Vector3d newPosition,
        Vector3d lastPosition,
        TrekCondition conditonRefresh)
    {
        Handler.Move.FrameVelocity = (newPosition - lastPosition) * InvDeltaTime;

        CurrentState.Update(conditonRefresh, CurrentState.ToTrekCondition());

        PlatformModule.HandlePlatformChange(CurrentState.GroundState);
        HandlePlatformTransitions();

        // Ceiling check runs last so platform inertia inherited this frame cannot bypass the clamp.
        CheckJumpStatus(newPosition);
    }

    private void HandleTraversalTransitions()
    {
        if (WasInGas && !IsInGas)
            HandleGasExitTransition();

        if (WaterModule?.IsEnabled == true && !IsInLiquid && WasInLiquid)
            Handler.ClearTransientState<WaterLocomotion>();
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
            JumpLocomotion jumpModule = JumpModule!;

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
        PlatformLocomotion platformModule = PlatformModule;
        if (!platformModule.IsActive || (!IsOnSolid && !platformModule.IsLockedToPlatform))
            return;

        platformModule.HandlePlatformMovement(position, rotation);
    }

    private void CheckJumpStatus(Vector3d position)
    {
        // Make sure we aren't hitting the ceiling
        if (Handler.Move.FrameVelocity.Y <= Fixed64.Zero || CurrentState.CeilingLevel == Fixed64.MaxValue)
            return;

        if (position.Y <= CurrentState.CeilingLevel) return;

        Handler.Move.FrameVelocity = new(
            Handler.Move.FrameVelocity.X,
            Fixed64.Zero,
            Handler.Move.FrameVelocity.Z);

        if (JumpModule != null)
        {
            JumpModule.IsJumping = false;
            JumpModule.IsHoldingJump = false;
        }
    }

    private void HandlePlatformTransitions()
    {
        // Don't process platform state when in water
        PlatformLocomotion platformModule = PlatformModule;
        if (!platformModule.IsEnabled || IsInLiquid)
            return;

        bool isReleasing = false;
        if (platformModule.IsHoldingPlatform)
            isReleasing = platformModule.TickHoldOnPlatform();

        if (isReleasing)
        {
            Handler.Move.FrameVelocity -= platformModule.PlatformVelocity;
            return;
        }

        if (!platformModule.InertiaApplied) return;

        if (WasOnSolid && IsInGas)
        {
            // Scout just left the ground, so it inherits platform inertia into its new velocity.
            platformModule.FramePlatformVelocity = platformModule.PlatformVelocity;
            Handler.Move.FrameVelocity += platformModule.PlatformVelocity;
            return;
        }

        if (WasInGas && IsOnSolid)
        {
            if (platformModule.IsNewPlatform)
                // If object landed on a new platform, we have to wait for two frames
                // before we know the new velocity of the platform under the object
                platformModule.SetHoldPlatform(platformModule.ActivePlatform);
            else
                // If the platform isn’t new, we assume the object landed back on the same platform
                // and subtract platform velocity to prevent doubling the effect.
                Handler.Move.FrameVelocity -= platformModule.PlatformVelocity;
        }
    }

    private void HandleSwimState(Vector3d position)
    {
        if (!IsInLiquid)
        {
            if (WaterModule?.IsEnabled == true && WasInLiquid)
                Handler.ClearTransientState<WaterLocomotion>();

            return;
        }

        bool hadSwimIntent = WaterModule?.RequestedSwimThisTraversal == true;
        // Clear the transient state when entering water for the first time
        if (!WasInLiquid)
            Handler.ClearAllTransientState();

        if (WaterModule?.IsEnabled == true)
        {
            WaterModule.IsSwimming = WaterModule.CanSwim && hadSwimIntent;
            WaterModule.IsDiving = position.Y < CurrentState.SurfaceLevel;

            WaterModule.UpdateDiveTime();

            if (WaterModule.IsDrowning)
                Events.OnDrowning?.Invoke(WaterModule.UnderwaterTimer);
        }
    }

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

        System.Diagnostics.Debug.Assert(
            !(FlyModule.IsFlying && IsFalling),
            "UpdateFlightState owns fall-state cleanup before traversal finalization.");
    }

    private void HandleFallState(Vector3d position)
    {
        if (!Handler.Fall.IsEnabled) return;

        if (ShouldClearActiveFallState())
        {
            System.Diagnostics.Debug.Assert(
                !IsFalling,
                "Swim, flight, and climb transitions own fall-state cleanup before fall finalization.");
            return;
        }

        if (IsFalling)
        {
            UpdateActiveFallState(position);
            return;
        }

        TryStartFall(position);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool ShouldClearActiveFallState() => IsInLiquid || IsFlying || IsClimbing;

    private void UpdateActiveFallState(Vector3d position)
    {
        if (position.Y > Handler.Fall.FallStart)
            Handler.Fall.FallStart = position.Y;

        if (!IsInGas && !IsTooSteep(FrameSlopeAngle))
        {
            Handler.Fall.IsFalling = false;
            Handler.Fall.FallEnd = position.Y;

            if (Handler.Fall.FallHeight > Fixed64.Zero)
                Events.OnStopFall?.Invoke(Handler.Fall.FallHeight);

            Handler.ClearTransientState<FallLocomotion>();
            return;
        }

        Fixed64 currentFallHeight = (Handler.Fall.FallStart - position.Y).Abs();
        if (currentFallHeight > Handler.Fall.MaxFallHeight)
            Events.OnMaxFallHeightReached?.Invoke();
    }

    private void TryStartFall(Vector3d position)
    {
        bool isSlidingTooSteep = IsTooSteep(FrameSlopeAngle);
        if (!(IsInGas || isSlidingTooSteep) || _forceOutput.Y >= Fixed64.Zero)
            return;

        Handler.Fall.IsFalling = true;
        Handler.Fall.FallStart = position.Y;

        if (JumpModule != null && JumpModule.JumpCount > 0 && !JumpModule.IsCoolingDown)
            JumpModule.StartCooldown();

        Events.OnStartFall?.Invoke();
    }

    private Fixed64 GetScaledFlightSpeedMultiplier(TrekRate rate, Fixed64 maxFastSpeed) => rate switch
    {
        TrekRate.Slow => Handler.Move.MaxSlowSpeed / maxFastSpeed,
        TrekRate.Moderate => Handler.Move.MaxModerateSpeed / maxFastSpeed,
        // Fast never reaches this helper because GetFlightSpeedMultiplier(...) short-circuits it to one.
        _ => Fixed64.Zero
    };

    private void StopClimb(bool wasForced)
    {
        Handler.ClearTransientState<ClimbLocomotion>();

        if (wasForced)
            Events.OnClimbSlip?.Invoke();

        Events.OnStopClimb?.Invoke();
    }

    private bool CanContinueActiveMantle()
    {
        ClimbLocomotion climbModule = ClimbModule!;
        if (!climbModule.ValidateActiveMantleWithHost)
            return true;

        if (climbModule.ClimbResolver is not IActiveMantleValidator validator)
            return true;

        return validator.TryValidateActiveMantle(
                CurrentState,
                climbModule.CreateActiveMantleState(),
                out MantleValidationSnapshot snapshot)
            && snapshot.CanContinueMantle;
    }

    private bool HasReachedMantleTarget(Vector3d position)
    {
        ClimbLocomotion climbModule = ClimbModule!;
        if (!climbModule.MantleTargetPosition.HasValue)
            return false;

        Fixed64 tolerance = climbModule.ClimbStartTolerance;
        Vector3d mantleTargetPosition = climbModule.MantleTargetPosition.GetValueOrDefault();
        return (mantleTargetPosition - position).MagnitudeSquared <= tolerance * tolerance;
    }

    private void CompleteMantle()
    {
        StopClimb(wasForced: false);
    }

    #endregion
}

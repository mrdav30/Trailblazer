//=======================================================================
// Navigator2D.Motor.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <content>Stages native motor intent and publishes support only at accepted poses.</content>
public abstract partial class Navigator2D
{
    private readonly NavigationMotorSettings2D _motorSettings;
    private Fixed64 _verticalVelocity;
    private Fixed64 _preparedVertical;
    private Vector2d _facing = Vector2d.Right;
    private Vector2d _preparedFacing;
    private Vector2d _preparedCarry;

    /// <summary>Gets the bound body geometry and navigation permissions.</summary>
    public NavigationAgentProfile2D NavigationProfile { get; }
    /// <summary>Gets the status of the most recently committed physical support sample.</summary>
    public NavigationSupportStatus2D SupportStatus { get; private set; }
    /// <summary>Gets the fresh physical witness, if available.</summary>
    public NavigationSupport2D? Support { get; private set; }
    /// <summary>Gets whether the fresh support meets slope policy and the motor is not departing upward.</summary>
    public bool IsGrounded { get; private set; }

    /// <summary>Samples support at the current pose without preparing motion or publishing occupancy.</summary>
    public void RefreshSupport()
    {
        EnsureIdle();
        object lifetime = _context.ClockLifetime;
        long frame = _context.FrameCount;
        long spawn = _context.World.SpawnToken;
        _busy = true;
        try
        {
            NavigationMotionResult2D result = ReadAcceptedMotion(null);
            ChronicleGuard.ThrowIfTrue(_context.IsDisposed || !ReferenceEquals(lifetime, _context.ClockLifetime)
                || frame != _context.FrameCount || spawn != _context.World.SpawnToken,
                message: "The support sample belongs to a different frame or lifetime.");
            ValidateMotionResult(result);
            ChronicleGuard.ThrowIfArgument(result.Accepted.Center != Pose.Center
                || result.Accepted.RotationRadians != Pose.RotationRadians, nameof(result),
                "Support refresh must sample the controller's accepted pose.");
            bool grounded = IsSupported(result) && _verticalVelocity <= Fixed64.Zero;
            if (grounded)
                _verticalVelocity = Fixed64.Zero;
            PublishSupport(result, grounded);
        }
        finally { _busy = false; }
    }

    /// <summary>Reads the live pose of the exact carrier registration. A failed lookup suppresses carry.</summary>
    protected virtual bool TryReadCarrierPose(int stableId, long lifetime, out NavigationPose2D pose)
    {
        pose = default;
        return false;
    }

    private void ValidateInput(in NavigationInput2D input)
    {
        ChronicleGuard.ThrowIfArgument(input.Move is Vector2d move
            && Vector2d.CompareDistanceSquared(Vector2d.Zero, move, Vector2d.Zero, Vector2d.Right) > 0, nameof(input));
        ChronicleGuard.ThrowIfArgument(input.Facing == Vector2d.Zero, nameof(input));
        if (NavigationProfile.Mode == NavigationMode2D.SideView)
            ChronicleGuard.ThrowIfArgument(input.Move is Vector2d direction && direction.Y != Fixed64.Zero
                || input.Facing is Vector2d facing && facing.Y != Fixed64.Zero, nameof(input));
    }

    private NavigationPose2D PrepareMotorTarget(Vector2d center, in NavigationInput2D input, bool carrierSampled)
    {
        Vector2d intent = input.Facing ?? input.Move ?? (center - Pose.Center);
        if (NavigationProfile.Mode == NavigationMode2D.TopDown)
        {
            Fixed64 rotation = Pose.RotationRadians;
            if (intent != Vector2d.Zero && _motorSettings.TurnRateRadians > Fixed64.Zero)
            {
                Fixed64 angle = FixedMath.Atan2(intent.Y, intent.X);
                Fixed64 difference = new NavigationPose2D(default, angle - rotation).RotationRadians;
                Fixed64 limit = Multiply(_motorSettings.TurnRateRadians, _context.DeltaTime);
                rotation += FixedMath.Clamp(difference, -limit, limit);
            }
            return new NavigationPose2D(center, rotation);
        }

        if (intent.X != Fixed64.Zero)
            _preparedFacing = intent.X < Fixed64.Zero ? -Vector2d.Right : Vector2d.Right;
        if (_sideViewStagePrepared)
        {
            _preparedVertical = Fixed64.Zero;
            return new NavigationPose2D(center, Fixed64.Zero);
        }
        bool jump = CanStartJump(input);
        _preparedVertical = jump ? _motorSettings.JumpSpeed : _verticalVelocity;
        if (!IsGrounded || jump)
        {
            if (!Fixed64.TryMultiplyAdd(-_motorSettings.Gravity, _context.DeltaTime, _preparedVertical, out _preparedVertical))
                throw new OverflowException("The vertical motor speed is outside the representable plane.");
            center = Add(center, new Vector2d(Fixed64.Zero, Multiply(_preparedVertical, _context.DeltaTime)));
        }
        else
        {
            NavigationSupport2D support = Support!.Value;
            // Preserve speed along the walkable tangent rather than adding energy on slopes.
            Fixed64 travel = center.X - Pose.Center.X;
            center = Add(Pose.Center, new Vector2d(Multiply(travel, support.Normal.Y), Multiply(travel, -support.Normal.X)));
        }

        if (!carrierSampled)
            _preparedCarry = ReadCarrierDisplacement();
        if (_preparedCarry != Vector2d.Zero)
            center = Add(center, _preparedCarry);
        return new NavigationPose2D(center, Fixed64.Zero);
    }

    private Vector2d ReadCarrierDisplacement()
    {
        if (!IsGrounded || Support!.Value.Carrier is not NavigationCarrier2D carrier
            || !TryReadCarrierPose(carrier.StableId, carrier.Lifetime, out NavigationPose2D current)
            || (current.Center == carrier.Pose.Center && current.RotationRadians == carrier.Pose.RotationRadians))
            return Vector2d.Zero;
        if (!Vector2d.TryGetRelativeOffset(current.Center, carrier.LocalAnchor, current.RotationRadians,
            carrier.Pose.Center, carrier.LocalAnchor, carrier.Pose.RotationRadians, out var carry))
            throw new OverflowException("The carrier displacement is outside the representable plane.");
        return carry;
    }

    private bool CanStartJump(in NavigationInput2D input) => input.JumpRequested && !input.BlockJump && IsGrounded
        && (NavigationProfile.Capabilities & TraversalCapability.Jump) != 0 && _motorSettings.JumpSpeed > Fixed64.Zero;

    private Fixed64 ResolveMotorVertical(Fixed64 acceptedVerticalDisplacement)
    {
        if (NavigationProfile.Mode == NavigationMode2D.TopDown || _actionFrame)
            return Fixed64.Zero;
        if (!Fixed64.TrySubtract(acceptedVerticalDisplacement, _preparedCarry.Y, out Fixed64 voluntary))
            throw new OverflowException("The accepted motor displacement is outside the representable plane.");
        Fixed64 acceptedSpeed = Multiply(voluntary, _preparedInvDelta);
        // A clipped ceiling must stop ascent; host slope corrections must not become jump energy.
        if (_preparedVertical > Fixed64.Zero)
            return FixedMath.Clamp(acceptedSpeed, Fixed64.Zero, _preparedVertical);
        return _preparedVertical;
    }

    private void ValidateMotionResult(in NavigationMotionResult2D result)
    {
        result.Validate();
        ChronicleGuard.ThrowIfArgument(NavigationProfile.Mode == NavigationMode2D.TopDown
            ? result.SupportStatus != NavigationSupportStatus2D.NotApplicable
            : result.SupportStatus == NavigationSupportStatus2D.NotApplicable || result.Accepted.RotationRadians != Fixed64.Zero,
            nameof(result));
    }

    private bool IsSupported(in NavigationMotionResult2D result) =>
        result.Support is NavigationSupport2D support && support.Normal.Y >= NavigationProfile.MinimumSupportNormalDot;

    private void PublishSupport(in NavigationMotionResult2D result, bool grounded)
    {
        SupportStatus = result.SupportStatus;
        Support = result.Support;
        IsGrounded = grounded;
    }

    private static Fixed64 Multiply(Fixed64 value, Fixed64 factor)
    {
        if (!Fixed64.TryMultiplyAdd(value, factor, Fixed64.Zero, out Fixed64 result))
            throw new OverflowException("The motor target is outside the representable plane.");
        return result;
    }

    private static Vector2d Add(Vector2d left, Vector2d right)
    {
        if (!Vector2d.TryAdd(left, right, out Vector2d result))
            throw new OverflowException("The motor target is outside the representable plane.");
        return result;
    }
}

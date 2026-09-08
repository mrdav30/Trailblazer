using FixedMathSharp;
using System;
using Trailblazer.Navigation;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;

namespace Trailblazer.Tests.Navigation;

internal sealed class TestNavigator : Navigator
{
    private Fixed64? _acceptedTestSurfaceLevel;

    public bool AcceptPhysicalReplacement { get; set; } = true;

    public bool UseBasePhysicalReplacement { get; set; }

    public Exception? PhysicalReplacementNotificationException { get; set; }

    public int PhysicalReplacementCallCount { get; private set; }

    public NavigationAgentProfile LastPhysicalReplacementProfile { get; private set; }

    public Vector3d LastPhysicalReplacementRoot { get; private set; }

    public TestNavigator() { }

    public TestNavigator(TrailblazerWorldContext context)
        : base(context)
    {
    }

    public TrekRequest FrameRequest => _frameRequest;

    public TrekCondition FrameCondition => _frameCondition;

    public Vector3d RequestedFramePosition =>
        _position + _positionDelta + _locomotionDisplacement;

    public bool ResolveTurnDirection(
        TrekRequest request,
        out Vector3d turnDirection) =>
        TryGetTurnDirection(request, out turnDirection);

    public void SetTestSteering(NavSteering steering) => _steering = steering;

    public void SetTestPosition(Vector3d position, bool syncLastPosition = true)
    {
        _position = position;
        if (syncLastPosition)
            _lastPosition = position;
    }

    public void SetTestMotion(Vector3d velocity)
    {
        _velocity = velocity;
        _speed = velocity.Magnitude;
    }

    public void ResolveAcceptedFramePosition(Vector3d acceptedPosition)
    {
        _positionDelta = acceptedPosition - _position;
        _locomotionDisplacement = Vector3d.Zero;
    }

    public void ConstrainTestMotionToSurface(Fixed64? surfaceLevel) =>
        _acceptedTestSurfaceLevel = surfaceLevel;

    public void ConfigurePartialControllerShell(
        bool includeSteering,
        bool includeTurning,
        bool includeMotor)
    {
        if (!includeSteering)
            _steering = null;
        if (!includeTurning)
            _turning = null;
        if (!includeMotor)
            _motor = null;
    }

    public bool ApplyHeightmapGrounding(
        bool updateMotorState = false,
        Fixed64? surfaceFriction = null,
        MotionTransfer motionTransfer = MotionTransfer.None)
    {
        return TryApplyHeightmapGrounding(updateMotorState, surfaceFriction, motionTransfer);
    }

    public override void CheckTrekCondition()
    {
        if (_acceptedTestSurfaceLevel is not Fixed64 surfaceLevel)
            return;

        _position.Y = surfaceLevel + NavigationProfile.Shape.RootToFootOffsetY;
        SetGroundContact(surfaceLevel, Vector3d.Up);
    }

    protected override bool TryCommitPhysicalProfileReplacement(
        NavigationAgentProfile replacement,
        Vector3d replacementRootPosition,
        Action publishNavigationState,
        out Exception? notificationException)
    {
        if (UseBasePhysicalReplacement)
        {
            return base.TryCommitPhysicalProfileReplacement(
                replacement,
                replacementRootPosition,
                publishNavigationState,
                out notificationException);
        }

        notificationException = PhysicalReplacementNotificationException;
        PhysicalReplacementCallCount++;
        LastPhysicalReplacementProfile = replacement;
        LastPhysicalReplacementRoot = replacementRootPosition;
        if (AcceptPhysicalReplacement)
            publishNavigationState();
        return AcceptPhysicalReplacement;
    }
}

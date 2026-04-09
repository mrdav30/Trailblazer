using FixedMathSharp;

namespace Trailblazer.Navigation.Animation;

public static class NavAnimationUpdater
{
    private static readonly Fixed64 _minAxisV = Fixed64.FromRaw(0x40000000L); // 0.25
    private static readonly Fixed64 _maxAxisV = Fixed64.FromRaw(0x80000000L); // 0.5
    private static readonly Fixed64 _minAxisH = Fixed64.FromRaw(0x40000000L); // 0.25
    private static readonly Fixed64 _maxAxisH = Fixed64.FromRaw(0xB3333333L); // 0.7

    public static void UpdateAnimationParameters(
        INavAnimationHandler handler,
        Vector3d moveDirection,
        FixedQuaternion rotation,
        bool isLockedOn,
        bool isSprinting,
        Fixed64 dampTime)
    {
        Vector3d localMoveDirection = ResolveLocalMoveDirection(moveDirection, rotation);
        Fixed64 vertical = localMoveDirection.z;
        Fixed64 horizontal = localMoveDirection.x;
        Fixed64 moveAmount = FixedMath.Clamp01(horizontal.Abs() + vertical.Abs());

        (Fixed64 f, Fixed64 s) = isLockedOn && !isSprinting
            ? ResolveLockedOnInput(horizontal, vertical)
            : ResolveFreeMoveInput(moveAmount);

        handler.SetDirectionalInput(f, s, dampTime);
        handler.SetIsSprinting(isSprinting);
    }

    private static Vector3d ResolveLocalMoveDirection(Vector3d moveDirection, FixedQuaternion rotation)
    {
        if (moveDirection == Vector3d.Zero)
            return moveDirection;

        Fixed3x3 orientationMatrix = rotation.ToMatrix3x3();
        return Fixed3x3.InverseTransformDirection(orientationMatrix, moveDirection);
    }

    private static (Fixed64 forward, Fixed64 sideways) ResolveLockedOnInput(Fixed64 horizontal, Fixed64 vertical)
    {
        Fixed64 forward = ResolveLockedAxisMagnitude(vertical.Abs(), _minAxisV, _maxAxisV);
        Fixed64 sideways = ResolveLockedAxisMagnitude(horizontal.Abs(), _minAxisH, _maxAxisH);
        bool clampToSignedOne = forward == Fixed64.One || sideways == Fixed64.One;

        if (clampToSignedOne)
        {
            forward = vertical < Fixed64.Zero ? -forward : forward;
            sideways = horizontal < Fixed64.Zero ? -sideways : sideways;
        }

        return (forward, sideways);
    }

    private static (Fixed64 forward, Fixed64 sideways) ResolveFreeMoveInput(Fixed64 moveAmount)
    {
        if (moveAmount > Fixed64.Zero && moveAmount <= Fixed64.Half)
            return (Fixed64.Half, Fixed64.Zero);

        if (moveAmount > Fixed64.Half)
            return (Fixed64.One, Fixed64.Zero);

        return (Fixed64.Zero, Fixed64.Zero);
    }

    private static Fixed64 ResolveLockedAxisMagnitude(Fixed64 axisAmount, Fixed64 min, Fixed64 max)
    {
        if (axisAmount > min && axisAmount <= max)
            return Fixed64.Half;

        if (axisAmount > max)
            return Fixed64.One;

        return Fixed64.Zero;
    }
}

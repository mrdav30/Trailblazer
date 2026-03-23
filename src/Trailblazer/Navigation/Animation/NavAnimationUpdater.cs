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
        Vector3d localMoveDirection = moveDirection;
        if (moveDirection != Vector3d.Zero)
        {
            Fixed3x3 orientationMatrix = rotation.ToMatrix3x3();
            localMoveDirection = Fixed3x3.InverseTransformDirection(orientationMatrix, moveDirection);
        }

        Fixed64 vertical = localMoveDirection.z;
        Fixed64 horizontal = localMoveDirection.x;
        Fixed64 moveAmount = FixedMath.Clamp01(horizontal.Abs() + vertical.Abs());

        Fixed64 f = Fixed64.Zero;
        Fixed64 s = Fixed64.Zero;

        if (isLockedOn && !isSprinting)
        {
            bool fixToOne = false;

            if (vertical.Abs() > _minAxisV && vertical.Abs() <= _maxAxisV)
                f = Fixed64.Half;
            else if (vertical.Abs() > _maxAxisV)
            {
                f = Fixed64.One;
                fixToOne = true;
            }

            if (horizontal.Abs() > _minAxisH && horizontal.Abs() <= _maxAxisH)
                s = Fixed64.Half;
            else if (horizontal.Abs() > _maxAxisH)
            {
                s = Fixed64.One;
                fixToOne = true;
            }

            if (fixToOne)
            {
                f = vertical < Fixed64.Zero ? -f : f;
                s = horizontal < Fixed64.Zero ? -s : s;
            }
        }
        else
        {
            if (moveAmount > Fixed64.Zero && moveAmount <= Fixed64.Half)
                f = Fixed64.Half;
            else if (moveAmount > Fixed64.Half)
                f = Fixed64.One;
        }

        handler.SetDirectionalInput(f, s, dampTime);
        handler.SetIsSprinting(isSprinting);
    }
}

using FixedMathSharp;

namespace Trailblazer.Navigation.Animation;

/// <summary>
/// Provides static methods for updating navigation animation parameters based on movement direction, rotation, and character state.
/// </summary>
/// <remarks>
/// This class is intended for use in navigation animation systems where animation parameters must be
/// updated according to player input and character state, such as lock-on targeting or sprinting. 
/// All methods are static and thread safety is guaranteed as there is no shared mutable state.
/// </remarks>
public static class NavAnimationUpdater
{
    // TODO: Consider making these configurable via parameters or a settings object if different thresholds are needed for different characters or animations.
    private static readonly Fixed64 _minAxisV = Fixed64.FromRaw(0x40000000L); // 0.25
    private static readonly Fixed64 _maxAxisV = Fixed64.FromRaw(0x80000000L); // 0.5
    private static readonly Fixed64 _minAxisH = Fixed64.FromRaw(0x40000000L); // 0.25
    private static readonly Fixed64 _maxAxisH = Fixed64.FromRaw(0xB3333333L); // 0.7

    /// <summary>
    /// Updates the animation parameters on the specified animation handler based on 
    /// movement direction, rotation, lock-on state, sprinting state, and damping time.
    /// </summary>
    /// <param name="handler">The animation handler that receives the updated movement and state parameters. Cannot be null.</param>
    /// <param name="moveDirection">The world-space movement direction to be applied to the animation parameters.</param>
    /// <param name="rotation">The current rotation used to resolve movement direction relative to the character.</param>
    /// <param name="isLockedOn">true if the character is locked onto a target; otherwise, false. Affects how movement input is interpreted.</param>
    /// <param name="isSprinting">true if the character is sprinting; otherwise, false. Determines whether sprinting animation parameters are set.</param>
    /// <param name="dampTime">The time, in seconds, over which to smoothly interpolate the animation parameters.</param>
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

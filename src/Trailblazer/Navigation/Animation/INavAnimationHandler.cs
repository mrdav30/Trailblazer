using FixedMathSharp;

namespace Trailblazer.Navigation.Animation;

/// <summary>
/// Defines methods for handling navigation-related animation state and root motion in response to character movement and input.
/// </summary>
/// <remarks>
/// Implementations of this interface are responsible for updating animation parameters based on 
/// movement input, sprinting state, and root motion deltas. 
/// This interface is typically used in character controllers or navigation systems to synchronize animation with physical movement.
/// </remarks>
public interface INavAnimationHandler
{
    /// <summary>
    /// Sets the directional input values for forward and sideways movement, along with the damping time for input smoothing.
    /// </summary>
    /// <param name="forward">
    /// The input value representing movement in the forward direction. 
    /// Positive values indicate forward movement;
    /// negative values indicate backward movement.
    /// </param>
    /// <param name="sideways">
    /// The input value representing movement in the sideways direction. 
    /// Positive values indicate rightward movement;
    /// negative values indicate leftward movement.
    /// </param>
    /// <param name="dampTime">The time, in seconds, over which the input is smoothed or damped. Must be non-negative.</param>
    void SetDirectionalInput(Fixed64 forward, Fixed64 sideways, Fixed64 dampTime);

    /// <summary>
    /// Sets the sprinting state for the current entity.
    /// </summary>
    /// <param name="isSprinting">
    /// A value indicating whether the entity should be marked as sprinting. Set to <see langword="true"/> to enable
    /// sprinting; otherwise, <see langword="false"/>.
    /// </param>
    void SetIsSprinting(bool isSprinting);

    // TODO: Add methods for setting other animation parameters, such as jump, crouch, or attack states, if needed.

    /// <summary>
    /// Applies a root motion delta to the object, modifying its position based on the specified movement vector and force multiplier.
    /// </summary>
    /// <param name="deltaPosition">The movement vector representing the desired change in position to apply as root motion.</param>
    /// <param name="forceMultiplier">
    /// A scaling factor applied to the delta position to control the strength or influence of the root motion.
    /// Typically, values greater than 1 amplify the effect, while values between 0 and 1 reduce it.
    /// </param>
    void ApplyRootMotion(Vector3d deltaPosition, Fixed64 forceMultiplier);
}

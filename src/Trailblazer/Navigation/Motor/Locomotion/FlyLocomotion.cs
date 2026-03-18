using FixedMathSharp;
using Trailblazer.Serialization;
using Trailblazer.Support;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Handles controlled flight movement while the scout is airborne.
/// </summary>
/// <remarks>
/// Flight is distinct from both jumping and falling.
/// It provides active air control, optional gravity cancellation, and controlled ascent or descent.
/// </remarks>
public class FlyLocomotion : ILocomotion
{
    #region Constants

    /// <summary>
    /// Default maximum horizontal flight speed.
    /// </summary>
    public static readonly Fixed64 DefaultMaxFlySpeed = (Fixed64)1.5d;

    /// <summary>
    /// Default maximum upward flight speed.
    /// </summary>
    public static readonly Fixed64 DefaultMaxAscendSpeed = (Fixed64)1.5d;

    /// <summary>
    /// Default maximum downward flight speed while actively descending.
    /// </summary>
    public static readonly Fixed64 DefaultMaxDescendSpeed = (Fixed64)1.5d;

    /// <summary>
    /// Default acceleration used while actively flying.
    /// </summary>
    public static readonly Fixed64 DefaultMaxFlyAcceleration = (Fixed64)20;

    /// <summary>
    /// Default amount of gravity canceled while actively flying.
    /// </summary>
    public static readonly Fixed64 DefaultGravityCompensation = Fixed64.One;

    #endregion

    #region Configuration State

    private bool _isEnabled = true;

    /// <summary>
    /// Determines whether the scout is allowed to enter controlled flight.
    /// </summary>
    public bool CanFly = true;

    /// <summary>
    /// The maximum horizontal speed while flying.
    /// </summary>
    public Fixed64 MaxFlySpeed = DefaultMaxFlySpeed;

    /// <summary>
    /// The maximum upward speed while flying.
    /// </summary>
    public Fixed64 MaxAscendSpeed = DefaultMaxAscendSpeed;

    /// <summary>
    /// The maximum downward speed while actively descending under flight control.
    /// </summary>
    public Fixed64 MaxDescendSpeed = DefaultMaxDescendSpeed;

    /// <summary>
    /// The maximum acceleration applied while steering in flight.
    /// </summary>
    public Fixed64 MaxFlyAcceleration = DefaultMaxFlyAcceleration;

    /// <summary>
    /// The amount of gravity canceled while flying, clamped between 0 and 1 by the motor.
    /// </summary>
    public Fixed64 GravityCompensation = DefaultGravityCompensation;

    #endregion

    #region Transient State

    /// <inheritdoc cref="ILocomotion.IsEnabled"/>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            if (!_isEnabled)
                this.ClearTransientState();
        }
    }

    /// <summary>
    /// Indicates whether controlled flight is currently active.
    /// </summary>
    [Transient]
    public bool IsFlying { get; set; }

    #endregion

    /// <inheritdoc />
    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref _isEnabled, "isEnabled", true);
        RecordValues.Look(chronicler, ref CanFly, "canFly", true);
        RecordValues.Look(chronicler, ref MaxFlySpeed, "maxFlySpeed", DefaultMaxFlySpeed);
        RecordValues.Look(chronicler, ref MaxAscendSpeed, "maxAscendSpeed", DefaultMaxAscendSpeed);
        RecordValues.Look(chronicler, ref MaxDescendSpeed, "maxDescendSpeed", DefaultMaxDescendSpeed);
        RecordValues.Look(chronicler, ref MaxFlyAcceleration, "maxFlyAcceleration", DefaultMaxFlyAcceleration);
        RecordValues.Look(chronicler, ref GravityCompensation, "gravityCompensation", DefaultGravityCompensation);

        bool isFlying = IsFlying;
        RecordValues.Look(chronicler, ref isFlying, "isFlying", false);

        if (chronicler.Mode == SerializationMode.Loading)
        {
            IsFlying = isFlying;

            if (!_isEnabled)
                this.ClearTransientState();
        }
    }
}

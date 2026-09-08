//=======================================================================
// FallLocomotion.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using Chronicler;
using FixedMathSharp;
using Trailblazer.Support;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Handles the scout’s behavior when falling, including tracking fall distance and applying movement constraints.
/// </summary>
public class FallLocomotion : ILocomotion
{
    #region Constants

    /// <summary>
    /// The default maximum height a scout can fall before a fatal impact occurs.
    /// </summary>
    public static readonly Fixed64 DefaultMaxFallHeight = (Fixed64)30;

    /// <summary>
    /// Default movement control multiplier when falling.
    /// Reduces movement responsiveness to simulate loss of control while airborne.
    /// </summary>
    public static readonly Fixed64 DefaultFallControlMultiplier = Fixed64.FromFraction(3, 16); // 50% control when falling

    /// <summary>
    /// Default maximum downward speed for an active controlled descent.
    /// </summary>
    public static readonly Fixed64 DefaultControlledDescentTerminalSpeed = Fixed64.FromFraction(3, 2);

    /// <summary>
    /// Default maximum reduction in downward speed per second for controlled descent.
    /// </summary>
    public static readonly Fixed64 DefaultControlledDescentDeceleration = (Fixed64)20;

    /// <summary>
    /// Default lateral-control multiplier while controlled descent is active.
    /// </summary>
    public static readonly Fixed64 DefaultControlledDescentControlMultiplier = Fixed64.FromFraction(3, 8);

    #endregion

    #region Configuration State

    /// <summary>
    /// Determines whether falling mechanics are enabled.
    /// If disabled, the scout will not experience fall behavior.
    /// </summary>
    private bool _isEnabled = true;

    /// <summary>
    /// The maximum allowable fall height before the scout reaches a critical threshold (e.g., death or heavy impact).
    /// </summary>
    public Fixed64 MaxFallHeight = DefaultMaxFallHeight;

    /// <summary>
    /// A multiplier controlling how much movement input affects the scout while falling.
    /// Lower values reduce movement responsiveness.
    /// </summary>
    public Fixed64 FallControlMultiplier = DefaultFallControlMultiplier;

    /// <summary>
    /// Determines whether this falling scout may enter controlled descent when requested.
    /// </summary>
    public bool CanControlDescent;

    /// <summary>
    /// The positive magnitude of the maximum downward speed while controlled descent is active.
    /// </summary>
    public Fixed64 ControlledDescentTerminalSpeed = DefaultControlledDescentTerminalSpeed;

    /// <summary>
    /// The maximum speed reduction per second while a too-fast fall approaches the controlled-descent terminal speed.
    /// </summary>
    public Fixed64 ControlledDescentDeceleration = DefaultControlledDescentDeceleration;

    /// <summary>
    /// The lateral input multiplier while controlled descent is active.
    /// The motor clamps this value to the inclusive range from zero to one.
    /// </summary>
    public Fixed64 ControlledDescentControlMultiplier = DefaultControlledDescentControlMultiplier;

    #endregion

    #region Transient State

    /// <inheritdoc cref="ILocomotion.IsEnabled"/>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            _isEnabled = value;
            if (!value)
                this.ClearTransientState();
        }
    }

    /// <summary>
    /// Indicates whether the scout is currently falling.
    /// </summary>
    [Transient]
    public bool IsFalling { get; set; }

    /// <summary>
    /// Indicates whether fall-preserving controlled descent is currently active.
    /// </summary>
    [Transient]
    public bool IsControlledDescending { get; set; }

    /// <summary>
    /// The vertical position where the scout started falling.
    /// </summary>
    [Transient]
    public Fixed64 FallStart { get; set; }

    /// <summary>
    /// The vertical position where the scout landed.
    /// </summary>
    [Transient]
    public Fixed64 FallEnd { get; set; }

    /// <summary>
    /// The total distance fallen, calculated as the difference between <see cref="FallStart"/> and <see cref="FallEnd"/>.
    /// </summary>
    public Fixed64 FallHeight => FallStart - FallEnd;

    #endregion

    /// <inheritdoc />
    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref _isEnabled, "IsEnabled", true);
        RecordValues.Look(chronicler, ref MaxFallHeight, "MaxFallHeight", DefaultMaxFallHeight);
        RecordValues.Look(chronicler, ref FallControlMultiplier, "FallControlMultiplier", DefaultFallControlMultiplier);
        RecordValues.Look(chronicler, ref CanControlDescent, "CanControlDescent", false);
        RecordValues.Look(chronicler, ref ControlledDescentTerminalSpeed, "ControlledDescentTerminalSpeed", DefaultControlledDescentTerminalSpeed);
        RecordValues.Look(chronicler, ref ControlledDescentDeceleration, "ControlledDescentDeceleration", DefaultControlledDescentDeceleration);
        RecordValues.Look(chronicler, ref ControlledDescentControlMultiplier, "ControlledDescentControlMultiplier", DefaultControlledDescentControlMultiplier);

        bool isFalling = IsFalling;
        bool isControlledDescending = IsControlledDescending;
        Fixed64 fallStart = FallStart;
        Fixed64 fallEnd = FallEnd;

        RecordValues.Look(chronicler, ref isFalling, "IsFalling", false);
        RecordValues.Look(chronicler, ref isControlledDescending, "IsControlledDescending", false);
        RecordValues.Look(chronicler, ref fallStart, "FallStart", Fixed64.Zero);
        RecordValues.Look(chronicler, ref fallEnd, "FallEnd", Fixed64.Zero);

        if (chronicler.Mode == SerializationMode.Loading)
        {
            IsFalling = isFalling;
            IsControlledDescending = isControlledDescending;
            FallStart = fallStart;
            FallEnd = fallEnd;

            if (!_isEnabled)
                this.ClearTransientState();
            else if (!IsFalling
                || !CanControlDescent
                || ControlledDescentTerminalSpeed <= Fixed64.Zero
                || ControlledDescentDeceleration <= Fixed64.Zero)
            {
                IsControlledDescending = false;
            }
        }
    }
}

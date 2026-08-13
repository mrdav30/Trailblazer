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

        bool isFalling = IsFalling;
        Fixed64 fallStart = FallStart;
        Fixed64 fallEnd = FallEnd;

        RecordValues.Look(chronicler, ref isFalling, "IsFalling", false);
        RecordValues.Look(chronicler, ref fallStart, "FallStart", Fixed64.Zero);
        RecordValues.Look(chronicler, ref fallEnd, "FallEnd", Fixed64.Zero);

        if (chronicler.Mode == SerializationMode.Loading)
        {
            IsFalling = isFalling;
            FallStart = fallStart;
            FallEnd = fallEnd;

            if (!_isEnabled)
                this.ClearTransientState();
        }
    }
}

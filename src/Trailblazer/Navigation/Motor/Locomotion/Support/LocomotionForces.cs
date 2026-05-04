using Chronicler;
using FixedMathSharp;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Holds the gravity and terminal velocity forces applied to a object, with support for per-instance overrides and global defaults.
/// </summary>
public sealed class LocomotionForces : IRecordable
{
    /// <summary>
    /// Simulation-wide gravity defaults applied to all <see cref="MoveLocomotion"/> instances
    /// that do not carry a per-instance override.
    /// </summary>
    /// <remarks>
    /// Assign fields on this instance to shift gravity for every unoverridden object at once.
    /// Call <see cref="GlobalEnviromentForces.Reset"/> to restore the defaults.
    /// </remarks>
    public static readonly GlobalEnviromentForces GlobalForces = new();

    #region Configurable Parameters

    /// <summary>
    /// The gravity force applied to this object.
    /// Returns the per-instance override when one is set;
    /// otherwise delegates to <see cref="GlobalEnviromentForces"/> so a single global change takes effect for all unoverridden navigators simultaneously.
    /// Assigning this property sets the per-instance override.
    /// Call <see cref="ClearGravityForceOverride"/> to remove the override and restore global tracking.
    /// </summary>
    public Fixed64 GravityForce
    {
        get => _gravityForceOverride ?? GlobalForces.GravityForce;
        set => _gravityForceOverride = value;
    }

    private Fixed64? _gravityForceOverride;

    /// <summary>
    /// Returns true when this instance carries a per-instance gravity override.
    /// When false, <see cref="GravityForce"/> follows <see cref="GlobalEnviromentForces"/>.
    /// </summary>
    public bool HasGravityForceOverride => _gravityForceOverride.HasValue;


    /// <summary>
    /// The terminal fall velocity cap for this object.
    /// Returns the per-instance override when one is set; otherwise delegates to
    /// <see cref="GlobalEnviromentForces"/>.
    /// Assigning this property sets the per-instance override.
    /// Call <see cref="ClearTerminalVelocityOverride"/> to remove the override and restore global tracking.
    /// </summary>
    public Fixed64 TerminalVelocity
    {
        get => _terminalVelocityOverride ?? GlobalForces.TerminalVelocity;
        set => _terminalVelocityOverride = value;
    }

    private Fixed64? _terminalVelocityOverride;

    /// <summary>
    /// Returns true when this instance carries a per-instance terminal velocity override.
    /// When false, <see cref="TerminalVelocity"/> follows <see cref="GlobalEnviromentForces"/>.
    /// </summary>
    public bool HasTerminalVelocityOverride => _terminalVelocityOverride.HasValue;

    #endregion

    #region Overrides

    /// <summary>
    /// Removes the per-instance gravity override so this object tracks
    /// <see cref="GlobalEnviromentForces"/>.
    /// </summary>
    public void ClearGravityForceOverride() => _gravityForceOverride = null;

    /// <summary>
    /// Removes the per-instance terminal velocity override so this object tracks
    /// <see cref="GlobalEnviromentForces"/>.
    /// </summary>
    public void ClearTerminalVelocityOverride() => _terminalVelocityOverride = null;

    #endregion

    #region Serialization

    /// <inheritdoc />
    public void RecordData(IChronicler chronicler)
    {
        // Gravity and terminal velocity are stored as per-instance overrides.
        // Serialize the presence flag first so loading knows whether to restore an override or clear it.
        bool hasGravityOverride = _gravityForceOverride.HasValue;
        Fixed64 gravityForce = _gravityForceOverride ?? GlobalForces.GravityForce;
        RecordValues.Look(chronicler, ref hasGravityOverride, "HasGravityOverride", false);
        RecordValues.Look(chronicler, ref gravityForce, "GravityForce", GlobalForces.GravityForce);

        bool hasTerminalVelocityOverride = _terminalVelocityOverride.HasValue;
        Fixed64 terminalVelocity = _terminalVelocityOverride ?? GlobalForces.TerminalVelocity;
        RecordValues.Look(chronicler, ref hasTerminalVelocityOverride, "HasTerminalVelocityOverride", false);
        RecordValues.Look(chronicler, ref terminalVelocity, "TerminalVelocity", GlobalForces.TerminalVelocity);

        if (chronicler.Mode == SerializationMode.Loading)
        {
            _gravityForceOverride = hasGravityOverride ? gravityForce : null;
            _terminalVelocityOverride = hasTerminalVelocityOverride ? terminalVelocity : null;
        }
    }

    #endregion
}

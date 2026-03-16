using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Trailblazer.Serialization;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Manages locomotion states and behaviors for the <see cref="NavMotor"/>.
/// </summary>
/// <remarks>
/// This class coordinates multiple locomotion types, ensuring that movement states are properly managed.
/// </remarks>
public class LocomotionHandler : IRecordable
{
    /// <summary>
    /// Determines whether the scout has control over movement input.
    /// </summary>
    public bool IsInControl = true;

    #region Locomotions

    /// <summary>
    /// Handles general movement, including speed limits, acceleration, and velocity calculations.
    /// </summary>
    public MoveLocomotion Move { get; private set; } = new();

    /// <summary>
    /// Manages movement when interacting with moving platforms or surfaces.
    /// </summary>
    /// <remarks>
    /// This locomotion maintains platform velocity tracking and movement transfer states.
    /// </remarks>
    public PlatformLocomotion Platform { get; private set; } = new();

    /// <summary>
    /// Controls the airborne state when a jump is executed successfully.
    /// </summary>
    /// <remarks>
    /// This locomotion governs jump height, cooldown timing, and jump force calculations.
    /// </remarks>
    public JumpLocomotion Jump { get; private set; } = new();

    /// <summary>
    /// Handles the scout’s falling behavior when downward momentum is detected.
    /// </summary>
    /// <remarks>
    /// This locomotion tracks fall distance, applies landing impact logic, and determines if a scout is free-falling.
    /// </remarks>
    public FallLocomotion Fall { get; private set; } = new();

    /// <summary>
    /// Manages movement when sliding down steep surfaces.
    /// </summary>
    /// <remarks>
    /// This locomotion determines when the scout should slide and how much control it has over movement during the slide.
    /// </remarks>
    public SlideLocomotion Slide { get; private set; } = new();

    /// <summary>
    /// Handles movement when the scout is in water, including buoyancy and water resistance.
    /// </summary>
    /// <remarks>
    /// This locomotion tracks swim speed, dive time, and breath management.
    /// </remarks>
    public SwimLocomotion Swim { get; private set; } = new();

    #endregion

    /// <summary>
    /// Synchronizes locomotion states with another <see cref="LocomotionHandler"/> instance.
    /// </summary>
    /// <remarks>
    /// This ensures that all locomotion modules maintain consistent movement behavior when synchronizing states,
    /// which is useful for rollback systems or deterministic simulations.
    /// </remarks>
    /// <param name="other">The locomotion handler instance to sync with.</param>
    public void SyncState(LocomotionHandler other)
    {
        if (other == null) return;

        IsInControl = other.IsInControl;

        foreach (var locomotion in GetLocomotions())
        {
            if (locomotion.IsEnabled)
            {
                ITransientLocomotion otherLocomotion = other.GetLocomotion(locomotion.GetType());
                if (otherLocomotion == null) continue;
                locomotion.SyncTransientState(otherLocomotion);
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearState<T>() where T : ITransientLocomotion
    {
        var locomotion = GetLocomotion(typeof(T));
        if (locomotion != null && locomotion.IsEnabled)
            locomotion.ClearTransientState();
    }

    /// <summary>
    /// Clears the transient state of all locomotion modules.
    /// </summary>
    /// <remarks>
    /// This method resets movement states without altering locomotion configurations,
    /// ensuring a clean reset of position, velocity, and state-based properties.
    /// </remarks>
    public void ClearStateAll()
    {
        foreach (var locomotion in GetLocomotions())
        {
            if (locomotion.IsEnabled)
                locomotion.ClearTransientState();
        }
    }

    /// <summary>
    /// Gets all locomotion instances in the handler.
    /// </summary>
    public IEnumerable<ITransientLocomotion> GetLocomotions()
    {
        yield return Move;
        yield return Platform;
        yield return Jump;
        yield return Fall;
        yield return Swim;
        yield return Slide;
    }

    /// <summary>
    /// Retrieves a locomotion instance of a specific type from the handler.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ITransientLocomotion GetLocomotion(Type type)
    {
        return GetLocomotions().FirstOrDefault(l => l.GetType() == type);
    }

    /// <inheritdoc />
    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref IsInControl, "isInControl", IsInControl);

        MoveLocomotion move = Move;
        PlatformLocomotion platform = Platform;
        JumpLocomotion jump = Jump;
        FallLocomotion fall = Fall;
        SlideLocomotion slide = Slide;
        SwimLocomotion swim = Swim;

        RecordDeep.Look(chronicler, ref move, "move");
        RecordDeep.Look(chronicler, ref platform, "platform");
        RecordDeep.Look(chronicler, ref jump, "jump");
        RecordDeep.Look(chronicler, ref fall, "fall");
        RecordDeep.Look(chronicler, ref slide, "slide");
        RecordDeep.Look(chronicler, ref swim, "swim");

        if (chronicler.Mode == SerializationMode.Loading)
        {
            Move = move;
            Platform = platform;
            Jump = jump;
            Fall = fall;
            Slide = slide;
            Swim = swim;
        }
    }
}

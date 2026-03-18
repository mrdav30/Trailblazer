using SwiftCollections;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Trailblazer.Serialization;
using Trailblazer.Support;

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
    /// Initializes a new handler using the default locomotion profile.
    /// </summary>
    public LocomotionHandler()
        : this(LocomotionProfile.CreateDefault()) { }

    /// <summary>
    /// Initializes a new handler from a locomotion profile.
    /// </summary>
    public LocomotionHandler(LocomotionProfile profile)
    {
        ApplyProfile(profile ?? throw new ArgumentNullException(nameof(profile)));
    }

    /// <summary>
    /// Determines whether the scout has control over movement input.
    /// </summary>
    public bool IsInControl = true;

    /// <summary>
    /// Gets the currently installed locomotion kinds.
    /// </summary>
    public LocomotionKind InstalledKinds { get; private set; } = LocomotionKind.All;

    #region Locomotions

    /// <summary>
    /// Handles general movement, including speed limits, acceleration, and velocity calculations.
    /// </summary>
    public MoveLocomotion Move { get; private set; }

    /// <summary>
    /// Manages movement when interacting with moving platforms or surfaces.
    /// </summary>
    /// <remarks>
    /// This locomotion maintains platform velocity tracking and movement transfer states.
    /// </remarks>
    public PlatformLocomotion Platform { get; private set; }

    /// <summary>
    /// Controls the airborne state when a jump is executed successfully.
    /// </summary>
    /// <remarks>
    /// This locomotion governs jump height, cooldown timing, and jump force calculations.
    /// </remarks>
    public JumpLocomotion Jump { get; private set; }

    /// <summary>
    /// Handles the scout’s falling behavior when downward momentum is detected.
    /// </summary>
    /// <remarks>
    /// This locomotion tracks fall distance, applies landing impact logic, and determines if a scout is free-falling.
    /// </remarks>
    public FallLocomotion Fall { get; private set; }

    /// <summary>
    /// Manages movement when sliding down steep surfaces.
    /// </summary>
    /// <remarks>
    /// This locomotion determines when the scout should slide and how much control it has over movement during the slide.
    /// </remarks>
    public SlideLocomotion Slide { get; private set; }

    /// <summary>
    /// Handles movement when the scout is in water, including buoyancy and water resistance.
    /// </summary>
    /// <remarks>
    /// This locomotion tracks swim speed, dive time, and breath management.
    /// </remarks>
    public SwimLocomotion Swim { get; private set; }

    #endregion

    #region Composition

    /// <summary>
    /// Gets whether a built-in locomotion kind is installed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has(LocomotionKind kind)
    {
        return (InstalledKinds & kind) == kind;
    }

    /// <summary>
    /// Gets whether a built-in locomotion type is installed.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has<T>() where T : class, ILocomotion
    {
        return TryGet<T>(out _);
    }

    /// <summary>
    /// Attempts to retrieve an installed locomotion by type.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet<T>(out T locomotion) where T : class, ILocomotion
    {
        locomotion = GetLocomotion(typeof(T)) as T;
        return locomotion != null;
    }

    /// <summary>
    /// Retrieves an installed locomotion by type or throws if it is not installed.
    /// </summary>
    public T Require<T>() where T : class, ILocomotion
    {
        if (TryGet<T>(out T locomotion))
            return locomotion;

        throw new InvalidOperationException($"{typeof(T).Name} is not installed on this locomotion handler.");
    }

    /// <summary>
    /// Installs or replaces a locomotion instance.
    /// </summary>
    public void Install<T>(T locomotion) where T : class, ILocomotion
    {
        Replace(locomotion);
    }

    /// <summary>
    /// Replaces a locomotion instance with a new one of the same type.
    /// </summary>
    public void Replace<T>(T locomotion) where T : class, ILocomotion
    {
        if (locomotion == null)
            ThrowHelper.ThrowArgumentNullException(nameof(locomotion));

        SetLocomotion(typeof(T), locomotion);
        RefreshInstalledKinds();
    }

    /// <summary>
    /// Removes an optional locomotion from the handler.
    /// </summary>
    public bool Remove<T>() where T : class, ILocomotion
    {
        Type type = typeof(T);
        if (type == typeof(MoveLocomotion) || type == typeof(FallLocomotion))
            return false;

        ILocomotion locomotion = GetLocomotion(type);
        if (locomotion == null)
            return false;

        if (locomotion is ITransient transient)
            transient.ClearTransientState();

        SetLocomotion(type, null);
        RefreshInstalledKinds();
        return true;
    }

    /// <summary>
    /// Replaces the installed locomotions with a new profile.
    /// </summary>
    public void ApplyProfile(LocomotionProfile profile)
    {
        if (profile == null)
            ThrowHelper.ThrowArgumentNullException(nameof(profile));

        ClearReplacedLocomotion(Move, profile.Move);
        ClearReplacedLocomotion(Fall, profile.Fall);
        ClearReplacedLocomotion(Platform, profile.Platform);
        ClearReplacedLocomotion(Jump, profile.Jump);
        ClearReplacedLocomotion(Slide, profile.Slide);
        ClearReplacedLocomotion(Swim, profile.Swim);

        Move = profile.Move ?? throw new InvalidOperationException("Move locomotion is required.");
        Fall = profile.Fall ?? throw new InvalidOperationException("Fall locomotion is required.");
        Platform = profile.Platform;
        Jump = profile.Jump;
        Slide = profile.Slide;
        Swim = profile.Swim;

        RefreshInstalledKinds();
    }

    /// <summary>
    /// Creates a profile representing the handler's current locomotion composition.
    /// </summary>
    public LocomotionProfile ToProfile()
    {
        return new LocomotionProfile(
            Move,
            Fall,
            Platform,
            Jump,
            Slide,
            Swim);
    }

    internal void ConfigureInstalledKinds(LocomotionKind kinds)
    {
        LocomotionKind normalizedKinds = kinds | LocomotionKind.Core;
        var builder = new LocomotionProfileBuilder(includeOptionalLocomotions: false);

        if ((normalizedKinds & LocomotionKind.Platform) != 0)
            builder.WithPlatform();

        if ((normalizedKinds & LocomotionKind.Jump) != 0)
            builder.WithJump();

        if ((normalizedKinds & LocomotionKind.Slide) != 0)
            builder.WithSlide();

        if ((normalizedKinds & LocomotionKind.Swim) != 0)
            builder.WithSwim();

        ApplyProfile(builder.Build());
    }

    private void RefreshInstalledKinds()
    {
        InstalledKinds = LocomotionKind.Core;

        if (Platform != null)
            InstalledKinds |= LocomotionKind.Platform;

        if (Jump != null)
            InstalledKinds |= LocomotionKind.Jump;

        if (Slide != null)
            InstalledKinds |= LocomotionKind.Slide;

        if (Swim != null)
            InstalledKinds |= LocomotionKind.Swim;
    }

    private ILocomotion GetLocomotion(Type type)
    {
        return type.Name switch
        {
            nameof(MoveLocomotion) => Move,
            nameof(PlatformLocomotion) => Platform,
            nameof(JumpLocomotion) => Jump,
            nameof(FallLocomotion) => Fall,
            nameof(SlideLocomotion) => Slide,
            nameof(SwimLocomotion) => Swim,
            _ => null,
        };
    }

    private void SetLocomotion(Type type, ILocomotion locomotion)
    {
        switch (type.Name)
        {
            case nameof(MoveLocomotion):
                {
                    Move = locomotion as MoveLocomotion
                        ?? throw new InvalidOperationException("Move locomotion cannot be removed.");
                    return;
                }
            case nameof(PlatformLocomotion):
                {
                    Platform = locomotion as PlatformLocomotion;
                    return;
                }
            case nameof(JumpLocomotion):
                {
                    Jump = locomotion as JumpLocomotion;
                    return;
                }
            case nameof(FallLocomotion):
                {
                    Fall = locomotion as FallLocomotion
                        ?? throw new InvalidOperationException("Fall locomotion cannot be removed.");
                    return;
                }
            case nameof(SlideLocomotion):
                {
                    Slide = locomotion as SlideLocomotion;
                    return;
                }
            case nameof(SwimLocomotion):
                {
                    Swim = locomotion as SwimLocomotion;
                    return;
                }
            default:
                throw new NotSupportedException($"Unsupported locomotion type '{type.Name}'.");
        }
    }

    private static void ClearReplacedLocomotion(ILocomotion current, ILocomotion next)
    {
        if (ReferenceEquals(current, next) || current is not ITransient transient)
            return;

        transient.ClearTransientState();
    }

    #endregion

    #region Transient State Management

    /// <summary>
    /// Synchronizes locomotion states with another <see cref="LocomotionHandler"/> instance.
    /// </summary>
    /// <remarks>
    /// This ensures that all locomotion modules maintain consistent movement behavior when synchronizing states,
    /// which is useful for rollback systems or deterministic simulations.
    /// </remarks>
    /// <param name="other">The locomotion handler instance to sync with.</param>
    public void SyncTransientState(LocomotionHandler other)
    {
        if (other == null) return;

        IsInControl = other.IsInControl;

        foreach (var locomotion in GetTransientLocomotions())
        {
            if (!locomotion.IsEnabled) continue;
            ITransientLocomotion otherLocomotion = other.GetTransientLocomotion(locomotion.GetType());
            if (otherLocomotion == null) continue;
            locomotion.SyncTransientState(otherLocomotion);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearTransientState<T>() where T : ITransientLocomotion
    {
        var locomotion = GetTransientLocomotion(typeof(T));
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
    public void ClearAllTransientState()
    {
        foreach (var locomotion in GetTransientLocomotions())
        {
            if (locomotion.IsEnabled)
                locomotion.ClearTransientState();
        }
    }

    /// <summary>
    /// Gets all locomotion instances in the handler.
    /// </summary>
    public IEnumerable<ITransientLocomotion> GetTransientLocomotions()
    {
        yield return Move;
        if (Platform != null)
            yield return Platform;

        if (Jump != null)
            yield return Jump;

        yield return Fall;

        if (Swim != null)
            yield return Swim;

        if (Slide != null)
            yield return Slide;
    }

    /// <summary>
    /// Retrieves a locomotion instance of a specific type from the handler.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ITransientLocomotion GetTransientLocomotion(Type type)
    {
        return GetLocomotion(type) as ITransientLocomotion;
    }

    #endregion

    #region Serialization

    /// <inheritdoc />
    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref IsInControl, "isInControl", true);
        int installedKinds = (int)InstalledKinds;
        RecordValues.Look(chronicler, ref installedKinds, "installedKinds", (int)LocomotionKind.All);

        if (chronicler.Mode == SerializationMode.Loading)
            ConfigureInstalledKinds((LocomotionKind)installedKinds);

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
            ApplyProfile(new LocomotionProfile(
                move,
                fall,
                platform,
                jump,
                slide,
                swim));
        }
    }

    #endregion
}

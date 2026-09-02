//=======================================================================
// LocomotionHandler.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Chronicler;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Manages locomotion states and behaviors for the <see cref="NavMotor"/>.
/// </summary>
/// <remarks>
/// This class coordinates multiple locomotion types, ensuring that movement states are properly managed.
/// </remarks>
public class LocomotionHandler : IRecordable
{
    #region Nested Types

    private enum LocomotionSlot
    {
        Move,
        Platform,
        Jump,
        Fall,
        Slide,
        Water,
        Fly,
        Climb
    }

    #endregion

    #region Fields

    /// <summary>
    /// Determines whether the scout has control over movement input.
    /// </summary>
    public bool IsInControl = true;

    private LocomotionForces _forces = new();

    private MoveLocomotion _move = null!;

    private FallLocomotion _fall = null!;

    private PlatformLocomotion _platform = null!;

    private JumpLocomotion? _jump;

    private SlideLocomotion? _slide;

    private WaterLocomotion? _water;

    private FlyLocomotion? _fly;

    private ClimbLocomotion? _climb;

    private TrailblazerWorldContext? _context;

    #endregion

    #region Initialization

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

    #endregion

    #region Properties

    /// <summary>
    /// Gets the currently installed locomotion kinds.
    /// </summary>
    public LocomotionKind InstalledKinds { get; private set; } = LocomotionKind.All;

    /// <inheritdoc cref="LocomotionForces"/>
    public LocomotionForces Forces => _forces;

    /// <summary>
    /// Handles general movement, including speed limits, acceleration, and velocity calculations.
    /// </summary>
    public MoveLocomotion Move => _move;

    /// <summary>
    /// Manages movement when interacting with moving platforms or surfaces.
    /// </summary>
    /// <remarks>
    /// This locomotion maintains platform velocity tracking and movement transfer states.
    /// </remarks>
    public PlatformLocomotion Platform => _platform;

    /// <summary>
    /// Controls the airborne state when a jump is executed successfully.
    /// </summary>
    /// <remarks>
    /// This locomotion governs jump height, cooldown timing, and jump force calculations.
    /// </remarks>
    public JumpLocomotion? Jump => _jump;

    /// <summary>
    /// Handles the scout’s falling behavior when downward momentum is detected.
    /// </summary>
    /// <remarks>
    /// This locomotion tracks fall distance, applies landing impact logic, and determines if a scout is free-falling.
    /// </remarks>
    public FallLocomotion Fall => _fall;

    /// <summary>
    /// Manages movement when sliding down steep surfaces.
    /// </summary>
    /// <remarks>
    /// This locomotion determines when the scout should slide and how much control it has over movement during the slide.
    /// </remarks>
    public SlideLocomotion? Slide => _slide;

    /// <summary>
    /// Handles movement when the scout is in water, including active swimming, buoyancy, and water resistance.
    /// </summary>
    /// <remarks>
    /// This locomotion tracks liquid-medium state such as swim speed, floating and sinking behavior,
    /// dive time, and breath management.
    /// </remarks>
    public WaterLocomotion? Water => _water;

    /// <summary>
    /// Handles controlled flight while the scout is airborne.
    /// </summary>
    public FlyLocomotion? Fly => _fly;

    /// <summary>
    /// Handles climb configuration and runtime attachment state.
    /// </summary>
    public ClimbLocomotion? Climb => _climb;

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
    public bool TryGet<T>([NotNullWhen(true)] out T? locomotion) where T : class, ILocomotion
    {
        locomotion = GetLocomotion(typeof(T)) as T;
        return locomotion != null;
    }

    /// <summary>
    /// Retrieves an installed locomotion by type or throws if it is not installed.
    /// </summary>
    public T Require<T>() where T : class, ILocomotion
    {
        if (TryGet<T>(out T? locomotion))
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
        SwiftThrowHelper.ThrowIfNull(locomotion, nameof(locomotion));

        SetLocomotion(typeof(T), locomotion);
        RefreshInstalledKinds();
    }

    /// <summary>
    /// Removes an optional locomotion from the handler.
    /// </summary>
    public bool Remove<T>() where T : class, ILocomotion
    {
        Type type = typeof(T);
        if (type == typeof(MoveLocomotion)
            || type == typeof(PlatformLocomotion)
            || type == typeof(FallLocomotion))
        {
            return false;
        }

        ILocomotion? locomotion = GetLocomotion(type);
        if (locomotion == null)
            return false;

        locomotion.ClearTransientState();

        SetLocomotion(type, null);
        RefreshInstalledKinds();
        return true;
    }

    /// <summary>
    /// Replaces the installed locomotions with a new profile.
    /// </summary>
    public void ApplyProfile(LocomotionProfile profile)
    {
        SwiftThrowHelper.ThrowIfNull(profile, nameof(profile));

        ClearReplacedLocomotion(Move, profile.Move);
        ClearReplacedLocomotion(Fall, profile.Fall);
        ClearReplacedLocomotion(Platform, profile.Platform);
        ClearReplacedLocomotion(Jump, profile.Jump);
        ClearReplacedLocomotion(Slide, profile.Slide);
        ClearReplacedLocomotion(Water, profile.Water);
        ClearReplacedLocomotion(Fly, profile.Fly);
        ClearReplacedLocomotion(Climb, profile.Climb);

        _move = profile.Move;
        _fall = profile.Fall;
        _platform = profile.Platform;
        _jump = profile.Jump;
        _slide = profile.Slide;
        _water = profile.Water;
        _fly = profile.Fly;
        _climb = profile.Climb;

        BindInstalledLocomotions();
        RefreshInstalledKinds();
    }

    internal void BindContext(TrailblazerWorldContext context)
    {
        TrailblazerWorldContext.ThrowIfUnusable(context);
        _context = context;
        BindInstalledLocomotions();
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
            Water,
            Fly,
            Climb);
    }

    internal void ConfigureInstalledKinds(LocomotionKind kinds)
    {
        LocomotionKind normalizedKinds = kinds | LocomotionKind.Core;
        var builder = new LocomotionProfileBuilder(includeOptionalLocomotions: false);

        if ((normalizedKinds & LocomotionKind.Jump) != 0)
            builder.WithJump();

        if ((normalizedKinds & LocomotionKind.Slide) != 0)
            builder.WithSlide();

        if ((normalizedKinds & LocomotionKind.Water) != 0)
            builder.WithWater();

        if ((normalizedKinds & LocomotionKind.Fly) != 0)
            builder.WithFly();

        if ((normalizedKinds & LocomotionKind.Climb) != 0)
            builder.WithClimb();

        ApplyProfile(builder.Build());
    }

    private void RefreshInstalledKinds()
    {
        InstalledKinds = LocomotionKind.Core;

        if (Jump != null)
            InstalledKinds |= LocomotionKind.Jump;

        if (Slide != null)
            InstalledKinds |= LocomotionKind.Slide;

        if (Water != null)
            InstalledKinds |= LocomotionKind.Water;

        if (Fly != null)
            InstalledKinds |= LocomotionKind.Fly;

        if (Climb != null)
            InstalledKinds |= LocomotionKind.Climb;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ILocomotion? GetLocomotion(Type type)
    {
        if (!TryResolveLocomotionSlot(type, out LocomotionSlot slot))
            return null;

        return GetLocomotion(slot);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ILocomotion? GetLocomotion(LocomotionSlot slot)
    {
        if (slot == LocomotionSlot.Move) return Move;
        if (slot == LocomotionSlot.Platform) return Platform;
        if (slot == LocomotionSlot.Jump) return Jump;
        if (slot == LocomotionSlot.Fall) return Fall;
        if (slot == LocomotionSlot.Slide) return Slide;
        if (slot == LocomotionSlot.Water) return Water;
        if (slot == LocomotionSlot.Fly) return Fly;
        return Climb;
    }

    /// <summary>
    /// Gets and enumerates all locomotion instances in the handler.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IEnumerable<ILocomotion> GetLocomotions()
    {
        yield return Move;
        yield return Platform;

        if (Jump != null)
            yield return Jump;

        yield return Fall;

        if (Water != null)
            yield return Water;

        if (Fly != null)
            yield return Fly;

        if (Climb != null)
            yield return Climb;

        if (Slide != null)
            yield return Slide;
    }

    private void SetLocomotion(Type type, ILocomotion? locomotion)
    {
        if (!TryResolveLocomotionSlot(type, out LocomotionSlot slot))
            throw new NotSupportedException($"Unsupported locomotion type '{type.Name}'.");

        SetLocomotion(slot, locomotion);
    }

    private static bool TryResolveLocomotionSlot(Type type, out LocomotionSlot slot)
    {
        if (type == typeof(MoveLocomotion))
        {
            slot = LocomotionSlot.Move;
            return true;
        }

        if (type == typeof(PlatformLocomotion))
        {
            slot = LocomotionSlot.Platform;
            return true;
        }

        if (type == typeof(JumpLocomotion))
        {
            slot = LocomotionSlot.Jump;
            return true;
        }

        if (type == typeof(FallLocomotion))
        {
            slot = LocomotionSlot.Fall;
            return true;
        }

        if (type == typeof(SlideLocomotion))
        {
            slot = LocomotionSlot.Slide;
            return true;
        }

        if (type == typeof(WaterLocomotion))
        {
            slot = LocomotionSlot.Water;
            return true;
        }

        if (type == typeof(FlyLocomotion))
        {
            slot = LocomotionSlot.Fly;
            return true;
        }

        if (type == typeof(ClimbLocomotion))
        {
            slot = LocomotionSlot.Climb;
            return true;
        }

        slot = default;
        return false;
    }

    private void SetLocomotion(LocomotionSlot slot, ILocomotion? locomotion)
    {
        BindLocomotion(locomotion);

        switch (slot)
        {
            case LocomotionSlot.Move:
                _move = (MoveLocomotion)locomotion!;
                return;
            case LocomotionSlot.Platform:
                _platform = (PlatformLocomotion)locomotion!;
                return;
            case LocomotionSlot.Jump:
                _jump = locomotion as JumpLocomotion;
                return;
            case LocomotionSlot.Fall:
                _fall = (FallLocomotion)locomotion!;
                return;
            case LocomotionSlot.Slide:
                _slide = locomotion as SlideLocomotion;
                return;
            case LocomotionSlot.Water:
                _water = locomotion as WaterLocomotion;
                return;
            case LocomotionSlot.Fly:
                _fly = locomotion as FlyLocomotion;
                return;
        }

        _climb = locomotion as ClimbLocomotion;
    }

    private void BindInstalledLocomotions()
    {
        BindLocomotion(_move);
        BindLocomotion(_platform);
        BindLocomotion(_jump);
        BindLocomotion(_fall);
        BindLocomotion(_slide);
        BindLocomotion(_water);
        BindLocomotion(_fly);
        BindLocomotion(_climb);
    }

    private void BindLocomotion(ILocomotion? locomotion)
    {
        if (_context == null || locomotion == null)
            return;

        switch (locomotion)
        {
            case JumpLocomotion jump:
                jump.BindContext(_context);
                break;
            case PlatformLocomotion platform:
                platform.BindContext(_context);
                break;
            case WaterLocomotion water:
                water.BindContext(_context);
                break;
        }
    }

    private static void ClearReplacedLocomotion(ILocomotion? current, ILocomotion? next)
    {
        if (current == null || ReferenceEquals(current, next))
            return;

        current.ClearTransientState();
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

        foreach (var locomotion in GetLocomotions())
        {
            if (!locomotion.IsEnabled) continue;
            ILocomotion? otherLocomotion = other.GetLocomotion(locomotion.GetType());
            if (otherLocomotion == null) continue;
            locomotion.SyncTransientState(otherLocomotion);
        }
    }

    /// <summary>
    /// Clears the transient state for the locomotion component of the specified type, if it is enabled.
    /// </summary>
    /// <remarks>This method has no effect if the specified locomotion component is not present or is not enabled.</remarks>
    /// <typeparam name="T">The type of locomotion component for which to clear transient state. Must implement the ILocomotion interface.</typeparam>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void ClearTransientState<T>() where T : ILocomotion
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
    public void ClearAllTransientState()
    {
        foreach (var locomotion in GetLocomotions())
        {
            if (locomotion.IsEnabled)
                locomotion.ClearTransientState();
        }
    }

    #endregion

    #region Serialization

    /// <inheritdoc />
    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref IsInControl, "IsInControl", true);
        int installedKinds = (int)InstalledKinds;
        RecordValues.Look(chronicler, ref installedKinds, "InstalledKinds", (int)LocomotionKind.All);

        if (chronicler.Mode == SerializationMode.Loading)
            ConfigureInstalledKinds((LocomotionKind)installedKinds);

        RecordDeep.Look(chronicler, ref _forces, "Forces");
        RecordDeep.Look(chronicler, ref _move, "Move");
        RecordDeep.Look(chronicler, ref _platform, "Platform");
        RecordOptionalLocomotion(chronicler, ref _jump, "Jump");
        RecordDeep.Look(chronicler, ref _fall, "Fall");
        RecordOptionalLocomotion(chronicler, ref _slide, "Slide");
        RecordOptionalLocomotion(chronicler, ref _water, "Water");
        RecordOptionalLocomotion(chronicler, ref _fly, "Fly");
        RecordOptionalLocomotion(chronicler, ref _climb, "Climb");

        if (chronicler.Mode == SerializationMode.Loading)
        {
            ApplyProfile(new LocomotionProfile(
                _move,
                _fall,
                _platform,
                _jump,
                _slide,
                _water,
                _fly,
                _climb));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void RecordOptionalLocomotion<T>(
        IChronicler chronicler,
        ref T? locomotion,
        string id)
        where T : class, ILocomotion, IRecordable
    {
        T local = locomotion ?? null!;
        RecordDeep.Look(chronicler, ref local, id);
        locomotion = local;
    }

    #endregion
}

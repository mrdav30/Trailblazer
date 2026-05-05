using System;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Describes the installed locomotion modules for a object motor.
/// </summary>
public sealed class LocomotionProfile
{
    /// <summary>
    /// Initializes a new locomotion profile.
    /// </summary>
    public LocomotionProfile(
        MoveLocomotion move,
        FallLocomotion fall,
        PlatformLocomotion? platform = null,
        JumpLocomotion? jump = null,
        SlideLocomotion? slide = null,
        WaterLocomotion? water = null,
        FlyLocomotion? fly = null,
        ClimbLocomotion? climb = null)
    {
        Move = move ?? throw new ArgumentNullException(nameof(move));
        Fall = fall ?? throw new ArgumentNullException(nameof(fall));
        Platform = platform ?? new PlatformLocomotion();
        Jump = jump;
        Slide = slide;
        Water = water;
        Fly = fly;
        Climb = climb;
    }

    /// <summary>
    /// Core movement configuration for the motor.
    /// </summary>
    public MoveLocomotion Move { get; }

    /// <summary>
    /// Fall-state configuration for the motor.
    /// </summary>
    public FallLocomotion Fall { get; }

    /// <summary>
    /// Required moving-platform locomotion.
    /// </summary>
    public PlatformLocomotion Platform { get; }

    /// <summary>
    /// Optional jump locomotion.
    /// </summary>
    public JumpLocomotion? Jump { get; }

    /// <summary>
    /// Optional slide locomotion.
    /// </summary>
    public SlideLocomotion? Slide { get; }

    /// <summary>
    /// Optional water locomotion.
    /// </summary>
    public WaterLocomotion? Water { get; }

    /// <summary>
    /// Optional flight locomotion.
    /// </summary>
    public FlyLocomotion? Fly { get; }

    /// <summary>
    /// Optional climb locomotion.
    /// </summary>
    public ClimbLocomotion? Climb { get; }

    /// <summary>
    /// Gets the installed locomotion flags for this profile.
    /// </summary>
    public LocomotionKind InstalledKinds
    {
        get
        {
            LocomotionKind result = LocomotionKind.Core;

            if (Jump != null)
                result |= LocomotionKind.Jump;

            if (Slide != null)
                result |= LocomotionKind.Slide;

            if (Water != null)
                result |= LocomotionKind.Water;

            if (Fly != null)
                result |= LocomotionKind.Fly;

            if (Climb != null)
                result |= LocomotionKind.Climb;

            return result;
        }
    }

    /// <summary>
    /// Creates the default profile with all built-in locomotions installed.
    /// </summary>
    public static LocomotionProfile CreateDefault()
    {
        return new LocomotionProfileBuilder().Build();
    }

    /// <summary>
    /// Creates a minimal profile with only required locomotion behavior installed.
    /// </summary>
    public static LocomotionProfile CreateCoreOnly()
    {
        return new LocomotionProfileBuilder(includeOptionalLocomotions: false).Build();
    }

    /// <summary>
    /// Creates a new builder seeded with the default full locomotion profile.
    /// </summary>
    public static LocomotionProfileBuilder CreateBuilder()
    {
        return new LocomotionProfileBuilder();
    }

    /// <summary>
    /// Creates a new builder seeded from the currently installed handler locomotions.
    /// </summary>
    internal static LocomotionProfileBuilder CreateBuilder(LocomotionHandler handler)
    {
        return LocomotionProfileBuilder.FromHandler(handler);
    }
}

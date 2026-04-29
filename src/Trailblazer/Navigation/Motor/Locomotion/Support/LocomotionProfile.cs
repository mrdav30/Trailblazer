using System;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Describes the installed locomotion modules for a navigator motor.
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
        SwimLocomotion? swim = null,
        FlyLocomotion? fly = null,
        ClimbLocomotion? climb = null)
    {
        Move = move ?? throw new ArgumentNullException(nameof(move));
        Fall = fall ?? throw new ArgumentNullException(nameof(fall));
        Platform = platform;
        Jump = jump;
        Slide = slide;
        Swim = swim;
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
    /// Optional moving-platform locomotion.
    /// </summary>
    public PlatformLocomotion? Platform { get; }

    /// <summary>
    /// Optional jump locomotion.
    /// </summary>
    public JumpLocomotion? Jump { get; }

    /// <summary>
    /// Optional slide locomotion.
    /// </summary>
    public SlideLocomotion? Slide { get; }

    /// <summary>
    /// Optional swim locomotion.
    /// </summary>
    public SwimLocomotion? Swim { get; }

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

            if (Platform != null)
                result |= LocomotionKind.Platform;

            if (Jump != null)
                result |= LocomotionKind.Jump;

            if (Slide != null)
                result |= LocomotionKind.Slide;

            if (Swim != null)
                result |= LocomotionKind.Swim;

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
    /// Creates a minimal profile with only core movement and fall behavior installed.
    /// </summary>
    public static LocomotionProfile CreateMoveAndFallOnly()
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

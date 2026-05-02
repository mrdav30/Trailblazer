using SwiftCollections;
using System;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Fluent builder for composing built-in locomotion profiles.
/// </summary>
public sealed class LocomotionProfileBuilder
{
    #region Constructor 

    /// <summary>
    /// Initializes a new builder.
    /// </summary>
    /// <param name="includeOptionalLocomotions">When true, seeds the builder with the default full locomotion profile.</param>
    public LocomotionProfileBuilder(bool includeOptionalLocomotions = true)
    {
        Move = new MoveLocomotion();
        Fall = new FallLocomotion();

        if (includeOptionalLocomotions)
        {
            Platform = new PlatformLocomotion();
            Jump = new JumpLocomotion();
            Slide = new SlideLocomotion();
            Water = new WaterLocomotion();
            Fly = new FlyLocomotion();
            Climb = new ClimbLocomotion();
        }
    }

    #endregion

    #region Properties

    /// <summary>
    /// The move locomotion to install.
    /// </summary>
    public MoveLocomotion Move { get; private set; }

    /// <summary>
    /// The fall locomotion to install.
    /// </summary>
    public FallLocomotion Fall { get; private set; }

    /// <summary>
    /// The optional platform locomotion to install.
    /// </summary>
    public PlatformLocomotion? Platform { get; private set; }

    /// <summary>
    /// The optional jump locomotion to install.
    /// </summary>
    public JumpLocomotion? Jump { get; private set; }

    /// <summary>
    /// The optional slide locomotion to install.
    /// </summary>
    public SlideLocomotion? Slide { get; private set; }

    /// <summary>
    /// The optional water locomotion to install.
    /// </summary>
    public WaterLocomotion? Water { get; private set; }

    /// <summary>
    /// The optional fly locomotion to install.
    /// </summary>
    public FlyLocomotion? Fly { get; private set; }

    /// <summary>
    /// The optional climb locomotion to install.
    /// </summary>
    public ClimbLocomotion? Climb { get; private set; }

    #endregion

    /// <summary>
    /// Replaces the core move locomotion.
    /// </summary>
    public LocomotionProfileBuilder WithMove(MoveLocomotion move)
    {
        Move = move ?? throw new ArgumentNullException(nameof(move));
        return this;
    }

    /// <summary>
    /// Replaces the core fall locomotion.
    /// </summary>
    public LocomotionProfileBuilder WithFall(FallLocomotion fall)
    {
        Fall = fall ?? throw new ArgumentNullException(nameof(fall));
        return this;
    }

    /// <summary>
    /// Installs or replaces platform locomotion.
    /// </summary>
    public LocomotionProfileBuilder WithPlatform(PlatformLocomotion? platform = null)
    {
        Platform = platform ?? new PlatformLocomotion();
        return this;
    }

    /// <summary>
    /// Removes platform locomotion.
    /// </summary>
    public LocomotionProfileBuilder WithoutPlatform()
    {
        Platform = null;
        return this;
    }

    /// <summary>
    /// Installs or replaces jump locomotion.
    /// </summary>
    public LocomotionProfileBuilder WithJump(JumpLocomotion? jump = null)
    {
        Jump = jump ?? new JumpLocomotion();
        return this;
    }

    /// <summary>
    /// Removes jump locomotion.
    /// </summary>
    public LocomotionProfileBuilder WithoutJump()
    {
        Jump = null;
        return this;
    }

    /// <summary>
    /// Installs or replaces slide locomotion.
    /// </summary>
    public LocomotionProfileBuilder WithSlide(SlideLocomotion? slide = null)
    {
        Slide = slide ?? new SlideLocomotion();
        return this;
    }

    /// <summary>
    /// Removes slide locomotion.
    /// </summary>
    public LocomotionProfileBuilder WithoutSlide()
    {
        Slide = null;
        return this;
    }

    /// <summary>
    /// Installs or replaces water locomotion.
    /// </summary>
    public LocomotionProfileBuilder WithWater(WaterLocomotion? water = null)
    {
        Water = water ?? new WaterLocomotion();
        return this;
    }

    /// <summary>
    /// Removes water locomotion.
    /// </summary>
    public LocomotionProfileBuilder WithoutWater()
    {
        Water = null;
        return this;
    }

    /// <summary>
    /// Installs or replaces fly locomotion.
    /// </summary>
    public LocomotionProfileBuilder WithFly(FlyLocomotion? fly = null)
    {
        Fly = fly ?? new FlyLocomotion();
        return this;
    }

    /// <summary>
    /// Removes fly locomotion.
    /// </summary>
    public LocomotionProfileBuilder WithoutFly()
    {
        Fly = null;
        return this;
    }

    /// <summary>
    /// Installs or replaces climb locomotion.
    /// </summary>
    public LocomotionProfileBuilder WithClimb(ClimbLocomotion? climb = null)
    {
        Climb = climb ?? new ClimbLocomotion();
        return this;
    }

    /// <summary>
    /// Removes climb locomotion.
    /// </summary>
    public LocomotionProfileBuilder WithoutClimb()
    {
        Climb = null;
        return this;
    }

    /// <summary>
    /// Builds the composed locomotion profile.
    /// </summary>
    public LocomotionProfile Build()
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

    internal static LocomotionProfileBuilder FromHandler(LocomotionHandler handler)
    {
        SwiftThrowHelper.ThrowIfNull(handler, nameof(handler));

        return new LocomotionProfileBuilder(includeOptionalLocomotions: false)
            .WithMove(handler.Move)
            .WithFall(handler.Fall)
            .SetPlatform(handler.Platform)
            .SetJump(handler.Jump)
            .SetSlide(handler.Slide)
            .SetWater(handler.Water)
            .SetFly(handler.Fly)
            .SetClimb(handler.Climb);
    }

    private LocomotionProfileBuilder SetPlatform(PlatformLocomotion? platform)
    {
        Platform = platform;
        return this;
    }

    private LocomotionProfileBuilder SetJump(JumpLocomotion? jump)
    {
        Jump = jump;
        return this;
    }

    private LocomotionProfileBuilder SetSlide(SlideLocomotion? slide)
    {
        Slide = slide;
        return this;
    }

    private LocomotionProfileBuilder SetWater(WaterLocomotion? water)
    {
        Water = water;
        return this;
    }

    private LocomotionProfileBuilder SetFly(FlyLocomotion? fly)
    {
        Fly = fly;
        return this;
    }

    private LocomotionProfileBuilder SetClimb(ClimbLocomotion? climb)
    {
        Climb = climb;
        return this;
    }
}

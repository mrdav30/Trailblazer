//=======================================================================
// NavMotor.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using Chronicler;
using FixedMathSharp;
using System;

namespace Trailblazer.Navigation.Motor;

/// <summary>
/// Controls character movement using an acceleration-based approach in a deterministic, lockstep simulation.
/// </summary>
/// <remarks>
/// This controller processes movement requests, applies forces such as gravity and platform adjustments,
/// and finalizes traversal states for consistent movement across frames.
/// </remarks>
[Serializable]
public partial class NavMotor : IRecordable
{
    #region Fields

    /// <summary>
    /// Manages locomotion states and behaviors.
    /// </summary>
    private LocomotionHandler _handler = new();

    private bool _isInitialized;

    private TrailblazerWorldContext? _context;

    /// <inheritdoc cref="NavMotorEvents"/>
    [NonSerialized]
    public NavMotorEvents Events = new();

    /// <summary>
    /// This stores the current <see cref="Navigator._frameCondition"/> for the frame.
    /// </summary>
    /// <remarks>
    /// This is set on <see cref="OnInitialize"/>, can be explicitly synchronized before traversal
    /// through <see cref="SyncTraversalState(TrekCondition, bool)"/>, and is refreshed at the end of
    /// the frame in <see cref="FinalizeTraversal"/>.
    /// </remarks>
    public TransitState CurrentState { get; private set; } = null!;

    /// <summary>
    /// Indicates whether a traversal has started for the current frame and still requires finalization or abort.
    /// </summary>
    public bool TraversalInProgress { get; private set; }

    /// <summary>
    /// Gets a value indicating whether the object has been initialized.
    /// </summary>
    public bool IsInitialized => _isInitialized;

    /// <summary>
    /// Gets the world context this motor is bound to, when explicitly bound.
    /// </summary>
    public TrailblazerWorldContext? Context => _context;

    /// <inheritdoc cref="PlatformLocomotion"/>
    public PlatformLocomotion PlatformModule => Handler.Platform;

    /// <inheritdoc cref="JumpLocomotion"/>
    public JumpLocomotion? JumpModule => Handler.Jump;

    /// <inheritdoc cref="SlideLocomotion"/>
    public SlideLocomotion? SlideModule => Handler.Slide;

    /// <inheritdoc cref="WaterLocomotion"/>
    public WaterLocomotion? WaterModule => Handler.Water;

    /// <inheritdoc cref="FlyLocomotion"/>
    public FlyLocomotion? FlyModule => Handler.Fly;

    /// <inheritdoc cref="ClimbLocomotion"/>
    public ClimbLocomotion? ClimbModule => Handler.Climb;

    #region Cache

    /// <summary>
    /// Stores the slope angle for the current frame based on <see cref="TrekRequest.Direction"/>.
    /// </summary>
    public Fixed64 FrameSlopeAngle { get; private set; }

    /// <summary>
    /// Accumulates forces applied during the traversal phase before they are committed.
    /// </summary>
    private Vector3d _forceOutput;

    /// <summary>
    /// Records the simulation frame that opened the current traversal so stale traversal usage can be detected.
    /// </summary>
    private int _pendingTraversalFrame = -1;

    #endregion

    #region State Status

    /// <summary>
    /// Indicates if the traversal medium has changed since the last frame.
    /// </summary>
    public bool StateChanged => CurrentState.Medium != CurrentState.PreviousMedium
        && CurrentState.Medium != TraversalMedium.Unknown
        && CurrentState.PreviousMedium != TraversalMedium.Unknown;

    /// <summary>
    /// Determines if the object is currently on the ground.
    /// </summary>
    public bool IsOnSolid => CurrentState.Medium == TraversalMedium.Solid;

    /// <summary>
    /// Determines if the object was on the ground in the previous frame.
    /// </summary>
    public bool WasOnSolid => CurrentState.PreviousMedium == TraversalMedium.Solid;

    /// <summary>
    /// Determines if the object is currently in the air.
    /// </summary>
    public bool IsInGas => CurrentState.Medium == TraversalMedium.Gas;

    /// <summary>
    /// Determines if the object was in the air in the previous frame.
    /// </summary>
    public bool WasInGas => CurrentState.PreviousMedium == TraversalMedium.Gas;

    /// <summary>
    /// Determines if the object is currently in water.
    /// </summary>
    public bool IsInLiquid => CurrentState.Medium == TraversalMedium.Liquid;

    /// <summary>
    /// Determines if the object was in water in the previous frame.
    /// </summary>
    public bool WasInLiquid => CurrentState.PreviousMedium == TraversalMedium.Liquid;

    /// <summary>
    /// Determines if the object is currently under active flight control.
    /// </summary>
    public bool IsFlying => FlyModule?.IsFlying == true;

    /// <summary>
    /// Determines if the object is currently in a jump state.
    /// </summary>
    public bool IsJumping => JumpModule?.IsJumping == true;

    /// <summary>
    /// Gets a value indicating whether the object is currently falling.
    /// </summary>
    public bool IsFalling => Handler.Fall.IsFalling;

    /// <summary>
    /// Determines if the object is currently attached to a climb affordance.
    /// </summary>
    public bool IsClimbing => ClimbModule?.IsClimbing == true;

    /// <summary>
    /// Checks if the object is in a state where it is airborne but not actively jumping, flying, or falling.
    /// </summary>
    public bool InLimbo => !IsOnSolid && !IsInGas && !IsInLiquid
        || IsInGas && !IsFlying && !IsJumping && !IsFalling && !IsClimbing;

    #endregion

    #endregion

    #region Construct

    /// <summary>
    /// Creates a new context-bound <see cref="NavMotor"/> instance.
    /// </summary>
    public static NavMotor CreateNew(
        TrailblazerWorldContext context,
        TrekCondition initialCondition,
        LocomotionProfile? profile = null) =>
        new(context, initialCondition, profile);

    /// <summary>
    /// Creates a new context-bound <see cref="NavMotor"/> instance without initializing it.
    /// </summary>
    public static NavMotor CreateUninitialized(TrailblazerWorldContext context, LocomotionHandler? handler = null)
    {
        var motor = new NavMotor();
        if (handler != null)
            motor._handler = handler;
        motor.BindContext(context);
        return motor;
    }

    // Parameterless constructor for serialization purposes. Not intended for direct use.
    private NavMotor() { }

    /// <summary>
    /// Initializes a new context-bound <see cref="NavMotor"/> instance.
    /// </summary>
    public NavMotor(TrailblazerWorldContext context, TrekCondition condition, LocomotionProfile? profile = null)
    {
        BindContext(context);
        OnInitialize(condition, profile);
    }

    /// <summary>
    /// Binds this motor to a world context.
    /// </summary>
    public void BindContext(TrailblazerWorldContext context)
    {
        Trailblazer.Pathing.PathRequestContextResolver.ThrowIfUnusable(context);
        _context = context;
        _handler.BindContext(context);
    }

    private TrailblazerWorldContext RequireContext() =>
        _context ?? throw new InvalidOperationException("NavMotor requires an explicit TrailblazerWorldContext.");

    private int FrameCount => RequireContext().FrameCount;

    private Fixed64 DeltaTime => RequireContext().DeltaTime;

    private Fixed64 InvDeltaTime => RequireContext().InvDeltaTime;

    private Fixed64 TotalTime => RequireContext().TotalTime;

    /// <summary>
    /// Prepares the controller by linking it to the given object and setting initial state values.
    /// </summary>
    /// <param name="condition">The initial traversal condition of the object</param>
    /// <param name="profile">Optional locomotion composition profile. When omitted, the default profile is used.</param>
    public void OnInitialize(TrekCondition condition, LocomotionProfile? profile = null)
    {
        _handler = profile != null ? new LocomotionHandler(profile) : new LocomotionHandler();
        if (_context != null)
            _handler.BindContext(_context);
        CurrentState = new TransitState(condition);
        if (CurrentState.GroundState.HasValue)
            PlatformModule.HandlePlatformChange(CurrentState.GroundState); // set the initial platform

        _isInitialized = true;
    }

    /// <summary>
    /// Replaces the currently installed locomotion profile.
    /// </summary>
    public void SetLocomotionProfile(LocomotionProfile profile)
    {
        SwiftThrowHelper.ThrowIfNull(profile, nameof(profile));

        if (TraversalInProgress)
            throw new InvalidOperationException("Cannot change locomotion composition while a traversal frame is in progress.");

        Handler.ApplyProfile(profile);

        if (CurrentState?.GroundState.HasValue == true)
            PlatformModule.HandlePlatformChange(CurrentState.GroundState);
    }

    /// <summary>
    /// Reconfigures the currently installed locomotion profile from the active handler state.
    /// </summary>
    public void ConfigureLocomotions(Action<LocomotionProfileBuilder> configure)
    {
        SwiftThrowHelper.ThrowIfNull(configure, nameof(configure));

        var builder = LocomotionProfile.CreateBuilder(Handler);
        configure(builder);
        SetLocomotionProfile(builder.Build());
    }

    #endregion

    #region Properties

    /// <inheritdoc cref="_handler"/>
    public LocomotionHandler Handler => _handler;

    #endregion

}

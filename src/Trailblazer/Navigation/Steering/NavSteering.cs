//=======================================================================
// NavSteering.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using Chronicler;
using FixedMathSharp;
using GridForge.Grids;
using SwiftCollections;
using Trailblazer.Navigation.MovementGroups;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation.Steering;

/// <summary>
/// Handles agent steering and path navigation logic by coordinating pathfinding, movement,
/// and group behaviors within a lockstep simulation. Supports both direct line-of-sight travel
/// and graph-backed A* or flow-field guidance.
/// </summary>
public partial class NavSteering : IRecordable
{
    #region Constants & Defaults

    /// <summary>
    /// Default body-radius multiplier for the group-neighbor search range.
    /// </summary>
    protected static readonly Fixed64 DefaultGroupFactor = (Fixed64)10;

    /// <summary>
    /// Default body-radius multiplier for the avoidance search range.
    /// </summary>
    protected static readonly Fixed64 DefaultAvoidFactor = (Fixed64)3;

    /// <summary>
    /// Default weights used for group-based steering calculations (separation, alignment, cohesion).
    /// </summary>
    protected static readonly GroupBehaviorWeights DefaultBehaviorWeights = new()
    {
        Separation = (Fixed64)2,
        Alignment = Fixed64.Half,
        Cohesion = Fixed64.FromFraction(1, 5),
        Avoidance = Fixed64.One
    };

    /// <summary>
    /// Default world-space tolerance used when advancing between ordinary guide steps.
    /// </summary>
    public static readonly Fixed64 DefaultWaypointTolerance = Fixed64.Half;

    /// <summary>
    /// Number of frames between pathfinding LOS rechecks.
    /// </summary>
    protected const int DefaultPathRecheckCooldown = 16;

    /// <summary>
    /// Maximum number of repath attempts before declaring the agent fully stuck.
    /// </summary>
    protected const int StuckRepathTries = 4;

    /// <summary>
    /// Default braking factor applied when decelerating or stopping motion.
    /// </summary>
    public static readonly Fixed64 DefaultBrakingPower = Fixed64.FromFraction(3, 20);

    #endregion

    #region Fields

    /// <summary>
    /// The final destination this agent is attempting to reach.
    /// </summary>
    protected Vector3d _destination;

    /// <summary>
    /// Gets the current target direction as a three-dimensional vector.
    /// </summary>
    protected Vector3d _targetDirection;

    /// <summary>
    /// Gets the direction vector of the most recent target interaction.
    /// </summary>
    protected Vector3d _lastTargetDirection;

    /// <summary>
    /// Whether the object is following a path or guide to the destination.
    /// </summary>
    protected bool _shouldMove;

    /// <summary>
    /// Whether the agent has become stuck and exhausted repathing attempts.
    /// </summary>
    protected bool _isStuck;

    /// <summary>
    /// True if the agent can reach the destination without requiring a path.
    /// </summary>
    protected bool _hasLineOfSightPath;

    /// <summary>
    /// Has this unit arrived at destination?
    /// </summary>
    protected bool _isAtDestination;

    /// <summary>
    /// Number of consecutive frames where movement failed and deceleration is occurring.
    /// </summary>
    protected int _stoppedFrameCount;

    #endregion

    #region Runtime State - Pathfinding

    /// <summary>
    /// Disable if a unit never needs graph-guide validation or repathing.
    /// </summary>
    public bool CanPathfind = true;

    /// <inheritdoc cref="_destination"/>
    public Vector3d Destination => _destination;

    private Vector3d _requestedDestination;

    private int _pathRecheckCooldownFrames = DefaultPathRecheckCooldown;

    private int? _pathRecheckPhase;

    private ulong _pathRecheckArmedFrameIndex;

    private int _pathRecheckDelayFrames;

    private object? _pathRecheckClockLifetime;

    /// <summary>
    /// Gets the configured interval for periodic line-of-sight rechecks, in frames.
    /// </summary>
    public int PathRecheckCooldownFrames => _pathRecheckCooldownFrames;

    /// <summary>
    /// Gets the optional context fixed-frame phase used for periodic line-of-sight rechecks.
    /// A null value selects the completed-heading countdown.
    /// </summary>
    public int? PathRecheckPhase => _pathRecheckPhase;

    /// <summary>
    /// Replaces the interval and optional context fixed-frame phase for periodic
    /// line-of-sight rechecks.
    /// </summary>
    /// <remarks>
    /// A null phase uses the completed-heading countdown. A non-null phase aligns
    /// periodic checks to the bound context clock; initial admission and required
    /// recovery do not wait for that phase.
    /// </remarks>
    /// <param name="intervalFrames">
    /// The recheck interval. It must be positive when <paramref name="phase"/> has a value.
    /// </param>
    /// <param name="phase">
    /// Null to use the completed-heading countdown, or a fixed-frame phase in the
    /// range [0, <paramref name="intervalFrames"/>).
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="phase"/> has a value and the interval is nonpositive, or the
    /// phase is outside the interval.
    /// </exception>
    public void ConfigurePathRechecks(int intervalFrames, int? phase = null)
    {
        ValidatePathRecheckConfiguration(intervalFrames, phase);

        if (_pathRecheckCooldownFrames == intervalFrames
            && _pathRecheckPhase == phase)
        {
            return;
        }

        bool activeSession = _currentQuery.HasValue && _shouldMove && !_isAtDestination;
        bool changedClockLifetime = activeSession
            && _pathRecheckPhase.HasValue
            && _pathRecheckClockLifetime != null
            && !ReferenceEquals(
                _pathRecheckClockLifetime,
                ResolveContext().ClockLifetime);
        bool preserveDue = activeSession
            && !changedClockLifetime
            && IsPeriodicPathRecheckDue();

        if (changedClockLifetime)
        {
            _hasLineOfSightPath = false;
            PreparePathRetry();
        }

        _pathRecheckCooldownFrames = intervalFrames;
        _pathRecheckPhase = phase;
        ClearPhasedPathRecheckSchedule();

        if (!activeSession)
        {
            _pathCheckCooldown = 0;
            return;
        }

        if (changedClockLifetime)
        {
            _pathCheckCooldown = 0;
            return;
        }

        if (!phase.HasValue)
        {
            _pathCheckCooldown = preserveDue ? 0 : intervalFrames;
            return;
        }

        ArmNextPhasedPathRecheck();
        if (preserveDue)
            _pathCheckCooldown = 0;
    }

    private static void ValidatePathRecheckConfiguration(int intervalFrames, int? phase)
    {
        if (!phase.HasValue)
            return;

        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            intervalFrames <= 0,
            intervalFrames,
            nameof(intervalFrames));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            phase.Value < 0 || phase.Value >= intervalFrames,
            phase.Value,
            nameof(phase));
    }

    /// <inheritdoc cref="_targetDirection"/>
    public Vector3d TargetDirection => _targetDirection;

    /// <inheritdoc cref="_lastTargetDirection"/>
    public Vector3d LastTargetDirection => _lastTargetDirection;

    /// <summary>
    /// Gets the immutable graph-backed medium-state query owned by the current steering session.
    /// </summary>
    private PathQuery? _currentQuery;

    private NavigationAreaPolicyKey? _pendingCommittedAreaPolicy;

    /// <inheritdoc cref="_currentQuery"/>
    public PathQuery? CurrentQuery => _currentQuery;

    internal NavigationAreaPolicyKey? AreaPolicyForCommit =>
        _currentQuery?.AreaPolicy ?? _pendingCommittedAreaPolicy;

    internal void ConsumePendingCommittedAreaPolicy() => _pendingCommittedAreaPolicy = null;

    private NavigationGuideLease? _navigationGuideLease;

    private NavigationFlowFieldLease? _navigationFlowFieldLease;

    private Navigator? _pendingTransitionOwner;

    /// <inheritdoc cref="_shouldMove"/>
    public bool ShouldMove => _shouldMove;

    /// <inheritdoc cref="_isStuck"/>
    public bool IsStuck => _isStuck;

    /// <inheritdoc cref="_hasLineOfSightPath"/>
    public bool HasLineOfSightPath => _hasLineOfSightPath;

    /// <summary>
    /// Current pathfinding search status.
    /// </summary>
    protected bool _shouldRequestPathThisFrame;

    /// <summary>
    /// Tracks the completed-heading countdown when fixed-frame phasing is disabled.
    /// With phasing enabled, it is a due marker: nonpositive forces work due
    /// immediately, while positive defers to the logical-frame schedule.
    /// </summary>
    protected int _pathCheckCooldown;

    /// <summary>
    /// How far we move each update
    /// </summary>
    protected Fixed64 _distanceToTarget;

    /// <inheritdoc cref="_distanceToTarget"/>
    public Fixed64 DistanceToTarget => _distanceToTarget;

    /// <summary>
    /// Indicates whether the agent is actively following an A* or Flow guide.
    /// </summary>
    public bool HasNavigationGuidance => !HasLineOfSightPath
        && (_navigationGuideLease != null
            || _navigationFlowFieldLease != null);

    internal NavigationGuideStatus PendingTransitionGuideStatus =>
        _currentQuery!.Value.Algorithm == PathAlgorithm.AStar
            ? _navigationGuideLease!.Value.Status
            : _navigationFlowFieldLease!.Value.Status;

    /// <inheritdoc cref="_isAtDestination"/>
    public bool IsAtDestination => _isAtDestination;

    internal void BindPendingTransitionOwner(Navigator owner)
    {
        SwiftThrowHelper.ThrowIfTrue(
            _pendingTransitionOwner != null
            && !ReferenceEquals(_pendingTransitionOwner, owner),
            message: "NavSteering is already bound to a different pending-transition owner.");

        _pendingTransitionOwner = owner;
    }

    internal void UnbindPendingTransitionOwner(Navigator owner)
    {
        if (ReferenceEquals(_pendingTransitionOwner, owner))
            _pendingTransitionOwner = null;
    }

    #endregion

    #region Runtime State - Steering & Motion

    /// <summary>
    /// Whether this agent can currently move.
    /// </summary>
    public bool CanMove = true;

    /// <summary>
    /// Internal cooldown before the agent can automatically stop again (used for bursty movement).
    /// </summary>
    protected int _autoStopFrameCount;

    /// <summary>
    /// Indicates whether the agent is currently eligible for automatic stopping logic.
    /// </summary>
    public bool CanAutoStop => _autoStopFrameCount <= 0;

    /// <summary>
    /// Number of attempts to repath after getting stuck.
    /// </summary>
    protected int _repathTries;

    /// <summary>
    /// Number of frames the agent has failed movement checks (used for stuck detection).
    /// </summary>
    protected int _stuckFrameCount;

    private Fixed64 _waypointTolerance = DefaultWaypointTolerance;

    /// <summary>
    /// Gets or sets the non-negative world-space tolerance used for ordinary guide steps.
    /// </summary>
    public Fixed64 WaypointTolerance
    {
        get => _waypointTolerance;
        set
        {
            SwiftThrowHelper.ThrowIfArgumentOutOfRange(
                value < Fixed64.Zero,
                null,
                nameof(value));
            _waypointTolerance = value;
        }
    }

    /// <summary>
    /// Gets or sets the non-negative body-radius multiplier used to find group
    /// neighbors for separation, alignment, and cohesion. Zero disables grouping forces.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public Fixed64 GroupFactor
    {
        get => _groupFactor;
        set
        {
            SwiftThrowHelper.ThrowIfArgumentOutOfRange(value < Fixed64.Zero, null, nameof(value));
            _groupFactor = value;
        }
    }

    private Fixed64 _groupFactor = DefaultGroupFactor;

    /// <summary>
    /// Gets or sets the non-negative body-radius multiplier used to find nearby
    /// steering occupants to avoid. Zero disables avoidance forces.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public Fixed64 AvoidFactor
    {
        get => _avoidFactor;
        set
        {
            SwiftThrowHelper.ThrowIfArgumentOutOfRange(value < Fixed64.Zero, null, nameof(value));
            _avoidFactor = value;
        }
    }

    private Fixed64 _avoidFactor = DefaultAvoidFactor;

    /// <summary>
    /// Weights for separating, aligning, and cohesion in group behavior.
    /// Avoidance weight is baked in here as well.
    /// </summary>
    public GroupBehaviorWeights BehaviorWeights = DefaultBehaviorWeights;

    /// <summary>
    /// Friction-based deceleration rate used when slowing down on ground surfaces.
    /// </summary>
    public Fixed64 BrakingPower = DefaultBrakingPower;

    private readonly MovementGroupSession _movementGroupSession = new();

    private readonly SwiftList<ISteer> _nearbySteerAgents = new();

    private readonly GridScanScratch _scanScratch = new();

    private MovementGroupTravelMode _movementGroupMode;

    private TrailblazerWorldContext? _context;

    #endregion

    #region Events

    /// <summary>
    /// Container for delegate events that fire on pathfinding state changes (start, stop, arrive).
    /// </summary>
    public NavSteeringEvents Events { get; protected set; } = new();

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new context-bound <see cref="NavSteering"/> instance.
    /// </summary>
    public NavSteering(TrailblazerWorldContext context)
    {
        BindContext(context);
        OnInitialize();
    }

    private NavSteering() { }

    internal static NavSteering CreateSerializationValidationShell() => new();

    /// <summary>
    /// Binds this steering controller to a world context.
    /// </summary>
    public void BindContext(TrailblazerWorldContext context)
    {
        TrailblazerWorldContext.ThrowIfUnusable(context);

        if (ReferenceEquals(_context, context))
            return;

        bool activePhasedSession = _pathRecheckPhase.HasValue
            && _currentQuery.HasValue
            && _shouldMove
            && !_isAtDestination;
        if (_context != null)
        {
            if ((_acquisitionMode == NavigationAcquisitionMode.Scheduled || HasNavigationConnector)
                && _currentQuery.HasValue)
            {
                _hasLineOfSightPath = false;
                PreparePathRetry();
            }
            _context.Navigation.MovementGroups.Remove(_movementGroupSession);
        }

        _context = context;
        if (_pathRecheckPhase.HasValue)
        {
            ClearPhasedPathRecheckSchedule();
            if (activePhasedSession)
            {
                _hasLineOfSightPath = false;
                PreparePathRetry();
                _pathCheckCooldown = 0;
            }
        }
    }

    #endregion

    #region Group Properties

    /// <summary>
    /// Gets or sets the unique identifier for the movement group associated with the current session.
    /// </summary>
    public int MovementGroupID
    {
        get => _movementGroupSession.GroupId;
        set => _movementGroupSession.GroupId = value;
    }

    /// <summary>
    /// Gets a value indicating whether the item is assigned to a movement group.
    /// </summary>
    public bool IsInGroup => MovementGroupID != -1;

    #endregion






}

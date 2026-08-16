//=======================================================================
// NavSteering.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

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
/// and guided path traversal using IGuide implementations like AStar or FlowField.
/// </summary>
public partial class NavSteering : IRecordable
{
    #region Constants & Defaults

    /// <summary>
    /// Default range to scan for other agents when calculating steering behaviors.
    /// </summary>
    protected static readonly Fixed64 DefaultGroupFactor = (Fixed64)10;

    /// <summary>
    /// Default padding radius used to maintain space between nearby agents.
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
    /// Default multiplier used to determine proximity tolerance when stopping at a destination.
    /// </summary>
    public static readonly Fixed64 DefaultDirectStop = Fixed64.FromRaw(0x40000000L); // 0.25f;

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

    /// <summary>
    /// Group fallback stop tolerance used when a formation breaks apart near the goal.
    /// </summary>
    protected static readonly Fixed64 DefaultGroupIndividualStop = Fixed64.One;

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
    /// Whether the currently resolved guide-backed route requires climb intent to remain engaged.
    /// </summary>
    protected bool _currentRouteRequestsClimbIntent;

    /// <summary>
    /// Version token that changes when the resolved route state relevant to guided climb intent changes.
    /// </summary>
    protected int _currentRouteTopologyVersion;

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
    /// Disable if a unit never needs voxel-guide validation or repathing.
    /// </summary>
    public bool CanPathfind = true;

    /// <inheritdoc cref="_destination"/>
    public Vector3d Destination => _destination;

    private Vector3d _requestedDestination;

    private Fixed64 _lastUnitSize;

    /// <inheritdoc cref="DefaultPathRecheckCooldown"/>
    public int PathRecheckCooldownFrames = DefaultPathRecheckCooldown;

    /// <inheritdoc cref="_targetDirection"/>
    public Vector3d TargetDirection => _targetDirection;

    /// <inheritdoc cref="_lastTargetDirection"/>
    public Vector3d LastTargetDirection => _lastTargetDirection;

    /// <summary>
    /// The pathfinding configuration used for the current movement request, including size, and type.
    /// </summary>
    private IPathRequest? _currentRequest;

    /// <inheritdoc cref="_currentRequest"/>
    public IPathRequest? CurrentRequest => _currentRequest;

    /// <summary>
    /// Gets the immutable graph-backed surface query owned by the current steering session.
    /// </summary>
    private PathQuery? _currentQuery;

    /// <inheritdoc cref="_currentQuery"/>
    public PathQuery? CurrentQuery => _currentQuery;

    /// <summary>
    /// Current guide used to compute the desired path or flow.
    /// </summary>
    private VolumeGuide? _volumeGuide;

    private NavigationGuideLease? _navigationGuideLease;

    private NavigationFlowFieldLease? _navigationFlowFieldLease;

    private NavigationGuideLease? _flowRecoveryGuideLease;

    private HybridRouteGuide? _hybridRouteGuide;

    /// <inheritdoc cref="_volumeGuide"/>
    public VolumeGuide? VolumeGuide => _volumeGuide;

    /// <inheritdoc cref="_shouldMove"/>
    public bool ShouldMove => _shouldMove;

    /// <inheritdoc cref="_isStuck"/>
    public bool IsStuck => _isStuck;

    /// <inheritdoc cref="_hasLineOfSightPath"/>
    public bool HasLineOfSightPath => _hasLineOfSightPath;

    /// <inheritdoc cref="_currentRouteRequestsClimbIntent"/>
    public bool CurrentRouteRequestsClimbIntent => _currentRouteRequestsClimbIntent;

    /// <inheritdoc cref="_currentRouteTopologyVersion"/>
    public int CurrentRouteTopologyVersion => _currentRouteTopologyVersion;

    /// <summary>
    /// Current pathfinding search status.
    /// </summary>
    protected bool _shouldRequestPathThisFrame;

    /// <summary>
    /// Represents the cooldown period, in milliseconds, before the next path check can be performed.
    /// </summary>
    protected int _pathCheckCooldown;

    private bool _currentRouteHasResolvedTopology;

    private bool _currentRouteUsesGuideTopology;

    /// <summary>
    /// How far we move each update
    /// </summary>
    protected Fixed64 _distanceToTarget;

    /// <inheritdoc cref="_distanceToTarget"/>
    public Fixed64 DistanceToTarget => _distanceToTarget;

    /// <summary>
    /// How far away the agent stops from the target
    /// </summary>
    private Fixed64 _closingDistance;

    /// <summary>
    /// Indicates whether the agent is actively following a guide path with queued waypoints.
    /// </summary>
    public bool HasNavigationGuidance => !HasLineOfSightPath
        && (_navigationGuideLease != null
            || _navigationFlowFieldLease != null
            || _flowRecoveryGuideLease != null
            || _hybridRouteGuide != null
            || _volumeGuide != null);

    /// <inheritdoc cref="_isAtDestination"/>
    public bool IsAtDestination => _isAtDestination;

    #endregion

    #region Runtime State - Steering & Motion

    /// <summary>
    /// Whether this agent can currently move.
    /// </summary>
    public bool CanMove = true;

    /// <inheritdoc cref="_stoppedFrameCount"/>
    public int StoppedFrameCount => _stoppedFrameCount;

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

    /// <summary>
    /// Multiplier used to determine how close the agent must be to its target before stopping.
    /// </summary>
    public Fixed64 StopMultiplier = DefaultDirectStop;

    /// <summary>
    /// How far to look for group neighbors (separation/alignment/cohesion).
    /// </summary>
    public Fixed64 GroupFactor = DefaultGroupFactor;

    /// <summary>
    /// How far to look for obstacles to avoid.
    /// </summary>
    public Fixed64 AvoidFactor = DefaultAvoidFactor;

    /// <summary>
    /// Weights for separating, aligning, and cohesion in group behavior.
    /// Avoidance weight is baked in here as well.
    /// </summary>
    public GroupBehaviorWeights BehaviorWeights = DefaultBehaviorWeights;

    /// <summary>
    /// Friction-based deceleration rate used when slowing down on ground surfaces.
    /// </summary>
    public Fixed64 BrakingPower = DefaultBrakingPower;

    private Fixed64 _agentRadius;

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

    /// <summary>
    /// Gets the world context this steering controller is bound to, when explicitly bound.
    /// </summary>
    public TrailblazerWorldContext? Context => _context;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new <see cref="NavSteering"/> instance bound to a world context.
    /// </summary>
    public static NavSteering CreateNew(TrailblazerWorldContext context, Fixed64 radius) => new(context, radius);

    private NavSteering() { }

    /// <summary>
    /// Initializes a new context-bound <see cref="NavSteering"/> instance.
    /// </summary>
    public NavSteering(TrailblazerWorldContext context, Fixed64 radius)
    {
        BindContext(context);
        OnInitialize(radius);
    }

    /// <summary>
    /// Binds this steering controller to a world context.
    /// </summary>
    public void BindContext(TrailblazerWorldContext context)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);

        if (ReferenceEquals(_context, context))
            return;

        if (_context != null)
            _context.Navigation.MovementGroups.Remove(_movementGroupSession);

        _context = context;
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
    /// Gets the index of the group associated with the current movement session.
    /// </summary>
    public int GroupIndex
    {
        get => _movementGroupSession.GroupIndex;
        protected set => _movementGroupSession.GroupIndex = value;
    }

    /// <summary>
    /// Gets a value indicating whether the item is assigned to a movement group.
    /// </summary>
    public bool IsInGroup => MovementGroupID != -1;

    #endregion






}

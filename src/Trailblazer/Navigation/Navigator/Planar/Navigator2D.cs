//=======================================================================
// Navigator2D.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>Coordinates native planar motion through a host-owned motion resolver.</summary>
/// <remarks>
/// Advance the context, prepare all controllers, resolve host motion, commit all controllers, then run the late step.
/// Queue writes from motion and occupancy notifications for the next ordered host command boundary.
/// </remarks>
public abstract partial class Navigator2D : IDisposable, IVoxelOccupant
{
    private readonly TrailblazerWorldContext _context;
    private readonly string _mapId;
    private readonly NavigationPlanarBinding _binding;
    private readonly TraversalMedium _medium;
    private readonly IDisposable _resetHook;
    private NavigationGuideLease? _guide;
    private NavigationAreaPolicyKey _policy;
    private NavigationWorkBudget _budget;
    private WorldVoxelIndex? _occupied;
    private string? _acceptedMapId;
    private NavigationMotionRequest2D? _pending;
    private object? _clockLifetime;
    private long _worldSpawn;
    private long _preparedFrame;
    private Fixed64 _preparedInvDelta;
    private bool _mayAdvance;
    private string? _preparedDestinationMap;
    private string? _preparedRegionalMap;
    private bool _busy;
    private bool _disposed;

    /// <summary>Binds a native circle and its host-accepted pose to a published top-down layer.</summary>
    protected Navigator2D(TrailblazerWorldContext context, string initialMapId, Guid globalId,
        NavigationPose2D initialPose, Fixed64 radius, Fixed64 moveSpeed, TraversalMedium medium,
        TraversalCapability capabilities = TraversalCapability.None)
        : this(context, initialMapId, globalId, initialPose,
            new NavigationAgentProfile2D(NavigationMode2D.TopDown, radius, Fixed64.Zero, Fixed64.Zero,
                Fixed64.Zero, Fixed64.Zero, NavigationCell.ToMedia(medium), capabilities),
            new NavigationMotorSettings2D(moveSpeed, Fixed64.Zero, Fixed64.Zero, Fixed64.Zero), medium) { }

    /// <summary>Binds native body geometry and motor tuning to a published layer of the same mode.</summary>
    /// <remarks>The explicit medium identifies navigation intent independently of physical grounding.</remarks>
    protected Navigator2D(TrailblazerWorldContext context, string initialMapId, Guid globalId,
        NavigationPose2D initialPose, NavigationAgentProfile2D profile, NavigationMotorSettings2D settings,
        TraversalMedium medium = TraversalMedium.Solid)
    {
        ChronicleGuard.ThrowIfNull(context, nameof(context));
        ChronicleGuard.ThrowIfArgument(string.IsNullOrWhiteSpace(initialMapId), nameof(initialMapId));
        ChronicleGuard.ThrowIfArgument(globalId == Guid.Empty, nameof(globalId));
        profile.Validate(nameof(profile));
        settings.Validate(profile.Mode);
        ChronicleGuard.ThrowIfArgument(NavigationCell.ToMedia(medium) == TraversalMedia.None, nameof(medium));
        ChronicleGuard.ThrowIfArgument((profile.AllowedMedia & NavigationCell.ToMedia(medium)) == 0, nameof(medium));
        ChronicleGuard.ThrowIfArgument(!context.Pathing.TryGetPlanarBinding(initialMapId, out _binding)
            || _binding.Mode != profile.Mode, nameof(initialMapId), "The initial map must be a materialized matching planar layer.");
        ChronicleGuard.ThrowIfArgument(profile.Mode == NavigationMode2D.SideView
            && initialPose.RotationRadians != Fixed64.Zero, nameof(initialPose));
        _context = context;
        _mapId = initialMapId;
        NavigationProfile = profile;
        _motorSettings = settings;
        _medium = medium;
        GlobalId = globalId;
        Pose = initialPose;
        SupportStatus = profile.Mode == NavigationMode2D.TopDown
            ? NavigationSupportStatus2D.NotApplicable : NavigationSupportStatus2D.Unavailable;
        _resetHook = context.RegisterOnReset($"Navigator2D:{globalId:N}", 0, ResetNavigation);
        context.World.OnReset += ResetWorld;
    }

    /// <inheritdoc/>
    public Guid GlobalId { get; }

    /// <inheritdoc/>
    public byte OccupantGroupId { get; set; }

    Vector3d IVoxelOccupant.Position => new(Pose.Center.X, _binding.EmbeddingY, Pose.Center.Y);

    private string CurrentMapId => _acceptedMapId ?? _mapId;

    /// <summary>Gets the most recently accepted pose.</summary>
    public NavigationPose2D Pose { get; private set; }

    /// <summary>Gets velocity derived from accepted displacement and the prepared timestep.</summary>
    public Vector2d Velocity { get; private set; }

    /// <summary>Gets accepted top-down rotation or the committed logical side-view facing.</summary>
    public Vector2d Facing => NavigationProfile.Mode == NavigationMode2D.SideView ? _facing
        : Vector2d.Rotate(Vector2d.Right, Pose.RotationRadians);

    /// <summary>Gets whether accepted motion reached the final center of the current trek.</summary>
    public bool HasArrived { get; private set; }

    /// <summary>Gets the exact selected route cost, excluding the initial approach to its first center.</summary>
    public Fixed64 GuideCost { get; private set; }

    /// <summary>Gets the latest acquisition or movement proof status.</summary>
    public NavigationGuideStatus? GuidanceStatus { get; private set; }

    /// <summary>Gets the latest cell resolved from an accepted pose.</summary>
    public NavigationCommittedCellState? LastCommittedCell { get; private set; }

    /// <summary>Reports entry into a different committed cell, area, or medium.</summary>
    public event Action<NavigationCommittedCellState?>? CommittedCellChanged;

    /// <summary>Releases the current route and clears arrival and guidance status.</summary>
    public void CancelGuidance()
    {
        EnsureIdle();
        ResetGuidance();
    }

    /// <summary>Submits an absolute target without publishing accepted motion.</summary>
    /// <remarks>
    /// Null uses guidance. A manual direction has magnitude at most one and replaces guidance.
    /// The host resolves manual motion. Guided targets also pass the navigation sweep.
    /// </remarks>
    public void PrepareFrameMotion(Vector2d? move = null)
        => PrepareFrameMotion(new NavigationInput2D(move, false));

    /// <summary>Stages native movement, facing and an admitted jump without publishing accepted state.</summary>
    public void PrepareFrameMotion(in NavigationInput2D input)
    {
        EnsureIdle();
        ValidateInput(input);
        BeginPrepareFrame();
        try
        {
            Vector2d center = Pose.Center;
            bool carrierSampled = false;
            if (input.Move is Vector2d move)
            {
                Fixed64 distance = Multiply(_motorSettings.MoveSpeed, _context.DeltaTime);
                center = Add(center, LimitRoundedStep(move * distance, distance));
                ResetGuidance();
            }
            else
            {
                if (_binding.Mode == NavigationMode2D.SideView && CanStartJump(input))
                    ResetGuidance();
                else
                {
                    AdvanceAcquisition();
                    if (_guide is NavigationGuideLease guide && !_heldTransition.HasValue)
                    {
                        if (_binding.Mode == NavigationMode2D.SideView)
                        {
                            carrierSampled = true;
                            _sideViewStagePrepared = TryPrepareSideViewStage(guide, out center);
                        }
                        else
                            center = PrepareGuidedCenter(new NavigationTopDownGuideLease(guide), out _mayAdvance);
                    }
                }
            }

            NavigationPose2D target = PrepareMotorTarget(center, input, carrierSampled);
            SubmitPreparedTarget(target, isAction: false);
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Publishes host-accepted pose, velocity, guidance progress, and occupancy once for the prepared frame.</summary>
    public void CommitFrameMotion()
    {
        EnsureAvailable();
        if (_pending is not NavigationMotionRequest2D request)
            throw new InvalidOperationException("No motion is prepared for commit.");
        _pending = null;
        _busy = true;
        try
        {
            ValidatePreparedFrame();
            NavigationMotionResult2D result = ReadAcceptedMotion(request);
            ValidatePreparedFrame();
            ValidateMotionResult(result);
            NavigationPose2D accepted = result.Accepted;
            if (!Vector2d.TrySubtract(accepted.Center, Pose.Center, out Vector2d displacement)
                || !Fixed64.TryMultiplyAdd(displacement.X, _preparedInvDelta, Fixed64.Zero, out Fixed64 x)
                || !Fixed64.TryMultiplyAdd(displacement.Y, _preparedInvDelta, Fixed64.Zero, out Fixed64 z))
                throw new OverflowException("The accepted displacement or velocity is outside the representable plane.");

            Fixed64 motorVertical = ResolveMotorVertical(displacement.Y);
            bool grounded = IsSupported(result) && motorVertical <= Fixed64.Zero;
            bool acceptedTarget = accepted.Center == request.Target.Center;
            string? actionMap = GetAcceptedActionMap(accepted.Center);
            bool? acceptedGuidedSegment = _preparedRegionalMap != null || _preparedVisibleAnchor.HasValue
                ? IsOnSegment(Pose.Center, request.Target.Center, accepted.Center) : null;
            string? preparedMap = (acceptedTarget ? _preparedDestinationMap : null)
                ?? (acceptedGuidedSegment == true ? _preparedRegionalMap : null);
            string preferredMap = actionMap ?? preparedMap ?? CurrentMapId;
            var cellStatus = _context.Pathing.ResolvePlanarCommittedCell(_mapId, _binding, accepted.Center, _medium,
                _guide.HasValue ? _policy : null, request.Frame, out var grid, out var voxel, out var cell,
                out var acceptedMapId, CurrentMapId, preferredMap);
            Vector2d previousCenter = Pose.Center;
            Pose = accepted;
            Velocity = new Vector2d(x, z);
            if (NavigationProfile.Mode == NavigationMode2D.SideView)
            {
                _facing = _preparedFacing;
                CommitMotorVertical(motorVertical, grounded);
            }
            PublishSupport(result, grounded);
            CommitActionMotion(actionMap != null);
            CommitGuidance(acceptedTarget, previousCenter, request.Target.Center, cell?.Address.MapId,
                acceptedGuidedSegment);
            if (cellStatus != NavigationCommittedCellResolveStatus.Unavailable)
            {
                _acceptedMapId = acceptedMapId ?? _acceptedMapId;
                PublishCell(grid, voxel, cell);
            }
            else
                DropExitedOccupancy();
        }
        finally
        {
            _busy = false;
        }
    }

    /// <summary>Submits the absolute request to the host motion resolver.</summary>
    protected abstract void SubmitMotionTarget(in NavigationMotionRequest2D request);

    /// <summary>Reads accepted pose and fresh support after resolution, or support at the current pose for a null request.</summary>
    protected abstract NavigationMotionResult2D ReadAcceptedMotion(NavigationMotionRequest2D? request);

    private void BeginPrepareFrame()
    {
        object lifetime = _context.ClockLifetime;
        long frame = _context.FrameCount;
        ChronicleGuard.ThrowIfTrue(frame == 0 || (ReferenceEquals(_clockLifetime, lifetime) && frame == _preparedFrame),
            message: "Prepare once after advancing the context frame.");
        _busy = true;
        _mayAdvance = false;
        _sideViewStagePrepared = false;
        _preparedDestinationMap = null;
        _preparedRegionalMap = null;
        _preparedVisibleAnchor = null;
        _preparedFacing = _facing;
        _preparedCarry = Vector2d.Zero;
        _preparedVertical = _verticalVelocity;
        _preparedJump = false;
        _clockLifetime = lifetime;
        _preparedFrame = frame;
        _worldSpawn = _context.World.SpawnToken;
        _preparedInvDelta = _context.InvDeltaTime;
    }

    private void SubmitPreparedTarget(NavigationPose2D target, bool isAction)
    {
        var request = new NavigationMotionRequest2D(_preparedFrame, target);
        ValidatePreparedFrame();
        _actionFrame = isAction;
        SubmitMotionTarget(request);
        ValidatePreparedFrame();
        _pending = request;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_disposed)
            return;
        ChronicleGuard.ThrowIfTrue(_busy, message: "Cannot dispose during a frame operation.");
        _disposed = true;
        _context.World.OnReset -= ResetWorld;
        _resetHook.Dispose();
        ResetNavigation();
    }

    private Vector2d PrepareGuidedCenter(NavigationTopDownGuideLease guide, out bool mayAdvance)
    {
        mayAdvance = false;
        var status = guide.TryGetCurrentStep(out var address, out Vector2d waypoint);
        if (status != NavigationGuideStatus.Success)
        {
            ApplyGuideStatus(status);
            return Pose.Center;
        }
        if (!TryValidateMovedOrigin(waypoint, address.MapId))
            return Pose.Center;
        string currentWaypointMap = address.MapId;
        bool atWaypoint = waypoint == Pose.Center;
        bool visible = false;
        if (atWaypoint)
        {
            GuidanceStatus = NavigationGuideStatus.Success;
            visible = TrySelectVisibleLeg(guide, out var nextAddress, out var nextCenter);
            if (visible)
            {
                address = nextAddress;
                waypoint = nextCenter;
            }
            else
            {
                // The current lease already certifies placement at this exact center.
                mayAdvance = true;
                _preparedDestinationMap = currentWaypointMap;
                return waypoint;
            }
        }
        if (!TryGetStepTarget(waypoint, out Vector2d target))
        {
            GuidanceStatus = NavigationGuideStatus.CostOverflow;
            return Pose.Center;
        }
        if (visible && target == Pose.Center)
        {
            mayAdvance = true;
            _preparedDestinationMap = currentWaypointMap;
            return target;
        }
        if (!_requiresConnector || target != waypoint)
        {
            status = _context.Pathing.ValidateTopDownMotion(_mapId, _binding, Pose.Center, target, NavigationProfile.Radius,
                _policy, _medium, NavigationProfile.Capabilities, _budget, (CurrentMapId, address.MapId));
        }
        if (status == NavigationGuideStatus.Success)
            status = guide.Status;
        ApplyGuideStatus(status);
        if (status != NavigationGuideStatus.Success)
            return Pose.Center;
        if (visible)
            _preparedVisibleAnchor = waypoint;
        bool reachedCenter = target == waypoint;
        mayAdvance = reachedCenter && !visible;
        if (reachedCenter)
            _preparedDestinationMap = address.MapId;
        if (_arrivalRadius.HasValue
            && guide.CurrentStepIndex == guide.StepCount - (visible ? 2 : 1))
            _preparedRegionalMap = address.MapId;
        return target;
    }

    private bool TryGetStepTarget(Vector2d waypoint, out Vector2d target)
    {
        target = Pose.Center;
        if (!Vector2d.TrySubtract(waypoint, Pose.Center, out Vector2d delta)
            || !delta.TryGetMagnitudeCeiling(out Fixed64 length))
            return false;
        target = waypoint;
        Fixed64 distance = Multiply(_motorSettings.MoveSpeed, _context.DeltaTime);
        if (length > distance)
        {
            // The positive divisor exceeds the step distance and bounds both component magnitudes.
            Fixed64.TryMultiplyDivide(delta.X, distance, length, out Fixed64 x);
            Fixed64.TryMultiplyDivide(delta.Y, distance, length, out Fixed64 z);
            Vector2d step = LimitRoundedStep(new Vector2d(x, z), distance);
            if (step == Vector2d.Zero && distance > Fixed64.Zero)
            {
                // A positive cap always admits one raw unit along the dominant component.
                Vector2d signs = Vector2d.Sign(delta);
                step = delta.X.Abs() >= delta.Y.Abs()
                    ? new Vector2d(signs.X * Fixed64.MinIncrement, Fixed64.Zero)
                    : new Vector2d(Fixed64.Zero, signs.Y * Fixed64.MinIncrement);
            }
            target = Pose.Center + step;
        }
        return true;
    }

    private static Vector2d LimitRoundedStep(Vector2d step, Fixed64 distance)
    {
        if (Vector2d.CompareDistanceSquared(Vector2d.Zero, step, Vector2d.Zero, new Vector2d(distance, Fixed64.Zero)) > 0)
        {
            // One final rounding can enlarge each component by at most half a raw unit.
            step -= Vector2d.Sign(step) * Fixed64.MinIncrement;
        }
        return step;
    }

    private void CommitGuidance(bool acceptedRequestedTarget, Vector2d previousCenter, Vector2d requestedCenter,
        string? acceptedMapId, bool? acceptedGuidedSegment)
    {
        if (_guide is not NavigationGuideLease route)
            return;
        if (_binding.Mode == NavigationMode2D.SideView)
        {
            CommitSideViewStage(route, acceptedRequestedTarget, previousCenter, requestedCenter, acceptedMapId,
                acceptedGuidedSegment);
            return;
        }
        var guide = new NavigationTopDownGuideLease(route);
        bool reachedTarget = _mayAdvance && acceptedRequestedTarget;
        if (!CommitVisibleLeg(guide, acceptedRequestedTarget, previousCenter, acceptedGuidedSegment.GetValueOrDefault(),
            ref reachedTarget))
            return;
        reachedTarget = reachedTarget || IsAcceptedRegionalArrival(route, acceptedMapId, acceptedGuidedSegment);
        bool arrived = reachedTarget && guide.CurrentStepIndex == guide.StepCount - 1;
        if (reachedTarget && HoldActionIfPresent(route))
            return;
        NavigationGuideStatus status = reachedTarget ? guide.TryAdvanceStep() : guide.Status;
        if (status != NavigationGuideStatus.Success)
            ApplyGuideStatus(status);
        else if (reachedTarget)
        {
            ReleaseConnectorValidation();
            if (arrived)
                FinishArrival();
        }
    }

    private bool IsAcceptedRegionalArrival(NavigationGuideLease route, string? acceptedMapId, bool? acceptedRegionalSegment) =>
        acceptedRegionalSegment == true && _arrivalRadius is Fixed64 radius && !_requiresConnector
        && !_heldTransition.HasValue && _preparedRegionalMap is string map && acceptedMapId == map
        && route.IsPlanarArrivalInRange(Pose.Center, map, radius);

    private void ApplyGuideStatus(NavigationGuideStatus status)
    {
        GuidanceStatus = status;
        if (status == NavigationGuideStatus.Stale)
            ReleaseGuide();
    }

    private void ReleaseGuide()
    {
        _guide?.Dispose();
        _guide = null;
        _sideViewContinuesStage = false;
        ReleaseConnectorValidation();
        ClearHeldAction();
    }

    private void ResetGuidance()
    {
        ReleaseGuide();
        CancelAcquisition();
        LeaveGroup();
        HasArrived = false;
        GuidanceStatus = null;
        GuideCost = default;
    }

    private bool CanPublishCell(Voxel? voxel) => voxel == null || voxel.HasVacancy
        || GridOccupantManager.TryGetOccupancyTicket(_context.World, this, voxel.WorldIndex, out _);

    private void PublishCell(VoxelGrid? grid, Voxel? voxel, NavigationCommittedCellState? cell, bool emitChange = true)
    {
        var previous = LastCommittedCell;
        LastCommittedCell = cell;
        WorldVoxelIndex? priorOccupancy = _occupied;
        WorldVoxelIndex? nextOccupancy = voxel?.WorldIndex;
        if (!Nullable.Equals(priorOccupancy, nextOccupancy))
        {
            // As in the spatial navigator, keep the occupant record alive across a successful handoff.
            _occupied = nextOccupancy;
            if (voxel != null && !NavigatorOccupancyTracker.TryRegisterResolvedVoxel(_context.World, grid!, voxel, this))
                _occupied = null;
            if (priorOccupancy is WorldVoxelIndex exited)
                GridOccupantManager.TryRemoveVoxelOccupant(_context.World, exited, this);
        }
        if (emitChange && !NavigationCommittedCellState.RepresentsSameCellEntry(previous, cell))
            CommittedCellChanged?.Invoke(cell);
    }

    private void DropExitedOccupancy()
    {
        if (_occupied is not WorldVoxelIndex index)
            return;
        GridCellPrism prism = default;
        if (_context.World.TryGetGridAndVoxel(index, out var grid, out var voxel))
            GridCellGeometry.TryGetPrism(grid!, voxel!.Index, out prism);
        if (!prism.Contains(((IVoxelOccupant)this).Position))
            ReleaseOccupancy();
    }

    private void ReleaseOccupancy()
    {
        if (_occupied is WorldVoxelIndex index)
        {
            _occupied = null;
            GridOccupantManager.TryRemoveVoxelOccupant(_context.World, index, this);
        }
    }

    private void ResetWorld()
    {
        _worldSpawn = 0;
        ResetNavigation();
    }

    private void ResetNavigation() => ResetNavigation(releaseOccupancy: true);

    private void ResetNavigation(bool releaseOccupancy)
    {
        _pending = null;
        ResetGuidance();
        _acceptedMapId = null;
        LastCommittedCell = null;
        _verticalVelocity = Fixed64.Zero;
        _jumpCount = 0;
        InvalidateSupport();
        if (releaseOccupancy)
            ReleaseOccupancy();
    }

    private void ValidatePreparedFrame()
    {
        ChronicleGuard.ThrowIfTrue(_context.IsDisposed || _worldSpawn != _context.World.SpawnToken
            || !ReferenceEquals(_clockLifetime, _context.ClockLifetime) || _preparedFrame != _context.FrameCount,
            message: "The prepared motion belongs to a different frame or lifetime.");
    }

    private void EnsureAvailable()
    {
        if (_disposed || _context.IsDisposed)
            throw new ObjectDisposedException(nameof(Navigator2D));
        ChronicleGuard.ThrowIfTrue(_busy || !_context.World.IsActive, message: "The controller cannot start a frame operation.");
    }

    private void EnsureIdle()
    {
        EnsureAvailable();
        ChronicleGuard.ThrowIfTrue(_pending.HasValue, message: "Commit the prepared motion before starting another operation.");
    }
}

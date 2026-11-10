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

/// <summary>Follows top-down circle routes through a host-owned motion resolver.</summary>
/// <remarks>
/// Advance the context, prepare all controllers, resolve host motion, commit all controllers, then run the late step.
/// Queue writes from motion and occupancy notifications for the next ordered host command boundary.
/// </remarks>
public abstract class Navigator2D : IDisposable, IVoxelOccupant
{
    private readonly TrailblazerWorldContext _context;
    private readonly string _mapId;
    private readonly NavigationPlanarBinding _binding;
    private readonly Fixed64 _radius;
    private readonly Fixed64 _moveSpeed;
    private readonly TraversalMedium _medium;
    private readonly TraversalCapability _capabilities;
    private readonly IDisposable _resetHook;
    private NavigationTopDownGuideLease? _guide;
    private NavigationAreaPolicyKey _policy;
    private NavigationWorkBudget _budget;
    private WorldVoxelIndex? _occupied;
    private NavigationMotionRequest2D? _pending;
    private object? _clockLifetime;
    private long _worldSpawn;
    private long _preparedFrame;
    private Fixed64 _preparedInvDelta;
    private bool _mayAdvance;
    private bool _busy;
    private bool _disposed;

    /// <summary>Binds a native circle and its host-accepted pose to a published top-down layer.</summary>
    protected Navigator2D(TrailblazerWorldContext context, string initialMapId, Guid globalId,
        NavigationPose2D initialPose, Fixed64 radius, Fixed64 moveSpeed, TraversalMedium medium,
        TraversalCapability capabilities = TraversalCapability.None)
    {
        ChronicleGuard.ThrowIfNull(context, nameof(context));
        ChronicleGuard.ThrowIfArgument(string.IsNullOrWhiteSpace(initialMapId), nameof(initialMapId));
        ChronicleGuard.ThrowIfArgument(globalId == Guid.Empty, nameof(globalId));
        ChronicleGuard.ThrowIfArgument(radius <= Fixed64.Zero, nameof(radius));
        ChronicleGuard.ThrowIfArgument(moveSpeed < Fixed64.Zero, nameof(moveSpeed));
        ChronicleGuard.ThrowIfArgument(NavigationCell.ToMedia(medium) == TraversalMedia.None, nameof(medium));
        ChronicleGuard.ThrowIfArgument((capabilities & ~NavigationCell.KnownCapabilities) != 0, nameof(capabilities));
        ChronicleGuard.ThrowIfArgument(!context.Pathing.TryGetTopDownBinding(initialMapId, out _binding),
            nameof(initialMapId), "The initial map must be a materialized top-down layer.");
        _context = context;
        _mapId = initialMapId;
        _radius = radius;
        _moveSpeed = moveSpeed;
        _medium = medium;
        _capabilities = capabilities;
        GlobalId = globalId;
        Pose = initialPose;
        _resetHook = context.RegisterOnReset($"Navigator2D:{globalId:N}", 0, ResetNavigation);
        context.World.OnReset += ResetWorld;
    }

    /// <inheritdoc/>
    public Guid GlobalId { get; }

    /// <inheritdoc/>
    public byte OccupantGroupId { get; set; }

    Vector3d IVoxelOccupant.Position => new(Pose.Center.X, _binding.EmbeddingY, Pose.Center.Y);

    /// <summary>Gets the most recently accepted pose.</summary>
    public NavigationPose2D Pose { get; private set; }

    /// <summary>Gets velocity derived from accepted displacement and the prepared timestep.</summary>
    public Vector2d Velocity { get; private set; }

    /// <summary>Gets the facing of the accepted pose.</summary>
    public Vector2d Facing => Vector2d.Rotate(Vector2d.Right, Pose.RotationRadians);

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

    /// <summary>Replaces guidance with an immediate route from the accepted center's cell to the destination.</summary>
    /// <remarks>The supplied budget bounds acquisition and each separate guided movement proof.</remarks>
    public NavigationGuideStatus ApplyGuidedTrekRequest(NavigationCellAddress destination, NavigationAreaPolicyKey policy,
        PathAlgorithm algorithm, NavigationWorkBudget budget)
    {
        EnsureIdle();
        policy.Validate(nameof(policy));
        ResetGuidance();
        var pathing = _context.Pathing;
        var graph = pathing.NavigationGraphStore.Current;
        var world = _context.World;
        var epoch = (world.SpawnToken, world.Version, world.ChangeSequence);
        var status = pathing.ResolveTopDownControllerStart(_mapId, _binding, Pose.Center, _radius, policy, _medium, _capabilities,
            budget, out NavigationCellAddress start);
        NavigationTopDownGuideLease guide = default;
        if (status == NavigationGuideStatus.Success)
        {
            status = _context.Guides.RequestTopDownGuide(start, destination, _radius, policy, _medium, _capabilities,
                algorithm, budget, out guide);
        }
        status = TrailblazerPathingService.ResolveTopDownSnapshotStatus(status, graph, pathing.NavigationGraphStore.Current,
            epoch, (world.SpawnToken, world.Version, world.ChangeSequence));
        GuidanceStatus = status;
        if (status == NavigationGuideStatus.Success)
        {
            _guide = guide;
            _policy = policy;
            _budget = budget;
            GuideCost = guide.TotalCost;
        }
        else
            guide.Dispose();
        return status;
    }

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
    {
        EnsureIdle();
        object lifetime = _context.ClockLifetime;
        long frame = _context.FrameCount;
        ChronicleGuard.ThrowIfTrue(frame == 0 || (ReferenceEquals(_clockLifetime, lifetime) && frame == _preparedFrame),
            message: "Prepare once after advancing the context frame.");
        _busy = true;
        try
        {
            Vector2d center = Pose.Center;
            _mayAdvance = false;
            if (move is Vector2d input)
            {
                ChronicleGuard.ThrowIfArgument(Vector2d.CompareDistanceSquared(Vector2d.Zero, input,
                    Vector2d.Zero, Vector2d.Right) > 0, nameof(move));
                Fixed64 distance = _moveSpeed * _context.DeltaTime;
                if (!Vector2d.TryAdd(center, LimitRoundedStep(input * distance, distance), out center))
                    throw new OverflowException("The manual target is outside the representable plane.");
                ResetGuidance();
            }
            else if (_guide is NavigationTopDownGuideLease guide)
            {
                center = PrepareGuidedCenter(guide, out _mayAdvance);
            }

            var request = new NavigationMotionRequest2D(frame, new NavigationPose2D(center, Pose.RotationRadians));
            _clockLifetime = lifetime;
            _preparedFrame = frame;
            _worldSpawn = _context.World.SpawnToken;
            _preparedInvDelta = _context.InvDeltaTime;
            SubmitMotionTarget(request);
            ValidatePreparedFrame();
            _pending = request;
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
            NavigationPose2D accepted = ReadAcceptedMotion(request);
            ValidatePreparedFrame();
            if (!Vector2d.TrySubtract(accepted.Center, Pose.Center, out Vector2d displacement)
                || !Fixed64.TryMultiplyAdd(displacement.X, _preparedInvDelta, Fixed64.Zero, out Fixed64 x)
                || !Fixed64.TryMultiplyAdd(displacement.Y, _preparedInvDelta, Fixed64.Zero, out Fixed64 z))
                throw new OverflowException("The accepted displacement or velocity is outside the representable plane.");

            var cellStatus = _context.Pathing.ResolveTopDownCommittedCell(_mapId, _binding, accepted.Center, _medium,
                _guide.HasValue ? _policy : null, request.Frame, out var grid, out var voxel, out var cell);
            Pose = accepted;
            Velocity = new Vector2d(x, z);
            CommitGuidance(_mayAdvance && accepted.Center == request.Target.Center);
            if (cellStatus != NavigationCommittedCellResolveStatus.Unavailable)
                PublishCell(grid, voxel, cell);
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

    /// <summary>Reads the matching accepted host pose after the shared resolution barrier.</summary>
    protected abstract NavigationPose2D ReadAcceptedMotion(in NavigationMotionRequest2D request);

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
        var status = guide.TryGetCurrentStep(out _, out Vector2d waypoint);
        if (status != NavigationGuideStatus.Success)
        {
            ApplyGuideStatus(status);
            return Pose.Center;
        }
        if (waypoint == Pose.Center)
        {
            // The current lease already certifies placement at this exact center.
            GuidanceStatus = NavigationGuideStatus.Success;
            mayAdvance = true;
            return waypoint;
        }
        if (!Vector2d.TrySubtract(waypoint, Pose.Center, out Vector2d delta)
            || !delta.TryGetMagnitudeCeiling(out Fixed64 length))
        {
            GuidanceStatus = NavigationGuideStatus.CostOverflow;
            return Pose.Center;
        }
        Fixed64 distance = _moveSpeed * _context.DeltaTime;
        Vector2d target = waypoint;
        if (length > distance)
        {
            // The positive divisor exceeds the step distance and bounds both component magnitudes.
            Fixed64.TryMultiplyDivide(delta.X, distance, length, out Fixed64 x);
            Fixed64.TryMultiplyDivide(delta.Y, distance, length, out Fixed64 z);
            target = Pose.Center + LimitRoundedStep(new Vector2d(x, z), distance);
        }
        status = _context.Pathing.ValidateTopDownMotion(_mapId, _binding, Pose.Center, target, _radius,
            _policy, _medium, _capabilities, _budget);
        if (status == NavigationGuideStatus.Success)
            status = guide.Status;
        ApplyGuideStatus(status);
        mayAdvance = status == NavigationGuideStatus.Success && target == waypoint;
        return status == NavigationGuideStatus.Success ? target : Pose.Center;
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

    private void CommitGuidance(bool reachedTarget)
    {
        if (_guide is not NavigationTopDownGuideLease guide)
            return;
        bool arrived = reachedTarget && guide.CurrentStepIndex == guide.StepCount - 1;
        NavigationGuideStatus status = reachedTarget ? guide.TryAdvanceStep() : guide.Status;
        if (status != NavigationGuideStatus.Success)
            ApplyGuideStatus(status);
        else if (arrived)
        {
            HasArrived = true;
            ReleaseGuide();
        }
    }

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
    }

    private void ResetGuidance()
    {
        ReleaseGuide();
        HasArrived = false;
        GuidanceStatus = null;
        GuideCost = default;
    }

    private void PublishCell(VoxelGrid? grid, Voxel? voxel, NavigationCommittedCellState? cell)
    {
        var previous = LastCommittedCell;
        LastCommittedCell = cell;
        WorldVoxelIndex? priorOccupancy = _occupied;
        WorldVoxelIndex? nextOccupancy = voxel?.WorldIndex;
        if (!Nullable.Equals(priorOccupancy, nextOccupancy))
        {
            // As in the spatial navigator, keep the occupant record alive across a successful handoff.
            _occupied = nextOccupancy;
            if (voxel != null && !grid!.TryAddVoxelOccupant(voxel, this))
                _occupied = null;
            if (priorOccupancy is WorldVoxelIndex exited)
                GridOccupantManager.TryRemoveVoxelOccupant(_context.World, exited, this);
        }
        if (!NavigationCommittedCellState.RepresentsSameCellEntry(previous, cell))
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

    private void ResetNavigation()
    {
        _pending = null;
        ResetGuidance();
        LastCommittedCell = null;
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

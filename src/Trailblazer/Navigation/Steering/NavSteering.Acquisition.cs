//=======================================================================
// NavSteering.Acquisition.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation.Steering;

public partial class NavSteering
{
    private NavigationAcquisitionMode _acquisitionMode;
    private AcquisitionState? _acquisitionState;

    // Immediate steering needs no scheduled ownership. Once created, retain this
    // state so cancellation/reuse never replaces a registered validation interest.
    private sealed class AcquisitionState
    {
        internal NavigationGuideRequest? Request;
        internal NavigationValidationInterest? ValidationInterest;
        internal object? Lifetime;
        internal object? AttemptLifetime;
        internal long AttemptFrame;
        internal object? AdoptionLifetime;
        internal long AdoptionFrame;
        internal bool GraphOnlyRetry;
        internal bool DeferAcquisition;
        internal FlowAdoptionSample? AdoptionSample;
        internal bool ConnectorProofForHeading;
        internal bool ConnectorWaiting;
    }

    // An adopted connector is registered before any host-visible boundary;
    // scheduler reset disposes its guide before clearing the owner. This only
    // skips impossible connector checks, never the live lease's validation.
    private bool HasNavigationConnector => _navigationGuideLease.HasValue
        && _acquisitionState?.ValidationInterest?.Owner != null
        && _navigationGuideLease.GetValueOrDefault().HasConnector;

    // Only Scheduled Flow adoption needs this large, single-heading value.
    // Keep it off the common steering object and reuse its holder after adoption.
    private sealed class FlowAdoptionSample
    {
        internal NavigationFlowSample? Value;
        internal Vector3d Origin;
        internal long GraphVersion;
        internal ulong WorldChangeSequence;
    }

    /// <summary>Gets the mandatory acquisition policy. Immediate is the default.</summary>
    public NavigationAcquisitionMode AcquisitionMode => _acquisitionMode;

    /// <summary>Gets whether durable movement intent is waiting for acquisition or current-foot validation.</summary>
    public bool IsAwaitingGuidance => _currentQuery.HasValue && _shouldMove
        && ((_acquisitionState != null && (_acquisitionState.ConnectorWaiting
                || _acquisitionState.DeferAcquisition || _acquisitionState.Request != null))
            || (_acquisitionMode == NavigationAcquisitionMode.Scheduled
            && _shouldRequestPathThisFrame && !HasNavigationGuidance && !_hasLineOfSightPath));

    /// <summary>Gets the latest mandatory acquisition or adoption outcome, if attempted.</summary>
    public NavigationGuideStatus? LastAcquisitionStatus { get; private set; }

    /// <summary>Changes mandatory acquisition policy, preserving already usable guidance.</summary>
    /// <remarks>Changing an in-flight policy cancels its ticket and retries no earlier than the next context frame.
    /// An adopted current-foot connector keeps its per-heading proof and fair operation-turn requirement in either mode.</remarks>
    public void ConfigureAcquisitionMode(NavigationAcquisitionMode mode)
    {
        ValidateAcquisitionMode(mode);
        if (_acquisitionMode == mode)
            return;
        bool pending = IsAwaitingGuidance;
        _acquisitionMode = mode;
        if (mode == NavigationAcquisitionMode.Scheduled)
            (_acquisitionState ??= new AcquisitionState()).Lifetime = _context?.ClockLifetime;
        if (!pending || HasNavigationGuidance)
            return;
        CancelAcquisition();
        (_acquisitionState ??= new AcquisitionState()).DeferAcquisition = true;
        _shouldRequestPathThisFrame = true;
        MarkAcquisitionAttempt();
    }

    private static void ValidateAcquisitionMode(NavigationAcquisitionMode mode) =>
        ChronicleGuard.ThrowIfArgumentOutOfRange(mode != NavigationAcquisitionMode.Immediate
            && mode != NavigationAcquisitionMode.Scheduled, (int)mode, nameof(mode));

    private void EnsureAcquisitionLifetime()
    {
        if ((_acquisitionMode != NavigationAcquisitionMode.Scheduled && !HasNavigationConnector)
            || !_currentQuery.HasValue)
            return;
        object lifetime = ResolveContext().ClockLifetime;
        AcquisitionState state = _acquisitionState ??= new AcquisitionState();
        if (state.Lifetime != null && !ReferenceEquals(state.Lifetime, lifetime))
        {
            _hasLineOfSightPath = false;
            PreparePathRetry();
        }
        state.Lifetime = lifetime;
    }

    private bool AttemptedThisFrame() => ReferenceEquals(_acquisitionState!.AttemptLifetime, ResolveContext().ClockLifetime)
        && _acquisitionState.AttemptFrame == ResolveContext().FrameCount;

    private void MarkAcquisitionAttempt()
    {
        _acquisitionState!.AttemptLifetime = ResolveContext().ClockLifetime;
        _acquisitionState.AttemptFrame = ResolveContext().FrameCount;
    }

    private bool ValidateScheduledMovementPath(Vector3d origin)
    {
        if (_hasLineOfSightPath)
            return true;
        // Successful population can restore Scheduled policy before its first
        // runtime use; the protected validation hook can also run before heading.
        AcquisitionState state = _acquisitionState ??= new AcquisitionState();
        NavigationGuideStatus? current = _navigationGuideLease?.Status ?? _navigationFlowFieldLease?.Status;
        if (current is NavigationGuideStatus.Stale or NavigationGuideStatus.CapacityExceeded)
            PreparePathRetry();
        if (!_shouldRequestPathThisFrame && state.Request == null)
            return true;

        if (state.Request == null)
        {
            if (AttemptedThisFrame())
                return true;
            MarkAcquisitionAttempt();
            PathQuery query = _currentQuery!.Value.WithStartState(origin, _currentQuery.Value.Traversal.StartMedium);
            _currentQuery = query;
            _destination = _requestedDestination = query.End.Position;
            LastAcquisitionStatus = ResolveContext().Guides.Scheduler.Schedule(query, out state.Request,
                !CanPathfind ? NavigationGuideRequestFlavor.NavigatorDirectOnly
                    : state.GraphOnlyRetry ? NavigationGuideRequestFlavor.NavigatorGraphOnly
                    : NavigationGuideRequestFlavor.NavigatorDirectFirst);
            state.GraphOnlyRetry = false;
            return IsRetryableAcquisition(LastAcquisitionStatus.Value);
        }

        NavigationGuideRequest request = state.Request;
        NavigationGuideScheduler scheduler = ResolveContext().Guides.Scheduler;
        // Pathing-only reset releases ownership without changing the clock or completed outcome.
        if (request.Owner != scheduler)
            return FinishAcquisitionFailure(NavigationGuideStatus.Cancelled);
        if (request.Status == NavigationGuideStatus.Pending)
            return true;
        if (request.Status != NavigationGuideStatus.Success)
            return FinishAcquisitionFailure(request.Status);
        NavigationValidationInterest interest = state.ValidationInterest ??= new NavigationValidationInterest();
        if (!scheduler.RegisterValidation(interest)
            || !scheduler.TryConsumeValidationTurn(interest))
            return true;
        state.AdoptionLifetime = ResolveContext().ClockLifetime;
        state.AdoptionFrame = ResolveContext().FrameCount;
        MarkAcquisitionAttempt();

        if (request.IsDirect)
        {
            // Direct tickets own no dependency stamp. Reprove against the current graph and foot.
            NavigationRayStatus direct = ResolveContext().Guides.TryGetDirectHeading(_currentQuery!.Value, origin, out _);
            if (direct == NavigationRayStatus.Success)
            {
                CancelAcquisition();
                _hasLineOfSightPath = true;
                CompleteAcquisition();
                return true;
            }
            if (direct == NavigationRayStatus.Blocked && CanPathfind)
            {
                FinishAcquisitionFailure(NavigationGuideStatus.LocalRecoveryRequired);
                state.GraphOnlyRetry = true;
                return true;
            }
            return FinishAcquisitionFailure(direct == NavigationRayStatus.Blocked
                ? NavigationGuideStatus.NoPath : NavigationGuideStatusMapper.ToPublic(direct));
        }

        if (request.Algorithm == PathAlgorithm.FlowField)
        {
            if (!request.TryTakeFlowField(out NavigationFlowFieldLease? acquired))
                return FinishAcquisitionFailure(request.Status);
            NavigationFlowFieldLease lease = acquired!.Value;
            TrailblazerWorldContext context = ResolveContext();
            long graphVersion = context.Pathing.NavigationGraphStore.Current.GraphVersion;
            ulong worldChangeSequence = context.World.ChangeSequence;
            var meter = new GuideSampleWorkMeter(context.Settings.GuideSampleBudget);
            NavigationGuideStatus status = lease.TrySample(origin, ref meter, out NavigationFlowSample sample);
            if (status != NavigationGuideStatus.Success)
            {
                lease.Dispose();
                return FinishAcquisitionFailure(status);
            }
            CancelAcquisition();
            _navigationFlowFieldLease = lease;
            FlowAdoptionSample adoptionSample = state.AdoptionSample ??= new FlowAdoptionSample();
            adoptionSample.Origin = origin;
            adoptionSample.GraphVersion = graphVersion;
            adoptionSample.WorldChangeSequence = worldChangeSequence;
            adoptionSample.Value = sample;
        }
        else
        {
            if (!request.TryTakeGuide(out NavigationGuideLease? lease))
                return FinishAcquisitionFailure(request.Status);
            if (origin != _currentQuery!.Value.Start.Position)
            {
                NavigationGuideStatus status = ResolveContext().Guides.TryProveAStarConnector(
                    _currentQuery.Value, origin, lease!.Value);
                if (status != NavigationGuideStatus.Success)
                {
                    lease!.Value.Dispose();
                    return FinishAcquisitionFailure(status);
                }
            }
            return CompleteAStarAdoption(scheduler, lease!.Value);
        }
        CompleteAcquisition();
        return true;
    }

    private NavigationGuideStatus SampleAdoptedFlowGuidance(
        FlowAdoptionSample adoptionSample,
        NavigationFlowFieldLease flowGuide,
        Vector3d position,
        out NavigationFlowSample sample)
    {
        sample = adoptionSample.Value.GetValueOrDefault();
        adoptionSample.Value = null;
        // Virtual hooks can change the requested foot, graph, world or lease
        // after adoption. Only reuse its sample at the same boundary.
        TrailblazerWorldContext context = ResolveContext();
        if (position == adoptionSample.Origin
            && context.Pathing.NavigationGraphStore.Current.GraphVersion == adoptionSample.GraphVersion
            && context.World.ChangeSequence == adoptionSample.WorldChangeSequence
            && flowGuide.Status == NavigationGuideStatus.Success)
            return NavigationGuideStatus.Success;

        // Adoption already spent this heading's sample. A host hook changed its
        // inputs: wait rather than sample twice or invalidate a still-current
        // lease solely for an unrelated graph/world revision. Normal dependency
        // validation and sampling resume on the next context frame.
        sample = default;
        _acquisitionState!.DeferAcquisition = true;
        MarkAcquisitionAttempt();
        return NavigationGuideStatus.LocalRecoveryRequired;
    }

    internal bool CompleteAStarAdoption(NavigationGuideScheduler scheduler, NavigationGuideLease lease)
    {
        // The caller has transferred/proved this lease and recorded its consumed adoption turn.
        CancelAcquisition();
        _navigationGuideLease = lease;
        if (lease.HasConnector)
        {
            AcquisitionState state = _acquisitionState ??= new AcquisitionState();
            NavigationValidationInterest interest = state.ValidationInterest ??= new NavigationValidationInterest();
            if (!scheduler.RegisterConnectorValidation(interest, lease))
            {
                ReleaseNavigationGuidance();
                return FinishAcquisitionFailure(NavigationGuideStatus.Cancelled);
            }
            state.ConnectorProofForHeading = true;
        }
        CompleteAcquisition();
        return true;
    }

    private void CompleteAcquisition()
    {
        _shouldRequestPathThisFrame = false;
        LastAcquisitionStatus = NavigationGuideStatus.Success;
        _pathCheckCooldown = PathRecheckCooldownFrames;
        ArmNextPhasedPathRecheck();
    }

    private static bool IsRetryableAcquisition(NavigationGuideStatus status) => status is
        NavigationGuideStatus.Pending or NavigationGuideStatus.Stale or NavigationGuideStatus.CapacityExceeded
        or NavigationGuideStatus.LocalRecoveryRequired or NavigationGuideStatus.Cancelled;

    private bool FinishAcquisitionFailure(NavigationGuideStatus status)
    {
        CancelAcquisition();
        LastAcquisitionStatus = status;
        _shouldRequestPathThisFrame = true;
        MarkAcquisitionAttempt();
        return IsRetryableAcquisition(status);
    }

    private void CancelAcquisition()
    {
        AcquisitionState? state = _acquisitionState;
        if (state == null)
            return;
        state.Request?.Dispose();
        state.Request = null;
        state.ValidationInterest?.Owner?.ReleaseValidation(state.ValidationInterest);
        if (state.AdoptionSample != null)
            state.AdoptionSample.Value = null;
        state.ConnectorProofForHeading = false;
        state.ConnectorWaiting = false;
    }

    private bool ValidateConnectorHeading(Vector3d origin)
    {
        // Both callers establish the A* lease immediately before this call.
        NavigationGuideLease guide = _navigationGuideLease!.Value;
        AcquisitionState? state = _acquisitionState;
        NavigationValidationInterest? interest = state?.ValidationInterest;
        if (interest?.Owner == null || !guide.HasConnector)
            return true;
        if (state!.DeferAcquisition && AttemptedThisFrame())
            return false;
        if (state!.ConnectorProofForHeading)
        {
            state.ConnectorProofForHeading = false;
            return true;
        }
        state.ConnectorWaiting = true;
        _targetDirection = Vector3d.Zero;
        TrailblazerWorldContext context = ResolveContext();
        if (ReferenceEquals(state.AdoptionLifetime, context.ClockLifetime) && state.AdoptionFrame == context.FrameCount)
            return false;
        NavigationGuideScheduler scheduler = context.Guides.Scheduler;
        if (!scheduler.RegisterConnectorValidation(interest, guide) || !scheduler.TryConsumeValidationTurn(interest))
            return false;
        state.AdoptionLifetime = context.ClockLifetime;
        state.AdoptionFrame = context.FrameCount;
        NavigationGuideStatus status = context.Guides.TryProveAStarConnector(_currentQuery!.Value, origin, guide);
        LastAcquisitionStatus = status;
        if (status == NavigationGuideStatus.Success)
        {
            state.ConnectorWaiting = false;
            return true;
        }
        // A stale local segment gets a zero-heading frame before fresh bounded reproof.
        // Keep the route: physical host movement alone must not restart full searches.
        if (status == NavigationGuideStatus.CapacityExceeded
            || (status == NavigationGuideStatus.Stale && guide.Status != NavigationGuideStatus.Stale))
            return false;
        // Unlike a stale local segment, a stale shared route cannot be reproved.
        // Retire it only after the charged turn and defer replacement this frame.
        PreparePathRetry();
        MarkAcquisitionAttempt();
        state.DeferAcquisition = true;
        if (!IsRetryableAcquisition(status))
            HandleInvalidPath("Current-foot connector cannot be proved within the query contract.");
        return false;
    }

    private void DeferAStarHeading(NavigationGuideStatus status)
    {
        if (status != NavigationGuideStatus.LocalRecoveryRequired)
            PreparePathRetry();
        LastAcquisitionStatus = status;
        (_acquisitionState ??= new AcquisitionState()).DeferAcquisition = true;
        MarkAcquisitionAttempt();
    }

    internal void ValidateActualMedium(TraversalMedium medium)
    {
        if ((_acquisitionMode == NavigationAcquisitionMode.Scheduled || HasNavigationConnector)
            && _currentQuery is PathQuery query && query.Traversal.StartMedium != medium
            && _pendingTransitionOwner?.PendingTransition == null)
        {
            LastAcquisitionStatus = NavigationGuideStatus.Unsupported;
            HandleInvalidPath("Unplanned traversal medium change requires a new guided intent.");
        }
    }
}

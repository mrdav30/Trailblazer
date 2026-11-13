//=======================================================================
// Navigator2D.SideViewGuidance.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Spatial;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <content>Follows the exact stages of a retained side-view capsule guide.</content>
public abstract partial class Navigator2D
{
    private NavigationCellAddress _sideViewSource;
    private Vector2d _sideViewConnectorStart;
    private VoxelIndex? _sideViewPreparedSupport;
    private bool _sideViewStagePrepared;
    private bool _sideViewContinuesStage;

    /// <summary>Replaces guidance from the accepted canonical Solid support anchor to the destination.</summary>
    /// <remarks>
    /// The accepted center must initially match the source anchor and have walkable host support.
    /// A scheduled guide can return from a moved, grounded center on the source support before advancing.
    /// A positive arrival radius allows reachable support anchors within the inclusive range on the
    /// destination map. The accepted pose can complete the final proved leg within that range with walkable support.
    /// </remarks>
    public NavigationGuideStatus ApplySideViewGuidedTrekRequest(NavigationCellAddress source,
        NavigationCellAddress destination, NavigationAreaPolicyKey policy, PathAlgorithm algorithm, NavigationWorkBudget budget,
        Fixed64? arrivalRadius = null) =>
        ApplySideViewGuidedTrekRequest(source, destination, policy, algorithm, budget, allowTransitions: false,
            arrivalRadius: arrivalRadius);

    /// <summary>Replaces side-view guidance with optional authored actions held for host execution.</summary>
    /// <remarks>A positive arrival radius affects only the final destination.</remarks>
    public NavigationGuideStatus ApplySideViewGuidedTrekRequest(NavigationCellAddress source,
        NavigationCellAddress destination, NavigationAreaPolicyKey policy, PathAlgorithm algorithm, NavigationWorkBudget budget,
        bool allowTransitions, Fixed64? arrivalRadius = null)
    {
        EnsureIdle();
        if (_binding.Mode != NavigationMode2D.SideView || _medium != TraversalMedium.Solid)
            return NavigationGuideStatus.InvalidProfile;
        policy.Validate(nameof(policy));
        BeginTrek(destination, policy, algorithm, budget, arrivalRadius);
        _allowTransitions = allowTransitions;
        _sideViewSource = source;
        return StartAcquisition();
    }

    private NavigationGuideStatus BeginSideViewAcquisition(out NavigationGuideLease guide)
    {
        guide = default;
        if (!IsGrounded)
            return _resumeSideView ? NavigationGuideStatus.Pending : NavigationGuideStatus.InvalidStart;
        if (_sideViewSource.MapId == null)
            return NavigationGuideStatus.InvalidStart;
        var status = _context.Pathing.ResolveSideViewControllerAnchor(_sideViewSource, _mapId, _binding, CurrentMapId,
            NavigationProfile, _policy, _budget, out var center);
        if (_resumeSideView && (status != NavigationGuideStatus.Success | center != Pose.Center))
        {
            // The accepted pose can lie beyond the previous anchor's support. The fresh
            // witness identifies its owner, then the connector proves actual motion to the anchor.
            status = _context.Pathing.ResolveSideViewResumeSource(_mapId, _binding, CurrentMapId, Support!.Value.Point,
                Pose.Center, NavigationProfile, _policy, out var restoredSource, out center);
            if (status == NavigationGuideStatus.Success)
                _sideViewSource = restoredSource;
        }
        if (status != NavigationGuideStatus.Success)
            return status;
        if (center != Pose.Center && !_resumeSideView)
            return NavigationGuideStatus.InvalidStart;
        if (_resumeSideView)
        {
            _queuedOrigin = center;
            _sideViewConnectorStart = Pose.Center;
        }
        if (AcquisitionMode == NavigationAcquisitionMode.Scheduled)
        {
            _queuedOrigin = center;
            status = _context.Guides.ScheduleSideViewGuide(_sideViewSource, _routeGoal, NavigationProfile, _policy,
                _algorithm, _budget, out var request, _allowTransitions, _arrivalRadius);
            _guideRequest = request;
            return status;
        }
        status = _context.Guides.RequestSideViewGuide(_sideViewSource, _routeGoal, NavigationProfile, _policy,
            _algorithm, _budget, out var acquired, _allowTransitions, _arrivalRadius);
        guide = acquired.RouteLease;
        return status;
    }

    private NavigationGuideStatus AdoptScheduledSideView(NavigationSideViewGuideRequest request)
    {
        if (!request.TryTakeSideViewGuide(out var guide))
            return FailedScheduledTransfer(request);
        if (!IsGrounded)
        {
            guide.Dispose();
            return _resumeSideView ? NavigationGuideStatus.Pending : NavigationGuideStatus.InvalidStart;
        }
        _sideViewConnectorStart = Pose.Center;
        return AdoptPlanarGuide(guide.RouteLease);
    }

    private bool TryPrepareSideViewStage(NavigationGuideLease guide, out Vector2d center)
    {
        center = Pose.Center;
        _preparedCarry = ReadCarrierDisplacement();
        if (_preparedCarry != Vector2d.Zero)
            return RejectSideViewStage(NavigationGuideStatus.InvalidStart);
        var status = guide.GetSideViewStage(out var source, out var target);
        if (status != NavigationGuideStatus.Success)
        {
            ApplyGuideStatus(status);
            return false;
        }
        Vector2d start = _requiresConnector ? _sideViewConnectorStart : source.Position.ToVector2d();
        Vector2d end = target.Position.ToVector2d();
        bool requiresSupport = !source.IsSideViewStepBend && (Pose.Center == start || !target.IsSideViewStepBend);
        bool exactOrigin = IsOnSegment(start, end, Pose.Center);
        if ((requiresSupport && !IsGrounded) || (!exactOrigin && !_sideViewContinuesStage))
            return RejectSideViewStage(NavigationGuideStatus.InvalidStart);
        if (!TryGetStepTarget(end, out var bounded))
            return RejectSideViewStage(NavigationGuideStatus.CostOverflow);
        if (bounded == Pose.Center && bounded != end)
        {
            GuidanceStatus = NavigationGuideStatus.Pending;
            return false;
        }
        bool onRemainingSegment = exactOrigin && IsOnSegment(Pose.Center, end, bounded);
        bool rounded = !onRemainingSegment;
        Vector2d stepTarget = bounded;
        if (rounded && bounded != end)
            bounded = RoundSideViewTarget(start, end);
        if (_requiresConnector || rounded)
        {
            if (_requiresConnector)
            {
                status = NavigationGuideStatus.Pending;
                if (TryConsumeConnectorValidationTurn())
                {
                    bool connectorTargetOnSegment = exactOrigin && bounded == stepTarget
                        ? onRemainingSegment : IsOnSegment(Pose.Center, end, bounded);
                    status = _context.Pathing.ValidateSideViewControllerConnector(target.Address, _mapId, _binding, CurrentMapId,
                        start, Pose.Center, end, connectorTargetOnSegment ? null : bounded,
                        NavigationProfile, _policy, _budget, allowRoundedOrigin: _resumeSideView);
                }
            }
            else
                status = _context.Pathing.ValidateSideViewControllerMotion(source.Address, target.Address, _mapId, _binding,
                    CurrentMapId, Pose.Center, bounded, NavigationProfile, _policy, _budget);
            if (status is not (NavigationGuideStatus.Success or NavigationGuideStatus.Pending))
                return RejectSideViewStage(status);
            if (status == NavigationGuideStatus.Success)
                status = guide.Status;
            if (status != NavigationGuideStatus.Success)
            {
                ApplyGuideStatus(status);
                return false;
            }
        }

        GuidanceStatus = NavigationGuideStatus.Success;
        center = bounded;
        _mayAdvance = center == end;
        if (_mayAdvance && !target.IsSideViewStepBend)
        {
            _preparedDestinationMap = target.Address.MapId;
            _sideViewPreparedSupport = target.IsSideViewGroundedBend ? null : target.Address.Index;
        }
        // Side-view bends are written before their canonical destination support.
        if (_arrivalRadius.HasValue && guide.CurrentStepIndex == guide.StepCount - 1)
            _preparedRegionalMap = target.Address.MapId;
        return true;
    }

    private void CommitSideViewStage(NavigationGuideLease guide, bool acceptedTarget,
        Vector2d previousCenter, Vector2d requestedCenter, string? acceptedMapId, bool? acceptedRegionalSegment)
    {
        if (!_mayAdvance || !acceptedTarget)
        {
            if (_sideViewStagePrepared && IsGrounded
                && IsAcceptedRegionalArrival(guide, acceptedMapId, acceptedRegionalSegment))
            {
                var arrivalStatus = guide.TryAdvancePlanarStep();
                ApplyGuideStatus(arrivalStatus);
                if (arrivalStatus == NavigationGuideStatus.Success)
                    FinishArrival();
                return;
            }
            var current = guide.Status;
            if (current != NavigationGuideStatus.Success)
                ApplyGuideStatus(current);
            if (current == NavigationGuideStatus.Stale)
                return;
            // Snapshot pressure can delay validation without invalidating an accepted clip.
            if (_sideViewStagePrepared)
            {
                _sideViewContinuesStage = acceptedRegionalSegment ?? IsOnSegment(previousCenter, requestedCenter, Pose.Center);
                if (!_sideViewContinuesStage)
                    RejectSideViewStage(NavigationGuideStatus.InvalidStart);
            }
            else if (Pose.Center != previousCenter)
                _sideViewContinuesStage = false;
            return;
        }

        _sideViewContinuesStage = false;
        string? supportMap = _preparedDestinationMap;
        if (supportMap != null && !IsGrounded)
        {
            RejectSideViewStage(NavigationGuideStatus.InvalidEnd);
            return;
        }
        // Accepted support is a candidate for a fresh proof even if the old route became stale.
        bool reachedCanonicalSupport = supportMap != null && _sideViewPreparedSupport.HasValue;
        if (reachedCanonicalSupport)
        {
            VoxelIndex support = _sideViewPreparedSupport!.Value;
            _sideViewSource = new NavigationCellAddress(supportMap!, support);
        }
        if (_allowTransitions && HoldActionIfPresent(guide))
        {
            _resumeSideView &= !reachedCanonicalSupport;
            return;
        }
        bool arrived = guide.CurrentStepIndex == guide.StepCount - 1;
        var status = guide.TryAdvancePlanarStep();
        if (status != NavigationGuideStatus.Success)
        {
            ApplyGuideStatus(status);
            return;
        }
        _resumeSideView &= !reachedCanonicalSupport;
        ReleaseConnectorValidation();
        if (arrived)
            FinishArrival();
    }

    private bool RejectSideViewStage(NavigationGuideStatus status)
    {
        ReleaseGuide();
        GuidanceStatus = status;
        return false;
    }
}

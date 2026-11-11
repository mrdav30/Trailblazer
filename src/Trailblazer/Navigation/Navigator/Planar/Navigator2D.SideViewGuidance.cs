//=======================================================================
// Navigator2D.SideViewGuidance.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Diagnostics;
using FixedMathSharp;
using GridForge.Spatial;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <content>Follows the exact stages of a retained side-view capsule guide.</content>
public abstract partial class Navigator2D
{
    private NavigationCellAddress _sideViewSource;
    private VoxelIndex _sideViewPreparedSupport;
    private bool _sideViewStagePrepared;

    /// <summary>Replaces guidance from the accepted canonical Solid support anchor to the destination.</summary>
    /// <remarks>
    /// The accepted center must match the source anchor and have walkable host support.
    /// Guides follow their exact stages, including step bends. Use the configured acquisition mode.
    /// </remarks>
    public NavigationGuideStatus ApplySideViewGuidedTrekRequest(NavigationCellAddress source,
        NavigationCellAddress destination, NavigationAreaPolicyKey policy, PathAlgorithm algorithm, NavigationWorkBudget budget)
    {
        EnsureIdle();
        if (_binding.Mode != NavigationMode2D.SideView || _medium != TraversalMedium.Solid)
            return NavigationGuideStatus.InvalidProfile;
        policy.Validate(nameof(policy));
        BeginTrek(destination, policy, algorithm, budget);
        _sideViewSource = source;
        return StartAcquisition();
    }

    private NavigationGuideStatus BeginSideViewAcquisition(out NavigationGuideLease guide)
    {
        guide = default;
        if (!IsGrounded || _sideViewSource.MapId == null)
            return NavigationGuideStatus.InvalidStart;
        var status = _context.Pathing.ResolveSideViewControllerAnchor(_sideViewSource, _mapId, _binding, CurrentMapId,
            NavigationProfile, _policy, _budget, out var center);
        if (status != NavigationGuideStatus.Success)
            return status;
        if (center != Pose.Center)
            return NavigationGuideStatus.InvalidStart;
        if (AcquisitionMode == NavigationAcquisitionMode.Scheduled)
        {
            _queuedOrigin = center;
            status = _context.Guides.ScheduleSideViewGuide(_sideViewSource, _routeGoal, NavigationProfile, _policy,
                _algorithm, _budget, out var request);
            _guideRequest = request;
            return status;
        }
        status = _context.Guides.RequestSideViewGuide(_sideViewSource, _routeGoal, NavigationProfile, _policy,
            _algorithm, _budget, out var acquired);
        guide = acquired.RouteLease;
        return status;
    }

    private NavigationGuideStatus AdoptScheduledSideView(NavigationSideViewGuideRequest request)
    {
        if (!request.TryTakeSideViewGuide(out var guide))
            return FailedScheduledTransfer(request);
        if (Pose.Center != _queuedOrigin || !IsGrounded)
        {
            guide.Dispose();
            return NavigationGuideStatus.InvalidStart;
        }
        AdoptGuide(guide.RouteLease);
        return NavigationGuideStatus.Success;
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
        Vector2d start = source.Position.ToVector2d();
        Vector2d end = target.Position.ToVector2d();
        bool requiresSupport = !source.IsSideViewBend && (Pose.Center == start || !target.IsSideViewBend);
        if ((requiresSupport && !IsGrounded) || !IsOnSegment(start, end, Pose.Center))
            return RejectSideViewStage(NavigationGuideStatus.InvalidStart);
        // The pose lies on a certified stage whose checked length fits the route's cost.
        bool boundedTarget = TryGetStepTarget(end, out var bounded);
        Debug.Assert(boundedTarget);
        // shortcut: inherit the capsule proof only on its exact segment, until rounded slope targets have their own proof.
        if (!IsOnSegment(Pose.Center, end, bounded))
            return RejectSideViewStage(NavigationGuideStatus.Unsupported);
        if (bounded == Pose.Center && bounded != end)
        {
            GuidanceStatus = NavigationGuideStatus.Pending;
            return false;
        }

        GuidanceStatus = NavigationGuideStatus.Success;
        center = bounded;
        _mayAdvance = center == end;
        if (_mayAdvance && !target.IsSideViewBend)
        {
            _preparedDestinationMap = target.Address.MapId;
            _sideViewPreparedSupport = target.Address.Index;
        }
        return true;
    }

    private void CommitSideViewStage(NavigationGuideLease guide, bool acceptedTarget,
        Vector2d previousCenter, Vector2d requestedCenter)
    {
        if (!_mayAdvance || !acceptedTarget)
        {
            var current = guide.Status;
            if (current != NavigationGuideStatus.Success)
                ApplyGuideStatus(current);
            else if (_sideViewStagePrepared && !IsOnSegment(previousCenter, requestedCenter, Pose.Center))
                RejectSideViewStage(NavigationGuideStatus.InvalidStart);
            return;
        }

        string? supportMap = _preparedDestinationMap;
        if (supportMap != null && !IsGrounded)
        {
            RejectSideViewStage(NavigationGuideStatus.InvalidEnd);
            return;
        }
        // Accepted support is a candidate for a fresh proof even if the old route became stale.
        if (supportMap != null)
            _sideViewSource = new NavigationCellAddress(supportMap, _sideViewPreparedSupport);
        bool arrived = guide.CurrentStepIndex == guide.StepCount - 1;
        var status = guide.TryAdvancePlanarStep();
        if (status != NavigationGuideStatus.Success)
        {
            ApplyGuideStatus(status);
            return;
        }
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

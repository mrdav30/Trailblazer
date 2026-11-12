//=======================================================================
// Navigator2D.ProfileReplacement.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.ExceptionServices;
using FixedMathSharp;
using GridForge.Grids;
using GridForge.Spatial;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <content>Coordinates the host's atomic planar posture and capability replacement.</content>
public abstract partial class Navigator2D
{
    /// <summary>Replaces the active profile and host envelope while preserving the nominal side-view foot.</summary>
    /// <remarks>
    /// Call between frames. A successful replacement retires old guidance proofs and preserves destination intent.
    /// Refresh side-view support at the new pose before using it for locomotion.
    /// Notification failures propagate after commitment.
    /// </remarks>
    public NavigationProfileReplacementStatus TryReplaceNavigationProfile(NavigationAgentProfile2D replacement)
    {
        if (_disposed | _context.IsDisposed | !_context.World.IsActive | _busy | _pending.HasValue)
            return NavigationProfileReplacementStatus.UnsafeLifecycle;
        if (_heldTransition.HasValue && ValidateHeldAction() != NavigationGuideStatus.Stale)
            return NavigationProfileReplacementStatus.ActionInProgress;
        if (replacement.Mode != _binding.Mode || replacement.Radius <= Fixed64.Zero)
            return NavigationProfileReplacementStatus.InvalidProfile;
        if (replacement == NavigationProfile)
            return NavigationProfileReplacementStatus.Unchanged;
        if ((replacement.AllowedMedia & NavigationCell.ToMedia(_medium)) == 0)
            return NavigationProfileReplacementStatus.CurrentMediumNotAllowed;
        if (_hasIntent && _requestedGoal.MapId == null)
            return NavigationProfileReplacementStatus.GuidanceIncompatible;
        var center = Pose.Center;
        if (_binding.Mode == NavigationMode2D.SideView)
        {
            if (!Fixed64.TrySubtract(center.Y, NavigationProfile.NominalFootOffset, out var foot)
                || !Fixed64.TryAdd(foot, replacement.NominalFootOffset, out var height))
                return NavigationProfileReplacementStatus.FootAnchorUnrepresentable;
            center.Y = height;
        }

        var pathing = _context.Pathing;
        NavigationAreaPolicyKey? policy = _hasIntent ? _policy : null;
        var resolved = pathing.ResolvePlanarCommittedCell(_mapId, _binding, center, _medium, policy,
            _context.FrameCount, out var grid, out var voxel, out var cell, out var acceptedMap, CurrentMapId, CurrentMapId,
            replacement, _budget, IsGrounded ? Support!.Value.Point : null,
            detachedReplacementOrigin: _hasIntent ? null : Pose.Center);
        if (resolved == NavigationCommittedCellResolveStatus.Unavailable || !CanPublishCell(voxel))
            return NavigationProfileReplacementStatus.NavigationClearanceRejected;
        if (!cell.HasValue && _hasIntent)
            return NavigationProfileReplacementStatus.NavigationClearanceRejected;

        return CommitProfileReplacement(replacement, center, grid, voxel, cell, acceptedMap);
    }

    /// <summary>Commits the complete host-owned envelope and invokes the supplied state publisher synchronously.</summary>
    /// <remarks>
    /// Invoke the publisher once after committing the physical envelope and before host notifications.
    /// Return post-commit notification failures separately. A rejection preserves host state and skips publication.
    /// A host that supports runtime replacement must override this method.
    /// </remarks>
    protected virtual bool TryCommitPhysicalProfileReplacement(NavigationAgentProfile2D replacement,
        Vector2d replacementCenter, Action publishNavigationState, out Exception? notificationException)
    {
        notificationException = null;
        return false;
    }

    private NavigationProfileReplacementStatus CommitProfileReplacement(NavigationAgentProfile2D replacement,
        Vector2d center, VoxelGrid? grid, Voxel? voxel, NavigationCommittedCellState? cell, string? acceptedMap)
    {
        bool published = false;
        bool open = true;
        Exception? navigationFailure = null;
        _busy = true;
        try
        {
            bool accepted = TryCommitPhysicalProfileReplacement(replacement, center, Publish, out var physicalFailure);
            ChronicleGuard.ThrowIfTrue(accepted != published, message: "The host must publish exactly once on acceptance.");
            if (!accepted)
                return NavigationProfileReplacementStatus.PhysicalFitRejected;
            if (navigationFailure != null && physicalFailure != null)
                throw new AggregateException(navigationFailure, physicalFailure);
            if ((navigationFailure ?? physicalFailure) is Exception failure)
                ExceptionDispatchInfo.Capture(failure).Throw();
            return NavigationProfileReplacementStatus.Applied;
        }
        finally
        {
            open = false;
            _busy = false;
        }

        void Publish()
        {
            ChronicleGuard.ThrowIfTrue(!open | published, message: "The host publisher belongs to one synchronous commit.");
            published = true;
            NavigationProfile = replacement;
            Pose = new NavigationPose2D(center, Pose.RotationRadians);
            ReleaseGuide();
            ReleaseRequest();
            if (_hasIntent && !HasArrived)
            {
                GuideCost = default;
                GuidanceStatus = NavigationGuideStatus.Pending;
                _lastAcquisitionFrame = -1;
                _formationGoalChosen = false;
                _routeGoal = _requestedGoal;
                _resumeSideView = _binding.Mode == NavigationMode2D.SideView;
                RefreshGroupTarget();
            }
            PublishSupport(new NavigationMotionResult2D(Pose, _binding.Mode == NavigationMode2D.SideView
                ? NavigationSupportStatus2D.Unavailable : NavigationSupportStatus2D.NotApplicable), false);
            _acceptedMapId = acceptedMap ?? _acceptedMapId;
            try
            {
                PublishCell(grid, voxel, cell);
            }
            catch (Exception exception)
            {
                navigationFailure = exception;
            }
        }
    }
}

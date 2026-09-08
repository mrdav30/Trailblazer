//=======================================================================
// Navigator.ProfileReplacement.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using System;
using System.Runtime.ExceptionServices;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

public abstract partial class Navigator
{
    /// <summary>
    /// Attempts to replace the active navigation profile and every dependent
    /// controller value as one synchronous transaction.
    /// </summary>
    /// <remarks>
    /// Call only between fixed frames, after committed motion and before the
    /// next <see cref="Simulate"/>. The current foot point, physical medium,
    /// stable identity, destination, controller instances, and unrelated
    /// locomotion state are preserved. Expected rejection does not throw,
    /// queue work, or partially mutate the Navigator. State is coherent before
    /// post-commit callbacks run. A host callback exception therefore propagates
    /// after commitment and must not be interpreted as a rejected replacement.
    /// </remarks>
    /// <param name="replacement">The complete immutable replacement profile.</param>
    /// <returns>The exact replacement outcome.</returns>
    public NavigationProfileReplacementStatus TryReplaceNavigationProfile(
        NavigationAgentProfile replacement)
    {
        if (!IsActive
            || Motor!.TraversalInProgress
            || _positionDelta != Vector3d.Zero
            || _locomotionDisplacement != Vector3d.Zero
            || _rotationDelta != FixedQuaternion.Identity)
        {
            return NavigationProfileReplacementStatus.UnsafeLifecycle;
        }

        if (!replacement.IsValid)
            return NavigationProfileReplacementStatus.InvalidProfile;

        if (replacement == _navigationProfile)
            return NavigationProfileReplacementStatus.Unchanged;

        if (_pendingTransition.HasValue)
            return NavigationProfileReplacementStatus.ActionInProgress;

        if (!TryGetProfileReplacementPositions(
                replacement.Shape.RootToFootOffsetY,
                out Vector3d footPosition,
                out Vector3d replacementPosition,
                out Vector3d replacementLastPosition))
        {
            return NavigationProfileReplacementStatus.FootAnchorUnrepresentable;
        }

        TraversalMedium currentMedium = _frameCondition.Medium;
        TraversalMedia currentMediumFlag = NavigationCell.ToMedia(currentMedium);
        if (currentMediumFlag == TraversalMedia.None
            || (replacement.AllowedMedia & currentMediumFlag) == 0)
        {
            return NavigationProfileReplacementStatus.CurrentMediumNotAllowed;
        }

        if (!Steering!.TryPrepareProfileReplacement(
                replacement,
                footPosition,
                currentMedium,
                _lastCommittedCell?.Address.MapId,
                out PathQuery? replacementQuery))
        {
            return NavigationProfileReplacementStatus.GuidanceIncompatible;
        }

        if (!HasNavigationClearance(
                replacement,
                footPosition,
                currentMedium,
                replacementQuery))
        {
            return NavigationProfileReplacementStatus.NavigationClearanceRejected;
        }

        TrailblazerWorldContext context = RequireContext();
        if (!NavigatorOccupancyTracker.CanProjectRoot(
                context.World,
                Position,
                replacementPosition))
        {
            return NavigationProfileReplacementStatus.NavigationClearanceRejected;
        }

        if (!TryCommitPhysicalProfileReplacement(
                replacement,
                replacementPosition,
                () => CommitNavigationProfileReplacement(
                    replacement,
                    replacementPosition,
                    replacementLastPosition,
                    replacementQuery,
                    context),
                out Exception? physicalNotificationException))
            return NavigationProfileReplacementStatus.PhysicalFitRejected;

        if (physicalNotificationException != null)
            ExceptionDispatchInfo.Capture(physicalNotificationException).Throw();

        return NavigationProfileReplacementStatus.Applied;
    }

    /// <summary>
    /// Attempts to commit the host-owned physical envelope for a validated
    /// profile replacement.
    /// </summary>
    /// <remarks>
    /// An override must synchronously return <see langword="true"/> only after
    /// the complete physical shape and root are committed and
    /// <paramref name="publishNavigationState"/> has run exactly once. Invoke
    /// that publisher after the physical state is committed, but before any
    /// physical post-commit host notifications. A rejection must return
    /// <see langword="false"/>, leave host state unchanged, and not invoke the
    /// publisher. The engine-agnostic base has no physical body and therefore
    /// publishes and accepts the replacement. Return post-commit notification
    /// failures separately so all notifications can finish before the caller
    /// rethrows them.
    /// </remarks>
    /// <param name="replacement">The validated replacement profile.</param>
    /// <param name="replacementRootPosition">The root that preserves the current foot point.</param>
    /// <param name="publishNavigationState">
    /// Publishes the already validated Navigator state once physical state is
    /// committed and before physical host notifications run.
    /// </param>
    /// <param name="notificationException">
    /// A host notification failure raised after the physical commit, or
    /// <see langword="null"/>.
    /// </param>
    /// <returns>Whether the host committed the complete physical replacement.</returns>
    protected virtual bool TryCommitPhysicalProfileReplacement(
        NavigationAgentProfile replacement,
        Vector3d replacementRootPosition,
        Action publishNavigationState,
        out Exception? notificationException)
    {
        notificationException = null;
        publishNavigationState();
        return true;
    }

    private void CommitNavigationProfileReplacement(
        NavigationAgentProfile replacement,
        Vector3d replacementPosition,
        Vector3d replacementLastPosition,
        PathQuery? replacementQuery,
        TrailblazerWorldContext context)
    {
        NavigationAgentProfile previous = _navigationProfile;
        Vector3d previousPosition = _position;
        _navigationProfile = replacement;
        _position = replacementPosition;
        _lastPosition = replacementLastPosition;
        Turning!.UpdateRadius(replacement.Shape.Radius);
        bool stoppedClimb = Motor!.ApplyNavigationCapabilityLoss(
            previous.Capabilities,
            replacement.Capabilities,
            ref _frameRequest);
        Steering!.CommitProfileReplacement(
            replacementQuery,
            replacementPosition,
            replacement.Shape.Radius);
        NavigatorOccupancyTracker.UpdateAfterRootProjection(
            context.World,
            this,
            previousPosition,
            replacementPosition);
        try
        {
            RebuildCommittedCellState(emitChange: true);
        }
        finally
        {
            Motor.NotifyNavigationCapabilityLoss(stoppedClimb);
        }
    }

    private bool TryGetProfileReplacementPositions(
        Fixed64 replacementRootToFootOffset,
        out Vector3d footPosition,
        out Vector3d replacementPosition,
        out Vector3d replacementLastPosition)
    {
        bool representable = Fixed64.TrySubtract(
                _position.Y,
                BodyShape.RootToFootOffsetY,
                out Fixed64 footY)
            & Fixed64.TrySubtract(
                _lastPosition.Y,
                BodyShape.RootToFootOffsetY,
                out Fixed64 lastFootY);
        if (!representable
            || !Fixed64.TryAdd(
                footY,
                replacementRootToFootOffset,
                out Fixed64 replacementY)
            || !Fixed64.TryAdd(
                lastFootY,
                replacementRootToFootOffset,
                out Fixed64 replacementLastY))
        {
            footPosition = default;
            replacementPosition = default;
            replacementLastPosition = default;
            return false;
        }

        footPosition = new Vector3d(_position.X, footY, _position.Z);
        replacementPosition = new Vector3d(_position.X, replacementY, _position.Z);
        replacementLastPosition = new Vector3d(
            _lastPosition.X,
            replacementLastY,
            _lastPosition.Z);
        return true;
    }

    private bool HasNavigationClearance(
        NavigationAgentProfile replacement,
        Vector3d footPosition,
        TraversalMedium currentMedium,
        PathQuery? replacementQuery)
    {
        TrailblazerWorldContext context = RequireContext();
        if (!NavigatorOccupancyTracker.TryResolveVoxel(
                context.World,
                footPosition,
                out VoxelGrid? grid,
                out Voxel? voxel))
        {
            return true;
        }

        string? mapId = replacementQuery?.Start.MapId
            ?? _lastCommittedCell?.Address.MapId;
        if (mapId == null)
        {
            NavigationCommittedCellResolveStatus rootStatus =
                context.Pathing.TryResolveCommittedCell(
                    grid!.Configuration.ToGridKey(),
                    voxel!.WorldIndex,
                    out NavigationCellAddress rootAddress,
                    out _,
                    out _);
            if (rootStatus == NavigationCommittedCellResolveStatus.Unavailable)
                return false;
            if (rootStatus == NavigationCommittedCellResolveStatus.Resolved)
                mapId = rootAddress.MapId;
        }

        if (mapId == null)
            return true;

        if (!TryGetNavigationCellAtFoot(
                context,
                grid!,
                voxel!,
                mapId,
                footPosition,
                currentMedium,
                out NavigationGraphCellState state))
        {
            return !replacementQuery.HasValue;
        }

        if (!state.IsPresent || state.IsBlocked)
        {
            return false;
        }

        NavigationCell cell = state.Cell;
        TraversalMedia currentMediumFlag = NavigationCell.ToMedia(currentMedium);
        if ((cell.Media & currentMediumFlag) == 0
            || (replacement.Capabilities & cell.RequiredCapabilities)
                != cell.RequiredCapabilities
            || replacement.Shape.Radius > cell.RadiusClearance
            || replacement.Shape.Height > cell.HeightClearance)
        {
            return false;
        }

        if (!replacementQuery.HasValue)
            return true;

        // Published policies contain exactly the context's configured area
        // count, and map publication rejects cells outside that same range.
        return context.Pathing.TryResolveNavigationAreaPolicy(
                replacementQuery.Value.AreaPolicy,
                out NavigationAreaPolicy? policy)
            && policy!.GetRequiredRule(cell.Area).IsAllowed;
    }

    private static bool TryGetNavigationCellAtFoot(
        TrailblazerWorldContext context,
        VoxelGrid grid,
        Voxel voxel,
        string mapId,
        Vector3d footPosition,
        TraversalMedium currentMedium,
        out NavigationGraphCellState state)
    {
        grid.Configuration.TryNormalize(out NormalizedGridConfiguration binding);
        VoxelIndex baseIndex = voxel.Index;
        if (TryGetNavigationCellAtExactFloor(
                context,
                binding,
                mapId,
                baseIndex,
                footPosition.Y,
                out state))
        {
            return true;
        }

        var aboveIndex = new VoxelIndex(
            baseIndex.x,
            baseIndex.y + 1,
            baseIndex.z);
        if (TryGetNavigationCellAtExactFloor(
                context,
                binding,
                mapId,
                aboveIndex,
                footPosition.Y,
                out state))
        {
            return true;
        }

        if (currentMedium != TraversalMedium.Solid)
        {
            return context.Pathing.TryGetNavigationGraphCellState(
                mapId,
                baseIndex,
                out state);
        }

        state = default;
        return false;
    }

    private static bool TryGetNavigationCellAtExactFloor(
        TrailblazerWorldContext context,
        NormalizedGridConfiguration binding,
        string mapId,
        VoxelIndex index,
        Fixed64 footY,
        out NavigationGraphCellState state)
    {
        if (binding.TryGetCellPrism(index, out GridCellPrism prism)
            && prism.VerticalMin == footY)
        {
            return context.Pathing.TryGetNavigationGraphCellState(
                mapId,
                index,
                out state);
        }

        state = default;
        return false;
    }
}

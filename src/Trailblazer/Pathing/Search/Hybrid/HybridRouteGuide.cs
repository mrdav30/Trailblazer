//=======================================================================
// HybridRouteGuide.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Sequences graph Flow leases and retained volume guides for one hybrid route.</summary>
internal sealed class HybridRouteGuide : IDisposable
{
    private readonly HybridRoutePlan _plan;
    private int _stepIndex;
    private NavigationFlowFieldLease? _surfaceLease;
    private VolumeGuide? _volumeGuide;
    private TrailblazerWorldContext? _volumeGuideContext;
    private bool _disposed;

    internal bool IsComplete => _stepIndex >= _plan.Steps.Length;

    internal PathQuery? ActiveSurfaceQuery => !_disposed
        && (uint)_stepIndex < (uint)_plan.Steps.Length
        ? _plan.Steps[_stepIndex].SurfaceQuery
        : null;

    internal HybridRouteGuide(HybridRoutePlan plan)
    {
        if (plan == null || plan.Steps.Length == 0)
            throw new ArgumentException("A hybrid route guide requires at least one route step.", nameof(plan));

        _plan = plan;
    }

    internal NavigationGuideStatus TrySample(
        Vector3d actualFootPosition,
        GuideSampleWorkBudget budget,
        out Vector3d heading)
    {
        heading = Vector3d.Zero;
        if (_disposed)
            return NavigationGuideStatus.Stale;

        int remainingAdvances = _plan.Steps.Length;
        while (_stepIndex < _plan.Steps.Length && remainingAdvances-- > 0)
        {
            HybridRouteStep step = _plan.Steps[_stepIndex];
            if (HasReachedTarget(actualFootPosition, step))
            {
                ReleaseActiveResource();
                _stepIndex++;
                continue;
            }

            if (step.SurfaceQuery == null && step.VolumeRequest == null)
            {
                heading = (step.WaypointPosition - actualFootPosition).Normalized;
                return NavigationGuideStatus.Success;
            }

            if (step.SurfaceQuery is PathQuery surfaceQuery)
                return TrySampleSurface(step.Context, surfaceQuery, actualFootPosition, budget, out heading);

            if (step.VolumeRequest != null)
                return TrySampleVolume(step, actualFootPosition, out heading);

            return NavigationGuideStatus.Unsupported;
        }

        return NavigationGuideStatus.Success;
    }

    private NavigationGuideStatus TrySampleSurface(
        TrailblazerWorldContext context,
        PathQuery query,
        Vector3d actualFootPosition,
        GuideSampleWorkBudget budget,
        out Vector3d heading)
    {
        heading = Vector3d.Zero;
        if (_surfaceLease == null)
        {
            ReleaseActiveResource();
            NavigationGuideStatus status = context.Guides.RequestFlowField(query, out NavigationFlowFieldLease? lease);
            if (status != NavigationGuideStatus.Success || lease == null)
            {
                lease?.Dispose();
                return status;
            }

            _surfaceLease = lease.Value;
        }

        return _surfaceLease.Value.TrySample(actualFootPosition, budget, out heading);
    }

    private NavigationGuideStatus TrySampleVolume(
        HybridRouteStep step,
        Vector3d actualFootPosition,
        out Vector3d heading)
    {
        heading = Vector3d.Zero;
        if (_volumeGuide == null)
        {
            ReleaseActiveResource();
            if (!step.Context.Guides.RequestGuide(step.VolumeRequest!, out VolumeGuide? guide)
                || guide == null)
            {
                return NavigationGuideStatus.NoPath;
            }

            _volumeGuide = guide;
            _volumeGuideContext = step.Context;
        }

        AdvanceReachedVolumeWaypoints(actualFootPosition, step.Context.VoxelSize * Fixed64.Half);
        heading = _volumeGuide.GetCurrentWaypointDirection(actualFootPosition);
        return heading == Vector3d.Zero
            ? NavigationGuideStatus.NoPath
            : NavigationGuideStatus.Success;
    }

    private void AdvanceReachedVolumeWaypoints(Vector3d actualFootPosition, Fixed64 completionDistance)
    {
        VolumeGuide guide = _volumeGuide!;
        Fixed64 completionDistanceSquared = completionDistance * completionDistance;
        int remainingAdvances = guide.ActiveWaypoints.Length;
        while (remainingAdvances-- > 0
            && guide.TryGetWaypointAt(guide.CurrentWaypointIndex, out AStarWaypoint waypoint)
            && (waypoint.Position - actualFootPosition).MagnitudeSquared <= completionDistanceSquared)
        {
            guide.AdvanceWaypoint();
        }
    }

    private static bool HasReachedTarget(Vector3d actualFootPosition, HybridRouteStep step)
    {
        Vector3d target = step.SurfaceQuery?.End.Position
            ?? step.VolumeRequest?.TargetPosition
            ?? step.WaypointPosition;
        Fixed64 completionDistance = step.Context.VoxelSize * Fixed64.Half;
        return (target - actualFootPosition).MagnitudeSquared <= completionDistance * completionDistance;
    }

    private void ReleaseActiveResource()
    {
        if (_surfaceLease != null)
        {
            _surfaceLease.Value.Dispose();
            _surfaceLease = null;
        }

        if (_volumeGuide != null)
        {
            _volumeGuideContext!.Guides.ReturnGuide(_volumeGuide);
            _volumeGuide = null;
            _volumeGuideContext = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        ReleaseActiveResource();
    }
}

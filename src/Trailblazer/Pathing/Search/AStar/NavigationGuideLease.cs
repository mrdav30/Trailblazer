//=======================================================================
// NavigationGuideLease.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Owns one graph-backed A* payload reference and its guide-local step cursor.</summary>
public readonly struct NavigationGuideLease : IDisposable
{
    private readonly NavigationAStarGuideLease? _inner;
    private readonly long _generation;

    internal NavigationGuideLease(NavigationAStarGuideLease inner)
    {
        _inner = inner;
        _generation = inner.Generation;
    }

    /// <summary>Gets the lease's current dependency-validated status.</summary>
    public NavigationGuideStatus Status => NavigationGuideStatusMapper.ToPublic(
        _inner?.GetStatus(_generation) ?? NavigationAStarQueryStatus.Stale);

    /// <summary>Gets the zero-based guide-local step cursor.</summary>
    public int CurrentStepIndex => _inner?.GetCurrentWaypointOrdinal(_generation) ?? -1;

    /// <summary>Gets the immutable number of movement and action steps in the leased route.</summary>
    public int StepCount => _inner?.GetWaypointCount(_generation) ?? 0;

    /// <summary>Gets the immutable exact route payload cost, excluding any Navigator current-foot connector.</summary>
    public Fixed64 TotalCost => _inner?.GetTotalCost(_generation) ?? Fixed64.Zero;

    internal bool HasConnector => _inner?.HasConnector(_generation) ?? false;

    internal bool IsPlanarArrivalInRange(Vector2d accepted, string mapId, Fixed64 radius) =>
        _inner?.IsPlanarArrivalInRange(_generation, accepted, mapId, radius) ?? false;

    internal NavigationGuideStatus GetSideViewStage(out NavigationAStarGuidePoint source,
        out NavigationAStarGuidePoint target)
    {
        source = target = default;
        return NavigationGuideStatusMapper.ToPublic(
            _inner?.GetSideViewStage(_generation, out source, out target) ?? NavigationAStarQueryStatus.Stale);
    }

    internal bool TryGetNextOrdinaryPoint(out NavigationAStarGuidePoint point)
    {
        point = default;
        return _inner?.TryGetNextOrdinaryPoint(_generation, out point) ?? false;
    }

    internal NavigationGuideStatus BeginConnector(out NavigationGuideStep step)
    {
        step = default;
        return _inner?.BeginConnector(_generation, out step) ?? NavigationGuideStatus.Stale;
    }

    internal NavigationGuideStatus InstallConnector(NavigationAStarConnectorProof proof, ref long reservation) =>
        _inner?.InstallConnector(_generation, proof, ref reservation) ?? NavigationGuideStatus.Stale;

    /// <summary>Gets the current dependency-validated movement or action step.</summary>
    public NavigationGuideStatus TryGetCurrentStep(out NavigationGuideStep step)
    {
        NavigationAStarGuideLease? inner = _inner;
        if (inner == null)
        {
            step = default;
            return NavigationGuideStatus.Stale;
        }

        return NavigationGuideStatusMapper.ToPublic(
            inner.TryGetCurrentStep(
                _generation,
                out step));
    }

    internal NavigationGuideStatus TryGetCurrentStep(Vector3d connectorOrigin, out NavigationGuideStep step)
    {
        step = default;
        return _inner?.TryGetCurrentStep(_generation, connectorOrigin, out step) ?? NavigationGuideStatus.Stale;
    }

    /// <summary>Advances one ordinary movement step; a pending action cannot be crossed.</summary>
    public NavigationGuideStatus TryAdvanceStep()
    {
        return _inner == null
            ? NavigationGuideStatus.Stale
            : NavigationGuideStatusMapper.ToPublic(
                _inner.TryAdvanceWaypoint(_generation));
    }

    internal NavigationGuideStatus TryAdvancePlanarStep()
    {
        NavigationAStarQueryStatus status = _inner?.TryAdvanceWaypoint(_generation) ?? NavigationAStarQueryStatus.Stale;
        return status == NavigationAStarQueryStatus.Pending
            ? NavigationGuideStatus.Pending
            : NavigationGuideStatusMapper.ToPublic(status);
    }

    internal NavigationGuideStatus TryGetPendingPlanarTransition(out NavigationTransitionInstruction2D instruction)
    {
        instruction = default;
        var status = TryGetCurrentStep(out var step);
        if (status != NavigationGuideStatus.Success)
            return status;
        if (!step.HasTransition)
            return NavigationGuideStatus.Pending;
        instruction = new NavigationTransitionInstruction2D(step.Transition);
        return NavigationGuideStatus.Success;
    }

    /// <summary>Completes the exact current pending semantic action.</summary>
    public NavigationGuideStatus CompletePendingTransition(
        in NavigationTransitionInstruction instruction) =>
        _inner == null
            ? NavigationGuideStatus.Stale
            : NavigationGuideStatusMapper.ToPublic(
                _inner.CompletePendingTransition(_generation, instruction));

    /// <inheritdoc />
    public void Dispose() => _inner?.Dispose(_generation);
}

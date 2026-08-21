//=======================================================================
// NavigationFlowFieldLease.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Owns one generation-validated graph flow field and its sampling cursor.</summary>
public readonly struct NavigationFlowFieldLease : IDisposable
{
    private readonly NavigationFlowFieldGuideLease? _inner;
    private readonly ulong _generation;

    internal NavigationFlowFieldLease(NavigationFlowFieldGuideLease inner)
    {
        _inner = inner;
        _generation = inner.Generation;
    }

    /// <summary>Gets the lease's current dependency-validated status.</summary>
    public NavigationGuideStatus Status =>
        _inner?.GetStatus(_generation) ?? NavigationGuideStatus.Stale;

    /// <summary>Gets the immutable integration cost at the resolved origin.</summary>
    public Fixed64 OriginIntegrationCost =>
        _inner?.GetOriginIntegrationCost(_generation) ?? Fixed64.Zero;

    /// <summary>Samples deterministic movement guidance or one pending semantic action.</summary>
    public NavigationGuideStatus TrySample(
        Vector3d actualFootPosition,
        GuideSampleWorkBudget budget,
        out NavigationFlowSample sample)
    {
        NavigationFlowFieldGuideLease? inner = _inner;
        if (inner == null)
        {
            sample = default;
            return NavigationGuideStatus.Stale;
        }
        var meter = new GuideSampleWorkMeter(budget);
        NavigationGuideStatus status = inner.TrySample(
            _generation,
            actualFootPosition,
            ref meter,
            out sample);
        return status;
    }

    internal NavigationGuideStatus TrySample(
        Vector3d actualFootPosition,
        ref GuideSampleWorkMeter meter,
        out NavigationFlowSample sample)
    {
        NavigationFlowFieldGuideLease? inner = _inner;
        if (inner == null)
        {
            sample = default;
            return NavigationGuideStatus.Stale;
        }
        return inner.TrySample(
            _generation,
            actualFootPosition,
            ref meter,
            out sample);
    }

    /// <summary>Completes the exact current pending semantic action.</summary>
    public NavigationGuideStatus CompletePendingTransition(
        in NavigationTransitionInstruction instruction) =>
        _inner?.CompletePendingTransition(_generation, instruction)
            ?? NavigationGuideStatus.Stale;

    /// <inheritdoc />
    public void Dispose() => _inner?.Dispose(_generation);
}

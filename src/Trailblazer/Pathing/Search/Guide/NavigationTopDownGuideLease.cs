//=======================================================================
// NavigationTopDownGuideLease.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Owns an immediate top-down route and its cursor over canonical cell centers.</summary>
/// <remarks>Copies share the cursor and ownership. Each successful request owns an independent lease.</remarks>
public readonly struct NavigationTopDownGuideLease : IDisposable
{
    private readonly NavigationGuideLease _lease;

    internal NavigationTopDownGuideLease(NavigationAStarGuideLease lease) => _lease = new NavigationGuideLease(lease);

    /// <summary>Gets the current snapshot-validated status.</summary>
    public NavigationGuideStatus Status => _lease.Status;

    /// <summary>Gets the zero-based cursor, or -1 for an unbound or disposed lease.</summary>
    public int CurrentStepIndex => _lease.CurrentStepIndex;

    /// <summary>Gets the number of centers in the route, including both endpoints.</summary>
    public int StepCount => _lease.StepCount;

    /// <summary>Gets the exact fixed-point route cost.</summary>
    public Fixed64 TotalCost => _lease.TotalCost;

    /// <summary>Reads the current snapshot-validated cell and its center in world X/Z.</summary>
    public NavigationGuideStatus TryGetCurrentStep(out NavigationCellAddress address, out Vector2d center)
    {
        NavigationGuideStatus status = _lease.TryGetCurrentStep(out var step);
        address = step.Address;
        center = step.Position.ToVector2d();
        return status;
    }

    /// <summary>Advances to the next center on a current route. At the last center this succeeds without moving.</summary>
    public NavigationGuideStatus TryAdvanceStep() => _lease.TryAdvanceStep();

    /// <inheritdoc />
    public void Dispose() => _lease.Dispose();
}

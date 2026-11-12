//=======================================================================
// NavigationSideViewGuideLease.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Owns a side-view route cursor over proved upright capsule center waypoints.</summary>
/// <remarks>Copies share the cursor and ownership. Each successful request owns an independent lease.</remarks>
public readonly struct NavigationSideViewGuideLease : IDisposable
{
    private readonly NavigationGuideLease _lease;

    internal NavigationSideViewGuideLease(NavigationAStarGuideLease lease) : this(new NavigationGuideLease(lease)) { }

    internal NavigationSideViewGuideLease(NavigationGuideLease lease) => _lease = lease;

    internal NavigationGuideLease RouteLease => _lease;

    /// <summary>Gets the current snapshot-validated status.</summary>
    public NavigationGuideStatus Status => _lease.Status;

    /// <summary>Gets the zero-based cursor, or -1 for an unbound or disposed lease.</summary>
    public int CurrentStepIndex => _lease.CurrentStepIndex;

    /// <summary>Gets the number of waypoints, including both endpoints and any bends.</summary>
    public int StepCount => _lease.StepCount;

    /// <summary>Gets the summed stage lengths and forward destination entry costs.</summary>
    public Fixed64 TotalCost => _lease.TotalCost;

    /// <summary>Reads the current snapshot-validated capsule center in world X/Z.</summary>
    /// <remarks>
    /// A step bend names its lower support owner and can be airborne.
    /// A grounded valley bend names its left support owner in either direction.
    /// </remarks>
    public NavigationGuideStatus TryGetCurrentStep(out NavigationCellAddress address, out Vector2d center)
    {
        var status = _lease.TryGetCurrentStep(out var step);
        address = step.Address;
        center = step.Position.ToVector2d();
        return status;
    }

    /// <summary>Advances one waypoint, or returns Pending when an action holds the current center.</summary>
    public NavigationGuideStatus TryAdvanceStep() => _lease.TryAdvancePlanarStep();

    /// <summary>Gets the current action, or returns Pending at an ordinary waypoint.</summary>
    public NavigationGuideStatus TryGetPendingTransition(out NavigationTransitionInstruction2D instruction) =>
        _lease.TryGetPendingPlanarTransition(out instruction);

    /// <summary>Completes the exact held action after the host accepts its supported landing.</summary>
    public NavigationGuideStatus CompletePendingTransition(in NavigationTransitionInstruction2D instruction) =>
        _lease.CompletePendingTransition(instruction.Instruction);

    /// <inheritdoc />
    public void Dispose() => _lease.Dispose();
}

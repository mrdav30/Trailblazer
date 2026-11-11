//=======================================================================
// NavigationTopDownGuideLease.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Owns a top-down route and its cursor over canonical cell centers and held actions.</summary>
/// <remarks>Copies share the cursor and ownership. Each successful request owns an independent lease.</remarks>
public readonly struct NavigationTopDownGuideLease : IDisposable
{
    private readonly NavigationGuideLease _lease;

    internal NavigationTopDownGuideLease(NavigationAStarGuideLease lease) : this(new NavigationGuideLease(lease)) { }

    internal NavigationTopDownGuideLease(NavigationGuideLease lease) => _lease = lease;

    internal NavigationGuideLease RouteLease => _lease;

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

    /// <summary>Advances to the next center, or returns Pending when an action holds the current center.</summary>
    public NavigationGuideStatus TryAdvanceStep() => _lease.TryAdvancePlanarStep();

    internal bool TryGetNextOrdinaryStep(out NavigationCellAddress address, out Vector2d center)
    {
        bool found = _lease.TryGetNextOrdinaryPoint(out var point);
        address = point.Address;
        center = point.Position.ToVector2d();
        return found;
    }

    /// <summary>Gets the exact held action, or returns Pending at an ordinary center.</summary>
    public NavigationGuideStatus TryGetPendingTransition(out NavigationTransitionInstruction2D instruction)
    {
        instruction = default;
        NavigationGuideStatus status = _lease.TryGetCurrentStep(out var step);
        if (status != NavigationGuideStatus.Success)
            return status;
        if (!step.HasTransition)
            return NavigationGuideStatus.Pending;
        instruction = new NavigationTransitionInstruction2D(step.Transition);
        return NavigationGuideStatus.Success;
    }

    /// <summary>Completes the exact held action after the host accepts its destination pose.</summary>
    public NavigationGuideStatus CompletePendingTransition(in NavigationTransitionInstruction2D instruction) =>
        _lease.CompletePendingTransition(instruction.Instruction);

    /// <inheritdoc />
    public void Dispose() => _lease.Dispose();
}

//=======================================================================
// NavigationRayStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing;

internal enum NavigationRayStatus : byte
{
    Pending,
    Success,
    Blocked,
    BudgetExceeded,
    CostOverflow,
    CapacityExceeded,
    Stale
}

internal enum NavigationRayEndpointAllowance : byte
{
    None,
    StartPrefix,
    DestinationSuffix
}

internal enum NavigationRayChainConstraintKind : byte
{
    Unrestricted,
    SourceAddress,
    SelectedEdge,
    SeedAddress,
    FinishAddress
}

internal readonly struct NavigationRayChainConstraint
{
    private NavigationRayChainConstraint(
        NavigationRayChainConstraintKind kind,
        NavigationCellAddress sourceAddress,
        NavigationCellAddress targetAddress,
        int edgeOrdinal)
    {
        Kind = kind;
        SourceAddress = sourceAddress;
        TargetAddress = targetAddress;
        EdgeOrdinal = edgeOrdinal;
    }

    internal NavigationRayChainConstraintKind Kind { get; }

    internal NavigationCellAddress SourceAddress { get; }

    internal NavigationCellAddress TargetAddress { get; }

    internal int EdgeOrdinal { get; }

    internal static NavigationRayChainConstraint SourceOnly(
        NavigationCellAddress sourceAddress) =>
        new(NavigationRayChainConstraintKind.SourceAddress, sourceAddress, default, -1);

    internal static NavigationRayChainConstraint SelectedEdge(
        NavigationCellAddress sourceAddress,
        NavigationCellAddress targetAddress,
        int edgeOrdinal)
    {
        SwiftThrowHelper.ThrowIfNegative(edgeOrdinal, nameof(edgeOrdinal));
        return new(
            NavigationRayChainConstraintKind.SelectedEdge,
            sourceAddress,
            targetAddress,
            edgeOrdinal);
    }

    internal static NavigationRayChainConstraint SeedAt(
        NavigationCellAddress sourceAddress) =>
        new(NavigationRayChainConstraintKind.SeedAddress, sourceAddress, default, -1);

    internal static NavigationRayChainConstraint FinishAt(
        NavigationCellAddress targetAddress) =>
        new(NavigationRayChainConstraintKind.FinishAddress, default, targetAddress, -1);
}

internal readonly struct NavigationRayRequest
{
    internal NavigationRayRequest(
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationWorldGraph expectedGraph,
        NavigationAgentProfile profile,
        NavigationAreaPolicy areaPolicy,
        TraversalIntent intent,
        bool allowTransitions,
        Vector3d start,
        Vector3d end,
        NavigationRayEndpointAllowance endpointAllowance,
        NavigationRayChainConstraint chainConstraint = default)
    {
        SwiftThrowHelper.ThrowIfNull(world, nameof(world));
        SwiftThrowHelper.ThrowIfNull(store, nameof(store));
        SwiftThrowHelper.ThrowIfNull(expectedGraph, nameof(expectedGraph));
        SwiftThrowHelper.ThrowIfNull(areaPolicy, nameof(areaPolicy));
        World = world;
        Store = store;
        ExpectedGraph = expectedGraph;
        Profile = profile;
        AreaPolicy = areaPolicy;
        Intent = intent;
        AllowTransitions = allowTransitions;
        Start = start;
        End = end;
        EndpointAllowance = endpointAllowance;
        ChainConstraint = chainConstraint;
    }

    internal GridWorld World { get; }

    internal NavigationWorldGraphStore Store { get; }

    internal NavigationWorldGraph ExpectedGraph { get; }

    internal NavigationAgentProfile Profile { get; }

    internal NavigationAreaPolicy AreaPolicy { get; }

    internal TraversalIntent Intent { get; }

    internal bool AllowTransitions { get; }

    internal Vector3d Start { get; }

    internal Vector3d End { get; }

    internal NavigationRayEndpointAllowance EndpointAllowance { get; }

    internal NavigationRayChainConstraint ChainConstraint { get; }
}

internal readonly struct NavigationRayResult
{
    internal NavigationRayResult(
        NavigationRayStatus status,
        NavigationCellAddress startAddress,
        NavigationCellAddress endAddress,
        Fixed64 traversalCost,
        bool isSemanticCostNeutral)
    {
        Status = status;
        StartAddress = startAddress;
        EndAddress = endAddress;
        TraversalCost = traversalCost;
        IsSemanticCostNeutral = isSemanticCostNeutral;
    }

    internal NavigationRayStatus Status { get; }

    internal NavigationCellAddress StartAddress { get; }

    internal NavigationCellAddress EndAddress { get; }

    internal Fixed64 TraversalCost { get; }

    internal bool IsSemanticCostNeutral { get; }
}

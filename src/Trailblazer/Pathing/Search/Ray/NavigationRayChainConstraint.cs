//=======================================================================
// NavigationRayChainConstraint.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

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
        ChronicleGuard.ThrowIfNegative(edgeOrdinal, nameof(edgeOrdinal));
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

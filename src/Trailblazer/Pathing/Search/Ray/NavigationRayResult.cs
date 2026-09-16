//=======================================================================
// NavigationRayResult.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

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

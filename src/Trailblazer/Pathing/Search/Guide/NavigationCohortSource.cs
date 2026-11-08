//=======================================================================
// NavigationCohortSource.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>One capacity-accounted payload pin, never an alias of a public lease.</summary>
internal sealed class NavigationCohortSource : IDisposable
{
    private NavigationAStarPayloadLease? _aStar;
    internal NavigationAStarPayloadLease? AStar
    {
        get => _aStar;
        set { _aStar = value; AStarGeneration = value?.Generation ?? 0; }
    }
    internal long AStarGeneration { get; private set; }
    internal NavigationFlowFieldPayloadLease Flow;
    internal bool HasFlow;
    internal NavigationCellAddress Origin;
    internal bool Transfer;
    internal bool IsEmpty => AStar == null && !HasFlow;

    public void Dispose()
    {
        NavigationAStarPayloadLease? pin = AStar;
        if (pin?.Generation == AStarGeneration)
            pin!.Dispose();
        AStar = null;
        Flow.Dispose();
        Flow = default;
        HasFlow = false;
    }
}

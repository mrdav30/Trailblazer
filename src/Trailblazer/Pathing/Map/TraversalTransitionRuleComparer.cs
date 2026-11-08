//=======================================================================
// TraversalTransitionRuleComparer.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Collections.Generic;

namespace Trailblazer.Pathing;

/// <summary>Provides complete deterministic canonical ordering for transition rules.</summary>
internal sealed class TraversalTransitionRuleComparer : IComparer<TraversalTransitionRule>
{
    internal static readonly TraversalTransitionRuleComparer Instance = new();

    public int Compare(TraversalTransitionRule left, TraversalTransitionRule right) =>
        string.CompareOrdinal(left.Id, right.Id);
}

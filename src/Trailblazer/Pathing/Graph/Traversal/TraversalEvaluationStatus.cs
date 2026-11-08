//=======================================================================
// TraversalEvaluationStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Reports the allocation-free result of one traversal evaluation.</summary>
internal enum TraversalEvaluationStatus : byte
{
    Passable = 0,
    Impassable = 1,
    CostOverflow = 2
}

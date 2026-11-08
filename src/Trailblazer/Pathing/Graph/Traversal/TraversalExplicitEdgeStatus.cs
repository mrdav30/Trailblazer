//=======================================================================
// TraversalExplicitEdgeStatus.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Reports progress through one bounded explicit-connection evaluation.</summary>
internal enum TraversalExplicitEdgeStatus : byte
{
    Pending = 0,
    Passable = 1,
    Impassable = 2,
    CostOverflow = 3
}

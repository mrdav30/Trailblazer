//=======================================================================
// TraversalTransitionRuleScope.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies where one procedural transition rule may apply.</summary>
internal enum TraversalTransitionRuleScope
{
    /// <summary>The source and destination medium states share one physical cell.</summary>
    SameCell = 0,

    /// <summary>The source and destination cells share one positive face contact.</summary>
    PositiveFaceContact = 1
}

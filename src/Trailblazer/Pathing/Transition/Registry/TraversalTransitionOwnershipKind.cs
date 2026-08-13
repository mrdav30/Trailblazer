//=======================================================================
// TraversalTransitionOwnershipKind.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>
/// Describes who owns a registered transition's lifecycle.
/// </summary>
internal enum TraversalTransitionOwnershipKind
{
    ManagedManual = 0,
    ManagedGenerated = 1
}

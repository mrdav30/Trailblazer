//=======================================================================
// NavigationRayChainConstraintKind.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies which cell or selected edge constrains navigation ray traversal.</summary>
internal enum NavigationRayChainConstraintKind : byte
{
    Unrestricted,
    SourceAddress,
    SelectedEdge,
    SeedAddress,
    FinishAddress
}

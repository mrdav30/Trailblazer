//=======================================================================
// NavigationGuideRequestPhase.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies the active processing phase of a scheduled guide request.</summary>
internal enum NavigationGuideRequestPhase : byte
{
    Queued,
    Direct,
    Admission,
    Search,
    Publication,
    Completed
}

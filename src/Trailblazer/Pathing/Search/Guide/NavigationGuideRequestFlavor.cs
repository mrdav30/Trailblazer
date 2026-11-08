//=======================================================================
// NavigationGuideRequestFlavor.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Identifies the guide algorithm owned by a scheduled request.</summary>
internal enum NavigationGuideRequestFlavor : byte
{
    PublicGraph,
    NavigatorDirectFirst,
    NavigatorDirectOnly,
    NavigatorGraphOnly
}

//=======================================================================
// INavigationPlanarGuideRequest.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

internal interface INavigationPlanarGuideRequest
{
    NavigationPlanarRouteRequest PlanarQuery { get; set; }
    NavigationAgentProfile2D SideViewProfile { get; }
    NavigationGuideLease? PlanarGuide { get; set; }
}

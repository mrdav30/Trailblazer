//=======================================================================
// NavigationSideViewSearch.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>Retains the upright capsule profile for one side-view search.</summary>
internal sealed class NavigationSideViewSearch : NavigationPlanarSearch
{
    internal NavigationSideViewSearch(GridWorld world, NavigationWorldGraph graph, NavigationPlanarWork proof,
        NavigationMapInstance map, NavigationPlanarBinding binding, in NavigationPlanarRouteRequest request,
        NavigationAreaPolicy policy, NavigationWorkMeter meter, in NavigationAgentProfile2D profile)
        : base(world, graph, proof, map, binding, request, policy, meter)
    {
        Profile = profile;
    }

    internal NavigationAgentProfile2D Profile { get; }
}

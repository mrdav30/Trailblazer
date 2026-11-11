//=======================================================================
// NavigationTopDownGuideRequest.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Owns scheduled acquisition of a retained top-down route and its unclaimed result.</summary>
public sealed class NavigationTopDownGuideRequest : NavigationGuideRequest, INavigationPlanarGuideRequest
{
    internal NavigationTopDownGuideRequest(NavigationGuideScheduler owner, PathAlgorithm algorithm,
        in NavigationPlanarRouteRequest query, long descriptorBytes)
        : base(owner, algorithm, descriptorBytes)
    {
        _query = query;
        Flavor = NavigationGuideRequestFlavor.TopDown;
    }

    private NavigationPlanarRouteRequest _query;
    private NavigationGuideLease? _guide;

    NavigationPlanarRouteRequest INavigationPlanarGuideRequest.PlanarQuery { get => _query; set => _query = value; }
    NavigationAgentProfile2D INavigationPlanarGuideRequest.SideViewProfile => default;
    NavigationGuideLease? INavigationPlanarGuideRequest.PlanarGuide { get => _guide; set => _guide = value; }

    /// <summary>Transfers the retained top-down route once when acquisition succeeds and its snapshot is current.</summary>
    public bool TryTakeTopDownGuide(out NavigationTopDownGuideLease guide)
    {
        guide = default;
        NavigationGuideScheduler? owner = Owner;
        if (owner == null || !owner.TryTakePlanarGuide(this, out var lease))
            return false;
        guide = new NavigationTopDownGuideLease(lease);
        return true;
    }
}

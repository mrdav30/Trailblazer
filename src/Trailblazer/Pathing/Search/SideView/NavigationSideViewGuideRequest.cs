//=======================================================================
// NavigationSideViewGuideRequest.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Owns scheduled acquisition of a side-view route and its unclaimed result.</summary>
public sealed class NavigationSideViewGuideRequest : NavigationGuideRequest, INavigationPlanarGuideRequest
{
    private NavigationPlanarRouteRequest _query;
    private readonly NavigationAgentProfile2D _profile;
    private NavigationGuideLease? _guide;

    internal NavigationSideViewGuideRequest(NavigationGuideScheduler owner, PathAlgorithm algorithm,
        in NavigationPlanarRouteRequest query, in NavigationAgentProfile2D profile, long descriptorBytes)
        : base(owner, algorithm, descriptorBytes)
    {
        _query = query;
        _profile = profile;
        Flavor = NavigationGuideRequestFlavor.SideView;
    }

    NavigationPlanarRouteRequest INavigationPlanarGuideRequest.PlanarQuery { get => _query; set => _query = value; }
    NavigationAgentProfile2D INavigationPlanarGuideRequest.SideViewProfile => _profile;
    NavigationGuideLease? INavigationPlanarGuideRequest.PlanarGuide { get => _guide; set => _guide = value; }

    /// <summary>Transfers the successful side-view route once if its snapshot remains current.</summary>
    public bool TryTakeSideViewGuide(out NavigationSideViewGuideLease guide)
    {
        guide = default;
        NavigationGuideScheduler? owner = Owner;
        if (owner == null || !owner.TryTakePlanarGuide(this, out var lease))
            return false;
        guide = new NavigationSideViewGuideLease(lease);
        return true;
    }
}

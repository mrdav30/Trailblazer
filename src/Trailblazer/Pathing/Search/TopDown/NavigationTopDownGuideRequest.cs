//=======================================================================
// NavigationTopDownGuideRequest.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Owns scheduled acquisition of a retained top-down route and its unclaimed result.</summary>
public sealed class NavigationTopDownGuideRequest : NavigationGuideRequest
{
    internal NavigationTopDownGuideRequest(NavigationGuideScheduler owner, PathAlgorithm algorithm,
        in NavigationPlanarRouteRequest query, long descriptorBytes)
        : base(owner, algorithm, descriptorBytes)
    {
        TopDownQuery = query;
    }

    internal NavigationPlanarRouteRequest TopDownQuery { get; set; }
    internal NavigationTopDownGuideLease? TopDownGuide { get; set; }

    /// <summary>Transfers the retained top-down route once when acquisition succeeds and its snapshot is current.</summary>
    public bool TryTakeTopDownGuide(out NavigationTopDownGuideLease guide)
    {
        guide = default;
        NavigationGuideScheduler? owner = Owner;
        return owner != null && owner.TryTakeTopDownGuide(this, out guide);
    }
}

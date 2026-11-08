//=======================================================================
// NavigationResolvedEndpoint.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Identifies one exact resolved graph endpoint.</summary>
internal readonly struct NavigationResolvedEndpoint
{
    internal NavigationResolvedEndpoint(
        NavigationNodeRef node,
        NavigationCellAddress address,
        TraversalMedia media,
        TraversalMedium resolutionMedium,
        Vector3d footAnchor,
        Fixed64 resolutionDistance)
    {
        Node = node;
        Address = address;
        Media = media;
        ResolutionMedium = resolutionMedium;
        FootAnchor = footAnchor;
        ResolutionDistance = resolutionDistance;
    }

    internal NavigationNodeRef Node { get; }
    internal NavigationCellAddress Address { get; }
    internal TraversalMedia Media { get; }
    internal TraversalMedium ResolutionMedium { get; }
    internal Vector3d FootAnchor { get; }
    internal Fixed64 ResolutionDistance { get; }
}

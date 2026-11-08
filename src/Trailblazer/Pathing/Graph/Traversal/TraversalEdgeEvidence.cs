//=======================================================================
// TraversalEdgeEvidence.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <summary>Returns one evaluated edge's exact cost and resolved portal evidence.</summary>
internal readonly struct TraversalEdgeEvidence
{
    internal TraversalEdgeEvidence(NavigationNodeRef dependencyNode)
    {
        this = default;
        DependencyNode = dependencyNode;
    }

    internal TraversalEdgeEvidence(
        Fixed64 cost,
        GridNavigationPortal portal,
        Vector3d sourcePortalAnchor,
        Vector3d targetPortalAnchor,
        NavigationCellAddress sourceAddress,
        NavigationCellAddress targetAddress,
        Vector3d sourceFootAnchor,
        Vector3d targetFootAnchor,
        in GridCellPrism sourcePrism,
        in GridCellPrism targetPrism,
        NavigationNodeRef dependencyNode)
    {
        Cost = cost;
        Portal = portal;
        SourcePortalAnchor = sourcePortalAnchor;
        TargetPortalAnchor = targetPortalAnchor;
        SourceAddress = sourceAddress;
        TargetAddress = targetAddress;
        SourceFootAnchor = sourceFootAnchor;
        TargetFootAnchor = targetFootAnchor;
        SourcePrism = sourcePrism;
        TargetPrism = targetPrism;
        DependencyNode = dependencyNode;
    }

    internal Fixed64 Cost { get; }

    internal GridNavigationPortal Portal { get; }

    internal Vector3d SourcePortalAnchor { get; }

    internal Vector3d TargetPortalAnchor { get; }

    internal NavigationCellAddress SourceAddress { get; }

    internal NavigationCellAddress TargetAddress { get; }

    internal Vector3d SourceFootAnchor { get; }

    internal Vector3d TargetFootAnchor { get; }

    internal GridCellPrism SourcePrism { get; }

    internal GridCellPrism TargetPrism { get; }

    internal NavigationNodeRef DependencyNode { get; }
}

//=======================================================================
// NavigationRayRequest.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;

namespace Trailblazer.Pathing;

/// <summary>Carries the endpoints and traversal constraints for an internal navigation ray.</summary>
internal readonly struct NavigationRayRequest
{
    internal NavigationRayRequest(
        GridWorld world,
        NavigationWorldGraphStore store,
        NavigationWorldGraph expectedGraph,
        NavigationAgentProfile profile,
        NavigationAreaPolicy areaPolicy,
        TraversalMedium medium,
        Vector3d start,
        Vector3d end,
        NavigationRayEndpointAllowance endpointAllowance,
        NavigationRayChainConstraint chainConstraint = default)
    {
        ChronicleGuard.ThrowIfNull(world, nameof(world));
        ChronicleGuard.ThrowIfNull(store, nameof(store));
        ChronicleGuard.ThrowIfNull(expectedGraph, nameof(expectedGraph));
        ChronicleGuard.ThrowIfNull(areaPolicy, nameof(areaPolicy));
        ChronicleGuard.ThrowIfArgumentOutOfRange(!NavigationCell.IsKnownMedium(medium), null, nameof(medium));
        World = world;
        Store = store;
        ExpectedGraph = expectedGraph;
        Profile = profile;
        AreaPolicy = areaPolicy;
        Medium = medium;
        Start = start;
        End = end;
        EndpointAllowance = endpointAllowance;
        ChainConstraint = chainConstraint;
    }

    internal GridWorld World { get; }

    internal NavigationWorldGraphStore Store { get; }

    internal NavigationWorldGraph ExpectedGraph { get; }

    internal NavigationAgentProfile Profile { get; }

    internal NavigationAreaPolicy AreaPolicy { get; }

    internal TraversalMedium Medium { get; }

    internal Vector3d Start { get; }

    internal Vector3d End { get; }

    internal NavigationRayEndpointAllowance EndpointAllowance { get; }

    internal NavigationRayChainConstraint ChainConstraint { get; }
}

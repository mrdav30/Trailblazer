//=======================================================================
// TrailblazerPathingService.PlanarRoutes.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <content>Dispatches planar requests to the shared A* and Flow admission lanes.</content>
public sealed partial class TrailblazerPathingService
{
    internal NavigationGuideStatus FindPlanarRoute(in NavigationPlanarRouteRequest request,
        PathAlgorithm algorithm, Span<NavigationCellAddress> route, out int count, out Fixed64 cost,
        NavigationAStarPayloadCache? retention, out NavigationAStarGuideLease? guide,
        in NavigationAgentProfile2D sideViewProfile = default) =>
        algorithm == PathAlgorithm.AStar
            ? _navigationAStarAdmissionGate.FindPlanar(_context.World, request, route, out count, out cost,
                retention, out guide, sideViewProfile)
            : _navigationFlowAdmissionGate.FindPlanar(_context.World, request, route, out count, out cost,
                retention, out guide, sideViewProfile);

    internal static NavigationGuideStatus ResolvePlanarSnapshotStatus(NavigationGuideStatus status,
        NavigationWorldGraph captured, NavigationWorldGraph current,
        (long Spawn, uint Version, ulong Sequence) before, (long Spawn, uint Version, ulong Sequence) after) =>
        ReferenceEquals(captured, current) && before.Equals(after) ? status : NavigationGuideStatus.Stale;
}

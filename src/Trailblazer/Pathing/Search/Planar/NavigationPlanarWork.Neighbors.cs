//=======================================================================
// NavigationPlanarWork.Neighbors.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <content>Enumerates bounded cross-map neighbor candidates.</content>
internal sealed partial class NavigationPlanarWork
{
    internal GridPlanarLayer GetBoundLayer(int ordinal) => _layers[ordinal];

    // GridForge also accepts coincident and rectangular corner-only legs for coverage proofs.
    internal static bool IsOrdinaryNeighbor(GridTopologyKind topology, Vector2d start, Vector2d end) =>
        start != end && (topology != GridTopologyKind.RectangularPrism || start.X == end.X || start.Y == end.Y);

    internal static bool AreHorizontalLatticesCompatible(GridConfiguration first, GridConfiguration second)
    {
        if (first.TopologyKind != second.TopologyKind)
            return false;

        GridTopologyMetrics a = first.TopologyMetrics;
        GridTopologyMetrics b = second.TopologyMetrics;
        return first.TopologyKind == GridTopologyKind.RectangularPrism
            ? a.CellWidth == b.CellWidth && a.CellLength == b.CellLength
            : a.CellRadius == b.CellRadius && a.HexOrientation == b.HexOrientation;
    }

    internal bool BeginNeighborCandidates(GridWorld world, GridPlanarLayer layer,
        GridConfiguration source, GridConfiguration target, Vector3d center, Fixed64 layerY)
    {
        GridTopologyMetrics metrics = source.TopologyMetrics;
        Fixed64 reachX = source.TopologyKind == GridTopologyKind.RectangularPrism
            ? metrics.CellWidth : metrics.CellRadius * Fixed64.Two;
        Fixed64 reachZ = source.TopologyKind == GridTopologyKind.RectangularPrism
            ? metrics.CellLength : reachX;
        // Saturation only clips the discovery window to representable world centers.
        var minimum = new Vector3d(center.X - reachX, layerY, center.Z - reachZ);
        var maximum = new Vector3d(center.X + reachX, layerY, center.Z + reachZ);
        if (maximum.X < target.BoundsMin.X || minimum.X > target.BoundsMax.X
            || maximum.Z < target.BoundsMin.Z || minimum.Z > target.BoundsMax.Z)
        {
            return false;
        }
        return world.TryBeginCoveredAddresses(_cursor, minimum, maximum, 1, layer.Generation.ConfigurationKey);
    }

    internal void BeginGoalCandidates(GridWorld world, GridCoveredAddressGeneration generation,
        Vector2d center, Fixed64 layerCenter, Vector2d extent) =>
        world.TryBeginCoveredAddresses(_cursor, new Vector3d(center.X - extent.X, layerCenter, center.Y - extent.Y),
            new Vector3d(center.X + extent.X, layerCenter, center.Y + extent.Y), 1, generation.ConfigurationKey);

    internal static bool IsInsideGoalRegion(Vector2d position, (Vector2d Center, Fixed64 Radius)? goalRegion) =>
        goalRegion is not { } region || Vector2d.CompareDistanceSquared(position, region.Center,
            Vector2d.Zero, new Vector2d(region.Radius, Fixed64.Zero)) <= 0;

    internal NavigationTraversalEdgeAdvanceStatus BindNeighborCandidateLayer(GridWorld world,
        GridPlanarLayer layer, NavigationWorkMeter meter)
    {
        Span<GridCoveredAddressGeneration> input = stackalloc GridCoveredAddressGeneration[1] { layer.Generation };
        NavigationGuideStatus status = AdvanceCoverage(world, input, Span<GridCoveredAddress>.Empty,
            meter, out _, out _);
        return status switch
        {
            NavigationGuideStatus.Success => NavigationTraversalEdgeAdvanceStatus.Pending,
            NavigationGuideStatus.Stale => NavigationTraversalEdgeAdvanceStatus.Stale,
            _ => NavigationTraversalEdgeAdvanceStatus.BudgetExceeded
        };
    }

    internal NavigationTraversalEdgeAdvanceStatus ReadNeighborCandidate(GridWorld world, NavigationWorkMeter meter,
        out GridCoveredAddress address, out bool complete)
    {
        Span<GridCoveredAddress> output = stackalloc GridCoveredAddress[1];
        GridCoveredAddressCursorStatus status = world.AdvanceCoveredAddresses(_cursor,
            ReadOnlySpan<GridCoveredAddressGeneration>.Empty, output, 0, Math.Min(1, meter.RemainingLookupProbes),
            output.Length, out _, out int probes, out _, out int count);
        meter.TryConsumeLookupProbes(probes);
        complete = status == GridCoveredAddressCursorStatus.Complete;
        address = count != 0 ? output[0] : default;
        if (status == GridCoveredAddressCursorStatus.Stale)
            return NavigationTraversalEdgeAdvanceStatus.Stale;
        if (count != 0)
            return NavigationTraversalEdgeAdvanceStatus.Edge;
        if (complete)
            return NavigationTraversalEdgeAdvanceStatus.Complete;
        return probes == 0
            ? NavigationTraversalEdgeAdvanceStatus.BudgetExceeded
            : NavigationTraversalEdgeAdvanceStatus.Pending;
    }
}

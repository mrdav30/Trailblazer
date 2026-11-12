//=======================================================================
// NavigationPlanarWork.SideView.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids;
using GridForge.Grids.Topology;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <content>Proves authored Solid support and upright capsule clearance for side-view movement.</content>
internal sealed partial class NavigationPlanarWork
{
    internal NavigationGuideStatus ResolveSideViewAnchor(GridWorld world, NavigationWorldGraph graph,
        NavigationMapInstance map, NavigationPlanarBinding binding, VoxelIndex index, int layerCount,
        in NavigationAgentProfile2D profile, NavigationAreaPolicy policy, NavigationWorkMeter meter,
        out Vector2d center, NavigationGuideStatus invalid = NavigationGuideStatus.InvalidStart)
    {
        center = default;
        if (!meter.TryConsumeEndpointCandidates(1))
            return NavigationGuideStatus.BudgetExceeded;
        var status = ReadSupportAnchor(map, index, profile, policy, meter, invalid,
            out _, out Vector2d candidate);
        if (status != NavigationGuideStatus.Success)
            return status;
        WorldVoxelIndex pin = Pin(map, index);
        status = TraceBody(world, graph, layerCount, binding, pin, pin, candidate, candidate,
            profile.Radius, profile.AxisLength, TraversalMedia.Solid, profile.Capabilities, policy, meter,
            invalid);
        if (status == NavigationGuideStatus.Success)
            center = candidate;
        return status;
    }

    internal NavigationGuideStatus ValidateSideViewLeg(GridWorld world, NavigationWorldGraph graph,
        NavigationMapInstance source, NavigationPlanarBinding binding, VoxelIndex sourceIndex,
        NavigationCellAddress destination, int layerCount, in NavigationAgentProfile2D profile,
        NavigationAreaPolicy policy, NavigationWorkMeter meter)
    {
        if (!TryGetMap(graph, destination.MapId, out var target, out var targetBinding, NavigationMode2D.SideView))
            return NavigationGuideStatus.NoMap;
        if (binding.EmbeddingY != targetBinding.EmbeddingY)
            return NavigationGuideStatus.NoPath;
        if (!meter.TryConsumeEvaluatedEdges(1))
            return NavigationGuideStatus.BudgetExceeded;

        return TraceSideViewLeg(world, graph, source, binding, sourceIndex, target, destination.Index, layerCount,
            profile, policy, meter, out _, out _, out _, chargeEndpointCandidates: true);
    }

    internal NavigationGuideStatus TraceSideViewConnector(GridWorld world, NavigationWorldGraph graph,
        NavigationMapInstance map, NavigationPlanarBinding binding, VoxelIndex index, int layerCount,
        (Vector2d Origin, Vector2d Current, Vector2d Anchor, Vector2d? RoundedTarget) connector,
        in NavigationAgentProfile2D profile, NavigationAreaPolicy policy, NavigationWorkMeter meter)
    {
        if (!meter.TryConsumeEvaluatedEdges(1))
            return NavigationGuideStatus.BudgetExceeded;
        var status = ReadSupportAnchor(map, index, profile, policy, meter, NavigationGuideStatus.InvalidStart,
            out var support, out var anchor);
        if (status != NavigationGuideStatus.Success)
            return status;
        if (anchor != connector.Anchor)
            return NavigationGuideStatus.Stale;
        // Check the origin in the authored track's midpoint chart to keep the interval representable.
        var span = support.Right - support.Left;
        var midpointOffset = (SupportMidpoint(support) - support.Left).X;
        if (!Vector2d.TrySubtract(connector.Origin, anchor, out var offset)
            || offset.X < -midpointOffset || offset.X > span.X - midpointOffset
            || Vector2d.OrientationSign(Vector2d.Zero, span, offset) != 0)
            return NavigationGuideStatus.InvalidStart;

        var pin = Pin(map, index);
        status = TraceSideView(world, graph, binding, layerCount, pin, pin, connector.Current, anchor, profile, policy, meter);
        if (status == NavigationGuideStatus.Success && connector.RoundedTarget is Vector2d target)
            status = TraceSideView(world, graph, binding, layerCount, pin, pin, connector.Current, target, profile, policy, meter);
        return status;
    }

    internal NavigationGuideStatus TraceSideViewLeg(GridWorld world, NavigationWorldGraph graph,
        NavigationMapInstance source, NavigationPlanarBinding binding, VoxelIndex sourceIndex,
        NavigationMapInstance target, VoxelIndex targetIndex, int layerCount, in NavigationAgentProfile2D profile,
        NavigationAreaPolicy policy, NavigationWorkMeter meter, out Vector2d start, out Vector2d end,
        out bool step, bool chargeEndpointCandidates = false)
    {
        start = end = default;
        step = false;
        if (chargeEndpointCandidates && !meter.TryConsumeEndpointCandidates(1))
            return NavigationGuideStatus.BudgetExceeded;
        var status = ReadSupportAnchor(source, sourceIndex, profile, policy, meter, NavigationGuideStatus.InvalidStart,
            out var from, out start);
        if (status != NavigationGuideStatus.Success)
            return status;
        if (chargeEndpointCandidates && !meter.TryConsumeEndpointCandidates(1))
            return NavigationGuideStatus.BudgetExceeded;
        status = ReadSupportAnchor(target, targetIndex, profile, policy, meter, NavigationGuideStatus.InvalidEnd,
            out var to, out end);
        if (status != NavigationGuideStatus.Success)
            return status;
        // Baked supports already certify their selected-layer footprints.
        source.Map.GridBinding.TryGetCellPrism(sourceIndex, out GridCellPrism fromPrism);
        target.Map.GridBinding.TryGetCellPrism(targetIndex, out GridCellPrism toPrism);
        if (!AreHorizontalLatticesCompatible(source.Map.GridBinding.Configuration, target.Map.GridBinding.Configuration)
            || !IsOrdinaryNeighbor(fromPrism.TopologyKind, fromPrism.Center.ToVector2d(), toPrism.Center.ToVector2d()))
            return NavigationGuideStatus.NoPath;

        bool sourceOnLeft = from.Left.X < to.Left.X;
        var left = sourceOnLeft ? from : to;
        var right = sourceOnLeft ? to : from;
        if (left.Right.X != right.Left.X)
            return NavigationGuideStatus.NoPath;

        WorldVoxelIndex sourcePin = Pin(source, sourceIndex);
        WorldVoxelIndex targetPin = Pin(target, targetIndex);
        if (SideViewSupportsMeet(from, to))
        {
            // A chord between distinct surface offsets cannot certify a corner.
            if (Vector2d.OrientationSign(left.Left, left.Right, right.Right) != 0 || left.Normal != right.Normal)
                return NavigationGuideStatus.NoPath;
            return TraceSideView(world, graph, binding, layerCount, sourcePin, targetPin, start, end, profile, policy, meter);
        }

        // An ordinary step can only join facing horizontal supports within one rectangular row.
        if (fromPrism.TopologyKind != GridTopologyKind.RectangularPrism || fromPrism.Center.Z != toPrism.Center.Z
            || from.Left.Y != from.Right.Y || to.Left.Y != to.Right.Y)
            return NavigationGuideStatus.NoPath;
        // The equal center offsets cancel within one representable rectangular row.
        Fixed64 rise = end.Y - start.Y;
        if (rise > profile.MaxStepUp || rise < -profile.MaxDropDown)
            return NavigationGuideStatus.NoPath;

        bool rising = rise > Fixed64.Zero;
        Vector2d lower = rising ? start : end;
        bool lowerOnLeft = rising == sourceOnLeft;
        Fixed64 seamDistance = lowerOnLeft ? left.Right.X - lower.X : lower.X - right.Left.X;
        if (seamDistance < profile.Radius)
            return NavigationGuideStatus.NoPath;

        // Trace the lift or descent in the lower owner, then the crossing between the two owners.
        // Traversal order determines which proof consumes the available budget first.
        Vector2d lifted = GetSideViewStepWaypoint(start, end);
        step = true;
        if (rising)
        {
            status = TraceSideView(world, graph, binding, layerCount, sourcePin, sourcePin, start, lifted, profile, policy, meter);
            return status != NavigationGuideStatus.Success ? status
                : TraceSideView(world, graph, binding, layerCount, sourcePin, targetPin, lifted, end, profile, policy, meter);
        }
        status = TraceSideView(world, graph, binding, layerCount, sourcePin, targetPin, start, lifted, profile, policy, meter);
        return status != NavigationGuideStatus.Success ? status
            : TraceSideView(world, graph, binding, layerCount, targetPin, targetPin, lifted, end, profile, policy, meter);
    }

    // The step proof certified horizontal anchors and a lift at the lower anchor's X.
    internal static Vector2d GetSideViewStepWaypoint(Vector2d start, Vector2d end) =>
        start.Y < end.Y ? new Vector2d(start.X, end.Y) : new Vector2d(end.X, start.Y);

    internal static bool SideViewSupportsMeet(in NavigationPlanarSupport from, in NavigationPlanarSupport to) =>
        from.Left.X < to.Left.X ? from.Right == to.Left : to.Right == from.Left;

    private NavigationGuideStatus TraceSideView(GridWorld world, NavigationWorldGraph graph, NavigationPlanarBinding binding,
        int layerCount, WorldVoxelIndex source, WorldVoxelIndex target, Vector2d start, Vector2d end,
        in NavigationAgentProfile2D profile, NavigationAreaPolicy policy, NavigationWorkMeter meter) =>
        TraceBody(world, graph, layerCount, binding, source, target, start, end, profile.Radius, profile.AxisLength,
            TraversalMedia.Solid, profile.Capabilities, policy, meter, NavigationGuideStatus.NoPath);

    private static NavigationGuideStatus ReadSupportAnchor(NavigationMapInstance map, VoxelIndex index,
        in NavigationAgentProfile2D profile, NavigationAreaPolicy policy, NavigationWorkMeter meter,
        NavigationGuideStatus invalid, out NavigationPlanarSupport support, out Vector2d center)
    {
        support = default;
        center = default;
        if (!meter.TryConsumeLookupProbes(1))
            return NavigationGuideStatus.BudgetExceeded;
        Fixed64 height = profile.AxisLength + profile.Radius + profile.Radius;
        if (!map.Map.TryGetPlanarSupport(index, out support) || support.Normal.Y < profile.MinimumSupportNormalDot
            || !map.TryGetSlot(index, out int slot) || !map.TryGetEffectiveCell(slot, out NavigationCell cell))
            return invalid;
        map.TryGetPhysicalState(slot, out bool present, out byte obstacles);
        if (!present || obstacles != 0
            || !TraversalEvaluator.IsCellPassable(cell, TraversalMedia.Solid, profile.Capabilities, profile.Radius, policy, out _)
            || height > cell.HeightClearance)
            return invalid;

        return MaterializeSupportAnchor(support, profile, invalid, out center);
    }

    internal static NavigationGuideStatus MaterializeSupportAnchor(in NavigationPlanarSupport support,
        in NavigationAgentProfile2D profile, NavigationGuideStatus invalid, out Vector2d center)
    {
        center = default;
        // Authoring checked the delta. A rounded midpoint must still lie on the finite support.
        Vector2d witness = SupportMidpoint(support);
        if (Vector2d.OrientationSign(support.Left, support.Right, witness) != 0)
            return invalid;
        // Round the center upward for an odd half-axis, then trace the original full axis.
        long rawAxis = profile.AxisLength.m_rawValue;
        Fixed64 halfAxis = Fixed64.FromRaw((rawAxis >> 1) + (rawAxis & 1));
        if (!Fixed64.TryMultiplyAdd(support.Normal.X, profile.Radius, witness.X, out Fixed64 x)
            || !Fixed64.TryMultiplyAdd(support.Normal.Y, profile.Radius, witness.Y, out Fixed64 y)
            || !Fixed64.TryAdd(y, halfAxis, out y))
            return NavigationGuideStatus.CostOverflow;
        center = new Vector2d(x, y);
        return NavigationGuideStatus.Success;
    }

    private static Vector2d SupportMidpoint(in NavigationPlanarSupport support) =>
        support.Left + (support.Right - support.Left) * Fixed64.Half;
}

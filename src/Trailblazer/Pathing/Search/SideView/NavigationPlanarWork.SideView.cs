//=======================================================================
// NavigationPlanarWork.SideView.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using FixedMathSharp.Geometry;
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
        (Vector2d Origin, Vector2d Current, Vector2d Anchor, Vector2d? RoundedTarget, bool AllowRoundedOrigin) connector,
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
        if (!IsSideViewConnectorOrigin(support, anchor, connector.Origin, connector.AllowRoundedOrigin))
            return NavigationGuideStatus.InvalidStart;

        var pin = Pin(map, index);
        status = TraceSideView(world, graph, binding, layerCount, pin, pin, connector.Current, anchor, profile, policy, meter);
        if (status == NavigationGuideStatus.Success && connector.RoundedTarget is Vector2d target)
            status = TraceSideView(world, graph, binding, layerCount, pin, pin, connector.Current, target, profile, policy, meter);
        return status;
    }

    private static bool IsSideViewConnectorOrigin(in NavigationPlanarSupport support, Vector2d anchor,
        Vector2d origin, bool allowRoundedOrigin)
    {
        // Use the midpoint chart to keep both ends of the authored track representable.
        var span = support.Right - support.Left;
        var midpointOffset = (SupportMidpoint(support) - support.Left).X;
        return Vector2d.TrySubtract(origin, anchor, out var offset)
            && offset.X >= -midpointOffset && offset.X <= span.X - midpointOffset
            && IsSideViewConnectorOffset(span, offset, allowRoundedOrigin);
    }

    private static bool IsSideViewConnectorOffset(Vector2d span, Vector2d offset, bool allowRoundedOrigin)
    {
        int side = Vector2d.OrientationSign(Vector2d.Zero, span, offset);
        if (side == 0)
            return true;
        if (!allowRoundedOrigin | side < 0)
            return false;

        // Reacquisition can begin in the two-raw open strip used by rounded guided steps.
        bool steep = span.X < span.Y.Abs();
        var major = steep ? offset.Y : offset.X;
        var minor = steep ? offset.X : offset.Y;
        var numerator = steep ? span.X : span.Y;
        var denominator = steep ? span.Y : span.X;
        return Fixed64.TryMultiplyDivide(major, numerator, denominator, out var ideal)
            & Fixed64.TrySubtract(minor, ideal, out var residual)
            & residual >= Fixed64.FromRaw(-2) & residual <= Fixed64.FromRaw(2);
    }

    internal NavigationGuideStatus TraceSideViewLeg(GridWorld world, NavigationWorldGraph graph,
        NavigationMapInstance source, NavigationPlanarBinding binding, VoxelIndex sourceIndex,
        NavigationMapInstance target, VoxelIndex targetIndex, int layerCount, in NavigationAgentProfile2D profile,
        NavigationAreaPolicy policy, NavigationWorkMeter meter, out Vector2d start, out Vector2d end,
        out Vector2d? bend, bool chargeEndpointCandidates = false)
    {
        start = end = default;
        bend = null;
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
        int bendOrdinal = GetSideViewBendOrdinal(left, right);
        if (bendOrdinal == NavigationAStarGuidePoint.OrdinaryOrdinal)
            return TraceSideView(world, graph, binding, layerCount, sourcePin, targetPin, start, end, profile, policy, meter);

        // The grounded bend contacts both exact owners, so both pins govern each stage's coverage.
        WorldVoxelIndex firstTarget = targetPin;
        WorldVoxelIndex secondSource = sourcePin;
        Vector2d waypoint;
        if (bendOrdinal == NavigationAStarGuidePoint.SideViewGroundedBendOrdinal)
        {
            // Each support's closed capsule contact region is convex, so both endpoints prove contact along the stage.
            if (!TryGetSideViewCorner(left, right, sourceOnLeft ? start : end, sourceOnLeft ? end : start, out waypoint)
                || !(from.TouchesCapsule(start, profile) & from.TouchesCapsule(waypoint, profile)
                    & to.TouchesCapsule(waypoint, profile) & to.TouchesCapsule(end, profile)))
                return NavigationGuideStatus.NoPath;
        }
        else
        {
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

            waypoint = GetSideViewStepWaypoint(start, end);
            if (rising)
                firstTarget = sourcePin;
            else
                secondSource = targetPin;
        }
        bend = waypoint;
        status = TraceSideView(world, graph, binding, layerCount, sourcePin, firstTarget, start, waypoint, profile, policy, meter);
        return status != NavigationGuideStatus.Success ? status
            : TraceSideView(world, graph, binding, layerCount, secondSource, targetPin, waypoint, end, profile, policy, meter);
    }

    // The step proof certified horizontal anchors and a lift at the lower anchor's X.
    internal static Vector2d GetSideViewStepWaypoint(Vector2d start, Vector2d end) =>
        start.Y < end.Y ? new Vector2d(start.X, end.Y) : new Vector2d(end.X, start.Y);

    internal static int GetSideViewBendOrdinal(in NavigationPlanarSupport left, in NavigationPlanarSupport right)
    {
        if (left.Right != right.Left)
            return NavigationAStarGuidePoint.SideViewStepBendOrdinal;
        return Vector2d.OrientationSign(left.Left, left.Right, right.Right) == 0 & left.Normal == right.Normal
            ? NavigationAStarGuidePoint.OrdinaryOrdinal : NavigationAStarGuidePoint.SideViewGroundedBendOrdinal;
    }

    internal static bool TryGetSideViewCorner(in NavigationPlanarSupport left, in NavigationPlanarSupport right,
        Vector2d leftAnchor, Vector2d rightAnchor, out Vector2d corner)
    {
        corner = default;
        // A convex crest requires support around a vertex, outside these two tracks.
        if (Vector2d.OrientationSign(left.Left, left.Right, right.Right) <= 0)
            return false;
        bool leftFits = Vector2d.TryAdd(leftAnchor, left.Right - SupportMidpoint(left), out var leftSeam);
        bool rightFits = Vector2d.TryAdd(rightAnchor, right.Left - SupportMidpoint(right), out var rightSeam);
        if (!(leftFits & rightFits))
            return false;
        var incoming = new FixedSegment2d(leftAnchor, leftSeam);
        if (!incoming.TryGetUniqueIntersection(new FixedSegment2d(rightSeam, rightAnchor), out var parameter))
            return false;
        // shortcut: decline rounded intersections off either track until exact point materialization is available.
        Vector2d point = Vector2d.Lerp(leftAnchor, leftSeam, parameter);
        if (!(point.X >= rightSeam.X & point.X <= rightAnchor.X
            & Vector2d.OrientationSign(leftAnchor, leftSeam, point) == 0
            & Vector2d.OrientationSign(rightSeam, rightAnchor, point) == 0))
            return false;
        corner = point;
        return true;
    }

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
        if (!map.Map.TryGetPlanarSupport(index, out support)
            || !map.TryGetSlot(index, out int slot) || !map.TryGetEffectiveCell(slot, out NavigationCell cell))
            return invalid;
        map.TryGetPhysicalState(slot, out bool present, out byte obstacles);
        if (!present || obstacles != 0
            || !FitsSideViewSupport(support, cell, profile)
            || !TraversalEvaluator.IsCellPassable(cell, TraversalMedia.None, profile.Capabilities, Fixed64.Zero, policy, out _))
            return invalid;

        return MaterializeSupportAnchor(support, profile, invalid, out center);
    }

    private static bool FitsSideViewSupport(in NavigationPlanarSupport support, in NavigationCell cell,
        in NavigationAgentProfile2D profile) => (cell.Media & TraversalMedia.Solid) != 0
        & support.Normal.Y >= profile.MinimumSupportNormalDot & profile.Radius <= cell.RadiusClearance
        & profile.AxisLength + profile.Radius + profile.Radius <= cell.HeightClearance;

    private static bool FitsReplacementContact(in NavigationPlanarSupport support, in NavigationCell cell,
        Vector2d center, in NavigationAgentProfile2D profile) => !IsReplacementContact(support, center, profile)
        || FitsSideViewSupport(support, cell, profile);

    private static bool IsReplacementContact(in NavigationPlanarSupport support, Vector2d center,
        in NavigationAgentProfile2D profile)
    {
        // Above-surface contact can support the lower cap. Roof contact still uses the headroom rules.
        var cap = new Vector2d(center.X, center.Y - profile.HalfAxisCeiling);
        return Vector2d.OrientationSign(support.Left, support.Right, cap) >= 0
            && support.TouchesCapsule(center, profile);
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
        if (!Fixed64.TryMultiplyAdd(support.Normal.X, profile.Radius, witness.X, out Fixed64 x)
            || !Fixed64.TryMultiplyAdd(support.Normal.Y, profile.Radius, witness.Y, out Fixed64 y)
            || !Fixed64.TryAdd(y, profile.HalfAxisCeiling, out y))
            return NavigationGuideStatus.CostOverflow;
        center = new Vector2d(x, y);
        return NavigationGuideStatus.Success;
    }

    private static Vector2d SupportMidpoint(in NavigationPlanarSupport support) =>
        support.Left + (support.Right - support.Left) * Fixed64.Half;
}

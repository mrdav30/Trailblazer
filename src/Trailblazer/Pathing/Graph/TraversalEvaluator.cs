//=======================================================================
// TraversalEvaluator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <summary>Reports the allocation-free result of one traversal evaluation.</summary>
internal enum TraversalEvaluationStatus : byte
{
    Passable = 0,
    Impassable = 1,
    CostOverflow = 2,
    Stale = 3
}

/// <summary>Reports progress through one bounded explicit-connection evaluation.</summary>
internal enum TraversalExplicitEdgeStatus : byte
{
    Pending = 0,
    Passable = 1,
    Impassable = 2,
    CostOverflow = 3,
    Stale = 4
}

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

/// <summary>Retains scalar state while explicit corridor legs are evaluated.</summary>
internal struct TraversalExplicitEdgeWork
{
    internal NavigationExplicitConnectionRecord Record;
    internal NavigationNodeRef Target;
    internal NavigationPagedSequence<GridNavigationPortal>.Enumerator Portals;
    internal NavigationCellAddress PreviousAddress;
    internal GridCellPrism PreviousPrism;
    internal Vector3d SourceFootAnchor;
    internal Vector3d TargetFootAnchor;
    internal Vector3d PreviousFootAnchor;
    internal Fixed64 TargetEnterCost;
    internal Fixed64 TargetAreaEnterCost;
    internal int WitnessIndex;
}

/// <summary>Evaluates nodes and native edges against one resolved immutable query profile.</summary>
internal readonly struct TraversalEvaluator
{
    private readonly NavigationWorldGraph _graph;
    private readonly NavigationAgentProfile _profile;
    private readonly NavigationAreaPolicy _areaPolicy;
    private readonly TraversalMedia _medium;

    internal TraversalEvaluator(
        NavigationWorldGraph graph,
        NavigationAgentProfile profile,
        NavigationAreaPolicy areaPolicy,
        TraversalMedium medium)
    {
        SwiftThrowHelper.ThrowIfNull(graph, nameof(graph));
        SwiftThrowHelper.ThrowIfNull(areaPolicy, nameof(areaPolicy));
        profile.Validate(nameof(profile));
        TraversalMedia resolvedMedium = NavigationCell.ToMedia(medium);
        SwiftThrowHelper.ThrowIfArgument(
            resolvedMedium == TraversalMedia.None,
            nameof(medium),
            "Traversal medium must resolve to one exact authored medium.");

        _graph = graph;
        _profile = profile;
        _areaPolicy = areaPolicy;
        _medium = resolvedMedium;
    }

    internal NavigationAgentProfile Profile => _profile;

    internal NavigationAreaPolicy AreaPolicy => _areaPolicy;

    internal bool TryGetPassableNodeState(
        NavigationNodeRef node,
        out NavigationNodeState state) =>
        TryGetPassableNode(node, out state, out _);

    internal TraversalEvaluationStatus EvaluateEdge(
        NavigationNodeRef source,
        in NavigationGraphEdge edge,
        out TraversalEdgeEvidence evidence)
    {
        return edge.Kind switch
        {
            NavigationGraphEdgeKind.Explicit => EvaluateExplicitEdge(
                source,
                edge,
                out evidence),
            NavigationGraphEdgeKind.Seam => EvaluateAutomaticSeam(
                source,
                edge,
                out evidence),
            _ => EvaluateNative(source, edge, out evidence)
        };
    }

    internal TraversalExplicitEdgeStatus BeginExplicitEdge(
        NavigationNodeRef source,
        in NavigationGraphEdge edge,
        out TraversalExplicitEdgeWork work)
    {
        work = default;
        NavigationExplicitConnectionRecord record = edge.ExplicitConnection;
        if (record == null
            || !record.IsActive
            || edge.Kind != NavigationGraphEdgeKind.Explicit
            || !_graph.TryGetNodeAddress(source, out NavigationCellAddress sourceAddress)
            || !_graph.TryGetNodeAddress(edge.Target, out NavigationCellAddress targetAddress)
            || record.NavigationPortals.Count != record.Definition.Witnesses.Count + 1
            || !_graph.TryGetSeamPrism(sourceAddress, out GridCellPrism sourcePrism))
        {
            return TraversalExplicitEdgeStatus.Stale;
        }
        if (!sourceAddress.Equals(record.Source)
            || !targetAddress.Equals(record.Destination))
        {
            return TraversalExplicitEdgeStatus.Stale;
        }
        if (!TryGetPassableNode(source, out NavigationNodeState sourceState, out _)
            || !TryGetPassableNode(
                edge.Target,
                out NavigationNodeState targetState,
                out NavigationAreaRule targetRule))
        {
            return TraversalExplicitEdgeStatus.Impassable;
        }

        KinematicBodyShape shape = _profile.Shape;
        NavigationConnection connection = record.Definition;
        if (shape.Radius > connection.PortalRadiusClearance
            || shape.Height > connection.PortalHeightClearance)
        {
            return TraversalExplicitEdgeStatus.Impassable;
        }

        work = new TraversalExplicitEdgeWork
        {
            Record = record,
            Target = edge.Target,
            Portals = record.NavigationPortals.GetEnumerator(),
            PreviousAddress = sourceAddress,
            PreviousPrism = sourcePrism,
            SourceFootAnchor = sourceState.FootAnchor,
            TargetFootAnchor = targetState.FootAnchor,
            PreviousFootAnchor = sourceState.FootAnchor,
            TargetEnterCost = targetState.Cell.EnterCost,
            TargetAreaEnterCost = targetRule.AdditionalEnterCost
        };
        return TraversalExplicitEdgeStatus.Pending;
    }

    internal TraversalExplicitEdgeStatus AdvanceExplicitEdge(
        ref TraversalExplicitEdgeWork work,
        out TraversalEdgeEvidence evidence)
    {
        evidence = default;
        NavigationExplicitConnectionRecord record = work.Record;
        NavigationConnection connection = record.Definition;
        bool final = work.WitnessIndex >= connection.Witnesses.Count;
        NavigationCellAddress nextAddress = final
            ? record.Destination
            : connection.Witnesses[work.WitnessIndex];
        NavigationNodeRef dependencyNode;
        Vector3d nextFootAnchor;
        if (final)
        {
            dependencyNode = work.Target;
            evidence = new TraversalEdgeEvidence(dependencyNode);
            nextFootAnchor = work.TargetFootAnchor;
        }
        else
        {
            if (!_graph.TryGetNodeRef(nextAddress, out dependencyNode))
                return TraversalExplicitEdgeStatus.Stale;
            evidence = new TraversalEdgeEvidence(dependencyNode);
            if (!TryGetPassableNode(
                    dependencyNode,
                    out NavigationNodeState nextState,
                    out _))
            {
                return TraversalExplicitEdgeStatus.Impassable;
            }
            nextFootAnchor = nextState.FootAnchor;
        }

        if (!_graph.TryGetSeamPrism(nextAddress, out GridCellPrism nextPrism)
            || !work.Portals.MoveNext())
        {
            return TraversalExplicitEdgeStatus.Stale;
        }
        GridNavigationPortal retainedPortal = work.Portals.Current;
        KinematicBodyShape shape = _profile.Shape;
        if (!retainedPortal.IsValid
            || !GridCellGeometry.TryCreateNavigationPortal(
                work.PreviousPrism,
                nextPrism,
                out GridNavigationPortal portal)
            || !IsSamePortal(retainedPortal, portal))
        {
            return TraversalExplicitEdgeStatus.Stale;
        }
        if (shape.Radius > portal.MaximumHorizontalRadius
            || shape.Height > portal.MaximumBodyHeight)
        {
            return TraversalExplicitEdgeStatus.Impassable;
        }
        if (!portal.TryResolveProfile(
                shape.Radius,
                shape.Height,
                out Vector3d sourcePortalAnchor,
                out Vector3d targetPortalAnchor))
        {
            return TraversalExplicitEdgeStatus.CostOverflow;
        }
        if (!IsPortalTransitionValid(
                work.PreviousPrism,
                nextPrism,
                portal,
                sourcePortalAnchor,
                targetPortalAnchor,
                shape))
        {
            return TraversalExplicitEdgeStatus.Impassable;
        }

        TraversalEvaluationStatus vertical = EvaluateVerticalDelta(
            work.PreviousFootAnchor,
            nextFootAnchor);
        if (vertical != TraversalEvaluationStatus.Passable)
            return ToExplicitStatus(vertical);

        Fixed64 cost = Fixed64.Zero;
        Fixed64 total = Fixed64.Zero;
        if (final
            && (!Vector3d.TryGetDistance(
                work.SourceFootAnchor,
                connection.EntryAnchor,
                out Fixed64 sourceDistance)
            || !Vector3d.TryGetDistance(
                connection.ExitAnchor,
                work.TargetFootAnchor,
                out Fixed64 targetDistance)
            || !Fixed64.TryAdd(sourceDistance, record.CorridorCost, out total)
            || !Fixed64.TryAdd(total, targetDistance, out total)
            || !Fixed64.TryAdd(total, connection.AdditionalCost, out total)
            || !Fixed64.TryAdd(total, work.TargetEnterCost, out total)
            || !Fixed64.TryAdd(total, work.TargetAreaEnterCost, out total)))
        {
            return TraversalExplicitEdgeStatus.CostOverflow;
        }
        if (final)
        {
            cost = total;
            if (work.Portals.MoveNext())
                return TraversalExplicitEdgeStatus.Stale;
        }

        evidence = new TraversalEdgeEvidence(
            cost,
            portal,
            sourcePortalAnchor,
            targetPortalAnchor,
            work.PreviousAddress,
            nextAddress,
            work.PreviousFootAnchor,
            nextFootAnchor,
            work.PreviousPrism,
            nextPrism,
            dependencyNode);
        work.PreviousAddress = nextAddress;
        work.PreviousPrism = nextPrism;
        work.PreviousFootAnchor = nextFootAnchor;
        if (!final)
            work.WitnessIndex++;
        return final
            ? TraversalExplicitEdgeStatus.Passable
            : TraversalExplicitEdgeStatus.Pending;
    }

    private TraversalEvaluationStatus EvaluateNative(
        NavigationNodeRef source,
        in NavigationGraphEdge edge,
        out TraversalEdgeEvidence evidence)
    {
        evidence = default;
        if (edge.Kind != NavigationGraphEdgeKind.Native
            || !TryGetPassableNode(source, out NavigationNodeState sourceState, out _)
            || !TryGetPassableNode(
                edge.Target,
                out NavigationNodeState targetState,
                out NavigationAreaRule targetRule))
        {
            return TraversalEvaluationStatus.Impassable;
        }

        KinematicBodyShape shape = _profile.Shape;
        GridNavigationPortal template = edge.NativePortal;
        if (!template.IsValid)
            return TraversalEvaluationStatus.Stale;
        if (shape.Radius > template.MaximumHorizontalRadius
            || shape.Height > template.MaximumBodyHeight)
        {
            return TraversalEvaluationStatus.Impassable;
        }

        if (!_graph.TryGetNodeAddress(source, out NavigationCellAddress sourceAddress)
            || !_graph.TryGetNodeAddress(edge.Target, out NavigationCellAddress targetAddress)
            || !_graph.TryGetSeamPrism(sourceAddress, out GridCellPrism sourcePrism)
            || !_graph.TryGetSeamPrism(targetAddress, out GridCellPrism targetPrism))
        {
            return TraversalEvaluationStatus.Stale;
        }
        if (!template.TryTranslate(sourceState.Center, out GridNavigationPortal retainedPortal))
            return TraversalEvaluationStatus.CostOverflow;
        if (!GridCellGeometry.TryCreateNavigationPortal(
                sourcePrism,
                targetPrism,
                out GridNavigationPortal portal)
            || !IsSamePortal(retainedPortal, portal))
        {
            return TraversalEvaluationStatus.Stale;
        }
        if (!portal.TryResolveProfile(
                shape.Radius,
                shape.Height,
                out Vector3d sourcePortalFoot,
                out Vector3d targetPortalFoot))
        {
            return TraversalEvaluationStatus.CostOverflow;
        }
        TraversalEvaluationStatus vertical = EvaluateVerticalDelta(
            sourceState.FootAnchor,
            targetState.FootAnchor);
        if (vertical != TraversalEvaluationStatus.Passable)
            return vertical;

        if (!NavigationDistanceMath.TryCeiling(
                sourceState.FootAnchor,
                sourcePortalFoot,
                out Fixed64 sourceDistance)
            || !NavigationDistanceMath.TryCeiling(
                targetPortalFoot,
                targetState.FootAnchor,
                out Fixed64 targetDistance)
            || !Fixed64.TryAdd(sourceDistance, targetDistance, out Fixed64 total)
            || !Fixed64.TryAdd(total, targetState.Cell.EnterCost, out total)
            || !Fixed64.TryAdd(total, targetRule.AdditionalEnterCost, out total))
        {
            return TraversalEvaluationStatus.CostOverflow;
        }

        evidence = new TraversalEdgeEvidence(
            total,
            portal,
            sourcePortalFoot,
            targetPortalFoot,
            sourceAddress,
            targetAddress,
            sourceState.FootAnchor,
            targetState.FootAnchor,
            sourcePrism,
            targetPrism,
            default);
        return TraversalEvaluationStatus.Passable;
    }

    private TraversalEvaluationStatus EvaluateExplicitEdge(
        NavigationNodeRef source,
        in NavigationGraphEdge edge,
        out TraversalEdgeEvidence evidence)
    {
        evidence = default;
        Fixed64 cost = Fixed64.Zero;
        TraversalExplicitEdgeStatus status = BeginExplicitEdge(source, edge, out TraversalExplicitEdgeWork work);
        while (status == TraversalExplicitEdgeStatus.Pending)
        {
            status = AdvanceExplicitEdge(ref work, out TraversalEdgeEvidence step);
            cost = step.Cost;
        }
        TraversalEvaluationStatus result = status switch
        {
            TraversalExplicitEdgeStatus.Passable => TraversalEvaluationStatus.Passable,
            TraversalExplicitEdgeStatus.CostOverflow => TraversalEvaluationStatus.CostOverflow,
            TraversalExplicitEdgeStatus.Stale => TraversalEvaluationStatus.Stale,
            _ => TraversalEvaluationStatus.Impassable
        };
        if (result == TraversalEvaluationStatus.Passable)
            evidence = new TraversalEdgeEvidence(
                cost,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default,
                default);
        return result;
    }

    private static TraversalExplicitEdgeStatus ToExplicitStatus(
        TraversalEvaluationStatus status) => status switch
        {
            TraversalEvaluationStatus.CostOverflow => TraversalExplicitEdgeStatus.CostOverflow,
            TraversalEvaluationStatus.Stale => TraversalExplicitEdgeStatus.Stale,
            _ => TraversalExplicitEdgeStatus.Impassable
        };

    private TraversalEvaluationStatus EvaluateAutomaticSeam(
        NavigationNodeRef source,
        in NavigationGraphEdge edge,
        out TraversalEdgeEvidence evidence)
    {
        evidence = default;
        NavigationAutomaticSeamRef seam = edge.AutomaticSeam;
        if (edge.Kind != NavigationGraphEdgeKind.Seam
            || seam.Pair == null
            || !_graph.AutomaticSeams.IsActive(seam)
            || !_graph.TryGetNodeAddress(source, out NavigationCellAddress sourceAddress)
            || !sourceAddress.Equals(seam.Source)
            || !_graph.TryGetNodeAddress(edge.Target, out NavigationCellAddress targetAddress)
            || !targetAddress.Equals(seam.Destination))
        {
            return TraversalEvaluationStatus.Stale;
        }
        if (!TryGetPassableNode(source, out NavigationNodeState sourceState, out _)
            || !TryGetPassableNode(
                edge.Target,
                out NavigationNodeState targetState,
                out NavigationAreaRule targetRule))
        {
            return TraversalEvaluationStatus.Impassable;
        }

        KinematicBodyShape shape = _profile.Shape;
        GridNavigationPortal retainedPortal = seam.Portal;
        if (!retainedPortal.IsValid)
            return TraversalEvaluationStatus.Stale;
        if (shape.Radius > retainedPortal.MaximumHorizontalRadius
            || shape.Height > retainedPortal.MaximumBodyHeight)
        {
            return TraversalEvaluationStatus.Impassable;
        }
        if (!_graph.TryGetSeamPrism(sourceAddress, out GridCellPrism sourcePrism)
            || !_graph.TryGetSeamPrism(targetAddress, out GridCellPrism targetPrism)
            || !GridCellGeometry.TryCreateNavigationPortal(
                sourcePrism,
                targetPrism,
                out GridNavigationPortal portal)
            || !IsSamePortal(
                retainedPortal,
                portal,
                seam.IsReverse))
        {
            return TraversalEvaluationStatus.Stale;
        }
        if (!portal.TryResolveProfile(
                shape.Radius,
                shape.Height,
                out Vector3d sourcePortalFoot,
                out Vector3d targetPortalFoot))
        {
            return TraversalEvaluationStatus.CostOverflow;
        }
        TraversalEvaluationStatus vertical = EvaluateVerticalDelta(
            sourceState.FootAnchor,
            targetState.FootAnchor);
        if (vertical != TraversalEvaluationStatus.Passable)
            return vertical;
        bool verticalPortal = portal.FaceKind == VoxelContactFaceKind.Vertical;
        if (!(verticalPortal
                ? NavigationDistanceMath.TryCeiling(
                    sourceState.FootAnchor,
                    sourcePortalFoot,
                    out Fixed64 sourceDistance)
                : Vector3d.TryGetDistance(
                    sourceState.FootAnchor,
                    sourcePortalFoot,
                    out sourceDistance))
            || !(verticalPortal
                ? NavigationDistanceMath.TryCeiling(
                    targetPortalFoot,
                    targetState.FootAnchor,
                    out Fixed64 targetDistance)
                : Vector3d.TryGetDistance(
                    targetPortalFoot,
                    targetState.FootAnchor,
                    out targetDistance))
            || !Fixed64.TryAdd(sourceDistance, targetDistance, out Fixed64 total)
            || !Fixed64.TryAdd(total, targetState.Cell.EnterCost, out total)
            || !Fixed64.TryAdd(total, targetRule.AdditionalEnterCost, out total))
        {
            return TraversalEvaluationStatus.CostOverflow;
        }

        evidence = new TraversalEdgeEvidence(
            total,
            portal,
            sourcePortalFoot,
            targetPortalFoot,
            sourceAddress,
            targetAddress,
            sourceState.FootAnchor,
            targetState.FootAnchor,
            sourcePrism,
            targetPrism,
            default);
        return TraversalEvaluationStatus.Passable;
    }

    internal static bool IsPortalTransitionValid(
        in GridCellPrism sourcePrism,
        in GridCellPrism targetPrism,
        in GridNavigationPortal portal,
        Vector3d sourceAnchor,
        Vector3d targetAnchor,
        in KinematicBodyShape shape)
    {
        if (portal.FaceKind != VoxelContactFaceKind.Horizontal)
            return true;
        return GridCellGeometry.TryGetNavigationPortalTraversalParameters(
                sourcePrism,
                targetPrism,
                portal,
                sourceAnchor,
                targetAnchor,
                shape.Radius,
                shape.Height,
                out Fixed64 sourceParameter,
                out Fixed64 targetParameter)
            && sourceParameter == Fixed64.Zero
            && targetParameter == Fixed64.One;
    }

    internal static bool IsSamePortal(
        in GridNavigationPortal expected,
        in GridNavigationPortal actual,
        bool reverseSourceToTarget = false) =>
        expected.FaceKind == actual.FaceKind
        && (reverseSourceToTarget
            ? -expected.SourceToTarget == actual.SourceToTarget
            : expected.SourceToTarget == actual.SourceToTarget)
        && expected.CanonicalFacePoint == actual.CanonicalFacePoint
        && expected.MaximumHorizontalRadius == actual.MaximumHorizontalRadius
        && expected.MaximumBodyHeight == actual.MaximumBodyHeight
        && expected.VerticalFaceSegmentStart == actual.VerticalFaceSegmentStart
        && expected.VerticalFaceSegmentEnd == actual.VerticalFaceSegmentEnd;

    private TraversalEvaluationStatus EvaluateVerticalDelta(
        Vector3d sourceFootAnchor,
        Vector3d targetFootAnchor)
    {
        if (!Fixed64.TrySubtract(
                targetFootAnchor.Y,
                sourceFootAnchor.Y,
                out Fixed64 verticalDelta))
        {
            return TraversalEvaluationStatus.CostOverflow;
        }
        if (verticalDelta > Fixed64.Zero)
        {
            return verticalDelta <= _profile.MaxStepUp
                ? TraversalEvaluationStatus.Passable
                : TraversalEvaluationStatus.Impassable;
        }
        if (verticalDelta >= Fixed64.Zero)
            return TraversalEvaluationStatus.Passable;
        if (!Fixed64.TrySubtract(Fixed64.Zero, verticalDelta, out Fixed64 drop))
            return TraversalEvaluationStatus.CostOverflow;
        return drop <= _profile.MaxDropDown
            ? TraversalEvaluationStatus.Passable
            : TraversalEvaluationStatus.Impassable;
    }

    private bool TryGetPassableNode(
        NavigationNodeRef node,
        out NavigationNodeState state,
        out NavigationAreaRule areaRule)
    {
        areaRule = default;
        if (!_graph.TryGetNodeState(node, out state)
            || !state.IsPresent
            || state.ObstacleCount != 0
            || (_profile.AllowedMedia & _medium) != _medium
            || (state.Cell.Media & _medium) != _medium
            || (_profile.Capabilities & state.Cell.RequiredCapabilities)
                != state.Cell.RequiredCapabilities
            || !_areaPolicy.TryGetRule(state.Cell.Area, out areaRule)
            || !areaRule.IsAllowed
            || _profile.Shape.Radius > state.Cell.RadiusClearance
            || _profile.Shape.Height > state.Cell.HeightClearance)
        {
            state = default;
            areaRule = default;
            return false;
        }

        return true;
    }
}

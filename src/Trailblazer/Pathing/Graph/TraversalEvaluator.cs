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
    CostOverflow = 2
}

/// <summary>Reports progress through one bounded explicit-connection evaluation.</summary>
internal enum TraversalExplicitEdgeStatus : byte
{
    Pending = 0,
    Passable = 1,
    Impassable = 2,
    CostOverflow = 3
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
    private readonly TraversalMedium _exactMedium;

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
        _exactMedium = medium;
    }

    internal NavigationAgentProfile Profile => _profile;

    internal NavigationAreaPolicy AreaPolicy => _areaPolicy;

    internal NavigationWorldGraph Graph => _graph;

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
        bool hasSourceAddress = _graph.TryGetNodeAddress(
            source,
            out NavigationCellAddress sourceAddress);
        bool hasTargetAddress = _graph.TryGetNodeAddress(
            edge.Target,
            out NavigationCellAddress targetAddress);
        bool hasSourcePrism = _graph.TryGetSeamPrism(
            sourceAddress,
            out GridCellPrism sourcePrism);
        System.Diagnostics.Debug.Assert(
            record != null
            && record.IsActive
            && edge.Kind == NavigationGraphEdgeKind.Explicit
            && hasSourceAddress
            && hasTargetAddress
            && record.NavigationPortals.Count == record.Definition.Witnesses.Count + 1
            && hasSourcePrism
            && sourceAddress.Equals(record.Source)
            && targetAddress.Equals(record.Destination));
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
            bool hasDependencyNode = _graph.TryGetNodeRef(nextAddress, out dependencyNode);
            System.Diagnostics.Debug.Assert(hasDependencyNode);
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

        bool hasNextPrism = _graph.TryGetSeamPrism(nextAddress, out GridCellPrism nextPrism);
        bool hasPortal = work.Portals.MoveNext();
        System.Diagnostics.Debug.Assert(hasNextPrism && hasPortal);
        GridNavigationPortal portal = work.Portals.Current;
        KinematicBodyShape shape = _profile.Shape;
        System.Diagnostics.Debug.Assert(portal.IsValid);
        bool resolvedProfile = portal.TryResolveProfile(
            shape.Radius,
            shape.Height,
            out Vector3d sourcePortalAnchor,
            out Vector3d targetPortalAnchor);
        System.Diagnostics.Debug.Assert(resolvedProfile);
        System.Diagnostics.Debug.Assert(
            portal.FaceKind != VoxelContactFaceKind.Horizontal
            || (GridCellGeometry.TryGetNavigationPortalTraversalParameters(
                    work.PreviousPrism,
                    nextPrism,
                    portal,
                    sourcePortalAnchor,
                    targetPortalAnchor,
                    shape.Radius,
                    shape.Height,
                    out Fixed64 sourceParameter,
                    out Fixed64 targetParameter)
                && sourceParameter == Fixed64.Zero
                && targetParameter == Fixed64.One),
            "published explicit corridors retain the exact validated profile crossing");

        TraversalEvaluationStatus vertical = EvaluateVerticalDelta(
            work.PreviousFootAnchor,
            nextFootAnchor);
        if (vertical != TraversalEvaluationStatus.Passable)
            return TraversalExplicitEdgeStatus.Impassable;

        Fixed64 cost = Fixed64.Zero;
        if (final
            && !TryGetExplicitConnectionCost(
                work.SourceFootAnchor,
                connection.EntryAnchor,
                record.CorridorCost,
                connection.ExitAnchor,
                work.TargetFootAnchor,
                connection.AdditionalCost,
                work.TargetEnterCost,
                work.TargetAreaEnterCost,
                out cost))
        {
            return TraversalExplicitEdgeStatus.CostOverflow;
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
        if (!TryGetPassableNode(source, out NavigationNodeState sourceState, out _)
            || !TryGetPassableNode(
                edge.Target,
                out NavigationNodeState targetState,
                out NavigationAreaRule targetRule))
        {
            return TraversalEvaluationStatus.Impassable;
        }

        KinematicBodyShape shape = _profile.Shape;
        GridNavigationPortal template = edge.NativePortal;
        System.Diagnostics.Debug.Assert(template.IsValid);
        if (shape.Radius > template.MaximumHorizontalRadius
            || shape.Height > template.MaximumBodyHeight)
        {
            return TraversalEvaluationStatus.Impassable;
        }

        bool hasSourceAddress = _graph.TryGetNodeAddress(
            source,
            out NavigationCellAddress sourceAddress);
        bool hasTargetAddress = _graph.TryGetNodeAddress(
            edge.Target,
            out NavigationCellAddress targetAddress);
        bool hasSourcePrism = _graph.TryGetSeamPrism(
            sourceAddress,
            out GridCellPrism sourcePrism);
        bool hasTargetPrism = _graph.TryGetSeamPrism(
            targetAddress,
            out GridCellPrism targetPrism);
        System.Diagnostics.Debug.Assert(
            hasSourceAddress && hasTargetAddress && hasSourcePrism && hasTargetPrism);
        bool translated = template.TryTranslate(
            sourceState.Center,
            out GridNavigationPortal retainedPortal);
        System.Diagnostics.Debug.Assert(translated);
        bool createdPortal = GridCellGeometry.TryCreateNavigationPortal(
            sourcePrism,
            targetPrism,
            out GridNavigationPortal portal);
        System.Diagnostics.Debug.Assert(createdPortal && retainedPortal.IsValid);
        bool resolvedProfile = portal.TryResolveProfile(
            shape.Radius,
            shape.Height,
            out Vector3d sourcePortalFoot,
            out Vector3d targetPortalFoot);
        System.Diagnostics.Debug.Assert(resolvedProfile);

        if (!TryGetPortalTraversalCost(
                useCeilingDistance: true,
                sourceState.FootAnchor,
                sourcePortalFoot,
                targetPortalFoot,
                targetState.FootAnchor,
                targetState.Cell.EnterCost,
                targetRule.AdditionalEnterCost,
                out Fixed64 total))
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

    private TraversalEvaluationStatus EvaluateAutomaticSeam(
        NavigationNodeRef source,
        in NavigationGraphEdge edge,
        out TraversalEdgeEvidence evidence)
    {
        evidence = default;
        NavigationAutomaticSeamRef seam = edge.AutomaticSeam;
        bool hasSourceAddress = _graph.TryGetNodeAddress(
            source,
            out NavigationCellAddress sourceAddress);
        bool hasTargetAddress = _graph.TryGetNodeAddress(
            edge.Target,
            out NavigationCellAddress targetAddress);
        System.Diagnostics.Debug.Assert(
            edge.Kind == NavigationGraphEdgeKind.Seam
            && seam.Pair != null
            && _graph.AutomaticSeams.IsActive(seam)
            && hasSourceAddress
            && sourceAddress.Equals(seam.Source)
            && hasTargetAddress
            && targetAddress.Equals(seam.Destination));
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
        System.Diagnostics.Debug.Assert(retainedPortal.IsValid);
        if (shape.Radius > retainedPortal.MaximumHorizontalRadius
            || shape.Height > retainedPortal.MaximumBodyHeight)
        {
            return TraversalEvaluationStatus.Impassable;
        }
        bool hasSourcePrism = _graph.TryGetSeamPrism(
            sourceAddress,
            out GridCellPrism sourcePrism);
        bool hasTargetPrism = _graph.TryGetSeamPrism(
            targetAddress,
            out GridCellPrism targetPrism);
        bool createdPortal = GridCellGeometry.TryCreateNavigationPortal(
            sourcePrism,
            targetPrism,
            out GridNavigationPortal portal);
        System.Diagnostics.Debug.Assert(
            hasSourcePrism
            && hasTargetPrism
            && createdPortal
            && retainedPortal.IsValid);
        bool resolvedProfile = portal.TryResolveProfile(
            shape.Radius,
            shape.Height,
            out Vector3d sourcePortalFoot,
            out Vector3d targetPortalFoot);
        System.Diagnostics.Debug.Assert(resolvedProfile);
        TraversalEvaluationStatus vertical = EvaluateVerticalDelta(
            sourceState.FootAnchor,
            targetState.FootAnchor);
        if (vertical != TraversalEvaluationStatus.Passable)
            return vertical;
        if (!TryGetPortalTraversalCost(
                portal.FaceKind == VoxelContactFaceKind.Vertical,
                sourceState.FootAnchor,
                sourcePortalFoot,
                targetPortalFoot,
                targetState.FootAnchor,
                targetState.Cell.EnterCost,
                targetRule.AdditionalEnterCost,
                out Fixed64 total))
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

    internal static bool TryGetExplicitConnectionCost(
        Vector3d sourceFootAnchor,
        Vector3d entryAnchor,
        Fixed64 corridorCost,
        Vector3d exitAnchor,
        Vector3d targetFootAnchor,
        Fixed64 additionalCost,
        Fixed64 targetEnterCost,
        Fixed64 targetAreaEnterCost,
        out Fixed64 total)
    {
        total = Fixed64.Zero;
        return Vector3d.TryGetDistance(sourceFootAnchor, entryAnchor, out Fixed64 sourceDistance)
            && Vector3d.TryGetDistance(exitAnchor, targetFootAnchor, out Fixed64 targetDistance)
            && Fixed64.TryAdd(sourceDistance, corridorCost, out total)
            && Fixed64.TryAdd(total, targetDistance, out total)
            && Fixed64.TryAdd(total, additionalCost, out total)
            && Fixed64.TryAdd(total, targetEnterCost, out total)
            && Fixed64.TryAdd(total, targetAreaEnterCost, out total);
    }

    internal static bool TryGetPortalTraversalCost(
        bool useCeilingDistance,
        Vector3d sourceFootAnchor,
        Vector3d sourcePortalFoot,
        Vector3d targetPortalFoot,
        Vector3d targetFootAnchor,
        Fixed64 targetEnterCost,
        Fixed64 targetAreaEnterCost,
        out Fixed64 total)
    {
        total = Fixed64.Zero;
        bool hasSourceDistance = useCeilingDistance
            ? NavigationDistanceMath.TryCeiling(
                sourceFootAnchor,
                sourcePortalFoot,
                out Fixed64 sourceDistance)
            : Vector3d.TryGetDistance(
                sourceFootAnchor,
                sourcePortalFoot,
                out sourceDistance);
        bool hasTargetDistance = useCeilingDistance
            ? NavigationDistanceMath.TryCeiling(
                targetPortalFoot,
                targetFootAnchor,
                out Fixed64 targetDistance)
            : Vector3d.TryGetDistance(
                targetPortalFoot,
                targetFootAnchor,
                out targetDistance);
        return hasSourceDistance
            && hasTargetDistance
            && Fixed64.TryAdd(sourceDistance, targetDistance, out total)
            && Fixed64.TryAdd(total, targetEnterCost, out total)
            && Fixed64.TryAdd(total, targetAreaEnterCost, out total);
    }

    private TraversalEvaluationStatus EvaluateVerticalDelta(
        Vector3d sourceFootAnchor,
        Vector3d targetFootAnchor)
    {
        bool hasVerticalDelta = Fixed64.TrySubtract(
            targetFootAnchor.Y,
            sourceFootAnchor.Y,
            out Fixed64 verticalDelta);
        System.Diagnostics.Debug.Assert(hasVerticalDelta);
        if (verticalDelta > Fixed64.Zero)
        {
            return verticalDelta <= _profile.MaxStepUp
                ? TraversalEvaluationStatus.Passable
                : TraversalEvaluationStatus.Impassable;
        }
        if (verticalDelta >= Fixed64.Zero)
            return TraversalEvaluationStatus.Passable;
        bool hasDrop = Fixed64.TrySubtract(Fixed64.Zero, verticalDelta, out Fixed64 drop);
        System.Diagnostics.Debug.Assert(hasDrop);
        return drop <= _profile.MaxDropDown
            ? TraversalEvaluationStatus.Passable
            : TraversalEvaluationStatus.Impassable;
    }

    internal bool TryGetPassableNode(
        NavigationNodeRef node,
        out NavigationNodeState state,
        out NavigationAreaRule areaRule)
    {
        areaRule = default;
        if (!_graph.TryGetNodeState(node, _exactMedium, out state)
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

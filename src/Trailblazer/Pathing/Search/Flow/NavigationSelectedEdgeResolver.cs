//=======================================================================
// NavigationSelectedEdgeResolver.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using GridForge.Grids.Topology;

namespace Trailblazer.Pathing;

/// <summary>Reuses one native edge and its immutable node geometry within a guide binding.</summary>
internal struct NavigationSelectedEdgeResolver
{
    private long _graphVersion;
    private NavigationNodeRef _source;
    private int _outgoingOrdinal;
    private int _cursorLegScans;
    private NavigationGraphEdge _edge;
    // Only Solid samples use these snapshots; volume sampling bypasses this resolver.
    private bool _hasNodeStates;
    private NavigationNodeState _sourceState;
    private NavigationNodeState _targetState;
    private GridCellPrism _sourcePrism;
    private GridCellPrism _targetPrism;
    private NavigationFlowFieldNode _node;
    private int _nodeLookupProbes;

    internal readonly bool TryGetNode(
        NavigationWorldGraph graph,
        NavigationCellAddress address,
        ref GuideSampleWorkMeter meter,
        out NavigationFlowFieldNode node,
        out NavigationNodeRef source)
    {
        if (_hasNodeStates
            && _graphVersion == graph.GraphVersion
            && _node.Address == address
            && meter.TryConsumeCurrentNodeLookupProbes(_nodeLookupProbes))
        {
            node = _node;
            source = _source;
            return true;
        }
        node = default;
        source = default;
        return false;
    }

    internal readonly bool TryGetSourceState(
        NavigationWorldGraph graph,
        NavigationNodeRef source,
        int outgoingOrdinal,
        out NavigationNodeState state,
        out GridCellPrism prism)
    {
        if (_hasNodeStates && Matches(graph, source, outgoingOrdinal))
        {
            state = _sourceState;
            prism = _sourcePrism;
            return true;
        }
        state = default;
        prism = default;
        return false;
    }

    internal readonly void GetTargetState(out NavigationNodeState state, out GridCellPrism prism)
    {
        state = _targetState;
        prism = _targetPrism;
    }

    internal void RememberNodeStates(
        in NavigationFlowFieldNode node,
        int nodeLookupProbes,
        in NavigationNodeState sourceState,
        in GridCellPrism sourcePrism,
        in NavigationNodeState targetState,
        in GridCellPrism targetPrism)
    {
        _node = node;
        _nodeLookupProbes = nodeLookupProbes;
        _sourceState = sourceState;
        _sourcePrism = sourcePrism;
        _targetState = targetState;
        _targetPrism = targetPrism;
        _hasNodeStates = true;
    }

    private readonly bool Matches(
        NavigationWorldGraph graph,
        NavigationNodeRef source,
        int outgoingOrdinal) => _cursorLegScans != 0
        && _graphVersion == graph.GraphVersion
        && _source.Equals(source)
        && _outgoingOrdinal == outgoingOrdinal;

    internal NavigationGuideStatus TryResolve(
        NavigationWorldGraph graph,
        NavigationNodeRef source,
        int outgoingOrdinal,
        ref GuideSampleWorkMeter meter,
        out NavigationGraphEdge edge)
    {
        // Store versions increase strictly; the owning lease clears this state on
        // rebinding. Structural ordering depends on the root and source, not medium.
        if (Matches(graph, source, outgoingOrdinal)
            && meter.TryConsumeCursorLegScans(_cursorLegScans))
        {
            edge = _edge;
            return NavigationGuideStatus.Success;
        }

        // An insufficient warm allowance must consume exactly the same prefix as
        // a cold enumeration. Failed bulk consumption above leaves the meter intact.
        int initialAllowance = meter.GetCursorLegScanAllowance();
        NavigationSurfaceEdgeEnumerator edges = graph.EnumerateStructuralSurfaceEdges(source);
        int edgeStepRemaining = int.MaxValue;
        while (true)
        {
            NavigationSurfaceEdgeAdvanceStatus status = edges.AdvanceOne(
                ref meter,
                ref edgeStepRemaining);
            NavigationGuideStatus mappedStatus =
                NavigationSelectedEdgeProgressWork.MapStructuralEdgeStatus(status);
            if (mappedStatus != NavigationGuideStatus.Success)
            {
                edge = default;
                return mappedStatus;
            }
            if (status == NavigationSurfaceEdgeAdvanceStatus.Pending)
                continue;
            if (edges.CurrentOrdinal != outgoingOrdinal)
                continue;
            edge = edges.Current;
            // Explicit records and automatic seams retain graph-owned objects.
            // A native edge is value-only, so this memo never pins a retired root.
            if (edge.Kind == NavigationGraphEdgeKind.Native)
            {
                _graphVersion = graph.GraphVersion;
                _source = source;
                _outgoingOrdinal = outgoingOrdinal;
                _cursorLegScans = initialAllowance - meter.GetCursorLegScanAllowance();
                _edge = edge;
                _hasNodeStates = false;
            }
            return NavigationGuideStatus.Success;
        }
    }
}

//=======================================================================
// NavigationIncomingSurfaceEdgeEnumerator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Enumerates original forward edges into one destination in canonical order.</summary>
internal struct NavigationIncomingSurfaceEdgeEnumerator
{
    private readonly NavigationWorldGraph? _graph;
    private readonly NavigationNodeRef _destination;
    private readonly NavigationCellAddress _destinationAddress;
    private NavigationSurfaceEdgeEnumerator _incomingCandidates;
    private NavigationSurfaceEdgeEnumerator _outgoingEdges;
    private NavigationGraphEdge _incomingCandidate;
    private NavigationNodeRef _predecessor;
    private int _outgoingOrdinal;
    private bool _hasIncomingCandidate;

    internal NavigationIncomingSurfaceEdgeEnumerator(
        NavigationWorldGraph graph,
        NavigationNodeRef destination)
    {
        _destination = destination;
        _outgoingEdges = default;
        _incomingCandidate = default;
        _predecessor = default;
        _outgoingOrdinal = 0;
        _hasIncomingCandidate = false;
        Current = default;
        if (!graph.TryGetNodeAddress(destination, out _destinationAddress)
            || !graph.TryGetNodeState(destination, out NavigationNodeState state)
            || !state.IsPresent)
        {
            _graph = null;
            _incomingCandidates = default;
            _destinationAddress = default;
            return;
        }

        _graph = graph;
        _incomingCandidates = new NavigationSurfaceEdgeEnumerator(
            graph,
            destination,
            incoming: true,
            includeNative: true,
            includeAutomaticSeams: true);
    }

    internal NavigationIncomingSurfaceEdge Current { get; private set; }

    internal bool MoveNext()
    {
        int unbounded = int.MaxValue;
        while (true)
        {
            NavigationSurfaceEdgeAdvanceStatus status = AdvanceOne(null, ref unbounded);
            if (status == NavigationSurfaceEdgeAdvanceStatus.Edge)
                return true;
            if (status == NavigationSurfaceEdgeAdvanceStatus.Complete)
                return false;
        }
    }

    internal NavigationSurfaceEdgeAdvanceStatus AdvanceOne(
        NavigationWorkMeter? meter,
        ref int edgeStepRemaining)
    {
        if (_graph == null)
            return NavigationSurfaceEdgeAdvanceStatus.Complete;

        while (true)
        {
            if (!_hasIncomingCandidate)
            {
                NavigationSurfaceEdgeAdvanceStatus incomingStatus =
                    _incomingCandidates.AdvanceOne(meter, ref edgeStepRemaining);
                if (incomingStatus == NavigationSurfaceEdgeAdvanceStatus.Complete)
                    Current = default;
                if (incomingStatus != NavigationSurfaceEdgeAdvanceStatus.Edge)
                    return incomingStatus;

                _incomingCandidate = _incomingCandidates.Current;
                _predecessor = _incomingCandidate.Target;
                _outgoingEdges = _graph.EnumerateSurfaceEdges(_predecessor);
                _outgoingOrdinal = 0;
                _hasIncomingCandidate = true;
            }

            NavigationSurfaceEdgeAdvanceStatus outgoingStatus =
                _outgoingEdges.AdvanceOne(meter, ref edgeStepRemaining);
            if (outgoingStatus == NavigationSurfaceEdgeAdvanceStatus.Blocked
                || outgoingStatus == NavigationSurfaceEdgeAdvanceStatus.Pending)
            {
                return outgoingStatus;
            }
            if (outgoingStatus == NavigationSurfaceEdgeAdvanceStatus.Complete)
            {
                ClearIncomingCandidate();
                continue;
            }

            NavigationGraphEdge forwardEdge = _outgoingEdges.Current;
            int ordinal = _outgoingOrdinal++;
            if (!MatchesIncomingCandidate(forwardEdge))
                continue;

            Current = new NavigationIncomingSurfaceEdge(
                _predecessor,
                forwardEdge,
                new NavigationSelectedEdgeRef(_destinationAddress, ordinal));
            ClearIncomingCandidate();
            return NavigationSurfaceEdgeAdvanceStatus.Edge;
        }
    }

    private bool MatchesIncomingCandidate(in NavigationGraphEdge forwardEdge)
    {
        if (forwardEdge.Target != _destination
            || forwardEdge.Kind != _incomingCandidate.Kind)
        {
            return false;
        }

        return forwardEdge.Kind switch
        {
            NavigationGraphEdgeKind.Explicit => ReferenceEquals(
                forwardEdge.ExplicitConnection,
                _incomingCandidate.ExplicitConnection),
            NavigationGraphEdgeKind.Seam => ReferenceEquals(
                forwardEdge.AutomaticSeam.Pair,
                _incomingCandidate.AutomaticSeam.Pair),
            _ => true
        };
    }

    private void ClearIncomingCandidate()
    {
        _outgoingEdges = default;
        _incomingCandidate = default;
        _predecessor = default;
        _outgoingOrdinal = 0;
        _hasIncomingCandidate = false;
    }
}

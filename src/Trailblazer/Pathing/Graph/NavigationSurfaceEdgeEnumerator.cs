//=======================================================================
// NavigationSurfaceEdgeEnumerator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

internal enum NavigationSurfaceEdgeAdvanceStatus : byte
{
    Pending = 0,
    Edge = 1,
    Complete = 2,
    Blocked = 3
}

/// <summary>Merges native, explicit, and automatic-seam edges in durable canonical order.</summary>
internal struct NavigationSurfaceEdgeEnumerator
{
    private readonly NavigationWorldGraph? _graph;
    private readonly NavigationCellAddress _origin;
    private NavigationPagedSequence<NavigationConnectionOwnerKey>.Enumerator _incident;
    private NavigationNativeSurfaceEdgeEnumerator _native;
    private NavigationAutomaticSeamIndex.EndpointEnumerator _seam;
    private readonly bool _incoming;
    private readonly bool _structural;
    private bool _nativeComplete;
    private bool _explicitComplete;
    private bool _seamComplete;
    private bool _hasNative;
    private bool _hasExplicit;
    private bool _hasSeam;
    private bool _nativeNeedsDebit;
    private bool _hasPendingExplicitOwner;
    private bool _hasPendingSeam;
    private NavigationConnectionOwnerKey _pendingExplicitOwner;
    private NavigationAutomaticSeamRef _pendingSeam;
    private NavigationGraphEdge _nativeEdge;
    private NavigationGraphEdge _explicitEdge;
    private NavigationGraphEdge _seamEdge;
    private NavigationCellAddress _nativeEndpoint;
    private NavigationCellAddress _explicitEndpoint;
    private NavigationCellAddress _seamEndpoint;
    private int _currentOrdinal;

    internal NavigationSurfaceEdgeEnumerator(
        NavigationWorldGraph graph,
        NavigationNodeRef origin,
        bool incoming,
        bool includeNative,
        bool includeAutomaticSeams,
        bool structural = false)
    {
        _graph = graph;
        _incoming = incoming;
        _structural = structural;
        _nativeComplete = !includeNative;
        _explicitComplete = false;
        _seamComplete = !includeAutomaticSeams;
        _hasNative = false;
        _hasExplicit = false;
        _hasSeam = false;
        _nativeNeedsDebit = false;
        _hasPendingExplicitOwner = false;
        _hasPendingSeam = false;
        _pendingExplicitOwner = default;
        _pendingSeam = default;
        _nativeEdge = default;
        _explicitEdge = default;
        _seamEdge = default;
        _nativeEndpoint = default;
        _explicitEndpoint = default;
        _seamEndpoint = default;
        _currentOrdinal = -1;
        Current = default;
        if (!graph.TryGetNodeAddress(origin, out _origin)
            || (structural
                ? !graph.HasEffectiveCell(_origin)
                : !graph.TryGetNodeState(origin, out NavigationNodeState state)
                    || !state.IsPresent))
        {
            _graph = null;
            _origin = default;
            _incident = default;
            _native = default;
            _seam = default;
            return;
        }
        NavigationPagedSequence<NavigationConnectionOwnerKey> endpoints =
            graph.ExplicitConnections.GetEndpointOwnerRow(_origin);
        _incident = endpoints.GetEnumerator();
        _seam = includeAutomaticSeams
            ? graph.AutomaticSeams.GetActiveEndpointEnumerator(_origin)
            : default;
        _native = includeNative
            ? structural
                ? graph.EnumerateStructuralNativeSurfaceEdges(origin)
                : graph.EnumerateNativeSurfaceEdges(origin)
            : default;
    }

    internal NavigationGraphEdge Current { get; private set; }

    internal int CurrentOrdinal => _currentOrdinal;

    internal bool MoveNext()
    {
        int unbounded = int.MaxValue;
        while (true)
        {
            NavigationSurfaceEdgeAdvanceStatus status = AdvanceOne(
                (NavigationWorkMeter?)null,
                ref unbounded);
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
        GuideSampleWorkMeter unused = default;
        return AdvanceOneCore(
            meter,
            null,
            ref unused,
            useGuideMeter: false,
            ref edgeStepRemaining);
    }

    internal NavigationSurfaceEdgeAdvanceStatus AdvanceOne(
        MaintenanceWorkMeter meter,
        ref int edgeStepRemaining)
    {
        GuideSampleWorkMeter unused = default;
        return AdvanceOneCore(
            null,
            meter,
            ref unused,
            useGuideMeter: false,
            ref edgeStepRemaining);
    }

    internal NavigationSurfaceEdgeAdvanceStatus AdvanceOne(
        ref GuideSampleWorkMeter meter,
        ref int edgeStepRemaining) => AdvanceOneCore(
            null,
            null,
            ref meter,
            useGuideMeter: true,
            ref edgeStepRemaining);

    private NavigationSurfaceEdgeAdvanceStatus AdvanceOneCore(
        NavigationWorkMeter? queryMeter,
        MaintenanceWorkMeter? maintenanceMeter,
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter,
        ref int edgeStepRemaining)
    {
        if (_graph == null)
            return NavigationSurfaceEdgeAdvanceStatus.Complete;

        if (!_hasNative && !_nativeComplete)
        {
            if (!_nativeNeedsDebit)
            {
                if (maintenanceMeter != null)
                {
                    NavigationSurfaceEdgeAdvanceStatus nativeStatus =
                        _native.AdvanceOne(maintenanceMeter, ref edgeStepRemaining);
                    if (nativeStatus == NavigationSurfaceEdgeAdvanceStatus.Blocked)
                        return nativeStatus;
                    if (nativeStatus == NavigationSurfaceEdgeAdvanceStatus.Pending)
                        return nativeStatus;
                    if (nativeStatus == NavigationSurfaceEdgeAdvanceStatus.Complete)
                        _nativeComplete = true;
                    else
                    {
                        _nativeEdge = _native.Current;
                        _graph.TryGetNodeAddress(_nativeEdge.Target, out _nativeEndpoint);
                        _hasNative = true;
                        return TrySelectReadyEdge();
                    }
                }
                else if (!_native.MoveNext())
                {
                    _nativeComplete = true;
                }
                else
                {
                    _nativeEdge = _native.Current;
                    _graph.TryGetNodeAddress(_nativeEdge.Target, out _nativeEndpoint);
                    _nativeNeedsDebit = true;
                }
            }
            if (_nativeNeedsDebit)
            {
                if (!TryConsumeCandidate(
                        queryMeter,
                        maintenanceMeter,
                        ref guideMeter,
                        useGuideMeter,
                        ref edgeStepRemaining))
                    return NavigationSurfaceEdgeAdvanceStatus.Blocked;
                _nativeNeedsDebit = false;
                _hasNative = true;
                return TrySelectReadyEdge();
            }
        }

        if (!_hasExplicit && !_explicitComplete)
        {
            if (!_hasPendingExplicitOwner)
            {
                if (!_incident.MoveNext())
                    _explicitComplete = true;
                else
                {
                    _pendingExplicitOwner = _incident.Current;
                    _hasPendingExplicitOwner = true;
                }
            }
            if (_hasPendingExplicitOwner)
            {
                if (!TryConsumeCandidate(
                        queryMeter,
                        maintenanceMeter,
                        ref guideMeter,
                        useGuideMeter,
                        ref edgeStepRemaining))
                    return NavigationSurfaceEdgeAdvanceStatus.Blocked;
                NavigationConnectionOwnerKey owner = _pendingExplicitOwner;
                _pendingExplicitOwner = default;
                _hasPendingExplicitOwner = false;
                FillExplicit(owner);
                return TrySelectReadyEdge();
            }
        }

        if (!_hasSeam && !_seamComplete)
        {
            if (!_hasPendingSeam)
            {
                if (!_seam.MoveNext())
                    _seamComplete = true;
                else
                {
                    _pendingSeam = _seam.Current;
                    _hasPendingSeam = true;
                }
            }
            if (_hasPendingSeam)
            {
                if (!TryConsumeCandidate(
                        queryMeter,
                        maintenanceMeter,
                        ref guideMeter,
                        useGuideMeter,
                        ref edgeStepRemaining))
                    return NavigationSurfaceEdgeAdvanceStatus.Blocked;
                NavigationAutomaticSeamRef seam = _pendingSeam;
                _pendingSeam = default;
                _hasPendingSeam = false;
                FillSeam(seam);
                return TrySelectReadyEdge();
            }
        }

        return TrySelectReadyEdge();
    }

    private NavigationSurfaceEdgeAdvanceStatus TrySelectReadyEdge()
    {
        if ((!_hasNative && !_nativeComplete)
            || (!_hasExplicit && !_explicitComplete)
            || (!_hasSeam && !_seamComplete))
        {
            return NavigationSurfaceEdgeAdvanceStatus.Pending;
        }
        if (!_hasNative && !_hasExplicit && !_hasSeam)
        {
            Current = default;
            return NavigationSurfaceEdgeAdvanceStatus.Complete;
        }
        NavigationGraphEdge selected = default;
        NavigationCellAddress selectedEndpoint = default;
        int selectedKind = -1;
        if (_hasNative)
        {
            selected = _nativeEdge;
            selectedEndpoint = _nativeEndpoint;
            selectedKind = 0;
        }
        if (_hasExplicit
            && (selectedKind < 0
                || Compare(
                    _explicitEdge,
                    _explicitEndpoint,
                    selected,
                    selectedEndpoint) < 0))
        {
            selected = _explicitEdge;
            selectedEndpoint = _explicitEndpoint;
            selectedKind = 1;
        }
        if (_hasSeam
            && (selectedKind < 0
                || Compare(
                    _seamEdge,
                    _seamEndpoint,
                    selected,
                    selectedEndpoint) < 0))
        {
            selected = _seamEdge;
            selectedKind = 2;
        }
        Current = selected;
        _currentOrdinal++;
        if (selectedKind == 0)
            _hasNative = false;
        else if (selectedKind == 1)
            _hasExplicit = false;
        else
            _hasSeam = false;
        return NavigationSurfaceEdgeAdvanceStatus.Edge;
    }

    private void FillExplicit(NavigationConnectionOwnerKey owner)
    {
        if (!_graph!.ExplicitConnections.TryGet(
                owner,
                out NavigationExplicitConnectionRecord record)
            || !record.IsActive)
        {
            return;
        }
        NavigationCellAddress endpoint;
        if (_incoming)
        {
            if (!record.Destination.Equals(_origin))
                return;
            endpoint = record.Source;
        }
        else
        {
            if (!record.Source.Equals(_origin))
                return;
            endpoint = record.Destination;
        }
        if (!_graph.TryGetNodeRef(endpoint, out NavigationNodeRef target))
            return;
        _explicitEndpoint = endpoint;
        _explicitEdge = new NavigationGraphEdge(target, record);
        _hasExplicit = true;
    }

    private void FillSeam(NavigationAutomaticSeamRef seam)
    {
        NavigationCellAddress endpoint = seam.Destination;
        if (!_graph!.TryGetNodeRef(endpoint, out NavigationNodeRef target)
            || (_structural
                ? !_graph.HasEffectiveCell(endpoint)
                : !_graph.TryGetNodeState(target, out NavigationNodeState state)
                    || !state.IsPresent))
        {
            return;
        }
        _seamEndpoint = endpoint;
        _seamEdge = new NavigationGraphEdge(target, seam);
        _hasSeam = true;
    }

    private static bool TryConsumeCandidate(
        NavigationWorkMeter? queryMeter,
        MaintenanceWorkMeter? maintenanceMeter,
        ref GuideSampleWorkMeter guideMeter,
        bool useGuideMeter,
        ref int edgeStepRemaining)
    {
        if (queryMeter == null && maintenanceMeter == null && !useGuideMeter)
            return true;
        if (edgeStepRemaining == 0
            || (queryMeter != null && !queryMeter.TryConsumeEvaluatedEdges(1))
            || (maintenanceMeter != null
                && !maintenanceMeter.TryConsumeSurfaceComponentEdges(1))
            || (useGuideMeter && !guideMeter.TryConsumeCursorLegScans(1)))
        {
            return false;
        }
        edgeStepRemaining--;
        return true;
    }

    private static int Compare(
        in NavigationGraphEdge left,
        NavigationCellAddress leftEndpoint,
        in NavigationGraphEdge right,
        NavigationCellAddress rightEndpoint)
    {
        int comparison = leftEndpoint.CompareTo(rightEndpoint);
        if (comparison != 0)
            return comparison;
        comparison = (int)left.Kind - (int)right.Kind;
        return comparison;
    }
}

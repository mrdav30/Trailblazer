//=======================================================================
// NavigationSurfaceEdgeEnumerator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Merges native, explicit, and automatic-seam edges in durable canonical order.</summary>
internal ref struct NavigationSurfaceEdgeEnumerator
{
    private readonly NavigationWorldGraph? _graph;
    private readonly NavigationCellAddress _origin;
    private NavigationPagedSequence<NavigationConnectionOwnerKey>.Enumerator _incident;
    private NavigationNativeSurfaceEdgeEnumerator _native;
    private NavigationAutomaticSeamIndex.EndpointEnumerator _seam;
    private readonly bool _incoming;
    private readonly bool _includeNative;
    private readonly bool _includeAutomaticSeams;
    private bool _nativeComplete;
    private bool _hasNative;
    private bool _hasExplicit;
    private bool _hasSeam;
    private NavigationGraphEdge _nativeEdge;
    private NavigationGraphEdge _explicitEdge;
    private NavigationGraphEdge _seamEdge;
    private NavigationCellAddress _nativeEndpoint;
    private NavigationCellAddress _explicitEndpoint;
    private NavigationCellAddress _seamEndpoint;

    internal NavigationSurfaceEdgeEnumerator(
        NavigationWorldGraph graph,
        NavigationNodeRef origin,
        bool incoming,
        bool includeNative,
        bool includeAutomaticSeams)
    {
        _graph = graph;
        _incoming = incoming;
        _includeNative = includeNative;
        _includeAutomaticSeams = includeAutomaticSeams;
        _nativeComplete = !includeNative;
        _hasNative = false;
        _hasExplicit = false;
        _hasSeam = false;
        _nativeEdge = default;
        _explicitEdge = default;
        _seamEdge = default;
        _nativeEndpoint = default;
        _explicitEndpoint = default;
        _seamEndpoint = default;
        Current = default;
        if (!graph.TryGetNodeAddress(origin, out _origin)
            || !graph.TryGetNodeState(origin, out NavigationNodeState state)
            || !state.IsPresent)
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
        _native = includeNative ? graph.EnumerateNativeSurfaceEdges(origin) : default;
    }

    internal NavigationGraphEdge Current { get; private set; }

    internal bool MoveNext()
    {
        if (_graph == null)
            return false;
        FillNative();
        FillExplicit();
        FillSeam();
        if (!_hasNative && !_hasExplicit && !_hasSeam)
        {
            Current = default;
            return false;
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
        if (selectedKind == 0)
            _hasNative = false;
        else if (selectedKind == 1)
            _hasExplicit = false;
        else
            _hasSeam = false;
        return true;
    }

    private void FillNative()
    {
        if (_hasNative || _nativeComplete || !_includeNative)
            return;
        if (!_native.MoveNext())
        {
            _nativeComplete = true;
            return;
        }
        _nativeEdge = _native.Current;
        _graph!.TryGetNodeAddress(_nativeEdge.Target, out _nativeEndpoint);
        _hasNative = true;
    }

    private void FillExplicit()
    {
        if (_hasExplicit)
            return;
        while (_incident.MoveNext())
        {
            NavigationConnectionOwnerKey owner = _incident.Current;
            if (!_graph!.ExplicitConnections.TryGet(
                    owner,
                    out NavigationExplicitConnectionRecord record)
                || !record.IsActive)
            {
                continue;
            }
            NavigationCellAddress endpoint;
            if (_incoming)
            {
                if (!record.Destination.Equals(_origin))
                    continue;
                endpoint = record.Source;
            }
            else
            {
                if (!record.Source.Equals(_origin))
                    continue;
                endpoint = record.Destination;
            }
            if (!_graph.TryGetNodeRef(endpoint, out NavigationNodeRef target))
                continue;
            _explicitEndpoint = endpoint;
            _explicitEdge = new NavigationGraphEdge(target, record);
            _hasExplicit = true;
            return;
        }
    }

    private void FillSeam()
    {
        if (_hasSeam || !_includeAutomaticSeams)
            return;
        while (_seam.MoveNext())
        {
            NavigationAutomaticSeamRef seam = _seam.Current;
            NavigationCellAddress endpoint = seam.Destination;
            if (!_graph!.TryGetNodeRef(endpoint, out NavigationNodeRef target)
                || !_graph.TryGetNodeState(target, out NavigationNodeState state)
                || !state.IsPresent)
            {
                continue;
            }
            _seamEndpoint = endpoint;
            _seamEdge = new NavigationGraphEdge(target, seam);
            _hasSeam = true;
            return;
        }
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
        if (comparison != 0)
            return comparison;
        if (left.Kind == NavigationGraphEdgeKind.Native)
            return left.NativeDirectionOrdinal.CompareTo(right.NativeDirectionOrdinal);
        if (left.Kind == NavigationGraphEdgeKind.Seam)
            return 0;
        comparison = string.CompareOrdinal(
            left.ExplicitConnection.Owner.ConnectionId,
            right.ExplicitConnection.Owner.ConnectionId);
        if (comparison != 0)
            return comparison;
        comparison = CompareAnchor(
            left.ExplicitConnection.Definition.EntryAnchor,
            right.ExplicitConnection.Definition.EntryAnchor);
        return comparison != 0
            ? comparison
            : CompareAnchor(
                left.ExplicitConnection.Definition.ExitAnchor,
                right.ExplicitConnection.Definition.ExitAnchor);
    }

    private static int CompareAnchor(
        FixedMathSharp.Vector3d left,
        FixedMathSharp.Vector3d right)
    {
        int comparison = left.X.CompareTo(right.X);
        if (comparison != 0)
            return comparison;
        comparison = left.Y.CompareTo(right.Y);
        return comparison != 0 ? comparison : left.Z.CompareTo(right.Z);
    }
}

//=======================================================================
// NavigationSurfaceEdgeEnumerator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Merges native and compiled explicit surface edges in durable canonical order.</summary>
internal ref struct NavigationSurfaceEdgeEnumerator
{
    private readonly NavigationWorldGraph? _graph;
    private readonly NavigationCellAddress _origin;
    private NavigationPagedSequence<NavigationConnectionOwnerKey>.Enumerator _incident;
    private NavigationNativeSurfaceEdgeEnumerator _native;
    private readonly bool _incoming;
    private readonly bool _includeNative;
    private bool _nativeComplete;
    private bool _hasNative;
    private bool _hasExplicit;
    private NavigationGraphEdge _nativeEdge;
    private NavigationGraphEdge _explicitEdge;
    private NavigationCellAddress _nativeEndpoint;
    private NavigationCellAddress _explicitEndpoint;

    internal NavigationSurfaceEdgeEnumerator(
        NavigationWorldGraph graph,
        NavigationNodeRef origin,
        bool incoming,
        bool includeNative)
    {
        _graph = graph;
        _incoming = incoming;
        _includeNative = includeNative;
        _nativeComplete = !includeNative;
        _hasNative = false;
        _hasExplicit = false;
        _nativeEdge = default;
        _explicitEdge = default;
        _nativeEndpoint = default;
        _explicitEndpoint = default;
        Current = default;
        if (!graph.TryGetNodeAddress(origin, out _origin)
            || !graph.TryGetNodeState(origin, out NavigationNodeState state)
            || !state.IsPresent)
        {
            _graph = null;
            _origin = default;
            _incident = default;
            _native = default;
            return;
        }
        NavigationPagedSequence<NavigationConnectionOwnerKey> endpoints =
            graph.ExplicitConnections.GetEndpointOwnerRow(_origin);
        _incident = endpoints.GetEnumerator();
        _native = includeNative ? graph.EnumerateNativeSurfaceEdges(origin) : default;
    }

    internal NavigationGraphEdge Current { get; private set; }

    internal bool MoveNext()
    {
        if (_graph == null)
            return false;
        FillNative();
        FillExplicit();
        if (!_hasNative && !_hasExplicit)
        {
            Current = default;
            return false;
        }
        if (!_hasExplicit
            || (_hasNative
                && Compare(
                    _nativeEdge,
                    _nativeEndpoint,
                    _explicitEdge,
                    _explicitEndpoint) <= 0))
        {
            Current = _nativeEdge;
            _hasNative = false;
            return true;
        }
        Current = _explicitEdge;
        _hasExplicit = false;
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

    private static int Compare(
        in NavigationGraphEdge left,
        NavigationCellAddress leftEndpoint,
        in NavigationGraphEdge right,
        NavigationCellAddress rightEndpoint)
    {
        int comparison = leftEndpoint.CompareTo(rightEndpoint);
        if (comparison != 0)
            return comparison;
        comparison = left.Kind.CompareTo(right.Kind);
        if (comparison != 0)
            return comparison;
        if (left.Kind == NavigationGraphEdgeKind.Native)
            return left.NativeDirectionOrdinal.CompareTo(right.NativeDirectionOrdinal);
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

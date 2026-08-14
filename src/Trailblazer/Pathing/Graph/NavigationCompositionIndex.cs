//=======================================================================
// NavigationCompositionIndex.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Stores persistent weak components and explicit dependency indexes.</summary>
internal sealed partial class NavigationCompositionIndex
{
    private const long BaseRetainedBytes = 88L;

    private readonly PersistentStringMap<NavigationStructuralNode> _nodes;
    private readonly PersistentStringMap<NavigationIncomingDependencyRecord> _incoming;
    private readonly PersistentStringMap<NavigationStructuralComponent> _components;
    private readonly PersistentStringMap<string> _componentMembership;
    private readonly long _nodeValueBytes;
    private readonly long _incomingValueBytes;
    private readonly long _componentValueBytes;
    private readonly int _nodeValuePages;
    private readonly int _incomingValuePages;
    private readonly int _componentValuePages;

    private NavigationCompositionIndex(
        long version,
        PersistentStringMap<NavigationStructuralNode> nodes,
        PersistentStringMap<NavigationIncomingDependencyRecord> incoming,
        PersistentStringMap<NavigationStructuralComponent> components,
        PersistentStringMap<string> componentMembership,
        long nodeValueBytes,
        long incomingValueBytes,
        long componentValueBytes,
        int nodeValuePages,
        int incomingValuePages,
        int componentValuePages,
        NavigationCompositionUpdateCounters lastUpdate)
    {
        Version = version;
        _nodes = nodes;
        _incoming = incoming;
        _components = components;
        _componentMembership = componentMembership;
        _nodeValueBytes = nodeValueBytes;
        _incomingValueBytes = incomingValueBytes;
        _componentValueBytes = componentValueBytes;
        _nodeValuePages = nodeValuePages;
        _incomingValuePages = incomingValuePages;
        _componentValuePages = componentValuePages;
        LastUpdate = lastUpdate;
    }

    internal static NavigationCompositionIndex Empty { get; } = new(
        0,
        PersistentStringMap<NavigationStructuralNode>.Empty,
        PersistentStringMap<NavigationIncomingDependencyRecord>.Empty,
        PersistentStringMap<NavigationStructuralComponent>.Empty,
        PersistentStringMap<string>.Empty,
        0,
        0,
        0,
        0,
        0,
        0,
        default);

    internal long Version { get; }

    internal int ComponentCount => _components.Count;

    internal NavigationCompositionUpdateCounters LastUpdate { get; }

    internal long RetainedBytes => checked(
        BaseRetainedBytes
        + _nodes.RetainedBytes
        + _incoming.RetainedBytes
        + _components.RetainedBytes
        + _componentMembership.RetainedBytes
        + _nodeValueBytes
        + _incomingValueBytes
        + _componentValueBytes);

    internal long RootAndValueRetainedBytes => RetainedBytes - BaseRetainedBytes;

    internal int PersistentPageCount => checked(
        4
        + _nodes.PersistentNodeCount
        + _incoming.PersistentNodeCount
        + _components.PersistentNodeCount
        + _componentMembership.PersistentNodeCount
        + _nodeValuePages
        + _incomingValuePages
        + _componentValuePages);

    internal int GetComponentId(int mapOrdinal)
    {
        string mapId = _nodes.GetKeyAt(mapOrdinal);
        return GetRootComponent(mapId).Id;
    }

    internal long GetComponentVersion(int mapOrdinal)
    {
        string mapId = _nodes.GetKeyAt(mapOrdinal);
        return GetRootComponent(mapId).Version;
    }

    internal NavigationStructuralComponent GetComponentRecord(string mapId) =>
        GetRootComponent(mapId);

    internal bool TryGetComponentRecord(
        string mapId,
        out NavigationStructuralComponent component) =>
        TryGetRootComponent(mapId, out component);

    internal bool TryGetComponentKey(string mapId, out string componentKey)
    {
        if (!TryGetRootComponent(mapId, out NavigationStructuralComponent component))
        {
            componentKey = string.Empty;
            return false;
        }
        componentKey = component.Key;
        return true;
    }

    internal NavigationCompositionIndex WithComponentVersion(string mapId, long version)
    {
        if (!TryGetRootComponent(mapId, out NavigationStructuralComponent current)
            || current.Version == version)
        {
            return this;
        }
        NavigationStructuralComponent next = NavigationStructuralComponent.CreateFlat(
            current.FlatMembers,
            current.Key,
            version);
        PersistentStringMap<NavigationStructuralComponent> components = _components.Set(
            current.Key,
            next);
        return new NavigationCompositionIndex(
            Version,
            _nodes,
            _incoming,
            components,
            _componentMembership,
            _nodeValueBytes,
            _incomingValueBytes,
            _componentValueBytes,
            _nodeValuePages,
            _incomingValuePages,
            _componentValuePages,
            LastUpdate);
    }

    internal NavigationCompositionIndex WithVersion(long version)
    {
        if (Version == version)
            return this;
        return new NavigationCompositionIndex(
            version,
            _nodes,
            _incoming,
            _components,
            _componentMembership,
            _nodeValueBytes,
            _incomingValueBytes,
            _componentValueBytes,
            _nodeValuePages,
            _incomingValuePages,
            _componentValuePages,
            LastUpdate);
    }

    private NavigationStructuralComponent GetRootComponent(string mapId)
    {
        bool found = TryGetRootComponent(mapId, out NavigationStructuralComponent component);
        SwiftCollections.Diagnostics.SwiftThrowHelper.ThrowIfArgument(
            !found,
            nameof(mapId),
            "Map does not belong to an active structural component.");
        return component;
    }

    private bool TryGetRootComponent(
        string mapId,
        out NavigationStructuralComponent component) =>
        TryGetRootComponent(
            mapId,
            _components,
            _componentMembership,
            out component);

    private static bool TryGetRootComponent(
        string mapId,
        PersistentStringMap<NavigationStructuralComponent> components,
        PersistentStringMap<string> membership,
        out NavigationStructuralComponent component)
    {
        if (!membership.TryGetValue(mapId, out string componentKey))
        {
            component = null!;
            return false;
        }
        return components.TryGetValue(componentKey, out component!);
    }

}

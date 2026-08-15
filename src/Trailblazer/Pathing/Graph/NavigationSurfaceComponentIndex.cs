//=======================================================================
// NavigationSurfaceComponentIndex.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Maps exact stable surface addresses to immutable weak components.</summary>
internal sealed class NavigationSurfaceComponentIndex
{
    private const long BaseRetainedBytes = 64L;

    private readonly PersistentStringMap<PersistentVoxelIndexMap<NavigationSurfaceComponentKey>>
        _membership;
    private readonly PersistentStringMap<PersistentVoxelIndexMap<NavigationSurfaceComponent>>
        _components;
    private readonly long _membershipMapBytes;
    private readonly long _componentMapBytes;
    private readonly long _componentValueBytes;
    private readonly int _membershipMapPages;
    private readonly int _componentMapPages;
    private readonly int _componentValuePages;

    private NavigationSurfaceComponentIndex(
        PersistentStringMap<PersistentVoxelIndexMap<NavigationSurfaceComponentKey>> membership,
        PersistentStringMap<PersistentVoxelIndexMap<NavigationSurfaceComponent>> components,
        long membershipMapBytes,
        long componentMapBytes,
        long componentValueBytes,
        int membershipMapPages,
        int componentMapPages,
        int componentValuePages)
    {
        _membership = membership;
        _components = components;
        _membershipMapBytes = membershipMapBytes;
        _componentMapBytes = componentMapBytes;
        _componentValueBytes = componentValueBytes;
        _membershipMapPages = membershipMapPages;
        _componentMapPages = componentMapPages;
        _componentValuePages = componentValuePages;
    }

    internal static NavigationSurfaceComponentIndex Empty { get; } = new(
        PersistentStringMap<PersistentVoxelIndexMap<NavigationSurfaceComponentKey>>.Empty,
        PersistentStringMap<PersistentVoxelIndexMap<NavigationSurfaceComponent>>.Empty,
        0,
        0,
        0,
        0,
        0,
        0);

    internal long RetainedBytes => checked(
        BaseRetainedBytes
        + _membership.RetainedBytes
        + _components.RetainedBytes
        + _membershipMapBytes
        + _componentMapBytes
        + _componentValueBytes);

    internal int PersistentPageCount => checked(
        2
        + _membership.PersistentNodeCount
        + _components.PersistentNodeCount
        + _membershipMapPages
        + _componentMapPages
        + _componentValuePages);

    internal int PersistentMapNodeCount => checked(
        _membership.PersistentNodeCount
        + _components.PersistentNodeCount
        + _membershipMapPages
        + _componentMapPages);

    internal bool TryGet(
        NavigationCellAddress address,
        out NavigationSurfaceComponent component)
    {
        if (_membership.TryGetValue(
                address.MapId,
                out PersistentVoxelIndexMap<NavigationSurfaceComponentKey> membership)
            && membership.TryGetValue(address.Index, out NavigationSurfaceComponentKey key)
            && TryGet(key, out component))
        {
            return true;
        }
        component = null!;
        return false;
    }

    internal bool TryGet(
        NavigationSurfaceComponentKey key,
        out NavigationSurfaceComponent component)
    {
        if (_components.TryGetValue(
                key.Representative.MapId,
                out PersistentVoxelIndexMap<NavigationSurfaceComponent> components)
            && components.TryGetValue(key.Representative.Index, out component!))
        {
            return true;
        }
        component = null!;
        return false;
    }

    internal NavigationSurfaceComponentIndex AddComponentRecord(
        NavigationSurfaceComponent component,
        out int copiedPersistentNodes)
    {
        copiedPersistentNodes = 0;
        NavigationSurfaceComponentKey key = component.Key;
        bool hadComponentMap = _components.TryGetValue(
            key.Representative.MapId,
            out PersistentVoxelIndexMap<NavigationSurfaceComponent> componentMap);
        componentMap ??= PersistentVoxelIndexMap<NavigationSurfaceComponent>.Empty;
        long componentMapBytes = _componentMapBytes
            - (hadComponentMap ? componentMap.RetainedBytes : 0L);
        int componentMapPages = _componentMapPages
            - (hadComponentMap ? componentMap.PersistentNodeCount : 0);
        componentMap = componentMap.Set(
            key.Representative.Index,
            component,
            out int copied);
        copiedPersistentNodes = checked(copiedPersistentNodes + copied);
        componentMapBytes = checked(componentMapBytes + componentMap.RetainedBytes);
        componentMapPages = checked(componentMapPages + componentMap.PersistentNodeCount);
        PersistentStringMap<PersistentVoxelIndexMap<NavigationSurfaceComponent>> components =
            _components.Set(key.Representative.MapId, componentMap, out copied);
        copiedPersistentNodes = checked(copiedPersistentNodes + copied);

        return new NavigationSurfaceComponentIndex(
            _membership,
            components,
            _membershipMapBytes,
            componentMapBytes,
            checked(_componentValueBytes + component.RetainedBytes),
            _membershipMapPages,
            componentMapPages,
            checked(_componentValuePages + component.PersistentPageCount));
    }

    internal NavigationSurfaceComponentIndex AddMembership(
        NavigationCellAddress address,
        NavigationSurfaceComponentKey key,
        out int copiedPersistentNodes)
    {
        copiedPersistentNodes = 0;
        bool hadMembershipMap = _membership.TryGetValue(
            address.MapId,
            out PersistentVoxelIndexMap<NavigationSurfaceComponentKey> membershipMap);
        membershipMap ??= PersistentVoxelIndexMap<NavigationSurfaceComponentKey>.Empty;
        long membershipMapBytes = _membershipMapBytes
            - (hadMembershipMap ? membershipMap.RetainedBytes : 0L);
        int membershipMapPages = _membershipMapPages
            - (hadMembershipMap ? membershipMap.PersistentNodeCount : 0);
        membershipMap = membershipMap.Set(address.Index, key, out int copied);
        copiedPersistentNodes = checked(copiedPersistentNodes + copied);
        membershipMapBytes = checked(membershipMapBytes + membershipMap.RetainedBytes);
        membershipMapPages = checked(
            membershipMapPages + membershipMap.PersistentNodeCount);
        PersistentStringMap<PersistentVoxelIndexMap<NavigationSurfaceComponentKey>> membership =
            _membership.Set(address.MapId, membershipMap, out copied);
        copiedPersistentNodes = checked(copiedPersistentNodes + copied);
        return new NavigationSurfaceComponentIndex(
            membership,
            _components,
            membershipMapBytes,
            _componentMapBytes,
            _componentValueBytes,
            membershipMapPages,
            _componentMapPages,
            _componentValuePages);
    }

    internal NavigationSurfaceComponentIndex RemoveComponentRecord(
        NavigationSurfaceComponent component,
        out int copiedPersistentNodes)
    {
        copiedPersistentNodes = 0;
        NavigationSurfaceComponentKey key = component.Key;
        if (!_components.TryGetValue(
                key.Representative.MapId,
                out PersistentVoxelIndexMap<NavigationSurfaceComponent> componentMap))
        {
            return this;
        }
        long componentMapBytes = _componentMapBytes - componentMap.RetainedBytes;
        int componentMapPages = _componentMapPages - componentMap.PersistentNodeCount;
        componentMap = componentMap.Remove(
            key.Representative.Index,
            out bool removed,
            out int copied);
        copiedPersistentNodes = checked(copiedPersistentNodes + copied);
        if (!removed)
            return this;
        PersistentStringMap<PersistentVoxelIndexMap<NavigationSurfaceComponent>> components;
        if (componentMap.Count == 0)
        {
            components = _components.Remove(
                key.Representative.MapId,
                out _,
                out copied);
        }
        else
        {
            components = _components.Set(
                key.Representative.MapId,
                componentMap,
                out copied);
            componentMapBytes = checked(componentMapBytes + componentMap.RetainedBytes);
            componentMapPages = checked(
                componentMapPages + componentMap.PersistentNodeCount);
        }
        copiedPersistentNodes = checked(copiedPersistentNodes + copied);
        return new NavigationSurfaceComponentIndex(
            _membership,
            components,
            _membershipMapBytes,
            componentMapBytes,
            checked(_componentValueBytes - component.RetainedBytes),
            _membershipMapPages,
            componentMapPages,
            checked(_componentValuePages - component.PersistentPageCount));
    }

    internal NavigationSurfaceComponentIndex RemoveMembership(
        NavigationCellAddress address,
        NavigationSurfaceComponentKey expectedKey,
        out int copiedPersistentNodes)
    {
        copiedPersistentNodes = 0;
        if (!_membership.TryGetValue(
                address.MapId,
                out PersistentVoxelIndexMap<NavigationSurfaceComponentKey> membershipMap)
            || !membershipMap.TryGetValue(address.Index, out NavigationSurfaceComponentKey key)
            || key != expectedKey)
        {
            return this;
        }
        long membershipMapBytes = _membershipMapBytes - membershipMap.RetainedBytes;
        int membershipMapPages = _membershipMapPages - membershipMap.PersistentNodeCount;
        membershipMap = membershipMap.Remove(address.Index, out _, out int copied);
        copiedPersistentNodes = checked(copiedPersistentNodes + copied);
        PersistentStringMap<PersistentVoxelIndexMap<NavigationSurfaceComponentKey>> membership;
        if (membershipMap.Count == 0)
        {
            membership = _membership.Remove(address.MapId, out _, out copied);
        }
        else
        {
            membership = _membership.Set(address.MapId, membershipMap, out copied);
            membershipMapBytes = checked(membershipMapBytes + membershipMap.RetainedBytes);
            membershipMapPages = checked(
                membershipMapPages + membershipMap.PersistentNodeCount);
        }
        copiedPersistentNodes = checked(copiedPersistentNodes + copied);
        return new NavigationSurfaceComponentIndex(
            membership,
            _components,
            membershipMapBytes,
            _componentMapBytes,
            _componentValueBytes,
            membershipMapPages,
            _componentMapPages,
            _componentValuePages);
    }
}

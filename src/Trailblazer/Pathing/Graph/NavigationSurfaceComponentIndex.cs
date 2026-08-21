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
    private const long BaseRetainedBytes = 112L;

    private readonly NavigationMediumSlots<
        PersistentStringMap<PersistentVoxelIndexMap<NavigationSurfaceComponentKey>>>
        _membership;
    private readonly NavigationMediumSlots<
        PersistentStringMap<PersistentVoxelIndexMap<NavigationSurfaceComponent>>>
        _components;
    private readonly long _membershipMapBytes;
    private readonly long _componentMapBytes;
    private readonly long _componentValueBytes;
    private readonly int _membershipMapPages;
    private readonly int _componentMapPages;
    private readonly int _componentValuePages;

    private NavigationSurfaceComponentIndex(
        NavigationMediumSlots<
            PersistentStringMap<PersistentVoxelIndexMap<NavigationSurfaceComponentKey>>>
            membership,
        NavigationMediumSlots<
            PersistentStringMap<PersistentVoxelIndexMap<NavigationSurfaceComponent>>>
            components,
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
        CreateEmptyRoots<NavigationSurfaceComponentKey>(),
        CreateEmptyRoots<NavigationSurfaceComponent>(),
        0,
        0,
        0,
        0,
        0,
        0);

    internal long RetainedBytes => checked(
        BaseRetainedBytes
        + GetRootBytes(_membership)
        + GetRootBytes(_components)
        + _membershipMapBytes
        + _componentMapBytes
        + _componentValueBytes);

    internal int PersistentPageCount => checked(
        GetRootObjectCount(_membership)
        + GetRootObjectCount(_components)
        + GetRootNodeCount(_membership)
        + GetRootNodeCount(_components)
        + _membershipMapPages
        + _componentMapPages
        + _componentValuePages);

    internal int PersistentMapNodeCount => checked(
        GetRootNodeCount(_membership)
        + GetRootNodeCount(_components)
        + _membershipMapPages
        + _componentMapPages);

    internal bool TryGet(
        NavigationCellAddress address,
        TraversalMedium medium,
        out NavigationSurfaceComponent component)
    {
        if (!NavigationCell.IsKnownMedium(medium))
        {
            component = null!;
            return false;
        }
        _membership.TryGet(medium, out var membership);
        if (membership.TryGetValue(
                address.MapId,
                out PersistentVoxelIndexMap<NavigationSurfaceComponentKey> membershipMap)
            && membershipMap.TryGetValue(address.Index, out NavigationSurfaceComponentKey key)
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
        if (!NavigationCell.IsKnownMedium(key.Medium))
        {
            component = null!;
            return false;
        }
        _components.TryGet(key.Medium, out var components);
        if (components.TryGetValue(
                key.Representative.MapId,
                out PersistentVoxelIndexMap<NavigationSurfaceComponent> componentMap)
            && componentMap.TryGetValue(key.Representative.Index, out component!))
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
        _components.TryGet(key.Medium, out var components);
        bool hadComponentMap = components.TryGetValue(
            key.Representative.MapId,
            out PersistentVoxelIndexMap<NavigationSurfaceComponent> componentMap);
        componentMap ??= PersistentVoxelIndexMap<NavigationSurfaceComponent>.Empty;
        long componentMapBytes = _componentMapBytes
            - (hadComponentMap ? componentMap.RetainedBytes : 0L);
        int componentMapPages = _componentMapPages
            - (hadComponentMap ? componentMap.PersistentNodeCount : 0);
        bool replaced = componentMap.TryGetValue(
            key.Representative.Index,
            out NavigationSurfaceComponent prior);
        componentMap = componentMap.Set(
            key.Representative.Index,
            component,
            out int copied);
        copiedPersistentNodes = checked(copiedPersistentNodes + copied);
        componentMapBytes = checked(componentMapBytes + componentMap.RetainedBytes);
        componentMapPages = checked(componentMapPages + componentMap.PersistentNodeCount);
        components = components.Set(key.Representative.MapId, componentMap, out copied);
        copiedPersistentNodes = checked(copiedPersistentNodes + copied);

        return new NavigationSurfaceComponentIndex(
            _membership,
            _components.Set(key.Medium, components),
            _membershipMapBytes,
            componentMapBytes,
            checked(_componentValueBytes
                - (replaced ? prior.RetainedBytes : 0L)
                + component.RetainedBytes),
            _membershipMapPages,
            componentMapPages,
            checked(_componentValuePages
                - (replaced ? prior.PersistentPageCount : 0)
                + component.PersistentPageCount));
    }

    internal NavigationSurfaceComponentIndex AddMembership(
        NavigationCellAddress address,
        NavigationSurfaceComponentKey key,
        out int copiedPersistentNodes)
    {
        copiedPersistentNodes = 0;
        _membership.TryGet(key.Medium, out var membership);
        bool hadMembershipMap = membership.TryGetValue(
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
        membershipMapPages = checked(membershipMapPages + membershipMap.PersistentNodeCount);
        membership = membership.Set(address.MapId, membershipMap, out copied);
        copiedPersistentNodes = checked(copiedPersistentNodes + copied);
        return new NavigationSurfaceComponentIndex(
            _membership.Set(key.Medium, membership),
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
        _components.TryGet(key.Medium, out var components);
        if (!components.TryGetValue(
                key.Representative.MapId,
                out PersistentVoxelIndexMap<NavigationSurfaceComponent> componentMap)
            || !componentMap.TryGetValue(
                key.Representative.Index,
                out NavigationSurfaceComponent existing))
        {
            return this;
        }
        long componentMapBytes = _componentMapBytes - componentMap.RetainedBytes;
        int componentMapPages = _componentMapPages - componentMap.PersistentNodeCount;
        componentMap = componentMap.Remove(key.Representative.Index, out bool removed, out int copied);
        copiedPersistentNodes = checked(copiedPersistentNodes + copied);
        if (!removed)
            return this;
        if (componentMap.Count == 0)
        {
            components = components.Remove(key.Representative.MapId, out _, out copied);
        }
        else
        {
            components = components.Set(key.Representative.MapId, componentMap, out copied);
            componentMapBytes = checked(componentMapBytes + componentMap.RetainedBytes);
            componentMapPages = checked(componentMapPages + componentMap.PersistentNodeCount);
        }
        copiedPersistentNodes = checked(copiedPersistentNodes + copied);
        return new NavigationSurfaceComponentIndex(
            _membership,
            _components.Set(key.Medium, components),
            _membershipMapBytes,
            componentMapBytes,
            checked(_componentValueBytes - existing.RetainedBytes),
            _membershipMapPages,
            componentMapPages,
            checked(_componentValuePages - existing.PersistentPageCount));
    }

    internal NavigationSurfaceComponentIndex RemoveMembership(
        NavigationCellAddress address,
        NavigationSurfaceComponentKey expectedKey,
        out int copiedPersistentNodes)
    {
        copiedPersistentNodes = 0;
        _membership.TryGet(expectedKey.Medium, out var membership);
        if (!membership.TryGetValue(
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
        if (membershipMap.Count == 0)
        {
            membership = membership.Remove(address.MapId, out _, out copied);
        }
        else
        {
            membership = membership.Set(address.MapId, membershipMap, out copied);
            membershipMapBytes = checked(membershipMapBytes + membershipMap.RetainedBytes);
            membershipMapPages = checked(membershipMapPages + membershipMap.PersistentNodeCount);
        }
        copiedPersistentNodes = checked(copiedPersistentNodes + copied);
        return new NavigationSurfaceComponentIndex(
            _membership.Set(expectedKey.Medium, membership),
            _components,
            membershipMapBytes,
            _componentMapBytes,
            _componentValueBytes,
            membershipMapPages,
            _componentMapPages,
            _componentValuePages);
    }

    private static NavigationMediumSlots<PersistentStringMap<PersistentVoxelIndexMap<T>>>
        CreateEmptyRoots<T>()
    {
        var roots = default(
            NavigationMediumSlots<PersistentStringMap<PersistentVoxelIndexMap<T>>>);
        for (TraversalMedium medium = TraversalMedium.Solid;
             medium <= TraversalMedium.Liquid;
             medium++)
        {
            roots = roots.Set(
                medium,
                PersistentStringMap<PersistentVoxelIndexMap<T>>.Empty);
        }
        return roots;
    }

    private static long GetRootBytes<T>(
        NavigationMediumSlots<PersistentStringMap<PersistentVoxelIndexMap<T>>> roots)
    {
        GetRoots(roots, out var solid, out var gas, out var liquid);
        return checked(
            solid.RetainedBytes
            + (ReferenceEquals(gas, solid) ? 0L : gas.RetainedBytes)
            + (ReferenceEquals(liquid, solid) || ReferenceEquals(liquid, gas)
                ? 0L
                : liquid.RetainedBytes));
    }

    private static int GetRootNodeCount<T>(
        NavigationMediumSlots<PersistentStringMap<PersistentVoxelIndexMap<T>>> roots)
    {
        int total = 0;
        for (TraversalMedium medium = TraversalMedium.Solid;
             medium <= TraversalMedium.Liquid;
             medium++)
        {
            roots.TryGet(medium, out var root);
            total = checked(total + root.PersistentNodeCount);
        }
        return total;
    }

    private static int GetRootObjectCount<T>(
        NavigationMediumSlots<PersistentStringMap<PersistentVoxelIndexMap<T>>> roots)
    {
        GetRoots(roots, out var solid, out var gas, out var liquid);
        return 1
            + (ReferenceEquals(gas, solid) ? 0 : 1)
            + (ReferenceEquals(liquid, solid) || ReferenceEquals(liquid, gas) ? 0 : 1);
    }

    private static void GetRoots<T>(
        NavigationMediumSlots<PersistentStringMap<PersistentVoxelIndexMap<T>>> roots,
        out PersistentStringMap<PersistentVoxelIndexMap<T>> solid,
        out PersistentStringMap<PersistentVoxelIndexMap<T>> gas,
        out PersistentStringMap<PersistentVoxelIndexMap<T>> liquid)
    {
        roots.TryGet(TraversalMedium.Solid, out solid!);
        roots.TryGet(TraversalMedium.Gas, out gas!);
        roots.TryGet(TraversalMedium.Liquid, out liquid!);
    }
}

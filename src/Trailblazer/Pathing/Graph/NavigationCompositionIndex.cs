//=======================================================================
// NavigationCompositionIndex.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>Stores persistent weak components and forward/reverse explicit dependency indexes.</summary>
internal sealed partial class NavigationCompositionIndex
{
    private const long BaseRetainedBytes = 96L;

    private readonly PersistentStringMap<NavigationStructuralNode> _nodes;
    private readonly PersistentStringMap<NavigationIncomingDependencyRecord> _incoming;
    private readonly PersistentStringMap<NavigationStructuralComponent> _components;
    private readonly PersistentStringMap<string> _componentMembership;
    private readonly PersistentStringMap<string> _componentAliases;
    private readonly long _nodeValueBytes;
    private readonly long _incomingValueBytes;
    private readonly long _componentValueBytes;

    private NavigationCompositionIndex(
        long version,
        PersistentStringMap<NavigationStructuralNode> nodes,
        PersistentStringMap<NavigationIncomingDependencyRecord> incoming,
        PersistentStringMap<NavigationStructuralComponent> components,
        PersistentStringMap<string> componentMembership,
        PersistentStringMap<string> componentAliases,
        long nodeValueBytes,
        long incomingValueBytes,
        long componentValueBytes,
        NavigationCompositionUpdateCounters lastUpdate)
    {
        Version = version;
        _nodes = nodes;
        _incoming = incoming;
        _components = components;
        _componentMembership = componentMembership;
        _componentAliases = componentAliases;
        _nodeValueBytes = nodeValueBytes;
        _incomingValueBytes = incomingValueBytes;
        _componentValueBytes = componentValueBytes;
        LastUpdate = lastUpdate;
    }

    internal static NavigationCompositionIndex Empty { get; } = new(
        0,
        PersistentStringMap<NavigationStructuralNode>.Empty,
        PersistentStringMap<NavigationIncomingDependencyRecord>.Empty,
        PersistentStringMap<NavigationStructuralComponent>.Empty,
        PersistentStringMap<string>.Empty,
        PersistentStringMap<string>.Empty,
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
        + _componentAliases.RetainedBytes
        + _nodeValueBytes
        + _incomingValueBytes
        + _componentValueBytes);

    internal int PersistentPageCount => 5
        + _nodes.PersistentNodeCount
        + _incoming.PersistentNodeCount
        + _components.PersistentNodeCount
        + _componentMembership.PersistentNodeCount
        + _componentAliases.PersistentNodeCount;

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

    internal int GetIncidentEdgeCount(int mapOrdinal)
    {
        string mapId = _nodes.GetKeyAt(mapOrdinal);
        _nodes.TryGetValue(mapId, out NavigationStructuralNode node);
        int count = 0;
        ReadOnlySpan<NavigationStructuralLink> outgoing = node.Links;
        for (int i = 0; i < outgoing.Length; i++)
        {
            if (_nodes.ContainsKey(outgoing[i].DestinationMapId))
                count = checked(count + outgoing[i].Count);
        }

        if (!_incoming.TryGetValue(mapId, out NavigationIncomingDependencyRecord incoming))
            return count;
        ReadOnlySpan<NavigationIncomingDependency> sources = incoming.Sources;
        for (int i = 0; i < sources.Length; i++)
        {
            if (!string.Equals(sources[i].SourceMapId, mapId, StringComparison.Ordinal)
                && _nodes.ContainsKey(sources[i].SourceMapId))
            {
                count = checked(count + sources[i].Count);
            }
        }
        return count;
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

    internal void AddComponentMembers(string mapId, SwiftHashSet<string> destination)
    {
        if (!TryGetRootComponent(mapId, out NavigationStructuralComponent component))
            return;
        var members = new string[component.MemberCount];
        int offset = 0;
        component.CopyMembersTo(members, ref offset);
        for (int i = 0; i < members.Length; i++)
            destination.Add(members[i]);
    }

    internal static NavigationCompositionIndex Build(
        NavigationMapInstance[] instances,
        long version) =>
        Build(NavigationInstanceDirectory.Create(instances), version);

    internal static NavigationCompositionIndex Build(
        NavigationInstanceDirectory directory,
        long version)
    {
        PersistentStringMap<NavigationStructuralNode> nodes =
            PersistentStringMap<NavigationStructuralNode>.Empty;
        PersistentStringMap<NavigationIncomingDependencyRecord> incoming =
            PersistentStringMap<NavigationIncomingDependencyRecord>.Empty;
        long nodeBytes = 0;
        long incomingBytes = 0;
        int edgeCount = 0;
        for (int i = 0; i < directory.Count; i++)
        {
            NavigationMapInstance instance = directory.Get(i);
            NavigationStructuralNode node = NavigationStructuralNode.Capture(instance);
            nodes = nodes.Set(instance.MapId, node);
            nodeBytes = checked(nodeBytes + node.RetainedBytes);
            ReadOnlySpan<NavigationStructuralLink> links = node.Links;
            for (int link = 0; link < links.Length; link++)
            {
                edgeCount = checked(edgeCount + links[link].Count);
                incoming = SetIncoming(
                    incoming,
                    links[link].DestinationMapId,
                    instance.MapId,
                    links[link].Count,
                    ref incomingBytes,
                    out _);
            }
        }

        PersistentStringMap<NavigationStructuralComponent> components =
            PersistentStringMap<NavigationStructuralComponent>.Empty;
        PersistentStringMap<string> membership = PersistentStringMap<string>.Empty;
        long componentBytes = 0;
        int visitedMaps = 0;
        int visitedEdges = 0;
        string[] allMaps = CopyNodeKeys(nodes);
        BuildFlatComponents(
            nodes,
            incoming,
            allMaps,
            version,
            ref components,
            ref membership,
            ref componentBytes,
            ref visitedMaps,
            ref visitedEdges,
            out int copiedComponents,
            out int copiedMemberships);

        return new NavigationCompositionIndex(
            version,
            nodes,
            incoming,
            components,
            membership,
            PersistentStringMap<string>.Empty,
            nodeBytes,
            incomingBytes,
            componentBytes,
            new NavigationCompositionUpdateCounters(
                directory.Count,
                visitedMaps,
                visitedEdges,
                directory.Count,
                edgeCount,
                copiedComponents,
                copiedMemberships,
                0));
    }

    /// <summary>
    /// Updates only changed source records and incident components. Connectivity deletion scans the
    /// complete old weak component; additions use persistent component unions without flattening it.
    /// </summary>
    internal NavigationCompositionIndex Update(
        NavigationInstanceDirectory directory,
        ReadOnlySpan<string> changedMapIds,
        long version)
    {
        string[] changes = NormalizeChanges(changedMapIds);
        if (changes.Length == 0)
            return this;

        PersistentStringMap<NavigationStructuralNode> nodes = _nodes;
        PersistentStringMap<NavigationIncomingDependencyRecord> incoming = _incoming;
        PersistentStringMap<NavigationStructuralComponent> components = _components;
        PersistentStringMap<string> membership = _componentMembership;
        PersistentStringMap<string> aliases = _componentAliases;
        long nodeBytes = _nodeValueBytes;
        long incomingBytes = _incomingValueBytes;
        long componentBytes = _componentValueBytes;
        int copiedNodes = 0;
        int copiedReverse = 0;
        int copiedComponents = 0;
        int copiedMemberships = 0;
        int visitedMaps = 0;
        int visitedEdges = 0;
        bool hasConnectivityRemoval = false;
        var splitRoots = new SwiftHashSet<string>(changes.Length, StringComparer.Ordinal);

        for (int i = 0; i < changes.Length; i++)
        {
            string mapId = changes[i];
            bool hadOld = _nodes.TryGetValue(mapId, out NavigationStructuralNode oldNode);
            bool hasNext = directory.TryGet(mapId, out NavigationMapInstance nextInstance);
            NavigationStructuralNode? nextNode = hasNext
                ? NavigationStructuralNode.Capture(nextInstance)
                : null;

            bool removesConnectivity = hadOld
                && (!hasNext || HasRemovedActiveLink(oldNode, nextNode!));
            if (removesConnectivity)
            {
                hasConnectivityRemoval = true;
                if (TryGetRootComponent(mapId, out NavigationStructuralComponent oldRoot))
                    splitRoots.Add(oldRoot.Key);
            }

            if (hadOld && hasNext && oldNode.HasSameLinks(nextNode!))
                continue;

            UpdateIncomingForSource(
                mapId,
                hadOld ? oldNode : null,
                nextNode,
                ref incoming,
                ref incomingBytes,
                ref copiedReverse);
            if (hadOld)
            {
                nodes = nodes.Remove(mapId, out _);
                nodeBytes = checked(nodeBytes - oldNode.RetainedBytes);
                copiedNodes++;
            }
            if (nextNode != null)
            {
                nodes = nodes.Set(mapId, nextNode);
                nodeBytes = checked(nodeBytes + nextNode.RetainedBytes);
                copiedNodes++;
            }
        }

        int oldComponentCount = components.Count;
        if (hasConnectivityRemoval)
        {
            SplitOldComponents(
                splitRoots,
                nodes,
                incoming,
                version,
                ref components,
                ref membership,
                ref aliases,
                ref componentBytes,
                ref copiedComponents,
                ref copiedMemberships,
                ref visitedMaps,
                ref visitedEdges);
        }

        for (int i = 0; i < changes.Length; i++)
        {
            string mapId = changes[i];
            if (!nodes.ContainsKey(mapId))
            {
                membership = membership.Remove(mapId, out bool removedMembership);
                if (removedMembership)
                    copiedMemberships++;
                continue;
            }

            EnsureSingleton(
                mapId,
                version,
                ref components,
                ref membership,
                ref aliases,
                ref componentBytes,
                ref copiedComponents,
                ref copiedMemberships);
            TouchComponent(
                mapId,
                version,
                ref components,
                membership,
                aliases,
                ref componentBytes,
                ref copiedComponents);
        }

        for (int i = 0; i < changes.Length; i++)
        {
            if (!nodes.ContainsKey(changes[i]))
                continue;
            UnionIncidentComponents(
                changes[i],
                version,
                nodes,
                incoming,
                ref components,
                membership,
                ref aliases,
                ref componentBytes,
                ref copiedComponents,
                ref visitedMaps,
                ref visitedEdges);
        }

        return new NavigationCompositionIndex(
            version,
            nodes,
            incoming,
            components,
            membership,
            aliases,
            nodeBytes,
            incomingBytes,
            componentBytes,
            new NavigationCompositionUpdateCounters(
                changes.Length,
                visitedMaps,
                visitedEdges,
                copiedNodes,
                copiedReverse,
                copiedComponents,
                copiedMemberships,
                Math.Max(0, oldComponentCount - splitRoots.Count)));
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
            _componentAliases,
            out component);

    private static bool TryGetRootComponent(
        string mapId,
        PersistentStringMap<NavigationStructuralComponent> components,
        PersistentStringMap<string> membership,
        PersistentStringMap<string> aliases,
        out NavigationStructuralComponent component)
    {
        if (!membership.TryGetValue(mapId, out string componentKey))
        {
            component = null!;
            return false;
        }

        while (aliases.TryGetValue(componentKey, out string parentKey))
            componentKey = parentKey;
        return components.TryGetValue(componentKey, out component!);
    }

    private static void SplitOldComponents(
        SwiftHashSet<string> oldRootKeys,
        PersistentStringMap<NavigationStructuralNode> nodes,
        PersistentStringMap<NavigationIncomingDependencyRecord> incoming,
        long version,
        ref PersistentStringMap<NavigationStructuralComponent> components,
        ref PersistentStringMap<string> membership,
        ref PersistentStringMap<string> aliases,
        ref long componentBytes,
        ref int copiedComponents,
        ref int copiedMemberships,
        ref int visitedMaps,
        ref int visitedEdges)
    {
        var treeKeys = new SwiftHashSet<string>(StringComparer.Ordinal);
        int maximumMemberCount = 0;
        foreach (string rootKey in oldRootKeys)
        {
            if (components.TryGetValue(rootKey, out NavigationStructuralComponent root))
                maximumMemberCount = checked(maximumMemberCount + root.MemberCount);
        }
        var domain = new string[maximumMemberCount];
        int domainCount = 0;
        foreach (string rootKey in oldRootKeys)
        {
            if (!components.TryGetValue(rootKey, out NavigationStructuralComponent root))
                continue;
            var members = new string[root.MemberCount];
            int offset = 0;
            root.CopyMembersTo(members, ref offset);
            for (int member = 0; member < members.Length; member++)
            {
                if (nodes.ContainsKey(members[member]))
                    domain[domainCount++] = members[member];
                membership = membership.Remove(members[member], out bool removed);
                if (removed)
                    copiedMemberships++;
            }

            treeKeys.Clear();
            root.CollectTreeKeys(treeKeys);
            foreach (string treeKey in treeKeys)
                aliases = aliases.Remove(treeKey, out _);
            components = components.Remove(rootKey, out _);
            componentBytes = checked(componentBytes - root.RetainedBytes);
            copiedComponents++;
        }

        if (domainCount != domain.Length)
            Array.Resize(ref domain, domainCount);
        BuildFlatComponents(
            nodes,
            incoming,
            domain,
            version,
            ref components,
            ref membership,
            ref componentBytes,
            ref visitedMaps,
            ref visitedEdges,
            out int builtComponents,
            out int builtMemberships);
        copiedComponents += builtComponents;
        copiedMemberships += builtMemberships;
    }

    private static void BuildFlatComponents(
        PersistentStringMap<NavigationStructuralNode> nodes,
        PersistentStringMap<NavigationIncomingDependencyRecord> incoming,
        string[] domain,
        long version,
        ref PersistentStringMap<NavigationStructuralComponent> components,
        ref PersistentStringMap<string> membership,
        ref long componentBytes,
        ref int visitedMaps,
        ref int visitedEdges,
        out int copiedComponents,
        out int copiedMemberships)
    {
        copiedComponents = 0;
        copiedMemberships = 0;
        if (domain.Length == 0)
            return;

        var domainSet = new SwiftHashSet<string>(domain.Length, StringComparer.Ordinal);
        var visited = new SwiftHashSet<string>(domain.Length, StringComparer.Ordinal);
        for (int i = 0; i < domain.Length; i++)
            domainSet.Add(domain[i]);
        var queue = new string[domain.Length];
        var members = new string[domain.Length];

        for (int start = 0; start < domain.Length; start++)
        {
            if (!visited.Add(domain[start]))
                continue;
            int read = 0;
            int write = 1;
            int memberCount = 0;
            queue[0] = domain[start];
            while (read < write)
            {
                string mapId = queue[read++];
                members[memberCount++] = mapId;
                visitedMaps++;
                nodes.TryGetValue(mapId, out NavigationStructuralNode node);
                ReadOnlySpan<NavigationStructuralLink> links = node.Links;
                for (int link = 0; link < links.Length; link++)
                {
                    visitedEdges = checked(visitedEdges + links[link].Count);
                    string neighbor = links[link].DestinationMapId;
                    if (domainSet.Contains(neighbor) && visited.Add(neighbor))
                        queue[write++] = neighbor;
                }

                if (!incoming.TryGetValue(mapId, out NavigationIncomingDependencyRecord reverse))
                    continue;
                ReadOnlySpan<NavigationIncomingDependency> sources = reverse.Sources;
                for (int source = 0; source < sources.Length; source++)
                {
                    string neighbor = sources[source].SourceMapId;
                    if (domainSet.Contains(neighbor) && visited.Add(neighbor))
                        queue[write++] = neighbor;
                }
            }

            var componentMembers = new string[memberCount];
            Array.Copy(members, componentMembers, memberCount);
            NavigationStructuralComponent component =
                NavigationStructuralComponent.CreateFlat(componentMembers, version);
            components = components.Set(component.Key, component);
            componentBytes = checked(componentBytes + component.RetainedBytes);
            copiedComponents++;
            for (int member = 0; member < componentMembers.Length; member++)
            {
                membership = membership.Set(componentMembers[member], component.Key);
                copiedMemberships++;
            }
        }
    }

    private static void EnsureSingleton(
        string mapId,
        long version,
        ref PersistentStringMap<NavigationStructuralComponent> components,
        ref PersistentStringMap<string> membership,
        ref PersistentStringMap<string> aliases,
        ref long componentBytes,
        ref int copiedComponents,
        ref int copiedMemberships)
    {
        if (membership.ContainsKey(mapId))
            return;
        aliases = aliases.Remove(mapId, out _);
        NavigationStructuralComponent component =
            NavigationStructuralComponent.CreateFlat(new[] { mapId }, version);
        components = components.Set(mapId, component);
        membership = membership.Set(mapId, mapId);
        componentBytes = checked(componentBytes + component.RetainedBytes);
        copiedComponents++;
        copiedMemberships++;
    }

    private static void TouchComponent(
        string mapId,
        long version,
        ref PersistentStringMap<NavigationStructuralComponent> components,
        PersistentStringMap<string> membership,
        PersistentStringMap<string> aliases,
        ref long componentBytes,
        ref int copiedComponents)
    {
        if (!TryGetRootComponent(mapId, components, membership, aliases, out NavigationStructuralComponent root)
            || root.Version == version)
        {
            return;
        }
        NavigationStructuralComponent touched = root.WithVersion(version);
        components = components.Set(root.Key, touched);
        componentBytes = checked(componentBytes - root.RetainedBytes + touched.RetainedBytes);
        copiedComponents++;
    }

    private static void UnionIncidentComponents(
        string mapId,
        long version,
        PersistentStringMap<NavigationStructuralNode> nodes,
        PersistentStringMap<NavigationIncomingDependencyRecord> incoming,
        ref PersistentStringMap<NavigationStructuralComponent> components,
        PersistentStringMap<string> membership,
        ref PersistentStringMap<string> aliases,
        ref long componentBytes,
        ref int copiedComponents,
        ref int visitedMaps,
        ref int visitedEdges)
    {
        visitedMaps++;
        nodes.TryGetValue(mapId, out NavigationStructuralNode node);
        ReadOnlySpan<NavigationStructuralLink> links = node.Links;
        for (int i = 0; i < links.Length; i++)
        {
            visitedEdges = checked(visitedEdges + links[i].Count);
            if (nodes.ContainsKey(links[i].DestinationMapId))
            {
                Union(
                    mapId,
                    links[i].DestinationMapId,
                    version,
                    ref components,
                    membership,
                    ref aliases,
                    ref componentBytes,
                    ref copiedComponents);
            }
        }

        if (!incoming.TryGetValue(mapId, out NavigationIncomingDependencyRecord reverse))
            return;
        ReadOnlySpan<NavigationIncomingDependency> sources = reverse.Sources;
        for (int i = 0; i < sources.Length; i++)
        {
            visitedEdges = checked(visitedEdges + sources[i].Count);
            if (nodes.ContainsKey(sources[i].SourceMapId))
            {
                Union(
                    mapId,
                    sources[i].SourceMapId,
                    version,
                    ref components,
                    membership,
                    ref aliases,
                    ref componentBytes,
                    ref copiedComponents);
            }
        }
    }

    private static void Union(
        string firstMapId,
        string secondMapId,
        long version,
        ref PersistentStringMap<NavigationStructuralComponent> components,
        PersistentStringMap<string> membership,
        ref PersistentStringMap<string> aliases,
        ref long componentBytes,
        ref int copiedComponents)
    {
        if (!TryGetRootComponent(firstMapId, components, membership, aliases, out NavigationStructuralComponent first)
            || !TryGetRootComponent(secondMapId, components, membership, aliases, out NavigationStructuralComponent second)
            || string.Equals(first.Key, second.Key, StringComparison.Ordinal))
        {
            return;
        }

        NavigationStructuralComponent merged = NavigationStructuralComponent.Merge(first, second, version);
        NavigationStructuralComponent loser = string.Equals(merged.Key, first.Key, StringComparison.Ordinal)
            ? second
            : first;
        components = components.Remove(loser.Key, out _);
        components = components.Set(merged.Key, merged);
        aliases = aliases.Set(loser.Key, merged.Key);
        componentBytes = checked(
            componentBytes
            - first.RetainedBytes
            - second.RetainedBytes
            + merged.RetainedBytes);
        copiedComponents++;
    }

    private bool HasRemovedActiveLink(
        NavigationStructuralNode oldNode,
        NavigationStructuralNode nextNode)
    {
        ReadOnlySpan<NavigationStructuralLink> links = oldNode.Links;
        for (int i = 0; i < links.Length; i++)
        {
            if (_nodes.ContainsKey(links[i].DestinationMapId)
                && nextNode.GetLinkCount(links[i].DestinationMapId) == 0)
            {
                return true;
            }
        }
        return false;
    }

    private static void UpdateIncomingForSource(
        string sourceMapId,
        NavigationStructuralNode? oldNode,
        NavigationStructuralNode? nextNode,
        ref PersistentStringMap<NavigationIncomingDependencyRecord> incoming,
        ref long incomingBytes,
        ref int copiedReverse)
    {
        ReadOnlySpan<NavigationStructuralLink> oldLinks = oldNode == null
            ? ReadOnlySpan<NavigationStructuralLink>.Empty
            : oldNode.Links;
        ReadOnlySpan<NavigationStructuralLink> nextLinks = nextNode == null
            ? ReadOnlySpan<NavigationStructuralLink>.Empty
            : nextNode.Links;
        int oldIndex = 0;
        int nextIndex = 0;
        while (oldIndex < oldLinks.Length || nextIndex < nextLinks.Length)
        {
            int comparison = oldIndex >= oldLinks.Length
                ? 1
                : nextIndex >= nextLinks.Length
                    ? -1
                    : string.CompareOrdinal(
                        oldLinks[oldIndex].DestinationMapId,
                        nextLinks[nextIndex].DestinationMapId);
            string destination;
            int count;
            if (comparison < 0)
            {
                destination = oldLinks[oldIndex++].DestinationMapId;
                count = 0;
            }
            else
            {
                destination = nextLinks[nextIndex].DestinationMapId;
                count = nextLinks[nextIndex++].Count;
                if (comparison == 0)
                {
                    if (oldLinks[oldIndex].Count == count)
                    {
                        oldIndex++;
                        continue;
                    }
                    oldIndex++;
                }
            }
            incoming = SetIncoming(
                incoming,
                destination,
                sourceMapId,
                count,
                ref incomingBytes,
                out bool copied);
            if (copied)
                copiedReverse++;
        }
    }

    private static PersistentStringMap<NavigationIncomingDependencyRecord> SetIncoming(
        PersistentStringMap<NavigationIncomingDependencyRecord> incoming,
        string destinationMapId,
        string sourceMapId,
        int count,
        ref long incomingBytes,
        out bool copied)
        => SetIncoming(
            incoming,
            destinationMapId,
            sourceMapId,
            count,
            ref incomingBytes,
            out copied,
            out _);

    private static PersistentStringMap<NavigationIncomingDependencyRecord> SetIncoming(
        PersistentStringMap<NavigationIncomingDependencyRecord> incoming,
        string destinationMapId,
        string sourceMapId,
        int count,
        ref long incomingBytes,
        out bool copied,
        out int copiedNodeCount)
    {
        incoming.TryGetValue(destinationMapId, out NavigationIncomingDependencyRecord current);
        current ??= NavigationIncomingDependencyRecord.Empty;
        NavigationIncomingDependencyRecord next = current.With(sourceMapId, count);
        if (ReferenceEquals(current, next))
        {
            copied = false;
            copiedNodeCount = 0;
            return incoming;
        }

        if (!ReferenceEquals(current, NavigationIncomingDependencyRecord.Empty))
            incomingBytes = checked(incomingBytes - current.RetainedBytes);
        if (next.IsEmpty)
        {
            copied = true;
            return incoming.Remove(destinationMapId, out _, out copiedNodeCount);
        }
        incomingBytes = checked(incomingBytes + next.RetainedBytes);
        copied = true;
        return incoming.Set(destinationMapId, next, out copiedNodeCount);
    }

    private static string[] CopyNodeKeys(PersistentStringMap<NavigationStructuralNode> nodes)
    {
        var keys = new string[nodes.Count];
        for (int i = 0; i < keys.Length; i++)
            keys[i] = nodes.GetKeyAt(i);
        return keys;
    }

    private static string[] NormalizeChanges(ReadOnlySpan<string> changes)
    {
        if (changes.IsEmpty)
            return Array.Empty<string>();
        var sorted = new string[changes.Length];
        changes.CopyTo(sorted);
        Array.Sort(sorted, StringComparer.Ordinal);
        int count = 1;
        for (int i = 1; i < sorted.Length; i++)
        {
            if (!string.Equals(sorted[count - 1], sorted[i], StringComparison.Ordinal))
                sorted[count++] = sorted[i];
        }
        if (count != sorted.Length)
            Array.Resize(ref sorted, count);
        return sorted;
    }
}

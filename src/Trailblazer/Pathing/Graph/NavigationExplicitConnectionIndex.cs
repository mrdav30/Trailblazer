//=======================================================================
// NavigationExplicitConnectionIndex.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Spatial;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>Stores persistent explicit owners and their exact address incidence.</summary>
internal sealed class NavigationExplicitConnectionIndex
{
    internal static readonly NavigationExplicitConnectionIndex Empty = new(
        PersistentStringMap<PersistentStringMap<NavigationExplicitConnectionRecord>>.Empty,
        PersistentStringMap<PersistentVoxelIndexMap<NavigationConnectionOwnerKey[]>>.Empty,
        ownerMapBytes: 0,
        incidentMapBytes: 0,
        recordBytes: 0,
        incidentArrayBytes: 0,
        ownerMapPages: 0,
        incidentMapPages: 0);

    private readonly PersistentStringMap<PersistentStringMap<NavigationExplicitConnectionRecord>> _owners;
    private readonly PersistentStringMap<PersistentVoxelIndexMap<NavigationConnectionOwnerKey[]>> _incident;
    private readonly long _ownerMapBytes;
    private readonly long _incidentMapBytes;
    private readonly long _recordBytes;
    private readonly long _incidentArrayBytes;
    private readonly int _ownerMapPages;
    private readonly int _incidentMapPages;

    private NavigationExplicitConnectionIndex(
        PersistentStringMap<PersistentStringMap<NavigationExplicitConnectionRecord>> owners,
        PersistentStringMap<PersistentVoxelIndexMap<NavigationConnectionOwnerKey[]>> incident,
        long ownerMapBytes,
        long incidentMapBytes,
        long recordBytes,
        long incidentArrayBytes,
        int ownerMapPages,
        int incidentMapPages)
    {
        _owners = owners;
        _incident = incident;
        _ownerMapBytes = ownerMapBytes;
        _incidentMapBytes = incidentMapBytes;
        _recordBytes = recordBytes;
        _incidentArrayBytes = incidentArrayBytes;
        _ownerMapPages = ownerMapPages;
        _incidentMapPages = incidentMapPages;
    }

    internal long RetainedBytes => checked(
        80L
        + _owners.RetainedBytes
        + _incident.RetainedBytes
        + _ownerMapBytes
        + _incidentMapBytes
        + _recordBytes
        + _incidentArrayBytes);

    internal int PersistentPageCount => checked(
        2
        + _owners.PersistentNodeCount
        + _incident.PersistentNodeCount
        + _ownerMapPages
        + _incidentMapPages);

    internal bool TryGet(
        NavigationConnectionOwnerKey owner,
        out NavigationExplicitConnectionRecord record)
    {
        if (_owners.TryGetValue(
                owner.MapId,
                out PersistentStringMap<NavigationExplicitConnectionRecord> map)
            && map.TryGetValue(owner.ConnectionId, out record!))
        {
            return true;
        }
        record = null!;
        return false;
    }

    internal int GetSourceOwnerCount(string mapId) =>
        _owners.TryGetValue(mapId, out PersistentStringMap<NavigationExplicitConnectionRecord> map)
            ? map.Count
            : 0;

    internal NavigationExplicitConnectionRecord GetSourceOwnerAt(string mapId, int ordinal)
    {
        _owners.TryGetValue(mapId, out PersistentStringMap<NavigationExplicitConnectionRecord> map);
        return map.GetValueAt(ordinal);
    }

    internal ReadOnlySpan<NavigationConnectionOwnerKey> GetIncidentOwners(
        NavigationCellAddress address)
    {
        if (_incident.TryGetValue(
                address.MapId,
                out PersistentVoxelIndexMap<NavigationConnectionOwnerKey[]> map)
            && map.TryGetValue(address.Index, out NavigationConnectionOwnerKey[] owners))
        {
            return owners;
        }
        return ReadOnlySpan<NavigationConnectionOwnerKey>.Empty;
    }

    internal int GetIncidentAddressCount(string mapId) =>
        _incident.TryGetValue(
            mapId,
            out PersistentVoxelIndexMap<NavigationConnectionOwnerKey[]> addresses)
                ? addresses.Count
                : 0;

    internal NavigationCellAddress GetIncidentAddressAt(string mapId, int ordinal)
    {
        _incident.TryGetValue(
            mapId,
            out PersistentVoxelIndexMap<NavigationConnectionOwnerKey[]> addresses);
        return new NavigationCellAddress(mapId, addresses.GetKeyAt(ordinal));
    }

    internal NavigationExplicitConnectionIndex SetOwner(
        NavigationExplicitConnectionRecord record,
        out int copiedNodes)
    {
        copiedNodes = 0;
        PersistentStringMap<NavigationExplicitConnectionRecord> ownerMap =
            _owners.TryGetValue(
                record.Owner.MapId,
                out PersistentStringMap<NavigationExplicitConnectionRecord> existing)
                ? existing
                : PersistentStringMap<NavigationExplicitConnectionRecord>.Empty;
        long ownerMapBytes = _ownerMapBytes - (existing?.RetainedBytes ?? 0);
        int ownerMapPages = _ownerMapPages - (existing?.PersistentNodeCount ?? 0);
        ownerMap.TryGetValue(record.Owner.ConnectionId, out NavigationExplicitConnectionRecord prior);
        ownerMap = ownerMap.Set(record.Owner.ConnectionId, record, out int copied);
        copiedNodes = checked(copiedNodes + copied);
        ownerMapBytes = checked(ownerMapBytes + ownerMap.RetainedBytes);
        ownerMapPages = checked(ownerMapPages + ownerMap.PersistentNodeCount);
        PersistentStringMap<PersistentStringMap<NavigationExplicitConnectionRecord>> owners =
            _owners.Set(record.Owner.MapId, ownerMap, out copied);
        copiedNodes = checked(copiedNodes + copied);
        return new NavigationExplicitConnectionIndex(
            owners,
            _incident,
            ownerMapBytes,
            _incidentMapBytes,
            checked(_recordBytes - (prior?.RetainedBytes ?? 0) + record.RetainedBytes),
            _incidentArrayBytes,
            ownerMapPages,
            _incidentMapPages);
    }

    internal NavigationExplicitConnectionIndex RemoveOwner(
        NavigationConnectionOwnerKey owner,
        out bool removed,
        out int copiedNodes)
    {
        copiedNodes = 0;
        if (!_owners.TryGetValue(
                owner.MapId,
                out PersistentStringMap<NavigationExplicitConnectionRecord> ownerMap)
            || !ownerMap.TryGetValue(owner.ConnectionId, out NavigationExplicitConnectionRecord prior))
        {
            removed = false;
            return this;
        }
        removed = true;
        long ownerMapBytes = _ownerMapBytes - ownerMap.RetainedBytes;
        int ownerMapPages = _ownerMapPages - ownerMap.PersistentNodeCount;
        ownerMap = ownerMap.Remove(owner.ConnectionId, out _, out int copied);
        copiedNodes = checked(copiedNodes + copied);
        PersistentStringMap<PersistentStringMap<NavigationExplicitConnectionRecord>> owners;
        if (ownerMap.Count == 0)
            owners = _owners.Remove(owner.MapId, out _, out copied);
        else
        {
            ownerMapBytes = checked(ownerMapBytes + ownerMap.RetainedBytes);
            ownerMapPages = checked(ownerMapPages + ownerMap.PersistentNodeCount);
            owners = _owners.Set(owner.MapId, ownerMap, out copied);
        }
        copiedNodes = checked(copiedNodes + copied);
        return new NavigationExplicitConnectionIndex(
            owners,
            _incident,
            ownerMapBytes,
            _incidentMapBytes,
            checked(_recordBytes - prior.RetainedBytes),
            _incidentArrayBytes,
            ownerMapPages,
            _incidentMapPages);
    }

    internal NavigationExplicitConnectionIndex UpdateIncidence(
        NavigationCellAddress address,
        NavigationConnectionOwnerKey owner,
        bool add,
        out int copiedNodes)
    {
        copiedNodes = 0;
        PersistentStringMap<PersistentVoxelIndexMap<NavigationConnectionOwnerKey[]>> incident =
            _incident;
        long incidentMapBytes = _incidentMapBytes;
        int incidentMapPages = _incidentMapPages;
        long incidentArrayBytes = _incidentArrayBytes;
        UpdateIncident(
            ref incident,
            address,
            owner,
            add,
            _owners,
            ref incidentMapBytes,
            ref incidentMapPages,
            ref incidentArrayBytes,
            ref copiedNodes);
        return new NavigationExplicitConnectionIndex(
            _owners,
            incident,
            _ownerMapBytes,
            incidentMapBytes,
            _recordBytes,
            incidentArrayBytes,
            _ownerMapPages,
            incidentMapPages);
    }

    private static void UpdateIncident(
        ref PersistentStringMap<PersistentVoxelIndexMap<NavigationConnectionOwnerKey[]>> root,
        NavigationCellAddress address,
        NavigationConnectionOwnerKey owner,
        bool add,
        PersistentStringMap<PersistentStringMap<NavigationExplicitConnectionRecord>> owners,
        ref long mapBytes,
        ref int mapPages,
        ref long arrayBytes,
        ref int copiedNodes)
    {
        bool hadMap = root.TryGetValue(
            address.MapId,
            out PersistentVoxelIndexMap<NavigationConnectionOwnerKey[]> map);
        map ??= PersistentVoxelIndexMap<NavigationConnectionOwnerKey[]>.Empty;
        mapBytes -= hadMap ? map.RetainedBytes : 0;
        mapPages -= hadMap ? map.PersistentNodeCount : 0;
        map.TryGetValue(address.Index, out NavigationConnectionOwnerKey[] prior);
        prior ??= Array.Empty<NavigationConnectionOwnerKey>();
        NavigationConnectionOwnerKey[] next = add
            ? Insert(prior, owner, owners)
            : Remove(prior, owner);
        if (ReferenceEquals(prior, next))
        {
            mapBytes += hadMap ? map.RetainedBytes : 0;
            mapPages += hadMap ? map.PersistentNodeCount : 0;
            return;
        }
        arrayBytes = checked(arrayBytes - (prior.Length * 16L) + (next.Length * 16L));
        int copied;
        if (next.Length == 0)
            map = map.Remove(address.Index, out _, out copied);
        else
            map = map.Set(address.Index, next, out copied);
        copiedNodes = checked(copiedNodes + copied);
        if (map.Count == 0)
            root = root.Remove(address.MapId, out _, out copied);
        else
        {
            mapBytes = checked(mapBytes + map.RetainedBytes);
            mapPages = checked(mapPages + map.PersistentNodeCount);
            root = root.Set(address.MapId, map, out copied);
        }
        copiedNodes = checked(copiedNodes + copied);
    }

    private static NavigationConnectionOwnerKey[] Insert(
        NavigationConnectionOwnerKey[] values,
        NavigationConnectionOwnerKey owner,
        PersistentStringMap<PersistentStringMap<NavigationExplicitConnectionRecord>> owners)
    {
        int low = 0;
        int high = values.Length;
        while (low < high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = CompareOwners(values[middle], owner, owners);
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle;
        }
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i].Equals(owner))
                return values;
        }
        var result = new NavigationConnectionOwnerKey[values.Length + 1];
        Array.Copy(values, 0, result, 0, low);
        result[low] = owner;
        Array.Copy(values, low, result, low + 1, values.Length - low);
        return result;
    }

    private static NavigationConnectionOwnerKey[] Remove(
        NavigationConnectionOwnerKey[] values,
        NavigationConnectionOwnerKey owner)
    {
        int index = -1;
        for (int i = 0; i < values.Length; i++)
        {
            if (values[i].Equals(owner))
            {
                index = i;
                break;
            }
        }
        if (index < 0)
            return values;
        if (values.Length == 1)
            return Array.Empty<NavigationConnectionOwnerKey>();
        var result = new NavigationConnectionOwnerKey[values.Length - 1];
        Array.Copy(values, 0, result, 0, index);
        Array.Copy(values, index + 1, result, index, result.Length - index);
        return result;
    }

    private static int CompareOwners(
        NavigationConnectionOwnerKey left,
        NavigationConnectionOwnerKey right,
        PersistentStringMap<PersistentStringMap<NavigationExplicitConnectionRecord>> owners)
    {
        if (!TryGetRecord(owners, left, out NavigationExplicitConnectionRecord leftRecord)
            || !TryGetRecord(owners, right, out NavigationExplicitConnectionRecord rightRecord))
        {
            return left.CompareTo(right);
        }
        int comparison = leftRecord.Source.CompareTo(rightRecord.Source);
        if (comparison != 0)
            return comparison;
        comparison = leftRecord.Destination.CompareTo(rightRecord.Destination);
        if (comparison != 0)
            return comparison;
        comparison = string.CompareOrdinal(left.ConnectionId, right.ConnectionId);
        if (comparison != 0)
            return comparison;
        comparison = CompareAnchor(leftRecord.Definition.EntryAnchor, rightRecord.Definition.EntryAnchor);
        return comparison != 0
            ? comparison
            : CompareAnchor(leftRecord.Definition.ExitAnchor, rightRecord.Definition.ExitAnchor);
    }

    private static bool TryGetRecord(
        PersistentStringMap<PersistentStringMap<NavigationExplicitConnectionRecord>> owners,
        NavigationConnectionOwnerKey owner,
        out NavigationExplicitConnectionRecord record)
    {
        if (owners.TryGetValue(
                owner.MapId,
                out PersistentStringMap<NavigationExplicitConnectionRecord> map)
            && map.TryGetValue(owner.ConnectionId, out record!))
        {
            return true;
        }
        record = null!;
        return false;
    }

    private static int CompareAnchor(FixedMathSharp.Vector3d left, FixedMathSharp.Vector3d right)
    {
        int comparison = left.X.CompareTo(right.X);
        if (comparison != 0)
            return comparison;
        comparison = left.Y.CompareTo(right.Y);
        return comparison != 0
            ? comparison
            : left.Z.CompareTo(right.Z);
    }
}

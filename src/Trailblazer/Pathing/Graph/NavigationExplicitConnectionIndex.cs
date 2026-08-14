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
        PersistentStringMap<PersistentVoxelIndexMap<NavigationPagedSequence<NavigationConnectionOwnerKey>>>.Empty,
        PersistentStringMap<PersistentVoxelIndexMap<NavigationPagedSequence<NavigationConnectionOwnerKey>>>.Empty,
        ownerMapBytes: 0,
        incidentMapBytes: 0,
        endpointMapBytes: 0,
        recordBytes: 0,
        incidentArrayBytes: 0,
        endpointArrayBytes: 0,
        ownerMapPages: 0,
        incidentMapPages: 0,
        endpointMapPages: 0,
        recordPages: 0,
        incidentArrayPages: 0,
        endpointArrayPages: 0);

    private readonly PersistentStringMap<PersistentStringMap<NavigationExplicitConnectionRecord>> _owners;
    private readonly PersistentStringMap<PersistentVoxelIndexMap<NavigationPagedSequence<NavigationConnectionOwnerKey>>> _incident;
    private readonly PersistentStringMap<PersistentVoxelIndexMap<NavigationPagedSequence<NavigationConnectionOwnerKey>>> _endpoints;
    private readonly long _ownerMapBytes;
    private readonly long _incidentMapBytes;
    private readonly long _endpointMapBytes;
    private readonly long _recordBytes;
    private readonly long _incidentArrayBytes;
    private readonly long _endpointArrayBytes;
    private readonly int _ownerMapPages;
    private readonly int _incidentMapPages;
    private readonly int _endpointMapPages;
    private readonly int _recordPages;
    private readonly int _incidentArrayPages;
    private readonly int _endpointArrayPages;

    private NavigationExplicitConnectionIndex(
        PersistentStringMap<PersistentStringMap<NavigationExplicitConnectionRecord>> owners,
        PersistentStringMap<PersistentVoxelIndexMap<NavigationPagedSequence<NavigationConnectionOwnerKey>>> incident,
        PersistentStringMap<PersistentVoxelIndexMap<NavigationPagedSequence<NavigationConnectionOwnerKey>>> endpoints,
        long ownerMapBytes,
        long incidentMapBytes,
        long endpointMapBytes,
        long recordBytes,
        long incidentArrayBytes,
        long endpointArrayBytes,
        int ownerMapPages,
        int incidentMapPages,
        int endpointMapPages,
        int recordPages,
        int incidentArrayPages,
        int endpointArrayPages)
    {
        _owners = owners;
        _incident = incident;
        _endpoints = endpoints;
        _ownerMapBytes = ownerMapBytes;
        _incidentMapBytes = incidentMapBytes;
        _endpointMapBytes = endpointMapBytes;
        _recordBytes = recordBytes;
        _incidentArrayBytes = incidentArrayBytes;
        _endpointArrayBytes = endpointArrayBytes;
        _ownerMapPages = ownerMapPages;
        _incidentMapPages = incidentMapPages;
        _endpointMapPages = endpointMapPages;
        _recordPages = recordPages;
        _incidentArrayPages = incidentArrayPages;
        _endpointArrayPages = endpointArrayPages;
    }

    internal long RetainedBytes => checked(
        112L
        + _owners.RetainedBytes
        + _incident.RetainedBytes
        + _endpoints.RetainedBytes
        + _ownerMapBytes
        + _incidentMapBytes
        + _endpointMapBytes
        + _recordBytes
        + _incidentArrayBytes
        + _endpointArrayBytes);

    internal int PersistentPageCount => checked(
        3
        + _owners.PersistentNodeCount
        + _incident.PersistentNodeCount
        + _endpoints.PersistentNodeCount
        + _ownerMapPages
        + _incidentMapPages
        + _endpointMapPages
        + _recordPages
        + _incidentArrayPages
        + _endpointArrayPages);

    internal long PayloadRetainedBytes => checked(
        _recordBytes + _incidentArrayBytes + _endpointArrayBytes);

    internal int PayloadPersistentPageCount => checked(
        _recordPages + _incidentArrayPages + _endpointArrayPages);

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

    internal NavigationPagedSequence<NavigationConnectionOwnerKey>.Enumerator
        GetIncidentOwnerEnumerator(NavigationCellAddress address) =>
            GetIncidentOwnerRow(address).GetEnumerator();

    internal NavigationPagedSequence<NavigationConnectionOwnerKey> GetIncidentOwnerRow(
        NavigationCellAddress address)
    {
        if (_incident.TryGetValue(
                address.MapId,
                out PersistentVoxelIndexMap<NavigationPagedSequence<NavigationConnectionOwnerKey>> map)
            && map.TryGetValue(
                address.Index,
                out NavigationPagedSequence<NavigationConnectionOwnerKey> owners))
        {
            return owners;
        }
        return NavigationPagedSequence<NavigationConnectionOwnerKey>.Empty;
    }

    internal NavigationPagedSequence<NavigationConnectionOwnerKey> GetEndpointOwnerRow(
        NavigationCellAddress address)
    {
        if (_endpoints.TryGetValue(
                address.MapId,
                out PersistentVoxelIndexMap<NavigationPagedSequence<NavigationConnectionOwnerKey>> map)
            && map.TryGetValue(
                address.Index,
                out NavigationPagedSequence<NavigationConnectionOwnerKey> owners))
        {
            return owners;
        }
        return NavigationPagedSequence<NavigationConnectionOwnerKey>.Empty;
    }

    internal int GetIncidentAddressCount(string mapId) =>
        _incident.TryGetValue(
            mapId,
            out PersistentVoxelIndexMap<NavigationPagedSequence<NavigationConnectionOwnerKey>> addresses)
                ? addresses.Count
                : 0;

    internal NavigationCellAddress GetIncidentAddressAt(string mapId, int ordinal)
    {
        _incident.TryGetValue(
            mapId,
            out PersistentVoxelIndexMap<NavigationPagedSequence<NavigationConnectionOwnerKey>> addresses);
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
            _endpoints,
            ownerMapBytes,
            _incidentMapBytes,
            _endpointMapBytes,
            checked(_recordBytes - (prior?.RetainedBytes ?? 0) + record.RetainedBytes),
            _incidentArrayBytes,
            _endpointArrayBytes,
            ownerMapPages,
            _incidentMapPages,
            _endpointMapPages,
            checked(_recordPages - (prior?.PersistentPageCount ?? 0) + record.PersistentPageCount),
            _incidentArrayPages,
            _endpointArrayPages);
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
            _endpoints,
            ownerMapBytes,
            _incidentMapBytes,
            _endpointMapBytes,
            checked(_recordBytes - prior.RetainedBytes),
            _incidentArrayBytes,
            _endpointArrayBytes,
            ownerMapPages,
            _incidentMapPages,
            _endpointMapPages,
            checked(_recordPages - prior.PersistentPageCount),
            _incidentArrayPages,
            _endpointArrayPages);
    }

    internal NavigationExplicitConnectionIndex SetIncidentRow(
        NavigationCellAddress address,
        NavigationPagedSequence<NavigationConnectionOwnerKey> prior,
        NavigationPagedSequence<NavigationConnectionOwnerKey> next,
        out int copiedNodes)
    {
        copiedNodes = 0;
        bool hadMap = _incident.TryGetValue(
            address.MapId,
            out PersistentVoxelIndexMap<NavigationPagedSequence<NavigationConnectionOwnerKey>> map);
        map ??= PersistentVoxelIndexMap<NavigationPagedSequence<NavigationConnectionOwnerKey>>.Empty;
        long incidentMapBytes = _incidentMapBytes - (hadMap ? map.RetainedBytes : 0);
        int incidentMapPages = _incidentMapPages - (hadMap ? map.PersistentNodeCount : 0);
        int copied;
        if (next.Count == 0)
            map = map.Remove(address.Index, out _, out copied);
        else
            map = map.Set(address.Index, next, out copied);
        copiedNodes = checked(copiedNodes + copied);
        PersistentStringMap<PersistentVoxelIndexMap<NavigationPagedSequence<NavigationConnectionOwnerKey>>> incident;
        if (map.Count == 0)
            incident = _incident.Remove(address.MapId, out _, out copied);
        else
        {
            incidentMapBytes = checked(incidentMapBytes + map.RetainedBytes);
            incidentMapPages = checked(incidentMapPages + map.PersistentNodeCount);
            incident = _incident.Set(address.MapId, map, out copied);
        }
        copiedNodes = checked(copiedNodes + copied);
        return new NavigationExplicitConnectionIndex(
            _owners,
            incident,
            _endpoints,
            _ownerMapBytes,
            incidentMapBytes,
            _endpointMapBytes,
            _recordBytes,
            checked(
                _incidentArrayBytes
                - prior.RetainedBytes
                + next.RetainedBytes),
            _endpointArrayBytes,
            _ownerMapPages,
            incidentMapPages,
            _endpointMapPages,
            _recordPages,
            checked(
                _incidentArrayPages
                - prior.PersistentPageCount
                + next.PersistentPageCount),
            _endpointArrayPages);
    }

    internal NavigationExplicitConnectionIndex SetEndpointRow(
        NavigationCellAddress address,
        NavigationPagedSequence<NavigationConnectionOwnerKey> prior,
        NavigationPagedSequence<NavigationConnectionOwnerKey> next,
        out int copiedNodes)
    {
        copiedNodes = 0;
        bool hadMap = _endpoints.TryGetValue(
            address.MapId,
            out PersistentVoxelIndexMap<NavigationPagedSequence<NavigationConnectionOwnerKey>> map);
        map ??= PersistentVoxelIndexMap<NavigationPagedSequence<NavigationConnectionOwnerKey>>.Empty;
        long endpointMapBytes = _endpointMapBytes - (hadMap ? map.RetainedBytes : 0);
        int endpointMapPages = _endpointMapPages - (hadMap ? map.PersistentNodeCount : 0);
        int copied;
        if (next.Count == 0)
            map = map.Remove(address.Index, out _, out copied);
        else
            map = map.Set(address.Index, next, out copied);
        copiedNodes = checked(copiedNodes + copied);
        PersistentStringMap<PersistentVoxelIndexMap<NavigationPagedSequence<NavigationConnectionOwnerKey>>> endpoints;
        if (map.Count == 0)
            endpoints = _endpoints.Remove(address.MapId, out _, out copied);
        else
        {
            endpointMapBytes = checked(endpointMapBytes + map.RetainedBytes);
            endpointMapPages = checked(endpointMapPages + map.PersistentNodeCount);
            endpoints = _endpoints.Set(address.MapId, map, out copied);
        }
        copiedNodes = checked(copiedNodes + copied);
        return new NavigationExplicitConnectionIndex(
            _owners,
            _incident,
            endpoints,
            _ownerMapBytes,
            _incidentMapBytes,
            endpointMapBytes,
            _recordBytes,
            _incidentArrayBytes,
            checked(
                _endpointArrayBytes
                - prior.RetainedBytes
                + next.RetainedBytes),
            _ownerMapPages,
            _incidentMapPages,
            endpointMapPages,
            _recordPages,
            _incidentArrayPages,
            checked(
                _endpointArrayPages
                - prior.PersistentPageCount
                + next.PersistentPageCount));
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

    internal int CompareOwners(
        NavigationConnectionOwnerKey left,
        NavigationConnectionOwnerKey right) => CompareOwners(left, right, _owners);

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

//=======================================================================
// NavigationTransitionPage.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using GridForge.Spatial;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Identifies one source-map-owned explicit transition.</summary>
internal readonly struct NavigationTransitionOwnerKey : IEquatable<NavigationTransitionOwnerKey>
{
    internal NavigationTransitionOwnerKey(string mapId, string transitionId)
    {
        MapId = mapId;
        TransitionId = transitionId;
    }

    internal string MapId { get; }

    internal string TransitionId { get; }

    public bool Equals(NavigationTransitionOwnerKey other) =>
        string.Equals(MapId, other.MapId, StringComparison.Ordinal)
        && string.Equals(TransitionId, other.TransitionId, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is NavigationTransitionOwnerKey other && Equals(other);

    public override int GetHashCode()
    {
        var comparer = SwiftHashTools.GetDeterministicStringEqualityComparer();
        return SwiftHashTools.CombineHashCodes(
            comparer.GetHashCode(MapId),
            comparer.GetHashCode(TransitionId));
    }
}

/// <summary>Identifies one exact map-local transition page.</summary>
internal readonly struct NavigationTransitionPageAddress : IEquatable<NavigationTransitionPageAddress>
{
    internal NavigationTransitionPageAddress(string mapId, int pageIndex)
    {
        MapId = mapId;
        PageIndex = pageIndex;
    }

    internal string MapId { get; }

    internal int PageIndex { get; }

    public bool Equals(NavigationTransitionPageAddress other) =>
        PageIndex == other.PageIndex
        && string.Equals(MapId, other.MapId, StringComparison.Ordinal);

    public override bool Equals(object? obj) =>
        obj is NavigationTransitionPageAddress other && Equals(other);

    public override int GetHashCode()
    {
        var comparer = SwiftHashTools.GetDeterministicStringEqualityComparer();
        return SwiftHashTools.CombineHashCodes(comparer.GetHashCode(MapId), PageIndex);
    }
}

/// <summary>Stores one complete source-owned explicit transition record.</summary>
internal readonly struct NavigationPublishedTransition : IEquatable<NavigationPublishedTransition>
{
    internal NavigationPublishedTransition(
        string ownerMapId,
        TraversalTransitionDefinition definition,
        NavigationTransitionPageAddress sourcePage,
        NavigationTransitionPageAddress? destinationPage)
    {
        Owner = new NavigationTransitionOwnerKey(ownerMapId, definition.Id);
        Definition = definition;
        SourcePageIndex = sourcePage.PageIndex;
        HasDestinationPage = destinationPage.HasValue;
        DestinationPageIndex = destinationPage?.PageIndex ?? -1;
    }

    internal NavigationTransitionOwnerKey Owner { get; }

    internal TraversalTransitionDefinition Definition { get; }

    internal int SourcePageIndex { get; }

    internal bool HasDestinationPage { get; }

    internal int DestinationPageIndex { get; }

    internal NavigationTransitionPageAddress SourcePage => new(
        Owner.MapId,
        SourcePageIndex);

    internal NavigationTransitionPageAddress? DestinationPage => HasDestinationPage
        ? new NavigationTransitionPageAddress(
            Definition.Destination.MapId,
            DestinationPageIndex)
        : null;

    internal NavigationCellAddress SourceAddress => new(Owner.MapId, Definition.SourceIndex);

    public bool Equals(NavigationPublishedTransition other) =>
        Owner.Equals(other.Owner)
        && Definition.Equals(other.Definition)
        && SourcePageIndex == other.SourcePageIndex
        && HasDestinationPage == other.HasDestinationPage
        && DestinationPageIndex == other.DestinationPageIndex;

    public override bool Equals(object? obj) =>
        obj is NavigationPublishedTransition other && Equals(other);

    public override int GetHashCode() =>
        SwiftHashTools.CombineHashCodes(Owner.GetHashCode(), Definition.GetHashCode());
}

/// <summary>Stores the lightweight destination-side reference to one source-owned transition.</summary>
internal readonly struct NavigationIncomingTransitionRef : IEquatable<NavigationIncomingTransitionRef>
{
    internal NavigationIncomingTransitionRef(NavigationPublishedTransition transition)
    {
        Owner = transition.Owner;
        SourcePageIndex = transition.SourcePageIndex;
        SourceIndex = transition.Definition.SourceIndex;
        SourceMedium = transition.Definition.SourceMedium;
        DestinationMedium = transition.Definition.DestinationMedium;
        Type = transition.Definition.Type;
    }

    internal NavigationTransitionOwnerKey Owner { get; }

    internal int SourcePageIndex { get; }

    internal VoxelIndex SourceIndex { get; }

    internal NavigationTransitionPageAddress SourcePage => new(
        Owner.MapId,
        SourcePageIndex);

    internal NavigationCellAddress SourceAddress => new(Owner.MapId, SourceIndex);

    internal TraversalMedium SourceMedium { get; }

    internal TraversalMedium DestinationMedium { get; }

    internal TraversalTransitionType Type { get; }

    public bool Equals(NavigationIncomingTransitionRef other) =>
        Owner.Equals(other.Owner)
        && SourcePageIndex == other.SourcePageIndex
        && SourceIndex.Equals(other.SourceIndex)
        && SourceMedium == other.SourceMedium
        && DestinationMedium == other.DestinationMedium
        && Type == other.Type;

    public override bool Equals(object? obj) =>
        obj is NavigationIncomingTransitionRef other && Equals(other);

    public override int GetHashCode() =>
        SwiftHashTools.CombineHashCodes(Owner.GetHashCode(), SourceIndex.GetHashCode());
}

/// <summary>Stores immutable outgoing records and incoming references for one cell page.</summary>
internal sealed class NavigationTransitionPage
{
    internal const long BaseRetainedBytes = 64L;
    internal static readonly long OutgoingRecordBytes =
        Unsafe.SizeOf<NavigationPublishedTransition>();
    internal static readonly long IncomingRecordBytes =
        Unsafe.SizeOf<NavigationIncomingTransitionRef>();
    private const long ArrayHeaderBytes = 24L;

    private readonly NavigationPublishedTransition[] _outgoing;
    private readonly NavigationIncomingTransitionRef[] _incoming;

    internal NavigationTransitionPage(
        NavigationTransitionPageAddress address,
        long version,
        NavigationPublishedTransition[] outgoing,
        NavigationIncomingTransitionRef[] incoming)
    {
        Address = address;
        Version = version;
        _outgoing = outgoing;
        _incoming = incoming;
    }

    internal NavigationTransitionPageAddress Address { get; }

    internal long Version { get; }

    internal int OutgoingCount => _outgoing.Length;

    internal int IncomingCount => _incoming.Length;

    internal bool IsEmpty => _outgoing.Length == 0 && _incoming.Length == 0;

    internal long RetainedBytes => GetRetainedBytes(_outgoing.Length, _incoming.Length);

    internal int PersistentPageCount => 1
        + (_outgoing.Length == 0 ? 0 : 1)
        + (_incoming.Length == 0 ? 0 : 1);

    internal bool TryGetOutgoing(
        NavigationTransitionOwnerKey owner,
        out NavigationPublishedTransition transition)
    {
        int index = FindOutgoing(owner);
        if (index >= 0)
        {
            transition = _outgoing[index];
            return true;
        }
        transition = default;
        return false;
    }

    internal NavigationPublishedTransition GetOutgoingAt(int index) => _outgoing[index];

    internal NavigationIncomingTransitionRef GetIncomingAt(int index) => _incoming[index];

    internal Enumerator GetOutgoingEnumerator(
        NavigationWorldGraph graph,
        NavigationMediumStateRef state) => new(
        graph,
        state,
        _outgoing,
        null,
        activeOnly: true);

    internal Enumerator GetOutgoingCandidateEnumerator(
        NavigationWorldGraph graph,
        NavigationMediumStateRef state) => new(
        graph,
        state,
        _outgoing,
        null,
        activeOnly: false);

    internal Enumerator GetIncomingEnumerator(
        NavigationWorldGraph graph,
        NavigationMediumStateRef state) => new(
        graph,
        state,
        null,
        _incoming,
        activeOnly: true);

    internal Enumerator GetIncomingCandidateEnumerator(
        NavigationWorldGraph graph,
        NavigationMediumStateRef state) => new(
        graph,
        state,
        null,
        _incoming,
        activeOnly: false);

    private int FindOutgoing(NavigationTransitionOwnerKey owner)
    {
        for (int i = 0; i < _outgoing.Length; i++)
        {
            if (_outgoing[i].Owner.Equals(owner))
                return i;
        }
        return -1;
    }

    internal static int CompareOutgoing(
        NavigationPublishedTransition left,
        NavigationPublishedTransition right)
    {
        int comparison = left.SourceAddress.CompareTo(right.SourceAddress);
        if (comparison != 0)
            return comparison;
        comparison = ((int)left.Definition.SourceMedium).CompareTo(
            (int)right.Definition.SourceMedium);
        if (comparison != 0)
            return comparison;
        comparison = left.Definition.Destination.CompareTo(right.Definition.Destination);
        if (comparison != 0)
            return comparison;
        comparison = ((int)left.Definition.DestinationMedium).CompareTo(
            (int)right.Definition.DestinationMedium);
        if (comparison != 0)
            return comparison;
        comparison = ((int)left.Definition.Type).CompareTo((int)right.Definition.Type);
        return comparison != 0
            ? comparison
            : string.CompareOrdinal(left.Owner.TransitionId, right.Owner.TransitionId);
    }

    internal static int CompareIncoming(
        NavigationIncomingTransitionRef left,
        NavigationIncomingTransitionRef right)
    {
        int comparison = ((int)left.DestinationMedium).CompareTo((int)right.DestinationMedium);
        if (comparison != 0)
            return comparison;
        comparison = left.SourceAddress.CompareTo(right.SourceAddress);
        if (comparison != 0)
            return comparison;
        comparison = ((int)left.SourceMedium).CompareTo((int)right.SourceMedium);
        if (comparison != 0)
            return comparison;
        comparison = ((int)left.Type).CompareTo((int)right.Type);
        if (comparison != 0)
            return comparison;
        comparison = string.CompareOrdinal(left.Owner.MapId, right.Owner.MapId);
        return comparison != 0
            ? comparison
            : string.CompareOrdinal(left.Owner.TransitionId, right.Owner.TransitionId);
    }

    internal static long GetRetainedBytes(int outgoingCount, int incomingCount) => checked(
        BaseRetainedBytes
        + GetArrayBytes(outgoingCount, OutgoingRecordBytes)
        + GetArrayBytes(incomingCount, IncomingRecordBytes));

    internal static long GetArrayBytes(int count, long elementBytes) => count == 0
        ? 0L
        : checked(ArrayHeaderBytes + ((long)count * elementBytes));

    internal struct Enumerator
    {
        private readonly NavigationWorldGraph? _graph;
        private readonly NavigationMediumStateRef _state;
        private readonly NavigationPublishedTransition[]? _outgoing;
        private readonly NavigationIncomingTransitionRef[]? _incoming;
        private readonly bool _activeOnly;
        private int _index;

        internal Enumerator(
            NavigationWorldGraph graph,
            NavigationMediumStateRef state,
            NavigationPublishedTransition[]? outgoing,
            NavigationIncomingTransitionRef[]? incoming,
            bool activeOnly)
        {
            _graph = graph;
            _state = state;
            _outgoing = outgoing;
            _incoming = incoming;
            _activeOnly = activeOnly;
            _index = 0;
            Current = default;
        }

        internal NavigationPublishedTransition Current { get; private set; }

        internal bool MoveNext()
        {
            if (_outgoing != null)
            {
                while (_index < _outgoing.Length)
                {
                    NavigationPublishedTransition candidate = _outgoing[_index++];
                    if (_activeOnly
                        ? _graph!.IsTransitionActive(candidate, _state, outgoing: true)
                        : _graph!.IsTransitionEndpoint(candidate, _state, outgoing: true))
                    {
                        Current = candidate;
                        return true;
                    }
                }
                return false;
            }
            while (_incoming != null && _index < _incoming.Length)
            {
                NavigationIncomingTransitionRef candidate = _incoming[_index++];
                if (_graph!.TryGetPublishedTransition(
                        candidate,
                        out NavigationPublishedTransition record)
                    && (_activeOnly
                        ? _graph.IsTransitionActive(record, _state, outgoing: false)
                        : _graph.IsTransitionEndpoint(record, _state, outgoing: false)))
                {
                    Current = record;
                    return true;
                }
            }
            Current = default;
            return false;
        }
    }
}

/// <summary>Owns immutable transition pages without adding a second edge registry or cache.</summary>
internal sealed class NavigationTransitionPageRoot
{
    internal static readonly NavigationTransitionPageRoot Empty = new(
        PersistentStringMap<PersistentIntMap<NavigationTransitionPage>>.Empty,
        innerMapBytes: 0,
        pageBytes: 0,
        innerMapPages: 0,
        pagePages: 0);

    private readonly PersistentStringMap<PersistentIntMap<NavigationTransitionPage>> _maps;
    private readonly long _innerMapBytes;
    private readonly long _pageBytes;
    private readonly int _innerMapPages;
    private readonly int _pagePages;

    private NavigationTransitionPageRoot(
        PersistentStringMap<PersistentIntMap<NavigationTransitionPage>> maps,
        long innerMapBytes,
        long pageBytes,
        int innerMapPages,
        int pagePages)
    {
        _maps = maps;
        _innerMapBytes = innerMapBytes;
        _pageBytes = pageBytes;
        _innerMapPages = innerMapPages;
        _pagePages = pagePages;
    }

    internal long RetainedBytes => checked(
        56L + _maps.RetainedBytes + _innerMapBytes + _pageBytes);

    internal int PersistentPageCount => checked(
        1 + _maps.PersistentNodeCount + _innerMapPages + _pagePages);

    internal bool TryGet(
        NavigationTransitionPageAddress address,
        out NavigationTransitionPage page)
    {
        if (_maps.TryGetValue(
                address.MapId,
                out PersistentIntMap<NavigationTransitionPage> map)
            && map.TryGetValue(address.PageIndex, out page!))
        {
            return true;
        }
        page = null!;
        return false;
    }

    internal NavigationTransitionPageRoot Set(
        NavigationTransitionPage page,
        out int copiedNodes) => Set(page, out copiedNodes, out _);

    internal NavigationTransitionPageRoot Set(
        NavigationTransitionPage page,
        out int copiedNodes,
        out long copiedBytes)
    {
        copiedNodes = 0;
        copiedBytes = 0;
        bool hadMap = _maps.TryGetValue(
            page.Address.MapId,
            out PersistentIntMap<NavigationTransitionPage> map);
        map ??= PersistentIntMap<NavigationTransitionPage>.Empty;
        map.TryGetValue(page.Address.PageIndex, out NavigationTransitionPage prior);
        long innerBytes = _innerMapBytes - (hadMap ? map.RetainedBytes : 0L);
        int innerPages = _innerMapPages - (hadMap ? map.PersistentNodeCount : 0);
        int copied;
        if (page.IsEmpty)
            map = map.Remove(page.Address.PageIndex, out copied);
        else
            map = map.Set(page.Address.PageIndex, page, out copied);
        copiedNodes = checked(copiedNodes + copied);
        copiedBytes = checked(copiedBytes + ((long)copied * 72L));
        PersistentStringMap<PersistentIntMap<NavigationTransitionPage>> maps;
        if (map.Count == 0)
            maps = _maps.Remove(page.Address.MapId, out _, out copied);
        else
        {
            innerBytes = checked(innerBytes + map.RetainedBytes);
            innerPages = checked(innerPages + map.PersistentNodeCount);
            maps = _maps.Set(page.Address.MapId, map, out copied);
        }
        copiedNodes = checked(copiedNodes + copied);
        copiedBytes = checked(copiedBytes + ((long)copied * 64L));
        return new NavigationTransitionPageRoot(
            maps,
            innerBytes,
            checked(_pageBytes - (prior?.RetainedBytes ?? 0L) + (page.IsEmpty ? 0L : page.RetainedBytes)),
            innerPages,
            checked(_pagePages - (prior?.PersistentPageCount ?? 0) + (page.IsEmpty ? 0 : page.PersistentPageCount)));
    }
}

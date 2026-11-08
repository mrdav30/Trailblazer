//=======================================================================
// NavigationTransitionPage.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>Stores immutable outgoing records and incoming references for one cell page.</summary>
internal sealed partial class NavigationTransitionPage
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
        return string.CompareOrdinal(left.Owner.TransitionId, right.Owner.TransitionId);
    }

    internal static long GetRetainedBytes(int outgoingCount, int incomingCount) => checked(
        BaseRetainedBytes
        + GetArrayBytes(outgoingCount, OutgoingRecordBytes)
        + GetArrayBytes(incomingCount, IncomingRecordBytes));

    internal static long GetArrayBytes(int count, long elementBytes) => count == 0
        ? 0L
        : checked(ArrayHeaderBytes + ((long)count * elementBytes));
}

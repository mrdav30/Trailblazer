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
        NavigationPublishedTransition right) =>
        CompareOutgoing(left, right.SourceAddress, right.Definition.SourceMedium, right.Definition.Destination,
            right.Definition.DestinationMedium, right.Definition.Type, right.Owner.TransitionId);

    internal int FindIncomingSourceOrdinal(NavigationIncomingTransitionRef incoming, NavigationCellAddress destination,
        NavigationWorkMeter meter, out bool budgetExhausted)
    {
        budgetExhausted = false;
        int low = 0;
        int high = _outgoing.Length - 1;
        NavigationCellAddress source = incoming.SourceAddress;
        while (low <= high)
        {
            if (!meter.TryConsumeLookupProbes(1))
            {
                budgetExhausted = true;
                return -1;
            }
            int middle = low + ((high - low) >> 1);
            int comparison = CompareOutgoing(_outgoing[middle], source, incoming.SourceMedium,
                destination, incoming.DestinationMedium, incoming.Type, incoming.Owner.TransitionId);
            if (comparison == 0)
                return middle;
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        return -1;
    }

    private static int CompareOutgoing(NavigationPublishedTransition left, NavigationCellAddress source,
        TraversalMedium sourceMedium, NavigationCellAddress destination, TraversalMedium destinationMedium,
        TraversalTransitionType type, string id)
    {
        int comparison = left.SourceAddress.CompareTo(source);
        if (comparison != 0)
            return comparison;
        comparison = ((int)left.Definition.SourceMedium).CompareTo(
            (int)sourceMedium);
        if (comparison != 0)
            return comparison;
        comparison = left.Definition.Destination.CompareTo(destination);
        if (comparison != 0)
            return comparison;
        comparison = ((int)left.Definition.DestinationMedium).CompareTo(
            (int)destinationMedium);
        if (comparison != 0)
            return comparison;
        comparison = ((int)left.Definition.Type).CompareTo((int)type);
        return comparison != 0
            ? comparison
            : string.CompareOrdinal(left.Owner.TransitionId, id);
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

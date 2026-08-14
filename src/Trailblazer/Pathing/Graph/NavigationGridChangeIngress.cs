//=======================================================================
// NavigationGridChangeIngress.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using GridForge.Grids;
using GridForge.Spatial;
using SwiftCollections;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>Buffers the single GridForge committed final-state feed without running graph work in callbacks.</summary>
internal sealed unsafe class NavigationGridChangeIngress
{
    private const int BaseRetainedBytes = 384;
    private static readonly int EventSlotBytes = sizeof(GridEventInfo) + (2 * sizeof(int));
    private static readonly int DictionaryEntryBytes = AlignToEight(
        sizeof(EventKey) + (2 * sizeof(int)) + sizeof(bool));
    private static readonly int ScopeEntryBytes = AlignToEight(
        sizeof(NavigationGridChangeScope) + sizeof(int));
    private readonly object _sync = new();
    private readonly GridEventInfo[] _events;
    private readonly int[] _next;
    private readonly int[] _previous;
    private readonly SwiftDictionary<EventKey, int> _coalescedIndexes;
    private readonly ScopeEntry[] _scopes;
    private int _head = -1;
    private int _tail = -1;
    private int _freeHead;
    private int _count;
    private int _scopeCount;
    private int _topologyLifecycleCount;
    private bool _scopeTrackingAll;
    private bool _overflowed;
    private bool _overflowedTopologyLifecycle;
    private bool _disposed;

    internal NavigationGridChangeIngress(int capacity, int maximumScopes = 16)
    {
        _events = new GridEventInfo[capacity];
        _next = new int[capacity];
        _previous = new int[capacity];
        int indexCapacity = checked(((capacity * 100) / 82) + 1);
        _coalescedIndexes = new SwiftDictionary<EventKey, int>(indexCapacity);
        _scopes = new ScopeEntry[maximumScopes];
        ResetSlots();
    }

    internal int IndexCapacity => _coalescedIndexes.Capacity;

    internal long GetRetainedBytes() => GetRetainedBytes(_events.Length, _scopes.Length);

    internal static int GetMaximumCapacity(
        int maximumEntries,
        long maximumBytes,
        int maximumScopes = 16)
    {
        int low = 0;
        int high = maximumEntries;
        while (low < high)
        {
            int middle = low + ((high - low + 1) >> 1);
            if (GetRetainedBytes(middle, maximumScopes) <= maximumBytes)
                low = middle;
            else
                high = middle - 1;
        }
        return low;
    }

    internal static long GetRetainedBytes(int capacity, int maximumScopes = 16)
    {
        int indexCapacity = GetIndexCapacity(capacity);
        return checked(
            BaseRetainedBytes
            + ((long)capacity * EventSlotBytes)
            + ((long)indexCapacity * DictionaryEntryBytes)
            + ((long)maximumScopes * ScopeEntryBytes));
    }

    internal void Enqueue(GridEventInfo eventInfo)
    {
        lock (_sync)
        {
            if (_disposed)
                return;
            if (_overflowed)
            {
                TrackScope(eventInfo);
                _overflowedTopologyLifecycle |= IsTopologyLifecycle(eventInfo);
                return;
            }
            if (EventKey.TryCreate(eventInfo, out EventKey key)
                && _coalescedIndexes.TryGetValue(key, out int existing))
            {
                bool priorTopologyLifecycle = IsTopologyLifecycle(_events[existing]);
                bool nextTopologyLifecycle = IsTopologyLifecycle(eventInfo);
                if (priorTopologyLifecycle != nextTopologyLifecycle)
                    _topologyLifecycleCount += nextTopologyLifecycle ? 1 : -1;
                Unlink(existing);
                _events[existing] = eventInfo;
                Append(existing);
                return;
            }
            if (_count == _events.Length)
            {
                _overflowed = true;
                TrackScope(eventInfo);
                _overflowedTopologyLifecycle = IsTopologyLifecycle(eventInfo);
                return;
            }
            int insertion = _freeHead;
            _freeHead = _next[insertion];
            _events[insertion] = eventInfo;
            Append(insertion);
            _count++;
            if (IsTopologyLifecycle(eventInfo))
                _topologyLifecycleCount++;
            if (key.IsValid)
                _coalescedIndexes[key] = insertion;
            TrackScope(eventInfo);
        }
    }

    internal int DetachInto(
        Span<GridEventInfo> destination,
        Span<NavigationGridChangeScope> blockedScopes,
        out int blockedScopeCount,
        out bool blockAll) => DetachInto(
            destination,
            blockedScopes,
            out blockedScopeCount,
            out blockAll,
            out _);

    internal int DetachInto(
        Span<GridEventInfo> destination,
        Span<NavigationGridChangeScope> blockedScopes,
        out int blockedScopeCount,
        out bool blockAll,
        out bool topologyLifecycleCoverageLost)
    {
        lock (_sync)
        {
            if (_overflowed)
            {
                topologyLifecycleCoverageLost = _topologyLifecycleCount != 0
                    || _overflowedTopologyLifecycle;
                blockedScopeCount = CopyBlockedScopes(blockedScopes, out blockAll);
                _coalescedIndexes.Clear();
                ResetSlots();
                return 0;
            }

            int count = _count < destination.Length ? _count : destination.Length;
            for (int i = 0; i < count; i++)
            {
                int slot = _head;
                GridEventInfo eventInfo = _events[slot];
                destination[i] = eventInfo;
                Unlink(slot);
                if (EventKey.TryCreate(eventInfo, out EventKey key)
                    && _coalescedIndexes.TryGetValue(key, out int indexedSlot)
                    && indexedSlot == slot)
                {
                    _coalescedIndexes.Remove(key);
                }
                UntrackScope(eventInfo);
                if (IsTopologyLifecycle(eventInfo))
                    _topologyLifecycleCount--;
                _next[slot] = _freeHead;
                _previous[slot] = -1;
                _freeHead = slot;
            }
            _count -= count;
            topologyLifecycleCoverageLost = false;
            blockAll = false;
            blockedScopeCount = _count > 0
                ? CopyBlockedScopes(blockedScopes, out blockAll)
                : 0;
            if (_count == 0)
            {
                blockAll = false;
                _scopeCount = 0;
                _scopeTrackingAll = false;
            }
            return count;
        }
    }

    /// <summary>
    /// Restores an unpublished committed prefix ahead of changes that arrived after detachment.
    /// Newer same-address final states win without moving backward in committed sequence order.
    /// </summary>
    internal void RequeuePrefix(ReadOnlySpan<GridEventInfo> prefix)
    {
        if (prefix.IsEmpty)
            return;
        lock (_sync)
        {
            if (_disposed)
                return;
            if (_overflowed || !IsOrderedBeforeCurrent(prefix))
            {
                for (int i = 0; i < prefix.Length; i++)
                {
                    TrackScope(prefix[i]);
                    _overflowedTopologyLifecycle |= IsTopologyLifecycle(prefix[i]);
                }
                _overflowed = true;
                return;
            }

            int insertionCount = 0;
            for (int i = 0; i < prefix.Length; i++)
            {
                if (!EventKey.TryCreate(prefix[i], out EventKey key)
                    || !_coalescedIndexes.TryGetValue(key, out int existing))
                {
                    insertionCount++;
                    continue;
                }
                if (_events[existing].ChangeSequence < prefix[i].ChangeSequence)
                {
                    for (int scope = 0; scope < prefix.Length; scope++)
                    {
                        TrackScope(prefix[scope]);
                        _overflowedTopologyLifecycle |= IsTopologyLifecycle(prefix[scope]);
                    }
                    _overflowed = true;
                    return;
                }
            }
            if (insertionCount > _events.Length - _count)
            {
                for (int i = 0; i < prefix.Length; i++)
                {
                    TrackScope(prefix[i]);
                    _overflowedTopologyLifecycle |= IsTopologyLifecycle(prefix[i]);
                }
                _overflowed = true;
                return;
            }

            for (int i = prefix.Length - 1; i >= 0; i--)
            {
                GridEventInfo eventInfo = prefix[i];
                bool keyed = EventKey.TryCreate(eventInfo, out EventKey key);
                if (keyed && _coalescedIndexes.ContainsKey(key))
                    continue;
                int insertion = _freeHead;
                _freeHead = _next[insertion];
                _events[insertion] = eventInfo;
                Prepend(insertion);
                _count++;
                if (IsTopologyLifecycle(eventInfo))
                    _topologyLifecycleCount++;
                if (keyed)
                    _coalescedIndexes[key] = insertion;
                TrackScope(eventInfo);
            }
        }
    }

    internal void MarkResnapshotRequired()
    {
        lock (_sync)
        {
            _scopeTrackingAll = true;
            _overflowed = true;
        }
    }

    internal void Reset()
    {
        lock (_sync)
        {
            _coalescedIndexes.Clear();
            ResetSlots();
        }
    }

    internal void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _coalescedIndexes.Clear();
            ResetSlots();
        }
    }

    private void Append(int slot)
    {
        _previous[slot] = _tail;
        _next[slot] = -1;
        if (_tail >= 0)
            _next[_tail] = slot;
        else
            _head = slot;
        _tail = slot;
    }

    private void Prepend(int slot)
    {
        _previous[slot] = -1;
        _next[slot] = _head;
        if (_head >= 0)
            _previous[_head] = slot;
        else
            _tail = slot;
        _head = slot;
    }

    private bool IsOrderedBeforeCurrent(ReadOnlySpan<GridEventInfo> prefix)
    {
        for (int i = 1; i < prefix.Length; i++)
        {
            if (prefix[i].ChangeSequence <= prefix[i - 1].ChangeSequence)
                return false;
        }
        return _head < 0 || prefix[prefix.Length - 1].ChangeSequence < _events[_head].ChangeSequence;
    }

    private void Unlink(int slot)
    {
        int previous = _previous[slot];
        int next = _next[slot];
        if (previous >= 0)
            _next[previous] = next;
        else
            _head = next;
        if (next >= 0)
            _previous[next] = previous;
        else
            _tail = previous;
    }

    private void ResetSlots()
    {
        _head = -1;
        _tail = -1;
        _count = 0;
        _scopeCount = 0;
        _topologyLifecycleCount = 0;
        _scopeTrackingAll = false;
        _overflowed = false;
        _overflowedTopologyLifecycle = false;
        _freeHead = _events.Length == 0 ? -1 : 0;
        for (int i = 0; i < _events.Length; i++)
        {
            _previous[i] = -1;
            _next[i] = i + 1 < _events.Length ? i + 1 : -1;
        }
    }

    private static bool IsTopologyLifecycle(in GridEventInfo eventInfo) =>
        eventInfo.ChangeKind == GridEventKind.GridAdded
        || eventInfo.ChangeKind == GridEventKind.GridRemoved
        || eventInfo.ChangeKind == GridEventKind.WorldReset;

    private static int GetIndexCapacity(int capacity)
    {
        int requested = checked(((capacity * 100) / 82) + 1);
        int result = SwiftDictionary<EventKey, int>.DefaultCapacity;
        while (result < requested)
            result = checked(result * 2);
        return result;
    }

    private static int AlignToEight(int value) => (value + 7) & ~7;

    private void TrackScope(in GridEventInfo eventInfo)
    {
        if (_scopeTrackingAll)
            return;
        if (eventInfo.ChangeKind == GridEventKind.WorldReset)
        {
            _scopeTrackingAll = true;
            return;
        }

        var scope = new NavigationGridChangeScope(eventInfo);
        for (int i = 0; i < _scopeCount; i++)
        {
            if (!_scopes[i].Scope.Equals(scope))
                continue;
            _scopes[i].Scope = scope;
            _scopes[i].Count++;
            return;
        }
        if (_scopeCount == _scopes.Length)
        {
            _scopeTrackingAll = true;
            return;
        }
        _scopes[_scopeCount++] = new ScopeEntry(scope, 1);
    }

    private void UntrackScope(in GridEventInfo eventInfo)
    {
        if (_scopeTrackingAll || eventInfo.ChangeKind == GridEventKind.WorldReset)
            return;
        var scope = new NavigationGridChangeScope(eventInfo);
        for (int i = 0; i < _scopeCount; i++)
        {
            if (!_scopes[i].Scope.Equals(scope))
                continue;
            if (--_scopes[i].Count > 0)
                return;
            _scopes[i] = _scopes[--_scopeCount];
            _scopes[_scopeCount] = default;
            return;
        }
    }

    private int CopyBlockedScopes(
        Span<NavigationGridChangeScope> destination,
        out bool blockAll)
    {
        blockAll = _scopeTrackingAll || _scopeCount > destination.Length;
        if (blockAll)
            return 0;
        for (int i = 0; i < _scopeCount; i++)
            destination[i] = _scopes[i].Scope;
        return _scopeCount;
    }

    private struct ScopeEntry
    {
        internal ScopeEntry(NavigationGridChangeScope scope, int count)
        {
            Scope = scope;
            Count = count;
        }

        internal NavigationGridChangeScope Scope;
        internal int Count;
    }

    private readonly struct EventKey : IEquatable<EventKey>
    {
        private EventKey(
            long worldSpawnToken,
            ushort gridIndex,
            long gridSpawnToken,
            VoxelIndex index)
        {
            WorldSpawnToken = worldSpawnToken;
            GridIndex = gridIndex;
            GridSpawnToken = gridSpawnToken;
            Index = index;
            IsValid = true;
        }

        private long WorldSpawnToken { get; }
        private ushort GridIndex { get; }
        private long GridSpawnToken { get; }
        private VoxelIndex Index { get; }
        internal bool IsValid { get; }

        internal static bool TryCreate(GridEventInfo eventInfo, out EventKey key)
        {
            if (!eventInfo.HasVoxelState)
            {
                key = default;
                return false;
            }
            key = new EventKey(
                eventInfo.WorldSpawnToken,
                eventInfo.GridIndex,
                eventInfo.GridSpawnToken,
                eventInfo.VoxelIndex);
            return true;
        }

        public bool Equals(EventKey other) =>
            WorldSpawnToken == other.WorldSpawnToken
            && GridIndex == other.GridIndex
            && GridSpawnToken == other.GridSpawnToken
            && Index.Equals(other.Index);

        public override bool Equals(object? obj) => obj is EventKey other && Equals(other);

        public override int GetHashCode()
        {
            int hash = SwiftHashTools.CombineHashCodes(
                WorldSpawnToken.GetHashCode(),
                GridSpawnToken.GetHashCode());
            hash = SwiftHashTools.CombineHashCodes(hash, GridIndex.GetHashCode());
            return SwiftHashTools.CombineHashCodes(hash, Index.GetHashCode());
        }
    }
}

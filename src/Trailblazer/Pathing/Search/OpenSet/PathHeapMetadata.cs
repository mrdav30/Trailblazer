//=======================================================================
// PathHeapMetadata.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// Low-allocation metadata table for <see cref="PathHeap{TNode}"/>.
/// </summary>
internal sealed class PathHeapMetadata<TNode> where TNode : class
{
    private const double LoadFactorThreshold = 0.72;

    private static readonly EqualityComparer<TNode> _comparer = EqualityComparer<TNode>.Default;

    private readonly int _minimumCapacity;

    private TNode?[] _keys = null!;

    private PathHeapMeta[] _values = null!;

    private int[] _hashes = null!;

    private int[] _occupiedSlots = null!;

    private int _mask;

    private int _resizeThreshold;

    private int _count;

    public PathHeapMetadata(int capacity)
    {
        _minimumCapacity = NormalizeCapacity(capacity);
        Initialize(_minimumCapacity);
    }

    public int Count => _count;

    public PathHeapMeta this[TNode key]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (TryGetValue(key, out PathHeapMeta value))
                return value;

            throw new KeyNotFoundException("The requested path heap node is not tracked.");
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        set => Set(key, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetValue(TNode key, out PathHeapMeta value)
    {
        if (key == null)
        {
            value = default;
            return false;
        }

        int hash = GetStoredHash(key);
        int slot = FindSlot(key, hash);
        if (slot < 0)
        {
            value = default;
            return false;
        }

        value = _values[slot];
        return true;
    }

    public void Set(TNode key, PathHeapMeta value)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        int hash = GetStoredHash(key);
        int existingSlot = FindSlot(key, hash);
        if (existingSlot >= 0)
        {
            _values[existingSlot] = value;
            return;
        }

        if (_count >= _resizeThreshold)
            Resize(_keys.Length * 2);

        InsertNew(key, hash, value);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ClosedEnumerable EnumerateClosed(uint heapVersion) => new(this, heapVersion);

    public void Clear()
    {
        for (int i = 0; i < _count; i++)
        {
            int slot = _occupiedSlots[i];
            _keys[slot] = null;
            _values[slot] = default;
            _hashes[slot] = 0;
            _occupiedSlots[i] = 0;
        }

        _count = 0;
    }

    public void EnsureCapacity(int capacity)
    {
        int normalized = NormalizeCapacity(capacity);
        if (normalized > _keys.Length)
            Resize(normalized);
    }

    public void TrimExcess()
    {
        int target = NormalizeCapacity(Math.Max(_minimumCapacity, _count));
        if (target < _keys.Length)
            Resize(target);
    }

    private void Initialize(int capacity)
    {
        _keys = new TNode?[capacity];
        _values = new PathHeapMeta[capacity];
        _hashes = new int[capacity];
        _occupiedSlots = new int[capacity];
        _mask = capacity - 1;
        _resizeThreshold = Math.Max(1, (int)(capacity * LoadFactorThreshold));
        _count = 0;
    }

    private void Resize(int capacity)
    {
        TNode?[] oldKeys = _keys;
        PathHeapMeta[] oldValues = _values;
        int[] oldHashes = _hashes;
        int[] oldOccupiedSlots = _occupiedSlots;
        int oldCount = _count;

        Initialize(NormalizeCapacity(capacity));

        for (int i = 0; i < oldCount; i++)
        {
            int oldSlot = oldOccupiedSlots[i];
            TNode? key = oldKeys[oldSlot];
            if (key != null)
                InsertNew(key, oldHashes[oldSlot], oldValues[oldSlot]);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void InsertNew(TNode key, int hash, PathHeapMeta value)
    {
        int slot = hash & _mask;
        while (_keys[slot] != null)
            slot = (slot + 1) & _mask;

        _keys[slot] = key;
        _hashes[slot] = hash;
        _values[slot] = value;
        _occupiedSlots[_count++] = slot;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int FindSlot(TNode key, int hash)
    {
        int slot = hash & _mask;

        while (true)
        {
            TNode? candidate = _keys[slot];
            if (candidate == null)
                return -1;

            if (_hashes[slot] == hash && _comparer.Equals(candidate, key))
                return slot;

            slot = (slot + 1) & _mask;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int GetStoredHash(TNode key)
    {
        unchecked
        {
            uint hash = (uint)_comparer.GetHashCode(key);
            hash ^= hash >> 16;
            hash *= 0x7feb352d;
            hash ^= hash >> 15;
            hash *= 0x846ca68b;
            hash ^= hash >> 16;
            return (int)(hash & 0x7FFFFFFF);
        }
    }

    private static int NormalizeCapacity(int capacity)
    {
        int normalized = 1;
        while (normalized < capacity)
            normalized <<= 1;

        return Math.Max(2, normalized);
    }

    internal readonly struct ClosedEnumerable : IEnumerable<TNode>
    {
        private readonly PathHeapMetadata<TNode> _metadata;

        private readonly uint _heapVersion;

        public ClosedEnumerable(PathHeapMetadata<TNode> metadata, uint heapVersion)
        {
            _metadata = metadata;
            _heapVersion = heapVersion;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Enumerator GetEnumerator() => new(_metadata, _heapVersion);

        IEnumerator<TNode> IEnumerable<TNode>.GetEnumerator() => GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    internal struct Enumerator : IEnumerator<TNode>
    {
        private readonly PathHeapMetadata<TNode> _metadata;

        private readonly uint _heapVersion;

        private int _index;

        private TNode? _current;

        public Enumerator(PathHeapMetadata<TNode> metadata, uint heapVersion)
        {
            _metadata = metadata;
            _heapVersion = heapVersion;
            _index = 0;
            _current = null;
        }

        public readonly TNode Current => _current!;

        readonly object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            while (_index < _metadata._count)
            {
                int slot = _metadata._occupiedSlots[_index++];
                if (_metadata._values[slot].ClosedHeapVersion != _heapVersion)
                    continue;

                TNode? key = _metadata._keys[slot];
                if (key == null)
                    continue;

                _current = key;
                return true;
            }

            _current = null;
            return false;
        }

        public void Reset()
        {
            _index = 0;
            _current = null;
        }

        public readonly void Dispose()
        {
        }
    }
}

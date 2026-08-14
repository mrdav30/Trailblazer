//=======================================================================
// NavigationPagedSequence.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Stores an immutable ordered sequence in fixed-size allocation pages.</summary>
internal sealed class NavigationPagedSequence<T>
{
    private const int PageCapacity = 8;
    private const long WrapperBytes = 32L;
    private const long BuilderBytes = 40L;
    private const long PageAndArrayHeadersBytes = 56L;

    internal static NavigationPagedSequence<T> Empty { get; } = new(null, 0, 0, 0);

    private readonly Page? _head;
    private readonly int _pageCount;
    private readonly int _elementBytes;

    private NavigationPagedSequence(Page? head, int count, int pageCount, int elementBytes)
    {
        _head = head;
        Count = count;
        _pageCount = pageCount;
        _elementBytes = elementBytes;
    }

    internal int Count { get; }

    internal long RetainedBytes => Count == 0
        ? 0L
        : checked(
            WrapperBytes
            + ((long)_pageCount
                * (PageAndArrayHeadersBytes + ((long)PageCapacity * _elementBytes))));

    internal int PersistentPageCount => Count == 0 ? 0 : checked(1 + (_pageCount * 2));

    internal T this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));
            Page page = _head!;
            while (index >= page.Count)
            {
                index -= page.Count;
                page = page.Next!;
            }
            return page.Values[index];
        }
    }

    internal Enumerator GetEnumerator() => new(_head, Count);

    internal struct Enumerator
    {
        private Page? _page;
        private int _pageIndex;
        private int _remaining;

        internal Enumerator(Page? page, int count)
        {
            _page = page;
            _pageIndex = 0;
            _remaining = count;
            Current = default!;
        }

        internal T Current { get; private set; }

        internal bool MoveNext()
        {
            if (_remaining == 0)
            {
                Current = default!;
                return false;
            }
            if (_pageIndex == _page!.Count)
            {
                _page = _page.Next;
                _pageIndex = 0;
            }
            Current = _page!.Values[_pageIndex++];
            _remaining--;
            return true;
        }
    }

    internal sealed class Builder
    {
        private readonly int _elementBytes;
        private Page? _head;
        private Page? _tail;
        private int _count;
        private int _pageCount;

        internal Builder(int elementBytes)
        {
            if (elementBytes <= 0)
                throw new ArgumentOutOfRangeException(nameof(elementBytes));
            _elementBytes = elementBytes;
        }

        internal long RetainedBytes => checked(
            BuilderBytes
            + ((long)_pageCount
                * (PageAndArrayHeadersBytes + ((long)PageCapacity * _elementBytes))));

        internal int PersistentPageCount => checked(1 + (_pageCount * 2));

        internal void Append(T value)
        {
            if (_tail == null || _tail.Count == PageCapacity)
            {
                var page = new Page();
                if (_tail == null)
                    _head = page;
                else
                    _tail.Next = page;
                _tail = page;
                _pageCount++;
            }
            _tail.Values[_tail.Count++] = value;
            _count++;
        }

        internal NavigationPagedSequence<T> Seal() => _count == 0
            ? Empty
            : new NavigationPagedSequence<T>(_head, _count, _pageCount, _elementBytes);
    }

    internal sealed class Page
    {
        internal readonly T[] Values = new T[PageCapacity];
        internal Page? Next;
        internal int Count;
    }
}

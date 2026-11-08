//=======================================================================
// NavigationPagedSequence.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Stores an immutable ordered sequence in fixed-size allocation pages.</summary>
internal sealed partial class NavigationPagedSequence<T>
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
}

//=======================================================================
// NavigationPagedSequence.Builder.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <content>Contains the Builder companion type.</content>
internal sealed partial class NavigationPagedSequence<T>
{
    /// <summary>Builds a paged sequence while reusing eligible pages.</summary>
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
}

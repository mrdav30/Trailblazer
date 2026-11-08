//=======================================================================
// NavigationPagedSequence.Enumerator.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <content>Contains the Enumerator companion type.</content>
internal sealed partial class NavigationPagedSequence<T>
{
    /// <summary>Traverses the occupied entries of the paged sequence in order.</summary>
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
}

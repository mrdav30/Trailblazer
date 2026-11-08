//=======================================================================
// NavigationPagedSequence.Page.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <content>Contains the Page companion type.</content>
internal sealed partial class NavigationPagedSequence<T>
{
    /// <summary>Stores the entries for one page of the sequence.</summary>
    internal sealed class Page
    {
        internal readonly T[] Values = new T[PageCapacity];
        internal Page? Next;
        internal int Count;
    }
}

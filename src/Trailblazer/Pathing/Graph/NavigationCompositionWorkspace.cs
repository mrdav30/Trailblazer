//=======================================================================
// NavigationCompositionWorkspace.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Owns the fixed structural-composition scratch reserved by one world context.</summary>
internal sealed class NavigationCompositionWorkspace
{
    private const long BaseRetainedBytes = 144L;

    internal NavigationCompositionWorkspace(int capacity)
    {
        if (capacity <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacity));
        DomainQueue = new string[capacity];
        BuildQueue = new string[capacity];
        RootKeys = new string[capacity];
        Domain = new NavigationStringStampSet(capacity);
        RootKeySet = new NavigationStringStampSet(capacity);
        BuildVisited = new NavigationStringStampSet(capacity);
    }

    internal string[] DomainQueue { get; }

    internal string[] BuildQueue { get; }

    internal string[] RootKeys { get; }

    internal NavigationStringStampSet Domain { get; }

    internal NavigationStringStampSet RootKeySet { get; }

    internal NavigationStringStampSet BuildVisited { get; }

    internal long RetainedBytes => checked(
        BaseRetainedBytes
        + ((long)DomainQueue.Length * 3L * IntPtr.Size)
        + Domain.RetainedBytes
        + RootKeySet.RetainedBytes
        + BuildVisited.RetainedBytes);

    internal static long GetRetainedBytes(int capacity) => checked(
        BaseRetainedBytes
        + ((long)capacity * 3L * IntPtr.Size)
        + (NavigationStringStampSet.GetRetainedBytes(capacity) * 3L));

    internal void Reset()
    {
        Domain.Reset();
        RootKeySet.Reset();
        BuildVisited.Reset();
    }
}

//=======================================================================
// PathQueryBatch.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Pairs one query with its caller-owned stable operation ordinal.</summary>
internal readonly struct PathQueryBatchItem
{
    internal const long LogicalRetainedBytes = 248L;

    internal PathQueryBatchItem(long stableOrdinal, PathQuery query)
    {
        StableOrdinal = stableOrdinal;
        Query = query;
    }

    internal long StableOrdinal { get; }

    internal PathQuery Query { get; }
}

/// <summary>Describes a caller-owned prefix of immutable path queries.</summary>
internal readonly struct PathQueryBatch
{
    internal PathQueryBatch(PathQueryBatchItem[] items, int count)
    {
        SwiftThrowHelper.ThrowIfNull(items, nameof(items));
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            count < 0 || count > items.Length,
            count,
            nameof(count));
        Items = items;
        Count = count;
    }

    internal PathQueryBatchItem[] Items { get; }

    internal int Count { get; }
}

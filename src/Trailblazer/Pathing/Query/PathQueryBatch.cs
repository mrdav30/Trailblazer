//=======================================================================
// PathQueryBatch.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Describes a caller-owned prefix of immutable path queries.</summary>
internal readonly struct PathQueryBatch
{
    internal PathQueryBatch(PathQueryBatchItem[] items, int count)
    {
        ChronicleGuard.ThrowIfNull(items, nameof(items));
        ChronicleGuard.ThrowIfArgumentOutOfRange(
            count < 0 || count > items.Length,
            count,
            nameof(count));
        Items = items;
        Count = count;
    }

    internal PathQueryBatchItem[] Items { get; }

    internal int Count { get; }

    internal long GetLogicalRetainedBytes()
    {
        long retainedBytes = 0;
        for (int i = 0; i < Count; i++)
        {
            retainedBytes += PathQueryBatchItem.GetLogicalRetainedBytes(
                Items[i].Query);
        }
        return retainedBytes;
    }
}

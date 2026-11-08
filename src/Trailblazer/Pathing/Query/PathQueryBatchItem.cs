//=======================================================================
// PathQueryBatchItem.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

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

    internal static long GetLogicalRetainedBytes(PathQuery query)
    {
        long retainedBytes = LogicalRetainedBytes;
        retainedBytes = AddStringPayload(retainedBytes, query.Start.MapId);
        retainedBytes = AddStringPayload(retainedBytes, query.End.MapId);
        return AddStringPayload(retainedBytes, query.AreaPolicy.PolicyId);
    }

    private static long AddStringPayload(long bytes, string? value) =>
        value == null
            ? bytes
            : bytes + ((long)value.Length * sizeof(char));
}

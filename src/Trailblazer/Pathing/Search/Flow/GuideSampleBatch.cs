//=======================================================================
// GuideSampleBatch.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Describes one stable-ordered internal flow guide sample.</summary>
internal readonly struct GuideSampleBatchItem
{
    internal GuideSampleBatchItem(
        long stableOrdinal,
        NavigationFlowFieldLease lease,
        Vector3d actualFootPosition)
    {
        StableOrdinal = stableOrdinal;
        Lease = lease;
        ActualFootPosition = actualFootPosition;
    }

    internal long StableOrdinal { get; }
    internal NavigationFlowFieldLease Lease { get; }
    internal Vector3d ActualFootPosition { get; }
}

/// <summary>Stores one internal batch sample result at its input ordinal.</summary>
internal readonly struct GuideSampleBatchResult
{
    internal GuideSampleBatchResult(
        NavigationGuideStatus status,
        Vector3d heading)
    {
        Status = status;
        Heading = heading;
    }

    internal NavigationGuideStatus Status { get; }
    internal Vector3d Heading { get; }
}

/// <summary>Samples caller-owned items in stable ordinal order with one shared meter.</summary>
internal static class GuideSampleBatch
{
    internal static void Sample(
        ReadOnlySpan<GuideSampleBatchItem> items,
        Span<GuideSampleBatchResult> results,
        GuideSampleWorkBudget budget)
    {
        if (results.Length < items.Length)
            throw new ArgumentException("The result span is smaller than the item span.", nameof(results));
        for (int i = 1; i < items.Length; i++)
        {
            if (items[i].StableOrdinal < items[i - 1].StableOrdinal)
            {
                throw new ArgumentException(
                    "Batch items must already be in canonical stable-ordinal order.",
                    nameof(items));
            }
        }

        var meter = new GuideSampleWorkMeter(budget);
        for (int inputIndex = 0; inputIndex < items.Length; inputIndex++)
        {
            GuideSampleBatchItem item = items[inputIndex];
            NavigationGuideStatus status = item.Lease.TrySample(
                item.ActualFootPosition,
                ref meter,
                out Vector3d heading);
            results[inputIndex] = new GuideSampleBatchResult(status, heading);
        }
    }
}

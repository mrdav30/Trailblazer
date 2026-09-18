using System;
using System.Runtime;

namespace Trailblazer.Tests;

internal static class AllocationTestUtility
{
    // Process-wide headroom, not an allowance for the measured thread: callers still
    // assert the exact byte count. Test parallelization is disabled for this assembly.
    private const long NoGCRegionHeadroom = 16 * 1024 * 1024;

    public static long MeasureAllocatedBytes(Action action)
    {
        // .NET 8's failed native admission also clears an existing region's state.
        // Reject nesting before admission so that the outer owner can still end it.
        if (GCSettings.LatencyMode == GCLatencyMode.NoGCRegion)
            throw new InvalidOperationException("Allocation measurements cannot nest no-GC regions.");

        // Finish any pending background collection before no-GC admission: .NET 8
        // can otherwise return successful admission with an already inactive region.
        // This setup is outside the counter interval and does not run measured work.
        GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: false);

        // Background GC can retire an allocation context without compensating this
        // thread's byte counter. Require the entire interval to remain GC-free.
        // Admission is outside try: a rejected nested call must not end its owner.
        if (!GC.TryStartNoGCRegion(NoGCRegionHeadroom, disallowFullBlockingGC: false))
            throw new InvalidOperationException("Could not start the allocation measurement's no-GC region.");

        try
        {
            if (GCSettings.LatencyMode != GCLatencyMode.NoGCRegion)
                throw new InvalidOperationException("Allocation measurement requires an active no-GC region.");

            long before = GC.GetAllocatedBytesForCurrentThread();
            action();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        finally
        {
            // Do not suppress failure: collection or exhausted headroom invalidates
            // the measurement, even if the observed byte delta happened to be zero.
            GC.EndNoGCRegion();
        }
    }
}

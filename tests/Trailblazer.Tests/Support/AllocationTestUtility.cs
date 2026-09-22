using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Tests;

internal static class AllocationTestUtility
{
    // The test host disables background GC to keep the per-thread counter reliable.
    // Callers own warmup: measured work may consume state and must run exactly once.
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static long MeasureAllocatedBytes(Action action)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        action();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}

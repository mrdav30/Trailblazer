using System;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks;

/// <summary>Validates benchmark guide ownership before measurement.</summary>
internal static class BenchmarkPreflight
{
    public static void AssertNoCacheLeak(TrailblazerWorldContext context)
    {
        if (context.Guides.AnyInUse)
        {
            throw new InvalidOperationException(
                "Preflight: One or more guides remain checked out after preflight.");
        }
    }
}

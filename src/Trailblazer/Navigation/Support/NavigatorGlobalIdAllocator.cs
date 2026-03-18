using System;
using System.Threading;

namespace Trailblazer.Navigation;

/// <summary>
/// Allocates deterministic navigator ids for the current Trailblazer runtime session.
/// </summary>
internal static class NavigatorGlobalIdAllocator
{
    private static long _nextId;

    internal static void RegisterTrailblazerLifecycleHooks()
    {
        TrailblazerManager.RegisterOnResetCore(
            owner: "NavigatorGlobalIdAllocator.Reset",
            order: TrailblazerLifecycleOrder.NavigationIdentityReset,
            callback: Reset);
    }

    internal static Guid Create()
    {
        long next = Interlocked.Increment(ref _nextId);
        return new Guid(
            unchecked((int)next),
            unchecked((short)(next >> 32)),
            unchecked((short)(next >> 48)),
            (byte)'T',
            (byte)'R',
            (byte)'A',
            (byte)'I',
            (byte)'L',
            (byte)'B',
            (byte)'L',
            (byte)'Z');
    }

    internal static void Reset()
    {
        Interlocked.Exchange(ref _nextId, 0);
    }
}

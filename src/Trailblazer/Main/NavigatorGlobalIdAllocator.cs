using System;

namespace Trailblazer;

/// <summary>
/// Allocates deterministic object ids for the current Trailblazer runtime session.
/// </summary>
internal static class NavigatorGlobalIdAllocator
{
    private static readonly NavigatorGlobalIdAllocatorState FallbackState = new();

    internal static void RegisterTrailblazerLifecycleHooks()
    {
        TrailblazerManager.RegisterOnResetCore(
            owner: "NavigatorGlobalIdAllocator.Reset",
            order: TrailblazerLifecycleOrder.NavigationIdentityReset,
            callback: Reset);
    }

    internal static Guid Create()
    {
        return TrailblazerManager.HasDefaultContext
            ? TrailblazerManager.DefaultContext.Navigation.CreateNavigatorId()
            : FallbackState.Create();
    }

    internal static Guid Create(TrailblazerWorldContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));

        return context.Navigation.CreateNavigatorId();
    }

    internal static void Reset()
    {
        FallbackState.Reset();
        if (TrailblazerManager.HasDefaultContext)
            TrailblazerManager.DefaultContext.Navigation.NavigatorIds.Reset();
    }
}

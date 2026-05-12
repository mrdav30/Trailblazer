using System;

namespace Trailblazer.Pathing;

internal static class PathRequestContextResolver
{
    internal static TrailblazerWorldContext DefaultContext => PathManager.ActiveState.Context;

    internal static void ThrowIfUnusable(TrailblazerWorldContext context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context));
        if (context.IsDisposed)
            throw new ObjectDisposedException(nameof(TrailblazerWorldContext));
        if (!context.World.IsActive)
            throw new InvalidOperationException("Path requests require an active TrailblazerWorldContext.");
    }
}

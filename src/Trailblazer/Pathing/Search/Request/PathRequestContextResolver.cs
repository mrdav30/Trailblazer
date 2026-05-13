using System;

namespace Trailblazer.Pathing;

internal static class PathRequestContextResolver
{
    internal static void ThrowIfUnusable(TrailblazerWorldContext? context)
    {
        if (context == null)
            throw new ArgumentNullException(nameof(context), "Path requests require an explicit TrailblazerWorldContext.");
        if (context.IsDisposed)
            throw new ObjectDisposedException(nameof(TrailblazerWorldContext));
        if (!context.World.IsActive)
            throw new InvalidOperationException("Path requests require an active TrailblazerWorldContext.");
    }
}

using System;
using SwiftCollections;
using Trailblazer.Navigation.MovementGroups;

namespace Trailblazer.Navigation;

/// <summary>
/// Context-owned navigation coordination state for navigators, steering, movement groups, and ids.
/// </summary>
public sealed class TrailblazerNavigationService
{
    private readonly TrailblazerWorldContext _context;

    internal TrailblazerNavigationService(TrailblazerWorldContext context)
    {
        _context = context;
        MovementGroups = new MovementGroupCoordinatorState(context);
        NavigatorIds = new NavigatorGlobalIdAllocatorState();
    }

    /// <summary>
    /// Gets the world context that owns this navigation service.
    /// </summary>
    public TrailblazerWorldContext Context => _context;

    internal MovementGroupCoordinatorState MovementGroups { get; }

    internal NavigatorGlobalIdAllocatorState NavigatorIds { get; }

    /// <summary>
    /// Binds an uninitialized navigator to this context.
    /// </summary>
    public void Bind(Navigator navigator)
    {
        SwiftThrowHelper.ThrowIfNull(navigator, nameof(navigator));
        navigator.BindContext(_context);
    }

    internal Guid CreateNavigatorId()
    {
        EnsureUsable();
        return NavigatorIds.Create();
    }

    internal void Reset()
    {
        MovementGroups.Reset();
        NavigatorIds.Reset();
    }

    private void EnsureUsable()
    {
        SwiftThrowHelper.ThrowIfDisposed(_context.IsDisposed, nameof(TrailblazerWorldContext));
        if (!_context.World.IsActive)
            throw new InvalidOperationException("TrailblazerNavigationService is bound to an inactive GridWorld.");
    }
}

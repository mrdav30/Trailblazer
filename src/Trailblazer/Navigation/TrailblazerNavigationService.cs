//=======================================================================
// TrailblazerNavigationService.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using Trailblazer.Navigation.MovementGroups;

namespace Trailblazer.Navigation;

/// <summary>
/// Context-owned navigation coordination state for navigators, steering, movement groups, and ids.
/// </summary>
internal sealed class TrailblazerNavigationService
{
    private readonly TrailblazerWorldContext _context;

    internal TrailblazerNavigationService(TrailblazerWorldContext context)
    {
        _context = context;
        MovementGroups = new MovementGroupCoordinatorState(context);
        NavigatorIds = new NavigatorGlobalIdAllocatorState();
    }

    internal MovementGroupCoordinatorState MovementGroups { get; }

    internal NavigatorGlobalIdAllocatorState NavigatorIds { get; }

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

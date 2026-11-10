//=======================================================================
// TrailblazerNavigationService.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using SwiftCollections;
using Trailblazer.Navigation.MovementGroups;

namespace Trailblazer.Navigation;

/// <summary>
/// Context-owned navigation coordination state for navigators, steering, movement groups, and ids.
/// </summary>
internal sealed class TrailblazerNavigationService
{
    private SwiftDictionary<string, MovementGroupCoordinatorState>? _planarMovementGroups;

    internal TrailblazerNavigationService(TrailblazerWorldContext context)
    {
        MovementGroups = new MovementGroupCoordinatorState(context);
        NavigatorIds = new NavigatorGlobalIdAllocatorState();
    }

    internal MovementGroupCoordinatorState MovementGroups { get; }

    internal NavigatorGlobalIdAllocatorState NavigatorIds { get; }

    internal Guid CreateNavigatorId() => NavigatorIds.Create();

    internal MovementGroupCoordinatorState GetPlanarMovementGroups(TrailblazerWorldContext context, string mapId)
    {
        _planarMovementGroups ??= new();
        if (!_planarMovementGroups.TryGetValue(mapId, out var groups))
        {
            groups = new MovementGroupCoordinatorState(context);
            _planarMovementGroups[mapId] = groups;
        }
        return groups;
    }

    internal void ReleasePlanarMovementGroup(string mapId, MovementGroupCoordinatorState owner, MovementGroupSession session)
    {
        owner.Remove(session);
        if (owner.IsEmpty && _planarMovementGroups!.TryGetValue(mapId, out var current) && ReferenceEquals(owner, current))
            _planarMovementGroups.Remove(mapId);
    }

    internal void Reset()
    {
        MovementGroups.Reset();
        _planarMovementGroups?.Clear();
        NavigatorIds.Reset();
    }
}

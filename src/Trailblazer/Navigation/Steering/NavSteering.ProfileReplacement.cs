//=======================================================================
// NavSteering.ProfileReplacement.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation.Steering;

public partial class NavSteering
{
    internal bool TryPrepareProfileReplacement(
        NavigationAgentProfile replacement,
        Vector3d footPosition,
        TraversalMedium currentMedium,
        string? currentMapId,
        out PathQuery? replacementQuery)
    {
        if (!_currentQuery.HasValue)
        {
            replacementQuery = null;
            return true;
        }

        PathQuery current = _currentQuery.Value;
        if ((current.Traversal.TargetMedia & ~replacement.AllowedMedia) != 0)
        {
            replacementQuery = null;
            return false;
        }

        replacementQuery = current.WithAgentAndStartState(
            replacement,
            footPosition,
            currentMedium,
            currentMapId ?? current.Start.MapId);
        return true;
    }

    internal void CommitProfileReplacement(
        PathQuery? replacementQuery,
        Vector3d rootPosition,
        Fixed64 radius)
    {
        if (_currentQuery.HasValue)
        {
            ReleaseNavigationGuidance();
            _currentQuery = replacementQuery;
            _pendingCommittedAreaPolicy = null;
            _hasLineOfSightPath = false;
            _pathCheckCooldown = 0;
            _shouldRequestPathThisFrame = _shouldMove;
        }

        if (IsInGroup)
            UpdateMovementGroupState(rootPosition, radius);
    }
}

//=======================================================================
// Navigator2D.Groups.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using FixedMathSharp;
using Trailblazer.Navigation.MovementGroups;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <content>Resolves formation goals using the shared group coordinator.</content>
public abstract partial class Navigator2D
{
    private MovementGroupCoordinatorState? _groupCoordinator;
    private MovementGroupSession? _groupSession;
    private bool _formationGoalChosen;

    /// <summary>Gets the current movement group ID, or -1 outside a group.</summary>
    public int MovementGroupId => _groupSession?.GroupId ?? -1;

    private void JoinGroup(int groupId)
    {
        if (groupId < 0 || _requestedGoal.MapId == null)
            return;
        _groupSession ??= new MovementGroupSession();
        _groupSession.GroupId = groupId;
        _groupCoordinator = _context.Navigation.GetPlanarMovementGroups(_context, _requestedGoal.MapId);
    }

    private bool RefreshGroupTarget()
    {
        if (_groupCoordinator == null
            || !_context.Pathing.TryGetTopDownCenter(_requestedGoal, _binding, out Vector2d destination))
            return false;
        MovementGroupTarget groupTarget = _groupCoordinator.UpdateTarget(_groupSession!,
            new Vector3d(destination.X, _binding.EmbeddingY, destination.Y),
            ((GridForge.Spatial.IVoxelOccupant)this).Position, NavigationProfile.Radius);
        if (_formationGoalChosen || groupTarget.TravelMode != MovementGroupTravelMode.Formation)
            return false;
        var status = _context.Pathing.ResolveTopDownEndpoint(_requestedGoal.MapId, groupTarget.Destination.ToVector2d(),
            NavigationProfile.Radius, _policy, _medium, NavigationProfile.Capabilities, _budget, out var address);
        if (status != NavigationGuideStatus.Success)
            return false;
        _formationGoalChosen = true;
        if (address == _routeGoal)
            return false;
        _routeGoal = address;
        return true;
    }

    private void LeaveGroup()
    {
        if (_groupCoordinator != null)
        {
            _context.Navigation.ReleasePlanarMovementGroup(_requestedGoal.MapId, _groupCoordinator, _groupSession!);
            _groupSession!.Reset();
            _groupCoordinator = null;
        }
        _formationGoalChosen = false;
    }
}

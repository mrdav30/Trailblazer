//=======================================================================
// NavSteering.Serialization.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using Chronicler;
using FixedMathSharp;
using Trailblazer.Navigation.MovementGroups;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation.Steering;

public partial class NavSteering
{
    #region Serialization

    /// <inheritdoc />
    public virtual void RecordData(IChronicler chronicler)
    {
        var requestRecord = new PathRequestRecord();
        var queryRecord = new PathQueryRecord();
        if (chronicler.Mode == SerializationMode.Saving)
        {
            if (_currentQuery.HasValue)
                queryRecord.Capture(_currentQuery, _navigationGuideLease);
            else
                requestRecord.Capture(_currentRequest, _trailGuide);
        }

        int movementGroupId = _movementGroupSession.GroupId;

        RecordValues.Look(chronicler, ref CanPathfind, "CanPathfind", true);
        RecordValues.Look(chronicler, ref _destination, "Destination", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _requestedDestination, "RequestedDestination", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _lastUnitSize, "LastUnitSize", Fixed64.Zero);
        RecordValues.Look(chronicler, ref PathRecheckCooldownFrames, "PathRecheckCooldownFrames", DefaultPathRecheckCooldown);
        RecordValues.Look(chronicler, ref _targetDirection, "TargetDirection", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _lastTargetDirection, "LastTargetDirection", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _shouldMove, "ShouldMove", false);
        RecordValues.Look(chronicler, ref _isStuck, "IsStuck", false);
        RecordValues.Look(chronicler, ref _hasLineOfSightPath, "HasLineOfSightPath", false);
        RecordValues.Look(chronicler, ref _currentRouteHasResolvedTopology, "CurrentRouteHasResolvedTopology", false);
        RecordValues.Look(chronicler, ref _currentRouteUsesGuideTopology, "CurrentRouteUsesGuideTopology", false);
        RecordValues.Look(chronicler, ref _currentRouteRequestsClimbIntent, "CurrentRouteRequestsClimbIntent", false);
        RecordValues.Look(chronicler, ref _currentRouteTopologyVersion, "CurrentRouteTopologyVersion", 0);
        RecordValues.Look(chronicler, ref _shouldRequestPathThisFrame, "ShouldRequestPathThisFrame", false);
        RecordValues.Look(chronicler, ref _pathCheckCooldown, "PathCheckCooldown", 0);
        RecordValues.Look(chronicler, ref _distanceToTarget, "DistanceToTarget", Fixed64.Zero);
        RecordValues.Look(chronicler, ref _isAtDestination, "IsAtDestination", false);
        RecordValues.Look(chronicler, ref CanMove, "CanMove", false);
        RecordValues.Look(chronicler, ref _stoppedFrameCount, "StoppedFrameCount", 0);
        RecordValues.Look(chronicler, ref _autoStopFrameCount, "AutoStopFrameCount", 0);
        RecordValues.Look(chronicler, ref _repathTries, "RepathTries", 0);
        RecordValues.Look(chronicler, ref _stuckFrameCount, "StuckFrameCount", 0);
        RecordValues.Look(chronicler, ref StopMultiplier, "StopMultiplier", DefaultDirectStop);
        RecordValues.Look(chronicler, ref GroupFactor, "GroupFactor", DefaultGroupFactor);
        RecordValues.Look(chronicler, ref AvoidFactor, "AvoidFactor", DefaultAvoidFactor);
        RecordValues.Look(chronicler, ref BehaviorWeights, "BehaviorWeights", DefaultBehaviorWeights);
        RecordValues.Look(chronicler, ref BrakingPower, "BrakingPower", DefaultBrakingPower);
        RecordValues.Look(chronicler, ref movementGroupId, "MovementGroupId", 0);
        RecordValues.Look(chronicler, ref _movementGroupMode, "MovementGroupMode", MovementGroupTravelMode.None);
        RecordDeep.Look(chronicler, ref queryRecord, "PathQuery");
        RecordDeep.Look(chronicler, ref requestRecord, "PathRequest");

        if (chronicler.Mode == SerializationMode.Loading)
        {
            ReleaseNavigationGuidance();
            ResetMovementGroupSession();

            _currentRequest = null;
            _currentQuery = null;
            bool restored = queryRecord.TryCreateQuery(out PathQuery? query);
            IPathRequest? request = null;
            if (restored && query.HasValue)
                _currentQuery = query;
            else if (restored)
                restored = requestRecord.TryCreateRequest(ResolveContext(), out request);

            if (!restored)
            {
                _shouldMove = false;
                _isStuck = false;
                _hasLineOfSightPath = false;
                _shouldRequestPathThisFrame = false;
                _destination = Vector3d.Zero;
                _requestedDestination = Vector3d.Zero;
                _targetDirection = Vector3d.Zero;
                _lastTargetDirection = Vector3d.Zero;
                _distanceToTarget = Fixed64.Zero;
                _movementGroupMode = MovementGroupTravelMode.None;
            }
            else
            {
                _currentRequest = request;
            }

            MovementGroupID = movementGroupId;
            GroupIndex = -1;
            if (movementGroupId < 0)
                _movementGroupMode = MovementGroupTravelMode.None;

            if (_currentQuery.HasValue
                && ShouldMove
                && queryRecord.WaypointIndex >= 0
                && !_shouldRequestPathThisFrame
                && !HasLineOfSightPath)
            {
                if (!queryRecord.TryCreateGuide(
                    ResolveContext(),
                    _currentQuery.Value,
                    out _navigationGuideLease))
                {
                    _shouldRequestPathThisFrame = ShouldMove;
                }
            }
            else if (_currentQuery.HasValue
                && ShouldMove
                && !HasLineOfSightPath)
            {
                _shouldRequestPathThisFrame = true;
            }
            else if (_currentRequest != null
                && ShouldMove
                && requestRecord.HasGuide
                && !_shouldRequestPathThisFrame
                && !HasLineOfSightPath)
            {
                if (!requestRecord.TryCreateGuide(_currentRequest, out _trailGuide))
                    _shouldRequestPathThisFrame = ShouldMove;
            }
            else if (_currentRequest != null
                && ShouldMove
                && !HasLineOfSightPath)
            {
                _shouldRequestPathThisFrame = true;
            }
        }
    }

    internal void RestoreFlowQueryFromLoadedFoot(Vector3d footPosition)
    {
        if (_currentQuery is not PathQuery query
            || query.Algorithm != PathAlgorithm.FlowField)
        {
            return;
        }

        query = query.WithStartPosition(footPosition);
        _currentQuery = query;
        _requestedDestination = query.End.Position;
        ReleaseNavigationGuidance();
        if (!ShouldMove || HasLineOfSightPath)
            return;

        NavigationGuideStatus status = ResolveContext().Guides.RequestFlowField(
            query,
            out NavigationFlowFieldLease? lease);
        if (status == NavigationGuideStatus.Success && lease != null)
        {
            _navigationFlowFieldLease = lease;
            _shouldRequestPathThisFrame = false;
            PublishRouteTopology(
                hasResolvedTopology: true,
                usesGuideTopology: true,
                requestsClimbIntent: false);
            return;
        }

        lease?.Dispose();
        if (status is NavigationGuideStatus.Stale
            or NavigationGuideStatus.CapacityExceeded)
        {
            _shouldRequestPathThisFrame = true;
            return;
        }

        _shouldMove = false;
        _shouldRequestPathThisFrame = false;
        _currentQuery = null;
        PublishRouteTopology(
            hasResolvedTopology: false,
            usesGuideTopology: false,
            requestsClimbIntent: false);
    }

    private void ReleaseNavigationGuidance(bool dispose = false)
    {
        _navigationGuideLease?.Dispose();
        _navigationGuideLease = null;
        _navigationFlowFieldLease?.Dispose();
        _navigationFlowFieldLease = null;
        _flowRecoveryGuideLease?.Dispose();
        _flowRecoveryGuideLease = null;

        if (_trailGuide == null)
            return;

        (_currentRequest?.Context ?? ResolveContext()).Guides.ReturnGuide(_trailGuide, dispose);
        _trailGuide = null;
    }

    private void ResetMovementGroupSession()
    {
        RemoveMovementGroupSession();
        _movementGroupSession.Reset();
    }

    #endregion
}

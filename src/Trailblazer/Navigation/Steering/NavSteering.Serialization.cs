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
        int movementGroupId = _movementGroupSession.GroupId;
        int pathRecheckCooldownFrames = _pathRecheckCooldownFrames;
        int? pathRecheckPhase = _pathRecheckPhase;
        NavigationAcquisitionMode acquisitionMode = _acquisitionMode;

        RecordValues.Look(chronicler, ref pathRecheckCooldownFrames, "PathRecheckCooldownFrames", DefaultPathRecheckCooldown);
        RecordValues.Look(chronicler, ref pathRecheckPhase, "PathRecheckPhase", null);
        RecordValues.Look(chronicler, ref acquisitionMode, "AcquisitionMode", NavigationAcquisitionMode.Immediate);
        if (chronicler.Mode == SerializationMode.Loading)
        {
            ValidatePathRecheckConfiguration(pathRecheckCooldownFrames, pathRecheckPhase);
            ValidateAcquisitionMode(acquisitionMode);
        }

        RecordValues.Look(chronicler, ref CanPathfind, "CanPathfind", true);
        RecordValues.Look(chronicler, ref _destination, "Destination", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _requestedDestination, "RequestedDestination", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _targetDirection, "TargetDirection", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _lastTargetDirection, "LastTargetDirection", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _shouldMove, "ShouldMove", false);
        RecordValues.Look(chronicler, ref _isStuck, "IsStuck", false);
        RecordValues.Look(chronicler, ref _hasLineOfSightPath, "HasLineOfSightPath", false);
        RecordValues.Look(chronicler, ref _shouldRequestPathThisFrame, "ShouldRequestPathThisFrame", false);
        RecordValues.Look(chronicler, ref _pathCheckCooldown, "PathCheckCooldown", 0);
        RecordValues.Look(chronicler, ref _distanceToTarget, "DistanceToTarget", Fixed64.Zero);
        RecordValues.Look(chronicler, ref _isAtDestination, "IsAtDestination", false);
        RecordValues.Look(chronicler, ref CanMove, "CanMove", false);
        RecordValues.Look(chronicler, ref _stoppedFrameCount, "StoppedFrameCount", 0);
        RecordValues.Look(chronicler, ref _autoStopFrameCount, "AutoStopFrameCount", 0);
        RecordValues.Look(chronicler, ref _repathTries, "RepathTries", 0);
        RecordValues.Look(chronicler, ref _stuckFrameCount, "StuckFrameCount", 0);
        RecordValues.Look(chronicler, ref _waypointTolerance, "WaypointTolerance", DefaultWaypointTolerance);
        RecordValues.Look(chronicler, ref GroupFactor, "GroupFactor", DefaultGroupFactor);
        RecordValues.Look(chronicler, ref AvoidFactor, "AvoidFactor", DefaultAvoidFactor);
        RecordValues.Look(chronicler, ref BehaviorWeights, "BehaviorWeights", DefaultBehaviorWeights);
        RecordValues.Look(chronicler, ref BrakingPower, "BrakingPower", DefaultBrakingPower);
        RecordValues.Look(chronicler, ref movementGroupId, "MovementGroupId", 0);
        RecordValues.Look(chronicler, ref _movementGroupMode, "MovementGroupMode", MovementGroupTravelMode.None);
        if (chronicler.Mode == SerializationMode.Loading)
        {
            _pathRecheckCooldownFrames = pathRecheckCooldownFrames;
            _acquisitionMode = acquisitionMode;
            _pathRecheckPhase = pathRecheckPhase;
            ClearPhasedPathRecheckSchedule();
            if (_context != null)
            {
                ReleaseNavigationGuidance();
                ResetMovementGroupSession();
            }
            else
            {
                _movementGroupSession.Reset();
            }

            _currentQuery = null;
            if (_acquisitionState != null)
                _acquisitionState.GraphOnlyRetry = false;
            _pendingCommittedAreaPolicy = null;
            _shouldRequestPathThisFrame = false;
            MovementGroupID = movementGroupId;
            _movementGroupSession.GroupIndex = -1;
            if (movementGroupId < 0)
                _movementGroupMode = MovementGroupTravelMode.None;
        }
    }

    internal void RestoreQueryFromLoadedSession(PathQuery? query)
    {
        if (_acquisitionState != null)
            _acquisitionState.GraphOnlyRetry = false;
        _pendingCommittedAreaPolicy = null;
        _currentQuery = query;
        ReleaseNavigationGuidance();
        ClearPhasedPathRecheckSchedule();
        if (!query.HasValue)
            return;

        _requestedDestination = query.Value.End.Position;
        if (_pathRecheckPhase.HasValue || _acquisitionMode == NavigationAcquisitionMode.Scheduled)
        {
            _hasLineOfSightPath = false;
            _shouldRequestPathThisFrame = _shouldMove;
            _pathCheckCooldown = 0;
        }
        else if (!HasLineOfSightPath)
            _shouldRequestPathThisFrame = true;
    }

    private void ReleaseNavigationGuidance()
    {
        CancelAcquisition();
        _navigationGuideLease?.Dispose();
        _navigationGuideLease = null;
        _navigationFlowFieldLease?.Dispose();
        _navigationFlowFieldLease = null;
    }

    private void ResetMovementGroupSession()
    {
        RemoveMovementGroupSession();
        _movementGroupSession.Reset();
    }

    #endregion
}

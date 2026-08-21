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

        RecordValues.Look(chronicler, ref CanPathfind, "CanPathfind", true);
        RecordValues.Look(chronicler, ref _destination, "Destination", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _requestedDestination, "RequestedDestination", Vector3d.Zero);
        RecordValues.Look(chronicler, ref PathRecheckCooldownFrames, "PathRecheckCooldownFrames", DefaultPathRecheckCooldown);
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
        RecordValues.Look(chronicler, ref StopMultiplier, "StopMultiplier", DefaultDirectStop);
        RecordValues.Look(chronicler, ref GroupFactor, "GroupFactor", DefaultGroupFactor);
        RecordValues.Look(chronicler, ref AvoidFactor, "AvoidFactor", DefaultAvoidFactor);
        RecordValues.Look(chronicler, ref BehaviorWeights, "BehaviorWeights", DefaultBehaviorWeights);
        RecordValues.Look(chronicler, ref BrakingPower, "BrakingPower", DefaultBrakingPower);
        RecordValues.Look(chronicler, ref movementGroupId, "MovementGroupId", 0);
        RecordValues.Look(chronicler, ref _movementGroupMode, "MovementGroupMode", MovementGroupTravelMode.None);
        if (chronicler.Mode == SerializationMode.Loading)
        {
            ReleaseNavigationGuidance();
            ResetMovementGroupSession();

            _currentQuery = null;
            _shouldRequestPathThisFrame = false;
            MovementGroupID = movementGroupId;
            GroupIndex = -1;
            if (movementGroupId < 0)
                _movementGroupMode = MovementGroupTravelMode.None;
        }
    }

    internal void RestoreQueryFromLoadedSession(PathQuery? query)
    {
        _currentQuery = query;
        ReleaseNavigationGuidance();
        if (!query.HasValue)
            return;

        _requestedDestination = query.Value.End.Position;
        if (ShouldMove && !HasLineOfSightPath)
            _shouldRequestPathThisFrame = true;
    }

    private void ReleaseNavigationGuidance()
    {
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

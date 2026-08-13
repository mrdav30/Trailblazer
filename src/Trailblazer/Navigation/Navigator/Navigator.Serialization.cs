//=======================================================================
// Navigator.Serialization.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using Chronicler;
using FixedMathSharp;
using System;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

public abstract partial class Navigator
{
    #region Serialization

    /// <inheritdoc />
    public virtual void RecordData(IChronicler chronicler)
    {
        GuidedVolumeExitHandoff? pendingGuidedVolumeExitHandoff = _pendingGuidedVolumeExitHandoff;
        if (chronicler.Mode == SerializationMode.Loading && pendingGuidedVolumeExitHandoff == null)
            pendingGuidedVolumeExitHandoff = new GuidedVolumeExitHandoff();

        RecordValues.Look(chronicler, ref _position, "Position", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _lastPosition, "LastPosition", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _rotation, "Rotation", FixedQuaternion.Identity);
        RecordValues.Look(chronicler, ref _velocity, "Velocity", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _speed, "Speed", Fixed64.Zero);
        RecordValues.Look(chronicler, ref _acceleration, "Acceleration", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _size, "Size", Fixed64.One);
        RecordValues.Look(chronicler, ref _footPositionAdjust, "FootPositionAdjust", DefaultFootPositionAdjust);
        RecordValues.Look(chronicler, ref _guidedPathMode, "GuidedPathMode", SolidPathAlgorithm.AStar);
        RecordValues.Look(chronicler, ref _guidedAllowUnwalkableEndpoints, "GuidedAllowUnwalkableEndpoints", false);
        RecordValues.Look(chronicler, ref _guidedAllowTraversalTransitions, "GuidedAllowTraversalTransitions", false);
        RecordValues.Look(chronicler, ref _guidedMaxClimbHeight, "GuidedMaxClimbHeight", Fixed64.One);
        RecordValues.Look(chronicler, ref _guidedAStarHeuristic, "GuidedAStarHeuristic", HeuristicMethod.Manhattan);
        RecordValues.Look(chronicler, ref _guidedFlowFieldExtraFloodRange, "GuidedFlowFieldExtraFloodRange", FlowFieldPathRequest.DefaultExtraFloodRange);
        RecordValues.Look(chronicler, ref _globalId, "GlobalId", Guid.Empty);
        RecordValues.Look(chronicler, ref _occupantGroupId, "OccupantGroupId", (byte)1);
        RecordValues.Look(chronicler, ref _isLockedOn, "IsLockedOn", false);
        RecordValues.Look(chronicler, ref _stuckThresholdSpeed, "StuckThresholdSpeed", Fixed64.Zero);
        RecordValues.Look(chronicler, ref _isGuideded, "IsGuideded", false);
        RecordValues.Look(chronicler, ref _guidedClimbIntent, "GuidedClimbIntent", false);
        RecordValues.Look(chronicler, ref _guidedClimbIntentMode, "GuidedClimbIntentMode", GuidedClimbIntentMode.Auto);
        RecordValues.Look(chronicler, ref _lastSeenGuidedRouteTopologyVersion, "LastSeenGuidedRouteTopologyVersion", 0);
        RecordDeepStruct.Look(chronicler, ref _frameCondition, "FrameCondition");
        RecordDeepStruct.Look(chronicler, ref _frameRequest, "FrameRequest");
        RecordDeep.Look(chronicler, ref _heightmapGrounding, "HeightmapGrounding");
        RecordDeep.Look(chronicler, ref pendingGuidedVolumeExitHandoff!, "PendingGuidedVolumeExitHandoff");
        if (_steering != null)
            RecordDeep.Look(chronicler, ref _steering, "Steering");
        if (_turning != null)
            RecordDeep.Look(chronicler, ref _turning, "Turning");
        if (_motor != null)
            RecordDeep.Look(chronicler, ref _motor, "Motor");

        if (chronicler.Mode == SerializationMode.Loading)
        {
            TrailblazerWorldContext context = RequireContext();
            _pendingGuidedVolumeExitHandoff = pendingGuidedVolumeExitHandoff?.IsValid == true
                ? pendingGuidedVolumeExitHandoff
                : null;

            Forward = Rotation != FixedQuaternion.Identity
                ? Rotation.Rotate(Vector3d.Forward)
                : Vector3d.Forward;

            _positionDelta = Vector3d.Zero;
            _velocityDelta = Vector3d.Zero;
            _rotationDelta = FixedQuaternion.Identity;
            _heightmapGrounding ??= new NavigatorHeightmapGroundingSettings();
            _isSet = true;
            _isInitialized = Motor != null;

            _steering?.BindContext(context);
            _steering?.UpdateOwnerRadius(Radius);

            _motor?.BindContext(context);

            _turning?.BindContext(context);
            _turning?.OnInitialize(Radius);

            CheckVoxelOccupancy(true);
        }
    }

    #endregion
}

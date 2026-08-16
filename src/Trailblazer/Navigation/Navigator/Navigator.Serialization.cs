//=======================================================================
// Navigator.Serialization.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using Chronicler;
using FixedMathSharp;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

public abstract partial class Navigator
{
    private enum SerializedGuidedPathMode
    {
        Invalid = -1,
        Graph = 2
    }

    #region Serialization

    /// <inheritdoc />
    public virtual void RecordData(IChronicler chronicler)
    {
        var navigationProfileRecord = chronicler.Mode == SerializationMode.Loading
            ? new NavigationAgentProfileRecord()
            : new NavigationAgentProfileRecord(_navigationProfile);
        SerializedGuidedPathMode serializedGuidedPathMode = chronicler.Mode == SerializationMode.Loading
            ? SerializedGuidedPathMode.Invalid
            : SerializedGuidedPathMode.Graph;
        GuidedVolumeExitHandoff? pendingGuidedVolumeExitHandoff = _pendingGuidedVolumeExitHandoff;
        if (chronicler.Mode == SerializationMode.Loading && pendingGuidedVolumeExitHandoff == null)
            pendingGuidedVolumeExitHandoff = new GuidedVolumeExitHandoff();

        RecordDeep.Look(chronicler, ref navigationProfileRecord, "NavigationProfile");
        if (chronicler.Mode == SerializationMode.Loading
            && (!navigationProfileRecord.TryCreate(out NavigationAgentProfile recordedProfile)
                || recordedProfile != _navigationProfile))
        {
            throw new InvalidOperationException(
                "Serialized navigation profile must exactly match the configured Navigator shell.");
        }

        RecordValues.Look(
            chronicler,
            ref serializedGuidedPathMode,
            "GuidedPathMode",
            SerializedGuidedPathMode.Invalid);
        if (chronicler.Mode == SerializationMode.Loading
            && serializedGuidedPathMode != SerializedGuidedPathMode.Graph)
        {
            throw new InvalidOperationException(
                "Serialized guided path mode is missing, retired, or unsupported.");
        }

        RecordValues.Look(chronicler, ref _position, "Position", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _lastPosition, "LastPosition", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _rotation, "Rotation", FixedQuaternion.Identity);
        RecordValues.Look(chronicler, ref _velocity, "Velocity", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _speed, "Speed", Fixed64.Zero);
        RecordValues.Look(chronicler, ref _acceleration, "Acceleration", Vector3d.Zero);
        RecordValues.Look(chronicler, ref _guidedAllowUnwalkableEndpoints, "GuidedAllowUnwalkableEndpoints", false);
        RecordValues.Look(chronicler, ref _guidedAllowTraversalTransitions, "GuidedAllowTraversalTransitions", false);
        RecordValues.Look(chronicler, ref _guidedMaxClimbHeight, "GuidedMaxClimbHeight", Fixed64.One);
        RecordValues.Look(chronicler, ref _guidedVolumeHeuristic, "GuidedAStarHeuristic", HeuristicMethod.Manhattan);
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
        {
            RecordDeep.Look(chronicler, ref _steering, "Steering");
            if (chronicler.Mode == SerializationMode.Loading
                && _steering.CurrentQuery is PathQuery restoredQuery
                && restoredQuery.Agent != _navigationProfile)
            {
                _steering.Reset();
                throw new InvalidOperationException(
                    "Serialized graph query profile must exactly match the configured Navigator shell.");
            }
            if (chronicler.Mode == SerializationMode.Loading)
                _steering.RestoreFlowQueryFromLoadedFoot(FootPosition);
        }
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

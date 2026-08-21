//=======================================================================
// Navigator.Serialization.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using Chronicler;
using FixedMathSharp;
using Trailblazer.Navigation.Motor;
using Trailblazer.Navigation.Steering;
using Trailblazer.Navigation.Turning;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

public abstract partial class Navigator
{
    private const int CurrentSerializationSchemaVersion = 1;

    #region Serialization

    /// <inheritdoc />
    public virtual void RecordData(IChronicler chronicler)
    {
        bool isLoading = chronicler.Mode == SerializationMode.Loading;
        int schemaVersion = isLoading ? 0 : CurrentSerializationSchemaVersion;
        var navigationProfileRecord = isLoading
            ? new NavigationAgentProfileRecord()
            : new NavigationAgentProfileRecord(_navigationProfile);
        Vector3d position = isLoading ? Vector3d.Zero : _position;
        TrekCondition frameCondition = isLoading ? new TrekCondition() : _frameCondition.Clone();
        var pathSession = new NavigatorPathSessionRecord();
        if (!isLoading)
            pathSession.Capture(_steering?.CurrentQuery);

        // Validate the exact schema, shell profile, restored frame, and durable
        // query before any state on the existing runtime shell is changed.
        RecordValues.Look(chronicler, ref schemaVersion, "SchemaVersion", 0);
        RecordDeep.Look(chronicler, ref navigationProfileRecord, "NavigationProfile");
        RecordValues.Look(chronicler, ref position, "Position", Vector3d.Zero);
        RecordDeepStruct.Look(chronicler, ref frameCondition, "FrameCondition");
        RecordDeep.Look(chronicler, ref pathSession, "PathSession");

        PathQuery? restoredQuery = null;
        if (isLoading)
        {
            if (schemaVersion != CurrentSerializationSchemaVersion)
            {
                throw new InvalidOperationException(
                    "Serialized Navigator schema is missing, retired, or unsupported.");
            }
            if (!navigationProfileRecord.TryCreate(out NavigationAgentProfile recordedProfile)
                || recordedProfile != _navigationProfile)
            {
                throw new InvalidOperationException(
                    "Serialized navigation profile must exactly match the configured Navigator shell.");
            }
            if (!TraversalTransitionDefinition.IsKnownMedium(frameCondition.Medium))
            {
                throw new InvalidOperationException(
                    "Serialized Navigator traversal medium is missing or unsupported.");
            }

            Vector3d loadedFootPosition =
                position + Vector3d.Down * _navigationProfile.Shape.RootToFootOffsetY;
            if (!pathSession.TryCreateQuery(
                    loadedFootPosition,
                    frameCondition.Medium,
                    _navigationProfile,
                    out restoredQuery)
                || (restoredQuery.HasValue && _steering == null))
            {
                throw new InvalidOperationException(
                    "Serialized Navigator path session is missing, invalid, or unsupported.");
            }

            ValidateNestedRecords(chronicler, frameCondition);
        }

        Vector3d lastPosition = isLoading ? Vector3d.Zero : _lastPosition;
        FixedQuaternion rotation = isLoading ? FixedQuaternion.Identity : _rotation;
        Vector3d velocity = isLoading ? Vector3d.Zero : _velocity;
        Fixed64 speed = isLoading ? Fixed64.Zero : _speed;
        Vector3d acceleration = isLoading ? Vector3d.Zero : _acceleration;
        Guid globalId = isLoading ? Guid.Empty : _globalId;
        byte occupantGroupId = isLoading ? (byte)1 : _occupantGroupId;
        bool isLockedOn = !isLoading && _isLockedOn;
        Fixed64 stuckThresholdSpeed = isLoading ? Fixed64.Zero : _stuckThresholdSpeed;
        bool isGuideded = !isLoading && _isGuideded;
        TrekRequest frameRequest = isLoading ? new TrekRequest() : _frameRequest.Clone();
        NavigatorHeightmapGroundingSettings heightmapGrounding = isLoading
            ? new NavigatorHeightmapGroundingSettings()
            : _heightmapGrounding;

        RecordValues.Look(chronicler, ref lastPosition, "LastPosition", Vector3d.Zero);
        RecordValues.Look(chronicler, ref rotation, "Rotation", FixedQuaternion.Identity);
        RecordValues.Look(chronicler, ref velocity, "Velocity", Vector3d.Zero);
        RecordValues.Look(chronicler, ref speed, "Speed", Fixed64.Zero);
        RecordValues.Look(chronicler, ref acceleration, "Acceleration", Vector3d.Zero);
        RecordValues.Look(chronicler, ref globalId, "GlobalId", Guid.Empty);
        RecordValues.Look(chronicler, ref occupantGroupId, "OccupantGroupId", (byte)1);
        RecordValues.Look(chronicler, ref isLockedOn, "IsLockedOn", false);
        RecordValues.Look(chronicler, ref stuckThresholdSpeed, "StuckThresholdSpeed", Fixed64.Zero);
        RecordValues.Look(chronicler, ref isGuideded, "IsGuideded", false);
        RecordDeepStruct.Look(chronicler, ref frameRequest, "FrameRequest");
        RecordDeep.Look(chronicler, ref heightmapGrounding, "HeightmapGrounding");

        if (isLoading)
        {
            _position = position;
            _lastPosition = lastPosition;
            _rotation = rotation;
            _velocity = velocity;
            _speed = speed;
            _acceleration = acceleration;
            _globalId = globalId;
            _occupantGroupId = occupantGroupId;
            _isLockedOn = isLockedOn;
            _stuckThresholdSpeed = stuckThresholdSpeed;
            _isGuideded = isGuideded;
            _frameCondition = frameCondition;
            _frameRequest = frameRequest;
            _heightmapGrounding = heightmapGrounding;
        }

        if (_steering != null)
            RecordDeep.Look(chronicler, ref _steering, "Steering");
        if (_turning != null)
            RecordDeep.Look(chronicler, ref _turning, "Turning");
        if (_motor != null)
            RecordDeep.Look(chronicler, ref _motor, "Motor");

        if (isLoading)
        {
            TrailblazerWorldContext context = RequireContext();
            _pendingTransition = null;
            _steering?.RestoreQueryFromLoadedSession(restoredQuery);

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
            _steering?.BindPendingTransitionOwner(this);
            _steering?.UpdateOwnerRadius(Radius);

            _motor?.BindContext(context);

            _turning?.BindContext(context);
            _turning?.OnInitialize(Radius);

            CheckVoxelOccupancy(true);
        }
    }

    private void ValidateNestedRecords(IChronicler chronicler, TrekCondition frameCondition)
    {
        TrailblazerWorldContext context = RequireContext();
        if (_steering != null)
        {
            var steering = new NavSteering(context, Radius);
            RecordDeep.Look(chronicler, ref steering, "Steering");
            if (steering.BrakingPower < Fixed64.Zero
                || steering.StopMultiplier < Fixed64.Zero
                || steering.GroupFactor < Fixed64.Zero
                || steering.AvoidFactor < Fixed64.Zero)
            {
                throw new InvalidOperationException(
                    "Serialized steering limits must be non-negative.");
            }
        }

        if (_turning != null)
        {
            var turning = new NavTurning(context, Radius);
            RecordDeep.Look(chronicler, ref turning, "Turning");
            if (turning.TurnRate < Fixed64.Zero)
            {
                throw new InvalidOperationException(
                    "Serialized turn rate must be non-negative.");
            }
        }

        if (_motor != null)
        {
            NavMotor motor = NavMotor.CreateNew(
                context,
                frameCondition,
                CreateLocomotionProfile());
            RecordDeep.Look(chronicler, ref motor, "Motor");
            if (motor.Handler.Move.MaxSlowSpeed < Fixed64.Zero
                || motor.Handler.Move.MaxModerateSpeed < Fixed64.Zero
                || motor.Handler.Move.MaxFastSpeed < Fixed64.Zero
                || motor.Handler.Move.MaxSidewaysSpeed < Fixed64.Zero
                || motor.Handler.Move.MaxBackwardsSpeed < Fixed64.Zero
                || motor.Handler.Move.MaxGroundAcceleration < Fixed64.Zero
                || motor.Handler.Move.MaxAirAcceleration < Fixed64.Zero)
            {
                throw new InvalidOperationException(
                    "Serialized movement limits must be non-negative.");
            }
        }
    }

    #endregion
}

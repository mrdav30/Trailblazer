//=======================================================================
// Navigator2D.Serialization.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using Chronicler;
using FixedMathSharp;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <content>Stages planar navigation records before replacing the live controller state.</content>
public abstract partial class Navigator2D : IRecordable
{
    private const string RecordKind = "Trailblazer.Navigator2D";
    private const int RecordSchemaVersion = 1;

    private (int Id, NavigationPose2D Pose, Vector2d Anchor)? _restoredCarrier;
    private bool _resumeSideView;

    /// <summary>Records accepted planar motion and durable guidance, or populates a matching existing shell.</summary>
    /// <remarks>Restore the host body and world before population. Successful population retires the old guidance.</remarks>
    public virtual void RecordData(IChronicler chronicler)
    {
        bool loading = chronicler.Mode == SerializationMode.Loading;
        if (loading)
            EnsureAvailable();
        else
            EnsureIdle();
        _busy = true;
        try
        {
            RecordShell(chronicler, loading);
            var frame = loading ? default : CaptureRecordedFrame();
            try
            {
                frame.Look(chronicler);
                if (loading)
                    ValidateRecordedFrame(frame);
            }
            catch (ArgumentException exception) when (loading)
            {
                throw new InvalidOperationException("Serialized Navigator2D continuation is invalid.", exception);
            }

            if (loading)
                PopulateRecordedFrame(frame);
        }
        finally
        {
            _busy = false;
        }
    }

    private void RecordShell(IChronicler chronicler, bool loading)
    {
        string? kind = loading ? null : RecordKind;
        int schema = loading ? 0 : RecordSchemaVersion;
        Guid id = GlobalId;
        string? map = _mapId;
        int layer = _binding.LayerIndex;
        Fixed64 embedding = _binding.EmbeddingY;
        TraversalMedium medium = _medium;
        var profile = NavigationProfile;
        var (mode, radius, axis, step, drop, normal, arrival, media, capabilities) = (profile.Mode, profile.Radius,
            profile.AxisLength, profile.MaxStepUp, profile.MaxDropDown, profile.MinimumSupportNormalDot,
            profile.ArrivalRadius, profile.AllowedMedia, profile.Capabilities);
        var settings = _motorSettings;
        var (speed, turn, gravity, jump, jumps) = (settings.MoveSpeed, settings.TurnRateRadians, settings.Gravity,
            settings.JumpSpeed, settings.MaxJumpCount);
        RecordValues.Look(chronicler, ref kind, "Kind", null);
        RecordValues.Look(chronicler, ref schema, "SchemaVersion", 0);
        RecordValues.Look(chronicler, ref id, "GlobalId", Guid.Empty);
        RecordValues.Look(chronicler, ref map, "InitialMap", null);
        RecordValues.Look(chronicler, ref layer, "Layer", -1);
        RecordValues.Look(chronicler, ref embedding, "EmbeddingY", Fixed64.Zero);
        RecordValues.Look(chronicler, ref medium, "Medium", default);
        RecordValues.Look(chronicler, ref mode, "Mode", default);
        RecordValues.Look(chronicler, ref radius, "Radius", Fixed64.Zero);
        RecordValues.Look(chronicler, ref axis, "AxisLength", Fixed64.Zero);
        RecordValues.Look(chronicler, ref step, "MaxStepUp", Fixed64.Zero);
        RecordValues.Look(chronicler, ref drop, "MaxDropDown", Fixed64.Zero);
        RecordValues.Look(chronicler, ref normal, "MinimumSupportNormalDot", Fixed64.Zero);
        RecordValues.Look(chronicler, ref arrival, "ArrivalRadius", Fixed64.Zero);
        RecordValues.Look(chronicler, ref media, "AllowedMedia", TraversalMedia.None);
        RecordValues.Look(chronicler, ref capabilities, "Capabilities", TraversalCapability.None);
        RecordValues.Look(chronicler, ref speed, "MoveSpeed", Fixed64.Zero);
        RecordValues.Look(chronicler, ref turn, "TurnRateRadians", Fixed64.Zero);
        RecordValues.Look(chronicler, ref gravity, "Gravity", Fixed64.Zero);
        RecordValues.Look(chronicler, ref jump, "JumpSpeed", Fixed64.Zero);
        RecordValues.Look(chronicler, ref jumps, "MaxJumpCount", 0);

        if (!loading)
            return;
        ChronicleGuard.ThrowIfTrue(kind != RecordKind | schema != RecordSchemaVersion,
            message: "Serialized Navigator2D kind or schema is missing or unsupported.");
        ChronicleGuard.ThrowIfTrue(id != GlobalId | map != _mapId | layer != _binding.LayerIndex
            | embedding != _binding.EmbeddingY | medium != _medium
            | !(mode, radius, axis, step, drop, normal, arrival, media, capabilities).Equals((profile.Mode, profile.Radius,
                profile.AxisLength, profile.MaxStepUp, profile.MaxDropDown, profile.MinimumSupportNormalDot,
                profile.ArrivalRadius, profile.AllowedMedia, profile.Capabilities))
            | !(speed, turn, gravity, jump, jumps).Equals((settings.MoveSpeed, settings.TurnRateRadians, settings.Gravity,
                settings.JumpSpeed, settings.MaxJumpCount)),
            message: "Serialized Navigator2D must exactly match the configured identity, domain, and tuning.");
    }

    private RecordedFrame CaptureRecordedFrame()
    {
        var carrier = _restoredCarrier;
        if (IsGrounded && Support!.Value.Carrier is NavigationCarrier2D sampled)
            carrier = (sampled.StableId, sampled.Pose, sampled.LocalAnchor);
        return new RecordedFrame
        {
            Center = Pose.Center,
            Rotation = Pose.RotationRadians,
            Velocity = Velocity,
            Facing = _facing,
            VerticalVelocity = _verticalVelocity,
            JumpCount = _jumpCount,
            OccupantGroup = OccupantGroupId,
            AcceptedMap = _acceptedMapId,
            AcquisitionMode = AcquisitionMode,
            VisibilityInterval = _visibilityInterval,
            VisibilityPhase = _visibilityPhase,
            CarrierId = carrier?.Id ?? 0,
            CarrierCenter = carrier?.Pose.Center ?? default,
            CarrierRotation = carrier?.Pose.RotationRadians ?? default,
            CarrierAnchor = carrier?.Anchor ?? default,
            Trek = CanRecordTrek() ? new Navigator2DTrekRecord
            {
                Source = _binding.Mode == NavigationMode2D.SideView ? _sideViewSource : default,
                Destination = _requestedGoal,
                Policy = _policy,
                Budget = _budget,
                Algorithm = _algorithm,
                MovementGroupId = MovementGroupId,
                AllowTransitions = _allowTransitions,
                Arrived = HasArrived
            } : null
        };
    }

    private bool CanRecordTrek() =>
        (_hasIntent & (_algorithm == PathAlgorithm.AStar | _algorithm == PathAlgorithm.FlowField))
        && IsPublishedTrek(_context.Pathing.NavigationGraphStore.Current, _sideViewSource, _requestedGoal, _policy);

    private void ValidateRecordedFrame(in RecordedFrame frame)
    {
        bool sideView = _binding.Mode == NavigationMode2D.SideView;
        ChronicleGuard.ThrowIfTrue(frame.Rotation < -Fixed64.Pi | frame.Rotation >= Fixed64.Pi
            | (sideView & frame.Rotation != Fixed64.Zero)
            | (frame.Facing != Vector2d.Right & frame.Facing != -Vector2d.Right)
            | frame.JumpCount < 0 | frame.JumpCount > _motorSettings.MaxJumpCount
            | (frame.VerticalVelocity > Fixed64.Zero & (frame.JumpCount == 0 | frame.VerticalVelocity > _motorSettings.JumpSpeed))
            | (!sideView & (frame.VerticalVelocity != Fixed64.Zero | frame.JumpCount != 0 | frame.Facing != Vector2d.Right)),
            message: "Serialized Navigator2D pose, facing, or motor continuation is invalid.");
        ChronicleGuard.ThrowIfTrue(frame.CarrierId < 0 | frame.CarrierRotation < -Fixed64.Pi
            | frame.CarrierRotation >= Fixed64.Pi | (!sideView & frame.CarrierId != 0)
            | (frame.CarrierId == 0 & (frame.CarrierCenter != Vector2d.Zero | frame.CarrierAnchor != Vector2d.Zero
                | frame.CarrierRotation != Fixed64.Zero)), message: "Serialized Navigator2D carrier continuation is invalid.");
        ChronicleGuard.ThrowIfTrue(frame.AcquisitionMode != NavigationAcquisitionMode.Immediate
            & frame.AcquisitionMode != NavigationAcquisitionMode.Scheduled, message: "Serialized Navigator2D acquisition mode is invalid.");
        NavigationFramePhase.ValidateConfiguration(frame.VisibilityInterval, frame.VisibilityPhase);
        if (frame.Trek is Navigator2DTrekRecord trek)
        {
            ChronicleGuard.ThrowIfTrue((trek.Algorithm != PathAlgorithm.AStar & trek.Algorithm != PathAlgorithm.FlowField)
                | trek.MovementGroupId < -1 | ((sideView | trek.Arrived) & trek.MovementGroupId >= 0)
                | (sideView != (trek.Source.MapId != null)), message: "Serialized Navigator2D trek options are invalid.");
        }
    }

    private void PopulateRecordedFrame(in RecordedFrame frame)
    {
        var pathing = _context.Pathing;
        var world = _context.World;
        var epoch = (world.SpawnToken, world.Version, world.ChangeSequence, _context.ClockLifetime, _context.FrameCount);
        NavigationWorldGraph graph;
        using (var snapshot = pathing.TryAcquireNavigationGraph())
        {
            if (snapshot == null)
                throw new InvalidOperationException("The Navigator2D publication is unavailable during population.");
            graph = snapshot.Graph;
            string acceptedMap = frame.AcceptedMap ?? _mapId;
            ChronicleGuard.ThrowIfTrue(!graph.TryGetMap(acceptedMap, out var map)
                || !NavigationPlanarWork.TryGetControllerLayer(map, _binding, out var layer)
                || (acceptedMap == _mapId & layer != _binding),
                message: "Serialized Navigator2D accepted map is outside its published domain.");
            if (frame.Trek is Navigator2DTrekRecord trek)
            {
                ChronicleGuard.ThrowIfTrue(!IsPublishedTrek(graph, trek.Source, trek.Destination, trek.Policy),
                    message: "Serialized Navigator2D trek requires published addresses and an exact area policy.");
            }
        }

        var status = pathing.ResolvePlanarCommittedCell(_mapId, _binding, frame.Center, _medium,
            frame.Trek?.Policy, _context.FrameCount, out var grid, out var voxel, out var cell, out var resolvedMap,
            frame.AcceptedMap ?? _mapId, frame.AcceptedMap ?? _mapId);
        ChronicleGuard.ThrowIfTrue(status == NavigationCommittedCellResolveStatus.Unavailable | !CanPublishCell(voxel)
            | !ReferenceEquals(graph, pathing.NavigationGraphStore.Current)
            | !epoch.Equals((world.SpawnToken, world.Version, world.ChangeSequence, _context.ClockLifetime, _context.FrameCount)),
            message: "The Navigator2D world changed during population.");

        ResetNavigation(releaseOccupancy: false);
        Pose = new NavigationPose2D(frame.Center, frame.Rotation);
        Velocity = frame.Velocity;
        _facing = frame.Facing;
        _verticalVelocity = frame.VerticalVelocity;
        _jumpCount = frame.JumpCount;
        OccupantGroupId = frame.OccupantGroup;
        AcquisitionMode = frame.AcquisitionMode;
        _visibilityInterval = frame.VisibilityInterval;
        _visibilityPhase = frame.VisibilityPhase;
        _acceptedMapId = resolvedMap ?? frame.AcceptedMap;
        _restoredCarrier = frame.CarrierId == 0 ? null : (frame.CarrierId,
            new NavigationPose2D(frame.CarrierCenter, frame.CarrierRotation), frame.CarrierAnchor);
        if (frame.Trek is Navigator2DTrekRecord restored)
        {
            _hasIntent = true;
            _requestedGoal = _routeGoal = restored.Destination;
            _sideViewSource = restored.Source;
            _policy = restored.Policy;
            _budget = restored.Budget;
            _algorithm = restored.Algorithm;
            _allowTransitions = restored.AllowTransitions;
            HasArrived = restored.Arrived;
            _resumeSideView = _binding.Mode == NavigationMode2D.SideView && !restored.Arrived;
            GuidanceStatus = restored.Arrived ? NavigationGuideStatus.Success : NavigationGuideStatus.Pending;
            JoinGroup(restored.MovementGroupId);
        }

        PublishCell(grid, voxel, cell, emitChange: false);
    }

    private bool IsPublishedTrek(NavigationWorldGraph graph, NavigationCellAddress source,
        NavigationCellAddress destination, NavigationAreaPolicyKey policy) =>
        graph.AreaCatalog.TryGet(policy, out _) & IsRecordedAddressPublished(graph, destination)
        & (_binding.Mode != NavigationMode2D.SideView | IsRecordedAddressPublished(graph, source));

    private bool IsRecordedAddressPublished(NavigationWorldGraph graph, NavigationCellAddress address) =>
        address.MapId != null && graph.TryGetMap(address.MapId, out var map)
        && NavigationPlanarWork.TryGetControllerLayer(map, _binding, out var layer)
        && (address.Index.y == layer.LayerIndex & map.Map.ContainsGridIndex(address.Index));

    private void ClearRestoredCarrier() => _restoredCarrier = null;

    private NavigationMotionResult2D RebindRestoredCarrier(in NavigationMotionResult2D result, bool grounded)
    {
        if (!grounded || _restoredCarrier is not { } prior)
            return result;
        var sample = result.Support!.Value;
        if (sample.Carrier is not NavigationCarrier2D fresh || (fresh.StableId != prior.Id
                | fresh.Pose.Center != prior.Pose.Center | fresh.Pose.RotationRadians != prior.Pose.RotationRadians))
            return result;
        var rebound = new NavigationCarrier2D(fresh.StableId, fresh.Lifetime, fresh.Pose, prior.Anchor);
        return new NavigationMotionResult2D(result.Accepted, result.SupportStatus,
            new NavigationSupport2D(sample.Point, sample.Normal, sample.Friction, rebound));
    }

    private struct RecordedFrame
    {
        internal Vector2d Center, Velocity, Facing, CarrierCenter, CarrierAnchor;
        internal Fixed64 Rotation, VerticalVelocity, CarrierRotation;
        internal int JumpCount, CarrierId, VisibilityInterval;
        internal int? VisibilityPhase;
        internal byte OccupantGroup;
        internal string? AcceptedMap;
        internal NavigationAcquisitionMode AcquisitionMode;
        internal Navigator2DTrekRecord? Trek;

        internal void Look(IChronicler chronicler)
        {
            RecordValues.Look(chronicler, ref Center, "Center", Vector2d.Zero);
            RecordValues.Look(chronicler, ref Rotation, "Rotation", Fixed64.Zero);
            RecordValues.Look(chronicler, ref Velocity, "Velocity", Vector2d.Zero);
            RecordValues.Look(chronicler, ref Facing, "Facing", Vector2d.Right);
            RecordValues.Look(chronicler, ref VerticalVelocity, "VerticalVelocity", Fixed64.Zero);
            RecordValues.Look(chronicler, ref JumpCount, "JumpCount", 0);
            RecordValues.Look(chronicler, ref OccupantGroup, "OccupantGroup", (byte)0);
            RecordValues.Look(chronicler, ref AcceptedMap, "AcceptedMap", null);
            RecordValues.Look(chronicler, ref AcquisitionMode, "AcquisitionMode", NavigationAcquisitionMode.Immediate);
            RecordValues.Look(chronicler, ref VisibilityInterval, "VisibilityInterval", 0);
            RecordValues.Look(chronicler, ref VisibilityPhase, "VisibilityPhase", null);
            RecordValues.Look(chronicler, ref CarrierId, "CarrierId", 0);
            RecordValues.Look(chronicler, ref CarrierCenter, "CarrierCenter", Vector2d.Zero);
            RecordValues.Look(chronicler, ref CarrierRotation, "CarrierRotation", Fixed64.Zero);
            RecordValues.Look(chronicler, ref CarrierAnchor, "CarrierAnchor", Vector2d.Zero);
            RecordNullableDeep.Look(chronicler, ref Trek, "Trek");
        }
    }
}

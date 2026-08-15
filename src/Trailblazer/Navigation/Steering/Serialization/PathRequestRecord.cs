//=======================================================================
// PathRequestRecord.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using Chronicler;
using FixedMathSharp;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation.Steering;

internal enum PathRequestRecordKind
{
    None = 0,
    FlowField = 2,
    Volume = 3
}

/// <summary>
/// Stores a serializable path-request shape and rebuilds it on load.
/// </summary>
internal sealed class PathRequestRecord : IRecordable
{
    private const int NoWaypointIndex = -1;

    public PathRequestRecordKind Kind;

    public Vector3d Origin;

    public Vector3d TargetPosition;

    public Fixed64 UnitSize = Fixed64.One;

    public bool AllowUnwalkableEndpoints;

    public bool AllowTraversalTransitions;

    public int MaxPathSearchRange;

    public Fixed64 MaxClimbHeight = Fixed64.One;

    public HeuristicMethod VolumeHeuristic = HeuristicMethod.Manhattan;

    public int FlowFieldExtraFloodRange = FlowFieldPathRequest.DefaultExtraFloodRange;

    public TraversalMedium Medium = TraversalMedium.Gas;

    public bool HasGuide;

    public int WaypointIndex = NoWaypointIndex;

    public void Capture(IPathRequest? request, IGuide? guide)
    {
        Reset();
        if (request == null)
            return;

        Origin = request.Origin;
        TargetPosition = request.TargetPosition;
        UnitSize = request.UnitSize;
        AllowUnwalkableEndpoints = request.AllowUnwalkableEndpoints;
        MaxPathSearchRange = request.MaxPathSearchRange;
        HasGuide = guide != null;

        switch (request)
        {
            case FlowFieldPathRequest flowField:
                Kind = PathRequestRecordKind.FlowField;
                AllowTraversalTransitions = flowField.AllowTraversalTransitions;
                MaxClimbHeight = flowField.MaxClimbHeight;
                FlowFieldExtraFloodRange = flowField.ExtraFloodRange;
                break;

            case VolumePathRequest volume:
                Kind = PathRequestRecordKind.Volume;
                VolumeHeuristic = volume.Heuristic;
                Medium = volume.Medium;
                break;

            default:
                throw new NotSupportedException(
                    $"Unable to record steering path request type '{request.GetType().Name}'.");
        }

        if (guide is IWaypointGuide waypointGuide)
            WaypointIndex = waypointGuide.CurrentWaypointIndex;
    }

    public bool TryCreateRequest(TrailblazerWorldContext context, out IPathRequest? request)
    {
        request = null;
        PathRequestContextResolver.ThrowIfUnusable(context);

        switch (Kind)
        {
            case PathRequestRecordKind.None:
                return true;

            case PathRequestRecordKind.FlowField:
                FlowFieldPathRequest? flowField = FlowFieldPathRequest.Create(
                    context,
                    Origin,
                    TargetPosition,
                    UnitSize,
                    AllowUnwalkableEndpoints,
                    AllowTraversalTransitions);
                if (flowField == null)
                    return false;

                flowField.MaxClimbHeight = MaxClimbHeight;
                flowField.ExtraFloodRange = FlowFieldExtraFloodRange;
                if (MaxPathSearchRange > 0)
                    flowField.MaxPathSearchRange = MaxPathSearchRange;

                request = flowField;
                return true;

            case PathRequestRecordKind.Volume:
                VolumePathRequest? volume = VolumePathRequest.Create(
                    context,
                    Origin,
                    TargetPosition,
                    UnitSize,
                    VolumeHeuristic,
                    AllowUnwalkableEndpoints,
                    Medium);
                if (volume == null)
                    return false;

                if (MaxPathSearchRange > 0)
                    volume.MaxPathSearchRange = MaxPathSearchRange;

                request = volume;
                return true;

            default:
                return false;
        }
    }

    public bool TryCreateGuide(IPathRequest? request, out IGuide? guide)
    {
        guide = null;
        if (!HasGuide || request == null)
            return false;

        if (!request.Context.Guides.RequestGuide(request, out guide) || guide == null)
            return false;

        if (guide is VolumeGuide volumeGuide)
            RestoreWaypointIndex(volumeGuide);
        return true;
    }

    public void Reset()
    {
        Kind = PathRequestRecordKind.None;
        Origin = Vector3d.Zero;
        TargetPosition = Vector3d.Zero;
        UnitSize = Fixed64.One;
        AllowUnwalkableEndpoints = false;
        AllowTraversalTransitions = false;
        MaxPathSearchRange = 0;
        MaxClimbHeight = Fixed64.One;
        VolumeHeuristic = HeuristicMethod.Manhattan;
        FlowFieldExtraFloodRange = FlowFieldPathRequest.DefaultExtraFloodRange;
        Medium = TraversalMedium.Gas;
        HasGuide = false;
        WaypointIndex = NoWaypointIndex;
    }

    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref Kind, "Kind", PathRequestRecordKind.None);
        RecordValues.Look(chronicler, ref Origin, "Origin", Vector3d.Zero);
        RecordValues.Look(chronicler, ref TargetPosition, "TargetPosition", Vector3d.Zero);
        RecordValues.Look(chronicler, ref UnitSize, "UnitSize", Fixed64.One);
        RecordValues.Look(chronicler, ref AllowUnwalkableEndpoints, "AllowUnwalkableEndpoints", false);
        RecordValues.Look(chronicler, ref AllowTraversalTransitions, "AllowTraversalTransitions", false);
        RecordValues.Look(chronicler, ref MaxPathSearchRange, "MaxPathSearchRange", 0);
        RecordValues.Look(chronicler, ref MaxClimbHeight, "MaxClimbHeight", Fixed64.One);
        RecordValues.Look(chronicler, ref VolumeHeuristic, "AStarHeuristic", HeuristicMethod.Manhattan);
        RecordValues.Look(chronicler, ref FlowFieldExtraFloodRange, "FlowFieldExtraFloodRange", FlowFieldPathRequest.DefaultExtraFloodRange);
        RecordValues.Look(chronicler, ref Medium, "Medium", TraversalMedium.Gas);
        RecordValues.Look(chronicler, ref HasGuide, "HasGuide", false);
        RecordValues.Look(chronicler, ref WaypointIndex, "WaypointIndex", NoWaypointIndex);
    }

    private void RestoreWaypointIndex(VolumeGuide guide)
    {
        if (WaypointIndex <= 0)
            return;

        while (guide.CurrentWaypointIndex < WaypointIndex
            && guide.TryGetWaypointAt(guide.CurrentWaypointIndex + 1, out _))
        {
            guide.AdvanceWaypoint();
        }
    }

}

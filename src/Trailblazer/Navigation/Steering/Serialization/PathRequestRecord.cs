using Chronicler;
using FixedMathSharp;
using System;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation.Steering;

internal enum PathRequestRecordKind
{
    None,
    AStar,
    FlowField,
    Volume,
    Hybrid
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

    public HeuristicMethod AStarHeuristic = HeuristicMethod.Manhattan;

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
            case AStarPathRequest aStar:
                Kind = PathRequestRecordKind.AStar;
                AllowTraversalTransitions = aStar.AllowTraversalTransitions;
                AStarHeuristic = aStar.Heuristic;
                MaxClimbHeight = aStar.MaxClimbHeight;
                break;

            case FlowFieldPathRequest flowField:
                Kind = PathRequestRecordKind.FlowField;
                AllowTraversalTransitions = flowField.AllowTraversalTransitions;
                MaxClimbHeight = flowField.MaxClimbHeight;
                FlowFieldExtraFloodRange = flowField.ExtraFloodRange;
                break;

            case VolumePathRequest volume:
                Kind = PathRequestRecordKind.Volume;
                AStarHeuristic = volume.Heuristic;
                Medium = volume.Medium;
                break;

            case HybridPathRequest hybrid:
                Kind = PathRequestRecordKind.Hybrid;
                AStarHeuristic = hybrid.Heuristic;
                MaxClimbHeight = hybrid.MaxClimbHeight;
                break;

            default:
                throw new NotSupportedException(
                    $"Unable to record steering path request type '{request.GetType().Name}'.");
        }

        if (guide is IWaypointGuide waypointGuide)
            WaypointIndex = waypointGuide.CurrentWaypointIndex;
    }

    public bool TryCreateRequest(out IPathRequest? request)
    {
        request = null;

        switch (Kind)
        {
            case PathRequestRecordKind.None:
                return true;

            case PathRequestRecordKind.AStar:
                AStarPathRequest? aStar = AStarPathRequest.Create(
                    Origin,
                    TargetPosition,
                    UnitSize,
                    AStarHeuristic,
                    AllowUnwalkableEndpoints,
                    AllowTraversalTransitions);
                if (aStar == null)
                    return false;

                aStar.MaxClimbHeight = MaxClimbHeight;
                if (MaxPathSearchRange > 0)
                    aStar.MaxPathSearchRange = MaxPathSearchRange;

                request = aStar;
                return true;

            case PathRequestRecordKind.FlowField:
                FlowFieldPathRequest? flowField = FlowFieldPathRequest.Create(
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
                    Origin,
                    TargetPosition,
                    UnitSize,
                    AStarHeuristic,
                    AllowUnwalkableEndpoints,
                    Medium);
                if (volume == null)
                    return false;

                if (MaxPathSearchRange > 0)
                    volume.MaxPathSearchRange = MaxPathSearchRange;

                request = volume;
                return true;

            case PathRequestRecordKind.Hybrid:
                HybridPathRequest? hybrid = HybridPathRequest.Create(
                    Origin,
                    TargetPosition,
                    UnitSize,
                    AStarHeuristic,
                    MaxClimbHeight,
                    AllowUnwalkableEndpoints);
                if (hybrid == null)
                    return false;

                if (MaxPathSearchRange > 0)
                    hybrid.MaxPathSearchRange = MaxPathSearchRange;

                request = hybrid;
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

        if (!PathGuideFactory.RequestGuide(request, out guide) || guide == null)
            return false;

        if (guide is AStarGuide aStarGuide)
            RestoreWaypointIndex(aStarGuide);
        else if (guide is VolumeGuide volumeGuide)
            RestoreWaypointIndex(volumeGuide);
        else if (guide is HybridGuide hybridGuide)
            RestoreWaypointIndex(hybridGuide);

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
        AStarHeuristic = HeuristicMethod.Manhattan;
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
        RecordValues.Look(chronicler, ref AStarHeuristic, "AStarHeuristic", HeuristicMethod.Manhattan);
        RecordValues.Look(chronicler, ref FlowFieldExtraFloodRange, "FlowFieldExtraFloodRange", FlowFieldPathRequest.DefaultExtraFloodRange);
        RecordValues.Look(chronicler, ref Medium, "Medium", TraversalMedium.Gas);
        RecordValues.Look(chronicler, ref HasGuide, "HasGuide", false);
        RecordValues.Look(chronicler, ref WaypointIndex, "WaypointIndex", NoWaypointIndex);
    }

    private void RestoreWaypointIndex(AStarGuide guide)
    {
        if (WaypointIndex <= 0)
            return;

        while (guide.CurrentWaypointIndex < WaypointIndex
            && guide.TryGetWaypointAt(guide.CurrentWaypointIndex + 1, out _))
        {
            guide.AdvanceWaypoint();
        }
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

    private void RestoreWaypointIndex(HybridGuide guide)
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

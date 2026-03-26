using FixedMathSharp;
using System;
using Trailblazer.Pathing;

namespace Trailblazer.Serialization;

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

    public bool AllowUnwalkableEndNode;

    public bool AllowTraversalTransitions;

    public int MaxPathSearchRange;

    public HeuristicMethod AStarHeuristic = HeuristicMethod.Manhattan;

    public Fixed64 AStarMaxClimbHeight = Fixed64.One;

    public int FlowFieldExtraFloodRange = FlowFieldPathRequest.DefaultExtraFloodRange;

    public VolumeTraversalMode TraversalMode = VolumeTraversalMode.Open;

    public bool HasGuide;

    public int WaypointIndex = NoWaypointIndex;

    public void Capture(IPathRequest request, IGuide guide)
    {
        Reset();
        if (request == null)
            return;

        Origin = request.Origin;
        TargetPosition = request.TargetPosition;
        UnitSize = request.UnitSize;
        AllowUnwalkableEndNode = request.AllowUnwalkableEndNode;
        MaxPathSearchRange = request.MaxPathSearchRange;
        HasGuide = guide != null;

        switch (request)
        {
            case AStarPathRequest aStar:
                Kind = PathRequestRecordKind.AStar;
                AllowTraversalTransitions = aStar.AllowTraversalTransitions;
                AStarHeuristic = aStar.Heuristic;
                AStarMaxClimbHeight = aStar.MaxClimbHeight;
                break;

            case FlowFieldPathRequest flowField:
                Kind = PathRequestRecordKind.FlowField;
                AllowTraversalTransitions = flowField.AllowTraversalTransitions;
                FlowFieldExtraFloodRange = flowField.ExtraFloodRange;
                break;

            case VolumePathRequest volume:
                Kind = PathRequestRecordKind.Volume;
                AStarHeuristic = volume.Heuristic;
                TraversalMode = volume.TraversalMode;
                break;

            case HybridPathRequest hybrid:
                Kind = PathRequestRecordKind.Hybrid;
                AStarHeuristic = hybrid.Heuristic;
                AStarMaxClimbHeight = hybrid.MaxClimbHeight;
                break;

            default:
                throw new NotSupportedException(
                    $"Unable to record steering path request type '{request.GetType().Name}'.");
        }

        if (guide is IWaypointGuide waypointGuide)
            WaypointIndex = waypointGuide.CurrentWaypointIndex;
    }

    public bool TryCreateRequest(out IPathRequest request)
    {
        request = null;

        switch (Kind)
        {
            case PathRequestRecordKind.None:
                return true;

            case PathRequestRecordKind.AStar:
                AStarPathRequest aStar = AStarPathRequest.Create(
                    Origin,
                    TargetPosition,
                    UnitSize,
                    AStarHeuristic,
                    AllowUnwalkableEndNode,
                    AllowTraversalTransitions);
                if (aStar == null)
                    return false;

                aStar.MaxClimbHeight = AStarMaxClimbHeight;
                if (MaxPathSearchRange > 0)
                    aStar.MaxPathSearchRange = MaxPathSearchRange;

                request = aStar;
                return true;

            case PathRequestRecordKind.FlowField:
                FlowFieldPathRequest flowField = FlowFieldPathRequest.Create(
                    Origin,
                    TargetPosition,
                    UnitSize,
                    AllowUnwalkableEndNode,
                    AllowTraversalTransitions);
                if (flowField == null)
                    return false;

                flowField.ExtraFloodRange = FlowFieldExtraFloodRange;
                if (MaxPathSearchRange > 0)
                    flowField.MaxPathSearchRange = MaxPathSearchRange;

                request = flowField;
                return true;

            case PathRequestRecordKind.Volume:
                VolumePathRequest volume = VolumePathRequest.Create(
                    Origin,
                    TargetPosition,
                    UnitSize,
                    AStarHeuristic,
                    AllowUnwalkableEndNode,
                    TraversalMode);
                if (volume == null)
                    return false;

                if (MaxPathSearchRange > 0)
                    volume.MaxPathSearchRange = MaxPathSearchRange;

                request = volume;
                return true;

            case PathRequestRecordKind.Hybrid:
                HybridPathRequest hybrid = HybridPathRequest.Create(
                    Origin,
                    TargetPosition,
                    UnitSize,
                    AStarHeuristic,
                    AStarMaxClimbHeight,
                    AllowUnwalkableEndNode);
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

    public bool TryCreateGuide(IPathRequest request, out IGuide guide)
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
        AllowUnwalkableEndNode = false;
        AllowTraversalTransitions = false;
        MaxPathSearchRange = 0;
        AStarHeuristic = HeuristicMethod.Manhattan;
        AStarMaxClimbHeight = Fixed64.One;
        FlowFieldExtraFloodRange = FlowFieldPathRequest.DefaultExtraFloodRange;
        TraversalMode = VolumeTraversalMode.Open;
        HasGuide = false;
        WaypointIndex = NoWaypointIndex;
    }

    public void RecordData(IChronicler chronicler)
    {
        PathRequestRecordKind kind = Kind;
        Vector3d origin = Origin;
        Vector3d targetPosition = TargetPosition;
        Fixed64 unitSize = UnitSize;
        bool allowUnwalkableEndNode = AllowUnwalkableEndNode;
        bool allowTraversalTransitions = AllowTraversalTransitions;
        int maxPathSearchRange = MaxPathSearchRange;
        HeuristicMethod aStarHeuristic = AStarHeuristic;
        Fixed64 aStarMaxClimbHeight = AStarMaxClimbHeight;
        int flowFieldExtraFloodRange = FlowFieldExtraFloodRange;
        VolumeTraversalMode traversalMode = TraversalMode;
        bool hasGuide = HasGuide;
        int waypointIndex = WaypointIndex;

        RecordValues.Look(chronicler, ref kind, "kind", PathRequestRecordKind.None);
        RecordValues.Look(chronicler, ref origin, "origin", Vector3d.Zero);
        RecordValues.Look(chronicler, ref targetPosition, "targetPosition", Vector3d.Zero);
        RecordValues.Look(chronicler, ref unitSize, "unitSize", Fixed64.One);
        RecordValues.Look(chronicler, ref allowUnwalkableEndNode, "allowUnwalkableEndNode", false);
        RecordValues.Look(chronicler, ref allowTraversalTransitions, "allowTraversalTransitions", false);
        RecordValues.Look(chronicler, ref maxPathSearchRange, "maxPathSearchRange", 0);
        RecordValues.Look(chronicler, ref aStarHeuristic, "aStarHeuristic", HeuristicMethod.Manhattan);
        RecordValues.Look(chronicler, ref aStarMaxClimbHeight, "aStarMaxClimbHeight", Fixed64.One);
        RecordValues.Look(chronicler, ref flowFieldExtraFloodRange, "flowFieldExtraFloodRange", FlowFieldPathRequest.DefaultExtraFloodRange);
        RecordValues.Look(chronicler, ref traversalMode, "volumeTraversalMode", VolumeTraversalMode.Open);
        RecordValues.Look(chronicler, ref hasGuide, "hasGuide", false);
        RecordValues.Look(chronicler, ref waypointIndex, "waypointIndex", NoWaypointIndex);

        if (chronicler.Mode == SerializationMode.Loading)
        {
            Kind = kind;
            Origin = origin;
            TargetPosition = targetPosition;
            UnitSize = unitSize;
            AllowUnwalkableEndNode = allowUnwalkableEndNode;
            AllowTraversalTransitions = allowTraversalTransitions;
            MaxPathSearchRange = maxPathSearchRange;
            AStarHeuristic = aStarHeuristic;
            AStarMaxClimbHeight = aStarMaxClimbHeight;
            FlowFieldExtraFloodRange = flowFieldExtraFloodRange;
            TraversalMode = traversalMode;
            HasGuide = hasGuide;
            WaypointIndex = waypointIndex;
        }
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

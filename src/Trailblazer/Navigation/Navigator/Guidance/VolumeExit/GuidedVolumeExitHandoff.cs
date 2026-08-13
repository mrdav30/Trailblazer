//=======================================================================
// GuidedVolumeExitHandoff.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using Chronicler;
using FixedMathSharp;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>
/// Stores the follow-up chart-backed leg for a object-owned volume exit handoff.
/// </summary>
internal sealed class GuidedVolumeExitHandoff : IRecordable
{
    public TrailblazerWorldContext? Context;

    public string? TransitionId;

    public Vector3d ChartOriginPosition;

    public Vector3d TargetPosition;

    public SolidPathAlgorithm ChartPathMode = SolidPathAlgorithm.AStar;

    public bool AllowUnwalkableEndpoints;

    public bool AllowTraversalTransitions;

    public Fixed64 MaxClimbHeight = Fixed64.One;

    public HeuristicMethod AStarHeuristic = HeuristicMethod.Manhattan;

    public int FlowFieldExtraFloodRange = FlowFieldPathRequest.DefaultExtraFloodRange;

    public int MovementGroupId = -1;

    public bool IsRequestingClimb;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(TransitionId)
        && (ChartPathMode == SolidPathAlgorithm.AStar || ChartPathMode == SolidPathAlgorithm.FlowField);

    public bool TryCreateFollowupRequest(
        TrailblazerWorldContext context,
        Vector3d currentPosition,
        Fixed64 unitSize,
        out IPathRequest? request)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        request = null;
        if (!IsValid)
            return false;
        switch (ChartPathMode)
        {
            case SolidPathAlgorithm.AStar:
                var aStar = AStarPathRequest.Create(
                    context,
                    ChartOriginPosition,
                    TargetPosition,
                    unitSize,
                    AStarHeuristic,
                    AllowUnwalkableEndpoints,
                    AllowTraversalTransitions);
                if (aStar == null || !aStar.TrySetOrigin(currentPosition))
                    return false;

                aStar.MaxClimbHeight = MaxClimbHeight;
                request = aStar;
                return true;

            case SolidPathAlgorithm.FlowField:
                var flowField = FlowFieldPathRequest.Create(
                    context,
                    ChartOriginPosition,
                    TargetPosition,
                    unitSize,
                    AllowUnwalkableEndpoints,
                    AllowTraversalTransitions);
                if (flowField == null || !flowField.TrySetOrigin(currentPosition))
                    return false;

                flowField.MaxClimbHeight = MaxClimbHeight;
                flowField.ExtraFloodRange = FlowFieldExtraFloodRange;
                request = flowField;
                return true;

            default:
                return false;
        }
    }

    public void RecordData(IChronicler chronicler)
    {
        RecordValues.Look(chronicler, ref TransitionId, "TransitionId", null);
        RecordValues.Look(chronicler, ref ChartOriginPosition, "ChartOriginPosition", Vector3d.Zero);
        RecordValues.Look(chronicler, ref TargetPosition, "TargetPosition", Vector3d.Zero);
        RecordValues.Look(chronicler, ref ChartPathMode, "ChartPathMode", SolidPathAlgorithm.AStar);
        RecordValues.Look(chronicler, ref AllowUnwalkableEndpoints, "AllowUnwalkableEndpoints", false);
        RecordValues.Look(chronicler, ref AllowTraversalTransitions, "AllowTraversalTransitions", false);
        RecordValues.Look(chronicler, ref MaxClimbHeight, "MaxClimbHeight", Fixed64.One);
        RecordValues.Look(chronicler, ref AStarHeuristic, "AStarHeuristic", HeuristicMethod.Manhattan);
        RecordValues.Look(chronicler, ref FlowFieldExtraFloodRange, "FlowFieldExtraFloodRange", FlowFieldPathRequest.DefaultExtraFloodRange);
        RecordValues.Look(chronicler, ref MovementGroupId, "MovementGroupId", -1);
        RecordValues.Look(chronicler, ref IsRequestingClimb, "IsRequestingClimb", false);
    }
}

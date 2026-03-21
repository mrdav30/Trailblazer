using FixedMathSharp;
using Trailblazer.Pathing;
using Trailblazer.Serialization;

namespace Trailblazer.Navigation;

/// <summary>
/// Stores the follow-up chart-backed leg for a navigator-owned volume exit handoff.
/// </summary>
internal sealed class GuidedVolumeExitHandoff : IRecordable
{
    public string TransitionId;

    public Vector3d ChartOriginPosition;

    public Vector3d TargetPosition;

    public GuidedPathMode ChartPathMode = GuidedPathMode.AStar;

    public bool AllowUnwalkable;

    public bool AllowTraversalTransitions;

    public HeuristicMethod AStarHeuristic = HeuristicMethod.Manhattan;

    public Fixed64 AStarMaxClimbHeight = Fixed64.One;

    public int FlowFieldExtraFloodRange = FlowFieldPathRequest.DefaultExtraFloodRange;

    public int MovementGroupId = -1;

    public bool IsValid =>
        !string.IsNullOrWhiteSpace(TransitionId)
        && (ChartPathMode == GuidedPathMode.AStar || ChartPathMode == GuidedPathMode.FlowField);

    public bool TryCreateFollowupRequest(
        Vector3d currentPosition,
        Fixed64 unitSize,
        out IPathRequest request)
    {
        request = null;
        if (!IsValid)
            return false;
        switch (ChartPathMode)
        {
            case GuidedPathMode.AStar:
                var aStar = AStarPathRequest.Create(
                    ChartOriginPosition,
                    TargetPosition,
                    unitSize,
                    AStarHeuristic,
                    AllowUnwalkable,
                    AllowTraversalTransitions);
                if (aStar == null || !aStar.TrySetOrigin(currentPosition))
                    return false;

                aStar.MaxClimbHeight = AStarMaxClimbHeight;
                request = aStar;
                return true;

            case GuidedPathMode.FlowField:
                var flowField = FlowFieldPathRequest.Create(
                    ChartOriginPosition,
                    TargetPosition,
                    unitSize,
                    AllowUnwalkable,
                    AllowTraversalTransitions);
                if (flowField == null || !flowField.TrySetOrigin(currentPosition))
                    return false;

                flowField.ExtraFloodRange = FlowFieldExtraFloodRange;
                request = flowField;
                return true;

            default:
                return false;
        }
    }

    public void RecordData(IChronicler chronicler)
    {
        string transitionId = TransitionId;
        Vector3d chartOriginPosition = ChartOriginPosition;
        Vector3d targetPosition = TargetPosition;
        GuidedPathMode chartPathMode = ChartPathMode;
        bool allowUnwalkable = AllowUnwalkable;
        bool allowTraversalTransitions = AllowTraversalTransitions;
        HeuristicMethod aStarHeuristic = AStarHeuristic;
        Fixed64 aStarMaxClimbHeight = AStarMaxClimbHeight;
        int flowFieldExtraFloodRange = FlowFieldExtraFloodRange;
        int movementGroupId = MovementGroupId;

        RecordValues.Look(chronicler, ref transitionId, "transitionId", null);
        RecordValues.Look(chronicler, ref chartOriginPosition, "chartOriginPosition", Vector3d.Zero);
        RecordValues.Look(chronicler, ref targetPosition, "targetPosition", Vector3d.Zero);
        RecordValues.Look(chronicler, ref chartPathMode, "chartPathMode", GuidedPathMode.AStar);
        RecordValues.Look(chronicler, ref allowUnwalkable, "allowUnwalkable", false);
        RecordValues.Look(chronicler, ref allowTraversalTransitions, "allowTraversalTransitions", false);
        RecordValues.Look(chronicler, ref aStarHeuristic, "aStarHeuristic", HeuristicMethod.Manhattan);
        RecordValues.Look(chronicler, ref aStarMaxClimbHeight, "aStarMaxClimbHeight", Fixed64.One);
        RecordValues.Look(chronicler, ref flowFieldExtraFloodRange, "flowFieldExtraFloodRange", FlowFieldPathRequest.DefaultExtraFloodRange);
        RecordValues.Look(chronicler, ref movementGroupId, "movementGroupId", -1);

        if (chronicler.Mode == SerializationMode.Loading)
        {
            TransitionId = transitionId;
            ChartOriginPosition = chartOriginPosition;
            TargetPosition = targetPosition;
            ChartPathMode = chartPathMode;
            AllowUnwalkable = allowUnwalkable;
            AllowTraversalTransitions = allowTraversalTransitions;
            AStarHeuristic = aStarHeuristic;
            AStarMaxClimbHeight = aStarMaxClimbHeight;
            FlowFieldExtraFloodRange = flowFieldExtraFloodRange;
            MovementGroupId = movementGroupId;
        }
    }
}

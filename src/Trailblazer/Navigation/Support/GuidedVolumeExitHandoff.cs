using Chronicler;
using FixedMathSharp;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>
/// Stores the follow-up chart-backed leg for a object-owned volume exit handoff.
/// </summary>
internal sealed class GuidedVolumeExitHandoff : IRecordable
{
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
        Vector3d currentPosition,
        Fixed64 unitSize,
        out IPathRequest? request)
    {
        request = null;
        if (!IsValid)
            return false;
        switch (ChartPathMode)
        {
            case SolidPathAlgorithm.AStar:
                var aStar = AStarPathRequest.Create(
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
        string? transitionId = TransitionId;
        Vector3d chartOriginPosition = ChartOriginPosition;
        Vector3d targetPosition = TargetPosition;
        SolidPathAlgorithm chartPathMode = ChartPathMode;
        bool allowUnwalkableEndpoints = AllowUnwalkableEndpoints;
        bool allowTraversalTransitions = AllowTraversalTransitions;
        Fixed64 maxClimbHeight = MaxClimbHeight;
        HeuristicMethod aStarHeuristic = AStarHeuristic;
        int flowFieldExtraFloodRange = FlowFieldExtraFloodRange;
        int movementGroupId = MovementGroupId;
        bool isRequestingClimb = IsRequestingClimb;

        RecordValues.Look(chronicler, ref transitionId, "transitionId", null);
        RecordValues.Look(chronicler, ref chartOriginPosition, "chartOriginPosition", Vector3d.Zero);
        RecordValues.Look(chronicler, ref targetPosition, "targetPosition", Vector3d.Zero);
        RecordValues.Look(chronicler, ref chartPathMode, "chartPathMode", SolidPathAlgorithm.AStar);
        RecordValues.Look(chronicler, ref allowUnwalkableEndpoints, "allowUnwalkableEndpoints", false);
        RecordValues.Look(chronicler, ref allowTraversalTransitions, "allowTraversalTransitions", false);
        RecordValues.Look(chronicler, ref maxClimbHeight, "maxClimbHeight", Fixed64.One);
        RecordValues.Look(chronicler, ref aStarHeuristic, "aStarHeuristic", HeuristicMethod.Manhattan);
        RecordValues.Look(chronicler, ref flowFieldExtraFloodRange, "flowFieldExtraFloodRange", FlowFieldPathRequest.DefaultExtraFloodRange);
        RecordValues.Look(chronicler, ref movementGroupId, "movementGroupId", -1);
        RecordValues.Look(chronicler, ref isRequestingClimb, "isRequestingClimb", false);

        if (chronicler.Mode == SerializationMode.Loading)
        {
            TransitionId = transitionId;
            ChartOriginPosition = chartOriginPosition;
            TargetPosition = targetPosition;
            ChartPathMode = chartPathMode;
            AllowUnwalkableEndpoints = allowUnwalkableEndpoints;
            AllowTraversalTransitions = allowTraversalTransitions;
            MaxClimbHeight = maxClimbHeight;
            AStarHeuristic = aStarHeuristic;
            FlowFieldExtraFloodRange = flowFieldExtraFloodRange;
            MovementGroupId = movementGroupId;
            IsRequestingClimb = isRequestingClimb;
        }
    }
}

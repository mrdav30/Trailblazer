//=======================================================================
// GuidedVolumeExitHandoff.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using Chronicler;
using FixedMathSharp;
using Trailblazer.Navigation.Steering;
using Trailblazer.Pathing;

namespace Trailblazer.Navigation;

/// <summary>
/// Stores the follow-up chart-backed leg for a object-owned volume exit handoff.
/// </summary>
internal sealed class GuidedVolumeExitHandoff : IRecordable
{
    private enum SerializedHandoffMode
    {
        Invalid = -1,
        FlowField = 1
    }

    public string? TransitionId;

    public Vector3d ChartOriginPosition;

    public Vector3d TargetPosition;

    public bool AllowUnwalkableEndpoints;

    public bool AllowTraversalTransitions;

    public Fixed64 MaxClimbHeight = Fixed64.One;

    public int FlowFieldExtraFloodRange = FlowFieldPathRequest.DefaultExtraFloodRange;

    public int MovementGroupId = -1;

    public bool IsRequestingClimb;

    public bool IsValid => !string.IsNullOrWhiteSpace(TransitionId);

    public bool TryCreateFollowupRequest(
        TrailblazerWorldContext context,
        Vector3d currentFootPosition,
        NavigationAgentProfile profile,
        out IPathRequest? request)
    {
        PathRequestContextResolver.ThrowIfUnusable(context);
        request = null;
        if (!IsValid)
            return false;

        Fixed64 unitSize = profile.Shape.Radius + profile.Shape.Radius;
        var flowField = FlowFieldPathRequest.Create(
            context,
            ChartOriginPosition,
            TargetPosition,
            unitSize,
            AllowUnwalkableEndpoints,
            AllowTraversalTransitions);
        if (flowField == null || !flowField.TrySetOrigin(currentFootPosition))
            return false;

        flowField.MaxClimbHeight = MaxClimbHeight;
        flowField.ExtraFloodRange = FlowFieldExtraFloodRange;
        request = flowField;
        return true;
    }

    public void RecordData(IChronicler chronicler)
    {
        SerializedHandoffMode serializedMode = chronicler.Mode == SerializationMode.Loading
            ? SerializedHandoffMode.Invalid
            : SerializedHandoffMode.FlowField;
        RecordValues.Look(chronicler, ref serializedMode, "ChartPathMode", SerializedHandoffMode.Invalid);
        RecordValues.Look(chronicler, ref TransitionId, "TransitionId", null);
        RecordValues.Look(chronicler, ref ChartOriginPosition, "ChartOriginPosition", Vector3d.Zero);
        RecordValues.Look(chronicler, ref TargetPosition, "TargetPosition", Vector3d.Zero);
        RecordValues.Look(chronicler, ref AllowUnwalkableEndpoints, "AllowUnwalkableEndpoints", false);
        RecordValues.Look(chronicler, ref AllowTraversalTransitions, "AllowTraversalTransitions", false);
        RecordValues.Look(chronicler, ref MaxClimbHeight, "MaxClimbHeight", Fixed64.One);
        RecordValues.Look(chronicler, ref FlowFieldExtraFloodRange, "FlowFieldExtraFloodRange", FlowFieldPathRequest.DefaultExtraFloodRange);
        RecordValues.Look(chronicler, ref MovementGroupId, "MovementGroupId", -1);
        RecordValues.Look(chronicler, ref IsRequestingClimb, "IsRequestingClimb", false);

        if (chronicler.Mode == SerializationMode.Loading)
        {
            if (serializedMode != SerializedHandoffMode.FlowField)
            {
                TransitionId = null;
            }
        }
    }
}

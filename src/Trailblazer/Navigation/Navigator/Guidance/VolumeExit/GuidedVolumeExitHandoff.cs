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
        Missing = -1,
        Invalid = 0,
        Graph = 2
    }

    public string? TransitionId;

    public int MovementGroupId = -1;

    public bool IsRequestingClimb;

    public PathQuery? FollowupQuery { get; set; }

    internal bool RejectedOnLoad { get; private set; }

    public bool IsValid => !string.IsNullOrWhiteSpace(TransitionId) && FollowupQuery.HasValue;

    public bool TryCreateFollowupQuery(
        Vector3d currentFootPosition,
        out PathQuery? query)
    {
        query = null;
        if (!IsValid || FollowupQuery is not PathQuery followup)
            return false;

        query = followup.WithStartPosition(currentFootPosition);
        return true;
    }

    public void RecordData(IChronicler chronicler)
    {
        var queryRecord = new PathQueryRecord();
        if (chronicler.Mode != SerializationMode.Loading)
            queryRecord.Capture(FollowupQuery, null);

        SerializedHandoffMode serializedMode = chronicler.Mode == SerializationMode.Loading
            ? SerializedHandoffMode.Missing
            : FollowupQuery.HasValue
                ? SerializedHandoffMode.Graph
                : SerializedHandoffMode.Invalid;
        string? transitionId = chronicler.Mode == SerializationMode.Loading ? null : TransitionId;
        int movementGroupId = chronicler.Mode == SerializationMode.Loading ? -1 : MovementGroupId;
        bool isRequestingClimb = chronicler.Mode != SerializationMode.Loading && IsRequestingClimb;
        RecordValues.Look(chronicler, ref serializedMode, "ChartPathMode", SerializedHandoffMode.Missing);
        RecordValues.Look(chronicler, ref transitionId, "TransitionId", null);
        RecordDeep.Look(chronicler, ref queryRecord, "FollowupQuery");
        RecordValues.Look(chronicler, ref movementGroupId, "MovementGroupId", -1);
        RecordValues.Look(chronicler, ref isRequestingClimb, "IsRequestingClimb", false);

        if (chronicler.Mode == SerializationMode.Loading)
        {
            RejectedOnLoad = false;
            if (serializedMode == SerializedHandoffMode.Invalid)
                return;

            if (serializedMode != SerializedHandoffMode.Graph
                || string.IsNullOrWhiteSpace(transitionId)
                || !queryRecord.TryCreateQuery(out PathQuery? query)
                || query is not PathQuery graphQuery
                || graphQuery.Algorithm != PathAlgorithm.FlowField
                || !graphQuery.AllowTransitions)
            {
                RejectedOnLoad = true;
                return;
            }

            TransitionId = transitionId;
            FollowupQuery = graphQuery;
            MovementGroupId = movementGroupId;
            IsRequestingClimb = isRequestingClimb;
        }
    }
}

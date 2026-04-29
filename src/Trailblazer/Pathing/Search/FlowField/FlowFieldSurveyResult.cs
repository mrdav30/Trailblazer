using GridForge.Spatial;
using SwiftCollections;
using System;

namespace Trailblazer.Pathing;

public class FlowFieldSurveyResult : SurveyResult
{
    public SwiftDictionary<WorldVoxelIndex, FlowField>? Fields { get; private set; }

    public override bool HasPath => IsValid && Fields != null && Fields.Count > 0;

    public static readonly FlowFieldSurveyResult Empty = new();

    public static FlowFieldSurveyResult Create(
        SwiftDictionary<WorldVoxelIndex, FlowField> fields,
        string[] chartsUtilized,
        int key)
    {
        return new FlowFieldSurveyResult()
        {
            IsValid = true,
            IsInUse = false,
            ChartsUtilized = chartsUtilized ?? Array.Empty<string>(),
            Fields = fields,
            LastUsedFrame = -1,
            RequestHashKey = key
        };
    }

    public override void Reset()
    {
        base.Reset();
        Fields = null;
    }
}

using SwiftCollections;

namespace Trailblazer.Pathing
{
    public struct FlowFieldSurveyResult : ISurveyResult
    {
        // key = voxel spawn token, value = vector flow field
        public SwiftDictionary<int, FlowField> Fields { get; private set; }

        public bool IsValid { get; set; }

        public readonly bool HasPath => Fields != null && Fields.Count > 0;

        public bool IsInUse { get; private set; }

        public int LastUsedFrame { get; private set; }

        public int RequestHashKey { get; private set; }

        public static readonly FlowFieldSurveyResult Empty = new();

        public static FlowFieldSurveyResult Create(SwiftDictionary<int, FlowField> fields, int key)
        {
            return new FlowFieldSurveyResult()
            {
                Fields = fields,
                IsInUse = false,
                LastUsedFrame = -1,
                RequestHashKey = key
            };
        }

        public void MarkInUse() => IsInUse = true;

        public void MarkInvalid()
        {
            IsValid = false;
            Fields = null;
        }

        public void Release()
        {
            IsInUse = false;
            LastUsedFrame = TrailblazerManager.FrameCount;
        }
    }
}

using FixedMathSharp;
using SwiftCollections;

namespace Trailblazer.Pathing
{
    public struct AStarSurveyResult : ISurveyResult
    {
        public SwiftList<Vector3d> Path { get; private set; }

        public readonly bool IsValid => Path != null && Path.Count > 0;

        public bool IsInUse { get; private set; }

        public int LastUsedFrame { get; private set; }

        public int RequestHashKey { get; private set; }

        public static readonly AStarSurveyResult Empty = new AStarSurveyResult();

        public static AStarSurveyResult Create(SwiftList<Vector3d> path, int key)
        {
            return new AStarSurveyResult()
            {
                Path = path,
                IsInUse = false,
                LastUsedFrame = -1,
                RequestHashKey = key
            };
        }

        public void MarkInUse() => IsInUse = true;

        public void Release()
        {
            IsInUse = false;
            LastUsedFrame = TrailblazerManager.FrameCount;
        }
    }
}

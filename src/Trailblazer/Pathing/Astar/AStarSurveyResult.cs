namespace Trailblazer.Pathing
{
    public struct AStarSurveyResult : ISurveyResult
    {
        public AStarWaypoint[] Waypoints { get; private set; }

        public readonly bool IsValid => Waypoints != null && Waypoints.Length > 0;

        public bool IsInUse { get; private set; }

        public int LastUsedFrame { get; private set; }

        public int RequestHashKey { get; private set; }

        public static readonly AStarSurveyResult Empty = new AStarSurveyResult();

        public static AStarSurveyResult Create(AStarWaypoint[] waypoints, int key)
        {
            return new AStarSurveyResult()
            {
                Waypoints = waypoints,
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

using System;

namespace Trailblazer.Pathing
{
    public class AStarSurveyResult : SurveyResult
    {
        public AStarWaypoint[] Waypoints { get; private set; }

        public override bool HasPath => IsValid && Waypoints != null && Waypoints.Length > 0;

        public static readonly AStarSurveyResult Empty = new();

        public static AStarSurveyResult Create(
            AStarWaypoint[] waypoints, 
            string[] chartsUtilized, 
            int key)
        {
            return new AStarSurveyResult()
            {
                IsValid = true,
                IsInUse = false,
                ChartsUtilized = chartsUtilized ?? Array.Empty<string>(),
                Waypoints = waypoints,
                LastUsedFrame = -1,
                RequestHashKey = key
            };
        }

        public override void Reset()
        {
            base.Reset();
            Waypoints = null;
        }
    }
}

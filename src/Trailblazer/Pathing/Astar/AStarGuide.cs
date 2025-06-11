using FixedMathSharp;
using SwiftCollections;
using System;

namespace Trailblazer.Pathing
{
    public class AStarGuide : IWaypointGuide
    {
        public AStarSurveyResult TrailMap { get; private set; }

        /// <summary>
        /// Index or key used to track the agent’s progress along the trail.
        /// </summary>
        public int CurrentWaypointIndex { get; private set; }

        private int _lastTriedIndex;

        public bool Initialize(AStarSurveyResult surveyResult)
        {
            if (!surveyResult.IsValid)
                return false;

            TrailMap = surveyResult;
            CurrentWaypointIndex = 0;

            return true;
        }

        public bool HasArrived()
        {
            return TrailMap.IsValid && CurrentWaypointIndex == TrailMap.Path.Count - 1;
        }

        public int GetIndex(Vector3d from)
        {
            Fixed64 minDistSq = Fixed64.MAX_VALUE;
            int bestIndex = -1;
            for (int i = 0; i < TrailMap.Path.Count; i++)
            {
                Fixed64 distSq = (from - TrailMap.Path[i]).SqrMagnitude;
                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    bestIndex = i;
                }

                if (minDistSq <= Fixed64.Epsilon)
                    break;
            }

            return bestIndex;
        }

        public void AdvanceWaypoint() => CurrentWaypointIndex++;

        public bool TryGetMovementDirection(Vector3d origin,  out Vector3d direction)
        {
            direction = Vector3d.Zero;

            if (!TrailMap.IsValid)
                return false;

            int closestIndex = GetIndex(origin);
            if (closestIndex == -1)
                return false;

            direction = (TrailMap.Path[closestIndex] - origin).Normalize();
            return true;
        }

        public Vector3d GetMovementDirection(Vector3d origin)
        {
            if (!TrailMap.IsValid || CurrentWaypointIndex < 0 || CurrentWaypointIndex >= TrailMap.Path.Count)
                return Vector3d.Zero;

            Vector3d movementDirection = TrailMap.Path[CurrentWaypointIndex];
            if (movementDirection == Vector3d.Zero)
                return Vector3d.Zero;

            return (movementDirection - origin).Normal;
        }

        public bool TryGetFallbackDirection(Vector3d from, out Vector3d fallbackDirection)
        {
            fallbackDirection = Vector3d.Zero;

            if (TrailMap.Path.Count == 0)
                return false;

            // Start from CurrentIndex + 1 and search forward
            int searchStart = Util.Clamp(_lastTriedIndex, 0, TrailMap.Path.Count - 1);
                
            Fixed64 minDistSq = Fixed64.MAX_VALUE;
            int bestIndex = -1;

            for (int i = searchStart; i < TrailMap.Path.Count; i++)
            {
                Fixed64 distSq = (from - TrailMap.Path[i]).SqrMagnitude;
                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    bestIndex = i;
                }
            }

            if (bestIndex >= 0)
            {
                fallbackDirection = (TrailMap.Path[bestIndex] - from).Normal;
                _lastTriedIndex = bestIndex;
                return true;
            }

            return false;
        }

        public bool TryGetWaypointAt(int index, out Vector3d waypoint)
        {
            if (!TrailMap.IsValid || index < 0 || index >= TrailMap.Path.Count)
            {
                waypoint = Vector3d.Zero;
                return false;
            }

            waypoint = TrailMap.Path[index];
            return true;
        }
    }
}

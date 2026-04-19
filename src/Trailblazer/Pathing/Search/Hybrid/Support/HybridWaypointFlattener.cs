using SwiftCollections;
using System;

namespace Trailblazer.Pathing;

/// <summary>
/// Flattens staged hybrid route steps into a single waypoint stream for cached hybrid guides.
/// </summary>
internal static class HybridWaypointFlattener
{
    public static bool TryBuild(
        HybridRoutePlan routePlan,
        out AStarWaypoint[] flattenedWaypoints,
        out string[] chartKeys)
    {
        flattenedWaypoints = null;
        chartKeys = Array.Empty<string>();
        if (routePlan == null)
            return false;

        SwiftList<AStarWaypoint> waypoints = new();
        SwiftList<IGuide> borrowedGuides = new();
        SwiftList<string> utilizedCharts = new();
        SwiftHashSet<string> utilizedChartSet = new();
        int pathCostOffset = 0;

        try
        {
            for (int i = 0; i < routePlan.Steps.Length; i++)
            {
                HybridRouteStep step = routePlan.Steps[i];
                switch (step.Kind)
                {
                    case HybridRouteStepKind.Waypoint:
                        pathCostOffset += step.AdditionalCost;
                        AppendWaypoint(
                            waypoints,
                            new AStarWaypoint
                            {
                                Position = step.WaypointPosition,
                                PathCost = pathCostOffset
                            });
                        break;

                    case HybridRouteStepKind.PathSegment:
                        if (!TryAppendSegmentWaypoints(
                            step.SegmentRequest,
                            waypoints,
                            borrowedGuides,
                            utilizedCharts,
                            utilizedChartSet,
                            ref pathCostOffset))
                        {
                            return false;
                        }
                        break;
                }
            }

            if (waypoints.Count == 0)
                return false;

            flattenedWaypoints = waypoints.ToArray();
            for (int i = 0; i < flattenedWaypoints.Length; i++)
                flattenedWaypoints[i].IsGoal = false;

            flattenedWaypoints[^1].IsGoal = true;
            chartKeys = utilizedCharts.ToArray();
            return true;
        }
        finally
        {
            for (int i = 0; i < borrowedGuides.Count; i++)
                PathGuideFactory.ReturnGuide(borrowedGuides[i]);
        }
    }

    private static bool TryAppendSegmentWaypoints(
        IPathRequest request,
        SwiftList<AStarWaypoint> destination,
        SwiftList<IGuide> borrowedGuides,
        SwiftList<string> utilizedCharts,
        SwiftHashSet<string> utilizedChartSet,
        ref int pathCostOffset)
    {
        switch (request)
        {
            case AStarPathRequest aStarRequest:
                AStarSurveyResult aStarResult = AStarSurveyor.Shared.FindPath(aStarRequest);
                if (!aStarResult.HasPath)
                    return false;

                AddChartKeys(utilizedCharts, utilizedChartSet, aStarResult.ChartsUtilized);
                AppendWaypoints(destination, aStarResult.Waypoints, ref pathCostOffset);
                return true;

            case VolumePathRequest volumeRequest:
                VolumeGuide volumeGuide = PathGuideFactory.RequestVolume(volumeRequest);
                if (volumeGuide == null)
                    return false;

                borrowedGuides.Add(volumeGuide);
                AppendWaypoints(destination, volumeGuide.ActiveWaypoints, ref pathCostOffset);
                return true;

            default:
                return false;
        }
    }

    private static void AppendWaypoints(
        SwiftList<AStarWaypoint> destination,
        AStarWaypoint[] waypoints,
        ref int pathCostOffset)
    {
        if (waypoints == null)
            return;

        for (int i = 0; i < waypoints.Length; i++)
        {
            AStarWaypoint waypoint = waypoints[i];
            waypoint.PathCost += pathCostOffset;
            AppendWaypoint(destination, waypoint);
        }

        if (destination.Count > 0)
            pathCostOffset = destination[destination.Count - 1].PathCost;
    }

    private static void AddChartKeys(
        SwiftList<string> destination,
        SwiftHashSet<string> utilizedChartSet,
        string[] charts)
    {
        if (charts == null)
            return;

        for (int i = 0; i < charts.Length; i++)
        {
            string chart = charts[i];
            if (string.IsNullOrEmpty(chart) || !utilizedChartSet.Add(chart))
                continue;

            destination.Add(chart);
        }
    }

    private static void AppendWaypoint(
        SwiftList<AStarWaypoint> destination,
        AStarWaypoint waypoint)
    {
        if (destination.Count > 0)
        {
            AStarWaypoint last = destination[destination.Count - 1];
            if (last.GlobalIndex.HasValue
                && waypoint.GlobalIndex.HasValue
                && last.GlobalIndex.Value.Equals(waypoint.GlobalIndex.Value))
            {
                return;
            }

            if (last.Position == waypoint.Position)
                return;
        }

        waypoint.IsGoal = false;
        destination.Add(waypoint);
    }
}

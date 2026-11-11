//=======================================================================
// NavigationPlanarRouteWork.SideView.cs
//=======================================================================
// MIT License, Copyright (c) 2026-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System.Diagnostics;
using FixedMathSharp;

namespace Trailblazer.Pathing;

internal sealed partial class NavigationPlanarRouteWork
{
    private NavigationGuideStatus AdvanceSideViewPoints(NavigationSideViewSearch sideView,
        ref NavigationAcquisitionSlice remaining)
    {
        // Count before reserving exact payload bytes. Each counted or emitted support takes
        // one work step. Emission writes its optional bend and anchor together.
        while (sideView.RouteSupportCursor < _length)
        {
            if (remaining.LookupRemaining == 0 || remaining.NodeRemaining == 0)
                return NavigationGuideStatus.Pending;
            if (!_meter!.TryConsumeLookupProbes(1))
                return NavigationGuideStatus.BudgetExceeded;
            remaining.LookupRemaining--;
            remaining.NodeRemaining--;
            int index = sideView.RouteSupportCursor++;
            var address = GetAddress(index);
            var support = GetRouteSupport(address);
            bool hasBend = index != 0 && !NavigationPlanarWork.SideViewSupportsMeet(sideView.PreviousRouteSupport, support);
            if (_points == null)
            {
                if (hasBend)
                    sideView.RouteBendCount++;
            }
            else
                WriteSideViewAnchor(sideView.Profile, address, support, hasBend);
            sideView.PreviousRouteSupport = support;
        }
        return NavigationGuideStatus.Success;
    }

    private void WriteSideViewAnchor(in NavigationAgentProfile2D profile, NavigationCellAddress address,
        in NavigationPlanarSupport support, bool hasBend)
    {
        Fixed64 embedding = _query.Start.FootAnchor.Y;
        var status = NavigationPlanarWork.MaterializeSupportAnchor(support, profile,
            NavigationGuideStatus.Stale, out Vector2d center);
        Debug.Assert(status == NavigationGuideStatus.Success);
        var points = _points!;
        if (hasBend)
        {
            var previousPoint = points[_pointWrite - 1];
            Vector2d previous = previousPoint.Position.ToVector2d();
            Vector2d bend = NavigationPlanarWork.GetSideViewStepWaypoint(previous, center);
            var owner = previous.Y < center.Y ? previousPoint.Address : address;
            points[_pointWrite++] = new NavigationAStarGuidePoint(owner,
                new Vector3d(bend.X, embedding, bend.Y), TraversalMedium.Solid,
                NavigationAStarGuidePoint.SideViewBendOrdinal);
        }
        points[_pointWrite++] = new NavigationAStarGuidePoint(address, new Vector3d(center.X, embedding, center.Y),
            TraversalMedium.Solid);
    }

    private NavigationPlanarSupport GetRouteSupport(NavigationCellAddress address)
    {
        // Search already certified these support addresses in this immutable publication.
        var graph = _query.Graph;
        var map = graph.GetInstance(graph.FindMapOrdinal(address.MapId));
        bool found = map.Map.TryGetPlanarSupport(address.Index, out var support);
        Debug.Assert(found);
        return support;
    }
}

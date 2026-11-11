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
    private bool TryCountSideViewPoints(out int count)
    {
        count = _length;
        NavigationPlanarSupport previous = default;
        for (int i = 0; i < _length; i++)
        {
            if (!_meter!.TryConsumeLookupProbes(1))
                return false;
            var support = GetRouteSupport(GetAddress(i));
            if (i != 0 && !NavigationPlanarWork.SideViewSupportsMeet(previous, support))
                count++;
            previous = support;
        }
        return true;
    }

    private bool TryWriteSideViewPoints(in NavigationAgentProfile2D profile, NavigationAStarGuidePoint[] points)
    {
        NavigationPlanarSupport previousSupport = default;
        NavigationAStarGuidePoint previousPoint = default;
        Fixed64 embedding = _query.Start.FootAnchor.Y;
        int written = 0;
        for (int i = 0; i < _length; i++)
        {
            if (!_meter!.TryConsumeLookupProbes(1))
                return false;
            var address = GetAddress(i);
            var support = GetRouteSupport(address);
            var status = NavigationPlanarWork.MaterializeSupportAnchor(support, profile,
                NavigationGuideStatus.Stale, out Vector2d center);
            Debug.Assert(status == NavigationGuideStatus.Success);
            if (i != 0 && !NavigationPlanarWork.SideViewSupportsMeet(previousSupport, support))
            {
                Vector2d previous = previousPoint.Position.ToVector2d();
                Vector2d bend = NavigationPlanarWork.GetSideViewStepWaypoint(previous, center);
                var owner = previous.Y < center.Y ? previousPoint.Address : address;
                points[written++] = new NavigationAStarGuidePoint(owner,
                    new Vector3d(bend.X, embedding, bend.Y), TraversalMedium.Solid);
            }
            previousSupport = support;
            previousPoint = new NavigationAStarGuidePoint(address, new Vector3d(center.X, embedding, center.Y),
                TraversalMedium.Solid);
            points[written++] = previousPoint;
        }
        Debug.Assert(written == points.Length);
        return true;
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

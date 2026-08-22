using Trailblazer.Pathing;

namespace Trailblazer.Tests.Pathing.Graph;

internal static class NavigationAStarPayloadCacheTestExtensions
{
    internal static bool TryCheckoutReserved(
        this NavigationAStarPayloadCache cache,
        NavigationAStarPayloadKey key,
        NavigationWorldGraph graph,
        NavigationAStarWorkspace workspace,
        out NavigationAStarPayloadLease lease) => cache.TryCheckoutReserved(
        key,
        graph,
        NavigationAStarPayload.GetMaximumRetainedBytes(
            workspace.GuidePoints.Length,
            workspace.PathNodes.Length - 1,
            workspace.EndpointComponents.Length,
            workspace.EndpointPages.Length),
        out lease);

    internal static bool TryCheckoutReserved(
        this NavigationAStarPayloadCache cache,
        NavigationAStarPayloadKey key,
        NavigationWorldGraph graph,
        long maximumPayloadBytes,
        out NavigationAStarPayloadLease lease)
    {
        if (!cache.TryReservePayload(
                maximumPayloadBytes,
                out NavigationAStarPayloadReservation reservation))
        {
            lease = null!;
            return false;
        }

        try
        {
            return cache.TryCheckoutReserved(key, graph, ref reservation, out lease);
        }
        finally
        {
            cache.ReleasePayloadReservation(ref reservation);
        }
    }
}

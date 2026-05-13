using FixedMathSharp;
using System;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks;

/// <summary>
/// Preflight checks that validate benchmark scenario state before measurement begins.
/// A failed preflight throws <see cref="InvalidOperationException"/> so the benchmark
/// fails fast rather than silently measuring a broken setup.
/// </summary>
internal static class BenchmarkPreflight
{
    /// <summary>
    /// Verifies that an A* guide request for the given endpoints succeeds and the resulting
    /// guide has at least one waypoint. Returns the guide to the factory.
    /// Throws if the request cannot be created or the guide is invalid.
    /// </summary>
    public static void AssertAStarRouteExists(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize)
    {
        AStarPathRequest request = AStarPathRequest.Create(context, origin, destination, unitSize)
            ?? throw new InvalidOperationException(
                $"Preflight: AStarPathRequest.Create returned null for {origin} -> {destination}. " +
                "Verify the chart is registered and both endpoints are walkable.");

        if (!context.Guides.RequestGuide(request, out AStarGuide guide))
            throw new InvalidOperationException(
                $"Preflight: A* guide request failed for {origin} -> {destination}. " +
                "Verify both endpoints are on the walkable surface.");

        if (guide.ActiveWaypoints == null || guide.ActiveWaypoints.Length == 0)
            throw new InvalidOperationException(
                $"Preflight: A* guide for {origin} -> {destination} returned an empty waypoint list.");

        context.Guides.ReturnGuide(guide);
    }

    /// <summary>
    /// Verifies that a flow-field guide request for the given endpoints succeeds and the
    /// resulting guide reports a valid field. Returns the guide to the factory.
    /// Throws if the request cannot be created or the guide is invalid.
    /// </summary>
    public static void AssertFlowFieldRouteExists(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize)
    {
        FlowFieldPathRequest request = FlowFieldPathRequest.Create(context, origin, destination, unitSize)
            ?? throw new InvalidOperationException(
                $"Preflight: FlowFieldPathRequest.Create returned null for {origin} -> {destination}. " +
                "Verify the chart is registered and both endpoints are walkable.");

        if (!context.Guides.RequestGuide(request, out FlowFieldGuide guide))
            throw new InvalidOperationException(
                $"Preflight: Flow-field guide request failed for {origin} -> {destination}. " +
                "Verify both endpoints are on the walkable surface.");

        context.Guides.ReturnGuide(guide);
    }

    /// <summary>
    /// Verifies that a flow-field guide request fails for the given endpoints.
    /// Used to confirm choke-point or invalid route scenarios are set up correctly.
    /// Throws if the request unexpectedly succeeds.
    /// </summary>
    public static void AssertFlowFieldRouteBlocked(
        TrailblazerWorldContext context,
        Vector3d origin,
        Vector3d destination,
        Fixed64 unitSize)
    {
        FlowFieldPathRequest request = FlowFieldPathRequest.Create(context, origin, destination, unitSize);
        if (request == null)
            return; // Expected — the request itself could not be formed.

        bool succeeded = context.Guides.RequestGuide(request, out FlowFieldGuide guide);
        if (succeeded)
        {
            context.Guides.ReturnGuide(guide);
            throw new InvalidOperationException(
                $"Preflight: Flow-field route unexpectedly succeeded for {origin} -> {destination} " +
                $"with unit size {unitSize}. The choke should block size > 1.");
        }
    }

    /// <summary>
    /// Verifies that all guide caches are empty after setup preflight.
    /// Call after returning all preflight guides to confirm no guides are left in use.
    /// </summary>
    public static void AssertNoCacheLeak(TrailblazerWorldContext context)
    {
        if (context.Guides.AnyInUse)
            throw new InvalidOperationException(
                "Preflight: One or more guides remain checked out after preflight. " +
                "Return all guides before measurement begins.");
    }
}

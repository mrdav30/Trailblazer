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
    public static void AssertAStarRouteExists(Vector3d origin, Vector3d destination, Fixed64 unitSize)
    {
        AStarPathRequest request = AStarPathRequest.Create(origin, destination, unitSize)
            ?? throw new InvalidOperationException(
                $"Preflight: AStarPathRequest.Create returned null for {origin} -> {destination}. " +
                "Verify the chart is registered and both endpoints are walkable.");

        if (!PathGuideFactory.RequestGuide(request, out AStarGuide guide))
            throw new InvalidOperationException(
                $"Preflight: A* guide request failed for {origin} -> {destination}. " +
                "Verify both endpoints are on the walkable surface.");

        if (guide.ActiveWaypoints == null || guide.ActiveWaypoints.Length == 0)
            throw new InvalidOperationException(
                $"Preflight: A* guide for {origin} -> {destination} returned an empty waypoint list.");

        PathGuideFactory.ReturnGuide(guide);
    }

    /// <summary>
    /// Verifies that a flow-field guide request for the given endpoints succeeds and the
    /// resulting guide reports a valid field. Returns the guide to the factory.
    /// Throws if the request cannot be created or the guide is invalid.
    /// </summary>
    public static void AssertFlowFieldRouteExists(Vector3d origin, Vector3d destination, Fixed64 unitSize)
    {
        FlowFieldPathRequest request = FlowFieldPathRequest.Create(origin, destination, unitSize)
            ?? throw new InvalidOperationException(
                $"Preflight: FlowFieldPathRequest.Create returned null for {origin} -> {destination}. " +
                "Verify the chart is registered and both endpoints are walkable.");

        if (!PathGuideFactory.RequestGuide(request, out FlowFieldGuide guide))
            throw new InvalidOperationException(
                $"Preflight: Flow-field guide request failed for {origin} -> {destination}. " +
                "Verify both endpoints are on the walkable surface.");

        PathGuideFactory.ReturnGuide(guide);
    }

    /// <summary>
    /// Verifies that a flow-field guide request fails for the given endpoints.
    /// Used to confirm choke-point or invalid route scenarios are set up correctly.
    /// Throws if the request unexpectedly succeeds.
    /// </summary>
    public static void AssertFlowFieldRouteBlocked(Vector3d origin, Vector3d destination, Fixed64 unitSize)
    {
        FlowFieldPathRequest request = FlowFieldPathRequest.Create(origin, destination, unitSize);
        if (request == null)
            return; // Expected — the request itself could not be formed.

        bool succeeded = PathGuideFactory.RequestGuide(request, out FlowFieldGuide guide);
        if (succeeded)
        {
            PathGuideFactory.ReturnGuide(guide);
            throw new InvalidOperationException(
                $"Preflight: Flow-field route unexpectedly succeeded for {origin} -> {destination} " +
                $"with unit size {unitSize}. The choke should block size > 1.");
        }
    }

    /// <summary>
    /// Verifies that all guide caches are empty after setup preflight.
    /// Call after returning all preflight guides to confirm no guides are left in use.
    /// </summary>
    public static void AssertNoCacheLeak()
    {
        if (PathGuideFactory.AnyInUse)
            throw new InvalidOperationException(
                "Preflight: One or more guides remain checked out after preflight. " +
                "Return all guides before measurement begins.");
    }
}

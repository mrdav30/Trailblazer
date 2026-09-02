//=======================================================================
// NavigationEndpoint.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>
/// Describes one immutable world-space endpoint of a path query.
/// </summary>
public readonly struct NavigationEndpoint : IEquatable<NavigationEndpoint>
{
    /// <summary>
    /// Gets the exact requested foot position in world space.
    /// </summary>
    public Vector3d Position { get; }

    /// <summary>
    /// Gets the optional stable map identifier used to filter endpoint candidates.
    /// </summary>
    public string? MapId { get; }

    /// <summary>
    /// Gets the endpoint resolution policy.
    /// </summary>
    public EndpointResolutionPolicy Resolution { get; }

    /// <summary>
    /// Gets the non-negative maximum resolution distance in world units.
    /// </summary>
    public Fixed64 MaxResolutionDistance { get; }

    /// <summary>
    /// Creates an immutable navigation endpoint.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when a supplied map identifier is empty or whitespace.</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when the policy is unknown or the distance is negative.</exception>
    public NavigationEndpoint(
        Vector3d position,
        string? mapId = null,
        EndpointResolutionPolicy resolution = EndpointResolutionPolicy.Strict,
        Fixed64 maxResolutionDistance = default)
    {
        SwiftThrowHelper.ThrowIfArgument(
            mapId != null && string.IsNullOrWhiteSpace(mapId),
            nameof(mapId),
            "Map identifier cannot be empty or whitespace.");
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            resolution is not EndpointResolutionPolicy.Strict and not EndpointResolutionPolicy.NearestNavigable,
            (int)resolution,
            nameof(resolution),
            "Endpoint resolution policy is unknown.");
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(
            maxResolutionDistance < Fixed64.Zero,
            actualValue: null,
            nameof(maxResolutionDistance),
            "Maximum resolution distance cannot be negative.");

        Position = position;
        MapId = mapId;
        Resolution = resolution;
        MaxResolutionDistance = maxResolutionDistance;
    }

    /// <inheritdoc/>
    public bool Equals(NavigationEndpoint other) =>
        Position == other.Position
        && string.Equals(MapId, other.MapId, StringComparison.Ordinal)
        && Resolution == other.Resolution
        && MaxResolutionDistance == other.MaxResolutionDistance;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NavigationEndpoint other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int mapIdHash = MapId == null
            ? 0
            : SwiftHashTools.GetDeterministicStringEqualityComparer().GetHashCode(MapId);
        int hash = SwiftHashTools.CombineHashCodes(Position.X.GetHashCode(), Position.Y.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, Position.Z.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, mapIdHash);
        hash = SwiftHashTools.CombineHashCodes(hash, (int)Resolution);
        return SwiftHashTools.CombineHashCodes(hash, MaxResolutionDistance.GetHashCode());
    }

    /// <summary>
    /// Returns whether two endpoints have exactly equal values.
    /// </summary>
    public static bool operator ==(NavigationEndpoint left, NavigationEndpoint right) => left.Equals(right);

    /// <summary>
    /// Returns whether two endpoints differ.
    /// </summary>
    public static bool operator !=(NavigationEndpoint left, NavigationEndpoint right) => !left.Equals(right);

}

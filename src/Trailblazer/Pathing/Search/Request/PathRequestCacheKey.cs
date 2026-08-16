//=======================================================================
// PathRequestCacheKey.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using FixedMathSharp;
using GridForge.Spatial;

namespace Trailblazer.Pathing;

/// <summary>Exact immutable identity for a retained volume path request.</summary>
public readonly struct PathRequestCacheKey : IEquatable<PathRequestCacheKey>
{
    private readonly WorldVoxelIndex _origin;
    private readonly WorldVoxelIndex _destination;
    private readonly Fixed64 _unitSize;
    private readonly bool _allowUnwalkableEndpoints;
    private readonly HeuristicMethod _heuristic;
    private readonly TraversalMedium _medium;
    private readonly int _maxPathSearchRange;
    private readonly int _volumeRulesRegistryVersion;
    private readonly int _hashCode;

    /// <summary>Gets whether this key represents a fully initialized request.</summary>
    public bool IsInitialized { get; }

    private PathRequestCacheKey(
        WorldVoxelIndex origin,
        WorldVoxelIndex destination,
        Fixed64 unitSize,
        bool allowUnwalkableEndpoints,
        HeuristicMethod heuristic,
        TraversalMedium medium,
        int maxPathSearchRange,
        int volumeRulesRegistryVersion)
    {
        _origin = origin;
        _destination = destination;
        _unitSize = unitSize;
        _allowUnwalkableEndpoints = allowUnwalkableEndpoints;
        _heuristic = heuristic;
        _medium = medium;
        _maxPathSearchRange = maxPathSearchRange;
        _volumeRulesRegistryVersion = volumeRulesRegistryVersion;
        IsInitialized = true;

        PathRequestHashBuilder hash = PathRequestHashBuilder.Create();
        hash.Add(origin.GetHashCode());
        hash.Add(destination.GetHashCode());
        hash.Add(unitSize.GetHashCode());
        hash.Add(allowUnwalkableEndpoints);
        hash.Add((int)heuristic);
        hash.Add((int)medium);
        hash.Add(maxPathSearchRange);
        hash.Add(volumeRulesRegistryVersion);
        _hashCode = hash.ToHashCode();
    }

    internal static PathRequestCacheKey CreateVolume(
        WorldVoxelIndex origin,
        WorldVoxelIndex destination,
        Fixed64 unitSize,
        bool allowUnwalkableEndpoints,
        HeuristicMethod heuristic,
        TraversalMedium traversalMedium,
        int maxPathSearchRange,
        int volumeRulesRegistryVersion) =>
        new(
            origin,
            destination,
            unitSize,
            allowUnwalkableEndpoints,
            heuristic,
            traversalMedium,
            maxPathSearchRange,
            volumeRulesRegistryVersion);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(PathRequestCacheKey other) =>
        IsInitialized == other.IsInitialized
        && (!IsInitialized
            || (_origin.Equals(other._origin)
                && _destination.Equals(other._destination)
                && _unitSize == other._unitSize
                && _allowUnwalkableEndpoints == other._allowUnwalkableEndpoints
                && _heuristic == other._heuristic
                && _medium == other._medium
                && _maxPathSearchRange == other._maxPathSearchRange
                && _volumeRulesRegistryVersion == other._volumeRulesRegistryVersion));

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is PathRequestCacheKey other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => _hashCode;

    /// <summary>Returns whether two request cache keys have exact identity.</summary>
    public static bool operator ==(PathRequestCacheKey left, PathRequestCacheKey right) => left.Equals(right);

    /// <summary>Returns whether two request cache keys have different identity.</summary>
    public static bool operator !=(PathRequestCacheKey left, PathRequestCacheKey right) => !left.Equals(right);
}

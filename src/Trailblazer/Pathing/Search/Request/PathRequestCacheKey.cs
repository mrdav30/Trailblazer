using FixedMathSharp;
using GridForge.Spatial;
using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>
/// Exact immutable identity for a path request cached by Trailblazer.
/// </summary>
/// <remarks>
/// Hash codes are used only for bucket placement. Equality compares the full world-scoped voxel
/// identities and every request option that affects the corresponding survey result.
/// </remarks>
public readonly struct PathRequestCacheKey : IEquatable<PathRequestCacheKey>
{
    private enum RequestFamily : byte
    {
        None,
        AStar,
        FlowField,
        Volume,
        Hybrid,
        FlowFieldHybridFallback
    }

    private readonly RequestFamily _family;
    private readonly WorldVoxelIndex _origin;
    private readonly WorldVoxelIndex _destination;
    private readonly Vector3d _exactOrigin;
    private readonly Vector3d _exactTargetPosition;
    private readonly Fixed64 _unitSize;
    private readonly Fixed64 _maxClimbHeight;
    private readonly bool _allowUnwalkableEndpoints;
    private readonly bool _allowTraversalTransitions;
    private readonly int _heuristic;
    private readonly int _traversalMedium;
    private readonly int _maxPathSearchRange;
    private readonly int _extraFloodRange;
    private readonly int _transitionRegistryVersion;
    private readonly int _volumeRulesRegistryVersion;
    private readonly int _hybridChartRequestKind;
    private readonly string[]? _transitionIds;
    private readonly int _hashCode;

    /// <summary>
    /// Gets whether this key represents a fully initialized path request.
    /// </summary>
    public bool IsInitialized { get; }

    private PathRequestCacheKey(
        RequestFamily family,
        WorldVoxelIndex origin,
        WorldVoxelIndex destination,
        Fixed64 unitSize,
        Fixed64 maxClimbHeight,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        int heuristic,
        int traversalMedium,
        int maxPathSearchRange,
        int extraFloodRange,
        int transitionRegistryVersion,
        int volumeRulesRegistryVersion,
        int hybridChartRequestKind,
        string[]? transitionIds,
        Vector3d exactOrigin,
        Vector3d exactTargetPosition)
    {
        _family = family;
        _origin = origin;
        _destination = destination;
        _exactOrigin = exactOrigin;
        _exactTargetPosition = exactTargetPosition;
        _unitSize = unitSize;
        _maxClimbHeight = maxClimbHeight;
        _allowUnwalkableEndpoints = allowUnwalkableEndpoints;
        _allowTraversalTransitions = allowTraversalTransitions;
        _heuristic = heuristic;
        _traversalMedium = traversalMedium;
        _maxPathSearchRange = maxPathSearchRange;
        _extraFloodRange = extraFloodRange;
        _transitionRegistryVersion = transitionRegistryVersion;
        _volumeRulesRegistryVersion = volumeRulesRegistryVersion;
        _hybridChartRequestKind = hybridChartRequestKind;
        _transitionIds = transitionIds;
        IsInitialized = true;
        _hashCode = ComputeHashCode(
            family,
            origin,
            destination,
            unitSize,
            maxClimbHeight,
            allowUnwalkableEndpoints,
            allowTraversalTransitions,
            heuristic,
            traversalMedium,
            maxPathSearchRange,
            extraFloodRange,
            transitionRegistryVersion,
            volumeRulesRegistryVersion,
            hybridChartRequestKind,
            transitionIds,
            exactOrigin,
            exactTargetPosition);
    }

    internal static PathRequestCacheKey CreateAStar(
        WorldVoxelIndex origin,
        WorldVoxelIndex destination,
        Fixed64 unitSize,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        HeuristicMethod heuristic,
        Fixed64 maxClimbHeight,
        int maxPathSearchRange,
        int transitionRegistryVersion) =>
        new(
            RequestFamily.AStar,
            origin,
            destination,
            unitSize,
            maxClimbHeight,
            allowUnwalkableEndpoints,
            allowTraversalTransitions,
            (int)heuristic,
            traversalMedium: 0,
            maxPathSearchRange,
            extraFloodRange: 0,
            allowTraversalTransitions ? transitionRegistryVersion : 0,
            volumeRulesRegistryVersion: 0,
            hybridChartRequestKind: 0,
            transitionIds: null,
            exactOrigin: default,
            exactTargetPosition: default);

    internal static PathRequestCacheKey CreateFlowField(
        WorldVoxelIndex destination,
        Fixed64 unitSize,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        Fixed64 maxClimbHeight,
        int extraFloodRange,
        int maxPathSearchRange,
        int transitionRegistryVersion) =>
        new(
            RequestFamily.FlowField,
            origin: default,
            destination,
            unitSize,
            maxClimbHeight,
            allowUnwalkableEndpoints,
            allowTraversalTransitions,
            heuristic: 0,
            traversalMedium: 0,
            maxPathSearchRange,
            extraFloodRange,
            allowTraversalTransitions ? transitionRegistryVersion : 0,
            volumeRulesRegistryVersion: 0,
            hybridChartRequestKind: 0,
            transitionIds: null,
            exactOrigin: default,
            exactTargetPosition: default);

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
            RequestFamily.Volume,
            origin,
            destination,
            unitSize,
            maxClimbHeight: default,
            allowUnwalkableEndpoints,
            allowTraversalTransitions: false,
            (int)heuristic,
            (int)traversalMedium,
            maxPathSearchRange,
            extraFloodRange: 0,
            transitionRegistryVersion: 0,
            volumeRulesRegistryVersion,
            hybridChartRequestKind: 0,
            transitionIds: null,
            exactOrigin: default,
            exactTargetPosition: default);

    internal static PathRequestCacheKey CreateHybrid(
        WorldVoxelIndex origin,
        WorldVoxelIndex destination,
        Fixed64 unitSize,
        HybridChartRequestKind chartRequestKind,
        bool allowUnwalkableEndpoints,
        HeuristicMethod heuristic,
        Fixed64 maxClimbHeight,
        int extraFloodRange,
        int maxPathSearchRange,
        TraversalTransition[] directedTransitions,
        int transitionRegistryVersion,
        int volumeRulesRegistryVersion,
        Vector3d exactOrigin = default,
        Vector3d exactTargetPosition = default) =>
        new(
            RequestFamily.Hybrid,
            origin,
            destination,
            unitSize,
            maxClimbHeight,
            allowUnwalkableEndpoints,
            allowTraversalTransitions: false,
            (int)heuristic,
            traversalMedium: 0,
            maxPathSearchRange,
            extraFloodRange,
            transitionRegistryVersion,
            volumeRulesRegistryVersion,
            (int)chartRequestKind,
            SnapshotTransitionIds(directedTransitions),
            exactOrigin,
            exactTargetPosition);

    internal static PathRequestCacheKey CreateFlowFieldHybridFallback(
        WorldVoxelIndex origin,
        WorldVoxelIndex destination,
        Fixed64 unitSize,
        bool allowUnwalkableEndpoints,
        Fixed64 maxClimbHeight,
        int extraFloodRange,
        int maxPathSearchRange,
        int transitionRegistryVersion,
        int volumeRulesRegistryVersion,
        Vector3d exactOrigin,
        Vector3d exactTargetPosition) =>
        new(
            RequestFamily.FlowFieldHybridFallback,
            origin,
            destination,
            unitSize,
            maxClimbHeight,
            allowUnwalkableEndpoints,
            allowTraversalTransitions: true,
            (int)HeuristicMethod.Manhattan,
            traversalMedium: 0,
            maxPathSearchRange,
            extraFloodRange,
            transitionRegistryVersion,
            volumeRulesRegistryVersion,
            (int)HybridChartRequestKind.FlowField,
            transitionIds: null,
            exactOrigin,
            exactTargetPosition);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Equals(PathRequestCacheKey other)
    {
        if (IsInitialized != other.IsInitialized)
            return false;

        if (!IsInitialized)
            return true;

        return _family == other._family
            && _origin.Equals(other._origin)
            && _destination.Equals(other._destination)
            && _exactOrigin.Equals(other._exactOrigin)
            && _exactTargetPosition.Equals(other._exactTargetPosition)
            && _unitSize == other._unitSize
            && _maxClimbHeight == other._maxClimbHeight
            && _allowUnwalkableEndpoints == other._allowUnwalkableEndpoints
            && _allowTraversalTransitions == other._allowTraversalTransitions
            && _heuristic == other._heuristic
            && _traversalMedium == other._traversalMedium
            && _maxPathSearchRange == other._maxPathSearchRange
            && _extraFloodRange == other._extraFloodRange
            && _transitionRegistryVersion == other._transitionRegistryVersion
            && _volumeRulesRegistryVersion == other._volumeRulesRegistryVersion
            && _hybridChartRequestKind == other._hybridChartRequestKind
            && TransitionIdsEqual(_transitionIds, other._transitionIds);
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is PathRequestCacheKey other && Equals(other);

    /// <inheritdoc/>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public override int GetHashCode() => _hashCode;

    /// <summary>
    /// Returns whether two request cache keys have exact identity.
    /// </summary>
    public static bool operator ==(PathRequestCacheKey left, PathRequestCacheKey right) =>
        left.Equals(right);

    /// <summary>
    /// Returns whether two request cache keys have different identity.
    /// </summary>
    public static bool operator !=(PathRequestCacheKey left, PathRequestCacheKey right) =>
        !left.Equals(right);

    private static bool TransitionIdsEqual(string[]? left, string[]? right)
    {
        int length = left?.Length ?? 0;
        if (length != (right?.Length ?? 0))
            return false;

        for (int i = 0; i < length; i++)
        {
            if (!string.Equals(left![i], right![i], StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static string[] SnapshotTransitionIds(TraversalTransition[]? directedTransitions)
    {
        int length = directedTransitions?.Length ?? 0;
        if (length == 0)
            return Array.Empty<string>();

        string[] transitionIds = new string[length];
        for (int i = 0; i < length; i++)
            transitionIds[i] = directedTransitions![i].Id;

        return transitionIds;
    }

    private static int ComputeHashCode(
        RequestFamily family,
        WorldVoxelIndex origin,
        WorldVoxelIndex destination,
        Fixed64 unitSize,
        Fixed64 maxClimbHeight,
        bool allowUnwalkableEndpoints,
        bool allowTraversalTransitions,
        int heuristic,
        int traversalMedium,
        int maxPathSearchRange,
        int extraFloodRange,
        int transitionRegistryVersion,
        int volumeRulesRegistryVersion,
        int hybridChartRequestKind,
        string[]? transitionIds,
        Vector3d exactOrigin,
        Vector3d exactTargetPosition)
    {
        PathRequestHashBuilder hash = PathRequestHashBuilder.Create();
        hash.Add((int)family);
        hash.Add(origin.GetHashCode());
        hash.Add(destination.GetHashCode());
        if (family is RequestFamily.Hybrid or RequestFamily.FlowFieldHybridFallback)
        {
            hash.Add(exactOrigin.X.GetHashCode());
            hash.Add(exactOrigin.Y.GetHashCode());
            hash.Add(exactOrigin.Z.GetHashCode());
            hash.Add(exactTargetPosition.X.GetHashCode());
            hash.Add(exactTargetPosition.Y.GetHashCode());
            hash.Add(exactTargetPosition.Z.GetHashCode());
        }
        hash.Add(unitSize.GetHashCode());
        hash.Add(maxClimbHeight.GetHashCode());
        hash.Add(allowUnwalkableEndpoints);
        hash.Add(allowTraversalTransitions);
        hash.Add(heuristic);
        hash.Add(traversalMedium);
        hash.Add(maxPathSearchRange);
        hash.Add(extraFloodRange);
        hash.Add(transitionRegistryVersion);
        hash.Add(volumeRulesRegistryVersion);
        hash.Add(hybridChartRequestKind);

        int transitionCount = transitionIds?.Length ?? 0;
        hash.Add(transitionCount);
        for (int i = 0; i < transitionCount; i++)
            hash.AddOrdinalString(transitionIds![i]);

        return hash.ToHashCode();
    }
}

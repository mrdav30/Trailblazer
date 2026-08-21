//=======================================================================
// TraversalTransitionDefinition.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using GridForge.Spatial;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>
/// Describes one directed, source-map-owned semantic traversal action.
/// </summary>
public readonly struct TraversalTransitionDefinition : IEquatable<TraversalTransitionDefinition>
{
    /// <summary>
    /// The stable map-local transition identifier.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The semantic action performed by this transition.
    /// </summary>
    public TraversalTransitionType Type { get; }

    /// <summary>
    /// The local source-cell index in the owning map.
    /// </summary>
    public VoxelIndex SourceIndex { get; }

    /// <summary>
    /// The traversal medium used at the source endpoint.
    /// </summary>
    public TraversalMedium SourceMedium { get; }

    /// <summary>
    /// The durable destination-cell address.
    /// </summary>
    public NavigationCellAddress Destination { get; }

    /// <summary>
    /// The traversal medium used at the destination endpoint.
    /// </summary>
    public TraversalMedium DestinationMedium { get; }

    /// <summary>
    /// Whether the source endpoint overrides its cell anchor.
    /// </summary>
    public bool HasSourcePointOverride { get; }

    /// <summary>
    /// The exact source endpoint when <see cref="HasSourcePointOverride"/> is true.
    /// </summary>
    public Vector3d SourcePointOverride { get; }

    /// <summary>
    /// Whether the destination endpoint overrides its cell anchor.
    /// </summary>
    public bool HasDestinationPointOverride { get; }

    /// <summary>
    /// The exact destination endpoint when <see cref="HasDestinationPointOverride"/> is true.
    /// </summary>
    public Vector3d DestinationPointOverride { get; }

    /// <summary>
    /// The complete set of capabilities required to use this transition.
    /// </summary>
    public TraversalCapability RequiredCapabilities { get; }

    /// <summary>
    /// The non-negative surcharge for performing this semantic action.
    /// </summary>
    public Fixed64 AdditionalCost { get; }

    /// <summary>
    /// Creates one complete directed semantic transition definition.
    /// </summary>
    public TraversalTransitionDefinition(
        string id,
        TraversalTransitionType type,
        VoxelIndex sourceIndex,
        TraversalMedium sourceMedium,
        NavigationCellAddress destination,
        TraversalMedium destinationMedium,
        TraversalCapability requiredCapabilities = TraversalCapability.None,
        Fixed64 additionalCost = default,
        Vector3d sourcePointOverride = default,
        bool hasSourcePointOverride = false,
        Vector3d destinationPointOverride = default,
        bool hasDestinationPointOverride = false)
    {
        SwiftThrowHelper.ThrowIfArgument(
            string.IsNullOrWhiteSpace(id),
            nameof(id),
            "Transition ID cannot be null, empty, or whitespace.");
        SwiftThrowHelper.ThrowIfArgument(
            !IsKnownType(type),
            nameof(type),
            "Transition type is unknown.");
        SwiftThrowHelper.ThrowIfArgument(
            !IsKnownMedium(sourceMedium),
            nameof(sourceMedium),
            "Source medium must identify one known traversal medium.");
        SwiftThrowHelper.ThrowIfArgument(
            !IsKnownMedium(destinationMedium),
            nameof(destinationMedium),
            "Destination medium must identify one known traversal medium.");
        SwiftThrowHelper.ThrowIfArgument(
            (requiredCapabilities & ~NavigationCell.KnownCapabilities) != 0,
            nameof(requiredCapabilities),
            "Required capabilities contain an unknown bit.");
        SwiftThrowHelper.ThrowIfArgument(
            additionalCost < Fixed64.Zero,
            nameof(additionalCost),
            "Additional cost must be non-negative.");
        SwiftThrowHelper.ThrowIfArgument(
            string.IsNullOrWhiteSpace(destination.MapId),
            nameof(destination),
            "Destination must contain a valid map ID.");

        Id = id;
        Type = type;
        SourceIndex = sourceIndex;
        SourceMedium = sourceMedium;
        Destination = destination;
        DestinationMedium = destinationMedium;
        RequiredCapabilities = requiredCapabilities;
        AdditionalCost = additionalCost;
        SourcePointOverride = hasSourcePointOverride ? sourcePointOverride : default;
        HasSourcePointOverride = hasSourcePointOverride;
        DestinationPointOverride = hasDestinationPointOverride ? destinationPointOverride : default;
        HasDestinationPointOverride = hasDestinationPointOverride;
    }

    /// <inheritdoc/>
    public bool Equals(TraversalTransitionDefinition other) =>
        string.Equals(Id, other.Id, StringComparison.Ordinal)
        && Type == other.Type
        && SourceIndex.Equals(other.SourceIndex)
        && SourceMedium == other.SourceMedium
        && Destination.Equals(other.Destination)
        && DestinationMedium == other.DestinationMedium
        && HasSourcePointOverride == other.HasSourcePointOverride
        && SourcePointOverride == other.SourcePointOverride
        && HasDestinationPointOverride == other.HasDestinationPointOverride
        && DestinationPointOverride == other.DestinationPointOverride
        && RequiredCapabilities == other.RequiredCapabilities
        && AdditionalCost == other.AdditionalCost;

    /// <inheritdoc/>
    public override bool Equals(object? obj) =>
        obj is TraversalTransitionDefinition other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int idHash = Id == null
            ? 0
            : SwiftHashTools.GetDeterministicStringEqualityComparer().GetHashCode(Id);
        int hash = SwiftHashTools.CombineHashCodes(idHash, (int)Type);
        hash = SwiftHashTools.CombineHashCodes(hash, SourceIndex.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, (int)SourceMedium);
        hash = SwiftHashTools.CombineHashCodes(hash, Destination.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, (int)DestinationMedium);
        hash = SwiftHashTools.CombineHashCodes(hash, HasSourcePointOverride ? 1 : 0);
        hash = SwiftHashTools.CombineHashCodes(hash, SourcePointOverride.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, HasDestinationPointOverride ? 1 : 0);
        hash = SwiftHashTools.CombineHashCodes(hash, DestinationPointOverride.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, (int)RequiredCapabilities);
        return SwiftHashTools.CombineHashCodes(hash, AdditionalCost.GetHashCode());
    }

    internal static bool IsKnownMedium(TraversalMedium medium) =>
        medium == TraversalMedium.Solid
        || medium == TraversalMedium.Gas
        || medium == TraversalMedium.Liquid;

    internal static bool IsKnownType(TraversalTransitionType type) =>
        type >= TraversalTransitionType.Custom && type <= TraversalTransitionType.Climb;

    /// <summary>
    /// Tests two definitions for value equality.
    /// </summary>
    public static bool operator ==(
        TraversalTransitionDefinition left,
        TraversalTransitionDefinition right) => left.Equals(right);

    /// <summary>
    /// Tests two definitions for value inequality.
    /// </summary>
    public static bool operator !=(
        TraversalTransitionDefinition left,
        TraversalTransitionDefinition right) => !left.Equals(right);
}

//=======================================================================
// NavigationCell.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>
/// Stores the complete immutable semantic payload for one authored navigation cell.
/// </summary>
public readonly struct NavigationCell : IEquatable<NavigationCell>
{
    internal const TraversalMedia KnownMedia =
        TraversalMedia.Solid | TraversalMedia.Gas | TraversalMedia.Liquid;

    internal const TraversalCapability KnownCapabilities =
        TraversalCapability.Jump
        | TraversalCapability.Climb
        | TraversalCapability.Swim
        | TraversalCapability.Fly
        | TraversalCapability.Teleport;

    internal const NavigationCellFlags KnownFlags =
        NavigationCellFlags.TransitionSourceHint
        | NavigationCellFlags.TransitionDestinationHint
        | NavigationCellFlags.ClimbSurfaceHint;

    internal static bool IsKnownMedium(TraversalMedium medium) =>
        medium == TraversalMedium.Solid
        || medium == TraversalMedium.Gas
        || medium == TraversalMedium.Liquid;

    internal static TraversalMedia ToMedia(TraversalMedium medium) => medium switch
    {
        TraversalMedium.Solid => TraversalMedia.Solid,
        TraversalMedium.Gas => TraversalMedia.Gas,
        TraversalMedium.Liquid => TraversalMedia.Liquid,
        _ => TraversalMedia.None
    };

    internal bool SupportsMedium(TraversalMedium medium) =>
        (Media & ToMedia(medium)) != 0;

    /// <summary>
    /// The traversal media supported by this cell.
    /// </summary>
    public TraversalMedia Media { get; }

    /// <summary>
    /// The complete set of capabilities an agent must possess to enter this cell.
    /// </summary>
    public TraversalCapability RequiredCapabilities { get; }

    /// <summary>
    /// The stable host-defined navigation area used by query-specific policies.
    /// </summary>
    public NavigationAreaId Area { get; }

    /// <summary>
    /// The non-negative cost charged when an edge enters this cell.
    /// </summary>
    public Fixed64 EnterCost { get; }

    /// <summary>
    /// The inclusive horizontal radius clearance at the cell anchor.
    /// </summary>
    public Fixed64 RadiusClearance { get; }

    /// <summary>
    /// The inclusive vertical clearance above the cell anchor.
    /// </summary>
    public Fixed64 HeightClearance { get; }

    /// <summary>
    /// Optional transition-generation hints.
    /// </summary>
    public NavigationCellFlags Flags { get; }

    /// <summary>
    /// Creates a complete authored navigation-cell payload.
    /// </summary>
    public NavigationCell(
        TraversalMedia media,
        TraversalCapability requiredCapabilities,
        NavigationAreaId area,
        Fixed64 enterCost,
        Fixed64 radiusClearance,
        Fixed64 heightClearance,
        NavigationCellFlags flags = NavigationCellFlags.None)
    {
        SwiftThrowHelper.ThrowIfArgument(
            media == TraversalMedia.None || (media & ~KnownMedia) != 0,
            nameof(media),
            "Media must contain at least one known traversal-medium bit.");
        SwiftThrowHelper.ThrowIfArgument(
            (requiredCapabilities & ~KnownCapabilities) != 0,
            nameof(requiredCapabilities),
            "Required capabilities contain an unknown bit.");
        SwiftThrowHelper.ThrowIfArgument(
            enterCost < Fixed64.Zero,
            nameof(enterCost),
            "Enter cost must be non-negative.");
        SwiftThrowHelper.ThrowIfArgument(
            radiusClearance < Fixed64.Zero,
            nameof(radiusClearance),
            "Radius clearance must be non-negative.");
        SwiftThrowHelper.ThrowIfArgument(
            heightClearance < Fixed64.Zero,
            nameof(heightClearance),
            "Height clearance must be non-negative.");
        SwiftThrowHelper.ThrowIfArgument(
            (flags & ~KnownFlags) != 0,
            nameof(flags),
            "Cell flags contain an unknown bit.");

        Media = media;
        RequiredCapabilities = requiredCapabilities;
        Area = area;
        EnterCost = enterCost;
        RadiusClearance = radiusClearance;
        HeightClearance = heightClearance;
        Flags = flags;
    }

    /// <inheritdoc/>
    public bool Equals(NavigationCell other) =>
        Media == other.Media
        && RequiredCapabilities == other.RequiredCapabilities
        && Area == other.Area
        && EnterCost == other.EnterCost
        && RadiusClearance == other.RadiusClearance
        && HeightClearance == other.HeightClearance
        && Flags == other.Flags;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is NavigationCell other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int hash = SwiftHashTools.CombineHashCodes((int)Media, (int)RequiredCapabilities);
        hash = SwiftHashTools.CombineHashCodes(hash, Area.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, EnterCost.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, RadiusClearance.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, HeightClearance.GetHashCode());
        return SwiftHashTools.CombineHashCodes(hash, (int)Flags);
    }

    /// <summary>
    /// Tests two cell payloads for value equality.
    /// </summary>
    public static bool operator ==(NavigationCell left, NavigationCell right) => left.Equals(right);

    /// <summary>
    /// Tests two cell payloads for value inequality.
    /// </summary>
    public static bool operator !=(NavigationCell left, NavigationCell right) => !left.Equals(right);
}

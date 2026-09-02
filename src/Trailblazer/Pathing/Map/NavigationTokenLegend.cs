//=======================================================================
// NavigationTokenLegend.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>
/// Maps trimmed authoring tokens to complete navigation-cell payloads.
/// </summary>
public sealed class NavigationTokenLegend
{
    private readonly SwiftDictionary<string, NavigationTokenLegendEntry> _entries =
        new(8, StringComparer.Ordinal);

    /// <summary>
    /// Creates the built-in solid, volume, mixed-media, and climb token set.
    /// </summary>
    public static NavigationTokenLegend CreateBuiltIn(
        Fixed64 radiusClearance = default,
        Fixed64 heightClearance = default)
    {
        SwiftThrowHelper.ThrowIfArgument(
            radiusClearance < Fixed64.Zero,
            nameof(radiusClearance),
            "Radius clearance must be non-negative.");
        SwiftThrowHelper.ThrowIfArgument(
            heightClearance < Fixed64.Zero,
            nameof(heightClearance),
            "Height clearance must be non-negative.");

        var legend = new NavigationTokenLegend();
        legend.Register(string.Empty, NavigationTokenLegendEntry.SkipCell());
        legend.Register(".", NavigationTokenLegendEntry.SkipCell());
        legend.Register("X", NavigationTokenLegendEntry.SkipCell());
        legend.Register("S", CreateEntry(
            TraversalMedia.Solid,
            radiusClearance,
            heightClearance,
            NavigationCellFlags.None,
            TraversalMedia.Solid));
        legend.Register("SC", CreateEntry(
            TraversalMedia.Solid,
            radiusClearance,
            heightClearance,
            NavigationCellFlags.ClimbSurfaceHint));
        legend.Register("G", CreateEntry(
            TraversalMedia.Gas,
            radiusClearance,
            heightClearance,
            NavigationCellFlags.None,
            TraversalMedia.Gas));
        legend.Register("L", CreateEntry(
            TraversalMedia.Liquid,
            radiusClearance,
            heightClearance,
            NavigationCellFlags.None,
            TraversalMedia.Liquid));
        legend.Register("LC", CreateEntry(
            TraversalMedia.Solid | TraversalMedia.Liquid,
            radiusClearance,
            heightClearance,
            NavigationCellFlags.ClimbSurfaceHint,
            TraversalMedia.Solid | TraversalMedia.Liquid));
        legend.Register("SG", CreateEntry(
            TraversalMedia.Solid | TraversalMedia.Gas,
            radiusClearance,
            heightClearance,
            NavigationCellFlags.None,
            TraversalMedia.Solid | TraversalMedia.Gas));
        legend.Register("SL", CreateEntry(
            TraversalMedia.Solid | TraversalMedia.Liquid,
            radiusClearance,
            heightClearance,
            NavigationCellFlags.None,
            TraversalMedia.Solid | TraversalMedia.Liquid));
        return legend;
    }

    /// <summary>
    /// Registers one trimmed token. A duplicate token returns false without replacement.
    /// </summary>
    public bool Register(string token, NavigationTokenLegendEntry entry)
    {
        SwiftThrowHelper.ThrowIfNull(token, nameof(token));
        string normalizedToken = token.Trim();
        SwiftThrowHelper.ThrowIfArgument(
            normalizedToken.IndexOf('!') >= 0 || normalizedToken.IndexOf('_') >= 0,
            nameof(token),
            "Legend tokens cannot contain reserved marker or inline-cost characters.");

        if (_entries.ContainsKey(normalizedToken))
            return false;

        _entries.Add(normalizedToken, entry);
        return true;
    }

    /// <summary>
    /// Attempts to resolve one token after ordinal whitespace trimming.
    /// </summary>
    public bool TryGetEntry(string token, out NavigationTokenLegendEntry entry)
    {
        SwiftThrowHelper.ThrowIfNull(token, nameof(token));
        return _entries.TryGetValue(token.Trim(), out entry);
    }

    private static NavigationTokenLegendEntry CreateEntry(
        TraversalMedia media,
        Fixed64 radiusClearance,
        Fixed64 heightClearance,
        NavigationCellFlags flags,
        TraversalMedia transitionMedia = TraversalMedia.None) =>
        new(
            new NavigationCell(
                media,
                TraversalCapability.None,
                default,
                Fixed64.Zero,
                radiusClearance,
                heightClearance,
                flags),
            transitionMedia);
}

//=======================================================================
// TraversalLegend.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>
/// Maps authoring tokens into chart cells and generated transition media.
/// </summary>
public sealed class TraversalLegend
{
    private readonly SwiftDictionary<string, TraversalLegendEntry> _entries =
        new(8, StringComparer.Ordinal);

    /// <summary>
    /// Creates a built-in traversal legend with predefined token mappings.
    /// </summary>
    /// <returns>A traversal legend with built-in entries.</returns>
    public static TraversalLegend CreateBuiltIn()
    {
        var legend = new TraversalLegend();
        // These tokens intentionally contribute no chart traversal and no generated transition anchor.
        legend.Register(string.Empty, TraversalLegendEntry.SkipCell());
        legend.Register(".", TraversalLegendEntry.SkipCell());
        legend.Register("X", TraversalLegendEntry.SkipCell());
        legend.Register("S", TraversalLegendEntry.Solid(NavigationChartCell.Solid));
        legend.Register("SC", new TraversalLegendEntry(new NavigationChartCell(
            TraversalMedia.Solid,
            flags: NavigationChartCellFlags.ClimbSurfaceHint)));
        legend.Register("G", TraversalLegendEntry.Gas());
        legend.Register("L", TraversalLegendEntry.Liquid());
        legend.Register("LC", new TraversalLegendEntry(
            new NavigationChartCell(
                TraversalMedia.Solid | TraversalMedia.Liquid,
                flags: NavigationChartCellFlags.ClimbSurfaceHint),
            TraversalMedia.Solid | TraversalMedia.Liquid));
        legend.Register("SG", new TraversalLegendEntry(
            NavigationChartCell.SolidGas,
            TraversalMedia.Solid | TraversalMedia.Gas));
        legend.Register("SL", new TraversalLegendEntry(
            NavigationChartCell.SolidLiquid,
            TraversalMedia.Solid | TraversalMedia.Liquid));
        return legend;
    }

    /// <summary>
    /// Registers a new token mapping in the legend.
    /// Tokens are normalized by trimming whitespace, and cannot include the transition marker character '!'.
    /// </summary>
    /// <param name="token">The token to register.</param>
    /// <param name="entry">The legend entry associated with the token.</param>
    /// <returns>True if the token was successfully registered; false if the token already exists in the legend.</returns>
    /// <exception cref="ArgumentException">Thrown if the token contains invalid characters.</exception>
    public bool Register(string token, TraversalLegendEntry entry)
    {
        string normalizedToken = NormalizeToken(token);
        if (normalizedToken.Contains('!'))
            throw new ArgumentException("Legend tokens cannot include transition marker characters.", nameof(token));

        if (_entries.ContainsKey(normalizedToken))
            return false;

        _entries.Add(normalizedToken, entry);
        return true;
    }

    /// <summary>
    /// Attempts to retrieve the legend entry associated with the specified token.
    /// </summary>
    /// <param name="token">The token to look up in the legend.</param>
    /// <param name="entry">When this method returns, contains the legend entry associated with the specified token, if the token is found; otherwise, null.</param>
    /// <returns>True if the token was found in the legend; otherwise, false.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGetEntry(string token, out TraversalLegendEntry entry) =>
            _entries.TryGetValue(NormalizeToken(token), out entry);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static string NormalizeToken(string token) => token?.Trim() ?? string.Empty;
}

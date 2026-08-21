//=======================================================================
// NavigationMap.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections.Utility;

namespace Trailblazer.Pathing;

/// <summary>
/// Stores one immutable, canonically ordered navigation bake for one normalized grid.
/// </summary>
public sealed class NavigationMap : IEquatable<NavigationMap>
{
    private readonly NavigationCellEntry[] _cells;
    private readonly NavigationConnection[] _connections;
    private readonly TraversalTransitionDefinition[] _transitions;
    private readonly TraversalTransitionRule[] _transitionRules;
    private readonly NavigationCell? _defaultCell;
    private readonly GridNavigationPortal[] _nativePortalTemplates;
    private readonly ReadOnlyCollection<NavigationCellEntry> _cellView;
    private readonly ReadOnlyCollection<NavigationConnection> _connectionView;
    private readonly ReadOnlyCollection<TraversalTransitionDefinition> _transitionView;

    /// <summary>
    /// The stable host-owned map identifier.
    /// </summary>
    public string MapId { get; }

    /// <summary>
    /// The normalized storage-neutral GridForge configuration binding.
    /// </summary>
    public NormalizedGridConfiguration GridBinding { get; }

    /// <summary>
    /// Authored cells in stable lexicographic topology-local index order.
    /// </summary>
    public IReadOnlyList<NavigationCellEntry> Cells => _cellView;

    /// <summary>
    /// Directed source-owned connections in ordinal ID order.
    /// </summary>
    public IReadOnlyList<NavigationConnection> Connections => _connectionView;

    /// <summary>
    /// Directed source-owned semantic transitions in ordinal ID order.
    /// </summary>
    public IReadOnlyList<TraversalTransitionDefinition> Transitions => _transitionView;

    internal ReadOnlySpan<NavigationCellEntry> CellSpan => _cells;

    internal ReadOnlySpan<NavigationConnection> ConnectionSpan => _connections;

    internal ReadOnlySpan<TraversalTransitionDefinition> TransitionSpan => _transitions;

    internal ReadOnlySpan<TraversalTransitionRule> TransitionRuleSpan => _transitionRules;

    internal NavigationCell? DefaultCell => _defaultCell;

    internal int NativePortalTemplateCount => _nativePortalTemplates.Length;

    internal long NativePortalTemplateRetainedBytes => checked(
        24L + ((long)_nativePortalTemplates.Length * Unsafe.SizeOf<GridNavigationPortal>()));

    internal NavigationMap(
        string mapId,
        NormalizedGridConfiguration gridBinding,
        NavigationCellEntry[] cells,
        NavigationConnection[] connections,
        TraversalTransitionDefinition[] transitions,
        TraversalTransitionRule[] transitionRules,
        NavigationCell? defaultCell)
    {
        MapId = mapId;
        GridBinding = gridBinding;
        _cells = cells;
        _connections = connections;
        _transitions = transitions;
        _transitionRules = transitionRules;
        _defaultCell = defaultCell;
        _nativePortalTemplates = CompileNativePortalTemplates(gridBinding);
        _cellView = Array.AsReadOnly(_cells);
        _connectionView = Array.AsReadOnly(_connections);
        _transitionView = Array.AsReadOnly(_transitions);
    }

    /// <inheritdoc/>
    public bool Equals(NavigationMap? other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (other is null
            || !string.Equals(MapId, other.MapId, StringComparison.Ordinal)
            || GridBinding.Key != other.GridBinding.Key
            || GridBinding.Width != other.GridBinding.Width
            || GridBinding.Height != other.GridBinding.Height
            || GridBinding.Length != other.GridBinding.Length
            || !_defaultCell.Equals(other._defaultCell)
            || _cells.Length != other._cells.Length
            || _connections.Length != other._connections.Length
            || _transitions.Length != other._transitions.Length
            || _transitionRules.Length != other._transitionRules.Length)
        {
            return false;
        }

        for (int i = 0; i < _cells.Length; i++)
        {
            if (!_cells[i].Equals(other._cells[i]))
                return false;
        }

        for (int i = 0; i < _connections.Length; i++)
        {
            if (!_connections[i].Equals(other._connections[i]))
                return false;
        }

        for (int i = 0; i < _transitions.Length; i++)
        {
            if (!_transitions[i].Equals(other._transitions[i]))
                return false;
        }

        for (int i = 0; i < _transitionRules.Length; i++)
        {
            if (!_transitionRules[i].Equals(other._transitionRules[i]))
                return false;
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => Equals(obj as NavigationMap);

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        int mapHash = SwiftHashTools.GetDeterministicStringEqualityComparer().GetHashCode(MapId);
        int hash = SwiftHashTools.CombineHashCodes(mapHash, GridBinding.Key.GetHashCode());
        hash = SwiftHashTools.CombineHashCodes(hash, GridBinding.Width);
        hash = SwiftHashTools.CombineHashCodes(hash, GridBinding.Height);
        hash = SwiftHashTools.CombineHashCodes(hash, GridBinding.Length);
        hash = SwiftHashTools.CombineHashCodes(hash, _defaultCell.GetHashCode());
        for (int i = 0; i < _cells.Length; i++)
            hash = SwiftHashTools.CombineHashCodes(hash, _cells[i].GetHashCode());
        for (int i = 0; i < _connections.Length; i++)
            hash = SwiftHashTools.CombineHashCodes(hash, _connections[i].GetHashCode());
        for (int i = 0; i < _transitions.Length; i++)
            hash = SwiftHashTools.CombineHashCodes(hash, _transitions[i].GetHashCode());
        for (int i = 0; i < _transitionRules.Length; i++)
            hash = SwiftHashTools.CombineHashCodes(hash, _transitionRules[i].GetHashCode());
        return hash;
    }

    internal bool ContainsCell(GridForge.Spatial.VoxelIndex index) =>
        FindCellIndex(index) >= 0;

    internal int FindCellIndex(GridForge.Spatial.VoxelIndex index)
    {
        int low = 0;
        int high = _cells.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = _cells[middle].Index.CompareTo(index);
            if (comparison == 0)
                return middle;
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }

        return -1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal GridNavigationPortal GetNativePortalTemplate(int directionIndex) =>
        (uint)directionIndex < (uint)_nativePortalTemplates.Length
            ? _nativePortalTemplates[directionIndex]
            : default;

    internal static int GetNativeSurfaceDirectionCount(GridTopologyKind topology) => topology switch
    {
        GridTopologyKind.RectangularPrism => 4,
        GridTopologyKind.HexPrism => 6,
        _ => 0
    };

    internal static VoxelIndex GetNativeSurfaceOffset(
        GridTopologyKind topology,
        int directionIndex)
    {
        if (topology == GridTopologyKind.HexPrism)
        {
            HexDirection direction = directionIndex switch
            {
                0 => HexDirection.QNegative,
                1 => HexDirection.QNegativeRPositive,
                2 => HexDirection.RNegative,
                3 => HexDirection.RPositive,
                4 => HexDirection.QPositiveRNegative,
                5 => HexDirection.QPositive,
                _ => default
            };
            return HexDirectionUtility.GetOffset(direction);
        }

        RectangularDirection rectangular = directionIndex switch
        {
            0 => RectangularDirection.West,
            1 => RectangularDirection.South,
            2 => RectangularDirection.North,
            3 => RectangularDirection.East,
            _ => default
        };
        (int x, int y, int z) offset = RectangularDirectionUtility.Offsets[(int)rectangular];
        return new VoxelIndex(offset.x, offset.y, offset.z);
    }

    private static GridNavigationPortal[] CompileNativePortalTemplates(
        NormalizedGridConfiguration binding)
    {
        GridTopologyKind topology = binding.Configuration.TopologyKind;
        int count = GetNativeSurfaceDirectionCount(topology);
        var templates = new GridNavigationPortal[count];
        for (int directionIndex = 0; directionIndex < count; directionIndex++)
        {
            VoxelIndex offset = GetNativeSurfaceOffset(topology, directionIndex);
            var sourceIndex = new VoxelIndex(
                offset.x < 0 ? 1 : 0,
                0,
                offset.z < 0 ? 1 : 0);
            var targetIndex = new VoxelIndex(
                sourceIndex.x + offset.x,
                sourceIndex.y + offset.y,
                sourceIndex.z + offset.z);
            if (!binding.IsValidIndex(sourceIndex) || !binding.IsValidIndex(targetIndex))
                continue;

            bool compiled = binding.TryGetCellPrism(sourceIndex, out GridCellPrism source)
                && binding.TryGetCellPrism(targetIndex, out GridCellPrism target)
                && GridCellGeometry.TryCreateNavigationPortal(source, target, out GridNavigationPortal portal)
                && Vector3d.TrySubtract(Vector3d.Zero, source.Center, out Vector3d translation)
                && portal.TryTranslate(translation, out templates[directionIndex]);
            SwiftThrowHelper.ThrowIfArgument(
                !compiled,
                nameof(binding),
                "Grid binding could not compile an exact native surface portal template.");
        }
        return templates;
    }
}

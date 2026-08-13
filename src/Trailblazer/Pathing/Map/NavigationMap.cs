//=======================================================================
// NavigationMap.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using GridForge.Configuration;
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

    internal NavigationMap(
        string mapId,
        NormalizedGridConfiguration gridBinding,
        NavigationCellEntry[] cells,
        NavigationConnection[] connections,
        TraversalTransitionDefinition[] transitions)
    {
        MapId = mapId;
        GridBinding = gridBinding;
        _cells = cells;
        _connections = connections;
        _transitions = transitions;
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
            || _cells.Length != other._cells.Length
            || _connections.Length != other._connections.Length
            || _transitions.Length != other._transitions.Length)
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
        for (int i = 0; i < _cells.Length; i++)
            hash = SwiftHashTools.CombineHashCodes(hash, _cells[i].GetHashCode());
        for (int i = 0; i < _connections.Length; i++)
            hash = SwiftHashTools.CombineHashCodes(hash, _connections[i].GetHashCode());
        for (int i = 0; i < _transitions.Length; i++)
            hash = SwiftHashTools.CombineHashCodes(hash, _transitions[i].GetHashCode());
        return hash;
    }

    internal bool ContainsCell(GridForge.Spatial.VoxelIndex index) =>
        FindCellIndex(index) >= 0;

    private int FindCellIndex(GridForge.Spatial.VoxelIndex index)
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
}

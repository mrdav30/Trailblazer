//=======================================================================
// NavigationMapBuilder.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using FixedMathSharp;
using GridForge.Configuration;
using GridForge.Grids.Topology;
using GridForge.Spatial;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>
/// Collects one-grid authoring input and builds a validated, canonically ordered map.
/// </summary>
public sealed class NavigationMapBuilder
{
    private readonly SwiftList<NavigationCellEntry> _cells = new();
    private readonly SwiftList<NavigationConnection> _connections = new();
    private readonly SwiftList<TraversalTransitionDefinition> _transitions = new();
    private readonly SwiftList<TraversalTransitionRule> _transitionRules = new();
    private NavigationCell? _defaultCell;

    /// <summary>
    /// The stable host-owned identifier assigned to the resulting map.
    /// </summary>
    public string MapId { get; }

    /// <summary>
    /// The normalized storage-neutral GridForge configuration binding.
    /// </summary>
    public NormalizedGridConfiguration GridBinding { get; }

    /// <summary>
    /// Creates a builder for an already normalized grid descriptor.
    /// </summary>
    public NavigationMapBuilder(
        string mapId,
        NormalizedGridConfiguration gridBinding)
    {
        SwiftThrowHelper.ThrowIfArgument(
            string.IsNullOrWhiteSpace(mapId),
            nameof(mapId),
            "Map ID cannot be null, empty, or whitespace.");
        SwiftThrowHelper.ThrowIfArgument(
            !gridBinding.IsValid,
            nameof(gridBinding),
            "Grid binding must contain a valid normalized address space.");

        MapId = mapId;
        GridBinding = gridBinding;
    }

    /// <summary>
    /// Creates a builder by normalizing an offline GridForge configuration.
    /// </summary>
    public NavigationMapBuilder(
        string mapId,
        GridConfiguration gridConfiguration)
        : this(mapId, Normalize(gridConfiguration))
    {
    }

    /// <summary>
    /// Adds one authored cell. Duplicate indices reject when <see cref="Build"/> normalizes the bake.
    /// </summary>
    public NavigationMapBuilder AddCell(VoxelIndex index, NavigationCell cell)
    {
        _cells.Add(new NavigationCellEntry(index, cell));
        return this;
    }

    /// <summary>
    /// Adds one authored cell entry.
    /// </summary>
    public NavigationMapBuilder AddCell(NavigationCellEntry entry)
    {
        _cells.Add(entry);
        return this;
    }

    /// <summary>
    /// Sets the optional complete fallback payload used when no explicit cell wins.
    /// </summary>
    public NavigationMapBuilder SetDefaultCell(NavigationCell? defaultCell)
    {
        _defaultCell = defaultCell;
        return this;
    }

    /// <summary>
    /// Adds one directed source-owned physical connection.
    /// </summary>
    public NavigationMapBuilder AddConnection(NavigationConnection connection)
    {
        SwiftThrowHelper.ThrowIfNull(connection, nameof(connection));
        _connections.Add(connection);
        return this;
    }

    /// <summary>
    /// Adds one directed source-owned semantic transition.
    /// </summary>
    public NavigationMapBuilder AddTransition(TraversalTransitionDefinition transition)
    {
        _transitions.Add(transition);
        return this;
    }

    /// <summary>Adds one bounded procedural semantic transition rule.</summary>
    public NavigationMapBuilder AddTransitionRule(TraversalTransitionRule rule)
    {
        _transitionRules.Add(rule);
        return this;
    }

    /// <summary>
    /// Validates and copies all input into an immutable canonical map.
    /// </summary>
    public NavigationMap Build()
    {
        NavigationCellEntry[] cells = _cells.ToArray();
        NavigationConnection[] connections = _connections.ToArray();
        TraversalTransitionDefinition[] transitions = _transitions.ToArray();
        TraversalTransitionRule[] transitionRules = _transitionRules.ToArray();

        Array.Sort(cells, NavigationCellEntryComparer.Instance);
        Array.Sort(connections, NavigationConnectionComparer.Instance);
        Array.Sort(transitions, TraversalTransitionDefinitionComparer.Instance);
        Array.Sort(transitionRules, TraversalTransitionRuleComparer.Instance);

        ValidateGridGeometry();
        if (_defaultCell.HasValue)
            ValidateCellPayload(_defaultCell.Value, nameof(_defaultCell));
        ValidateCells(cells);
        ValidateConnections(cells, connections);
        ValidateTransitions(cells, transitions);
        ValidateTransitionRules(transitionRules);

        return new NavigationMap(
            MapId,
            GridBinding,
            cells,
            connections,
            transitions,
            transitionRules,
            _defaultCell);
    }

    /// <summary>
    /// Imports an X/Y/Z rectangular dense source into the canonical sparse map representation.
    /// Null entries are omitted from the bake.
    /// </summary>
    public static NavigationMap ImportDenseRectangular(
        string mapId,
        GridConfiguration gridConfiguration,
        NavigationCell?[,,] cells)
    {
        SwiftThrowHelper.ThrowIfNull(cells, nameof(cells));
        var builder = new NavigationMapBuilder(mapId, gridConfiguration);
        SwiftThrowHelper.ThrowIfArgument(
            builder.GridBinding.Configuration.TopologyKind != GridTopologyKind.RectangularPrism,
            nameof(gridConfiguration),
            "Dense rectangular import requires rectangular-prism topology.");
        SwiftThrowHelper.ThrowIfArgument(
            cells.GetLength(0) != builder.GridBinding.Width
            || cells.GetLength(1) != builder.GridBinding.Height
            || cells.GetLength(2) != builder.GridBinding.Length,
            nameof(cells),
            "Dense input dimensions must match the normalized grid address space in X/Y/Z order.");

        for (int x = 0; x < cells.GetLength(0); x++)
        {
            for (int y = 0; y < cells.GetLength(1); y++)
            {
                for (int z = 0; z < cells.GetLength(2); z++)
                {
                    NavigationCell? cell = cells[x, y, z];
                    if (cell.HasValue)
                        builder.AddCell(new VoxelIndex(x, y, z), cell.Value);
                }
            }
        }

        return builder.Build();
    }

    /// <summary>
    /// Imports explicit axial Q/layer/R hex cells into the canonical sparse map representation.
    /// </summary>
    public static NavigationMap ImportAxialHex(
        string mapId,
        GridConfiguration gridConfiguration,
        IEnumerable<NavigationCellEntry> cells)
    {
        SwiftThrowHelper.ThrowIfNull(cells, nameof(cells));
        var builder = new NavigationMapBuilder(mapId, gridConfiguration);
        SwiftThrowHelper.ThrowIfArgument(
            builder.GridBinding.Configuration.TopologyKind != GridTopologyKind.HexPrism,
            nameof(gridConfiguration),
            "Axial hex import requires hex-prism topology.");

        foreach (NavigationCellEntry cell in cells)
            builder.AddCell(cell);

        return builder.Build();
    }

    private static NormalizedGridConfiguration Normalize(GridConfiguration configuration)
    {
        bool normalized = configuration.TryNormalize(out NormalizedGridConfiguration descriptor);
        SwiftThrowHelper.ThrowIfArgument(
            !normalized,
            nameof(configuration),
            "Grid configuration could not be normalized into a supported address space.");
        return descriptor;
    }

    private void ValidateCells(NavigationCellEntry[] cells)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            NavigationCellEntry entry = cells[i];
            if (!GridBinding.IsValidIndex(entry.Index))
            {
                SwiftThrowHelper.ThrowIfArgument(
                    true,
                    nameof(cells),
                    $"Cell index {entry.Index} is outside the normalized grid address space.");
            }
            ValidateCellPayload(entry.Cell, nameof(cells));

            if (i > 0 && cells[i - 1].Index.Equals(entry.Index))
            {
                SwiftThrowHelper.ThrowIfArgument(
                    true,
                    nameof(cells),
                    $"Duplicate authored cell index {entry.Index}.");
            }
        }
    }

    private void ValidateConnections(
        NavigationCellEntry[] cells,
        NavigationConnection[] connections)
    {
        int maximumPrismCount = GetMaximumLocalPrismCount(connections);
        var orderedPrisms = maximumPrismCount == 0
            ? Array.Empty<GridCellPrism>()
            : new GridCellPrism[maximumPrismCount];
        var portalWaypoints = maximumPrismCount <= 1
            ? Array.Empty<Vector3d>()
            : new Vector3d[(maximumPrismCount - 1) * 2];
        int maximumWitnessCount = GetMaximumWitnessCount(connections);
        var witnessSet = new SwiftHashSet<NavigationCellAddress>(maximumWitnessCount);

        for (int i = 0; i < connections.Length; i++)
        {
            NavigationConnection connection = connections[i];
            if (i > 0 && string.Equals(connections[i - 1].Id, connection.Id, StringComparison.Ordinal))
            {
                SwiftThrowHelper.ThrowIfArgument(
                    true,
                    nameof(connections),
                    $"Duplicate map-local connection ID '{connection.Id}'.");
            }

            if (!TryGetAuthoredCell(cells, connection.SourceIndex, out NavigationCell sourceCell))
            {
                SwiftThrowHelper.ThrowIfArgument(
                    true,
                    nameof(connections),
                    $"Connection '{connection.Id}' references missing local source {connection.SourceIndex}.");
            }
            ValidateLocalAnchor(
                connection.EntryAnchor,
                connection.SourceIndex,
                nameof(connections),
                "Connection entry anchor is outside its source prism.");

            ValidateClearance(
                sourceCell,
                connection.PortalRadiusClearance,
                connection.PortalHeightClearance,
                nameof(connections),
                "Connection exceeds source-cell clearance.");

            if (string.Equals(connection.Destination.MapId, MapId, StringComparison.Ordinal))
            {
                if (!TryGetAuthoredCell(
                        cells,
                        connection.Destination.Index,
                        out NavigationCell destinationCell))
                {
                    SwiftThrowHelper.ThrowIfArgument(
                        true,
                        nameof(connections),
                        $"Connection '{connection.Id}' references missing local destination {connection.Destination.Index}.");
                }
                ValidateLocalAnchor(
                    connection.ExitAnchor,
                    connection.Destination.Index,
                    nameof(connections),
                    "Connection exit anchor is outside its destination prism.");
                ValidateClearance(
                    destinationCell,
                    connection.PortalRadiusClearance,
                    connection.PortalHeightClearance,
                    nameof(connections),
                    "Connection exceeds destination-cell clearance.");
            }

            ValidateWitnesses(cells, connection, witnessSet);
            ValidateConnectionGeometry(cells, connection, orderedPrisms, portalWaypoints);
        }
    }

    private void ValidateTransitions(
        NavigationCellEntry[] cells,
        TraversalTransitionDefinition[] transitions)
    {
        for (int i = 0; i < transitions.Length; i++)
        {
            TraversalTransitionDefinition transition = transitions[i];
            if (i > 0 && string.Equals(transitions[i - 1].Id, transition.Id, StringComparison.Ordinal))
            {
                SwiftThrowHelper.ThrowIfArgument(
                    true,
                    nameof(transitions),
                    $"Duplicate map-local transition ID '{transition.Id}'.");
            }

            if (!TryGetAuthoredCell(cells, transition.SourceIndex, out NavigationCell sourceCell))
            {
                SwiftThrowHelper.ThrowIfArgument(
                    true,
                    nameof(transitions),
                    $"Transition '{transition.Id}' references missing local source {transition.SourceIndex}.");
            }
            SwiftThrowHelper.ThrowIfArgument(
                !SupportsMedium(sourceCell, transition.SourceMedium),
                nameof(transitions),
                "Transition source medium is not authored on its source cell.");

            if (transition.HasSourcePointOverride)
            {
                ValidateLocalAnchor(
                    transition.SourcePointOverride,
                    transition.SourceIndex,
                    nameof(transitions),
                    "Transition source point is outside its source prism.");
            }

            if (!string.Equals(transition.Destination.MapId, MapId, StringComparison.Ordinal))
                continue;

            if (!TryGetAuthoredCell(
                    cells,
                    transition.Destination.Index,
                    out NavigationCell destinationCell))
            {
                SwiftThrowHelper.ThrowIfArgument(
                    true,
                    nameof(transitions),
                    $"Transition '{transition.Id}' references missing local destination {transition.Destination.Index}.");
            }
            SwiftThrowHelper.ThrowIfArgument(
                !SupportsMedium(destinationCell, transition.DestinationMedium),
                nameof(transitions),
                "Transition destination medium is not authored on its destination cell.");

            if (transition.HasDestinationPointOverride)
            {
                ValidateLocalAnchor(
                    transition.DestinationPointOverride,
                    transition.Destination.Index,
                    nameof(transitions),
                    "Transition destination point is outside its destination prism.");
            }
        }
    }

    private static void ValidateTransitionRules(TraversalTransitionRule[] rules)
    {
        for (int i = 0; i < rules.Length; i++)
        {
            TraversalTransitionRule rule = rules[i];
            rule.Validate();
            if (i > 0 && string.Equals(rules[i - 1].Id, rule.Id, StringComparison.Ordinal))
            {
                SwiftThrowHelper.ThrowIfArgument(
                    true,
                    nameof(rules),
                    $"Duplicate map-local transition rule ID '{rule.Id}'.");
            }
        }
    }

    private void ValidateWitnesses(
        NavigationCellEntry[] cells,
        NavigationConnection connection,
        SwiftHashSet<NavigationCellAddress> witnessSet)
    {
        witnessSet.Clear();
        for (int i = 0; i < connection.Witnesses.Count; i++)
        {
            NavigationCellAddress witness = connection.Witnesses[i];
            if (!witnessSet.Add(witness))
            {
                SwiftThrowHelper.ThrowIfArgument(
                    true,
                    nameof(connection),
                    $"Connection '{connection.Id}' repeats witness {witness}.");
            }

            if (!string.Equals(witness.MapId, MapId, StringComparison.Ordinal))
                continue;

            if (!TryGetAuthoredCell(cells, witness.Index, out NavigationCell witnessCell))
            {
                SwiftThrowHelper.ThrowIfArgument(
                    true,
                    nameof(connection),
                    $"Connection '{connection.Id}' references missing local witness {witness.Index}.");
            }
            ValidateClearance(
                witnessCell,
                connection.PortalRadiusClearance,
                connection.PortalHeightClearance,
                nameof(connection),
                "Connection exceeds witness-cell clearance.");
        }
    }

    private void ValidateGridGeometry()
    {
        int lastX = GridBinding.Width - 1;
        int lastY = GridBinding.Height - 1;
        int lastZ = GridBinding.Length - 1;
        for (int xSelector = 0; xSelector < 2; xSelector++)
        {
            int x = xSelector == 0 ? 0 : lastX;
            for (int ySelector = 0; ySelector < 2; ySelector++)
            {
                int y = ySelector == 0 ? 0 : lastY;
                for (int zSelector = 0; zSelector < 2; zSelector++)
                {
                    int z = zSelector == 0 ? 0 : lastZ;
                    bool representable = GridBinding.TryGetCellPrism(
                        new VoxelIndex(x, y, z),
                        out _);
                    SwiftThrowHelper.ThrowIfArgument(
                        !representable,
                        nameof(GridBinding),
                        "Grid binding cannot produce exact cell prisms across its address-space extremes.");
                }
            }
        }
    }

    private void ValidateConnectionGeometry(
        NavigationCellEntry[] cells,
        NavigationConnection connection,
        GridCellPrism[] prismScratch,
        Vector3d[] waypointScratch)
    {
        // Full cross-map geometry is validated transactionally when the destination map is present.
        if (!IsCompleteLocalChain(connection))
            return;

        int prismCount = connection.Witnesses.Count + 2;
        GridBinding.TryGetCellPrism(connection.SourceIndex, out prismScratch[0]);
        for (int i = 0; i < connection.Witnesses.Count; i++)
        {
            GridBinding.TryGetCellPrism(
                connection.Witnesses[i].Index,
                out prismScratch[i + 1]);
        }
        GridBinding.TryGetCellPrism(connection.Destination.Index, out prismScratch[prismCount - 1]);

        bool validCorridor = GridCellGeometry.TryValidateNavigationCorridor(
            prismScratch.AsSpan(0, prismCount),
            connection.EntryAnchor,
            connection.ExitAnchor,
            connection.PortalRadiusClearance,
            connection.PortalHeightClearance,
            waypointScratch.AsSpan(0, (prismCount - 1) * 2),
            out _,
            out Fixed64 corridorCost);
        if (!validCorridor)
        {
            SwiftThrowHelper.ThrowIfArgument(
                true,
                nameof(connection),
                $"Connection '{connection.Id}' does not define a valid clearance-bearing prism corridor.");
        }

        if (!connection.IsLowerBoundCertified)
            return;

        TryGetAuthoredCell(cells, connection.Destination.Index, out NavigationCell destinationCell);
        bool certified = TryProveLowerBound(
            prismScratch[0],
            prismScratch[prismCount - 1],
            connection,
            corridorCost,
            destinationCell.EnterCost);
        if (!certified)
        {
            SwiftThrowHelper.ThrowIfArgument(
                true,
                nameof(connection),
                $"Connection '{connection.Id}' lower-bound declaration cannot be proven by its canonical fixed-point cost.");
        }
    }

    private bool IsCompleteLocalChain(NavigationConnection connection)
    {
        if (!string.Equals(connection.Destination.MapId, MapId, StringComparison.Ordinal))
            return false;

        for (int i = 0; i < connection.Witnesses.Count; i++)
        {
            if (!string.Equals(connection.Witnesses[i].MapId, MapId, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private int GetMaximumLocalPrismCount(NavigationConnection[] connections)
    {
        int maximumPrismCount = 0;
        for (int i = 0; i < connections.Length; i++)
        {
            if (!IsCompleteLocalChain(connections[i]))
                continue;

            int prismCount = connections[i].Witnesses.Count + 2;
            if (prismCount > maximumPrismCount)
                maximumPrismCount = prismCount;
        }

        return maximumPrismCount;
    }

    private static int GetMaximumWitnessCount(NavigationConnection[] connections)
    {
        int maximumWitnessCount = 0;
        for (int i = 0; i < connections.Length; i++)
        {
            if (connections[i].Witnesses.Count > maximumWitnessCount)
                maximumWitnessCount = connections[i].Witnesses.Count;
        }

        return maximumWitnessCount;
    }

    private static bool TryProveLowerBound(
        in GridCellPrism source,
        in GridCellPrism destination,
        NavigationConnection connection,
        Fixed64 corridorCost,
        Fixed64 destinationEnterCost)
    {
        Vector3d sourceAnchor = GetFootAnchor(source);
        Vector3d destinationAnchor = GetFootAnchor(destination);
        return Vector3d.TryGetDistance(sourceAnchor, connection.EntryAnchor, out Fixed64 approachCost)
            && Vector3d.TryGetDistance(connection.ExitAnchor, destinationAnchor, out Fixed64 departureCost)
            && Fixed64.TryAdd(approachCost, corridorCost, out Fixed64 traversalCost)
            && Fixed64.TryAdd(traversalCost, departureCost, out traversalCost)
            && Fixed64.TryAdd(traversalCost, connection.AdditionalCost, out traversalCost)
            && Fixed64.TryAdd(traversalCost, destinationEnterCost, out traversalCost)
            && NavigationDistanceMath.TryCeiling(
                sourceAnchor,
                destinationAnchor,
                out Fixed64 directCost)
            && traversalCost >= directCost;
    }

    private static Vector3d GetFootAnchor(in GridCellPrism prism) =>
        new(prism.Center.X, prism.VerticalMin, prism.Center.Z);

    private void ValidateLocalAnchor(
        Vector3d anchor,
        VoxelIndex index,
        string parameterName,
        string message)
    {
        bool contains = GridBinding.TryGetCellPrism(index, out GridCellPrism prism)
            && prism.Contains(anchor);
        SwiftThrowHelper.ThrowIfArgument(!contains, parameterName, message);
    }

    private static void ValidateClearance(
        NavigationCell cell,
        Fixed64 radius,
        Fixed64 height,
        string parameterName,
        string message) =>
        SwiftThrowHelper.ThrowIfArgument(
            radius > cell.RadiusClearance || height > cell.HeightClearance,
            parameterName,
            message);

    private static void ValidateCellPayload(NavigationCell cell, string parameterName)
    {
        SwiftThrowHelper.ThrowIfArgument(
            cell.Media == TraversalMedia.None || (cell.Media & ~NavigationCell.KnownMedia) != 0,
            parameterName,
            "Cell media must contain at least one known traversal-medium bit.");
        SwiftThrowHelper.ThrowIfArgument(
            (cell.RequiredCapabilities & ~NavigationCell.KnownCapabilities) != 0,
            parameterName,
            "Cell required capabilities contain an unknown bit.");
        SwiftThrowHelper.ThrowIfArgument(
            cell.EnterCost < Fixed64.Zero
            || cell.RadiusClearance < Fixed64.Zero
            || cell.HeightClearance < Fixed64.Zero,
            parameterName,
            "Cell costs and clearances must be non-negative.");
        SwiftThrowHelper.ThrowIfArgument(
            (cell.Flags & ~NavigationCell.KnownFlags) != 0,
            parameterName,
            "Cell flags contain an unknown bit.");
    }

    private static bool SupportsMedium(NavigationCell cell, TraversalMedium medium) => medium switch
    {
        TraversalMedium.Solid => (cell.Media & TraversalMedia.Solid) != 0,
        TraversalMedium.Gas => (cell.Media & TraversalMedia.Gas) != 0,
        TraversalMedium.Liquid => (cell.Media & TraversalMedia.Liquid) != 0,
        _ => false
    };

    private bool TryGetAuthoredCell(
        NavigationCellEntry[] cells,
        VoxelIndex index,
        out NavigationCell cell)
    {
        int ordinal = FindCell(cells, index);
        if (ordinal >= 0)
        {
            cell = cells[ordinal].Cell;
            return true;
        }
        if (_defaultCell.HasValue && GridBinding.IsValidIndex(index))
        {
            cell = _defaultCell.Value;
            return true;
        }

        cell = default;
        return false;
    }

    private static int FindCell(NavigationCellEntry[] cells, VoxelIndex index)
    {
        int low = 0;
        int high = cells.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = cells[middle].Index.CompareTo(index);
            if (comparison == 0)
                return middle;
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }

        return -1;
    }

    private sealed class NavigationCellEntryComparer : IComparer<NavigationCellEntry>
    {
        internal static readonly NavigationCellEntryComparer Instance = new();

        public int Compare(NavigationCellEntry left, NavigationCellEntry right) =>
            left.Index.CompareTo(right.Index);
    }

    private sealed class NavigationConnectionComparer : IComparer<NavigationConnection>
    {
        internal static readonly NavigationConnectionComparer Instance = new();

        public int Compare(NavigationConnection? left, NavigationConnection? right) =>
            StringComparer.Ordinal.Compare(left?.Id, right?.Id);
    }

    private sealed class TraversalTransitionDefinitionComparer : IComparer<TraversalTransitionDefinition>
    {
        internal static readonly TraversalTransitionDefinitionComparer Instance = new();

        public int Compare(
            TraversalTransitionDefinition left,
            TraversalTransitionDefinition right) =>
            StringComparer.Ordinal.Compare(left.Id, right.Id);
    }
}

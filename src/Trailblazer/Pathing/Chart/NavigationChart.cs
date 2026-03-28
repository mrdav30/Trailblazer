using FixedMathSharp;
using SwiftCollections;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

// TODO: Consider only allowing these to be 1 layer high and using the Y axis for verticality, as this is more common in games and simplifies indexing logic.
// This would also allow us to use a more cache-friendly layout of X, Z, then Y, which is more intuitive for 2D grid-based pathfinding and reduces the likelihood of cache misses when iterating over walkable cells in a typical game scenario.
// ...or we could keep the current layout but provide optimized methods for iterating over surface cells that skip over empty vertical columns, which would mitigate some of the cache inefficiency while still supporting fully 3D charts when needed.

namespace Trailblazer.Pathing;

/// <summary>
/// Represents dense authored traversal data across surface and volume spaces.
/// Provides utility methods for querying authored cells and converting world positions into discrete grid indices.
/// </summary>
[Serializable]
public class NavigationChart
{
    /// <summary>
    /// The name identifier for this navigation chart.
    /// </summary>
    public readonly string Name;

    /// <summary>
    /// The minimum world-space bounds of the grid. Determines the starting point for grid indexing.
    /// </summary>
    public readonly Vector3d MinBounds;

    /// <summary>
    /// Higher values take precedence when this chart overlaps another chart on the same voxel.
    /// </summary>
    public readonly int Priority;

    /// <summary>
    /// The maximum world-space bounds of the grid, computed as MinBounds + grid size * Interval.
    /// Represents the exclusive upper bound of the grid.
    /// </summary>
    public readonly Vector3d MaxBounds;

    /// <summary>
    /// The distance between grid points along each axis.
    /// </summary>
    public readonly Fixed64 Interval;

    /// <summary>
    /// The number of cells along the X axis.
    /// </summary>
    public readonly int SizeX;

    /// <summary>
    /// The number of cells along the Y axis.
    /// </summary>
    public readonly int SizeY;

    /// <summary>
    /// The number of cells along the Z axis.
    /// </summary>
    public readonly int SizeZ;

    /// <summary>
    /// A flattened 3D map of authored chart cells indexed in row-major order across Y, X, then Z.
    /// </summary>
    private readonly NavigationChartCell[] _cells;

    /// <summary>
    /// Indicates whether this chart has been fully initialized and is ready for queries.
    /// </summary>
    public bool IsInitialized { get; internal set; }

    /// <summary>
    /// Tracks when the chart was registered relative to other charts.
    /// Higher values win same-priority overlap ties.
    /// </summary>
    public int RegistrationOrder { get; internal set; }

    /// <summary>
    /// Creates a new navigation chart using a pre-flattened map array and spatial parameters.
    /// </summary>
    /// <param name="name">The chart's unique identifier.</param>
    /// <param name="map">A flattened boolean array representing authored surface cells and empty cells.</param>
    /// <param name="sizeX">Number of cells along the X axis.</param>
    /// <param name="sizeY">Number of cells along the Y axis.</param>
    /// <param name="sizeZ">Number of cells along the Z axis.</param>
    /// <param name="minBounds">The minimum world-space bounds of the grid.</param>
    /// <param name="maxBounds">The maximum world-space bounds of the grid.</param>
    /// <param name="interval">Distance between adjacent grid points.</param>
    /// <param name="medium">The authored traversal medium emitted for each <c>true</c> cell.</param>
    /// <param name="priority">The authored precedence used when this chart overlaps another chart on the same voxel.</param>
     /// <exception cref="ArgumentNullException">Thrown when <paramref name="map"/> is null.</exception>
     /// <exception cref="ArgumentOutOfRangeException">
     /// Thrown when <paramref name="medium"/> is not <see cref="TraversalMedium.Solid"/>,
     /// <see cref="TraversalMedium.Gas"/>, or <see cref="TraversalMedium.Liquid"/>.
     /// </exception>
    public NavigationChart(
        string name,
        bool[] map,
        int sizeX,
        int sizeY,
        int sizeZ,
        Vector3d minBounds,
        Vector3d maxBounds,
        Fixed64 interval,
        TraversalMedium medium = TraversalMedium.Solid,
        int priority = 0)
        : this(
            name,
            CreateCells(map, medium),
            sizeX,
            sizeY,
            sizeZ,
            minBounds,
            maxBounds,
            interval,
            priority)
    { }

    /// <summary>
    /// Creates a new navigation chart using a pre-flattened cell array and spatial parameters.
    /// </summary>
    /// <param name="name">The chart's unique identifier.</param>
    /// <param name="cells">A flattened array representing authored chart cell payloads.</param>
    /// <param name="sizeX">Number of cells along the X axis.</param>
    /// <param name="sizeY">Number of cells along the Y axis.</param>
    /// <param name="sizeZ">Number of cells along the Z axis.</param>
    /// <param name="minBounds">The minimum world-space bounds of the grid.</param>
    /// <param name="maxBounds">The maximum world-space bounds of the grid.</param>
    /// <param name="interval">Distance between adjacent grid points.</param>
    /// <param name="priority">The authored precedence used when this chart overlaps another chart on the same voxel.</param>
    public NavigationChart(
        string name,
        NavigationChartCell[] cells,
        int sizeX,
        int sizeY,
        int sizeZ,
        Vector3d minBounds,
        Vector3d maxBounds,
        Fixed64 interval,
        int priority = 0)
    {
        Name = name;
        _cells = cells ?? throw new ArgumentNullException(nameof(cells));
        SizeX = sizeX;
        SizeY = sizeY;
        SizeZ = sizeZ;
        MinBounds = minBounds;
        MaxBounds = maxBounds;
        Interval = interval;
        Priority = priority;

        int expectedCellCount = sizeX * sizeY * sizeZ;
        if (_cells.Length != expectedCellCount)
            throw new ArgumentException($"Expected {expectedCellCount} chart cells but received {_cells.Length}.", nameof(cells));
    }

    /// <summary>
    /// Converts 3D grid indices (x, y, z) into a 1D flattened index for accessing the internal map.
    /// </summary>
    /// <param name="x">The X index in the grid.</param>
    /// <param name="y">The Y index in the grid.</param>
    /// <param name="z">The Z index in the grid.</param>
    /// <returns>The flattened index corresponding to the provided 3D coordinates.</returns>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int ToIndex(int x, int y, int z) => (y * SizeX * SizeZ) + (x * SizeZ) + z;

    /// <summary>
    /// Attempts to convert a world-space position into the grid's local indices.
    /// </summary>
    /// <param name="pos">The world-space position.</param>
    /// <param name="x">Resulting X index.</param>
    /// <param name="y">Resulting Y index.</param>
    /// <param name="z">Resulting Z index.</param>
    /// <returns>True if the position is within bounds; otherwise, false.</returns>
    public bool TryWorldToIndex(Vector3d pos, out int x, out int y, out int z)
    {
        x = (int)((pos.x - MinBounds.x) / Interval);
        y = (int)((pos.y - MinBounds.y) / Interval);
        z = (int)((pos.z - MinBounds.z) / Interval);

        bool valid = x >= 0 && x < SizeX &&
                     y >= 0 && y < SizeY &&
                     z >= 0 && z < SizeZ;

        if (!valid)
        {
            x = y = z = -1;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Checks if the given world-space position corresponds to an authored surface traversal cell.
    /// </summary>
    /// <param name="worldPos">The position to query.</param>
    /// <returns>True if traversable; otherwise, false.</returns>
    public bool IsWalkable(Vector3d worldPos)
    {
        if (!TryGetCell(worldPos, out NavigationChartCell cell))
            return false;

        return cell.HasSolid;
    }

    /// <summary>
    /// Attempts to retrieve the authored chart cell at the provided world-space position.
    /// </summary>
    /// <param name="worldPos">The world-space position to query.</param>
    /// <param name="cell">The authored chart cell payload.</param>
    /// <returns>True if the position resolves inside this chart; otherwise, false.</returns>
    public bool TryGetCell(Vector3d worldPos, out NavigationChartCell cell)
    {
        if (!TryWorldToIndex(worldPos, out int x, out int y, out int z))
        {
            cell = default;
            return false;
        }

        cell = GetCell(x, y, z);
        return true;
    }

    /// <summary>
    /// Returns all authored surface traversal positions within the chart.
    /// </summary>
    /// <returns>A collection of traversable surface positions.</returns>
    public IEnumerable<Vector3d> GetWalkablePositions()
    {
        foreach ((Vector3d position, _) in GetSurfaceCells())
            yield return position;
    }

    /// <summary>
    /// Returns each authored surface traversal position together with its authored cell payload.
    /// </summary>
    internal IEnumerable<(Vector3d Position, NavigationChartCell Cell)> GetSurfaceCells()
    {
        for (int y = 0; y < SizeY; y++)
            for (int x = 0; x < SizeX; x++)
                for (int z = 0; z < SizeZ; z++)
                {
                    NavigationChartCell cell = GetCell(x, y, z);
                    if (cell.HasSolid)
                    {
                        yield return (
                            new Vector3d(
                                MinBounds.x + x * Interval,
                                MinBounds.y + y * Interval,
                                MinBounds.z + z * Interval),
                            cell);
                    }
                }
    }

    /// <summary>
    /// Returns each authored traversal position together with its authored cell payload.
    /// </summary>
    internal IEnumerable<(Vector3d Position, NavigationChartCell Cell)> GetAuthoredCells()
    {
        for (int y = 0; y < SizeY; y++)
            for (int x = 0; x < SizeX; x++)
                for (int z = 0; z < SizeZ; z++)
                {
                    NavigationChartCell cell = GetCell(x, y, z);
                    if (!cell.HasTraversalData)
                        continue;

                    yield return (
                        new Vector3d(
                            MinBounds.x + x * Interval,
                            MinBounds.y + y * Interval,
                            MinBounds.z + z * Interval),
                        cell);
                }
    }

    /// <summary>
    /// Creates a navigation chart from a 3D boolean array representing authored traversal voxels.
    /// </summary>
    /// <param name="name">Name identifier for the chart.</param>
    /// <param name="sourceMap">3D map of authored cells (true = authored traversal).</param>
    /// <param name="minBounds">The minimum world-space bounds of the grid.</param>
    /// <param name="interval">The spacing between each grid point.</param>
    /// <param name="medium">The authored traversal medium emitted for each <c>true</c> cell.</param>
    /// <param name="priority">The authored precedence used when this chart overlaps another chart on the same voxel.</param>
    /// <returns>A constructed NavigationChart instance.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="sourceMap"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// Thrown when <paramref name="medium"/> is not <see cref="TraversalMedium.Solid"/>,
    /// <see cref="TraversalMedium.Gas"/>, or <see cref="TraversalMedium.Liquid"/>.
    /// </exception>
    public static NavigationChart From3D(
        string name,
        bool[,,] sourceMap,
        Vector3d minBounds,
        Fixed64 interval,
        TraversalMedium medium = TraversalMedium.Solid,
        int priority = 0)
    {
        if (sourceMap == null)
            ThrowHelper.ThrowArgumentNullException(nameof(sourceMap));

        int sizeY = sourceMap.GetLength(0);
        int sizeX = sourceMap.GetLength(1);
        int sizeZ = sourceMap.GetLength(2);

        Vector3d maxBounds = minBounds + new Vector3d(
            sizeX * interval,
            sizeY * interval,
            sizeZ * interval
        );

        var flat = new bool[sizeX * sizeY * sizeZ];
        for (int y = 0; y < sizeY; y++)
            for (int x = 0; x < sizeX; x++)
                for (int z = 0; z < sizeZ; z++)
                    flat[(y * sizeX * sizeZ) + (x * sizeZ) + z] = sourceMap[y, x, z];

        return new(
            name,
            CreateCells(flat, medium),
            sizeX,
            sizeY,
            sizeZ,
            minBounds,
            maxBounds,
            interval,
            priority);
    }

    /// <summary>
    /// Creates a navigation chart from a 3D array of authored chart cell payloads.
    /// </summary>
    /// <param name="name">Name identifier for the chart.</param>
    /// <param name="sourceMap">3D map of authored chart cells.</param>
    /// <param name="minBounds">The minimum world-space bounds of the grid.</param>
    /// <param name="interval">The spacing between each grid point.</param>
    /// <param name="priority">The authored precedence used when this chart overlaps another chart on the same voxel.</param>
    /// <returns>A constructed NavigationChart instance.</returns>
    public static NavigationChart From3D(
        string name,
        NavigationChartCell[,,] sourceMap,
        Vector3d minBounds,
        Fixed64 interval,
        int priority = 0)
    {
        if (sourceMap == null)
            ThrowHelper.ThrowArgumentNullException(nameof(sourceMap));

        int sizeY = sourceMap.GetLength(0);
        int sizeX = sourceMap.GetLength(1);
        int sizeZ = sourceMap.GetLength(2);

        Vector3d maxBounds = minBounds + new Vector3d(
            sizeX * interval,
            sizeY * interval,
            sizeZ * interval
        );

        var flat = new NavigationChartCell[sizeX * sizeY * sizeZ];
        for (int y = 0; y < sizeY; y++)
            for (int x = 0; x < sizeX; x++)
                for (int z = 0; z < sizeZ; z++)
                    flat[(y * sizeX * sizeZ) + (x * sizeZ) + z] = sourceMap[y, x, z];

        return new(
            name,
            flat,
            sizeX,
            sizeY,
            sizeZ,
            minBounds,
            maxBounds,
            interval,
            priority);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private NavigationChartCell GetCell(int x, int y, int z) => _cells[ToIndex(x, y, z)];

    private static NavigationChartCell[] CreateCells(bool[] map, TraversalMedium medium)
    {
        if (map == null)
            ThrowHelper.ThrowArgumentNullException(nameof(map));

        NavigationChartCell traversableCell = medium switch
        {
            TraversalMedium.Solid => NavigationChartCell.Solid,
            TraversalMedium.Gas => NavigationChartCell.Gas,
            TraversalMedium.Liquid => NavigationChartCell.Liquid,
            _ => throw new ArgumentOutOfRangeException(
                nameof(medium),
                medium,
                "Boolean chart factories support Solid, Gas, or Liquid traversal only.")
        };

        var cells = new NavigationChartCell[map.Length];
        for (int i = 0; i < map.Length; i++)
            cells[i] = map[i] ? traversableCell : NavigationChartCell.Empty;

        return cells;
    }
}

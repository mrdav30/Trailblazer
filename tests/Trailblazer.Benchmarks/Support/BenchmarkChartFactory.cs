using FixedMathSharp;
using GridForge.Configuration;
using System;
using Trailblazer.Pathing;

namespace Trailblazer.Benchmarks;

/// <summary>
/// Builds deterministic NavigationChart fixtures for benchmark scenarios.
/// All charts use a 1-unit interval and are registered plus initialized through PathManager.
/// Charts have a flat Y=0 surface layer unless otherwise noted.
/// </summary>
internal static class BenchmarkChartFactory
{
    // -------------------------------------------------------------------------
    // Grid configuration helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a GridConfiguration large enough to contain a square chart whose cells
    /// span [0, size-1] on X and Z at Y=0.
    /// </summary>
    public static GridConfiguration GridConfigForSquare(int size, int padding = 4)
    {
        int extent = size + padding;
        return new GridConfiguration(
            new Vector3d(-padding, -padding, -padding),
            new Vector3d(extent, extent, extent));
    }

    /// <summary>
    /// Returns a GridConfiguration large enough to contain a corridor of <paramref name="length"/> cells
    /// running along the X axis at Y=0, Z=0.
    /// </summary>
    public static GridConfiguration GridConfigForCorridor(int length, int padding = 4)
    {
        int extent = length + padding;
        return new GridConfiguration(
            new Vector3d(-padding, -padding, -padding),
            new Vector3d(extent, padding, padding));
    }

    // -------------------------------------------------------------------------
    // Open plane
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers and initializes a fully walkable square chart.
    /// Origin is (0,0,0); cells span [0, size-1] on X and Z.
    /// </summary>
    /// <param name="name">Unique chart name.</param>
    /// <param name="size">Number of cells along each horizontal axis.</param>
    /// <returns>The origin (0,0,0) and far corner (size-1, 0, size-1).</returns>
    public static (Vector3d Origin, Vector3d FarCorner) RegisterOpenPlane(string name, int size)
    {
        bool[,,] data = new bool[1, size, size];
        for (int x = 0; x < size; x++)
            for (int z = 0; z < size; z++)
                data[0, x, z] = true;

        var chart = NavigationChart.From3D(name, data, Vector3d.Zero, Fixed64.One);
        PathManager.Register(chart);

        return (Vector3d.Zero, new Vector3d(size - 1, 0, size - 1));
    }

    // -------------------------------------------------------------------------
    // Long corridor
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers and initializes a 1-cell-wide corridor of <paramref name="length"/> cells
    /// running along the X axis starting at (0,0,0).
    /// </summary>
    /// <param name="name">Unique chart name.</param>
    /// <param name="length">Number of walkable cells.</param>
    /// <returns>The start (0,0,0) and end (length-1, 0, 0) endpoints.</returns>
    public static (Vector3d Start, Vector3d End) RegisterLongCorridor(string name, int length)
    {
        bool[,,] data = new bool[1, length, 1];
        for (int x = 0; x < length; x++)
            data[0, x, 0] = true;

        var chart = NavigationChart.From3D(name, data, Vector3d.Zero, Fixed64.One);
        PathManager.Register(chart);

        return (Vector3d.Zero, new Vector3d(length - 1, 0, 0));
    }

    // -------------------------------------------------------------------------
    // Sparse blocker field
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers and initializes a square chart with regularly spaced blocked cells.
    /// Blockers appear at every other position on an offset grid, leaving navigable
    /// paths through the field. Origin is (0,0,0).
    /// </summary>
    /// <param name="name">Unique chart name.</param>
    /// <param name="size">Number of cells along each horizontal axis.</param>
    /// <returns>A near-origin start and a far-corner end endpoint that are both walkable.</returns>
    public static (Vector3d Start, Vector3d End) RegisterSparseBlockerField(string name, int size)
    {
        bool[,,] data = new bool[1, size, size];
        for (int x = 0; x < size; x++)
        {
            for (int z = 0; z < size; z++)
            {
                // Checker-style blockers with a border of open cells so endpoints stay reachable.
                bool isBorder = x == 0 || z == 0 || x == size - 1 || z == size - 1;
                bool isBlocker = !isBorder && (x % 4 == 2) && (z % 4 == 2);
                data[0, x, z] = !isBlocker;
            }
        }

        var chart = NavigationChart.From3D(name, data, Vector3d.Zero, Fixed64.One);
        PathManager.Register(chart);

        return (Vector3d.Zero, new Vector3d(size - 1, 0, size - 1));
    }

    // -------------------------------------------------------------------------
    // Choke point (unit-size clearance failure)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers a chart that has a single-voxel gap blocked for unit sizes larger than 1.
    /// A size-2 agent cannot pass; a size-1 agent can.
    /// </summary>
    /// <param name="name">Unique chart name.</param>
    /// <returns>Endpoints on opposite sides of the choke.</returns>
    public static (Vector3d Start, Vector3d End) RegisterChokePoint(string name)
    {
        // 7 wide, 5 deep. A single-voxel gap at column x=3.
        const int sizeX = 7;
        const int sizeZ = 5;
        bool[,,] data = new bool[1, sizeX, sizeZ];
        for (int x = 0; x < sizeX; x++)
        {
            for (int z = 0; z < sizeZ; z++)
            {
                // Block the middle column except the center cell.
                bool isChokeColumn = x == sizeX / 2;
                bool isCenterRow = z == sizeZ / 2;
                data[0, x, z] = !isChokeColumn || isCenterRow;
            }
        }

        var chart = NavigationChart.From3D(name, data, Vector3d.Zero, Fixed64.One);
        PathManager.Register(chart);

        return (new Vector3d(0, 0, sizeZ / 2), new Vector3d(sizeX - 1, 0, sizeZ / 2));
    }

    // -------------------------------------------------------------------------
    // Destination cluster (many starts, shared flow-field destination)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Registers a fully walkable square chart and returns an array of
    /// <paramref name="startCount"/> spread-out start positions and a shared destination.
    /// All starts and the destination are on the walkable surface.
    /// </summary>
    /// <param name="name">Unique chart name.</param>
    /// <param name="size">Number of cells per side.</param>
    /// <param name="startCount">How many distinct start positions to return.</param>
    /// <returns>An array of start positions and the shared destination.</returns>
    public static (Vector3d[] Starts, Vector3d Destination) RegisterDestinationCluster(
        string name,
        int size,
        int startCount)
    {
        bool[,,] data = new bool[1, size, size];
        for (int x = 0; x < size; x++)
            for (int z = 0; z < size; z++)
                data[0, x, z] = true;

        var chart = NavigationChart.From3D(name, data, Vector3d.Zero, Fixed64.One);
        PathManager.Register(chart);

        var destination = new Vector3d(size - 1, 0, size - 1);
        var starts = new Vector3d[startCount];
        int stride = Math.Max(1, (size - 2) / (int)Math.Ceiling(Math.Sqrt(startCount)));
        int index = 0;
        for (int z = 0; z < size - 1 && index < startCount; z += stride)
        {
            for (int x = 0; x < size - 1 && index < startCount; x += stride)
                starts[index++] = new Vector3d(x, 0, z);
        }

        // Fill remaining with the first position if geometry ran out.
        for (int i = index; i < startCount; i++)
            starts[i] = starts[0];

        return (starts, destination);
    }

    // -------------------------------------------------------------------------
    // Cache pressure set (unique request key factory)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns an array of <paramref name="count"/> unique start positions spread across
    /// an already-registered open-plane chart. Used for cache pressure scenarios.
    /// The caller is responsible for registering an open-plane chart that covers these positions.
    /// </summary>
    /// <param name="size">Side length of the registered open plane.</param>
    /// <param name="count">Number of unique positions to generate.</param>
    /// <param name="destination">A fixed destination position used for all requests.</param>
    public static Vector3d[] GenerateUniqueStartPositions(int size, int count, out Vector3d destination)
    {
        destination = new Vector3d(size - 1, 0, size - 1);
        var positions = new Vector3d[count];
        int index = 0;
        for (int z = 0; z < size && index < count; z++)
        {
            for (int x = 0; x < size && index < count; x++)
            {
                // Skip the destination cell itself.
                if (x == size - 1 && z == size - 1)
                    continue;
                positions[index++] = new Vector3d(x, 0, z);
            }
        }

        if (index < count)
            throw new InvalidOperationException(
                $"Open plane of size {size} provides only {index} unique positions but {count} were requested.");

        return positions;
    }
}

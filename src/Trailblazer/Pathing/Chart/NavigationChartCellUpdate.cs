namespace Trailblazer.Pathing;

/// <summary>
/// Describes one authored cell mutation to apply to a registered <see cref="NavigationChart"/>.
/// </summary>
public readonly struct NavigationChartCellUpdate
{
    /// <summary>
    /// The target cell index on the X axis.
    /// </summary>
    public int X { get; }

    /// <summary>
    /// The target cell index on the Y axis.
    /// </summary>
    public int Y { get; }

    /// <summary>
    /// The target cell index on the Z axis.
    /// </summary>
    public int Z { get; }

    /// <summary>
    /// The authored cell payload to write at the requested indices.
    /// </summary>
    public NavigationChartCell Cell { get; }

    /// <summary>
    /// Creates a sparse chart-cell update.
    /// </summary>
    public NavigationChartCellUpdate(int x, int y, int z, NavigationChartCell cell)
    {
        X = x;
        Y = y;
        Z = z;
        Cell = cell;
    }
}

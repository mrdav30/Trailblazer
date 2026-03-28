using SwiftCollections;
using System;
using System.Collections.Generic;

namespace Trailblazer.Pathing;

/// <summary>
/// Tracks all authored chart cells that claim one voxel and resolves the winning effective cell.
/// </summary>
internal sealed class ResolvedChartVoxelState
{
    private readonly SwiftHashSet<string> _chartOwners = new();

    private readonly SwiftDictionary<string, NavigationChartCell> _chartCells =
        new(4, StringComparer.Ordinal);

    public SwiftHashSet<string> ChartOwners => _chartOwners;

    public bool HasAnyOwners => _chartOwners.Count > 0;

    public string EffectiveChartOwner { get; private set; }

    public NavigationChartCell EffectiveCell { get; private set; }

    public void AddOwner(string chartName, NavigationChartCell cell)
    {
        _chartOwners.Add(chartName);
        _chartCells[chartName] = cell;
        ResolveEffectiveCell();
    }

    public void RemoveOwner(string chartName)
    {
        _chartOwners.Remove(chartName);
        _chartCells.Remove(chartName);
        ResolveEffectiveCell();
    }

    private void ResolveEffectiveCell()
    {
        EffectiveChartOwner = null;
        EffectiveCell = NavigationChartCell.Empty;

        foreach (KeyValuePair<string, NavigationChartCell> pair in _chartCells)
        {
            if (EffectiveChartOwner == null
                || PathManager.IsHigherChartPrecedence(pair.Key, EffectiveChartOwner))
            {
                EffectiveChartOwner = pair.Key;
                EffectiveCell = pair.Value;
            }
        }
    }
}

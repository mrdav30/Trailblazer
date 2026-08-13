//=======================================================================
// ResolvedChartVoxelState.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;
using SwiftCollections;

namespace Trailblazer.Pathing;

/// <summary>
/// Tracks all authored chart cells that claim one voxel and resolves the winning effective cell.
/// </summary>
internal sealed class ResolvedChartVoxelState
{
    private readonly SwiftDictionary<string, ChartContribution> _chartContributions =
        new(4, StringComparer.Ordinal);

    private int _effectivePriority;

    private int _effectiveRegistrationOrder;

    public bool HasAnyOwners => _chartContributions.Count > 0;

    public string? EffectiveChartOwner { get; private set; }

    public NavigationChartCell EffectiveCell { get; private set; }

    public void AddOwner(
        string chartName,
        NavigationChartCell cell,
        int priority,
        int registrationOrder)
    {
        var contribution = new ChartContribution(cell, priority, registrationOrder);
        _chartContributions[chartName] = contribution;

        if (EffectiveChartOwner == null
            || string.Equals(chartName, EffectiveChartOwner, StringComparison.Ordinal)
            || HasHigherPrecedence(
                chartName,
                priority,
                registrationOrder,
                EffectiveChartOwner,
                _effectivePriority,
                _effectiveRegistrationOrder))
        {
            SetEffectiveContribution(chartName, contribution);
        }
    }

    public void RemoveOwner(string chartName)
    {
        if (!_chartContributions.ContainsKey(chartName))
            return;

        _chartContributions.Remove(chartName);
        if (string.Equals(chartName, EffectiveChartOwner, StringComparison.Ordinal))
            ResolveEffectiveCell();
    }

    public bool ContainsOwner(string chartName) => _chartContributions.ContainsKey(chartName);

    public void AddChartOwnersTo(SwiftHashSet<string> destination)
    {
        if (destination == null)
            return;

        foreach (KeyValuePair<string, ChartContribution> pair in _chartContributions)
            destination.Add(pair.Key);
    }

    private void ResolveEffectiveCell()
    {
        EffectiveChartOwner = null;
        EffectiveCell = NavigationChartCell.Empty;
        _effectivePriority = 0;
        _effectiveRegistrationOrder = 0;

        foreach (KeyValuePair<string, ChartContribution> pair in _chartContributions)
        {
            if (EffectiveChartOwner == null
                || HasHigherPrecedence(
                    pair.Key,
                    pair.Value.Priority,
                    pair.Value.RegistrationOrder,
                    EffectiveChartOwner,
                    _effectivePriority,
                    _effectiveRegistrationOrder))
            {
                SetEffectiveContribution(pair.Key, pair.Value);
            }
        }
    }

    private void SetEffectiveContribution(string chartName, ChartContribution contribution)
    {
        EffectiveChartOwner = chartName;
        EffectiveCell = contribution.Cell;
        _effectivePriority = contribution.Priority;
        _effectiveRegistrationOrder = contribution.RegistrationOrder;
    }

    private static bool HasHigherPrecedence(
        string candidateChartName,
        int candidatePriority,
        int candidateRegistrationOrder,
        string currentChartName,
        int currentPriority,
        int currentRegistrationOrder)
    {
        if (candidatePriority != currentPriority)
            return candidatePriority > currentPriority;

        if (candidateRegistrationOrder != currentRegistrationOrder)
            return candidateRegistrationOrder > currentRegistrationOrder;

        return string.CompareOrdinal(candidateChartName, currentChartName) > 0;
    }

    private readonly struct ChartContribution
    {
        public ChartContribution(NavigationChartCell cell, int priority, int registrationOrder)
        {
            Cell = cell;
            Priority = priority;
            RegistrationOrder = registrationOrder;
        }

        public NavigationChartCell Cell { get; }

        public int Priority { get; }

        public int RegistrationOrder { get; }
    }
}

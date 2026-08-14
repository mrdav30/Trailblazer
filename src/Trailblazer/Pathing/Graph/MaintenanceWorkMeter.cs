//=======================================================================
// MaintenanceWorkMeter.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Debits every deterministic maintenance producer against one fixed-step budget.</summary>
internal sealed class MaintenanceWorkMeter
{
    private readonly MaintenanceWorkBudget _budget;
    private int _consumedEnvelopes;
    private int _baselineAddresses;
    private int _overlaySlots;
    private int _componentNodes;
    private int _seamCandidateProbes;
    private int _explicitEdges;
    private int _dependencyEntries;

    internal MaintenanceWorkMeter(MaintenanceWorkBudget budget) => _budget = budget;

    internal int ConsumedEnvelopes => _consumedEnvelopes;
    internal int BaselineAddresses => _baselineAddresses;
    internal int OverlaySlots => _overlaySlots;
    internal int ComponentNodes => _componentNodes;
    internal int SeamCandidateProbes => _seamCandidateProbes;
    internal int ExplicitEdges => _explicitEdges;
    internal int DependencyEntries => _dependencyEntries;

    internal int RemainingEnvelopes => _budget.MaxConsumedEnvelopes - ConsumedEnvelopes;
    internal int RemainingBaselineAddresses => _budget.MaxBaselineAddresses - BaselineAddresses;
    internal int RemainingOverlaySlots => _budget.MaxOverlaySlots - OverlaySlots;
    internal int RemainingComponentNodes => _budget.MaxComponentNodes - ComponentNodes;
    internal int RemainingSeamCandidateProbes =>
        _budget.MaxSeamCandidateProbes - SeamCandidateProbes;
    internal int RemainingExplicitEdges => _budget.MaxExplicitEdges - ExplicitEdges;
    internal int RemainingDependencyEntries => _budget.MaxDependencyEntries - DependencyEntries;

    internal void Reset()
    {
        _consumedEnvelopes = 0;
        _baselineAddresses = 0;
        _overlaySlots = 0;
        _componentNodes = 0;
        _seamCandidateProbes = 0;
        _explicitEdges = 0;
        _dependencyEntries = 0;
    }

    internal bool TryConsumeEnvelopes(int count) =>
        TryConsume(ref _consumedEnvelopes, count, _budget.MaxConsumedEnvelopes);

    internal bool TryConsumeBaselineAddresses(int count) =>
        TryConsume(ref _baselineAddresses, count, _budget.MaxBaselineAddresses);

    internal bool TryConsumeOverlaySlots(int count) =>
        TryConsume(ref _overlaySlots, count, _budget.MaxOverlaySlots);

    internal bool TryConsumeComponentNodes(int count) =>
        TryConsume(ref _componentNodes, count, _budget.MaxComponentNodes);

    internal bool TryConsumeSeamCandidateProbes(int count) =>
        TryConsume(ref _seamCandidateProbes, count, _budget.MaxSeamCandidateProbes);

    internal bool TryConsumeExplicitEdges(int count) =>
        TryConsume(ref _explicitEdges, count, _budget.MaxExplicitEdges);

    internal bool TryConsumeDependencyEntries(int count) =>
        TryConsume(ref _dependencyEntries, count, _budget.MaxDependencyEntries);

    private static bool TryConsume(ref int consumed, int count, int maximum)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(count < 0, count, nameof(count));
        if (count > maximum - consumed)
            return false;
        consumed += count;
        return true;
    }
}

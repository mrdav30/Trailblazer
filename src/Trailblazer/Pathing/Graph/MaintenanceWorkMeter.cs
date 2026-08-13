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
    private int _seamCandidates;
    private int _componentNodes;
    private int _implicitEdges;
    private int _explicitEdges;
    private int _dependencyEntries;
    private int _cacheInvalidations;

    internal MaintenanceWorkMeter(MaintenanceWorkBudget budget) => _budget = budget;

    internal int ConsumedEnvelopes => _consumedEnvelopes;
    internal int BaselineAddresses => _baselineAddresses;
    internal int OverlaySlots => _overlaySlots;
    internal int SeamCandidates => _seamCandidates;
    internal int ComponentNodes => _componentNodes;
    internal int ImplicitEdges => _implicitEdges;
    internal int ExplicitEdges => _explicitEdges;
    internal int DependencyEntries => _dependencyEntries;
    internal int CacheInvalidations => _cacheInvalidations;

    internal int RemainingEnvelopes => _budget.MaxConsumedEnvelopes - ConsumedEnvelopes;
    internal int RemainingBaselineAddresses => _budget.MaxBaselineAddresses - BaselineAddresses;
    internal int RemainingOverlaySlots => _budget.MaxOverlaySlots - OverlaySlots;
    internal int RemainingSeamCandidates => _budget.MaxSeamCandidates - SeamCandidates;
    internal int RemainingComponentNodes => _budget.MaxComponentNodes - ComponentNodes;
    internal int RemainingImplicitEdges => _budget.MaxImplicitEdges - ImplicitEdges;
    internal int RemainingExplicitEdges => _budget.MaxExplicitEdges - ExplicitEdges;
    internal int RemainingDependencyEntries => _budget.MaxDependencyEntries - DependencyEntries;
    internal int RemainingCacheInvalidations => _budget.MaxCacheInvalidations - CacheInvalidations;

    internal void Reset()
    {
        _consumedEnvelopes = 0;
        _baselineAddresses = 0;
        _overlaySlots = 0;
        _seamCandidates = 0;
        _componentNodes = 0;
        _implicitEdges = 0;
        _explicitEdges = 0;
        _dependencyEntries = 0;
        _cacheInvalidations = 0;
    }

    internal bool TryConsumeEnvelopes(int count) =>
        TryConsume(ref _consumedEnvelopes, count, _budget.MaxConsumedEnvelopes);

    internal bool TryConsumeBaselineAddresses(int count) =>
        TryConsume(ref _baselineAddresses, count, _budget.MaxBaselineAddresses);

    internal bool TryConsumeOverlaySlots(int count) =>
        TryConsume(ref _overlaySlots, count, _budget.MaxOverlaySlots);

    internal bool TryConsumeSeamCandidates(int count) =>
        TryConsume(ref _seamCandidates, count, _budget.MaxSeamCandidates);

    internal bool TryConsumeComponentNodes(int count) =>
        TryConsume(ref _componentNodes, count, _budget.MaxComponentNodes);

    internal bool TryConsumeImplicitEdges(int count) =>
        TryConsume(ref _implicitEdges, count, _budget.MaxImplicitEdges);

    internal bool TryConsumeExplicitEdges(int count) =>
        TryConsume(ref _explicitEdges, count, _budget.MaxExplicitEdges);

    internal bool TryConsumeDependencyEntries(int count) =>
        TryConsume(ref _dependencyEntries, count, _budget.MaxDependencyEntries);

    internal bool TryConsumeCacheInvalidations(int count) =>
        TryConsume(ref _cacheInvalidations, count, _budget.MaxCacheInvalidations);

    private static bool TryConsume(ref int consumed, int count, int maximum)
    {
        SwiftThrowHelper.ThrowIfArgumentOutOfRange(count < 0, count, nameof(count));
        if (count > maximum - consumed)
            return false;
        consumed += count;
        return true;
    }
}

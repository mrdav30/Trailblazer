//=======================================================================
// NavigationEndpointProofWork.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Certifies admission against a payload, or captures its independently owned endpoint proof.</summary>
internal struct NavigationEndpointProofWork
{
    private NavigationDependencySortWork _sort;
    private NavigationDependencyStampWork? _stamp;
    private readonly NavigationEndpointWorkspace _workspace;
    private readonly NavigationResolvedPathQuery _query;
    private readonly GraphDependencyStamp _payloadProof;
    private int _ordinal;
    private int _low;
    private int _high;
    private int _match;
    private bool _pages;
    private bool _certificationComplete;

    internal NavigationEndpointProofWork(NavigationEndpointWorkspace workspace, NavigationResolvedPathQuery query,
        GraphDependencyStamp payloadProof)
    {
        this = default;
        _workspace = workspace;
        _query = query;
        _payloadProof = payloadProof;
        _high = payloadProof.Components.Length - 1;
        _match = -1;
        _sort = new NavigationDependencySortWork(workspace.Components, workspace.ComponentCount,
            workspace.Pages, workspace.PageCount);
        _certificationComplete = payloadProof.AreaPolicy != query.AreaPolicy.Key
            || (workspace.Dependencies.HasTransitionDependency
                && (!payloadProof.HasTransitionRuleDependency
                    || payloadProof.TransitionRuleVersion != query.Graph.TransitionRules.Version));
    }

    internal bool IsCertified { get; private set; }
    internal GraphDependencyStamp? Proof => IsCertified ? null : _stamp!.Result;

    internal static bool IsCurrent(NavigationWorldGraphStore store, GraphDependencyStamp? proof)
        => proof == null || store.Current.IsDependencyCurrent(proof);

    internal NavigationQueryAdmissionStatus AdvanceCertification(ref NavigationAcquisitionSlice remaining)
    {
        // Search each admitted address in the canonical immutable proof. No copy, sort or
        // allocation is required on a certified hit; each comparison and graph read is charged.
        while (!_certificationComplete)
        {
            if (_ordinal == (_pages ? _workspace.PageCount : _workspace.ComponentCount))
            {
                if (_pages)
                {
                    IsCertified = true;
                    _certificationComplete = true;
                    break;
                }
                _pages = true;
                _ordinal = 0;
                ResetSearch();
                continue;
            }
            if (_match < 0 && _low > _high)
            {
                _certificationComplete = true;
                break;
            }
            if (remaining.LookupRemaining == 0 || !_query.Meter.TryConsumeLookupProbes(1))
                return _query.Meter.RemainingLookupProbes == 0
                    ? NavigationQueryAdmissionStatus.BudgetExceeded : NavigationQueryAdmissionStatus.Pending;
            remaining.LookupRemaining--;
            if (_match >= 0)
            {
                // Successful admission validated this readset against the same retained immutable root.
                bool current;
                if (_pages)
                {
                    bool found = _query.Graph.TryGetPageDependency(_workspace.Pages[_ordinal],
                        _payloadProof.HasTransitionRuleDependency, out GraphPageDependency page);
                    System.Diagnostics.Debug.Assert(found);
                    current = page.Equals(_payloadProof.Pages[_match]);
                }
                else
                {
                    bool found = _query.Graph.TryGetComponentDependency(_workspace.Components[_ordinal],
                        out GraphComponentDependency component);
                    System.Diagnostics.Debug.Assert(found);
                    current = component.Equals(_payloadProof.Components[_match]);
                }
                if (!current)
                {
                    _certificationComplete = true;
                    break;
                }
                _ordinal++;
                ResetSearch();
                continue;
            }
            int middle = _low + ((_high - _low) / 2);
            int comparison;
            if (_pages)
            {
                GraphPageDependency expected = _payloadProof.Pages[middle];
                GraphPageDependencyAddress actual = _workspace.Pages[_ordinal];
                comparison = string.CompareOrdinal(expected.MapId, actual.MapId);
                if (comparison == 0)
                    comparison = expected.PageIndex.CompareTo(actual.PageIndex);
            }
            else comparison = _payloadProof.Components[middle].Key.CompareTo(_workspace.Components[_ordinal]);
            if (comparison == 0) _match = middle;
            else if (comparison < 0) _low = middle + 1;
            else _high = middle - 1;
        }
        return NavigationQueryAdmissionStatus.Success;
    }

    private void ResetSearch()
    {
        _low = 0;
        _high = (_pages ? _payloadProof.Pages.Length : _payloadProof.Components.Length) - 1;
        _match = -1;
    }

    internal NavigationQueryAdmissionStatus AdvanceCapture(ref NavigationAcquisitionSlice remaining)
    {
        NavigationWorkMeter meter = _query.Meter;
        int before = meter.LookupProbes;
        bool sorted = _sort.Advance(meter, remaining.LookupRemaining);
        remaining.LookupRemaining -= meter.LookupProbes - before;
        if (!sorted)
            return meter.RemainingLookupProbes == 0 ? NavigationQueryAdmissionStatus.BudgetExceeded : NavigationQueryAdmissionStatus.Pending;
        _stamp ??= new NavigationDependencyStampWork(_query.Graph, _query.AreaPolicy,
            _workspace.Components, _workspace.ComponentCount, _workspace.Pages, _workspace.PageCount,
            _workspace.Dependencies.HasTransitionDependency);
        before = meter.LookupProbes;
        bool complete = _stamp.Advance(meter, remaining.LookupRemaining);
        remaining.LookupRemaining -= meter.LookupProbes - before;
        return !complete
            ? meter.RemainingLookupProbes == 0 ? NavigationQueryAdmissionStatus.BudgetExceeded : NavigationQueryAdmissionStatus.Pending
            : _stamp.IsValid ? NavigationQueryAdmissionStatus.Success : NavigationQueryAdmissionStatus.Stale;
    }
}

//=======================================================================
// NavigationAreaCatalog.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;

namespace Trailblazer.Pathing;

/// <summary>Stores one immutable, ordinally indexed set of latest area-policy revisions.</summary>
internal sealed class NavigationAreaCatalog
{
    private readonly NavigationAreaPolicy[] _policies;
    private readonly long _retainedBytes;

    private NavigationAreaCatalog(NavigationAreaPolicy[] policies, int totalRuleCount, long version)
    {
        _policies = policies;
        TotalRuleCount = totalRuleCount;
        Version = version;
        long bytes = 48L + ((long)_policies.Length * sizeof(long));
        for (int i = 0; i < _policies.Length; i++)
            bytes = checked(bytes + _policies[i].RetainedBytes);
        _retainedBytes = bytes;
    }

    internal static NavigationAreaCatalog Empty { get; } = new(
        Array.Empty<NavigationAreaPolicy>(),
        totalRuleCount: 0,
        version: 0);

    internal int PolicyCount => _policies.Length;

    internal int TotalRuleCount { get; }

    internal long Version { get; }

    internal long RetainedBytes => _retainedBytes;

    internal int PersistentPageCount => 1 + _policies.Length;

    internal int GetPublishWork(
        NavigationAreaPolicy policy,
        int maxPolicies,
        int maxRules)
    {
        int index = FindPolicy(policy.Key.PolicyId);
        bool canInsert = index < 0
            && _policies.Length < maxPolicies
            && TotalRuleCount <= maxRules - policy.RuleCount;
        int copiedPolicyReferences = canInsert
            ? _policies.Length + 1
            : _policies.Length;
        return checked(
            1 + Math.Max(policy.RuleCount, 2 * copiedPolicyReferences));
    }

    internal bool TryGet(NavigationAreaPolicyKey key, out NavigationAreaPolicy? policy)
    {
        int index = FindPolicy(key.PolicyId);
        if (index >= 0 && _policies[index].Key.Equals(key))
        {
            policy = _policies[index];
            return true;
        }
        policy = null;
        return false;
    }

    internal NavigationOperationRejection TryPublish(
        NavigationAreaPolicy policy,
        int maxPolicies,
        int requiredRuleCount,
        int maxRulesPerPolicy,
        int maxRules,
        out NavigationAreaCatalog next)
    {
        if (policy.RuleCount != requiredRuleCount)
        {
            next = this;
            return NavigationOperationRejection.ValidationFailed;
        }
        if (policy.RuleCount > maxRulesPerPolicy)
        {
            next = this;
            return NavigationOperationRejection.CapacityExceeded;
        }

        int index = FindPolicy(policy.Key.PolicyId);
        if (index >= 0)
        {
            NavigationAreaPolicy current = _policies[index];
            if (policy.Key.Revision < current.Key.Revision)
            {
                next = this;
                return NavigationOperationRejection.Stale;
            }
            if (policy.Key.Revision == current.Key.Revision)
            {
                next = this;
                return current.ContentEquals(policy)
                    ? NavigationOperationRejection.None
                    : NavigationOperationRejection.ValidationFailed;
            }

            int rules = checked(TotalRuleCount - current.RuleCount + policy.RuleCount);
            if (rules > maxRules)
            {
                next = this;
                return NavigationOperationRejection.CapacityExceeded;
            }
            var replacement = (NavigationAreaPolicy[])_policies.Clone();
            replacement[index] = policy;
            next = new NavigationAreaCatalog(replacement, rules, Version + 1);
            return NavigationOperationRejection.None;
        }

        if (_policies.Length >= maxPolicies || TotalRuleCount + policy.RuleCount > maxRules)
        {
            next = this;
            return NavigationOperationRejection.CapacityExceeded;
        }

        int insertion = ~index;
        var added = new NavigationAreaPolicy[_policies.Length + 1];
        Array.Copy(_policies, 0, added, 0, insertion);
        added[insertion] = policy;
        Array.Copy(_policies, insertion, added, insertion + 1, _policies.Length - insertion);
        next = new NavigationAreaCatalog(added, TotalRuleCount + policy.RuleCount, Version + 1);
        return NavigationOperationRejection.None;
    }

    private int FindPolicy(string policyId)
    {
        int low = 0;
        int high = _policies.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = string.CompareOrdinal(_policies[middle].Key.PolicyId, policyId);
            if (comparison == 0)
                return middle;
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        return ~low;
    }
}

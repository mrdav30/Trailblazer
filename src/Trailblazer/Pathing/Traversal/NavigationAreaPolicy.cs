//=======================================================================
// NavigationAreaPolicy.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Collections.Generic;

namespace Trailblazer.Pathing;

/// <summary>
/// Stores one immutable direct-indexed navigation-area policy snapshot.
/// </summary>
public sealed class NavigationAreaPolicy
{
    private const int MaximumRuleCount = ushort.MaxValue + 1;
    private readonly NavigationAreaRule[] _rules;
    private readonly long _retainedBytes;

    /// <summary>Creates a policy by copying rules whose indices are stable area IDs.</summary>
    public NavigationAreaPolicy(
        NavigationAreaPolicyKey key,
        IReadOnlyList<NavigationAreaRule> rules)
    {
        key.Validate(nameof(key));
        SwiftThrowHelper.ThrowIfNull(rules, nameof(rules));
        SwiftThrowHelper.ThrowIfArgument(
            rules.Count == 0 || rules.Count > MaximumRuleCount,
            nameof(rules),
            "A policy requires between one and 65,536 direct-indexed rules.");

        Key = key;
        _rules = new NavigationAreaRule[rules.Count];
        for (int i = 0; i < rules.Count; i++)
            _rules[i] = rules[i];
        _retainedBytes = checked(
            48L
            + ((long)key.PolicyId.Length * sizeof(char))
            + ((long)_rules.Length * 24L));
    }

    /// <summary>Gets the exact immutable policy identity.</summary>
    public NavigationAreaPolicyKey Key { get; }

    /// <summary>Gets the number of configured direct-indexed area rules.</summary>
    public int RuleCount => _rules.Length;

    /// <summary>Attempts to resolve an area rule without allocation or lookup hashing.</summary>
    public bool TryGetRule(NavigationAreaId area, out NavigationAreaRule rule)
    {
        int index = area.Value;
        if (index < _rules.Length)
        {
            rule = _rules[index];
            return true;
        }

        rule = default;
        return false;
    }

    internal ReadOnlySpan<NavigationAreaRule> RuleSpan => _rules;

    internal long RetainedBytes => _retainedBytes;

    internal bool ContentEquals(NavigationAreaPolicy other)
    {
        if (ReferenceEquals(this, other))
            return true;
        if (!Key.Equals(other.Key) || _rules.Length != other._rules.Length)
            return false;
        for (int i = 0; i < _rules.Length; i++)
        {
            if (!_rules[i].Equals(other._rules[i]))
                return false;
        }
        return true;
    }
}

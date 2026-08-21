//=======================================================================
// NavigationTransitionRuleTable.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using System.Runtime.CompilerServices;

namespace Trailblazer.Pathing;

/// <summary>Stores one exact-size, immutable, globally ID-sorted rule array.</summary>
internal sealed class NavigationTransitionRuleTable
{
    internal const long BaseRetainedBytes = 40L;
    internal static readonly long RecordRetainedBytes = Unsafe.SizeOf<TraversalTransitionRule>();
    private const long ArrayHeaderBytes = 24L;

    internal static readonly NavigationTransitionRuleTable Empty = new(
        Array.Empty<TraversalTransitionRule>(),
        version: 0);

    private readonly TraversalTransitionRule[] _rules;

    internal NavigationTransitionRuleTable(
        TraversalTransitionRule[] rules,
        long version)
    {
        _rules = rules;
        Version = version;
    }

    internal int Count => _rules.Length;

    internal long Version { get; }

    internal TraversalTransitionRule this[int index] => _rules[index];

    internal long RetainedBytes => checked(
        BaseRetainedBytes
        + (_rules.Length == 0
            ? 0L
            : ArrayHeaderBytes + ((long)_rules.Length * RecordRetainedBytes)));

    internal int PersistentPageCount => _rules.Length == 0 ? 1 : 2;

}

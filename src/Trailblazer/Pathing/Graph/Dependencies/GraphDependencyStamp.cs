//=======================================================================
// GraphDependencyStamp.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

namespace Trailblazer.Pathing;

/// <summary>Captures exact immutable component and page dependencies without a global graph version.</summary>
internal sealed class GraphDependencyStamp
{
    private const long ObjectHeaderBytes = 16L;
    private const long ArrayHeaderBytes = 24L;
    private const long ReferenceSlotBytes = 8L;
    private const long Int64Bytes = 8L;
    private const long NavigationAreaPolicyKeyBytes = ReferenceSlotBytes + Int64Bytes;
    private const long GraphComponentDependencyBytes = 40L;
    private const long GraphPageDependencyBytes = 56L;
    private static readonly long BaseRetainedBytes = Align8(
        ObjectHeaderBytes
        + NavigationAreaPolicyKeyBytes
        + (2L * ReferenceSlotBytes)
        + Int64Bytes
        + Int64Bytes);

    internal GraphDependencyStamp(
        NavigationAreaPolicyKey areaPolicy,
        GraphComponentDependency[] components,
        GraphPageDependency[] pages,
        bool hasTransitionRuleDependency = false,
        long transitionRuleVersion = 0)
    {
        AreaPolicy = areaPolicy;
        Components = components;
        Pages = pages;
        HasTransitionRuleDependency = hasTransitionRuleDependency;
        TransitionRuleVersion = hasTransitionRuleDependency ? transitionRuleVersion : 0;
    }

    internal NavigationAreaPolicyKey AreaPolicy { get; }

    internal GraphComponentDependency[] Components { get; }

    internal GraphPageDependency[] Pages { get; }

    internal bool HasTransitionRuleDependency { get; }

    internal long TransitionRuleVersion { get; }

    internal long RetainedBytes => checked(
        BaseRetainedBytes
        + GetArrayRetainedBytes(Components, GraphComponentDependencyBytes)
        + GetArrayRetainedBytes(Pages, GraphPageDependencyBytes));

    internal static long GetRetainedBytes(int componentCount, int pageCount)
    {
        ChronicleGuard.ThrowIfNegative(componentCount, nameof(componentCount));
        ChronicleGuard.ThrowIfNegative(pageCount, nameof(pageCount));
        return checked(
            BaseRetainedBytes
            + GetArrayRetainedBytes(componentCount, GraphComponentDependencyBytes)
            + GetArrayRetainedBytes(pageCount, GraphPageDependencyBytes));
    }

    private static long GetArrayRetainedBytes<T>(T[] values, long elementBytes) =>
        values.Length == 0
            ? 0L
            : Align8(checked(ArrayHeaderBytes + ((long)values.Length * elementBytes)));

    private static long GetArrayRetainedBytes(int length, long elementBytes) =>
        length == 0
            ? 0L
            : Align8(checked(ArrayHeaderBytes + ((long)length * elementBytes)));

    private static long Align8(long value) => checked((value + 7L) & ~7L);
}

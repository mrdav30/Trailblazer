//=======================================================================
// NavigationFlowFieldPayload.cs
//=======================================================================
// MIT License, Copyright (c) 2024-present David Oravsky (mrdav30)
// See LICENSE file in the project root for full license information.
//=======================================================================

using System;
using FixedMathSharp;

namespace Trailblazer.Pathing;

/// <summary>Stores one immutable stable-address flow-field prefix.</summary>
internal sealed class NavigationFlowFieldPayload
{
    private const long ObjectHeaderBytes = 16L;
    private const long ArrayHeaderBytes = 24L;
    private const long ReferenceSlotBytes = 8L;
    private const long NavigationFlowFieldPayloadKeyBytes = 216L;
    private const long NavigationFlowFieldNodeBytes = 64L;
    private const long Int32Bytes = 4L;
    private const long BooleanBytes = 1L;
    private static readonly long BaseRetainedBytes = Align8(
        ObjectHeaderBytes
        + NavigationFlowFieldPayloadKeyBytes
        + (3L * ReferenceSlotBytes)
        + BooleanBytes);

    internal NavigationFlowFieldPayload(
        NavigationFlowFieldPayloadKey key,
        NavigationFlowFieldNode[] nodes,
        int[] addressLookupOrdinals,
        GraphDependencyStamp dependencies,
        bool isComplete)
    {
        SwiftThrowHelper.ThrowIfNull(nodes, nameof(nodes));
        SwiftThrowHelper.ThrowIfNull(addressLookupOrdinals, nameof(addressLookupOrdinals));
        SwiftThrowHelper.ThrowIfNull(dependencies, nameof(dependencies));
        if (nodes.Length == 0 || addressLookupOrdinals.Length != nodes.Length)
            throw new ArgumentException("Flow payload arrays must be non-empty and aligned.");
        Key = key;
        Nodes = nodes;
        AddressLookupOrdinals = addressLookupOrdinals;
        Dependencies = dependencies;
        IsComplete = isComplete;
    }

    internal NavigationFlowFieldPayloadKey Key { get; }
    internal NavigationFlowFieldNode[] Nodes { get; }
    internal int[] AddressLookupOrdinals { get; }
    internal GraphDependencyStamp Dependencies { get; }
    internal bool IsComplete { get; }
    internal Fixed64 LastSettledCost => Nodes[Nodes.Length - 1].IntegrationCost;
    internal NavigationCellAddress LastSettledAddress => Nodes[Nodes.Length - 1].Address;

    internal long RetainedBytes => GetRetainedBytes(Nodes.Length, Dependencies);

    internal bool TryGetNode(
        NavigationCellAddress address,
        out NavigationFlowFieldNode node)
    {
        int low = 0;
        int high = AddressLookupOrdinals.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            NavigationFlowFieldNode candidate = Nodes[AddressLookupOrdinals[middle]];
            int comparison = candidate.Address.CompareTo(address);
            if (comparison == 0)
            {
                node = candidate;
                return true;
            }
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        node = default;
        return false;
    }

    internal static long GetRetainedBytes(
        int nodeCount,
        GraphDependencyStamp dependencies)
    {
        SwiftThrowHelper.ThrowIfNegative(nodeCount, nameof(nodeCount));
        SwiftThrowHelper.ThrowIfNull(dependencies, nameof(dependencies));
        return checked(
            BaseRetainedBytes
            + GetArrayRetainedBytes(nodeCount, NavigationFlowFieldNodeBytes)
            + GetArrayRetainedBytes(nodeCount, Int32Bytes)
            + dependencies.RetainedBytes);
    }

    internal static long GetMaximumRetainedBytes(
        int nodeCount,
        int componentCount,
        int pageCount)
    {
        SwiftThrowHelper.ThrowIfNegative(nodeCount, nameof(nodeCount));
        SwiftThrowHelper.ThrowIfNegative(componentCount, nameof(componentCount));
        SwiftThrowHelper.ThrowIfNegative(pageCount, nameof(pageCount));
        return checked(
            BaseRetainedBytes
            + GetArrayRetainedBytes(nodeCount, NavigationFlowFieldNodeBytes)
            + GetArrayRetainedBytes(nodeCount, Int32Bytes)
            + GraphDependencyStamp.GetRetainedBytes(componentCount, pageCount));
    }

    private static long GetArrayRetainedBytes(int length, long elementBytes) =>
        length == 0
            ? 0L
            : Align8(checked(ArrayHeaderBytes + ((long)length * elementBytes)));

    private static long Align8(long value) => checked((value + 7L) & ~7L);
}
